using System.Text.RegularExpressions;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

public static class Naming
{
    public static string Slug(string s)
    {
        var x = Regex.Replace(s ?? "", "([a-z0-9])([A-Z])", "$1-$2");
        x = Regex.Replace(x, "[^A-Za-z0-9]+", "-").Trim('-').ToLowerInvariant();
        return x.Length == 0 ? "x" : x;
    }

    public static string Pascal(string s)
    {
        var parts = Regex.Split(s ?? "", "[^A-Za-z0-9]+").Where(p => p.Length > 0);
        var r = string.Concat(parts.Select(p => char.ToUpperInvariant(p[0]) + p[1..]));
        if (r.Length == 0) return "Page";
        return char.IsDigit(r[0]) ? "P" + r : r;
    }

    public static string Camel(string s)
    {
        var p = Pascal(s);
        return char.ToLowerInvariant(p[0]) + p[1..];
    }

    public static string PageKebab(string s)
    {
        var k = Slug(s);
        return char.IsDigit(k[0]) ? "page-" + k : k;
    }
}

public static class NodeRules
{
    public static IReadOnlyList<DesignNode> Kids(DesignNode n) =>
        n.Children ?? new List<DesignNode>();

    public static bool IsToggle(DesignNode n)
    {
        if (n.Type != "icon") return false;
        var icon = (n.Content?.IconName ?? "").ToLowerInvariant();
        var role = (n.Role ?? "").ToLowerInvariant();
        return icon.StartsWith("eye") || role.Contains("toggle") || role.Contains("visib");
    }

    public static bool HasText(DesignNode n) =>
        !string.IsNullOrWhiteSpace(n.Content?.Text) || n.Content?.Runs is { Count: > 0 }
        || Kids(n).Any(HasText);

    public static bool IsInteractive(DesignNode n) =>
        n.Type is "button" or "input" or "select" or "textarea" or "link" or "checkbox" || Kids(n).Any(IsInteractive);

    public static bool IsBareInput(DesignNode n) =>
        n.Type is "input" or "textarea" or "select"
        && (string.IsNullOrWhiteSpace(n.Style?.Border) || n.Style!.Border == "none");

    public static DesignNode? FirstInput(DesignNode n)
    {
        if (n.Type is "input" or "textarea" or "select") return n;
        foreach (var k in Kids(n))
        {
            var f = FirstInput(k);
            if (f != null) return f;
        }
        return null;
    }
}

public static class NodeNames
{
    public static Dictionary<DesignNode, string> Build(DesignNode root)
    {
        var map = new Dictionary<DesignNode, string>(ReferenceEqualityComparer.Instance);
        var used = new HashSet<string>();

        void Walk(DesignNode n)
        {
            var baseName = Naming.Slug(n.Id);
            if (char.IsDigit(baseName[0])) baseName = "n-" + baseName;
            var name = baseName;
            var i = 2;
            while (!used.Add(name)) name = $"{baseName}-{i++}";
            map[n] = name;
            foreach (var c in NodeRules.Kids(n)) Walk(c);
        }

        Walk(root);
        return map;
    }
}
