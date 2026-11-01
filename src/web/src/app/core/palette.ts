import { deltaE, toHex, toLab } from './color';
import { PaletteColor } from './models';

const SAMPLE_WIDTH = 320;
const MERGE_DISTANCE = 8; // colors closer than this (CIE76) count as the same color
const MIN_SHARE = 0.0015; // ignore colors under 0.15% of the pixels (anti-aliasing noise)

interface Bucket { n: number; r: number; g: number; b: number }

/**
 * Measures the dominant colors of the design from its real pixels (no guessing).
 * Pixels are grouped into 4096 buckets, then similar buckets are merged by perceptual distance,
 * so small but important colors (a green button) survive while anti-aliasing noise is absorbed.
 */
export async function extractPalette(file: File, max = 14): Promise<PaletteColor[]> {
  const bitmap = await createImageBitmap(file);
  const scale = Math.min(1, SAMPLE_WIDTH / bitmap.width);
  const w = Math.max(1, Math.round(bitmap.width * scale));
  const h = Math.max(1, Math.round(bitmap.height * scale));
  const canvas = document.createElement('canvas');
  canvas.width = w;
  canvas.height = h;
  const ctx = canvas.getContext('2d', { willReadFrequently: true })!;
  ctx.drawImage(bitmap, 0, 0, w, h);
  bitmap.close();

  const data = ctx.getImageData(0, 0, w, h).data;
  const buckets = new Map<number, Bucket>();
  let total = 0;
  for (let i = 0; i < data.length; i += 4) {
    if (data[i + 3] < 200) continue;
    const [r, g, b] = [data[i], data[i + 1], data[i + 2]];
    const key = ((r >> 4) << 8) | ((g >> 4) << 4) | (b >> 4);
    const entry = buckets.get(key);
    if (entry) {
      entry.n++;
      entry.r += r;
      entry.g += g;
      entry.b += b;
    } else {
      buckets.set(key, { n: 1, r, g, b });
    }
    total++;
  }
  if (total === 0) return [];

  const sorted = [...buckets.values()].sort((a, b) => b.n - a.n);
  const picked: { r: number; g: number; b: number; lab: number[]; n: number }[] = [];
  for (const bucket of sorted) {
    const [r, g, b] = [bucket.r / bucket.n, bucket.g / bucket.n, bucket.b / bucket.n];
    const lab = toLab(r, g, b);
    const near = picked.find((p) => deltaE(p.lab, lab) < MERGE_DISTANCE);
    if (near) {
      near.n += bucket.n;
    } else if (picked.length < max && bucket.n / total >= MIN_SHARE) {
      picked.push({ r, g, b, lab, n: bucket.n });
    }
  }

  return picked
    .sort((a, b) => b.n - a.n)
    .map((p) => ({ hex: toHex([p.r, p.g, p.b]), share: Math.round((p.n / total) * 10000) / 10000 }));
}
