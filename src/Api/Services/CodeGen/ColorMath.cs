using System.Globalization;
using System.Text.RegularExpressions;

namespace DesignToAngular.Services.CodeGen;

public static class ColorMath
{
    private static readonly Regex Hex = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static bool IsHex(string? s) => s != null && Hex.IsMatch(s);

    public static (double R, double G, double B) ParseHex(string hex) => (
        int.Parse(hex.AsSpan(1, 2), NumberStyles.HexNumber) / 255.0,
        int.Parse(hex.AsSpan(3, 2), NumberStyles.HexNumber) / 255.0,
        int.Parse(hex.AsSpan(5, 2), NumberStyles.HexNumber) / 255.0);

    public static (double L, double A, double B) ToLab(string hex)
    {
        var (r, g, b) = ParseHex(hex);
        static double Lin(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        double lr = Lin(r), lg = Lin(g), lb = Lin(b);
        var x = (0.4124 * lr + 0.3576 * lg + 0.1805 * lb) / 0.95047;
        var y = 0.2126 * lr + 0.7152 * lg + 0.0722 * lb;
        var z = (0.0193 * lr + 0.1192 * lg + 0.9505 * lb) / 1.08883;
        static double F(double t) => t > 0.008856 ? Math.Cbrt(t) : 7.787 * t + 16.0 / 116;
        double fx = F(x), fy = F(y), fz = F(z);
        return (116 * fy - 16, 500 * (fx - fy), 200 * (fy - fz));
    }

    /// <summary>Perceptual distance (CIE76). Around 2 is barely visible, above 10 is clearly a different color.</summary>
    public static double DeltaE(string hexA, string hexB)
    {
        var a = ToLab(hexA);
        var b = ToLab(hexB);
        return Math.Sqrt(Math.Pow(a.L - b.L, 2) + Math.Pow(a.A - b.A, 2) + Math.Pow(a.B - b.B, 2));
    }

    /// <summary>Lightness (0-100) and chroma (0 = pure gray) in Lab. Presentation backdrops are light and nearly neutral.</summary>
    public static (double Lightness, double Chroma) LightChroma(string hex)
    {
        var lab = ToLab(hex);
        return (lab.L, Math.Sqrt(lab.A * lab.A + lab.B * lab.B));
    }
}
