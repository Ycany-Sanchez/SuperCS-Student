using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed class AvlTracer
{
    private sealed class AvlNode
    {
        public string Id { get; }
        public int Key { get; set; }
        public int Height { get; set; }
        public AvlNode? Left { get; set; }
        public AvlNode? Right { get; set; }
        public AvlNode? Parent { get; set; }

        public AvlNode(string id, int key, int height)
        {
            Id = id;
            Key = key;
            Height = height;
        }
    }

    private static readonly IReadOnlyList<string> InsertPseudocode =
    [
        "AvlInsert(key):",
        "  perform standard BST insertion",
        "  walk upward updating node heights",
        "  if |h(L) - h(R)| > 1 at node z:",
        "    pick taller child y of z and taller child x of y",
        "    perform trinode restructuring (ZIG/ZIGZAG) & update heights"
    ];

    private static readonly IReadOnlyList<string> DeletePseudocode =
    [
        "AvlDelete(key):",
        "  perform BST deletion honoring DeletionReplacement",
        "  walk upward from removed node's parent to root:",
        "    update height and check |h(L) - h(R)| <= 1",
        "    if unbalanced: pick y, tie-break x when BF(y) == 0",
        "    restructure (ZIG/ZIGZAG) and continue upward to root"
    ];

    private static readonly IReadOnlyList<string> SearchPseudocode =
    [
        "Search(node, key):",
        "  while node != null:",
        "    if key == node.Key: return FOUND",
        "    node = (key < node.Key) ? node.Left : node.Right",
        "  return NOT_FOUND"
    ];

    private AvlNode? _root;
    private int _nextId = 1;

    public TreeRuleset Ruleset { get; set; }
    public int Count => CountNodes(_root);
    public int? RootKey => _root?.Key;

    public AvlTracer(TreeRuleset? ruleset = null)
    {
        Ruleset = ruleset ?? TreeRuleset.CourseStandard;
    }

    public void Clear()
    {
        _root = null;
        _nextId = 1;
    }

    public int GetHeight()
    {
        SyncAllHeights(_root);
        return NodeHeight(_root);
    }

    public bool VerifyParentLinks()
    {
        if (_root is null) return true;
        if (_root.Parent is not null) return false;
        return VerifySubtreeParents(_root);
    }

    public bool VerifyAvlInvariants()
    {
        SyncAllHeights(_root);
        if (!VerifyParentLinks()) return false;
        if (!VerifyBstRange(_root, null, null)) return false;
        return VerifySubtreeBalance(_root, out _);
    }

    public TraceTimeline<TreeStepState> Search(int key)
    {
        SyncAllHeights(_root);
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
        SyncAllHeights(_root);
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteInsert(key, steps, ops, out var inserted);
        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
            ops,
            inserted ? $"Inserted {key} and restored AVL balance." : $"Duplicate key {key} ignored.",
            isSuccess: inserted);
    }

    public TraceTimeline<TreeStepState> Delete(int key)
    {
        SyncAllHeights(_root);
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteDelete(key, steps, ops, out var deleted);
        return new TraceTimeline<TreeStepState>(
            steps,
            DeletePseudocode,
            ops,
            deleted ? $"Deleted {key} and restored AVL balance." : $"Key {key} not found.",
            isSuccess: deleted);
    }

    public TraceTimeline<TreeStepState> RunScript(string script)
    {
        SyncAllHeights(_root);
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
                    steps.Add(MakeStep(steps.Count, "Clear AVL Tree", "Reset AVL tree to empty.", null, Array.Empty<string>(), opCode: "CLEAR"));
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
            steps.Add(MakeStep(0, "No Operations", "No valid AVL commands parsed.", null, Array.Empty<string>()));
        }

        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
            ops,
            $"Executed {commands.Count} AVL operation(s). Nodes: {Count}, Height: {GetHeight()}.",
            isSuccess: allOk);
    }

    public TreeStepState BuildSnapshot(
        string summary = "AVL snapshot",
        IReadOnlyDictionary<string, string>? stateOverrides = null)
    {
        SyncAllHeights(_root);
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
        void Walk(AvlNode? n)
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
        void Walk(AvlNode? n)
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
        void Walk(AvlNode? n)
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
        var q = new Queue<AvlNode>();
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
                    $"Matched key {key} in AVL tree.",
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
            $"Reached external leaf; {key} is not in the AVL tree.",
            4, Array.Empty<string>(), opCode: "SEARCH_MISS", badge: "Miss", stateOverrides: visited));
        found = false;
    }

    private void ExecuteInsert(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool inserted)
    {
        if (_root is null)
        {
            _root = new AvlNode($"avl_{_nextId++}", key, TreeLayoutHelpers.LeafHeight(Ruleset.HeightConvention));
            ops.Add($"INSERT {key}");
            steps.Add(MakeStep(steps.Count, $"Insert Root {key}",
                $"Created root {key} with height {_root.Height}.",
                1, new[] { _root.Id }, opCode: "INSERT", badge: "Root",
                stateOverrides: new Dictionary<string, string> { [_root.Id] = SemanticStates.Accepted }));
            inserted = true;
            return;
        }

        var visited = new Dictionary<string, string>();
        var cur = _root;
        AvlNode parent = _root;
        var goLeft = false;

        while (cur is not null)
        {
            parent = cur;
            if (key == cur.Key)
            {
                var dupStates = new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Violation };
                ops.Add($"INSERT_DUPLICATE {key}");
                steps.Add(MakeStep(steps.Count, $"Duplicate Key {key}",
                    $"Key {key} already exists in AVL tree.",
                    1, new[] { cur.Id }, opCode: "DUPLICATE", badge: "Dup", stateOverrides: dupStates));
                inserted = false;
                return;
            }

            goLeft = key < cur.Key;
            visited[cur.Id] = SemanticStates.Visited;
            cur = goLeft ? cur.Left : cur.Right;
        }

        var node = new AvlNode($"avl_{_nextId++}", key, TreeLayoutHelpers.LeafHeight(Ruleset.HeightConvention))
        {
            Parent = parent
        };
        if (goLeft) parent.Left = node;
        else parent.Right = node;

        UpdateHeight(parent);
        var insertStates = new Dictionary<string, string>(visited) { [node.Id] = SemanticStates.Accepted };
        ops.Add($"INSERT {key}");
        steps.Add(MakeStep(steps.Count, $"Insert {key}",
            $"Inserted {key} as {(goLeft ? "left" : "right")} child of {parent.Key}.",
            1, new[] { node.Id }, new[] { $"{parent.Id}->{node.Id}" }, "INSERT", "New", insertStates));

        RebalanceUpward(parent, steps, ops, stopAfterFirstRestructure: true);
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
                $"Key {key} is not present in the AVL tree.",
                1, Array.Empty<string>(), opCode: "DELETE_MISS", badge: "Miss", stateOverrides: visited));
            deleted = false;
            return;
        }

        var physicallyRemoved = target;
        if (target.Left is not null && target.Right is not null)
        {
            AvlNode replacement;
            if (Ruleset.DeletionReplacement == DeletionReplacement.InOrderSuccessor)
            {
                replacement = target.Right;
                while (replacement.Left is not null) replacement = replacement.Left;
            }
            else
            {
                replacement = target.Left;
                while (replacement.Right is not null) replacement = replacement.Right;
            }

            var repKey = replacement.Key;
            steps.Add(MakeStep(steps.Count, $"Replace {key} with {repKey}",
                $"Copying {(Ruleset.DeletionReplacement == DeletionReplacement.InOrderSuccessor ? "in-order successor" : "in-order predecessor")} {repKey} into node {key}.",
                1, new[] { target.Id, replacement.Id }, opCode: "REPLACE",
                stateOverrides: new Dictionary<string, string>
                {
                    [target.Id] = SemanticStates.Active,
                    [replacement.Id] = SemanticStates.Frontier
                }));

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
        var afterDeleteHighlights = parent is not null ? new[] { parent.Id } : Array.Empty<string>();
        var afterDeleteStates = parent is not null
            ? new Dictionary<string, string> { [parent.Id] = SemanticStates.Visited }
            : null;
        steps.Add(MakeStep(steps.Count, $"Deleted {key}",
            $"Removed {key}; checking ancestor heights for AVL balance.",
            2, afterDeleteHighlights, opCode: "DELETE", badge: "Removed", stateOverrides: afterDeleteStates));

        if (parent is not null)
        {
            RebalanceUpward(parent, steps, ops, stopAfterFirstRestructure: false);
        }

        deleted = true;
    }

    private void RebalanceUpward(
        AvlNode? start,
        List<TraceStep<TreeStepState>> steps,
        List<string> ops,
        bool stopAfterFirstRestructure)
    {
        var cur = start;
        var restructuredOnce = false;

        while (cur is not null)
        {
            UpdateHeight(cur);
            var bf = BalanceFactor(cur);

            if (Math.Abs(bf) > 1)
            {
                var z = cur;
                var y = bf > 1 ? z.Left! : z.Right!;
                var yIsLeft = y == z.Left;
                var yBf = BalanceFactor(y);

                AvlNode x;
                string tieNote = "";
                if (yBf > 0)
                {
                    x = y.Left!;
                }
                else if (yBf < 0)
                {
                    x = y.Right!;
                }
                else
                {
                    // Equal-height children on y: honor AvlEqualHeightTieBreaker
                    if (Ruleset.AvlEqualHeightTieBreaker == AvlEqualHeightTieBreaker.SameSideSingleRotation)
                    {
                        x = yIsLeft ? y.Left! : y.Right!;
                        tieNote = $" (BF({y.Key})=0 tie-break: same-side child {x.Key} for single rotation)";
                    }
                    else
                    {
                        x = yIsLeft ? y.Right! : y.Left!;
                        tieNote = $" (BF({y.Key})=0 tie-break: opposite-side child {x.Key} for double rotation)";
                    }
                }

                var xIsLeft = x == y.Left;
                var violationStates = new Dictionary<string, string>
                {
                    [z.Id] = SemanticStates.Violation,
                    [y.Id] = SemanticStates.Active,
                    [x.Id] = SemanticStates.Frontier
                };

                steps.Add(MakeStep(steps.Count, $"Height Imbalance at {z.Key}",
                    $"Node {z.Key} has BF={TreeLayoutHelpers.FormatSigned(bf)} (|h(L)-h(R)| > 1). Selected z={z.Key}, y={y.Key}, x={x.Key}{tieNote}.",
                    3, new[] { z.Id, y.Id, x.Id }, opCode: "IMBALANCE", badge: $"BF {TreeLayoutHelpers.FormatSigned(bf)}",
                    stateOverrides: violationStates));

                var (newSubRoot, opCode) = Restructure(z, y, x, yIsLeft, xIsLeft);
                ops.Add(opCode);

                var restoredStates = new Dictionary<string, string>
                {
                    [newSubRoot.Id] = SemanticStates.Accepted
                };
                if (newSubRoot.Left is not null) restoredStates[newSubRoot.Left.Id] = SemanticStates.Visited;
                if (newSubRoot.Right is not null) restoredStates[newSubRoot.Right.Id] = SemanticStates.Visited;

                steps.Add(MakeStep(steps.Count, $"{opCode}: {newSubRoot.Key} replaces {z.Key}",
                    $"Performed {opCode} trinode restructuring; {newSubRoot.Key} becomes subtree root replacing {z.Key}.",
                    5, new[] { newSubRoot.Id, z.Id }, opCode: opCode, badge: opCode,
                    stateOverrides: restoredStates));

                cur = newSubRoot.Parent;
                restructuredOnce = true;

                if (stopAfterFirstRestructure)
                {
                    while (cur is not null)
                    {
                        UpdateHeight(cur);
                        cur = cur.Parent;
                    }
                    break;
                }
            }
            else
            {
                cur = cur.Parent;
            }
        }

        if (restructuredOnce)
        {
            SyncAllHeights(_root);
        }
    }

    private (AvlNode NewSubRoot, string OpCode) Restructure(
        AvlNode z, AvlNode y, AvlNode x, bool yIsLeft, bool xIsLeft)
    {
        AvlNode a, b, c;
        AvlNode? t0, t1, t2, t3;
        string opCode;

        if (yIsLeft && xIsLeft)
        {
            // Left-Left -> single right rotation
            opCode = "ZIGRIGHT";
            a = x; b = y; c = z;
            t0 = x.Left; t1 = x.Right; t2 = y.Right; t3 = z.Right;
        }
        else if (!yIsLeft && !xIsLeft)
        {
            // Right-Right -> single left rotation
            opCode = "ZIGLEFT";
            a = z; b = y; c = x;
            t0 = z.Left; t1 = y.Left; t2 = x.Left; t3 = x.Right;
        }
        else if (yIsLeft && !xIsLeft)
        {
            // Left-Right -> left rotation on y then right rotation on z
            opCode = "ZIGZAGRIGHT";
            a = y; b = x; c = z;
            t0 = y.Left; t1 = x.Left; t2 = x.Right; t3 = z.Right;
        }
        else
        {
            // Right-Left -> right rotation on y then left rotation on z
            opCode = "ZIGZAGLEFT";
            a = z; b = x; c = y;
            t0 = z.Left; t1 = x.Left; t2 = x.Right; t3 = y.Right;
        }

        var grandParent = z.Parent;
        b.Parent = grandParent;
        if (grandParent is null)
        {
            _root = b;
        }
        else if (grandParent.Left == z)
        {
            grandParent.Left = b;
        }
        else
        {
            grandParent.Right = b;
        }

        b.Left = a; a.Parent = b;
        b.Right = c; c.Parent = b;

        a.Left = t0; if (t0 is not null) t0.Parent = a;
        a.Right = t1; if (t1 is not null) t1.Parent = a;
        c.Left = t2; if (t2 is not null) t2.Parent = c;
        c.Right = t3; if (t3 is not null) t3.Parent = c;

        UpdateHeight(a);
        UpdateHeight(c);
        UpdateHeight(b);

        return (b, opCode);
    }

    private int NodeHeight(AvlNode? node) =>
        node is null ? TreeLayoutHelpers.EmptyHeight(Ruleset.HeightConvention) : node.Height;

    private void UpdateHeight(AvlNode node)
    {
        node.Height = 1 + Math.Max(NodeHeight(node.Left), NodeHeight(node.Right));
    }

    private int SyncAllHeights(AvlNode? node)
    {
        if (node is null) return TreeLayoutHelpers.EmptyHeight(Ruleset.HeightConvention);
        var lh = SyncAllHeights(node.Left);
        var rh = SyncAllHeights(node.Right);
        node.Height = 1 + Math.Max(lh, rh);
        return node.Height;
    }

    private int BalanceFactor(AvlNode node) => NodeHeight(node.Left) - NodeHeight(node.Right);

    private LayoutTreeNode ToLayoutNode(AvlNode node, IReadOnlyDictionary<string, string>? stateOverrides)
    {
        var semantic = stateOverrides is not null && stateOverrides.TryGetValue(node.Id, out var s)
            ? s
            : SemanticStates.Default;

        var bf = BalanceFactor(node);
        var badge = $"h={node.Height} BF={TreeLayoutHelpers.FormatSigned(bf)}";

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
            Badge = badge,
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

    private static int CountNodes(AvlNode? node) =>
        node is null ? 0 : 1 + CountNodes(node.Left) + CountNodes(node.Right);

    private static bool VerifySubtreeParents(AvlNode node)
    {
        if (node.Left is not null && (node.Left.Parent != node || !VerifySubtreeParents(node.Left)))
            return false;
        if (node.Right is not null && (node.Right.Parent != node || !VerifySubtreeParents(node.Right)))
            return false;
        return true;
    }

    private static bool VerifyBstRange(AvlNode? node, int? min, int? max)
    {
        if (node is null) return true;
        if (min.HasValue && node.Key <= min.Value) return false;
        if (max.HasValue && node.Key >= max.Value) return false;
        return VerifyBstRange(node.Left, min, node.Key) && VerifyBstRange(node.Right, node.Key, max);
    }

    private bool VerifySubtreeBalance(AvlNode? node, out int height)
    {
        if (node is null)
        {
            height = TreeLayoutHelpers.EmptyHeight(Ruleset.HeightConvention);
            return true;
        }

        if (!VerifySubtreeBalance(node.Left, out var lh) || !VerifySubtreeBalance(node.Right, out var rh))
        {
            height = 0;
            return false;
        }

        if (Math.Abs(lh - rh) > 1)
        {
            height = 0;
            return false;
        }

        height = 1 + Math.Max(lh, rh);
        return node.Height == height;
    }
}
