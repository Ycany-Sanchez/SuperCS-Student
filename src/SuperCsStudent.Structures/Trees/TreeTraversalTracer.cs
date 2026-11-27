using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Tracing;

namespace SuperCsStudent.Structures.Trees;

public enum TreeTraversalMode
{
    PreOrder,
    InOrder,
    PostOrder,
    LevelOrder,
    EulerTour
}

public sealed record BinaryTraversalNode(
    string Id,
    string Label,
    BinaryTraversalNode? Left,
    BinaryTraversalNode? Right);

/// <summary>
/// Unified step-by-step traversal walker for all binary-shaped trees (Binary Tree, BST, AVL,
/// Red-Black, and Splay). Emits explicit descend/visit/return steps with DiagramMotion so the UI
/// animates a smooth traveler cursor along actual tree edges from node A to node B.
/// </summary>
public static class TreeTraversalTracer
{
    private static readonly IReadOnlyList<string> PreOrderPseudocode =
    [
        "PreOrder(node):",
        "  if node == null: return",
        "  visit(node)",
        "  PreOrder(node.Left)",
        "  PreOrder(node.Right)"
    ];

    private static readonly IReadOnlyList<string> InOrderPseudocode =
    [
        "InOrder(node):",
        "  if node == null: return",
        "  InOrder(node.Left)",
        "  visit(node)",
        "  InOrder(node.Right)"
    ];

    private static readonly IReadOnlyList<string> PostOrderPseudocode =
    [
        "PostOrder(node):",
        "  if node == null: return",
        "  PostOrder(node.Left)",
        "  PostOrder(node.Right)",
        "  visit(node)"
    ];

    private static readonly IReadOnlyList<string> BfsPseudocode =
    [
        "LevelOrder(root):",
        "  if root == null: return",
        "  enqueue(root)",
        "  while queue not empty:",
        "    node = dequeue(); visit(node)",
        "    enqueue non-null Left, Right"
    ];

    private static readonly IReadOnlyList<string> EulerTourPseudocode =
    [
        "EulerTour(node):",
        "  visitLeft(node)   // pre-visit",
        "  if node.Left != null: EulerTour(node.Left)",
        "  visitBelow(node)  // in-visit",
        "  if node.Right != null: EulerTour(node.Right)",
        "  visitRight(node)  // post-visit"
    ];

    public static string DisplayName(TreeTraversalMode mode) => mode switch
    {
        TreeTraversalMode.PreOrder => "Pre-order",
        TreeTraversalMode.InOrder => "In-order",
        TreeTraversalMode.PostOrder => "Post-order",
        TreeTraversalMode.LevelOrder => "Level-order",
        TreeTraversalMode.EulerTour => "Euler Tour",
        _ => "Traverse"
    };

    public static TraceTimeline<TreeStepState> TraceWalk(
        BinaryTraversalNode? root,
        TreeTraversalMode mode,
        Func<string, IReadOnlyDictionary<string, string>?, TreeStepState> buildSnapshot,
        bool preserveNodeColors = false)
    {
        var pseudocode = mode switch
        {
            TreeTraversalMode.PreOrder => PreOrderPseudocode,
            TreeTraversalMode.InOrder => InOrderPseudocode,
            TreeTraversalMode.PostOrder => PostOrderPseudocode,
            TreeTraversalMode.LevelOrder => BfsPseudocode,
            _ => EulerTourPseudocode
        };

        if (root is null)
        {
            var emptySnap = buildSnapshot("Empty Tree", null);
            var emptyStep = new TraceStep<TreeStepState>(
                0,
                "Empty Tree",
                $"Tree is empty; {DisplayName(mode).ToLowerInvariant()} sequence is [].",
                1,
                emptySnap,
                Array.Empty<string>(),
                Array.Empty<string>(),
                "EMPTY");
            return new TraceTimeline<TreeStepState>(
                new[] { emptyStep },
                pseudocode,
                Array.Empty<string>(),
                $"{DisplayName(mode)}: (empty)");
        }

        return mode switch
        {
            TreeTraversalMode.LevelOrder => TraceLevelOrder(root, buildSnapshot, preserveNodeColors, pseudocode),
            TreeTraversalMode.EulerTour => TraceEulerTour(root, buildSnapshot, preserveNodeColors, pseudocode),
            _ => TraceRecursiveDfs(root, mode, buildSnapshot, preserveNodeColors, pseudocode)
        };
    }

    private static TraceTimeline<TreeStepState> TraceRecursiveDfs(
        BinaryTraversalNode root,
        TreeTraversalMode mode,
        Func<string, IReadOnlyDictionary<string, string>?, TreeStepState> buildSnapshot,
        bool preserveNodeColors,
        IReadOnlyList<string> pseudocode)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visitedOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderLabels = new List<string>();

        var modeName = DisplayName(mode);
        var opPrefix = mode switch
        {
            TreeTraversalMode.PreOrder => "PREORDER",
            TreeTraversalMode.InOrder => "INORDER",
            _ => "POSTORDER"
        };
        var visitLine = mode switch
        {
            TreeTraversalMode.PreOrder => 2,
            TreeTraversalMode.InOrder => 3,
            _ => 4
        };
        var leftLine = mode switch
        {
            TreeTraversalMode.PreOrder => 3,
            TreeTraversalMode.InOrder => 2,
            _ => 2
        };
        var rightLine = mode switch
        {
            TreeTraversalMode.PreOrder => 4,
            TreeTraversalMode.InOrder => 4,
            _ => 3
        };

        void EmitStep(
            string title,
            string explanation,
            int activeLine,
            string focusNodeId,
            bool isVisit,
            DiagramMotion motion,
            string opCode,
            string? badge = null,
            (string From, string To)? activeEdge = null)
        {
            IReadOnlyDictionary<string, string>? overrides = null;
            if (!preserveNodeColors)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var vid in visitedOrder.Keys)
                {
                    map[vid] = SemanticStates.Visited;
                }
                map[focusNodeId] = isVisit ? SemanticStates.Active : SemanticStates.Frontier;
                overrides = map;
            }

            var rawState = buildSnapshot(title, overrides);
            var decorated = DecorateState(rawState, visitedOrder, motion, activeEdge, isVisit);
            var highlightedEdges = activeEdge is { } e
                ? new[] { $"{e.From}->{e.To}", $"{e.To}->{e.From}" }
                : Array.Empty<string>();

            steps.Add(new TraceStep<TreeStepState>(
                steps.Count,
                title,
                explanation,
                activeLine,
                decorated,
                new[] { focusNodeId },
                highlightedEdges,
                opCode,
                badge));
        }

        void VisitNode(BinaryTraversalNode node)
        {
            orderLabels.Add(node.Label);
            visitedOrder[node.Id] = orderLabels.Count;
            ops.Add($"VISIT {node.Label}");

            var positionHint = mode switch
            {
                TreeTraversalMode.PreOrder => "before its subtrees",
                TreeTraversalMode.InOrder => "after its left subtree",
                _ => "after both subtrees"
            };

            EmitStep(
                $"{modeName} Visit {node.Label}",
                $"Visit '{node.Label}' {positionHint}. Sequence: [{string.Join(", ", orderLabels)}]",
                visitLine,
                node.Id,
                isVisit: true,
                new DiagramMotion(node.Id, node.Id, "visit", "var(--warning)"),
                opPrefix,
                $"#{orderLabels.Count}");
        }

        void Walk(BinaryTraversalNode node, bool isRoot)
        {
            // For In-order / Post-order with children, announce entering the root before descending
            if (isRoot && mode != TreeTraversalMode.PreOrder && (node.Left is not null || node.Right is not null))
            {
                ops.Add($"ENTER {node.Label}");
                EmitStep(
                    $"Start at Root {node.Label}",
                    $"Begin {modeName.ToLowerInvariant()} traversal at root '{node.Label}' and descend into subtrees before visiting.",
                    0,
                    node.Id,
                    isVisit: false,
                    new DiagramMotion(node.Id, node.Id, "descend", "var(--accent)"),
                    "ENTER",
                    "Start");
            }

            if (mode == TreeTraversalMode.PreOrder)
            {
                VisitNode(node);
            }

            if (node.Left is not null)
            {
                ops.Add($"DESCEND {node.Label} -> {node.Left.Label}");
                EmitStep(
                    $"Descend Left: {node.Label} → {node.Left.Label}",
                    $"Move down left branch from '{node.Label}' to '{node.Left.Label}'.",
                    leftLine,
                    node.Left.Id,
                    isVisit: false,
                    new DiagramMotion(node.Id, node.Left.Id, "descend", "var(--accent)"),
                    "DESCEND_L",
                    "Left",
                    (node.Id, node.Left.Id));

                Walk(node.Left, isRoot: false);

                ops.Add($"RETURN {node.Left.Label} -> {node.Label}");
                EmitStep(
                    $"Return to {node.Label}",
                    $"Finished left subtree of '{node.Label}'; returning up from '{node.Left.Label}'.",
                    leftLine,
                    node.Id,
                    isVisit: false,
                    new DiagramMotion(node.Left.Id, node.Id, "return", "var(--accent)"),
                    "RETURN",
                    "Up",
                    (node.Id, node.Left.Id));
            }

            if (mode == TreeTraversalMode.InOrder)
            {
                VisitNode(node);
            }

            if (node.Right is not null)
            {
                ops.Add($"DESCEND {node.Label} -> {node.Right.Label}");
                EmitStep(
                    $"Descend Right: {node.Label} → {node.Right.Label}",
                    $"Move down right branch from '{node.Label}' to '{node.Right.Label}'.",
                    rightLine,
                    node.Right.Id,
                    isVisit: false,
                    new DiagramMotion(node.Id, node.Right.Id, "descend", "var(--accent)"),
                    "DESCEND_R",
                    "Right",
                    (node.Id, node.Right.Id));

                Walk(node.Right, isRoot: false);

                ops.Add($"RETURN {node.Right.Label} -> {node.Label}");
                EmitStep(
                    $"Return to {node.Label}",
                    $"Finished right subtree of '{node.Label}'; returning up from '{node.Right.Label}'.",
                    rightLine,
                    node.Id,
                    isVisit: false,
                    new DiagramMotion(node.Right.Id, node.Id, "return", "var(--accent)"),
                    "RETURN",
                    "Up",
                    (node.Id, node.Right.Id));
            }

            if (mode == TreeTraversalMode.PostOrder)
            {
                VisitNode(node);
            }
        }

        Walk(root, isRoot: true);

        return new TraceTimeline<TreeStepState>(
            steps,
            pseudocode,
            ops,
            $"{modeName}: {string.Join(", ", orderLabels)}");
    }

    private static TraceTimeline<TreeStepState> TraceLevelOrder(
        BinaryTraversalNode root,
        Func<string, IReadOnlyDictionary<string, string>?, TreeStepState> buildSnapshot,
        bool preserveNodeColors,
        IReadOnlyList<string> pseudocode)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visitedOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        var orderLabels = new List<string>();
        var queue = new Queue<BinaryTraversalNode>();
        queue.Enqueue(root);
        string? prevVisitedId = null;

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            orderLabels.Add(cur.Label);
            visitedOrder[cur.Id] = orderLabels.Count;
            ops.Add($"VISIT {cur.Label}");

            if (cur.Left is not null) queue.Enqueue(cur.Left);
            if (cur.Right is not null) queue.Enqueue(cur.Right);

            IReadOnlyDictionary<string, string>? overrides = null;
            if (!preserveNodeColors)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var vid in visitedOrder.Keys) map[vid] = SemanticStates.Visited;
                foreach (var q in queue)
                {
                    if (!map.ContainsKey(q.Id)) map[q.Id] = SemanticStates.Frontier;
                }
                map[cur.Id] = SemanticStates.Active;
                overrides = map;
            }

            var motion = prevVisitedId is null
                ? new DiagramMotion(cur.Id, cur.Id, "visit", "var(--warning)")
                : new DiagramMotion(prevVisitedId, cur.Id, "hop", "var(--warning)");

            var title = $"Level-Order Visit {cur.Label}";
            var rawState = buildSnapshot(title, overrides);
            var decorated = DecorateState(rawState, visitedOrder, motion, activeEdge: null, isVisit: true);

            steps.Add(new TraceStep<TreeStepState>(
                steps.Count,
                title,
                $"Dequeued '{cur.Label}'. Queue: [{string.Join(", ", queue.Select(n => n.Label))}]. Sequence: [{string.Join(", ", orderLabels)}]",
                4,
                decorated,
                new[] { cur.Id },
                Array.Empty<string>(),
                "BFS",
                $"#{orderLabels.Count}"));

            prevVisitedId = cur.Id;
        }

        return new TraceTimeline<TreeStepState>(
            steps,
            pseudocode,
            ops,
            $"Level-order: {string.Join(", ", orderLabels)}");
    }

    private static TraceTimeline<TreeStepState> TraceEulerTour(
        BinaryTraversalNode root,
        Func<string, IReadOnlyDictionary<string, string>?, TreeStepState> buildSnapshot,
        bool preserveNodeColors,
        IReadOnlyList<string> pseudocode)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var completedOrder = new Dictionary<string, int>(StringComparer.Ordinal);
        var eulerPath = new List<string>();
        string? prevNodeId = null;

        void EmitEuler(
            BinaryTraversalNode node,
            string title,
            string explanation,
            int line,
            string opCode,
            string badge,
            string motionKind,
            (string From, string To)? edge)
        {
            IReadOnlyDictionary<string, string>? overrides = null;
            if (!preserveNodeColors)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var cid in completedOrder.Keys) map[cid] = SemanticStates.Visited;
                map[node.Id] = SemanticStates.Active;
                overrides = map;
            }

            var fromId = prevNodeId ?? node.Id;
            var motion = new DiagramMotion(fromId, node.Id, fromId == node.Id ? "visit" : motionKind, "var(--warning)");
            var rawState = buildSnapshot(title, overrides);
            var decorated = DecorateState(rawState, completedOrder, motion, edge, isVisit: fromId == node.Id);

            steps.Add(new TraceStep<TreeStepState>(
                steps.Count,
                title,
                explanation,
                line,
                decorated,
                new[] { node.Id },
                Array.Empty<string>(),
                opCode,
                badge));

            prevNodeId = node.Id;
        }

        void Tour(BinaryTraversalNode node, BinaryTraversalNode? parent)
        {
            eulerPath.Add($"{node.Label}:L");
            ops.Add($"LEFT {node.Label}");
            EmitEuler(
                node,
                $"Euler Left (Pre): {node.Label}",
                $"Entering '{node.Label}' from the left (pre-order position). Tour length: {eulerPath.Count}.",
                1,
                "EULER_LEFT",
                "Pre",
                "descend",
                parent is not null ? (parent.Id, node.Id) : null);

            if (node.Left is not null)
            {
                Tour(node.Left, node);
            }

            eulerPath.Add($"{node.Label}:B");
            ops.Add($"BELOW {node.Label}");
            EmitEuler(
                node,
                $"Euler Below (In): {node.Label}",
                $"Passing below '{node.Label}' between subtrees (in-order position). Tour length: {eulerPath.Count}.",
                3,
                "EULER_BELOW",
                "In",
                "return",
                node.Left is not null ? (node.Id, node.Left.Id) : null);

            if (node.Right is not null)
            {
                Tour(node.Right, node);
            }

            completedOrder[node.Id] = completedOrder.Count + 1;
            eulerPath.Add($"{node.Label}:R");
            ops.Add($"RIGHT {node.Label}");
            EmitEuler(
                node,
                $"Euler Right (Post): {node.Label}",
                $"Exiting '{node.Label}' on the right (post-order position). Tour length: {eulerPath.Count}.",
                5,
                "EULER_RIGHT",
                "Post",
                "return",
                node.Right is not null ? (node.Id, node.Right.Id) : null);
        }

        Tour(root, parent: null);

        return new TraceTimeline<TreeStepState>(
            steps,
            pseudocode,
            ops,
            $"Euler Tour ({eulerPath.Count} visits): {string.Join(" -> ", eulerPath)}");
    }

    private static TreeStepState DecorateState(
        TreeStepState rawState,
        IReadOnlyDictionary<string, int> visitedOrder,
        DiagramMotion motion,
        (string From, string To)? activeEdge,
        bool isVisit)
    {
        var nodes = rawState.Scene.Nodes.Select(n =>
        {
            if (n.Shape != NodeShapes.Sentinel && visitedOrder.TryGetValue(n.Id, out var ord))
            {
                return n with { SubLabel = $"#{ord}" };
            }
            return n;
        }).ToList();

        var edges = rawState.Scene.Edges.Select(e =>
        {
            if (activeEdge is { } ae &&
                ((e.SourceId == ae.From && e.TargetId == ae.To) ||
                 (e.SourceId == ae.To && e.TargetId == ae.From)))
            {
                return e with { SemanticState = isVisit ? SemanticStates.Active : SemanticStates.Frontier };
            }
            return e;
        }).ToList();

        var scene = rawState.Scene with
        {
            Nodes = nodes,
            Edges = edges,
            ActiveMotion = motion
        };

        return rawState with { Scene = scene };
    }
}
