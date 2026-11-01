using System.Text.RegularExpressions;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

/// <summary>Turns "$colors.primary" into "var(--color-primary)". Unknown tokens return null (declaration is skipped).</summary>
public class TokenResolver(Tokens tokens)
{
    private static readonly Regex Ref =
        new(@"\$(colors|radius|shadows)\.([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

    public string? Resolve(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var ok = true;

        var result = Ref.Replace(value, m =>
        {
            var group = m.Groups[1].Value;
            var key = m.Groups[2].Value;
            if (group == "colors" && tokens.Colors.ContainsKey(key)) return $"var(--color-{Naming.Slug(key)})";
            if (group == "radius" && tokens.Radius.ContainsKey(key)) return $"var(--radius-{Naming.Slug(key)})";
            if (group == "shadows" && tokens.Shadows != null && tokens.Shadows.ContainsKey(key)) return $"var(--shadow-{Naming.Slug(key)})";
            ok = false;
            return m.Value;
        });

        return ok ? result : null;
    }
}
