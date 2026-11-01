using System.Text;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

public static class AngularEmitter
{
    public static string Component(string kebab, string pascal, HtmlBuilder h, bool usesReveal)
    {
        var sb = new StringBuilder();
        var hasValidators = h.Fields.Any(f => f.Required || f.IsEmail);

        sb.AppendLine(h.HasForm ? "import { Component, inject } from '@angular/core';" : "import { Component } from '@angular/core';");
        if (h.HasForm)
        {
            sb.AppendLine($"import {{ FormBuilder, ReactiveFormsModule{(hasValidators ? ", Validators" : "")} }} from '@angular/forms';");
            sb.AppendLine($"import {{ {pascal}Service }} from './{kebab}.service';");
        }
        if (usesReveal) sb.AppendLine("import { RevealDirective } from '../shared/reveal.directive';");
        sb.AppendLine();
        sb.AppendLine("@Component({");
        sb.AppendLine($"  selector: 'app-{kebab}',");
        sb.AppendLine("  standalone: true,");
        var imports = new List<string>();
        if (h.HasForm) imports.Add("ReactiveFormsModule");
        if (usesReveal) imports.Add("RevealDirective");
        sb.AppendLine($"  imports: [{string.Join(", ", imports)}],");
        sb.AppendLine($"  templateUrl: './{kebab}.component.html',");
        sb.AppendLine($"  styleUrl: './{kebab}.component.scss',");
        sb.AppendLine("})");
        sb.AppendLine($"export class {pascal}Component {{");

        if (h.NeedsShowPassword) sb.AppendLine("  showPassword = false;");
        if (h.HasForm)
        {
            sb.AppendLine("  loading = false;");
            sb.AppendLine();
            sb.AppendLine("  private fb = inject(FormBuilder);");
            sb.AppendLine($"  private service = inject({pascal}Service);");
            sb.AppendLine();
            sb.AppendLine("  form = this.fb.nonNullable.group({");
            foreach (var f in h.Fields)
            {
                var v = new List<string>();
                if (f.Required) v.Add("Validators.required");
                if (f.IsEmail) v.Add("Validators.email");
                sb.AppendLine(v.Count > 0
                    ? $"    {f.Name}: [{f.Initial}, [{string.Join(", ", v)}]],"
                    : $"    {f.Name}: [{f.Initial}],");
            }
            sb.AppendLine("  });");
            sb.AppendLine();
            sb.AppendLine("  onSubmit(): void {");
            sb.AppendLine("    if (this.form.invalid) {");
            sb.AppendLine("      this.form.markAllAsTouched();");
            sb.AppendLine("      return;");
            sb.AppendLine("    }");
            sb.AppendLine();
            sb.AppendLine("    this.loading = true;");
            sb.AppendLine("    this.service.submit(this.form.getRawValue()).subscribe({");
            sb.AppendLine("      next: () => {");
            sb.AppendLine("        this.loading = false;");
            sb.AppendLine("        // TODO: handle success (navigate, show a message...)");
            sb.AppendLine("      },");
            sb.AppendLine("      error: () => {");
            sb.AppendLine("        this.loading = false;");
            sb.AppendLine("        // TODO: show an error message");
            sb.AppendLine("      },");
            sb.AppendLine("    });");
            sb.AppendLine("  }");
        }
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string Models(string pascal, HtmlBuilder h)
    {
        var sb = new StringBuilder();
        var groups = h.Fields.GroupBy(f => f.Entity).ToList();
        if (groups.Count == 0)
        {
            sb.AppendLine($"export type {pascal}FormValue = Record<string, never>;");
            return sb.ToString();
        }

        foreach (var g in groups)
        {
            sb.AppendLine($"export interface {g.Key} {{");
            foreach (var f in g) sb.AppendLine($"  {f.Name}: {f.TsType};");
            sb.AppendLine("}");
            sb.AppendLine();
        }
        if (groups.Count > 1)
            sb.AppendLine($"export type {pascal}FormValue = {string.Join(" & ", groups.Select(g => g.Key))};");
        return sb.ToString().TrimEnd() + "\n";
    }

    public static string Service(string kebab, string pascal, HtmlBuilder h)
    {
        var groups = h.Fields.Select(f => f.Entity).Distinct().ToList();
        var payload = groups.Count == 0 ? $"{pascal}FormValue" : groups.Count == 1 ? groups[0] : $"{pascal}FormValue";
        var sb = new StringBuilder();
        sb.AppendLine("import { HttpClient } from '@angular/common/http';");
        sb.AppendLine("import { Injectable, inject } from '@angular/core';");
        sb.AppendLine("import { Observable } from 'rxjs';");
        sb.AppendLine($"import {{ {payload} }} from './{kebab}.models';");
        sb.AppendLine();
        sb.AppendLine("// TODO: move this to your environment config");
        sb.AppendLine("const API_URL = 'http://localhost:5025/api';");
        sb.AppendLine();
        sb.AppendLine("@Injectable({ providedIn: 'root' })");
        sb.AppendLine($"export class {pascal}Service {{");
        sb.AppendLine("  private http = inject(HttpClient);");
        sb.AppendLine();
        sb.AppendLine("  // TODO: adjust the endpoint and response type to match your backend");
        sb.AppendLine($"  submit(payload: {payload}): Observable<unknown> {{");
        sb.AppendLine($"    return this.http.post(`${{API_URL}}/{kebab}`, payload);");
        sb.AppendLine("  }");
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string TokensScss(DesignDocument doc)
    {
        var t = doc.Tokens;
        var sb = new StringBuilder();
        sb.AppendLine("// Design tokens extracted from the design.");
        sb.AppendLine("// Use the SCSS variables in your own styles, or import the CSS variables globally.");
        sb.AppendLine();
        foreach (var c in t.Colors) sb.AppendLine($"$color-{Naming.Slug(c.Key)}: {c.Value};");
        foreach (var r in t.Radius) sb.AppendLine($"$radius-{Naming.Slug(r.Key)}: {CssBuilder.Px(r.Value)};");
        if (t.Shadows != null)
            foreach (var s in t.Shadows) sb.AppendLine($"$shadow-{Naming.Slug(s.Key)}: {s.Value};");
        if (!string.IsNullOrWhiteSpace(t.Typography?.FontFamilyHeading))
            sb.AppendLine($"$font-heading: {CssBuilder.FontStack(t.Typography!.FontFamilyHeading!)};");
        if (!string.IsNullOrWhiteSpace(t.Typography?.FontFamilyBody))
            sb.AppendLine($"$font-body: {CssBuilder.FontStack(t.Typography!.FontFamilyBody!)};");
        if (t.Typography?.Scale != null)
            foreach (var s in t.Typography.Scale) sb.AppendLine($"$font-size-{Naming.Slug(s.Key)}: {CssBuilder.Px(s.Value)};");
        sb.AppendLine();
        sb.AppendLine(":root {");
        foreach (var line in CssBuilder.TokenLines(t)) sb.AppendLine("  " + line);
        sb.AppendLine("}");
        return sb.ToString();
    }

    public static string Readme(DesignDocument doc, string kebab, string pascal, HtmlBuilder h, bool hasAssets, bool usesReveal)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {pascal} page");
        sb.AppendLine();
        sb.AppendLine("Generated by Design-to-Angular. Open `preview.html` in a browser to see the page without running Angular.");
        sb.AppendLine();
        sb.AppendLine("## Add it to your Angular project (v17+)");
        sb.AppendLine();
        sb.AppendLine($"1. Copy the `{kebab}/` folder into `src/app/`.");
        var step = 2;
        if (h.HasForm)
        {
            sb.AppendLine($"{step++}. Make sure `provideHttpClient()` is in the providers of `src/app/app.config.ts`.");
        }
        sb.AppendLine($"{step++}. Add a route: `{{ path: '{kebab}', component: {pascal}Component }}`, or use `<app-{kebab} />` and import `{pascal}Component`.");

        if (usesReveal)
            sb.AppendLine($"{step++}. Install the animation library (MIT license): `npm install motion@13`. Scroll animations come from the `data-reveal` attributes and `shared/reveal.directive.ts`; hover effects are plain CSS. Delete the attributes to switch an animation off.");
        if (hasAssets)
            sb.AppendLine($"{step++}. Copy the `assets/` folder from this ZIP into your project's `public/` folder (Angular 18+) or `src/assets/` (older versions). It holds the pictures cropped from the design.");
        var fonts = Fonts(doc);
        if (fonts.Count > 0)
            sb.AppendLine($"{step++}. The design uses these fonts: {string.Join(", ", fonts.Select(f => $"\"{f}\""))}. Add them to `src/index.html` (for example from Google Fonts) and include the weights you need.");
        if (h.HasForm)
            sb.AppendLine($"{step++}. Open `{kebab}/{kebab}.service.ts` and set the real API URL and endpoint (`TODO` comments).");
        sb.AppendLine();
        sb.AppendLine("## Files");
        sb.AppendLine();
        sb.AppendLine($"- `{kebab}.component.*`: template, styles (design tokens are CSS variables on `:host`) and logic");
        if (h.HasForm)
        {
            sb.AppendLine($"- `{kebab}.service.ts`: HTTP service, ready to connect to your backend");
            sb.AppendLine($"- `{kebab}.models.ts`: TypeScript interfaces for the form data");
        }
        if (usesReveal) sb.AppendLine("- `shared/reveal.directive.ts`: plays the scroll-in animations with Motion");
        if (hasAssets) sb.AppendLine("- `assets/images/`: pictures cropped from the original design");
        sb.AppendLine("- `design-tokens.scss`: colors, radius, shadows and font sizes extracted from the design");
        return sb.ToString();
    }

    public const string RevealDirective = """
        import { Directive, ElementRef, OnDestroy, OnInit, inject } from '@angular/core';
        import { animate, inView } from 'motion';

        // Plays a scroll-in animation for every element that has a data-reveal attribute.
        // Uses the free Motion library (MIT). Respects "reduce motion" and leaves content visible if anything fails.
        const PRESETS: Record<string, Record<string, unknown>> = {
          'fade-up': { opacity: [0, 1], translate: ['0px 24px', '0px 0px'] },
          'fade-in': { opacity: [0, 1] },
          'slide-down': { opacity: [0, 1], translate: ['0px -24px', '0px 0px'] },
          'scale-in': { opacity: [0, 1], scale: [0.94, 1] },
        };

        @Directive({ selector: '[data-reveal]', standalone: true })
        export class RevealDirective implements OnInit, OnDestroy {
          private readonly el = inject<ElementRef<HTMLElement>>(ElementRef).nativeElement;
          private stop?: () => void;

          ngOnInit(): void {
            if (typeof window === 'undefined' || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;

            const preset = PRESETS[this.el.dataset['reveal'] ?? ''];
            if (!preset) return;

            const delay = Number(this.el.dataset['revealDelay'] ?? 0) / 1000;
            this.el.style.opacity = '0';
            this.stop = inView(
              this.el,
              () => {
                animate(this.el, preset as never, { duration: 0.7, delay, ease: [0.22, 1, 0.36, 1] }).finished.then(() => {
                  for (const property of ['opacity', 'translate', 'scale']) this.el.style.removeProperty(property);
                });
              },
              { amount: 0.15 },
            );
          }

          ngOnDestroy(): void {
            this.stop?.();
          }
        }
        """;

    private static readonly string[] GenericFonts =
        ["sans-serif", "serif", "monospace", "system-ui", "arial", "helvetica", "inherit", "cursive", "fantasy"];

    /// <summary>Families known to ship the weights 400 to 700, so the full set can be requested safely.</summary>
    private static readonly string[] FullWeightFonts =
        ["inter", "poppins", "dm sans", "roboto", "open sans", "montserrat", "manrope", "plus jakarta sans",
         "outfit", "urbanist", "work sans", "rubik", "nunito", "mulish", "sora", "lexend", "figtree"];

    public static string? FirstFont(string? family)
    {
        var safe = CssSafe.Font(family);
        if (safe == null) return null;
        var first = safe.Split(',')[0].Trim().Trim('\'', '"');
        return first.Length == 0 || GenericFonts.Contains(first.ToLowerInvariant()) ? null : first;
    }

    /// <summary>Every web font the design uses: heading, body and per-element fonts.</summary>
    public static List<string> Fonts(DesignDocument doc)
    {
        var list = new List<string>();
        void Add(string? f)
        {
            var name = FirstFont(f);
            if (name != null && !list.Contains(name, StringComparer.OrdinalIgnoreCase)) list.Add(name);
        }

        Add(doc.Tokens.Typography?.FontFamilyBody);
        Add(doc.Tokens.Typography?.FontFamilyHeading);
        void Walk(DesignNode n)
        {
            Add(n.Style?.FontFamily);
            foreach (var k in NodeRules.Kids(n)) Walk(k);
        }
        Walk(doc.Root);
        return list;
    }

    /// <summary>The Google Fonts address of one family (full weights only for families known to have them).</summary>
    public static string GoogleFontUrl(string family)
    {
        var name = System.Net.WebUtility.UrlEncode(family);
        var weights = FullWeightFonts.Contains(family.ToLowerInvariant()) ? ":wght@400;500;600;700" : "";
        return $"https://fonts.googleapis.com/css2?family={name}{weights}&display=swap";
    }
}
