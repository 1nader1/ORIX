using System.Globalization;
using System.Text;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

public class CssBuilder(DesignDocument doc, Dictionary<DesignNode, string> names, TokenResolver resolver, Dictionary<DesignNode, GeneratedAsset> assets, MotionPlan plan)
{
    private const string Ring = "outline: 2px solid var(--color-primary, #3b82f6);\n  outline-offset: 2px;";

    /// <param name="tokenScope">":host" for the Angular component, ":root" for the standalone preview.</param>
    public string Build(string tokenScope, bool preview)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{tokenScope} {{");
        if (!preview) sb.AppendLine("  display: block;");
        foreach (var line in TokenLines(doc.Tokens)) sb.AppendLine("  " + line);
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("*,\n*::before,\n*::after {\n  box-sizing: border-box;\n}");
        sb.AppendLine();
        if (preview) sb.AppendLine("body {\n  margin: 0;\n}\n");
        Walk(doc.Root, true, sb);

        if (plan.Float.Count > 0)
            sb.AppendLine("@keyframes motion-float {\n  0%,\n  100% {\n    translate: 0 0;\n  }\n  50% {\n    translate: 0 -10px;\n  }\n}\n");
        if (plan.Hover.Count > 0 || plan.Float.Count > 0 || plan.Marquee.Count > 0)
            sb.AppendLine("@media (prefers-reduced-motion: reduce) {\n  *,\n  *::before,\n  *::after {\n    animation: none !important;\n    transition: none !important;\n  }\n}");
        return sb.ToString().TrimEnd() + "\n";
    }

    public static IEnumerable<string> TokenLines(Tokens t)
    {
        foreach (var c in t.Colors.Where(c => CssSafe.IsSafe(c.Value))) yield return $"--color-{Naming.Slug(c.Key)}: {c.Value};";
        foreach (var r in t.Radius) yield return $"--radius-{Naming.Slug(r.Key)}: {Px(r.Value)};";
        if (t.Shadows != null)
            foreach (var s in t.Shadows.Where(s => CssSafe.IsSafe(s.Value))) yield return $"--shadow-{Naming.Slug(s.Key)}: {s.Value};";
        if (CssSafe.Font(t.Typography?.FontFamilyHeading) is { } heading)
            yield return $"--font-heading: {FontStack(heading)};";
        if (CssSafe.Font(t.Typography?.FontFamilyBody) is { } body)
            yield return $"--font-body: {FontStack(body)};";
        if (t.Typography?.Scale != null)
            foreach (var s in t.Typography.Scale) yield return $"--font-size-{Naming.Slug(s.Key)}: {Px(s.Value)};";
        if (t.Spacing != null) yield return $"--spacing-unit: {Px(t.Spacing.Unit)};";
    }

    public static string FontStack(string family) =>
        family.Contains(',') ? family : family + ", sans-serif";

    private void Walk(DesignNode n, bool isRoot, StringBuilder sb)
    {
        sb.Append(Rule(n, isRoot));
        foreach (var k in NodeRules.Kids(n)) Walk(k, false, sb);
    }

    private string Rule(DesignNode n, bool isRoot)
    {
        var cls = names[n];
        var d = new Decls();
        var typo = doc.Tokens.Typography;
        var extra = new StringBuilder();

        switch (n.Type)
        {
            case "heading":
                d.Set("margin", "0");
                if (CssSafe.Font(typo?.FontFamilyHeading) != null) d.Set("font-family", "var(--font-heading)");
                break;
            case "text":
                d.Set("margin", "0");
                break;
            case "button":
                d.Set("display", "inline-flex");
                d.Set("align-items", "center");
                d.Set("justify-content", "center");
                d.Set("border", "none");
                d.Set("cursor", "pointer");
                d.Set("font-family", "inherit");
                extra.AppendLine($".{cls}:disabled {{\n  opacity: 0.6;\n  cursor: not-allowed;\n}}\n");
                break;
            case "input":
            case "textarea":
            case "select":
                d.Set("font-family", "inherit");
                d.Set("font-size", "var(--font-size-body, 14px)");
                d.Set("border", "none");
                d.Set("background", "transparent");
                d.Set("min-width", "0");
                if (NodeRules.IsBareInput(n)) d.Set("outline", "none");
                var ph = resolver.Resolve(n.Style?.PlaceholderColor)
                         ?? (doc.Tokens.Colors.ContainsKey("placeholder") ? "var(--color-placeholder)" : null);
                if (ph != null && n.Type != "select")
                    extra.AppendLine($".{cls}::placeholder {{\n  color: {ph};\n}}\n");
                break;
            case "icon":
                d.Set("display", "inline-flex");
                d.Set("align-items", "center");
                d.Set("justify-content", "center");
                d.Set("flex-shrink", "0");
                if (NodeRules.IsToggle(n))
                {
                    d.Set("background", "none");
                    d.Set("border", "none");
                    d.Set("padding", "0");
                    d.Set("cursor", "pointer");
                }
                var size = resolver.Resolve(n.Style?.FontSize) ?? "20px";
                extra.AppendLine($".{cls} svg {{\n  width: {size};\n  height: {size};\n}}\n");
                break;
            case "link":
                d.Set("text-decoration", "none");
                d.Set("cursor", "pointer");
                break;
            case "divider":
                d.Set("border", "none");
                d.Set("border-top", "1px solid var(--color-border, #e5e7eb)");
                d.Set("width", "100%");
                d.Set("margin", "0");
                break;
            case "image":
                d.Set("display", "block");
                d.Set("max-width", "100%");
                d.Set("object-fit", "cover");
                if (!assets.ContainsKey(n)) d.Set("background", "var(--color-border, #e5e7eb)");
                break;
            case "list":
                d.Set("list-style", "none");
                d.Set("margin", "0");
                d.Set("padding", "0");
                break;
        }

        if (isRoot)
        {
            if (CssSafe.Font(typo?.FontFamilyBody) != null) d.Set("font-family", "var(--font-body)");
            if (doc.Tokens.Colors.ContainsKey("text")) d.Set("color", "var(--color-text)");
        }

        if (NodeRules.Kids(n).Any(k => k.Layout?.Position == "absolute")) d.Set("position", "relative");

        AddLayout(n.Layout, d, isRoot);
        AddStyle(n.Style, d);
        AddRuns(n, cls, extra);

        AddMotion(n, cls, d, extra);

        var hasOwnBorder = !string.IsNullOrWhiteSpace(n.Style?.Border) && n.Style!.Border != "none";
        if (hasOwnBorder && NodeRules.Kids(n).Any(k => NodeRules.IsBareInput(k)))
            extra.AppendLine($".{cls}:focus-within {{\n  {Ring}\n}}\n");

        var sb = new StringBuilder();
        if (d.Count > 0)
        {
            sb.AppendLine($".{cls} {{");
            foreach (var line in d.Lines()) sb.AppendLine("  " + line);
            sb.AppendLine("}");
            sb.AppendLine();
        }
        sb.Append(extra);
        return sb.ToString();
    }

    /// <summary>Hover, floating and scrolling logo rows are plain CSS. Scroll-in animations are played by Motion.</summary>
    private void AddMotion(DesignNode n, string cls, Decls d, StringBuilder extra)
    {
        if (plan.Hover.Contains(n))
        {
            d.Set("transition", "translate 0.3s ease, box-shadow 0.3s ease, filter 0.3s ease");
            var hover = n.Type == "card"
                ? "translate: 0 -4px;\n    box-shadow: 0 16px 32px -16px rgba(0, 0, 0, 0.3);"
                : "translate: 0 -2px;\n    filter: brightness(1.06);";
            extra.AppendLine($"@media (hover: hover) {{\n  .{cls}:hover {{\n    {hover}\n  }}\n}}\n");
        }

        if (plan.Float.Contains(n))
        {
            var index = cls.Sum(ch => ch) % 3; // stable between runs, unlike string.GetHashCode()
            d.Set("animation", $"motion-float {5 + index}s ease-in-out {(index * 7 / 10.0).ToString("0.#", CultureInfo.InvariantCulture)}s infinite");
        }

        if (plan.Marquee.Contains(n))
        {
            var width = System.Text.RegularExpressions.Regex.Match(n.Layout?.Width ?? "", @"^(\d+)px$");
            var seconds = width.Success ? Math.Clamp(int.Parse(width.Groups[1].Value) / 50, 12, 60) : 30;
            extra.AppendLine($".{cls}-marquee {{\n  overflow: hidden;\n  width: {(width.Success ? width.Value : "100%")};\n  max-width: 100%;\n}}\n");
            extra.AppendLine($".{cls}-marquee-track {{\n  display: flex;\n  width: max-content;\n  animation: {cls}-marquee {seconds}s linear infinite;\n}}\n");
            extra.AppendLine($".{cls}-marquee-track > img {{\n  flex: none;\n  max-width: none;\n}}\n");
            extra.AppendLine($"@keyframes {cls}-marquee {{\n  to {{\n    transform: translateX(-50%);\n  }}\n}}\n");
        }
    }

    private void AddLayout(LayoutInfo? l, Decls d, bool isRoot)
    {
        if (l == null) return;

        var display = l.Display;
        if (display == null && (l.Direction != null || l.Gap != null || l.Align != null || l.Justify != null))
            display = "flex";
        d.Set("display", display);

        if (display == "flex") d.Set("flex-direction", l.Direction);
        if (l.Gap != null) d.Set("gap", Px(l.Gap.Value));
        d.Set("align-items", l.Align switch
        {
            "start" => "flex-start",
            "end" => "flex-end",
            null => null,
            var a => a
        });
        d.Set("justify-content", l.Justify switch
        {
            "start" => "flex-start",
            "end" => "flex-end",
            "between" => "space-between",
            "around" => "space-around",
            null => null,
            var j => j
        });
        if (display == "grid" && l.Columns != null)
        {
            d.Set("grid-template-columns", $"repeat({l.Columns}, 1fr)");
            d.Set("align-content", "start"); // extra height must not stretch the rows
        }
        d.Set("padding", Box(l.Padding));
        d.Set("margin", Box(l.Margin));
        d.Set("width", l.Width);
        if (isRoot && (l.Height == null || l.Height == "100%" || l.Height == "100vh"))
            d.Set("min-height", "100vh");
        else
            d.Set("height", l.Height);
        d.Set("max-width", l.MaxWidth);
        d.Set("aspect-ratio", CssSafe.Ratio(l.AspectRatio));
        if (!isRoot) d.Set("min-height", CssSafe.Length(l.MinHeight));

        d.Set("position", CssSafe.OneOf(l.Position, "relative", "absolute"));
        d.Set("top", l.Top);
        d.Set("right", l.Right);
        d.Set("bottom", l.Bottom);
        d.Set("left", l.Left);
        if (l.ZIndex is { } z) d.Set("z-index", Math.Clamp(z, -10, 1000).ToString(CultureInfo.InvariantCulture));
        if (l.Rotate is { } r && r != 0)
            d.Set("transform", $"rotate({Math.Clamp(r, -360, 360).ToString("0.##", CultureInfo.InvariantCulture)}deg)");
        d.Set("overflow", CssSafe.OneOf(l.Overflow, "hidden", "auto"));
    }

    private void AddStyle(StyleInfo? s, Decls d)
    {
        if (s == null) return;
        d.Set("background", resolver.Resolve(s.Background));
        d.Set("color", resolver.Resolve(s.Color));
        d.Set("font-size", resolver.Resolve(s.FontSize));
        if (s.FontWeight != null) d.Set("font-weight", s.FontWeight.Value.ToString(CultureInfo.InvariantCulture));
        d.Set("text-align", s.TextAlign);
        d.Set("border-radius", resolver.Resolve(s.Radius));
        d.Set("border", resolver.Resolve(s.Border));
        d.Set("box-shadow", resolver.Resolve(s.Shadow));
        if (s.Opacity != null) d.Set("opacity", s.Opacity.Value.ToString("0.##", CultureInfo.InvariantCulture));
        if (CssSafe.Font(s.FontFamily) is { } family) d.Set("font-family", FontStack(family));
        d.Set("letter-spacing", CssSafe.Spacing(s.LetterSpacing));
        d.Set("line-height", CssSafe.Line(s.LineHeight));
        d.Set("text-transform", CssSafe.OneOf(s.TextTransform, "none", "uppercase", "lowercase", "capitalize"));
        if (s.Italic == true) d.Set("font-style", "italic");
    }

    /// <summary>Parts of a text with their own look (one colored word in a heading) get their own small rules.</summary>
    private void AddRuns(DesignNode n, string cls, StringBuilder extra)
    {
        var runs = n.Content?.Runs;
        if (runs == null) return;

        for (var i = 0; i < runs.Count; i++)
        {
            var r = runs[i];
            var d = new Decls();
            d.Set("color", resolver.Resolve(r.Color));
            if (r.FontWeight != null) d.Set("font-weight", r.FontWeight.Value.ToString(CultureInfo.InvariantCulture));
            if (r.Italic == true) d.Set("font-style", "italic");
            var background = resolver.Resolve(r.Background);
            if (background != null)
            {
                d.Set("background", background);
                d.Set("padding", "0 0.2em");
                d.Set("border-radius", "0.2em");
            }
            if (d.Count == 0) continue;

            extra.AppendLine($".{cls}-run-{i} {{");
            foreach (var line in d.Lines()) extra.AppendLine("  " + line);
            extra.AppendLine("}\n");
        }
    }

    public static string Px(double v) =>
        v == 0 ? "0" : v.ToString("0.##", CultureInfo.InvariantCulture) + "px";

    private static string? Box(double[]? a) => a?.Length switch
    {
        1 => Px(a[0]),
        2 => $"{Px(a[0])} {Px(a[1])}",
        3 => $"{Px(a[0])} {Px(a[1])} {Px(a[2])}",
        4 => $"{Px(a[0])} {Px(a[1])} {Px(a[2])} {Px(a[3])}",
        _ => null
    };

    private sealed class Decls
    {
        private readonly List<KeyValuePair<string, string>> _items = new();
        public int Count => _items.Count;

        public void Set(string prop, string? value)
        {
            if (!CssSafe.IsSafe(value)) return;
            var i = _items.FindIndex(x => x.Key == prop);
            var kv = new KeyValuePair<string, string>(prop, value!);
            if (i >= 0) _items[i] = kv; else _items.Add(kv);
        }

        public IEnumerable<string> Lines() => _items.Select(x => $"{x.Key}: {x.Value};");
    }
}
