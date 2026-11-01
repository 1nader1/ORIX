import {
  ChangeDetectionStrategy, Component, DestroyRef, ElementRef, afterNextRender, computed, effect, inject, input, signal, viewChild,
} from '@angular/core';
import { DomSanitizer } from '@angular/platform-browser';

/**
 * Shows the generated page inside a sandboxed iframe, scaled down to fit its container.
 * The HTML comes from our own backend; the sandbox (no allow-same-origin) keeps it isolated anyway.
 */
@Component({
  selector: 'app-preview-frame',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:message)': 'onMessage($event)' },
  template: `
    <div class="frame">
      <div class="bar" aria-hidden="true">
        <i></i><i></i><i></i>
        <span class="url">preview.html</span>
      </div>
      <div #viewport class="viewport" [style.aspect-ratio]="width() + ' / ' + height()">
        @for (run of [replayKey()]; track run) {
          <iframe
            #frame
            title="Generated page preview"
            sandbox="allow-scripts"
            [srcdoc]="doc()"
            [style.width.px]="width()"
            [style.height.px]="height()"
            [style.transform]="'scale(' + scale() + ')'"
          ></iframe>
        }
      </div>
    </div>
  `,
  styles: `
    :host {
      display: block;
    }
    .frame {
      overflow: hidden;
      border-radius: var(--radius);
      border: 1px solid var(--line);
      background: #fff;
      box-shadow: var(--shadow);
      animation: popIn 0.7s var(--ease) both;
    }
    .bar {
      display: flex;
      align-items: center;
      gap: 6px;
      height: 38px;
      padding: 0 14px;
      background: #f6f6f2;
      border-bottom: 1px solid var(--line);
    }
    .bar i {
      width: 10px;
      height: 10px;
      border-radius: 50%;
      background: #dcdcd4;
    }
    .url {
      margin: 0 auto;
      padding: 3px 14px;
      border-radius: 999px;
      font-size: 12px;
      color: var(--ink-3);
      background: #fff;
      border: 1px solid var(--line);
    }
    .viewport {
      position: relative;
      width: 100%;
      overflow: hidden;
      background: #fff;
    }
    iframe {
      position: absolute;
      top: 0;
      left: 0;
      border: 0;
      transform-origin: 0 0;
      background: #fff;
    }
  `,
})
export class PreviewFrameComponent {
  readonly html = input.required<string>();
  readonly designWidth = input(1440);
  /** Changing this number reloads the preview, which replays its animations. */
  readonly replayKey = input(0);

  private readonly sanitizer = inject(DomSanitizer);
  private readonly viewport = viewChild.required<ElementRef<HTMLElement>>('viewport');
  private readonly frame = viewChild<ElementRef<HTMLIFrameElement>>('frame');

  /** Real height of the generated page, reported by a tiny script inside the preview. */
  private readonly reported = signal(0);

  protected readonly scale = signal(0.5);
  protected readonly doc = computed(() => this.sanitizer.bypassSecurityTrustHtml(this.html()));
  protected readonly width = computed(() => Math.min(Math.max(Math.round(this.designWidth()) || 1440, 320), 2560));
  protected readonly height = computed(() => {
    const base = this.width() < 700 ? 844 : Math.round(this.width() * 0.625);
    return Math.max(base, Math.min(this.reported(), 8000));
  });

  protected onMessage(event: MessageEvent): void {
    if (event.source !== this.frame()?.nativeElement.contentWindow) return;
    const data = event.data as { type?: string; height?: number } | null;
    if (data?.type === 'design-preview-height' && Number.isFinite(data.height)) {
      this.reported.set(Math.round(data.height as number));
    }
  }

  constructor() {
    const destroyRef = inject(DestroyRef);
    effect(() => {
      this.html();
      this.reported.set(0);
    });
    afterNextRender(() => {
      const el = this.viewport().nativeElement;
      const observer = new ResizeObserver(() => this.scale.set(el.clientWidth / this.width()));
      observer.observe(el);
      destroyRef.onDestroy(() => observer.disconnect());
    });
  }
}
