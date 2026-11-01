using System.Net;
using System.Text.Json;

namespace DesignToAngular.Services.CodeGen;

/// <summary>
/// Inline SVG icons from the Lucide set (ISC license, about 1900 icons), loaded from Resources/icons.json.
/// The model names an icon; the name is matched exactly, through common aliases, then through Lucide's own tags.
/// When nothing matches, the caller crops the icon from the original design instead.
/// If icons.json is missing, a tiny built-in set keeps the generator working.
/// </summary>
public static class IconLibrary
{
    private sealed record IconData(string B, string[] T);

    private const string Outline =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"none\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">";

    private const string Solid =
        "<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 24 24\" fill=\"currentColor\" stroke=\"currentColor\" stroke-width=\"2\" stroke-linecap=\"round\" stroke-linejoin=\"round\" aria-hidden=\"true\">";

    private static readonly Dictionary<string, string> Builtin = new()
    {
        ["eye"] = "<path d=\"M10 12a2 2 0 1 0 4 0a2 2 0 0 0 -4 0\"/><path d=\"M21 12c-2.4 4 -5.4 6 -9 6c-3.6 0 -6.6 -2 -9 -6c2.4 -4 5.4 -6 9 -6c3.6 0 6.6 2 9 6\"/>",
        ["search"] = "<path d=\"M10 10m-7 0a7 7 0 1 0 14 0a7 7 0 1 0 -14 0\"/><path d=\"M21 21l-6 -6\"/>",
        ["x"] = "<path d=\"M18 6l-12 12\"/><path d=\"M6 6l12 12\"/>",
        ["check"] = "<path d=\"M5 12l5 5l10 -10\"/>",
        ["menu"] = "<path d=\"M4 6l16 0\"/><path d=\"M4 12l16 0\"/><path d=\"M4 18l16 0\"/>",
        ["arrow-right"] = "<path d=\"M5 12l14 0\"/><path d=\"M13 18l6 -6\"/><path d=\"M13 6l6 6\"/>",
    };

    private static readonly Dictionary<string, string> Aliases = new()
    {
        ["home"] = "house", ["close"] = "x", ["cross"] = "x", ["hamburger"] = "menu", ["burger"] = "menu",
        ["magnify"] = "search", ["magnifier"] = "search", ["magnifying-glass"] = "search",
        ["arrow-forward"] = "arrow-right", ["arrow-back"] = "arrow-left", ["email"] = "mail", ["envelope"] = "mail",
        ["location"] = "map-pin", ["pin"] = "map-pin", ["cart"] = "shopping-cart", ["bag"] = "shopping-bag",
        ["gear"] = "settings", ["cog"] = "settings", ["like"] = "heart", ["favorite"] = "heart", ["favourite"] = "heart",
        ["share"] = "share-2", ["telephone"] = "phone", ["person"] = "user", ["profile"] = "user", ["tick"] = "check",
        ["checkmark"] = "check", ["download"] = "download", ["external-link"] = "external-link", ["more"] = "ellipsis",
        ["dots"] = "ellipsis", ["more-horizontal"] = "ellipsis", ["more-vertical"] = "ellipsis-vertical",
        ["visibility"] = "eye", ["visibility-off"] = "eye-off", ["eye-slash"] = "eye-off", ["lightning"] = "zap",
        ["magic"] = "sparkles", ["sparkle"] = "sparkles", ["loudspeaker"] = "megaphone",
    };

    /// <summary>Names of companies: these must never be guessed from tags (a bird is not the Twitter logo).</summary>
    private static readonly HashSet<string> Brands = new()
    {
        "facebook", "twitter", "x-twitter", "instagram", "linkedin", "youtube", "github", "tiktok", "pinterest",
        "whatsapp", "telegram", "discord", "slack", "google", "apple", "amazon", "microsoft", "snapchat", "reddit",
        "spotify", "twitch", "figma", "dribbble", "behance", "medium", "paypal", "visa", "mastercard", "netflix",
        "notion", "hubspot", "zoom", "dropbox", "android", "windows", "gitlab", "stripe", "shopify",
    };

    /// <summary>Simple closed shapes that still look right when filled.</summary>
    private static readonly HashSet<string> Fillable = new()
    {
        "heart", "star", "bookmark", "flag", "circle", "square", "triangle", "zap", "cloud", "moon", "play",
        "thumbs-up", "thumbs-down", "crown", "gem",
    };

    private static readonly string[] Prefixes =
        ["lucide-", "icon-", "tabler-", "ti-", "ph-", "fa-", "fas-", "far-", "fab-", "bi-", "mdi-", "heroicons-", "feather-"];

    private static readonly string[] Suffixes =
        ["-icon", "-outline", "-filled", "-solid", "-fill", "-line", "-regular", "-bold", "-thin", "-light"];

    private static readonly Lazy<Dictionary<string, IconData>> Data = new(Load);
    private static readonly Lazy<Dictionary<string, List<string>>> TagIndex = new(BuildTagIndex);

    private static Dictionary<string, IconData> Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Resources", "icons.json");
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, IconData>>(json,
                       new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch (Exception)
        {
            return new(); // a broken icon file must never break code generation
        }
    }

    private static Dictionary<string, List<string>> BuildTagIndex()
    {
        var index = new Dictionary<string, List<string>>();
        foreach (var (key, icon) in Data.Value)
        {
            foreach (var tag in icon.T ?? Array.Empty<string>())
            {
                var t = tag.ToLowerInvariant();
                if (!index.TryGetValue(t, out var list)) index[t] = list = new();
                list.Add(key);
            }
        }
        return index;
    }

    private static string Normalize(string name)
    {
        var key = name.Trim().ToLowerInvariant().Replace('_', '-').Replace(' ', '-');
        foreach (var p in Prefixes) if (key.StartsWith(p)) { key = key[p.Length..]; break; }
        foreach (var s in Suffixes) if (key.EndsWith(s)) { key = key[..^s.Length]; break; }
        return key;
    }

    /// <summary>The canonical icon name for what the model wrote, or null when no icon fits.</summary>
    public static string? Find(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var key = Normalize(name);
        if (key.Length == 0) return null;

        var data = Data.Value;
        if (data.Count == 0) return Builtin.ContainsKey(key) ? key : (Aliases.TryGetValue(key, out var a0) && Builtin.ContainsKey(a0) ? a0 : null);

        if (data.ContainsKey(key)) return key;
        if (Aliases.TryGetValue(key, out var alias) && data.ContainsKey(alias)) return alias;

        // Lucide names these "circle-check", while other sets say "check-circle".
        foreach (var shape in new[] { "circle", "square" })
        {
            if (!key.EndsWith("-" + shape)) continue;
            var swapped = $"{shape}-{key[..^(shape.Length + 1)]}";
            if (data.ContainsKey(swapped)) return swapped;
        }

        if (Brands.Contains(key)) return null;

        var phrase = key.Replace('-', ' ');
        if (TagIndex.Value.TryGetValue(phrase, out var keys) || TagIndex.Value.TryGetValue(key, out keys))
            return keys.OrderBy(k => k.Length).ThenBy(k => k, StringComparer.Ordinal).First();

        return null;
    }

    public static string Render(string? name, string? style)
    {
        var key = Find(name);
        var filled = style == "filled";

        if (key == "eye" && filled)
            return Solid + "<path d=\"M12 5C7 5 3.5 8.5 2 12c1.5 3.5 5 7 10 7s8.5-3.5 10-7c-1.5-3.5-5-7-10-7z\"/><circle cx=\"12\" cy=\"12\" r=\"3\" fill=\"#fff\" stroke=\"none\"/></svg>";

        if (key != null)
        {
            var body = Data.Value.TryGetValue(key, out var icon) ? icon.B : Builtin[key];
            return (filled && Fillable.Contains(key) ? Solid : Outline) + body + "</svg>";
        }

        return Outline + $"<!-- TODO: icon '{WebUtility.HtmlEncode((name ?? "unknown").Replace("--", "-"))}' was not found -->" +
               "<circle cx=\"12\" cy=\"12\" r=\"9\"/></svg>";
    }
}
