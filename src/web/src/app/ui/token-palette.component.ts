import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { DesignTokens } from '../core/models';

@Component({
  selector: 'app-token-palette',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="palette">
      <header>
        <h3>Extracted design tokens</h3>
        <p>Click any color to copy it. These become CSS variables in the generated code.</p>
      </header>

      <div class="swatches">
        @for (c of colors(); track c[0]) {
          <button type="button" class="sw" (click)="copy(c[1])" [attr.aria-label]="'Copy ' + c[1]">
            <span class="chip" [style.background]="c[1]"></span>
            <span class="name">{{ c[0] }}</span>
            <span class="hex">{{ copied() === c[1] ? 'Copied ✓' : c[1] }}</span>
          </button>
        }
      </div>

      <div class="meta">
        @if (radius().length) {
          <div class="group">
            <h4>Radius</h4>
            <div class="radii">
              @for (r of radius(); track r[0]) {
                <span class="rad">
                  <i [style.border-radius.px]="min(r[1], 14)"></i>
                  {{ r[0] }} <b>{{ r[1] > 999 ? 'full' : r[1] + 'px' }}</b>
                </span>
              }
            </div>
          </div>
        }
        @if (fonts().length) {
          <div class="group">
            <h4>Typography</h4>
            <div class="radii">
              @for (f of fonts(); track f) {
                <span class="rad"><b>{{ f }}</b></span>
              }
            </div>
          </div>
        }
        @if (scale().length) {
          <div class="group">
            <h4>Type scale</h4>
            <div class="radii">
              @for (s of scale(); track s[0]) {
                <span class="rad">{{ s[0] }} <b>{{ s[1] }}px</b></span>
              }
            </div>
          </div>
        }
      </div>
    </section>
  `,
  styles: `
    .palette {
      margin-top: 32px;
      padding: 28px;
      border-radius: var(--radius-lg);
      border: 1px solid var(--line);
      background: var(--surface);
      box-shadow: var(--shadow);
    }
    h3 {
      font-size: 18px;
      font-weight: 600;
      letter-spacing: -0.01em;
    }
    header p {
      margin-top: 4px;
      font-size: 14px;
      color: var(--ink-3);
    }
    .swatches {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(132px, 1fr));
      gap: 12px;
      margin-top: 20px;
    }
    .sw {
      display: grid;
      gap: 2px;
      padding: 8px 8px 10px;
      text-align: left;
      font-family: inherit;
      color: inherit;
      border: 1px solid var(--line);
      border-radius: 14px;
      background: #fff;
      cursor: pointer;
      transition: transform 0.3s var(--ease), box-shadow 0.3s var(--ease), border-color 0.2s;
    }
    .sw:hover {
      transform: translateY(-3px);
      border-color: #d4d4cc;
      box-shadow: 0 14px 24px -14px rgba(16, 24, 40, 0.28);
    }
    .sw:focus-visible {
      outline: 2px solid var(--accent);
      outline-offset: 2px;
    }
    .chip {
      height: 54px;
      margin-bottom: 8px;
      border-radius: 9px;
      box-shadow: inset 0 0 0 1px rgba(0, 0, 0, 0.07);
    }
    .name {
      padding: 0 4px;
      font-size: 13px;
      font-weight: 600;
    }
    .hex {
      padding: 0 4px;
      font: 400 12px/1.2 var(--font-mono);
      color: var(--ink-3);
      text-transform: uppercase;
    }
    .meta {
      display: flex;
      flex-wrap: wrap;
      gap: 28px 44px;
      margin-top: 26px;
      padding-top: 22px;
      border-top: 1px solid var(--line-2);
    }
    h4 {
      margin: 0 0 10px;
      font-size: 12px;
      font-weight: 600;
      letter-spacing: 0.1em;
      text-transform: uppercase;
      color: var(--ink-3);
    }
    .radii {
      display: flex;
      flex-wrap: wrap;
      gap: 8px;
    }
    .rad {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 6px 12px 6px 8px;
      border-radius: 999px;
      font-size: 13px;
      color: var(--ink-2);
      background: var(--line-2);
    }
    .rad b {
      font-weight: 600;
      color: var(--ink);
    }
    .rad i {
      width: 20px;
      height: 14px;
      border: 1.5px solid var(--accent);
      border-right: 0;
      border-bottom: 0;
      background: var(--accent-soft);
    }
  `,
})
export class TokenPaletteComponent {
  readonly tokens = input.required<DesignTokens>();

  protected readonly copied = signal('');
  protected readonly colors = computed(() => Object.entries(this.tokens().colors ?? {}));
  protected readonly radius = computed(() => Object.entries(this.tokens().radius ?? {}));
  protected readonly scale = computed(() => Object.entries(this.tokens().typography?.scale ?? {}));
  protected readonly fonts = computed(() => {
    const t = this.tokens().typography;
    const list = [t?.fontFamilyHeading, t?.fontFamilyBody].filter((f): f is string => !!f);
    return [...new Set(list)];
  });

  protected min(a: number, b: number): number {
    return Math.min(a, b);
  }

  protected async copy(hex: string): Promise<void> {
    try {
      await navigator.clipboard.writeText(hex);
      this.copied.set(hex);
      setTimeout(() => this.copied.set(''), 1200);
    } catch {
      /* ignore */
    }
  }
}
