using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

/// <summary>A picture that must be cropped out of the original design. Box = [yMin, xMin, yMax, xMax] in 0-1000.</summary>
public record GeneratedAsset(string FileName, int[] Box, string? Description);

/// <summary>A cropped picture sent back by the client (PNG, base64).</summary>
public record AssetData(string FileName, string Base64);

public static class AssetPlanner
{
    /// <summary>Pictures to crop: every image node, plus icons that are not in the icon set (brand logos, special glyphs).</summary>
    public static Dictionary<DesignNode, GeneratedAsset> Plan(DesignNode root, Dictionary<DesignNode, string> names)
    {
        var map = new Dictionary<DesignNode, GeneratedAsset>(ReferenceEqualityComparer.Instance);

        void Walk(DesignNode n)
        {
            if (NeedsCrop(n) && TryBox(BoxOf(n), out var box))
                map[n] = new GeneratedAsset($"{names[n]}.png", box, n.Content?.ImageDescription ?? n.Content?.IconName);
            foreach (var k in NodeRules.Kids(n)) Walk(k);
        }

        Walk(root);
        return map;
    }

    public static bool NeedsCrop(DesignNode n) =>
        n.Type == "image" || (n.Type == "icon" && IconLibrary.Find(n.Content?.IconName) == null);

    public static int[]? BoxOf(DesignNode n) => n.Box ?? n.Content?.ImageBox;

    public static bool TryBox(int[]? b, out int[] box)
    {
        box = Array.Empty<int>();
        if (b is not { Length: 4 }) return false;
        var c = b.Select(v => Math.Clamp(v, 0, 1000)).ToArray();
        if (c[2] - c[0] < 5 || c[3] - c[1] < 5) return false; // yMax > yMin and xMax > xMin by a real margin
        box = c;
        return true;
    }
}
