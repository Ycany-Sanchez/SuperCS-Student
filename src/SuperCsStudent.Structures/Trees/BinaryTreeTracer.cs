using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed class BinaryTreeTracer
{
    private sealed class BinaryNode
    {
        public string Id { get; }
        public string Label { get; set; }
        public BinaryNode? Left { get; set; }
        public BinaryNode? Right { get; set; }
        public BinaryNode? Parent { get; set; }

        public BinaryNode(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }

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
        "BreadthFirst(root):",
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

    private BinaryNode? _root;
    private int _nextId = 1;

    public TreeRuleset Ruleset { get; set; }
    public int Count => CountNodes(_root);
    public string? RootId => _root?.Id;
    public string? RootLabel => _root?.Label;

    public BinaryTreeTracer(TreeRuleset? ruleset = null)
    {
        Ruleset = ruleset ?? TreeRuleset.CourseStandard;
    }

    public void Clear()
    {
        _root = null;
        _nextId = 1;
    }

    public string? FindNodeIdByLabel(string label) => FindNode(_root, label)?.Id;

    public int GetHeight() => ComputeHeight(_root, Ruleset.HeightConvention);

    public bool VerifyParentLinks()
    {
        if (_root is null) return true;
        if (_root.Parent is not null) return false;
        return VerifySubtreeParents(_root);
    }

    public TraceTimeline<TreeStepState> AddRoot(string label)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var pseudocode = new[]
        {
            "AddRoot(value):",
            "  if root != null: reject",
            "  root = new Node(value)"
        };

        if (_root is not null)
        {
            steps.Add(MakeStep(0, "Root Already Exists",
                $"Cannot add root '{label}' because root '{_root.Label}' already exists.",
                1, new[] { _root.Id }, null, "REJECT", "Error",
                new Dictionary<string, string> { [_root.Id] = SemanticStates.Violation }));
            ops.Add($"ADD_ROOT_FAILED {label}");
            return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Root already exists ({_root.Label}).", isSuccess: false);
        }

        _root = new BinaryNode($"bt_{_nextId++}", label);
        ops.Add($"ADD_ROOT {label}");
        steps.Add(MakeStep(0, $"Add Root {label}",
            $"Created root node '{label}'.",
            2, new[] { _root.Id }, null, "ADD_ROOT", "Root",
            new Dictionary<string, string> { [_root.Id] = SemanticStates.Accepted }));

        return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Added root '{label}'.", isSuccess: true);
    }

    public TraceTimeline<TreeStepState> AddLeft(string parentIdOrLabel, string label) =>
        AddChild(parentIdOrLabel, label, isLeft: true);

    public TraceTimeline<TreeStepState> AddRight(string parentIdOrLabel, string label) =>
        AddChild(parentIdOrLabel, label, isLeft: false);

    public TraceTimeline<TreeStepState> Remove(string nodeIdOrLabel)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var pseudocode = new[]
        {
            "Remove(node):",
            "  locate target node",
            "  if node has 2 children: reject",
            "  splice single child (or null) to parent"
        };

        var target = FindNode(_root, nodeIdOrLabel);
        if (target is null)
        {
            steps.Add(MakeStep(0, "Node Not Found",
                $"No node matching '{nodeIdOrLabel}' exists in the binary tree.",
                1, Array.Empty<string>(), null, "NOT_FOUND", "Miss"));
            ops.Add($"REMOVE_MISS {nodeIdOrLabel}");
            return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Node '{nodeIdOrLabel}' not found.", isSuccess: false);
        }

        steps.Add(MakeStep(steps.Count, $"Locate {target.Label}",
            $"Selected node '{target.Label}' for removal.",
            1, new[] { target.Id }, null, "LOCATE", "Target",
            new Dictionary<string, string> { [target.Id] = SemanticStates.Active }));

        if (target.Left is not null && target.Right is not null)
        {
            steps.Add(MakeStep(steps.Count, "Cannot Remove Internal Node With Two Children",
                $"Node '{target.Label}' has both left child '{target.Left.Label}' and right child '{target.Right.Label}'.",
                2, new[] { target.Id }, null, "REJECT", "2 Children",
                new Dictionary<string, string> { [target.Id] = SemanticStates.Violation }));
            ops.Add($"REMOVE_REJECTED {target.Label}");
            return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Cannot remove '{target.Label}' because it has two children.", isSuccess: false);
        }

        var child = target.Left ?? target.Right;
        var parent = target.Parent;
        if (parent is null)
        {
            _root = child;
            if (child is not null) child.Parent = null;
        }
        else if (parent.Left == target)
        {
            parent.Left = child;
            if (child is not null) child.Parent = parent;
        }
        else
        {
            parent.Right = child;
            if (child is not null) child.Parent = parent;
        }

        target.Parent = null;
        target.Left = null;
        target.Right = null;

        ops.Add($"REMOVE {target.Label}");
        var highlights = new List<string>();
        var states = new Dictionary<string, string>();
        if (child is not null)
        {
            highlights.Add(child.Id);
            states[child.Id] = SemanticStates.Accepted;
        }
        else if (parent is not null)
        {
            highlights.Add(parent.Id);
            states[parent.Id] = SemanticStates.Accepted;
        }

        steps.Add(MakeStep(steps.Count, $"Removed {target.Label}",
            child is not null
                ? $"Removed '{target.Label}' and promoted its child '{child.Label}'."
                : $"Removed leaf node '{target.Label}'.",
            3, highlights, null, "REMOVE", "Removed", states));

        return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Removed '{target.Label}'.", isSuccess: true);
    }

    public TraceTimeline<TreeStepState> TraceWalk(TreeTraversalMode mode) =>
        TreeTraversalTracer.TraceWalk(
            ToTraversalNode(_root),
            mode,
            (summary, overrides) => BuildSnapshot(summary, overrides));

    private static BinaryTraversalNode? ToTraversalNode(BinaryNode? n) =>
        n is null ? null : new BinaryTraversalNode(n.Id, n.Label, ToTraversalNode(n.Left), ToTraversalNode(n.Right));

    public TraceTimeline<TreeStepState> TracePreOrder()
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visited = new HashSet<string>();
        var order = new List<string>();

        void Dfs(BinaryNode? node)
        {
            if (node is null) return;
            order.Add(node.Label);
            ops.Add($"VISIT {node.Label}");

            var states = BuildVisitedMap(visited, node.Id);
            steps.Add(MakeStep(steps.Count, $"Preorder Visit {node.Label}",
                $"Visit '{node.Label}' before its subtrees. Sequence: [{string.Join(", ", order)}]",
                2, new[] { node.Id }, null, "PREORDER", $"#{order.Count}", states));

            visited.Add(node.Id);
            Dfs(node.Left);
            Dfs(node.Right);
        }

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty Tree", "Tree is empty; preorder sequence is [].", 1, Array.Empty<string>()));
        }
        else
        {
            Dfs(_root);
        }

        return new TraceTimeline<TreeStepState>(steps, PreOrderPseudocode, ops,
            $"Preorder: {string.Join(", ", order)}");
    }

    public TraceTimeline<TreeStepState> TraceInOrder()
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visited = new HashSet<string>();
        var order = new List<string>();

        void Dfs(BinaryNode? node)
        {
            if (node is null) return;
            Dfs(node.Left);

            order.Add(node.Label);
            ops.Add($"VISIT {node.Label}");
            var states = BuildVisitedMap(visited, node.Id);
            steps.Add(MakeStep(steps.Count, $"Inorder Visit {node.Label}",
                $"Visit '{node.Label}' after left subtree. Sequence: [{string.Join(", ", order)}]",
                3, new[] { node.Id }, null, "INORDER", $"#{order.Count}", states));

            visited.Add(node.Id);
            Dfs(node.Right);
        }

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty Tree", "Tree is empty; inorder sequence is [].", 1, Array.Empty<string>()));
        }
        else
        {
            Dfs(_root);
        }

        return new TraceTimeline<TreeStepState>(steps, InOrderPseudocode, ops,
            $"Inorder: {string.Join(", ", order)}");
    }

    public TraceTimeline<TreeStepState> TracePostOrder()
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visited = new HashSet<string>();
        var order = new List<string>();

        void Dfs(BinaryNode? node)
        {
            if (node is null) return;
            Dfs(node.Left);
            Dfs(node.Right);

            order.Add(node.Label);
            ops.Add($"VISIT {node.Label}");
            var states = BuildVisitedMap(visited, node.Id);
            steps.Add(MakeStep(steps.Count, $"Postorder Visit {node.Label}",
                $"Visit '{node.Label}' after both subtrees. Sequence: [{string.Join(", ", order)}]",
                4, new[] { node.Id }, null, "POSTORDER", $"#{order.Count}", states));

            visited.Add(node.Id);
        }

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty Tree", "Tree is empty; postorder sequence is [].", 1, Array.Empty<string>()));
        }
        else
        {
            Dfs(_root);
        }

        return new TraceTimeline<TreeStepState>(steps, PostOrderPseudocode, ops,
            $"Postorder: {string.Join(", ", order)}");
    }

    public TraceTimeline<TreeStepState> TraceBreadthFirst()
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visited = new HashSet<string>();
        var order = new List<string>();

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty Tree", "Tree is empty; level-order sequence is [].", 1, Array.Empty<string>()));
            return new TraceTimeline<TreeStepState>(steps, BfsPseudocode, ops, "Level-order: (empty)");
        }

        var queue = new Queue<BinaryNode>();
        queue.Enqueue(_root);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            order.Add(cur.Label);
            ops.Add($"VISIT {cur.Label}");

            var states = BuildVisitedMap(visited, cur.Id);
            foreach (var queued in queue)
            {
                if (!states.ContainsKey(queued.Id))
                {
                    states[queued.Id] = SemanticStates.Frontier;
                }
            }

            if (cur.Left is not null)
            {
                queue.Enqueue(cur.Left);
                states[cur.Left.Id] = SemanticStates.Frontier;
            }
            if (cur.Right is not null)
            {
                queue.Enqueue(cur.Right);
                states[cur.Right.Id] = SemanticStates.Frontier;
            }

            steps.Add(MakeStep(steps.Count, $"Level-Order Visit {cur.Label}",
                $"Dequeued '{cur.Label}'. Queue: [{string.Join(", ", queue.Select(n => n.Label))}]. Sequence: [{string.Join(", ", order)}]",
                4, new[] { cur.Id }, null, "BFS", $"#{order.Count}", states));

            visited.Add(cur.Id);
        }

        return new TraceTimeline<TreeStepState>(steps, BfsPseudocode, ops,
            $"Level-order: {string.Join(", ", order)}");
    }

    public TraceTimeline<TreeStepState> TraceEulerTour()
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var completed = new HashSet<string>();
        var eulerPath = new List<string>();

        void Tour(BinaryNode? node)
        {
            if (node is null) return;

            eulerPath.Add($"{node.Label}:L");
            ops.Add($"LEFT {node.Label}");
            steps.Add(MakeStep(steps.Count, $"Euler Left (Pre): {node.Label}",
                $"Entering '{node.Label}' from the left (preorder position). Tour length: {eulerPath.Count}.",
                1, new[] { node.Id }, null, "EULER_LEFT", "Pre", BuildVisitedMap(completed, node.Id)));

            if (node.Left is not null)
            {
                Tour(node.Left);
            }

            eulerPath.Add($"{node.Label}:B");
            ops.Add($"BELOW {node.Label}");
            steps.Add(MakeStep(steps.Count, $"Euler Below (In): {node.Label}",
                $"Passing below '{node.Label}' between subtrees (inorder position). Tour length: {eulerPath.Count}.",
                3, new[] { node.Id }, null, "EULER_BELOW", "In", BuildVisitedMap(completed, node.Id)));

            if (node.Right is not null)
            {
                Tour(node.Right);
            }

            eulerPath.Add($"{node.Label}:R");
            ops.Add($"RIGHT {node.Label}");
            steps.Add(MakeStep(steps.Count, $"Euler Right (Post): {node.Label}",
                $"Exiting '{node.Label}' on the right (postorder position). Tour length: {eulerPath.Count}.",
                5, new[] { node.Id }, null, "EULER_RIGHT", "Post", BuildVisitedMap(completed, node.Id)));

            completed.Add(node.Id);
        }

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty Tree", "Tree is empty; Euler tour is [].", 0, Array.Empty<string>()));
        }
        else
        {
            Tour(_root);
        }

        return new TraceTimeline<TreeStepState>(steps, EulerTourPseudocode, ops,
            $"Euler Tour ({eulerPath.Count} visits): {string.Join(" -> ", eulerPath)}");
    }

    public TreeStepState BuildSnapshot(
        string summary = "Binary tree snapshot",
        IReadOnlyDictionary<string, string>? stateOverrides = null)
    {
        var layoutRoot = _root is null ? null : ToLayoutNode(_root, stateOverrides);
        var scene = DiagramLayout.LayoutTidyTree(layoutRoot, caption: summary);

        return new TreeStepState(
            Scene: scene,
            PreOrder: GetPreOrder(),
            InOrder: GetInOrder(),
            PostOrder: GetPostOrder(),
            LevelOrder: GetLevelOrder(),
            ParentLinksHealthy: VerifyParentLinks(),
            StatusSummary: summary);
    }

    public IReadOnlyList<string> GetPreOrder()
    {
        var list = new List<string>();
        void Walk(BinaryNode? n)
        {
            if (n is null) return;
            list.Add(n.Label);
            Walk(n.Left);
            Walk(n.Right);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetInOrder()
    {
        var list = new List<string>();
        void Walk(BinaryNode? n)
        {
            if (n is null) return;
            Walk(n.Left);
            list.Add(n.Label);
            Walk(n.Right);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetPostOrder()
    {
        var list = new List<string>();
        void Walk(BinaryNode? n)
        {
            if (n is null) return;
            Walk(n.Left);
            Walk(n.Right);
            list.Add(n.Label);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetLevelOrder()
    {
        var list = new List<string>();
        if (_root is null) return list;
        var q = new Queue<BinaryNode>();
        q.Enqueue(_root);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            list.Add(cur.Label);
            if (cur.Left is not null) q.Enqueue(cur.Left);
            if (cur.Right is not null) q.Enqueue(cur.Right);
        }
        return list;
    }

    private TraceTimeline<TreeStepState> AddChild(string parentIdOrLabel, string label, bool isLeft)
    {
        var sideName = isLeft ? "Left" : "Right";
        var opCode = isLeft ? "ADD_LEFT" : "ADD_RIGHT";
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var pseudocode = new[]
        {
            $"Add{sideName}(parent, value):",
            "  locate parent node",
            $"  if parent.{sideName} != null: reject",
            $"  parent.{sideName} = new Node(value)"
        };

        var parent = FindNode(_root, parentIdOrLabel);
        if (parent is null)
        {
            steps.Add(MakeStep(0, "Parent Not Found",
                $"Could not find parent node '{parentIdOrLabel}'.",
                1, Array.Empty<string>(), null, "NOT_FOUND", "Miss"));
            ops.Add($"{opCode}_MISS {parentIdOrLabel}");
            return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Parent '{parentIdOrLabel}' not found.", isSuccess: false);
        }

        var existing = isLeft ? parent.Left : parent.Right;
        if (existing is not null)
        {
            steps.Add(MakeStep(0, $"{sideName} Child Occupied",
                $"Node '{parent.Label}' already has {sideName.ToLowerInvariant()} child '{existing.Label}'.",
                2, new[] { parent.Id, existing.Id }, null, "REJECT", "Occupied",
                new Dictionary<string, string> { [existing.Id] = SemanticStates.Violation }));
            ops.Add($"{opCode}_REJECTED {parent.Label} {label}");
            return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"{parent.Label} already has a {sideName.ToLowerInvariant()} child.", isSuccess: false);
        }

        var node = new BinaryNode($"bt_{_nextId++}", label) { Parent = parent };
        if (isLeft) parent.Left = node;
        else parent.Right = node;

        ops.Add($"{opCode} {parent.Label} {label}");
        steps.Add(MakeStep(0, $"Add {sideName} Child {label}",
            $"Attached '{label}' as the {sideName.ToLowerInvariant()} child of '{parent.Label}'.",
            3, new[] { node.Id }, new[] { $"{parent.Id}->{node.Id}" }, opCode, sideName,
            new Dictionary<string, string>
            {
                [parent.Id] = SemanticStates.Visited,
                [node.Id] = SemanticStates.Accepted
            }));

        return new TraceTimeline<TreeStepState>(steps, pseudocode, ops, $"Added {sideName.ToLowerInvariant()} child '{label}' to '{parent.Label}'.", isSuccess: true);
    }

    private LayoutTreeNode ToLayoutNode(BinaryNode node, IReadOnlyDictionary<string, string>? stateOverrides)
    {
        var semantic = stateOverrides is not null && stateOverrides.TryGetValue(node.Id, out var s)
            ? s
            : SemanticStates.Default;

        var children = new List<LayoutTreeNode>();
        var edgeLabels = new List<string>();

        if (Ruleset.ShowExternalSentinels)
        {
            children.Add(node.Left is not null ? ToLayoutNode(node.Left, stateOverrides) : TreeLayoutHelpers.CreateSentinel(node.Id, "L"));
            edgeLabels.Add("L");
            children.Add(node.Right is not null ? ToLayoutNode(node.Right, stateOverrides) : TreeLayoutHelpers.CreateSentinel(node.Id, "R"));
            edgeLabels.Add("R");
        }
        else
        {
            var singleChild = (node.Left is null) ^ (node.Right is null);
            if (node.Left is not null)
            {
                children.Add(ToLayoutNode(node.Left, stateOverrides));
                edgeLabels.Add(singleChild ? "L" : "");
            }
            if (node.Right is not null)
            {
                children.Add(ToLayoutNode(node.Right, stateOverrides));
                edgeLabels.Add(singleChild ? "R" : "");
            }
        }

        return new LayoutTreeNode
        {
            Id = node.Id,
            Label = node.Label,
            Shape = NodeShapes.Circle,
            SemanticState = semantic,
            Children = children,
            ChildEdgeLabels = edgeLabels
        };
    }

    private TraceStep<TreeStepState> MakeStep(
        int stepIndex,
        string title,
        string explanation,
        int? activeLine,
        IReadOnlyList<string> highlightedNodes,
        IReadOnlyList<string>? highlightedEdges = null,
        string? opCode = null,
        string? badge = null,
        IReadOnlyDictionary<string, string>? stateOverrides = null)
    {
        var state = BuildSnapshot(title, stateOverrides);
        return new TraceStep<TreeStepState>(
            stepIndex,
            title,
            explanation,
            activeLine,
            state,
            highlightedNodes,
            highlightedEdges ?? Array.Empty<string>(),
            opCode,
            badge);
    }

    private static Dictionary<string, string> BuildVisitedMap(HashSet<string> visited, string activeId)
    {
        var map = new Dictionary<string, string>();
        foreach (var id in visited)
        {
            map[id] = SemanticStates.Visited;
        }
        map[activeId] = SemanticStates.Active;
        return map;
    }

    private static BinaryNode? FindNode(BinaryNode? node, string idOrLabel)
    {
        if (node is null) return null;
        if (string.Equals(node.Id, idOrLabel, StringComparison.Ordinal) ||
            string.Equals(node.Label, idOrLabel, StringComparison.Ordinal))
        {
            return node;
        }

        return FindNode(node.Left, idOrLabel) ?? FindNode(node.Right, idOrLabel);
    }

    private static int CountNodes(BinaryNode? node) =>
        node is null ? 0 : 1 + CountNodes(node.Left) + CountNodes(node.Right);

    private static int ComputeHeight(BinaryNode? node, HeightConvention convention)
    {
        if (node is null) return TreeLayoutHelpers.EmptyHeight(convention);
        if (node.Left is null && node.Right is null) return TreeLayoutHelpers.LeafHeight(convention);
        return 1 + Math.Max(ComputeHeight(node.Left, convention), ComputeHeight(node.Right, convention));
    }

    private static bool VerifySubtreeParents(BinaryNode node)
    {
        if (node.Left is not null && (node.Left.Parent != node || !VerifySubtreeParents(node.Left)))
            return false;
        if (node.Right is not null && (node.Right.Parent != node || !VerifySubtreeParents(node.Right)))
            return false;
        return true;
    }
}
