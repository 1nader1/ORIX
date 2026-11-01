import { deltaE, Lab, labOf, parseHex, RGB, toHex } from './color';
import { DesignDocument, DesignNodeJson } from './models';

/** Elements that can have a background of their own. Text, icons and pictures are left alone. */
const MEASURED_TYPES = new Set([
  'container', 'section', 'card', 'navbar', 'footer', 'form', 'list', 'button', 'input', 'textarea', 'select',
]);
const SAMPLES = 12;
const MAX_WIDTH = 1600;

interface Surface { w: number; h: number; data: Uint8ClampedArray }

type Measured =
  | { kind: 'solid'; color: RGB }
  | { kind: 'gradient'; angle: number; stops: { color: RGB; at: number }[] };

/**
 * Reads the real background of every element from the pixels of the design: solid colors and gradients.
 * The vision model often misses gradients and section backgrounds; the pixels do not lie.
 * Only the strips just inside the edges of each element are sampled, where its own background is visible.
 * Returns a new document; the input is left untouched. Any failure keeps the model's values.
 */
export async function measureBackgrounds(file: File, design: DesignDocument): Promise<DesignDocument> {
  const surface = await loadSurface(file);
  const result = structuredClone(design);
  const tokens = Object.entries(result.tokens.colors ?? {})
    .map(([name, hex]) => ({ name, rgb: parseHex(hex) }))
    .filter((t): t is { name: string; rgb: RGB } => t.rgb !== null);

  const walk = (node: DesignNodeJson, isRoot: boolean): void => {
    const rotated = (node.layout?.rotate ?? 0) !== 0;
    if (!rotated && (isRoot || MEASURED_TYPES.has(node.type)) && validBox(node.box ?? (isRoot ? [0, 0, 1000, 1000] : null))) {
      const box = node.box ?? [0, 0, 1000, 1000];
      const measured = measure(surface, box, isRoot, node.type);
      if (measured) apply(node, measured, tokens, result.tokens.colors);
    }
    for (const child of node.children ?? []) walk(child, false);
  };
  walk(result.root, true);
  return result;
}

function validBox(box: number[] | null | undefined): box is number[] {
  return !!box && box.length === 4 && box[2] - box[0] > 5 && box[3] - box[1] > 5;
}

async function loadSurface(file: File): Promise<Surface> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, MAX_WIDTH / bitmap.width);
  const w = Math.max(1, Math.round(bitmap.width * scale));
  const h = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
  ctx.drawImage(bitmap, 0, 0, w, h);
  bitmap.close();
  return { w, h, data: ctx.getImageData(0, 0, w, h).data };
}

const clamp = (v: number, lo: number, hi: number) => Math.min(hi, Math.max(lo, v));
const mid = (vals: number[]) => [...vals].sort((a, b) => a - b)[Math.floor(vals.length / 2)];
const medianColor = (colors: RGB[]): RGB => [mid(colors.map((c) => c[0])), mid(colors.map((c) => c[1])), mid(colors.map((c) => c[2]))];
const meanColor = (a: RGB, b: RGB): RGB => [(a[0] + b[0]) / 2, (a[1] + b[1]) / 2, (a[2] + b[2]) / 2];

/** Median of a 3x3 patch, so one noisy pixel cannot decide. */
function patch(s: Surface, x: number, y: number): RGB {
  const cx = clamp(Math.round(x), 1, s.w - 2);
  const cy = clamp(Math.round(y), 1, s.h - 2);
  const colors: RGB[] = [];
  for (let dy = -1; dy <= 1; dy++) {
    for (let dx = -1; dx <= 1; dx++) {
      const i = ((cy + dy) * s.w + (cx + dx)) * 4;
      colors.push([s.data[i], s.data[i + 1], s.data[i + 2]]);
    }
  }
  return medianColor(colors);
}

/** Removes single outliers (a letter touching the edge) by a median over three neighbours. */
const smooth = (seq: RGB[]): RGB[] => seq.map((c, i) => medianColor([seq[Math.max(0, i - 1)], c, seq[Math.min(seq.length - 1, i + 1)]]));

interface Axis { flat: boolean; smooth: boolean; total: number }

function analyze(seq: RGB[]): Axis {
  const labs = seq.map(labOf);
  const total = deltaE(labs[0], labs[labs.length - 1]);
  const spread = Math.max(...labs.map((l) => deltaE(l, labs[0])));
  const maxStep = Math.max(...labs.slice(1).map((l, i) => deltaE(l, labs[i])));
  const flat = spread < 5;
  return { flat, smooth: !flat && total >= 7 && maxStep <= Math.max(6, total * 0.45), total };
}

function measure(s: Surface, box: number[], isRoot: boolean, type: string): Measured | null {
  const x0 = Math.round((box[1] / 1000) * s.w);
  const x1 = Math.round((box[3] / 1000) * s.w) - 1;
  const y0 = Math.round((box[0] / 1000) * s.h);
  const y1 = Math.round((box[2] / 1000) * s.h) - 1;
  const w = x1 - x0;
  const h = y1 - y0;
  if (w < 24 || h < 24) return null;

  const inset = clamp(Math.round(Math.min(w, h) * 0.03), 2, 8);
  const mx = clamp(Math.round(w * 0.04), 3, 16);
  const my = clamp(Math.round(h * 0.04), 3, 16);
  const at = (a: number, b: number, i: number) => a + ((b - a) * i) / (SAMPLES - 1);
  const strip = (fn: (i: number) => RGB) => smooth(Array.from({ length: SAMPLES }, (_, i) => fn(i)));

  const left = strip((i) => patch(s, x0 + inset, at(y0 + my, y1 - my, i)));
  const right = strip((i) => patch(s, x1 - inset, at(y0 + my, y1 - my, i)));
  const top = strip((i) => patch(s, at(x0 + mx, x1 - mx, i), y0 + inset));
  const bottom = strip((i) => patch(s, at(x0 + mx, x1 - mx, i), y1 - inset));
  const [L, R, T, B] = [analyze(left), analyze(right), analyze(top), analyze(bottom)];

  const consistent = (a: RGB[], b: RGB[]) => a.every((c, i) => deltaE(labOf(c), labOf(b[i])) < 8);
  const average = (a: RGB[], b: RGB[]) => a.map((c, i) => meanColor(c, b[i]));

  let measured: Measured | null = null;
  if (L.flat && R.flat && T.flat && B.flat) {
    const parts = [medianColor(left), medianColor(right), medianColor(top), medianColor(bottom)];
    const color = medianColor(parts);
    if (parts.every((p) => deltaE(labOf(p), labOf(color)) < 6)) measured = { kind: 'solid', color };
  } else if (L.smooth && R.smooth && T.flat && B.flat && consistent(left, right)) {
    measured = { kind: 'gradient', angle: 180, stops: stopsOf(average(left, right)) };
  } else if (T.smooth && B.smooth && L.flat && R.flat && consistent(top, bottom)) {
    measured = { kind: 'gradient', angle: 90, stops: stopsOf(average(top, bottom)) };
  } else if (L.smooth && R.smooth && T.smooth && B.smooth) {
    const c = clamp(Math.round(Math.min(w, h) * 0.1), 3, 24);
    const tl = patch(s, x0 + inset + c, y0 + inset + c);
    const tr = patch(s, x1 - inset - c, y0 + inset + c);
    const bl = patch(s, x0 + inset + c, y1 - inset - c);
    const br = patch(s, x1 - inset - c, y1 - inset - c);
    const main = deltaE(labOf(tl), labOf(br));
    const anti = deltaE(labOf(bl), labOf(tr));
    if (Math.max(main, anti) >= 7) {
      measured = main >= anti
        ? { kind: 'gradient', angle: 135, stops: [{ color: tl, at: 0 }, { color: br, at: 100 }] }
        : { kind: 'gradient', angle: 45, stops: [{ color: bl, at: 0 }, { color: tr, at: 100 }] };
    }
  }

  // Pills and rounded cards pollute the edge strips with the outside color. Their middle line is still clean:
  // the most common color along it is the background (text covers only a minority of the line).
  if (!measured && !isRoot && SOLID_FALLBACK.has(type)) {
    const color = dominantAlongCenter(s, x0, x1, y0, y1);
    if (color) measured = { kind: 'solid', color };
  }
  if (!measured) return null;

  // An element that looks just like its surroundings has no background of its own.
  if (!isRoot && measured.kind === 'solid') {
    const outside = outsideColor(s, x0, x1, y0, y1, inset + 2, mx, my);
    if (outside && deltaE(labOf(measured.color), labOf(outside)) < 4) return null;
  }
  return measured;
}

const SOLID_FALLBACK = new Set(['button', 'input', 'textarea', 'select', 'card']);

function dominantAlongCenter(s: Surface, x0: number, x1: number, y0: number, y1: number): RGB | null {
  const y = Math.round((y0 + y1) / 2);
  const r = clamp(Math.round((y1 - y0) * 0.2), 4, 40);
  const n = 48;
  const clusters: { lab: Lab; members: RGB[] }[] = [];
  for (let i = 0; i < n; i++) {
    const c = patch(s, x0 + r + ((x1 - x0 - 2 * r) * i) / (n - 1), y);
    const lab = labOf(c);
    const hit = clusters.find((k) => deltaE(k.lab, lab) < 6);
    if (hit) hit.members.push(c);
    else clusters.push({ lab, members: [c] });
  }
  const best = clusters.sort((a, b) => b.members.length - a.members.length)[0];
  return best.members.length / n >= 0.5 ? medianColor(best.members) : null;
}

/** Start, end and (when the middle is clearly not a straight blend) one middle stop of a gradient. */
function stopsOf(seq: RGB[]): { color: RGB; at: number }[] {
  const labs = seq.map(labOf);
  const first = labs[0];
  const last = labs[labs.length - 1];
  let worst = 0;
  let worstAt = -1;
  labs.forEach((l, i) => {
    const t = i / (labs.length - 1);
    const expected = [first[0] + (last[0] - first[0]) * t, first[1] + (last[1] - first[1]) * t, first[2] + (last[2] - first[2]) * t];
    const dev = deltaE(l, expected);
    if (i > 0 && i < labs.length - 1 && dev > worst) {
      worst = dev;
      worstAt = i;
    }
  });
  const stops = [{ color: seq[0], at: 0 }];
  if (worst > 6 && worstAt > 0) stops.push({ color: seq[worstAt], at: Math.round((worstAt / (labs.length - 1)) * 100) });
  stops.push({ color: seq[seq.length - 1], at: 100 });
  return stops;
}

/** Color just outside the element, or null when the element sits at the image border. */
function outsideColor(s: Surface, x0: number, x1: number, y0: number, y1: number, gap: number, mx: number, my: number): RGB | null {
  const samples: RGB[] = [];
  for (let i = 0; i < 6; i++) {
    const fx = x0 + mx + ((x1 - x0 - 2 * mx) * i) / 5;
    const fy = y0 + my + ((y1 - y0 - 2 * my) * i) / 5;
    if (y0 - gap >= 1) samples.push(patch(s, fx, y0 - gap));
    if (y1 + gap <= s.h - 2) samples.push(patch(s, fx, y1 + gap));
    if (x0 - gap >= 1) samples.push(patch(s, x0 - gap, fy));
    if (x1 + gap <= s.w - 2) samples.push(patch(s, x1 + gap, fy));
  }
  return samples.length >= 6 ? medianColor(samples) : null;
}

function apply(node: DesignNodeJson, m: Measured, tokens: { name: string; rgb: RGB }[], colors: Record<string, string>): void {
  const declared = node.style?.background ?? undefined;
  const declaredRgb = declared ? resolveColor(declared, colors) : null;

  if (m.kind === 'solid') {
    if (declared?.includes('gradient')) return; // the model saw a gradient we could not measure: trust it
    if (declaredRgb && deltaE(labOf(declaredRgb), labOf(m.color)) < 4) return;
  }

  const value = m.kind === 'solid'
    ? colorRef(m.color, tokens)
    : `linear-gradient(${m.angle}deg, ${m.stops.map((st) => `${colorRef(st.color, tokens)} ${st.at}%`).join(', ')})`;
  node.style = { ...(node.style ?? {}), background: value };
}

function resolveColor(value: string, colors: Record<string, string>): RGB | null {
  if (value.startsWith('$colors.')) return parseHex(colors[value.slice(8)] ?? '');
  return parseHex(value);
}

/** An existing design token when the color matches one, otherwise the plain hex value. */
function colorRef(color: RGB, tokens: { name: string; rgb: RGB }[]): string {
  const lab = labOf(color);
  let best: { name: string; d: number } | null = null;
  for (const t of tokens) {
    const d = deltaE(lab, labOf(t.rgb));
    if (d < 3 && (!best || d < best.d)) best = { name: t.name, d };
  }
  return best ? `$colors.${best.name}` : toHex(color);
}
