using System.IO.Compression;
using System.Net;
using System.Text;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

public record GeneratedFile(string Path, string Content);

public record GeneratedProject(string Name, string PreviewHtml, List<GeneratedFile> Files, List<GeneratedAsset> Assets);

public class CodeGenerator
{
    public GeneratedProject Generate(DesignDocument input, string? pageName = null, MotionLevel motion = MotionLevel.Subtle, LayoutMode layout = LayoutMode.Measured)
    {
        var doc = GeometryRefiner.Apply(DesignNormalizer.Apply(input), layout);
        var kebab = Naming.PageKebab(string.IsNullOrWhiteSpace(pageName) ? doc.Meta.PageType : pageName);
        var pascal = Naming.Pascal(kebab);

        var names = NodeNames.Build(doc.Root);
        var resolver = new TokenResolver(doc.Tokens);
        var assets = AssetPlanner.Plan(doc.Root, names);
        var plan = MotionPlanner.Plan(doc, motion, assets);
        var css = new CssBuilder(doc, names, resolver, assets, plan);

        var ng = new HtmlBuilder(doc, names, assets, plan, angular: true);
        var ngHtml = ng.Build();
        var pv = new HtmlBuilder(doc, names, assets, plan, angular: false);
        var pvBody = pv.Build();

        var files = new List<GeneratedFile>
        {
            new($"{kebab}/{kebab}.component.ts", AngularEmitter.Component(kebab, pascal, ng, ng.UsesReveal)),
            new($"{kebab}/{kebab}.component.html", ngHtml),
            new($"{kebab}/{kebab}.component.scss", css.Build(":host", preview: false)),
        };
        if (ng.HasForm)
        {
            files.Add(new($"{kebab}/{kebab}.service.ts", AngularEmitter.Service(kebab, pascal, ng)));
            files.Add(new($"{kebab}/{kebab}.models.ts", AngularEmitter.Models(pascal, ng)));
        }
        if (ng.UsesReveal)
            files.Add(new("shared/reveal.directive.ts", AngularEmitter.RevealDirective.Replace("\r\n", "\n").TrimStart() + "\n"));
        files.Add(new("design-tokens.scss", AngularEmitter.TokensScss(doc)));
        files.Add(new("README.md", AngularEmitter.Readme(doc, kebab, pascal, ng, assets.Count > 0, ng.UsesReveal)));

        var preview = BuildPreview(doc, css.Build(":root", preview: true), pvBody, pv.UsesReveal);
        return new GeneratedProject(kebab, preview, files, assets.Values.ToList());
    }

    // Lets the Angular app resize the preview iframe to the real page height. Harmless when opened on its own.
    private const string HeightReporter = """
        <script>
        (function () {
          if (window.parent === window) return;
          function send() {
            window.parent.postMessage({ type: 'design-preview-height', height: document.documentElement.scrollHeight }, '*');
          }
          window.addEventListener('load', send);
          if (window.ResizeObserver) new ResizeObserver(send).observe(document.documentElement);
        })();
        </script>
        """;

    // The scroll-in animations of the preview: the same Motion library the generated Angular code uses.
    // If the script cannot load, nothing is hidden and the page simply shows without animation.
    private const string RevealScript = """
        <script src="https://cdn.jsdelivr.net/npm/motion@13.0.0/dist/motion.js"></script>
        <script>
        (function () {
          if (!window.Motion || window.matchMedia('(prefers-reduced-motion: reduce)').matches) return;
          var presets = {
            'fade-up': { opacity: [0, 1], translate: ['0px 24px', '0px 0px'] },
            'fade-in': { opacity: [0, 1] },
            'slide-down': { opacity: [0, 1], translate: ['0px -24px', '0px 0px'] },
            'scale-in': { opacity: [0, 1], scale: [0.94, 1] }
          };
          document.querySelectorAll('[data-reveal]').forEach(function (el) {
            var preset = presets[el.getAttribute('data-reveal')];
            if (!preset) return;
            var delay = Number(el.getAttribute('data-reveal-delay') || 0) / 1000;
            el.style.opacity = '0';
            Motion.inView(el, function () {
              Motion.animate(el, preset, { duration: 0.7, delay: delay, ease: [0.22, 1, 0.36, 1] }).finished.then(function () {
                ['opacity', 'translate', 'scale'].forEach(function (p) { el.style.removeProperty(p); });
              });
            }, { amount: 0.15 });
          });
        })();
        </script>
        """;

    private static string BuildPreview(DesignDocument doc, string css, string body, bool usesReveal)
    {
        var lang = doc.Meta.Language == "ar" ? "ar" : "en";
        var dir = doc.Meta.Direction == "rtl" ? "rtl" : "ltr";
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html>");
        sb.AppendLine($"<html lang=\"{lang}\" dir=\"{dir}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"utf-8\" />");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />");
        sb.AppendLine("<title>Preview</title>");
        // One link per family: if one family does not exist on Google Fonts, the others still load.
        foreach (var font in AngularEmitter.Fonts(doc))
            sb.AppendLine($"<link rel=\"stylesheet\" href=\"{WebUtility.HtmlEncode(AngularEmitter.GoogleFontUrl(font))}\" />");
        sb.AppendLine("<style>");
        sb.Append(css);
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.Append(body);
        sb.AppendLine(HeightReporter);
        if (usesReveal) sb.AppendLine(RevealScript);
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");
        return sb.ToString();
    }
}

/// <summary>Small heuristics that fix systematic mistakes of the vision model.</summary>
public static class DesignNormalizer
{
    public static DesignDocument Apply(DesignDocument doc)
    {
        doc = FixPresentationBackground(doc);
        doc = doc with { Root = SanitizeBoxes(doc.Root, null) };
        return doc with { Root = LimitAbsolutePositioning(doc.Root) };
    }

    // Designs shown inside a browser or device mockup often come with a light gray presentation backdrop.
    // The model sometimes uses it as the page background. Only a light, almost neutral color is treated that way,
    // so real colored or gradient backgrounds are never touched.
    private static DesignDocument FixPresentationBackground(DesignDocument doc)
    {
        if (doc.Meta.HasMockupFrame == true
            && doc.Root.Style?.Background == "$colors.background"
            && doc.Tokens.Colors.TryGetValue("background", out var bg)
            && ColorMath.IsHex(bg)
            && doc.Tokens.Colors.ContainsKey("surface"))
        {
            var (lightness, chroma) = ColorMath.LightChroma(bg);
            if (lightness > 85 && chroma < 6)
            {
                var style = doc.Root.Style with { Background = "$colors.surface" };
                return doc with { Root = doc.Root with { Style = style } };
            }
        }
        return doc;
    }

    /// <summary>A box that lies mostly outside its parent is a measuring mistake: it is dropped, never used.</summary>
    private static DesignNode SanitizeBoxes(DesignNode n, int[]? parentBox)
    {
        var box = AssetPlanner.TryBox(n.Box, out var b) ? b : null;
        if (box != null && parentBox != null)
        {
            var w = Math.Min(box[3], parentBox[3]) - Math.Max(box[1], parentBox[1]);
            var h = Math.Min(box[2], parentBox[2]) - Math.Max(box[0], parentBox[0]);
            var area = (box[3] - box[1]) * (double)(box[2] - box[0]);
            if (w <= 0 || h <= 0 || w * (double)h / area < 0.5) box = null;
        }

        var kids = n.Children?.Select(k => SanitizeBoxes(k, box ?? parentBox)).ToList();
        return n with { Box = box, Children = kids };
    }

    /// <summary>
    /// Absolute positioning is for decoration (a sticker, a floating shape). When the model uses it for content, text
    /// and buttons end up on top of each other, so such nodes go back into the normal flow.
    /// </summary>
    private static DesignNode LimitAbsolutePositioning(DesignNode root)
    {
        var total = 0;
        var absolute = 0;
        void Count(DesignNode n)
        {
            total++;
            if (n.Layout?.Position == "absolute") absolute++;
            foreach (var k in NodeRules.Kids(n)) Count(k);
        }
        Count(root);
        var everythingIsAbsolute = absolute > Math.Max(3, total / 4);

        DesignNode Fix(DesignNode n, bool isRoot)
        {
            var kids = n.Children?.Select(k => Fix(k, false)).ToList();
            var layout = n.Layout;
            if (!isRoot && layout?.Position == "absolute")
            {
                var decorative = n.Type is "container" or "card" or "image" or "icon"
                                 && !NodeRules.HasText(n) && !NodeRules.IsInteractive(n);
                var keep = decorative && (!everythingIsAbsolute || n.Type is "image" or "icon");
                if (!keep)
                    layout = layout with { Position = null, Top = null, Right = null, Bottom = null, Left = null, ZIndex = null };
            }
            return n with { Layout = layout, Children = kids };
        }
        return Fix(root, true);
    }
}

public static class ZipBuilder
{
    private const int MaxAssetBytes = 10 * 1024 * 1024;

    public static byte[] Build(GeneratedProject project, IEnumerable<AssetData>? assets = null)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string content)
            {
                var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
                using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
                w.Write(content);
            }

            Add("preview.html", project.PreviewHtml);
            foreach (var f in project.Files) Add(f.Path, f.Content);

            // Only accept pictures the generator itself asked for (also prevents path tricks in file names).
            var allowed = project.Assets.Select(a => a.FileName).ToHashSet();
            foreach (var a in assets ?? Enumerable.Empty<AssetData>())
            {
                if (!allowed.Contains(a.FileName)) continue;
                byte[] bytes;
                try { bytes = Convert.FromBase64String(a.Base64); } catch (FormatException) { continue; }
                if (bytes.Length == 0 || bytes.Length > MaxAssetBytes) continue;

                var entry = zip.CreateEntry($"assets/images/{a.FileName}", CompressionLevel.NoCompression);
                using var stream = entry.Open();
                stream.Write(bytes, 0, bytes.Length);
            }
        }
        return ms.ToArray();
    }
}
