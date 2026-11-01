import { CroppedAsset, GeneratedAsset } from './models';

const MAX_OUTPUT_WIDTH = 1600;

/**
 * Crops every requested picture out of the original design, in the browser.
 * Boxes are [yMin, xMin, yMax, xMax] normalized to 0-1000 over the full image.
 */
export async function cropAssets(source: File, assets: GeneratedAsset[]): Promise<CroppedAsset[]> {
  if (assets.length === 0) return [];

  const bitmap = await createImageBitmap(source);
  const out: CroppedAsset[] = [];
  try {
    for (const asset of assets) {
      const [yMin, xMin, yMax, xMax] = asset.box.map((v) => Math.min(1000, Math.max(0, v)));
      const sx = Math.round((xMin / 1000) * bitmap.width);
      const sy = Math.round((yMin / 1000) * bitmap.height);
      const sw = Math.max(1, Math.round(((xMax - xMin) / 1000) * bitmap.width));
      const sh = Math.max(1, Math.round(((yMax - yMin) / 1000) * bitmap.height));

      const scale = Math.min(1, MAX_OUTPUT_WIDTH / sw);
      const canvas = document.createElement('canvas');
      canvas.width = Math.max(1, Math.round(sw * scale));
      canvas.height = Math.max(1, Math.round(sh * scale));
      canvas.getContext('2d')!.drawImage(bitmap, sx, sy, sw, sh, 0, 0, canvas.width, canvas.height);

      const dataUrl = canvas.toDataURL('image/png');
      out.push({ fileName: asset.fileName, dataUrl, base64: dataUrl.split(',')[1], width: canvas.width, height: canvas.height });
    }
  } finally {
    bitmap.close();
  }
  return out;
}

/** Swaps the relative picture paths in the preview HTML for the cropped data URLs. */
export function inlineAssets(html: string, assets: CroppedAsset[]): string {
  let result = html;
  for (const a of assets) result = result.split(`assets/images/${a.fileName}`).join(a.dataUrl);
  return result;
}

const MAX_REPLACEMENT_WIDTH = 2000;
const MAX_REPLACEMENT_BYTES = 8 * 1024 * 1024; // the server accepts up to 10 MB per picture

/** Turns a picture chosen by the developer into a PNG asset, shrinking it only if it is too heavy. */
export async function encodeReplacement(file: File, fileName: string): Promise<CroppedAsset> {
  const bitmap = await createImageBitmap(file);
  try {
    let width = Math.min(bitmap.width, MAX_REPLACEMENT_WIDTH);
    for (let attempt = 0; attempt < 5; attempt++) {
      const height = Math.max(1, Math.round((bitmap.height / bitmap.width) * width));
      const canvas = document.createElement('canvas');
      canvas.width = width;
      canvas.height = height;
      canvas.getContext('2d')!.drawImage(bitmap, 0, 0, width, height);
      const dataUrl = canvas.toDataURL('image/png');
      const base64 = dataUrl.split(',')[1];
      if (base64.length * 0.75 <= MAX_REPLACEMENT_BYTES || attempt === 4) {
        return { fileName, dataUrl, base64, width, height, replaced: true };
      }
      width = Math.round(width * 0.75);
    }
    throw new Error('unreachable');
  } finally {
    bitmap.close();
  }
}
