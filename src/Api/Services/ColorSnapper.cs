using DesignToAngular.Domain.Design;
using DesignToAngular.Services.CodeGen;

namespace DesignToAngular.Services;

/// <summary>A color measured from the pixels of the uploaded image (share = fraction of pixels, 0-1).</summary>
public record PaletteColor(string Hex, double Share);

/// <summary>
/// The vision model estimates colors by eye. This pulls each token color to the nearest color that was
/// really measured in the image, when that measured color is close enough to be the same one.
/// </summary>
public static class ColorSnapper
{
    private const double MaxDeltaE = 10;

    public static DesignDocument Snap(DesignDocument doc, IReadOnlyList<PaletteColor>? palette)
    {
        if (palette is null || palette.Count == 0) return doc;

        var snapped = new Dictionary<string, string>();
        foreach (var (name, hex) in doc.Tokens.Colors)
        {
            snapped[name] = ColorMath.IsHex(hex) ? Nearest(hex, palette) : hex;
        }
        return doc with { Tokens = doc.Tokens with { Colors = snapped } };
    }

    private static string Nearest(string hex, IReadOnlyList<PaletteColor> palette)
    {
        var best = hex;
        var bestDistance = MaxDeltaE;
        foreach (var p in palette)
        {
            var d = ColorMath.DeltaE(hex, p.Hex);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = p.Hex;
            }
        }
        return best;
    }

    /// <summary>Keeps only well-formed entries from client input.</summary>
    public static List<PaletteColor> Clean(IEnumerable<PaletteColor>? input) =>
        (input ?? Enumerable.Empty<PaletteColor>())
            .Where(p => ColorMath.IsHex(p.Hex) && p.Share is >= 0 and <= 1)
            .Take(24)
            .Select(p => p with { Hex = p.Hex.ToLowerInvariant() })
            .ToList();
}
