using System.Text.RegularExpressions;
using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

public enum MotionLevel { None, Subtle, Expressive }

/// <summary>One scroll-in animation: kind is fade-up, fade-in, slide-down or scale-in.</summary>
public record MotionSpec(string Kind, int DelayMs);

public class MotionPlan
{
    public Dictionary<DesignNode, MotionSpec> Reveal { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<DesignNode> Hover { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<DesignNode> Float { get; } = new(ReferenceEqualityComparer.Instance);
    public HashSet<DesignNode> Marquee { get; } = new(ReferenceEqualityComparer.Instance);

    public bool IsEmpty => Reveal.Count == 0 && Hover.Count == 0 && Float.Count == 0 && Marquee.Count == 0;
}

/// <summary>
/// Chooses animations with fixed rules. A still image says nothing about motion, so nothing is guessed by the AI:
/// the rules follow the structure of the page, cost no tokens, and the Motion library (MIT) plays them.
/// </summary>
public static class MotionPlanner
{
    private static readonly Regex LogoWords =
        new(@"logo|brand|partner|client|customer|trusted", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static MotionLevel Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "none" or "off" => MotionLevel.None,
        "expressive" => MotionLevel.Expressive,
        _ => MotionLevel.Subtle,
    };

    public static MotionPlan Plan(DesignDocument doc, MotionLevel level, Dictionary<DesignNode, GeneratedAsset> assets)
    {
        var plan = new MotionPlan();
        if (level == MotionLevel.None) return plan;

        var heroHeading = FindFirst(doc.Root, n => n.Type == "heading");
        var navbarDone = false;

        void Walk(DesignNode n, DesignNode? parent, int depth)
        {
            // Hover feedback on things you can press or pick up.
            if (n.Type is "button" or "card") plan.Hover.Add(n);

            // The top bar slides in once.
            if (!navbarDone && n.Type == "navbar" && depth <= 3 && !Contains(n, heroHeading))
            {
                plan.Reveal.TryAdd(n, new MotionSpec("slide-down", 0));
                navbarDone = true;
            }

            // The first heading and the two elements after it come in one after the other.
            if (ReferenceEquals(n, heroHeading) && parent != null)
            {
                plan.Reveal.TryAdd(n, new MotionSpec("fade-up", 100));
                var kids = NodeRules.Kids(parent);
                var at = IndexOf(kids, n);
                var delay = 200;
                for (var i = at + 1; i < kids.Count && i <= at + 2; i++, delay += 100)
                    if (kids[i].Type is "text" or "button" or "container" or "link" or "form")
                        plan.Reveal.TryAdd(kids[i], new MotionSpec("fade-up", delay));
            }

            // Page sections appear while scrolling.
            if (n.Type is "section" or "footer" && depth <= 3 && !Contains(n, heroHeading))
                plan.Reveal.TryAdd(n, new MotionSpec("fade-up", 0));

            // Cards of one group rise one after the other.
            var cards = NodeRules.Kids(n).Where(k => k.Type == "card").ToList();
            if (cards.Count >= 2)
                for (var i = 0; i < cards.Count; i++)
                    plan.Reveal.TryAdd(cards[i], new MotionSpec("fade-up", Math.Min(i, 5) * 80));

            if (level == MotionLevel.Expressive)
            {
                if (n.Type == "heading" && !ReferenceEquals(n, heroHeading))
                    plan.Reveal.TryAdd(n, new MotionSpec("fade-up", 0));

                if (n.Type == "image" && assets.ContainsKey(n))
                {
                    if (IsLogoStrip(n)) plan.Marquee.Add(n);
                    else plan.Reveal.TryAdd(n, new MotionSpec("scale-in", 0));
                }

                if (IsFloatingDecoration(n)) plan.Float.Add(n);
            }

            foreach (var k in NodeRules.Kids(n)) Walk(k, n, depth + 1);
        }

        Walk(doc.Root, null, 0);
        return plan;
    }

    /// <summary>A wide picture that looks like a row of partner or client logos.</summary>
    private static bool IsLogoStrip(DesignNode n)
    {
        var text = $"{n.Id} {n.Role} {n.Content?.ImageDescription}";
        if (!LogoWords.IsMatch(text)) return false;
        return AssetPlanner.TryBox(AssetPlanner.BoxOf(n), out var b) && (b[3] - b[1]) >= 4 * Math.Max(1, b[2] - b[0]);
    }

    /// <summary>Something that floats over the page without text: a sticker, a shape, a decoration.</summary>
    private static bool IsFloatingDecoration(DesignNode n) =>
        n.Type is "container" or "card" or "image" or "icon"
        && (n.Layout?.Position == "absolute" || (n.Layout?.Rotate ?? 0) != 0)
        && !HasText(n);

    private static bool HasText(DesignNode n) =>
        !string.IsNullOrWhiteSpace(n.Content?.Text) || n.Content?.Runs is { Count: > 0 }
        || NodeRules.Kids(n).Any(HasText);

    private static DesignNode? FindFirst(DesignNode n, Func<DesignNode, bool> match)
    {
        if (match(n)) return n;
        foreach (var k in NodeRules.Kids(n))
        {
            var f = FindFirst(k, match);
            if (f != null) return f;
        }
        return null;
    }

    private static bool Contains(DesignNode n, DesignNode? target)
    {
        if (target == null) return false;
        if (ReferenceEquals(n, target)) return true;
        return NodeRules.Kids(n).Any(k => Contains(k, target));
    }

    private static int IndexOf(IReadOnlyList<DesignNode> list, DesignNode n)
    {
        for (var i = 0; i < list.Count; i++) if (ReferenceEquals(list[i], n)) return i;
        return -1;
    }
}
