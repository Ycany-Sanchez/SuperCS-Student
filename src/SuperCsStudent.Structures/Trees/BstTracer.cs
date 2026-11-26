using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed class BstTracer
{
    private sealed class BstNode
    {
        public string Id { get; }
        public int Key { get; set; }
        public BstNode? Left { get; set; }
        public BstNode? Right { get; set; }
        public BstNode? Parent { get; set; }

        public BstNode(string id, int key)
        {
            Id = id;
            Key = key;
        }
    }

    private static readonly IReadOnlyList<string> SearchPseudocode =
    [
        "Search(node, key):",
        "  while node != null:",
        "    if key == node.Key: return FOUND",
        "    node = (key < node.Key) ? node.Left : node.Right",
        "  return NOT_FOUND"
    ];

    private static readonly IReadOnlyList<string> InsertPseudocode =
    [
        "Insert(key):",
        "  if root == null: root = new Node(key); return",
        "  walk down comparing key with cur.Key",
        "  if duplicate key: reject",
        "  attach new Node(key) as Left or Right leaf"
    ];

    private static readonly IReadOnlyList<string> DeletePseudocode =
    [
        "Delete(key):",
        "  locate target node with key",
        "  if target has 2 children:",
        "    replace target.Key with replacement (Successor/Predecessor)",
        "    target = replacement node",
        "  splice target's single child (or null) to target.Parent"
    ];

    private BstNode? _root;
    private int _nextId = 1;

    public TreeRuleset Ruleset { get; set; }
    public int Count => CountNodes(_root);
    public int? RootKey => _root?.Key;

    public BstTracer(TreeRuleset? ruleset = null)
    {
        Ruleset = ruleset ?? TreeRuleset.CourseStandard;
    }

    public void Clear()
    {
        _root = null;
        _nextId = 1;
    }

    public int GetHeight() => ComputeHeight(_root, Ruleset.HeightConvention);

    public bool VerifyParentLinks()
    {
        if (_root is null) return true;
        if (_root.Parent is not null) return false;
        return VerifySubtreeParents(_root);
    }

    public bool VerifyBstInvariants()
    {
        if (!VerifyParentLinks()) return false;
        return VerifyBstRange(_root, null, null);
    }

    public TraceTimeline<TreeStepState> Search(int key)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteSearch(key, steps, ops, out var found);
        return new TraceTimeline<TreeStepState>(
            steps,
            SearchPseudocode,
            ops,
            found ? $"Found key {key}." : $"Key {key} not found.",
            isSuccess: found);
    }

    public TraceTimeline<TreeStepState> Insert(int key)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteInsert(key, steps, ops, out var inserted);
        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
            ops,
            inserted ? $"Inserted {key}." : $"Duplicate key {key} ignored.",
            isSuccess: inserted);
    }

    public TraceTimeline<TreeStepState> Delete(int key)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteDelete(key, steps, ops, out var deleted);
        return new TraceTimeline<TreeStepState>(
            steps,
            DeletePseudocode,
            ops,
            deleted ? $"Deleted {key}." : $"Key {key} not found.",
            isSuccess: deleted);
    }

    public TraceTimeline<TreeStepState> RunScript(string script)
    {
        var commands = TreeScriptParser.Parse(script);
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var allOk = true;

        foreach (var cmd in commands)
        {
            switch (cmd.Kind)
            {
                case TreeCommandKind.Clear:
                    Clear();
                    ops.Add("CLEAR");
                    steps.Add(MakeStep(steps.Count, "Clear Tree", "Reset BST to empty.", null, Array.Empty<string>(), opCode: "CLEAR"));
                    break;
                case TreeCommandKind.Insert when cmd.Key.HasValue:
                    ExecuteInsert(cmd.Key.Value, steps, ops, out var insOk);
                    allOk &= insOk;
                    break;
                case TreeCommandKind.Delete when cmd.Key.HasValue:
                    ExecuteDelete(cmd.Key.Value, steps, ops, out var delOk);
                    allOk &= delOk;
                    break;
                case TreeCommandKind.Search when cmd.Key.HasValue:
                    ExecuteSearch(cmd.Key.Value, steps, ops, out var srchOk);
                    allOk &= srchOk;
                    break;
            }
        }

        if (steps.Count == 0)
        {
            steps.Add(MakeStep(0, "No Operations", "No valid BST commands parsed.", null, Array.Empty<string>()));
        }

        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
            ops,
            $"Executed {commands.Count} BST operation(s). Nodes: {Count}.",
            isSuccess: allOk);
    }

    public TreeStepState BuildSnapshot(
        string summary = "BST snapshot",
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
        void Walk(BstNode? n)
        {
            if (n is null) return;
            list.Add(n.Key.ToString());
            Walk(n.Left);
            Walk(n.Right);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetInOrder()
    {
        var list = new List<string>();
        void Walk(BstNode? n)
        {
            if (n is null) return;
            Walk(n.Left);
            list.Add(n.Key.ToString());
            Walk(n.Right);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetPostOrder()
    {
        var list = new List<string>();
        void Walk(BstNode? n)
        {
            if (n is null) return;
            Walk(n.Left);
            Walk(n.Right);
            list.Add(n.Key.ToString());
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetLevelOrder()
    {
        var list = new List<string>();
        if (_root is null) return list;
        var q = new Queue<BstNode>();
        q.Enqueue(_root);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            list.Add(cur.Key.ToString());
            if (cur.Left is not null) q.Enqueue(cur.Left);
            if (cur.Right is not null) q.Enqueue(cur.Right);
        }
        return list;
    }

    public TraceTimeline<TreeStepState> TracePreOrder() => TraceDfsTraversal("Preorder", mode: 0);
    public TraceTimeline<TreeStepState> TraceInOrder() => TraceDfsTraversal("Inorder", mode: 1);
    public TraceTimeline<TreeStepState> TracePostOrder() => TraceDfsTraversal("Postorder", mode: 2);

    public TraceTimeline<TreeStepState> TraceBreadthFirst()
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visited = new HashSet<string>();
        var order = new List<string>();

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty BST", "Tree is empty.", null, Array.Empty<string>()));
            return new TraceTimeline<TreeStepState>(steps, SearchPseudocode, ops, "Level-order: (empty)");
        }

        var q = new Queue<BstNode>();
        q.Enqueue(_root);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            order.Add(cur.Key.ToString());
            ops.Add($"VISIT {cur.Key}");

            var states = new Dictionary<string, string>();
            foreach (var id in visited) states[id] = SemanticStates.Visited;
            states[cur.Id] = SemanticStates.Active;

            if (cur.Left is not null) { q.Enqueue(cur.Left); states[cur.Left.Id] = SemanticStates.Frontier; }
            if (cur.Right is not null) { q.Enqueue(cur.Right); states[cur.Right.Id] = SemanticStates.Frontier; }

            steps.Add(MakeStep(steps.Count, $"Level-Order Visit {cur.Key}",
                $"Sequence: [{string.Join(", ", order)}]",
                null, new[] { cur.Id }, opCode: "BFS", badge: $"#{order.Count}", stateOverrides: states));
            visited.Add(cur.Id);
        }

        return new TraceTimeline<TreeStepState>(steps, SearchPseudocode, ops, $"Level-order: {string.Join(", ", order)}");
    }

    private TraceTimeline<TreeStepState> TraceDfsTraversal(string name, int mode)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        var visited = new HashSet<string>();
        var order = new List<string>();

        void EmitVisit(BstNode node)
        {
            order.Add(node.Key.ToString());
            ops.Add($"VISIT {node.Key}");
            var states = new Dictionary<string, string>();
            foreach (var id in visited) states[id] = SemanticStates.Visited;
            states[node.Id] = SemanticStates.Active;
            steps.Add(MakeStep(steps.Count, $"{name} Visit {node.Key}",
                $"Sequence: [{string.Join(", ", order)}]",
                null, new[] { node.Id }, opCode: name.ToUpperInvariant(), badge: $"#{order.Count}", stateOverrides: states));
            visited.Add(node.Id);
        }

        void Walk(BstNode? node)
        {
            if (node is null) return;
            if (mode == 0) EmitVisit(node);
            Walk(node.Left);
            if (mode == 1) EmitVisit(node);
            Walk(node.Right);
            if (mode == 2) EmitVisit(node);
        }

        if (_root is null)
        {
            steps.Add(MakeStep(0, "Empty BST", "Tree is empty.", null, Array.Empty<string>()));
        }
        else
        {
            Walk(_root);
        }

        return new TraceTimeline<TreeStepState>(steps, SearchPseudocode, ops, $"{name}: {string.Join(", ", order)}");
    }

    private void ExecuteSearch(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool found)
    {
        var visited = new Dictionary<string, string>();
        var cur = _root;
        while (cur is not null)
        {
            var states = new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Active };
            if (key == cur.Key)
            {
                states[cur.Id] = SemanticStates.Accepted;
                ops.Add($"SEARCH_HIT {key}");
                steps.Add(MakeStep(steps.Count, $"Found {key}",
                    $"Matched key {key} at node {cur.Key}.",
                    2, new[] { cur.Id }, opCode: "SEARCH_HIT", badge: "Found", stateOverrides: states));
                found = true;
                return;
            }

            var goLeft = key < cur.Key;
            steps.Add(MakeStep(steps.Count, $"Compare {key} vs {cur.Key}",
                $"{key} {(goLeft ? "<" : ">")} {cur.Key}: descend {(goLeft ? "left" : "right")}.",
                3, new[] { cur.Id }, opCode: "COMPARE", stateOverrides: states));
            visited[cur.Id] = SemanticStates.Visited;
            cur = goLeft ? cur.Left : cur.Right;
        }

        ops.Add($"SEARCH_MISS {key}");
        steps.Add(MakeStep(steps.Count, $"Key {key} Not Found",
            $"Reached external leaf; {key} is not in the BST.",
            4, Array.Empty<string>(), opCode: "SEARCH_MISS", badge: "Miss", stateOverrides: visited));
        found = false;
    }

    private void ExecuteInsert(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool inserted)
    {
        if (_root is null)
        {
            _root = new BstNode($"bst_{_nextId++}", key);
            ops.Add($"INSERT {key}");
            steps.Add(MakeStep(steps.Count, $"Insert Root {key}",
                $"Tree was empty; created root {key}.",
                1, new[] { _root.Id }, opCode: "INSERT", badge: "Root",
                stateOverrides: new Dictionary<string, string> { [_root.Id] = SemanticStates.Accepted }));
            inserted = true;
            return;
        }

        var visited = new Dictionary<string, string>();
        var cur = _root;
        BstNode parent = _root;
        var goLeft = false;

        while (cur is not null)
        {
            parent = cur;
            if (key == cur.Key)
            {
                var dupStates = new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Violation };
                ops.Add($"INSERT_DUPLICATE {key}");
                steps.Add(MakeStep(steps.Count, $"Duplicate Key {key}",
                    $"Key {key} already exists; BST enforces unique keys.",
                    3, new[] { cur.Id }, opCode: "DUPLICATE", badge: "Dup", stateOverrides: dupStates));
                inserted = false;
                return;
            }

            goLeft = key < cur.Key;
            visited[cur.Id] = SemanticStates.Visited;
            cur = goLeft ? cur.Left : cur.Right;
        }

        var node = new BstNode($"bst_{_nextId++}", key) { Parent = parent };
        if (goLeft) parent.Left = node;
        else parent.Right = node;

        var finalStates = new Dictionary<string, string>(visited) { [node.Id] = SemanticStates.Accepted };
        ops.Add($"INSERT {key}");
        steps.Add(MakeStep(steps.Count, $"Insert {key}",
            $"Inserted {key} as {(goLeft ? "left" : "right")} child of {parent.Key}.",
            4, new[] { node.Id }, new[] { $"{parent.Id}->{node.Id}" }, "INSERT", "New", finalStates));
        inserted = true;
    }

    private void ExecuteDelete(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool deleted)
    {
        var visited = new Dictionary<string, string>();
        var target = _root;
        while (target is not null && target.Key != key)
        {
            visited[target.Id] = SemanticStates.Visited;
            target = key < target.Key ? target.Left : target.Right;
        }

        if (target is null)
        {
            ops.Add($"DELETE_MISS {key}");
            steps.Add(MakeStep(steps.Count, $"Delete Miss {key}",
                $"Key {key} was not found in the BST.",
                1, Array.Empty<string>(), opCode: "DELETE_MISS", badge: "Miss", stateOverrides: visited));
            deleted = false;
            return;
        }

        var foundStates = new Dictionary<string, string>(visited) { [target.Id] = SemanticStates.Active };
        steps.Add(MakeStep(steps.Count, $"Locate {key} for Deletion",
            $"Found target node {key}.",
            1, new[] { target.Id }, opCode: "LOCATE", badge: "Target", stateOverrides: foundStates));

        var physicallyRemoved = target;
        if (target.Left is not null && target.Right is not null)
        {
            BstNode replacement;
            string roleName;
            if (Ruleset.DeletionReplacement == DeletionReplacement.InOrderSuccessor)
            {
                replacement = target.Right;
                while (replacement.Left is not null) replacement = replacement.Left;
                roleName = "in-order successor";
            }
            else
            {
                replacement = target.Left;
                while (replacement.Right is not null) replacement = replacement.Right;
                roleName = "in-order predecessor";
            }

            var repKey = replacement.Key;
            var repStates = new Dictionary<string, string>
            {
                [target.Id] = SemanticStates.Active,
                [replacement.Id] = SemanticStates.Frontier
            };
            steps.Add(MakeStep(steps.Count, $"Replace {key} with {repKey}",
                $"Node {key} has 2 children; copying {roleName} {repKey} into node {key}.",
                3, new[] { target.Id, replacement.Id }, opCode: "REPLACE", badge: roleName == "in-order successor" ? "Succ" : "Pred",
                stateOverrides: repStates));

            target.Key = repKey;
            physicallyRemoved = replacement;
        }

        var child = physicallyRemoved.Left ?? physicallyRemoved.Right;
        var parent = physicallyRemoved.Parent;
        if (parent is null)
        {
            _root = child;
            if (child is not null) child.Parent = null;
        }
        else if (parent.Left == physicallyRemoved)
        {
            parent.Left = child;
            if (child is not null) child.Parent = parent;
        }
        else
        {
            parent.Right = child;
            if (child is not null) child.Parent = parent;
        }

        physicallyRemoved.Parent = null;
        physicallyRemoved.Left = null;
        physicallyRemoved.Right = null;

        ops.Add($"DELETE {key}");
        var doneStates = new Dictionary<string, string>();
        var highlights = new List<string>();
        if (physicallyRemoved != target)
        {
            doneStates[target.Id] = SemanticStates.Accepted;
            highlights.Add(target.Id);
        }
        if (child is not null)
        {
            doneStates[child.Id] = SemanticStates.Accepted;
            highlights.Add(child.Id);
        }
        else if (parent is not null && !doneStates.ContainsKey(parent.Id))
        {
            doneStates[parent.Id] = SemanticStates.Visited;
            highlights.Add(parent.Id);
        }

        steps.Add(MakeStep(steps.Count, $"Deleted {key}",
            $"Removed {key} from the BST.",
            5, highlights, opCode: "DELETE", badge: "Done", stateOverrides: doneStates));
        deleted = true;
    }

    private LayoutTreeNode ToLayoutNode(BstNode node, IReadOnlyDictionary<string, string>? stateOverrides)
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
            Label = node.Key.ToString(),
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

    private static int CountNodes(BstNode? node) =>
        node is null ? 0 : 1 + CountNodes(node.Left) + CountNodes(node.Right);

    private static int ComputeHeight(BstNode? node, HeightConvention convention)
    {
        if (node is null) return TreeLayoutHelpers.EmptyHeight(convention);
        if (node.Left is null && node.Right is null) return TreeLayoutHelpers.LeafHeight(convention);
        return 1 + Math.Max(ComputeHeight(node.Left, convention), ComputeHeight(node.Right, convention));
    }

    private static bool VerifySubtreeParents(BstNode node)
    {
        if (node.Left is not null && (node.Left.Parent != node || !VerifySubtreeParents(node.Left)))
            return false;
        if (node.Right is not null && (node.Right.Parent != node || !VerifySubtreeParents(node.Right)))
            return false;
        return true;
    }

    private static bool VerifyBstRange(BstNode? node, int? min, int? max)
    {
        if (node is null) return true;
        if (min.HasValue && node.Key <= min.Value) return false;
        if (max.HasValue && node.Key >= max.Value) return false;
        return VerifyBstRange(node.Left, min, node.Key) && VerifyBstRange(node.Right, node.Key, max);
    }
}
