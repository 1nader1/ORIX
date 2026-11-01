using System.Text.RegularExpressions;

namespace DesignToAngular.Services.CodeGen;

/// <summary>
/// Everything the vision model writes ends up in generated CSS and HTML. Text inside an uploaded image could try to
/// smuggle in code, so unusual values are dropped instead of copied.
/// </summary>
public static class CssSafe
{
    private static readonly string[] Banned =
        ["url(", "expression", "@import", "javascript:", "<", ">", "{", "}", ";", "\\", "/*", "*/"];

    private static readonly Regex FontFamily = new(@"^[A-Za-z0-9 ,'""\-_]{1,120}$", RegexOptions.Compiled);
    private static readonly Regex LetterSpacing = new(@"^-?\d*\.?\d+(px|em|rem)$", RegexOptions.Compiled);
    private static readonly Regex LineHeight = new(@"^\d*\.?\d+(px|em|rem|%)?$", RegexOptions.Compiled);

    public static bool IsSafe(string? v) =>
        !string.IsNullOrWhiteSpace(v) && v.Length <= 300 &&
        !Banned.Any(b => v.Contains(b, StringComparison.OrdinalIgnoreCase));

    private static readonly Regex Ratio_ = new(@"^\d+(\.\d+)?\s*/\s*\d+(\.\d+)?$", RegexOptions.Compiled);
    private static readonly Regex Length_ = new(@"^\d+(\.\d+)?(px|rem|em|vh|%)$", RegexOptions.Compiled);

    public static string? Ratio(string? v) => v != null && Ratio_.IsMatch(v.Trim()) ? v.Trim() : null;
    public static string? Length(string? v) => v != null && Length_.IsMatch(v.Trim()) ? v.Trim() : null;

    public static string? Font(string? v) => v != null && FontFamily.IsMatch(v.Trim()) ? v.Trim() : null;
    public static string? Spacing(string? v) => v != null && LetterSpacing.IsMatch(v.Trim()) ? v.Trim() : null;
    public static string? Line(string? v) => v != null && LineHeight.IsMatch(v.Trim()) ? v.Trim() : null;

    public static string? OneOf(string? v, params string[] allowed) =>
        v != null && allowed.Contains(v) ? v : null;

    public static string Href(string? href)
    {
        if (string.IsNullOrWhiteSpace(href)) return "#";
        var h = href.Trim();
        var ok = h.StartsWith('#') || h.StartsWith('/')
                 || h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                 || h.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                 || h.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                 || h.StartsWith("tel:", StringComparison.OrdinalIgnoreCase);
        return ok ? h : "#";
    }
}
