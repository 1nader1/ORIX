using DesignToAngular.Domain.Design;

namespace DesignToAngular.Services.CodeGen;

/// <summary>Where the layout comes from: measured from the element boxes, or only what the model wrote.</summary>
public enum LayoutMode { Measured, ModelOnly }

/// <summary>
/// The model reports where every element is (box). From those positions this works out how the page is really
/// built: rows or columns, alignment, widths, gaps, paddings and picture sizes. The model's own values always win
/// when it gave one, except when the boxes clearly show a row where it wrote a column (or the other way round).
/// A container whose children's boxes overlap is not trusted at all. Nothing here uses the AI.
/// </summary>
public static class GeometryRefiner
{
    private sealed record Scale(double X, double? Y);
    private sealed record Item(DesignNode Node, int[] Box);

    /// <summary>What a container passes down to its children: how they flow and how wide the container is.</summary>
    private sealed record Parent(int[] Box, string? Direction);

    private static readonly HashSet<string> Containers = new() { "container", "section", "card", "navbar", "footer", "form", "list" };
    private static readonly HashSet<string> Blocks = new() { "container", "section", "card", "navbar", "footer", "form", "list", "table" };

    public static DesignDocument Apply(DesignDocument doc, LayoutMode mode = LayoutMode.Measured)
    {
        if (mode == LayoutMode.ModelOnly) return doc;

        var rootBox = AssetPlanner.TryBox(doc.Root.Box, out var rb) ? rb : new[] { 0, 0, 1000, 1000 };
        var span = rootBox[3] - rootBox[1];
        if (span < 100) return doc;

        var designWidth = Math.Clamp(doc.Meta.DesignWidth ?? 1440, 320, 2560);
        var px = designWidth / (double)span; // design pixels per box unit, horizontally
        double? pxY = doc.Meta.ImageWidth is > 0 && doc.Meta.ImageHeight is > 0
            ? px * doc.Meta.ImageHeight.Value / doc.Meta.ImageWidth.Value
            : null; // vertical distances need the real proportions of the image

        var root = doc.Root with { Box = rootBox };
        return doc with { Root = Refine(root, new Scale(px, pxY), isRoot: true, parentIsRoot: false, parent: null) };
    }

    private static DesignNode Refine(DesignNode n, Scale s, bool isRoot, bool parentIsRoot, Parent? parent)
    {
        var layout = n.Layout ?? new LayoutInfo();
        var hasBox = AssetPlanner.TryBox(n.Box, out var box);
        var items = InFlowItems(NodeRules.Kids(n));

        // 1. How this container lays out its children.
        if (hasBox && Containers.Contains(n.Type) && !HeavyOverlap(items))
            layout = InferLayout(layout, items, box, s);

        // CSS: a flex container without a direction is a row.
        var direction = layout.Display == "flex" ? layout.Direction ?? "row" : null;
        Dictionary<DesignNode, double>? margins = null;

        // 2. This node's own size, relative to the container it sits in.
        if (hasBox)
        {
            var wPx = (box[3] - box[1]) * s.X;
            double? hPx = s.Y is { } y ? (box[2] - box[0]) * y : null;

            if (AssetPlanner.NeedsCrop(n))
            {
                if (layout.Width == null) layout = layout with { Width = Px(Math.Clamp(wPx, 8, 3000)) };
                if (layout.AspectRatio == null && hPx is > 0)
                    layout = layout with { AspectRatio = $"{(int)Math.Round(wPx)} / {(int)Math.Round(hPx.Value)}" };
            }
            else if (parent != null && layout.Position != "absolute")
            {
                layout = FitWidth(n, layout, box, parent, s, wPx, hPx);
            }

            // Buttons and fields keep their real height.
            if (n.Type is "button" or "input" or "select" or "textarea" && hPx is > 0 && layout.Height == null)
                layout = layout with { Height = Px(hPx.Value) };

            // A text block keeps the height it had, so what follows it does not drift upwards.
            if (n.Type is "text" or "heading" && hPx is >= 12 && layout.Height == null && layout.MinHeight == null)
                layout = layout with { MinHeight = Px(hPx.Value) };

            // Page sections and visible boxes keep their real height (a minimum, so longer content can still grow).
            if (((parentIsRoot || n.Type is "section" or "footer" or "navbar") || IsVisibleBox(n)) && !isRoot
                && hPx is >= 40 && layout.Height == null && layout.MinHeight == null)
                layout = layout with { MinHeight = Px(hPx.Value) };

            // Padding of containers, measured from where their content really starts and ends.
            if (!isRoot && s.Y is { } py && layout.Padding == null && Containers.Contains(n.Type) && !HeavyOverlap(items))
            {
                var padding = MeasurePadding(box, items, s.X, py);
                if (padding != null) layout = layout with { Padding = padding };
            }

            // The space between siblings: one gap when it is even, otherwise a margin per child.
            if (direction != null && layout.Gap == null && layout.Justify is not ("between" or "around") && !HeavyOverlap(items))
            {
                var spaces = MeasureSpaces(n, items, direction, s);
                if (spaces != null && spaces.Max() - spaces.Min() <= 4) layout = layout with { Gap = Math.Round(spaces.Median()) };
                else if (spaces != null) margins = spaces.Margins;
            }
        }

        var kids = n.Children?.Select(k => Refine(k, s, false, isRoot, hasBox ? new Parent(box, direction) : null)).ToList();
        if (margins != null && kids != null && direction != null)
        {
            // Uneven spacing: each child gets the space that really precedes it.
            for (var i = 0; i < kids.Count; i++)
            {
                if (!margins.TryGetValue(n.Children![i], out var space) || space < 1 || space > 300) continue;
                var current = kids[i].Layout ?? new LayoutInfo();
                if (current.Margin != null) continue;
                double[] margin = direction == "row" ? [0, 0, 0, Math.Round(space)] : [Math.Round(space), 0, 0, 0];
                kids[i] = kids[i] with { Layout = current with { Margin = margin } };
            }
        }
        return n with { Layout = layout, Children = kids };
    }

    // ---------------------------------------------------------------- layout inference

    private static LayoutInfo InferLayout(LayoutInfo layout, List<Item> items, int[] box, Scale s)
    {
        if (items.Count == 0 || layout.Display == "grid") return layout;
        var width = box[3] - box[1];
        if (width < 50) return layout;

        // One child: is it centered or pushed to the right?
        if (items.Count == 1)
        {
            var c = items[0].Box;
            if ((c[3] - c[1]) / (double)width >= 0.96) return layout;
            var align = HorizontalAlign(new List<int[]> { c }, box);
            if (align == null || align == "start") return layout;
            if (layout.Display is null or "block")
                return layout with { Display = "flex", Direction = "column", Align = layout.Align ?? align };
            if (layout.Display == "flex" && (layout.Direction ?? "row") == "column" && layout.Align == null)
                return layout with { Align = align };
            return layout;
        }

        var row = IsRow(items);
        var column = IsColumn(items);
        if (row == column) return TryGrid(layout, items, box, s); // unclear, or both: only a grid can still be detected

        var direction = row ? "row" : "column";
        if (layout.Display is null or "block")
            layout = layout with { Display = "flex", Direction = direction };
        else if (layout.Display == "flex" && (layout.Direction ?? "row") != direction)
            layout = layout with { Direction = direction };
        else if (layout.Display != "flex")
            return layout;

        var boxes = items.Select(i => i.Box).ToList();
        if (layout.Align == null)
        {
            var align = row ? VerticalAlign(boxes) : HorizontalAlign(boxes, box);
            if (!row && boxes.All(b => (b[3] - b[1]) / (double)width >= 0.96)) align = null; // full width children: stretch
            if (align != null) layout = layout with { Align = align };
        }

        if (row && layout.Justify == null)
        {
            var ordered = boxes.OrderBy(b => b[1]).ToList();
            var lead = ordered[0][1] - box[1];
            var trail = box[3] - ordered.Max(b => b[3]);
            var gaps = Enumerable.Range(1, ordered.Count - 1).Select(i => ordered[i][1] - ordered[i - 1][3]).Where(g => g > 0).ToList();
            if (lead <= 0.06 * width && trail <= 0.06 * width && gaps.Count > 0
                && (gaps.Max() > 0.1 * width || gaps.Max() > 2 * Math.Max(1, gaps.Min())))
                layout = layout with { Justify = "between" };
        }
        return layout;
    }

    /// <summary>Rows of equal length with matching columns: a grid.</summary>
    private static LayoutInfo TryGrid(LayoutInfo layout, List<Item> items, int[] box, Scale s)
    {
        if (layout.Display == "grid" || items.Count < 4) return layout;

        var rows = new List<List<Item>>();
        foreach (var item in items.OrderBy(i => i.Box[0]).ThenBy(i => i.Box[1]))
        {
            var home = rows.FirstOrDefault(r => Overlap(r[0].Box[0], r[0].Box[2], item.Box[0], item.Box[2]) >= 0.5);
            if (home != null) home.Add(item); else rows.Add(new List<Item> { item });
        }
        var k = rows[0].Count;
        if (rows.Count < 2 || k < 2 || rows.Any(r => r.Count != k)) return layout;

        var width = box[3] - box[1];
        var widths = items.Select(i => (double)(i.Box[3] - i.Box[1])).ToList();
        if (widths.Max() / Math.Max(1, widths.Min()) > 1.15) return layout; // columns of different widths are not a simple grid
        for (var j = 0; j < k; j++)
        {
            var xs = rows.Select(r => r.OrderBy(i => i.Box[1]).ElementAt(j).Box[1]).ToList();
            if (xs.Max() - xs.Min() > 0.03 * width) return layout;
        }

        // One gap for both directions: the horizontal space between neighbours.
        var xs2 = rows.SelectMany(r => { var o = r.OrderBy(i => i.Box[1]).ToList(); return Enumerable.Range(1, o.Count - 1).Select(j => o[j].Box[1] - o[j - 1].Box[3]); }).Where(g => g > 0).ToList();
        double? gap = xs2.Count > 0 ? Math.Round(xs2.OrderBy(g => g).ElementAt(xs2.Count / 2) * s.X) : null;
        return layout with { Display = "grid", Columns = k, Direction = null, Gap = layout.Gap ?? gap };
    }

    private static string? VerticalAlign(List<int[]> boxes)
    {
        var tallest = boxes.Max(b => b[2] - b[0]);
        if (tallest <= 0) return null;
        // Equal tops look the same as equal centers when the items are equally tall, and top is the common case.
        if (boxes.Max(b => b[0]) - boxes.Min(b => b[0]) <= 0.1 * tallest) return "start";
        var centers = boxes.Select(b => (b[0] + b[2]) / 2.0).ToList();
        if (centers.Max() - centers.Min() <= 0.12 * tallest) return "center";
        if (boxes.Max(b => b[2]) - boxes.Min(b => b[2]) <= 0.1 * tallest) return "end";
        return null;
    }

    private static string? HorizontalAlign(List<int[]> boxes, int[] container)
    {
        var width = container[3] - container[1];
        var tolerance = 0.02 * width;
        var centers = boxes.Select(b => (b[1] + b[3]) / 2.0).ToList();
        if (centers.Max() - centers.Min() <= tolerance && Math.Abs(centers[0] - (container[1] + container[3]) / 2.0) <= 0.03 * width)
            return "center";
        if (boxes.Max(b => b[1]) - boxes.Min(b => b[1]) <= tolerance) return "start";
        if (boxes.Max(b => b[3]) - boxes.Min(b => b[3]) <= tolerance && container[3] - boxes[0][3] <= 0.06 * width) return "end";
        return null;
    }

    private static bool IsRow(List<Item> items)
    {
        var ordered = items.OrderBy(i => i.Box[1]).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var a = ordered[i - 1].Box;
            var b = ordered[i].Box;
            if (b[1] - a[3] < -3 || Overlap(a[0], a[2], b[0], b[2]) < 0.5) return false;
        }
        return true;
    }

    private static bool IsColumn(List<Item> items)
    {
        var ordered = items.OrderBy(i => i.Box[0]).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            var a = ordered[i - 1].Box;
            var b = ordered[i].Box;
            if (b[0] - a[2] < -3 || Overlap(a[1], a[3], b[1], b[3]) < 0.5) return false;
        }
        return true;
    }

    /// <summary>Shared length of two ranges, as a fraction of the shorter one.</summary>
    private static double Overlap(int a0, int a1, int b0, int b1)
    {
        var shared = Math.Min(a1, b1) - Math.Max(a0, b0);
        var shorter = Math.Min(a1 - a0, b1 - b0);
        return shorter <= 0 ? 0 : Math.Max(0, shared) / (double)shorter;
    }

    /// <summary>Siblings drawn on top of each other: the boxes of this container cannot be trusted.</summary>
    private static bool HeavyOverlap(List<Item> items)
    {
        for (var i = 0; i < items.Count; i++)
            for (var j = i + 1; j < items.Count; j++)
            {
                var a = items[i].Box;
                var b = items[j].Box;
                var w = Math.Min(a[3], b[3]) - Math.Max(a[1], b[1]);
                var h = Math.Min(a[2], b[2]) - Math.Max(a[0], b[0]);
                if (w <= 0 || h <= 0) continue;
                var smaller = Math.Min((a[3] - a[1]) * (a[2] - a[0]), (b[3] - b[1]) * (b[2] - b[0]));
                if (smaller > 0 && (double)w * h / smaller > 0.3) return true;
            }
        return false;
    }

    // ---------------------------------------------------------------- sizes, gaps, padding

    /// <summary>Gives a block its real width when the model gave none, so it does not shrink or stretch by accident.</summary>
    private static LayoutInfo FitWidth(DesignNode n, LayoutInfo layout, int[] box, Parent parent, Scale s, double wPx, double? hPx)
    {
        var parentWidth = parent.Box[3] - parent.Box[1];
        if (parentWidth < 50 || layout.Width != null) return layout;
        var ratio = (box[3] - box[1]) / (double)parentWidth;
        var wide = ratio >= 0.96;
        var column = parent.Direction != "row";

        if (Blocks.Contains(n.Type) || n.Type is "input" or "select" or "textarea")
        {
            if (wide) return layout with { Width = "100%" };
            return column
                ? layout with { Width = Px(wPx), MaxWidth = layout.MaxWidth ?? "100%" }
                : layout with { Width = Px(wPx) };
        }

        if (n.Type == "button")
            return wide ? layout with { Width = "100%" } : layout with { Width = Px(wPx) };

        // A paragraph keeps the width it wrapped at, so its lines break where they did in the design.
        if (n.Type is "text" or "heading" && column && !wide && hPx != null && layout.MaxWidth == null)
        {
            var size = FontSizePx(n.Style?.FontSize);
            if (size != null && hPx.Value / (size.Value * 1.3) >= 1.8)
                return layout with { MaxWidth = Px(wPx * 1.05) };
        }
        return layout;
    }

    private static double? FontSizePx(string? value)
    {
        if (value == null || !value.EndsWith("px")) return null;
        return double.TryParse(value[..^2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? v : null;
    }

    private static bool IsVisibleBox(DesignNode n) =>
        Containers.Contains(n.Type)
        && (n.Type == "card"
            || !string.IsNullOrWhiteSpace(n.Style?.Background)
            || !string.IsNullOrWhiteSpace(n.Style?.Border)
            || !string.IsNullOrWhiteSpace(n.Style?.Shadow));

    private static List<Item> InFlowItems(IReadOnlyList<DesignNode> kids) =>
        kids.Where(k => k.Layout?.Position != "absolute" && (k.Layout?.Rotate ?? 0) == 0)
            .Select(k => AssetPlanner.TryBox(k.Box, out var kb) ? new Item(k, kb) : null)
            .Where(i => i != null).Select(i => i!).ToList();

    private static double[]? MeasurePadding(int[] box, List<Item> items, double px, double py)
    {
        if (items.Count == 0) return null;
        var top = (items.Min(i => i.Box[0]) - box[0]) * py;
        var left = (items.Min(i => i.Box[1]) - box[1]) * px;
        var bottom = (box[2] - items.Max(i => i.Box[2])) * py;
        var right = (box[3] - items.Max(i => i.Box[3])) * px;

        double Clean(double v) => Math.Round(Math.Clamp(v, 0, 160));
        double[] result = [Clean(top), Clean(right), Clean(bottom), Clean(left)];
        if (result.All(v => v < 3)) return null;
        if (Math.Abs(result[1] - result[3]) < 4) result[1] = result[3] = Math.Round((result[1] + result[3]) / 2);
        return result;
    }

    /// <summary>The space before each in-flow child, in design pixels, in the order they appear in the page.</summary>
    private sealed record Spaces(List<double> Values, Dictionary<DesignNode, double> Margins)
    {
        public double Min() => Values.Min();
        public double Max() => Values.Max();
        public double Median() { var v = Values.OrderBy(x => x).ToList(); return v[v.Count / 2]; }
    }

    private static Spaces? MeasureSpaces(DesignNode n, List<Item> items, string direction, Scale s)
    {
        var row = direction == "row";
        if (items.Count < 2 || (!row && s.Y == null)) return null;

        // The children must appear in the page in the same order as in the code, or margins would land on the wrong child.
        var inCode = NodeRules.Kids(n).Select(k => items.FirstOrDefault(i => ReferenceEquals(i.Node, k))).Where(i => i != null).Select(i => i!).ToList();
        var start = (Item i) => row ? i.Box[1] : i.Box[0];
        var end = (Item i) => row ? i.Box[3] : i.Box[2];
        var unit = row ? s.X : s.Y!.Value;

        var values = new List<double>();
        var margins = new Dictionary<DesignNode, double>(ReferenceEqualityComparer.Instance);
        for (var i = 1; i < inCode.Count; i++)
        {
            var units = start(inCode[i]) - end(inCode[i - 1]);
            if (units < 0) return null; // out of order or overlapping: do not guess
            var px = units * unit;
            values.Add(px);
            margins[inCode[i].Node] = px;
        }
        return values.Count == 0 || values.Max() > 400 ? null : new Spaces(values, margins);
    }

    private static string Px(double v) => $"{(int)Math.Round(v)}px";
}
