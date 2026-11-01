import { ChangeDetectionStrategy, Component, computed, input, output, signal } from '@angular/core';
import { encodeReplacement } from '../core/image-crop';
import { CroppedAsset, GeneratedAsset } from '../core/models';

@Component({
  selector: 'app-image-manager',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <section class="manager">
      <header>
        <h3>Images in this design</h3>
        <p>
          These pictures were cropped from your screenshot, so their quality can't be higher than the screenshot itself.
          Replace any of them with a high-resolution version. The preview and the ZIP update instantly.
        </p>
      </header>

      @if (error()) {
        <p class="error" role="alert">{{ error() }}</p>
      }

      <ul class="grid">
        @for (item of items(); track item.asset.fileName) {
          <li class="item">
            <div class="thumb">
              @if (item.crop; as crop) {
                <img [src]="crop.dataUrl" [alt]="item.asset.description || item.asset.fileName" />
              } @else {
                <span class="empty">No picture yet</span>
              }
            </div>
            <div class="info">
              <p class="name">{{ item.asset.fileName }}</p>
              <p class="meta">
                @if (item.crop; as crop) {
                  {{ crop.width }} × {{ crop.height }} px
                  @if (crop.replaced) {
                    <span class="tag ok">Replaced</span>
                  } @else if (crop.width < 480) {
                    <span class="tag warn">Low resolution</span>
                  }
                }
              </p>
              <label class="btn btn-ghost small">
                Replace
                <input type="file" hidden accept="image/*" (change)="pick(item.asset, $event)" />
              </label>
            </div>
          </li>
        }
      </ul>
    </section>
  `,
  styles: `
    .manager {
      padding: 28px;
      border-radius: var(--radius-lg);
      border: 1px solid var(--line);
      background: var(--surface);
      box-shadow: var(--shadow);
      animation: popIn 0.6s var(--ease) both;
    }
    h3 {
      font-size: 18px;
      font-weight: 600;
      letter-spacing: -0.01em;
    }
    header p {
      max-width: 680px;
      margin-top: 6px;
      font-size: 14px;
      color: var(--ink-3);
    }
    .error {
      margin-top: 14px;
      font-size: 14px;
      color: var(--danger);
    }
    .grid {
      display: grid;
      grid-template-columns: repeat(auto-fill, minmax(260px, 1fr));
      gap: 16px;
      margin: 22px 0 0;
      padding: 0;
      list-style: none;
    }
    .item {
      display: grid;
      gap: 12px;
      padding: 12px;
      border-radius: 18px;
      border: 1px solid var(--line);
      background: #fff;
      transition: transform 0.4s var(--ease), box-shadow 0.4s var(--ease);
    }
    .item:hover {
      transform: translateY(-3px);
      box-shadow: 0 18px 30px -18px rgba(16, 24, 40, 0.3);
    }
    .thumb {
      display: grid;
      place-items: center;
      height: 150px;
      overflow: hidden;
      border-radius: 12px;
      background:
        conic-gradient(#f1f1ec 25%, #fafaf7 0 50%, #f1f1ec 0 75%, #fafaf7 0) 0 0 / 16px 16px;
    }
    .thumb img {
      max-width: 100%;
      max-height: 100%;
      object-fit: contain;
    }
    .empty {
      font-size: 13px;
      color: var(--ink-3);
    }
    .name {
      overflow: hidden;
      font: 500 13px/1.3 var(--font-mono);
      white-space: nowrap;
      text-overflow: ellipsis;
    }
    .meta {
      display: flex;
      align-items: center;
      gap: 8px;
      min-height: 26px;
      margin: 4px 0 10px;
      font-size: 13px;
      color: var(--ink-3);
    }
    .tag {
      padding: 2px 8px;
      border-radius: 999px;
      font-size: 11px;
      font-weight: 600;
    }
    .tag.warn {
      color: #b45309;
      background: #fef3c7;
    }
    .tag.ok {
      color: var(--ok);
      background: rgba(22, 163, 74, 0.1);
    }
    .small {
      height: 36px;
      padding: 0 16px;
      font-size: 13px;
    }
    label.btn:focus-within {
      outline: 2px solid var(--accent);
      outline-offset: 3px;
    }
  `,
})
export class ImageManagerComponent {
  readonly assets = input.required<GeneratedAsset[]>();
  readonly cropped = input.required<CroppedAsset[]>();
  readonly replaced = output<CroppedAsset>();

  protected readonly error = signal('');
  protected readonly items = computed(() =>
    this.assets().map((asset) => ({ asset, crop: this.cropped().find((c) => c.fileName === asset.fileName) })),
  );

  protected async pick(asset: GeneratedAsset, event: Event): Promise<void> {
    const el = event.target as HTMLInputElement;
    const file = el.files?.[0];
    el.value = '';
    if (!file) return;
    if (!file.type.startsWith('image/')) {
      this.error.set('Please choose an image file (PNG, JPG, WEBP...).');
      return;
    }
    try {
      this.error.set('');
      this.replaced.emit(await encodeReplacement(file, asset.fileName));
    } catch {
      this.error.set('That image could not be read. Try another file.');
    }
  }
}
