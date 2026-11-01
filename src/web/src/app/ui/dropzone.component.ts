import { ChangeDetectionStrategy, Component, input, output, signal } from '@angular/core';

const ACCEPTED = ['image/png', 'image/jpeg', 'image/webp'];
const MAX_BYTES = 5 * 1024 * 1024;

@Component({
  selector: 'app-dropzone',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(window:paste)': 'onPaste($event)' },
  template: `
    <button
      type="button"
      class="zone"
      [class.drag]="dragging()"
      [disabled]="disabled()"
      (click)="fileInput.click()"
      (dragover)="$event.preventDefault(); dragging.set(true)"
      (dragleave)="dragging.set(false)"
      (drop)="onDrop($event)"
    >
      <span class="icon" aria-hidden="true">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
          <path d="M12 16V4" /><path d="M7 9l5-5l5 5" /><path d="M4 16v2a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2v-2" />
        </svg>
      </span>
      <span class="title">Drop your design here</span>
      <span class="sub">or <u>browse files</u>, or paste from your clipboard</span>
      <span class="formats">PNG · JPG · WEBP · up to 5 MB</span>
    </button>
    <input #fileInput type="file" hidden accept="image/png,image/jpeg,image/webp" (change)="onInput($event)" />
    @if (hint()) {
      <p class="hint" role="alert">{{ hint() }}</p>
    }
  `,
  styles: `
    :host {
      display: block;
    }
    .zone {
      position: relative;
      width: 100%;
      min-height: 280px;
      display: flex;
      flex-direction: column;
      align-items: center;
      justify-content: center;
      gap: 6px;
      padding: 40px 24px;
      color: var(--ink);
      font-family: inherit;
      text-align: center;
      cursor: pointer;
      border: 1.5px dashed #d3d3cb;
      border-radius: 28px;
      background:
        radial-gradient(520px 200px at 50% 0%, rgba(99, 102, 241, 0.07), transparent 70%),
        var(--surface);
      box-shadow: var(--shadow);
      transition:
        border-color 0.3s var(--ease),
        transform 0.4s var(--ease),
        box-shadow 0.4s var(--ease),
        background 0.3s;
    }
    .zone:hover:not(:disabled),
    .zone.drag {
      border-color: var(--accent);
      transform: translateY(-2px);
      box-shadow: var(--shadow-lg);
    }
    .zone.drag {
      background: radial-gradient(520px 240px at 50% 0%, rgba(99, 102, 241, 0.16), transparent 70%), var(--surface);
    }
    .zone:focus-visible {
      outline: 2px solid var(--accent);
      outline-offset: 4px;
    }
    .icon {
      width: 64px;
      height: 64px;
      display: grid;
      place-items: center;
      margin-bottom: 10px;
      border-radius: 20px;
      color: var(--accent);
      background: var(--accent-soft);
      animation: float 4s ease-in-out infinite;
      transition: transform 0.4s var(--ease), background 0.3s, color 0.3s;
    }
    .zone.drag .icon {
      color: #fff;
      background: linear-gradient(135deg, var(--accent), var(--accent-2));
      transform: scale(1.08);
    }
    .icon svg {
      width: 28px;
      height: 28px;
    }
    .title {
      font-size: 22px;
      font-weight: 600;
      letter-spacing: -0.02em;
    }
    .sub {
      font-size: 15px;
      color: var(--ink-2);
    }
    .sub u {
      text-decoration-color: rgba(79, 70, 229, 0.45);
      text-underline-offset: 3px;
      color: var(--accent);
    }
    .formats {
      margin-top: 14px;
      padding: 6px 12px;
      border-radius: 999px;
      font-size: 12px;
      font-weight: 500;
      color: var(--ink-3);
      background: var(--line-2);
    }
    .hint {
      margin-top: 12px;
      text-align: center;
      font-size: 14px;
      color: var(--danger);
      animation: fadeIn 0.3s;
    }
  `,
})
export class DropzoneComponent {
  readonly disabled = input(false);
  readonly picked = output<File>();

  protected readonly dragging = signal(false);
  protected readonly hint = signal('');

  protected onInput(event: Event): void {
    const el = event.target as HTMLInputElement;
    const file = el.files?.[0];
    if (file) this.take(file);
    el.value = '';
  }

  protected onDrop(event: DragEvent): void {
    event.preventDefault();
    this.dragging.set(false);
    const file = event.dataTransfer?.files?.[0];
    if (file) this.take(file);
  }

  protected onPaste(event: ClipboardEvent): void {
    const file = event.clipboardData?.files?.[0];
    if (file) this.take(file);
  }

  private take(file: File): void {
    if (this.disabled()) return;
    if (!ACCEPTED.includes(file.type)) {
      this.hint.set('Please use a PNG, JPG or WEBP image.');
      return;
    }
    if (file.size > MAX_BYTES) {
      this.hint.set('This image is larger than 5 MB. Please use a smaller one.');
      return;
    }
    this.hint.set('');
    this.picked.emit(file);
  }
}
