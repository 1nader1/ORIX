import { ChangeDetectionStrategy, Component, computed, input, signal } from '@angular/core';
import { GeneratedFile } from '../core/models';

@Component({
  selector: 'app-code-viewer',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="viewer">
      <div class="tabs" role="tablist" aria-label="Generated files">
        @for (f of files(); track f.path; let i = $index) {
          <button type="button" role="tab" [class.on]="i === active()" [attr.aria-selected]="i === active()" (click)="select(i)">
            <span class="ext" [attr.data-ext]="ext(f.path)"></span>{{ base(f.path) }}
          </button>
        }
      </div>
      <div class="bar">
        <span class="path">{{ current().path }}</span>
        <button type="button" class="copy" (click)="copy()">{{ copied() ? 'Copied ✓' : 'Copy' }}</button>
      </div>
      <pre tabindex="0"><code>@for (l of lines(); track $index) {<span class="ln">{{ l }}</span>}</code></pre>
    </div>
  `,
  styles: `
    .viewer {
      overflow: hidden;
      border-radius: var(--radius);
      border: 1px solid #1f2430;
      background: #0e1116;
      box-shadow: var(--shadow-lg);
      animation: popIn 0.6s var(--ease) both;
    }
    .tabs {
      display: flex;
      gap: 2px;
      padding: 10px 10px 0;
      overflow-x: auto;
      background: #0a0d12;
      scrollbar-width: none;
    }
    .tabs button {
      display: inline-flex;
      align-items: center;
      gap: 8px;
      padding: 9px 14px;
      border: 0;
      border-radius: 10px 10px 0 0;
      font: 500 13px/1 var(--font-mono);
      color: #7c8497;
      background: transparent;
      white-space: nowrap;
      cursor: pointer;
      transition: color 0.2s, background 0.2s;
    }
    .tabs button:hover {
      color: #c8cedb;
    }
    .tabs button.on {
      color: #fff;
      background: #0e1116;
    }
    .tabs button:focus-visible {
      outline: 2px solid var(--accent-2);
      outline-offset: -2px;
    }
    .ext {
      width: 8px;
      height: 8px;
      border-radius: 50%;
      background: #7c8497;
    }
    .ext[data-ext='ts'] { background: #3b82f6; }
    .ext[data-ext='html'] { background: #f97316; }
    .ext[data-ext='scss'] { background: #ec4899; }
    .ext[data-ext='md'] { background: #a3a3a3; }
    .bar {
      display: flex;
      align-items: center;
      justify-content: space-between;
      padding: 10px 16px;
      border-bottom: 1px solid #1a1f2a;
      font: 400 12px/1 var(--font-mono);
      color: #7c8497;
    }
    .copy {
      padding: 6px 12px;
      border-radius: 999px;
      border: 1px solid #2a3040;
      color: #c8cedb;
      background: transparent;
      font: 500 12px/1 var(--font);
      cursor: pointer;
      transition: background 0.2s, border-color 0.2s;
    }
    .copy:hover {
      background: #171c26;
      border-color: #3a4256;
    }
    pre {
      margin: 0;
      max-height: 560px;
      overflow: auto;
      padding: 16px 0 20px;
      counter-reset: line;
      font: 400 13px/1.7 var(--font-mono);
      color: #d6dbe6;
      tab-size: 2;
    }
    pre:focus-visible {
      outline: 2px solid var(--accent-2);
      outline-offset: -2px;
    }
    .ln {
      display: block;
      min-height: 1.7em;
      padding: 0 20px 0 0;
      white-space: pre;
    }
    .ln::before {
      counter-increment: line;
      content: counter(line);
      display: inline-block;
      width: 48px;
      padding-right: 16px;
      margin-right: 12px;
      text-align: right;
      color: #4a5266;
      user-select: none;
    }
  `,
})
export class CodeViewerComponent {
  readonly files = input.required<GeneratedFile[]>();

  protected readonly active = signal(0);
  protected readonly copied = signal(false);
  protected readonly current = computed(() => this.files()[this.active()] ?? this.files()[0]);
  protected readonly lines = computed(() => {
    const parts = this.current().content.replace(/\r\n/g, '\n').split('\n');
    if (parts.length > 1 && parts[parts.length - 1] === '') parts.pop();
    return parts;
  });

  protected select(i: number): void {
    this.active.set(i);
    this.copied.set(false);
  }

  protected base(path: string): string {
    return path.split('/').pop() ?? path;
  }

  protected ext(path: string): string {
    return path.split('.').pop() ?? '';
  }

  protected async copy(): Promise<void> {
    try {
      await navigator.clipboard.writeText(this.current().content);
      this.copied.set(true);
      setTimeout(() => this.copied.set(false), 1500);
    } catch {
      /* clipboard may be blocked; ignore */
    }
  }
}
