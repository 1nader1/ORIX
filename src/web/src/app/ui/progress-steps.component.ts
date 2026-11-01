import { ChangeDetectionStrategy, Component, input } from '@angular/core';

@Component({
  selector: 'app-progress-steps',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <ol class="steps">
      @for (s of steps(); track $index) {
        <li [class.done]="$index < current()" [class.active]="$index === current()">
          <span class="ico">
            @if ($index < current()) {
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M5 12l5 5l10-10" /></svg>
            } @else if ($index === current()) {
              <span class="ring"></span>
            } @else {
              <span class="dot"></span>
            }
          </span>
          <span class="label">{{ s }}</span>
        </li>
      }
    </ol>
  `,
  styles: `
    .steps {
      list-style: none;
      margin: 22px 0 0;
      padding: 0;
      display: grid;
      gap: 6px;
    }
    li {
      position: relative;
      display: flex;
      align-items: center;
      gap: 14px;
      padding: 10px 0;
      color: var(--ink-3);
      font-size: 15px;
      font-weight: 500;
      transition: color 0.4s var(--ease);
    }
    li:not(:last-child)::after {
      content: '';
      position: absolute;
      left: 13px;
      top: 40px;
      width: 2px;
      height: 14px;
      border-radius: 2px;
      background: var(--line);
      transition: background 0.4s var(--ease);
    }
    li.done::after {
      background: var(--ok);
    }
    li.active,
    li.done {
      color: var(--ink);
    }
    .ico {
      width: 28px;
      height: 28px;
      flex: none;
      display: grid;
      place-items: center;
      border-radius: 50%;
      background: var(--line-2);
      border: 1px solid var(--line);
      transition: all 0.4s var(--ease);
    }
    li.done .ico {
      background: var(--ok);
      border-color: var(--ok);
      color: #fff;
      animation: popIn 0.4s var(--ease);
    }
    li.active .ico {
      background: #fff;
      border-color: var(--accent);
      animation: pulse 1.6s ease-out infinite;
    }
    svg {
      width: 14px;
      height: 14px;
    }
    .ring {
      width: 12px;
      height: 12px;
      border-radius: 50%;
      border: 2px solid var(--accent-soft);
      border-top-color: var(--accent);
      animation: spin 0.8s linear infinite;
    }
    .dot {
      width: 6px;
      height: 6px;
      border-radius: 50%;
      background: #c9c9c0;
    }
  `,
})
export class ProgressStepsComponent {
  readonly steps = input.required<string[]>();
  readonly current = input.required<number>();
}
