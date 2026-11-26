using SuperCsStudent.Core.Diagram;

namespace SuperCsStudent.Core.Layout;

public sealed record GraphNodeInput(
    string Id,
    string Label,
    string Shape = NodeShapes.Circle,
    string SemanticState = SemanticStates.Default,
    string? SubLabel = null,
    string? Badge = null,
    bool IsStart = false,
    bool IsAccepting = false,
    bool IsDimmed = false,
    double Width = 52,
    double Height = 52);

public sealed record GraphEdgeInput(
    string Id,
    string SourceId,
    string TargetId,
    string Label = "",
    bool IsDirected = true,
    string SemanticState = SemanticStates.Default,
    bool IsDashed = false,
    bool IsDimmed = false);

public sealed class LayoutTreeNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Shape { get; init; } = NodeShapes.Circle;
    public string SemanticState { get; init; } = SemanticStates.Default;
    public string? SubLabel { get; init; }
    public string? Badge { get; init; }
    public bool IsDimmed { get; init; }
    public double Width { get; init; } = 46;
    public double Height { get; init; } = 46;
    public IReadOnlyList<string>? MultiKeys { get; init; }
    public List<LayoutTreeNode> Children { get; init; } = new();
    public List<string>? ChildEdgeLabels { get; init; }
}

public static class DiagramLayout
{
    /// <summary>
    /// Computes clean layered horizontal coordinates for finite automata, TM schemas, and directed graphs,
    /// with automatic Bezier curvature separation for antiparallel edges and self-loops.
    /// </summary>
    public static DiagramScene LayoutStateGraph(
        IReadOnlyList<GraphNodeInput> nodes,
        IReadOnlyList<GraphEdgeInput> edges,
        string? startNodeId = null,
        bool preferCircular = false,
        string? caption = null,
        string? overlayTag = null)
    {
        if (nodes.Count == 0)
        {
            return new DiagramScene(640, 360, Array.Empty<DiagramNode>(), Array.Empty<DiagramEdge>(), caption, overlayTag);
        }

        var positions = new Dictionary<string, (double X, double Y)>();

        if (preferCircular || nodes.Count <= 2)
        {
            PlaceCircular(nodes, positions, out var w, out var h);
            return BuildScene(nodes, edges, positions, w, h, caption, overlayTag);
        }

        // BFS layering from start node (or first node)
        var adj = nodes.ToDictionary(n => n.Id, _ => new List<string>());
        foreach (var e in edges)
        {
            if (adj.TryGetValue(e.SourceId, out var list) && e.TargetId != e.SourceId)
            {
                list.Add(e.TargetId);
            }
        }

        var layers = new Dictionary<string, int>();
        var queue = new Queue<string>();
        var rootId = startNodeId ?? nodes.FirstOrDefault(n => n.IsStart)?.Id ?? nodes[0].Id;

        void BfsFrom(string seed, int baseLayer)
        {
            if (layers.ContainsKey(seed)) return;
            layers[seed] = baseLayer;
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                var curLayer = layers[cur];
                foreach (var nxt in adj[cur])
                {
                    if (!layers.ContainsKey(nxt))
                    {
                        layers[nxt] = curLayer + 1;
                        queue.Enqueue(nxt);
                    }
                }
            }
        }

        BfsFrom(rootId, 0);
        foreach (var n in nodes)
        {
            if (!layers.ContainsKey(n.Id))
            {
                var nextBase = layers.Count == 0 ? 0 : layers.Values.Max() + 1;
                BfsFrom(n.Id, nextBase);
            }
        }

        var grouped = layers
            .GroupBy(kv => kv.Value)
            .OrderBy(g => g.Key)
            .Select(g => g.Select(kv => kv.Key).ToList())
            .ToList();

        const double marginX = 96;
        const double marginY = 86;
        const double layerGap = 158;
        const double rowGap = 116;

        var maxRows = grouped.Max(g => g.Count);
        var totalHeight = System.Math.Max(340, marginY * 2 + (maxRows - 1) * rowGap);
        var totalWidth = System.Math.Max(680, marginX * 2 + (grouped.Count - 1) * layerGap);

        for (var col = 0; col < grouped.Count; col++)
        {
            var colIds = grouped[col];
            var colSpan = (colIds.Count - 1) * rowGap;
            var startY = (totalHeight - colSpan) / 2.0;
            var x = marginX + col * layerGap;
            for (var r = 0; r < colIds.Count; r++)
            {
                positions[colIds[r]] = (x, startY + r * rowGap);
            }
        }

        return BuildScene(nodes, edges, positions, totalWidth, totalHeight, caption, overlayTag);
    }

    private static void PlaceCircular(
        IReadOnlyList<GraphNodeInput> nodes,
        Dictionary<string, (double X, double Y)> positions,
        out double width,
        out double height)
    {
        var count = nodes.Count;
        var radius = System.Math.Max(120, count * 28);
        width = radius * 2 + 220;
        height = radius * 2 + 180;
        var cx = width / 2.0;
        var cy = height / 2.0;

        for (var i = 0; i < count; i++)
        {
            var angle = -System.Math.PI / 2.0 + (2.0 * System.Math.PI * i / count);
            positions[nodes[i].Id] = (
                cx + radius * System.Math.Cos(angle),
                cy + radius * System.Math.Sin(angle));
        }
    }

    private static DiagramScene BuildScene(
        IReadOnlyList<GraphNodeInput> nodes,
        IReadOnlyList<GraphEdgeInput> edges,
        Dictionary<string, (double X, double Y)> positions,
        double width,
        double height,
        string? caption,
        string? overlayTag)
    {
        var diagramNodes = nodes.Select(n =>
        {
            var (x, y) = positions[n.Id];
            return new DiagramNode(
                n.Id,
                n.Label,
                System.Math.Round(x, 1),
                System.Math.Round(y, 1),
                n.Width,
                n.Height,
                n.Shape,
                n.SemanticState,
                n.SubLabel,
                n.Badge,
                n.IsStart,
                n.IsAccepting,
                n.IsDimmed);
        }).ToList();

        var directedPairs = new HashSet<(string, string)>(edges.Select(e => (e.SourceId, e.TargetId)));
        var diagramEdges = new List<DiagramEdge>(edges.Count);

        foreach (var e in edges)
        {
            double curve = 0;
            if (e.SourceId == e.TargetId)
            {
                curve = 42;
            }
            else if (e.IsDirected && directedPairs.Contains((e.TargetId, e.SourceId)))
            {
                curve = 28;
            }
            else if (positions.TryGetValue(e.SourceId, out var p1) && positions.TryGetValue(e.TargetId, out var p2))
            {
                // Slight arc for long skip edges across layers so they don't cut through intermediate nodes
                if (System.Math.Abs(p1.Y - p2.Y) < 1 && System.Math.Abs(p1.X - p2.X) > 200)
                {
                    curve = p1.X < p2.X ? -38 : 44;
                }
            }

            diagramEdges.Add(new DiagramEdge(
                e.Id,
                e.SourceId,
                e.TargetId,
                e.Label,
                e.IsDirected,
                e.SemanticState,
                e.IsDashed,
                e.IsDimmed,
                curve));
        }

        return new DiagramScene(width, height, diagramNodes, diagramEdges, caption, overlayTag);
    }

    /// <summary>
    /// Computes zero-overlap hierarchical coordinates for binary trees, multi-key (2,4) trees, and CFG parse trees.
    /// </summary>
    public static DiagramScene LayoutTidyTree(
        LayoutTreeNode? root,
        double horizontalGap = 24,
        double levelGap = 84,
        string? caption = null,
        string? overlayTag = null)
    {
        if (root is null)
        {
            return new DiagramScene(640, 320, Array.Empty<DiagramNode>(), Array.Empty<DiagramEdge>(), caption, overlayTag);
        }

        var widths = new Dictionary<string, double>();
        double Measure(LayoutTreeNode node)
        {
            if (node.Children.Count == 0)
            {
                widths[node.Id] = node.Width;
                return node.Width;
            }

            double sum = 0;
            for (var i = 0; i < node.Children.Count; i++)
            {
                sum += Measure(node.Children[i]);
                if (i > 0) sum += horizontalGap;
            }

            var total = System.Math.Max(node.Width, sum);
            widths[node.Id] = total;
            return total;
        }

        var rootSpan = Measure(root);
        var diagramNodes = new List<DiagramNode>();
        var diagramEdges = new List<DiagramEdge>();
        double maxY = 0;

        const double padX = 56;
        const double padY = 56;

        void Place(LayoutTreeNode node, double leftX, int depth)
        {
            var span = widths[node.Id];
            var cx = leftX + span / 2.0;
            var cy = padY + depth * levelGap;
            maxY = System.Math.Max(maxY, cy + node.Height / 2.0);

            diagramNodes.Add(new DiagramNode(
                node.Id,
                node.Label,
                System.Math.Round(cx, 1),
                System.Math.Round(cy, 1),
                node.Width,
                node.Height,
                node.Shape,
                node.SemanticState,
                node.SubLabel,
                node.Badge,
                false,
                false,
                node.IsDimmed,
                node.MultiKeys));

            if (node.Children.Count == 0) return;

            double childrenSpan = 0;
            for (var i = 0; i < node.Children.Count; i++)
            {
                childrenSpan += widths[node.Children[i].Id];
                if (i > 0) childrenSpan += horizontalGap;
            }

            var cursor = leftX + (span - childrenSpan) / 2.0;
            for (var i = 0; i < node.Children.Count; i++)
            {
                var child = node.Children[i];
                var childSpan = widths[child.Id];
                var edgeLabel = node.ChildEdgeLabels is not null && i < node.ChildEdgeLabels.Count
                    ? node.ChildEdgeLabels[i]
                    : string.Empty;

                diagramEdges.Add(new DiagramEdge(
                    $"{node.Id}->{child.Id}",
                    node.Id,
                    child.Id,
                    edgeLabel,
                    IsDirected: false,
                    SemanticState: child.SemanticState == SemanticStates.Active ? SemanticStates.Active : SemanticStates.Default,
                    IsDashed: child.Shape == NodeShapes.Sentinel,
                    IsDimmed: node.IsDimmed || child.IsDimmed));

                Place(child, cursor, depth + 1);
                cursor += childSpan + horizontalGap;
            }
        }

        Place(root, padX, 0);

        var totalWidth = System.Math.Max(640, rootSpan + padX * 2);
        var totalHeight = System.Math.Max(320, maxY + padY);
        return new DiagramScene(totalWidth, totalHeight, diagramNodes, diagramEdges, caption, overlayTag);
    }
}
