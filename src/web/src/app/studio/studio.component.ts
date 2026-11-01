import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_URL } from '../core/api.config';
import { DesignApi } from '../core/design-api.service';
import { measureBackgrounds } from '../core/background-measure';
import { cropAssets, inlineAssets } from '../core/image-crop';
import { extractPalette } from '../core/palette';
import { CroppedAsset, DesignDocument, GeneratedProject, LayoutMode, MotionLevel, PaletteColor } from '../core/models';
import { mergeBands, splitIntoBands } from '../core/tiling';
import { CodeViewerComponent } from '../ui/code-viewer.component';
import { ImageManagerComponent } from '../ui/image-manager.component';
import { DropzoneComponent } from '../ui/dropzone.component';
import { PreviewFrameComponent } from '../ui/preview-frame.component';
import { ProgressStepsComponent } from '../ui/progress-steps.component';
import { TokenPaletteComponent } from '../ui/token-palette.component';

type Phase = 'idle' | 'working' | 'done' | 'error';
type Tab = 'preview' | 'code' | 'images';

const sleep = (ms: number) => new Promise<void>((r) => setTimeout(r, ms));

@Component({
  selector: 'app-root',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [DropzoneComponent, ProgressStepsComponent, PreviewFrameComponent, CodeViewerComponent, ImageManagerComponent, TokenPaletteComponent],
  templateUrl: './studio.component.html',
  styleUrl: './studio.component.scss',
})
export class StudioComponent {
  private readonly api = inject(DesignApi);

  protected readonly analyzeLabel = signal('Analyzing the design with Vision AI');
  protected readonly steps = computed(() => [this.analyzeLabel(), 'Generating Angular components', 'Preparing your preview']);

  protected readonly motionLevels: { id: MotionLevel; label: string }[] = [
    { id: 'none', label: 'Off' },
    { id: 'subtle', label: 'Subtle' },
    { id: 'expressive', label: 'Expressive' },
  ];
  protected readonly motion = signal<MotionLevel>('subtle');
  protected readonly motionIndex = computed(() => this.motionLevels.findIndex((m) => m.id === this.motion()));
  protected readonly layoutOptions: { id: LayoutMode; label: string }[] = [
    { id: 'measured', label: 'Measured' },
    { id: 'model', label: 'AI only' },
  ];
  protected readonly layout = signal<LayoutMode>('measured');
  protected readonly layoutIndex = computed(() => this.layoutOptions.findIndex((o) => o.id === this.layout()));
  protected readonly jsonCopied = signal(false);
  protected readonly regenerating = signal(false);
  protected readonly replayKey = signal(0);

  protected readonly phase = signal<Phase>('idle');
  protected readonly step = signal(0);
  protected readonly error = signal('');
  protected readonly file = signal<File | null>(null);
  protected readonly imageUrl = signal<string | null>(null);
  protected readonly design = signal<DesignDocument | null>(null);
  protected readonly project = signal<GeneratedProject | null>(null);
  protected readonly tab = signal<Tab>('preview');

  /** The Images tab only exists when the design has pictures that were cropped out of it. */
  protected readonly tabs = computed<{ id: Tab; label: string }[]>(() => {
    const list: { id: Tab; label: string }[] = [
      { id: 'preview', label: 'Preview' },
      { id: 'code', label: 'Code' },
    ];
    if ((this.project()?.assets?.length ?? 0) > 0) list.push({ id: 'images', label: 'Images' });
    return list;
  });
  protected readonly tabIndex = computed(() => Math.max(0, this.tabs().findIndex((t) => t.id === this.tab())));
  protected readonly downloading = signal(false);
  protected readonly devJson = signal('');
  protected readonly cropped = signal<CroppedAsset[]>([]);

  /** The preview HTML with the pictures cropped from the original already inlined. */
  protected readonly previewHtml = computed(() => {
    const p = this.project();
    return p ? inlineAssets(p.previewHtml, this.cropped()) : '';
  });

  protected readonly designWidth = computed(() => this.design()?.meta?.designWidth || 1440);
  protected readonly metaChips = computed(() => {
    const m = this.design()?.meta;
    if (!m) return [];
    const chips = [m.pageType, m.direction.toUpperCase(), m.language.toUpperCase()];
    if (m.confidence != null) chips.push(`${Math.round(m.confidence * 100)}% confidence`);
    const images = this.project()?.assets?.length ?? 0;
    if (images > 0) chips.push(`${images} ${images === 1 ? 'image' : 'images'} extracted`);
    return chips;
  });

  protected onFile(file: File): void {
    this.revokeImage();
    this.file.set(file);
    this.imageUrl.set(URL.createObjectURL(file));
    this.error.set('');
    this.phase.set('idle');
  }

  protected async generate(): Promise<void> {
    const file = this.file();
    if (!file) return;

    this.begin();
    try {
      const palette = await this.measureColors(file);
      const analyzed = await this.analyzeDesign(file, palette);
      const design = await this.refineBackgrounds(file, analyzed);
      this.step.set(1);
      const project = await firstValueFrom(this.api.generate(design, this.motion(), this.layout()));
      await this.finish(design, project, await this.cropPictures(file, project));
    } catch (e) {
      this.fail(e);
    }
  }

  /** One request for a normal page. A very long page is cut into sections and each one is analyzed on its own. */
  private async analyzeDesign(file: File, palette: PaletteColor[]): Promise<DesignDocument> {
    const tiling = await this.splitLongPage(file);
    if (!tiling) {
      this.analyzeLabel.set('Analyzing the design with Vision AI');
      const design = await firstValueFrom(this.api.analyze(file, palette));
      const size = await imageSize(file);
      design.meta.imageWidth = size.width;
      design.meta.imageHeight = size.height;
      return design;
    }

    const designs: DesignDocument[] = [];
    for (const [i, band] of tiling.bands.entries()) {
      this.analyzeLabel.set(`Analyzing section ${i + 1} of ${tiling.bands.length} with Vision AI`);
      designs.push(await firstValueFrom(this.api.analyze(band.file, palette, i + 1, tiling.bands.length)));
    }
    return mergeBands(designs, tiling);
  }

  private async splitLongPage(file: File) {
    try {
      return await splitIntoBands(file);
    } catch (e) {
      console.warn('Could not split the page into sections', e);
      return null;
    }
  }

  /** Reads real backgrounds and gradients from the pixels. A failure keeps what the AI said. */
  private async refineBackgrounds(file: File, design: DesignDocument): Promise<DesignDocument> {
    try {
      return await measureBackgrounds(file, design);
    } catch (e) {
      console.warn('Could not measure the backgrounds', e);
      return design;
    }
  }

  protected async generateFromJson(): Promise<void> {
    let design: DesignDocument;
    try {
      design = JSON.parse(this.devJson()) as DesignDocument;
    } catch {
      this.error.set('That is not valid JSON. Paste the full response of /api/design/analyze.');
      return;
    }

    this.revokeImage();
    this.file.set(null);
    this.imageUrl.set(null);
    this.begin();
    this.step.set(1);
    try {
      const project = await firstValueFrom(this.api.generate(design, this.motion(), this.layout()));
      await this.finish(design, project, []);
    } catch (e) {
      this.fail(e);
    }
  }

  protected async download(): Promise<void> {
    const design = this.design();
    const project = this.project();
    if (!design || !project) return;

    this.downloading.set(true);
    try {
      const blob = await firstValueFrom(this.api.download(design, this.cropped(), this.motion(), this.layout()));
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `${project.name}-angular.zip`;
      a.click();
      URL.revokeObjectURL(url);
    } catch (e) {
      this.fail(e, false);
    } finally {
      this.downloading.set(false);
    }
  }

  /** Switches the animation level and rebuilds the page. No AI call is involved, so it is instant. */
  protected async setMotion(level: MotionLevel): Promise<void> {
    if (level === this.motion()) return;
    this.motion.set(level);
    await this.regenerate();
  }

  /** Switches between spacing measured from the element boxes and spacing written only by the AI. */
  protected async setLayout(mode: LayoutMode): Promise<void> {
    if (mode === this.layout()) return;
    this.layout.set(mode);
    await this.regenerate();
  }

  private async regenerate(): Promise<void> {
    const design = this.design();
    if (!design || !this.project()) return;

    this.regenerating.set(true);
    try {
      this.project.set(await firstValueFrom(this.api.generate(design, this.motion(), this.layout())));
      this.replayKey.update((k) => k + 1);
    } catch (e) {
      this.fail(e, false);
    } finally {
      this.regenerating.set(false);
    }
  }

  /** Copies the analyzed design as JSON, handy for reporting a problem or testing without the AI. */
  protected async copyJson(): Promise<void> {
    const design = this.design();
    if (!design) return;
    try {
      await navigator.clipboard.writeText(JSON.stringify(design, null, 2));
      this.jsonCopied.set(true);
      setTimeout(() => this.jsonCopied.set(false), 1500);
    } catch {
      /* clipboard may be blocked */
    }
  }

  protected replay(): void {
    this.replayKey.update((k) => k + 1);
  }

  /** A picture replaced by the developer takes the place of the crop (or adds it when there was none). */
  protected onReplaced(asset: CroppedAsset): void {
    this.cropped.update((list) =>
      list.some((a) => a.fileName === asset.fileName)
        ? list.map((a) => (a.fileName === asset.fileName ? asset : a))
        : [...list, asset],
    );
  }

  protected reset(): void {
    this.revokeImage();
    this.file.set(null);
    this.imageUrl.set(null);
    this.design.set(null);
    this.project.set(null);
    this.cropped.set([]);
    this.error.set('');
    this.phase.set('idle');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  protected size(bytes: number): string {
    return bytes < 1024 * 1024 ? `${Math.max(1, Math.round(bytes / 1024))} KB` : `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  private begin(): void {
    this.error.set('');
    this.step.set(0);
    this.phase.set('working');
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  /** Measures the real colors of the image so the AI does not have to guess them. Never blocks the flow. */
  private async measureColors(file: File): Promise<PaletteColor[]> {
    try {
      return await extractPalette(file);
    } catch (e) {
      console.warn('Could not measure the colors of the design', e);
      return [];
    }
  }

  /** Crops the pictures out of the original design. A failure here must never block the result. */
  private async cropPictures(file: File, project: GeneratedProject): Promise<CroppedAsset[]> {
    try {
      return await cropAssets(file, project.assets ?? []);
    } catch (e) {
      console.warn('Could not crop the pictures from the design', e);
      return [];
    }
  }

  private async finish(design: DesignDocument, project: GeneratedProject, cropped: CroppedAsset[]): Promise<void> {
    this.step.set(2);
    await sleep(600);
    this.design.set(design);
    this.cropped.set(cropped);
    this.project.set(project);
    this.tab.set('preview');
    this.phase.set('done');
  }

  private fail(e: unknown, backToStart = true): void {
    let message = 'Something went wrong. Please try again.';
    if (e instanceof HttpErrorResponse) {
      if (e.status === 0) {
        message = `Can't reach the API at ${API_URL}. Make sure the backend is running and the port matches.`;
      } else if (e.status === 400) {
        message = typeof e.error === 'string' && e.error ? e.error : 'The server rejected this request.';
      } else {
        message = 'The AI service failed or timed out. Try a simpler image, or try again in a moment.';
      }
    }
    this.error.set(message);
    if (backToStart) this.phase.set('error');
  }

  private revokeImage(): void {
    const url = this.imageUrl();
    if (url) URL.revokeObjectURL(url);
  }
}

async function imageSize(file: File): Promise<{ width: number; height: number }> {
  const bitmap = await createImageBitmap(file);
  const size = { width: bitmap.width, height: bitmap.height };
  bitmap.close();
  return size;
}
