import { deltaE, labOf, parseHex } from './color';
import { DesignDocument, DesignNodeJson } from './models';

export interface Band {
  file: File;
  top: number;
  height: number;
}

export interface Tiling {
  width: number;
  height: number;
  bands: Band[];
}

const MIN_RATIO = 2.4; // only pages at least this much taller than wide are split
const MAX_BANDS = 6;

/**
 * Very long pages lose detail when sent to the AI as one image. They are cut into sections at "seams":
 * rows of the page that are one flat color, where no element can be cut in half.
 * Returns null when the page is short or no clean seam exists.
 */
export async function splitIntoBands(file: File): Promise<Tiling | null> {
  const bitmap = await createImageBitmap(file);
  const { width: W, height: H } = bitmap;
  if (H / W < MIN_RATIO) {
    bitmap.close();
    return null;
  }

  const seams = findSeams(bitmap, W, H);
  const cuts = chooseCuts(seams, W, H);
  if (cuts.length === 0) {
    bitmap.close();
    return null;
  }

  const edges = [0, ...cuts, H];
  const bands: Band[] = [];
  for (let i = 0; i < edges.length - 1; i++) {
    const top = edges[i];
    const height = edges[i + 1] - top;
    bands.push({ file: await cropBand(bitmap, W, top, height, i), top, height });
  }
  bitmap.close();
  return { width: W, height: H, bands };
}

/** Middle (in image pixels) of every run of flat-colored rows that is tall enough to cut through. */
function findSeams(bitmap: ImageBitmap, W: number, H: number): number[] {
  const scale = 200 / W;
  const w = 200;
  const h = Math.max(1, Math.round(H * scale));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
  ctx.drawImage(bitmap, 0, 0, w, h);
  const data = ctx.getImageData(0, 0, w, h).data;

  const flat: boolean[] = [];
  for (let y = 0; y < h; y++) {
    let sum = 0;
    let sumSq = 0;
    for (let x = 0; x < w; x++) {
      const i = (y * w + x) * 4;
      const lum = 0.299 * data[i] + 0.587 * data[i + 1] + 0.114 * data[i + 2];
      sum += lum;
      sumSq += lum * lum;
    }
    const mean = sum / w;
    flat.push(Math.sqrt(Math.max(0, sumSq / w - mean * mean)) < 2.5);
  }

  const seams: number[] = [];
  let start = -1;
  for (let y = 0; y <= h; y++) {
    if (y < h && flat[y]) {
      if (start < 0) start = y;
    } else if (start >= 0) {
      if ((y - start) / scale >= 8) seams.push(Math.round(((start + y) / 2 / scale)));
      start = -1;
    }
  }
  return seams;
}

function chooseCuts(seams: number[], W: number, H: number): number[] {
  for (const factor of [1.3, 1.7, 2.2]) {
    const target = factor * W;
    const min = 0.7 * W;
    const max = Math.max(2.2 * W, target * 1.4);
    const cuts: number[] = [];
    let y0 = 0;
    while (H - y0 > target * 1.4) {
      // never leave a sliver at the bottom, and only cut inside the allowed height range
      const options = seams.filter((c) => c >= y0 + min && c <= y0 + max && H - c >= 0.4 * W);
      if (options.length === 0) break;
      const best = options.reduce((a, b) => (Math.abs(a - (y0 + target)) <= Math.abs(b - (y0 + target)) ? a : b));
      cuts.push(best);
      y0 = best;
    }
    if (cuts.length > 0 && cuts.length + 1 <= MAX_BANDS) return cuts;
  }
  return [];
}

async function cropBand(bitmap: ImageBitmap, W: number, top: number, height: number, index: number): Promise<File> {
  const canvas = document.createElement('canvas');
  canvas.width = W;
  canvas.height = height;
  canvas.getContext('2d')!.drawImage(bitmap, 0, top, W, height, 0, 0, W, height);

  const toBlob = (type: string, quality?: number) =>
    new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, type, quality));
  let blob = await toBlob('image/png');
  let extension = 'png';
  if (!blob || blob.size > 4.5 * 1024 * 1024) {
    blob = await toBlob('image/jpeg', 0.92);
    extension = 'jpg';
  }
  if (!blob) throw new Error('Could not cut the page into sections');
  return new File([blob], `section-${index + 1}.${extension}`, { type: blob.type });
}

/** Joins the analysis of every section back into one design, with boxes relative to the whole page. */
export function mergeBands(designs: DesignDocument[], tiling: Tiling): DesignDocument {
  const base = structuredClone(designs[0]);
  const usedIds = new Set<string>();
  const roots: DesignNodeJson[] = [];

  designs.forEach((design, index) => {
    const band = tiling.bands[index];
    let tree = structuredClone(design.root);

    // Color tokens: reuse an existing one when it is the same color, otherwise add it under a free name.
    if (index > 0) {
      const renames = mergeColors(base, design);
      if (Object.keys(renames).length > 0) tree = renameColors(tree, renames);
      for (const key of ['radius', 'shadows'] as const) {
        const source = design.tokens[key];
        if (!source) continue;
        const target = (base.tokens[key] ??= {}) as Record<string, unknown>;
        for (const [name, value] of Object.entries(source)) target[name] ??= value;
      }
      base.tokens.typography ??= design.tokens.typography;
    }

    toGlobal(tree, band, tiling.height, usedIds, index);
    if (tree.type === 'container') tree.type = 'section';
    const layout = (tree.layout ??= {});
    layout['width'] ??= '100%';
    if (layout.height === '100%' || layout.height === '100vh') delete layout.height;
    roots.push(tree);
  });

  const confidences = designs.map((d) => d.meta.confidence).filter((c): c is number => typeof c === 'number');
  base.meta = {
    ...base.meta,
    hasMockupFrame: false,
    designWidth: designs.map((d) => d.meta.designWidth).find((w) => !!w) ?? base.meta.designWidth,
    confidence: confidences.length ? Math.min(...confidences) : base.meta.confidence,
    imageWidth: tiling.width,
    imageHeight: tiling.height,
  };
  base.root = {
    id: 'page',
    type: 'container',
    role: 'page',
    box: [0, 0, 1000, 1000],
    layout: { display: 'flex', direction: 'column', width: '100%' },
    children: roots,
  };
  return base;
}

function mergeColors(base: DesignDocument, other: DesignDocument): Record<string, string> {
  const renames: Record<string, string> = {};
  const colors = base.tokens.colors;
  for (const [name, hex] of Object.entries(other.tokens.colors ?? {})) {
    const rgb = parseHex(hex);
    const same = rgb
      ? Object.entries(colors).find(([, h]) => {
          const c = parseHex(h);
          return c && deltaE(labOf(c), labOf(rgb)) < 4;
        })
      : undefined;
    if (same) {
      if (same[0] !== name) renames[name] = same[0];
    } else if (!(name in colors)) {
      colors[name] = hex;
    } else {
      let free = `${name}2`;
      for (let n = 3; free in colors; n++) free = `${name}${n}`;
      colors[free] = hex;
      renames[name] = free;
    }
  }
  return renames;
}

function renameColors(tree: DesignNodeJson, renames: Record<string, string>): DesignNodeJson {
  const json = JSON.stringify(tree).replace(/\$colors\.([A-Za-z0-9_-]+)/g, (m, name: string) =>
    name in renames ? `$colors.${renames[name]}` : m,
  );
  return JSON.parse(json) as DesignNodeJson;
}

/** Boxes of a section are relative to the section image; make them relative to the whole page. */
function toGlobal(node: DesignNodeJson, band: Band, fullHeight: number, usedIds: Set<string>, index: number): void {
  const convert = (box: number[]): number[] => {
    const [y0, x0, y1, x1] = box;
    const y = (v: number) => Math.round(((band.top + (v / 1000) * band.height) / fullHeight) * 1000);
    return [y(y0), x0, y(y1), x1];
  };
  if (node.box?.length === 4) node.box = convert(node.box);
  if (node.content?.imageBox?.length === 4) node.content.imageBox = convert(node.content.imageBox);

  let id = node.id;
  for (let n = 2; usedIds.has(id); n++) id = `${node.id}_${index + 1}_${n}`;
  usedIds.add(id);
  node.id = id;

  for (const child of node.children ?? []) toGlobal(child, band, fullHeight, usedIds, index);
}
