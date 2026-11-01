export interface DesignTokens {
  colors: Record<string, string>;
  typography?: {
    fontFamilyHeading?: string | null;
    fontFamilyBody?: string | null;
    scale?: Record<string, number> | null;
  } | null;
  radius?: Record<string, number> | null;
  shadows?: Record<string, string> | null;
  spacing?: { unit?: number } | null;
}

export interface DesignMeta {
  pageType: string;
  direction: string;
  language: string;
  designWidth?: number | null;
  hasMockupFrame?: boolean | null;
  confidence?: number | null;
  /** Pixel size of the uploaded image. Used to measure real distances from the element boxes. */
  imageWidth?: number | null;
  imageHeight?: number | null;
}

/** One element of the design. The fields the browser touches are typed; the rest passes through untouched. */
export interface DesignNodeJson {
  id: string;
  type: string;
  role?: string | null;
  /** [yMin, xMin, yMax, xMax], each 0-1000 over the full image. */
  box?: number[] | null;
  layout?: { rotate?: number | null; height?: string | null; [key: string]: unknown } | null;
  style?: { background?: string | null; [key: string]: unknown } | null;
  content?: { imageBox?: number[] | null; [key: string]: unknown } | null;
  children?: DesignNodeJson[] | null;
  [key: string]: unknown;
}

export interface DesignDocument {
  meta: DesignMeta;
  tokens: DesignTokens;
  root: DesignNodeJson;
}

/** How much the generated page should move. */
export type MotionLevel = 'none' | 'subtle' | 'expressive';

/** Where spacing and alignment come from: measured from the element boxes, or only what the AI wrote. */
export type LayoutMode = 'measured' | 'model';

export interface GeneratedFile {
  path: string;
  content: string;
}

/** A picture to crop from the original design. box = [yMin, xMin, yMax, xMax], each 0-1000. */
export interface GeneratedAsset {
  fileName: string;
  box: number[];
  description?: string | null;
}

export interface GeneratedProject {
  name: string;
  previewHtml: string;
  files: GeneratedFile[];
  assets: GeneratedAsset[];
}

/** A cropped picture, ready to preview (dataUrl) and to send to the server (base64). */
export interface CroppedAsset {
  fileName: string;
  dataUrl: string;
  base64: string;
  width: number;
  height: number;
  /** True when the developer replaced the crop with their own picture. */
  replaced?: boolean;
}

/** A color measured from the real pixels of the uploaded design (share = fraction of all pixels). */
export interface PaletteColor {
  hex: string;
  share: number;
}
