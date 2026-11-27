using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed class RedBlackTreeTracer
{
    private sealed class RbNode
    {
        public string Id { get; }
        public int Key { get; set; }
        public bool IsRed { get; set; }
        public RbNode? Left { get; set; }
        public RbNode? Right { get; set; }
        public RbNode? Parent { get; set; }

        public RbNode(string id, int key, bool isRed)
        {
            Id = id;
            Key = key;
            IsRed = isRed;
        }

        public string Formatted => $"{Key}{(IsRed ? "(R)" : "(B)")}";
    }

    private static readonly IReadOnlyList<string> InsertPseudocode =
    [
        "RbInsert(key):",
        "  insert new RED node x (BLACK if root)",
        "  while x.Parent is RED (double-red violation):",
        "    if uncle w is BLACK: Case 1 (Trinode Restructure -> b=BLACK, a,c=RED)",
        "    else (uncle w is RED): Case 2 (Recolor y,w=BLACK, z=RED; repeat on z)",
        "  ensure root is BLACK"
    ];

    private static readonly IReadOnlyList<string> DeletePseudocode =
    [
        "RbDelete(key):",
        "  BST delete honoring DeletionReplacement; splice child r in place of removed node v",
        "  if v is BLACK and r is BLACK (double-black at r):",
        "    Case 1 (BLACK sibling y has RED child z): restructure -> b=oldColor(p), a,c=BLACK",
        "    Case 2 (BLACK sibling y has BLACK children): y=RED; if p RED make BLACK else propagate",
        "    Case 3 (RED sibling y): rotate y over p, y=BLACK, p=RED -> reduces to Case 1 or 2"
    ];

    private RbNode? _root;
    private int _nextId = 1;

    public TreeRuleset Ruleset { get; set; }
    public int Count => CountNodes(_root);
    public int? RootKey => _root?.Key;

    public RedBlackTreeTracer(TreeRuleset? ruleset = null)
    {
        Ruleset = ruleset ?? TreeRuleset.CourseStandard;
    }

    public void Clear()
    {
        _root = null;
        _nextId = 1;
    }

    public bool VerifyParentLinks()
    {
        if (_root is null) return true;
        if (_root.Parent is not null) return false;
        return VerifySubtreeParents(_root);
    }

    public int GetBlackHeight()
    {
        if (!TryComputeBlackDepth(_root, out var bh)) return -1;
        return bh;
    }

    /// <summary>
    /// Verifies all four Red-Black invariants (Root black, External leaves black,
    /// No red-red adjacency, Equal black depth on all root-to-leaf paths),
    /// plus BST ordering and parent pointer integrity.
    /// </summary>
    public bool VerifyInvariants()
    {
        if (!VerifyParentLinks()) return false;
        if (!VerifyBstRange(_root, null, null)) return false;
        if (_root is null) return true;

        // 1. Root Property
        if (_root.IsRed) return false;

        // 3. Red Property & 4. Black-Depth Property (2. External Property holds by construction: null = black)
        return VerifyRedAndBlackDepth(_root, out _);
    }

    /// <summary>
    /// Converts the current Red-Black tree into its canonical isomorphic (2,4) tree DiagramScene
    /// by grouping each Black node with its immediate Red child(ren) into a single multi-key node.
    /// </summary>
    public DiagramScene BuildIsomorphicTwoFourScene()
    {
        if (_root is null)
        {
            return DiagramLayout.LayoutTidyTree(null, caption: "Isomorphic (2,4) Tree (Empty)");
        }

        LayoutTreeNode ConvertBlackCluster(RbNode blackNode)
        {
            var keys = new List<string>();
            var blackChildren = new List<RbNode>();

            if (blackNode.Left is not null && blackNode.Left.IsRed)
            {
                var redLeft = blackNode.Left;
                keys.Add(redLeft.Key.ToString());
                if (redLeft.Left is not null) blackChildren.Add(redLeft.Left);
                if (redLeft.Right is not null) blackChildren.Add(redLeft.Right);
            }
            else if (blackNode.Left is not null)
            {
                blackChildren.Add(blackNode.Left);
            }

            keys.Add(blackNode.Key.ToString());

            if (blackNode.Right is not null && blackNode.Right.IsRed)
            {
                var redRight = blackNode.Right;
                keys.Add(redRight.Key.ToString());
                if (redRight.Left is not null) blackChildren.Add(redRight.Left);
                if (redRight.Right is not null) blackChildren.Add(redRight.Right);
            }
            else if (blackNode.Right is not null)
            {
                blackChildren.Add(blackNode.Right);
            }

            var childLayouts = blackChildren.Select(ConvertBlackCluster).ToList();

            return new LayoutTreeNode
            {
                Id = $"iso_{blackNode.Id}",
                Label = string.Join(" | ", keys),
                Shape = NodeShapes.MultiKey,
                SemanticState = SemanticStates.Default,
                Width = Math.Max(48, keys.Count * 36),
                Height = 36,
                MultiKeys = keys,
                Children = childLayouts
            };
        }

        var rootCluster = ConvertBlackCluster(_root);
        return DiagramLayout.LayoutTidyTree(rootCluster, horizontalGap: 28, levelGap: 88, caption: "Isomorphic (2,4) Tree");
    }

    public TraceTimeline<TreeStepState> Search(int key)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteSearch(key, steps, ops, out var found);
        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
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
            inserted ? $"Inserted {key} and preserved Red-Black invariants." : $"Duplicate key {key} ignored.",
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
            deleted ? $"Deleted {key} and preserved Red-Black invariants." : $"Key {key} not found.",
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
                    steps.Add(MakeStep(steps.Count, "Clear Red-Black Tree", "Reset Red-Black tree to empty.", null, Array.Empty<string>(), opCode: "CLEAR"));
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
            steps.Add(MakeStep(0, "No Operations", "No valid Red-Black commands parsed.", null, Array.Empty<string>()));
        }

        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
            ops,
            $"Executed {commands.Count} Red-Black operation(s). Nodes: {Count}, Black Depth: {GetBlackHeight()}.",
            isSuccess: allOk);
    }

    public TreeStepState BuildSnapshot(
        string summary = "Red-Black tree snapshot",
        IReadOnlyDictionary<string, string>? stateOverrides = null,
        string? doubleBlackSentinelParentId = null,
        bool doubleBlackSentinelIsLeft = false)
    {
        var layoutRoot = _root is null
            ? null
            : ToLayoutNode(_root, stateOverrides, doubleBlackSentinelParentId, doubleBlackSentinelIsLeft);
        var scene = DiagramLayout.LayoutTidyTree(layoutRoot, caption: summary);
        var iso = Ruleset.ShowTwoFourIsomorphism && VerifyInvariants()
            ? BuildIsomorphicTwoFourScene()
            : null;

        return new TreeStepState(
            Scene: scene,
            PreOrder: GetPreOrder(),
            InOrder: GetInOrder(),
            PostOrder: GetPostOrder(),
            LevelOrder: GetLevelOrder(),
            ParentLinksHealthy: VerifyParentLinks(),
            StatusSummary: summary,
            IsomorphicScene: iso);
    }

    public TraceTimeline<TreeStepState> TraceWalk(TreeTraversalMode mode) =>
        TreeTraversalTracer.TraceWalk(
            ToTraversalNode(_root),
            mode,
            (summary, overrides) => BuildSnapshot(summary, overrides),
            preserveNodeColors: true);

    private static BinaryTraversalNode? ToTraversalNode(RbNode? n) =>
        n is null ? null : new BinaryTraversalNode(n.Id, n.Key.ToString(), ToTraversalNode(n.Left), ToTraversalNode(n.Right));

    public IReadOnlyList<string> GetPreOrder()
    {
        var list = new List<string>();
        void Walk(RbNode? n)
        {
            if (n is null) return;
            list.Add(n.Formatted);
            Walk(n.Left);
            Walk(n.Right);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetInOrder()
    {
        var list = new List<string>();
        void Walk(RbNode? n)
        {
            if (n is null) return;
            Walk(n.Left);
            list.Add(n.Formatted);
            Walk(n.Right);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetPostOrder()
    {
        var list = new List<string>();
        void Walk(RbNode? n)
        {
            if (n is null) return;
            Walk(n.Left);
            Walk(n.Right);
            list.Add(n.Formatted);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetLevelOrder()
    {
        var list = new List<string>();
        if (_root is null) return list;
        var q = new Queue<RbNode>();
        q.Enqueue(_root);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            list.Add(cur.Formatted);
            if (cur.Left is not null) q.Enqueue(cur.Left);
            if (cur.Right is not null) q.Enqueue(cur.Right);
        }
        return list;
    }

    private void ExecuteSearch(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool found)
    {
        var cur = _root;
        while (cur is not null)
        {
            if (key == cur.Key)
            {
                ops.Add($"SEARCH_HIT {key}");
                steps.Add(MakeStep(steps.Count, $"Found {cur.Formatted}",
                    $"Found key {key} ({(cur.IsRed ? "Red" : "Black")}).",
                    0, new[] { cur.Id }, opCode: "SEARCH_HIT", badge: "Found",
                    stateOverrides: new Dictionary<string, string> { [cur.Id] = SemanticStates.Accepted }));
                found = true;
                return;
            }

            cur = key < cur.Key ? cur.Left : cur.Right;
        }

        ops.Add($"SEARCH_MISS {key}");
        steps.Add(MakeStep(steps.Count, $"Key {key} Not Found",
            $"Reached black external sentinel; {key} is not in the tree.",
            0, Array.Empty<string>(), opCode: "SEARCH_MISS", badge: "Miss"));
        found = false;
    }

    private void ExecuteInsert(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool inserted)
    {
        if (_root is null)
        {
            _root = new RbNode($"rb_{_nextId++}", key, isRed: false);
            ops.Add($"INSERT {key}");
            steps.Add(MakeStep(steps.Count, $"Insert Root {key}(B)",
                $"Inserted {key} as the black root node.",
                1, new[] { _root.Id }, opCode: "INSERT", badge: "Root(B)"));
            inserted = true;
            return;
        }

        var cur = _root;
        RbNode parent = _root;
        var goLeft = false;

        while (cur is not null)
        {
            parent = cur;
            if (key == cur.Key)
            {
                ops.Add($"INSERT_DUPLICATE {key}");
                steps.Add(MakeStep(steps.Count, $"Duplicate Key {key}",
                    $"Key {key} already exists in Red-Black tree.",
                    1, new[] { cur.Id }, opCode: "DUPLICATE", badge: "Dup",
                    stateOverrides: new Dictionary<string, string> { [cur.Id] = SemanticStates.Violation }));
                inserted = false;
                return;
            }
            goLeft = key < cur.Key;
            cur = goLeft ? cur.Left : cur.Right;
        }

        var node = new RbNode($"rb_{_nextId++}", key, isRed: true) { Parent = parent };
        if (goLeft) parent.Left = node;
        else parent.Right = node;

        ops.Add($"INSERT {key}");
        var doubleRed = parent.IsRed;
        steps.Add(MakeStep(steps.Count, $"Insert {key}(R)",
            doubleRed
                ? $"Inserted red node {key}(R) under red parent {parent.Formatted}, creating a double-red violation."
                : $"Inserted red node {key}(R) under black parent {parent.Formatted}.",
            1, new[] { node.Id, parent.Id }, new[] { $"{parent.Id}->{node.Id}" },
            opCode: "INSERT", badge: doubleRed ? "Red-Red!" : "(R)"));

        FixDoubleRed(node, steps, ops);
        inserted = true;
    }

    private void FixDoubleRed(RbNode x, List<TraceStep<TreeStepState>> steps, List<string> ops)
    {
        while (x != _root && x.Parent is not null && x.Parent.IsRed)
        {
            var y = x.Parent;
            var z = y.Parent!;
            var yIsLeft = y == z.Left;
            var w = yIsLeft ? z.Right : z.Left; // uncle

            if (w is null || !w.IsRed)
            {
                // INSERTION Violation: Case 1 (Black uncle -> Restructuring)
                ops.Add("INSERTION Violation: Case 1");
                var xIsLeft = x == y.Left;

                steps.Add(MakeStep(steps.Count, $"INSERTION Violation: Case 1 at {x.Key}",
                    $"Double-red ({x.Formatted}, {y.Formatted}) with black uncle ({w?.Formatted ?? "NIL"}). Restructuring ({x.Key}, {y.Key}, {z.Key}).",
                    3, new[] { x.Id, y.Id, z.Id }, opCode: "INSERTION Violation: Case 1", badge: "Ins Case 1"));

                var (top, leftChild, rightChild, rotCode) = RestructureTriad(z, y, x, yIsLeft, xIsLeft, steps, ops, recolorContext: "insertion");
                top.IsRed = false;
                leftChild.IsRed = true;
                rightChild.IsRed = true;

                ops.Add(rotCode);
                steps.Add(MakeStep(steps.Count, $"{rotCode} & Recolor ({top.Formatted})",
                    $"Completed {rotCode}: {top.Formatted} is black with red children {leftChild.Formatted} and {rightChild.Formatted}.",
                    3, new[] { top.Id, leftChild.Id, rightChild.Id }, opCode: rotCode, badge: rotCode));
                break;
            }
            else
            {
                // INSERTION Violation: Case 2 (Red uncle -> Recoloring)
                ops.Add("INSERTION Violation: Case 2");
                y.IsRed = false;
                w.IsRed = false;
                z.IsRed = z != _root;

                steps.Add(MakeStep(steps.Count, $"INSERTION Violation: Case 2 (Recolor {z.Key})",
                    $"Red uncle {w.Key}(R): recolored {y.Key} & {w.Key} to Black and {z.Key} to {(z.IsRed ? "Red" : "Black (root)")}.",
                    4, new[] { y.Id, w.Id, z.Id }, opCode: "INSERTION Violation: Case 2", badge: "Ins Case 2"));

                x = z;
            }
        }

        if (_root is not null)
        {
            _root.IsRed = false;
        }
    }

    private void ExecuteDelete(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool deleted)
    {
        var target = _root;
        while (target is not null && target.Key != key)
        {
            target = key < target.Key ? target.Left : target.Right;
        }

        if (target is null)
        {
            ops.Add($"DELETE_MISS {key}");
            steps.Add(MakeStep(steps.Count, $"Delete Miss {key}",
                $"Key {key} is not in the Red-Black tree.",
                1, Array.Empty<string>(), opCode: "DELETE_MISS", badge: "Miss"));
            deleted = false;
            return;
        }

        var removed = target;
        if (target.Left is not null && target.Right is not null)
        {
            RbNode replacement;
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
                $"Copied {(Ruleset.DeletionReplacement == DeletionReplacement.InOrderSuccessor ? "in-order successor" : "in-order predecessor")} {repKey} into node {key}.",
                1, new[] { target.Id, replacement.Id }, opCode: "REPLACE", badge: "Swap"));

            target.Key = repKey;
            removed = replacement;
        }

        var removedWasRed = removed.IsRed;
        var r = removed.Left ?? removed.Right;
        var parent = removed.Parent;
        var rIsLeft = parent is not null && parent.Left == removed;

        if (parent is null)
        {
            _root = r;
            if (r is not null) r.Parent = null;
        }
        else if (rIsLeft)
        {
            parent.Left = r;
            if (r is not null) r.Parent = parent;
        }
        else
        {
            parent.Right = r;
            if (r is not null) r.Parent = parent;
        }

        removed.Parent = null;
        removed.Left = null;
        removed.Right = null;
        ops.Add($"DELETE {key}");

        if (removedWasRed)
        {
            steps.Add(MakeStep(steps.Count, $"Deleted Red Node ({key})",
                $"Removed red node; black depth is unchanged.",
                1, parent is not null ? new[] { parent.Id } : Array.Empty<string>(), opCode: "DELETE", badge: "Done"));
            deleted = true;
            return;
        }

        if (r is not null && r.IsRed)
        {
            r.IsRed = false;
            steps.Add(MakeStep(steps.Count, $"Deleted {key} & Recolored {r.Key}(B)",
                $"Removed black node and recolored red child {r.Key} to Black, preserving black depth.",
                1, new[] { r.Id }, opCode: "DELETE", badge: "Recolored"));
            deleted = true;
            return;
        }

        if (parent is null)
        {
            if (_root is not null) _root.IsRed = false;
            steps.Add(MakeStep(steps.Count, $"Deleted Root {key}",
                _root is null ? "Tree is now empty." : $"New root {_root.Formatted} absorbs double-black.",
                1, _root is not null ? new[] { _root.Id } : Array.Empty<string>(), opCode: "DELETE", badge: "Done"));
            deleted = true;
            return;
        }

        // Double-black violation at (r, parent, rIsLeft)
        var dbStates = r is not null ? new Dictionary<string, string> { [r.Id] = SemanticStates.DoubleBlack } : null;
        steps.Add(MakeStep(steps.Count, $"Double-Black Violation after Deleting {key}",
            $"Removed black node with black replacement ({r?.Formatted ?? "NIL"}), creating a double-black under {parent.Formatted}.",
            2, r is not null ? new[] { r.Id, parent.Id } : new[] { parent.Id },
            opCode: "DOUBLE_BLACK", badge: "Double-Black",
            stateOverrides: dbStates,
            doubleBlackSentinelParentId: r is null ? parent.Id : null,
            doubleBlackSentinelIsLeft: rIsLeft));

        FixDoubleBlack(r, parent, rIsLeft, steps, ops);
        deleted = true;
    }

    private void FixDoubleBlack(
        RbNode? r,
        RbNode? parent,
        bool rIsLeft,
        List<TraceStep<TreeStepState>> steps,
        List<string> ops)
    {
        while (parent is not null)
        {
            var y = rIsLeft ? parent.Right : parent.Left;
            if (y is null)
            {
                // Defensive safeguard: in a valid RB tree a double-black node always has a non-null sibling
                break;
            }

            if (y.IsRed)
            {
                // DELETION Violation: Case 3 (Red sibling -> Adjustment)
                ops.Add("DELETION Violation: Case 3");
                var yIsRight = rIsLeft;
                var rotCode = yIsRight ? "ZIGLEFT" : "ZIGRIGHT";

                if (yIsRight) RotateLeft(y);
                else RotateRight(y);

                y.IsRed = false;
                parent.IsRed = true;
                ops.Add(rotCode);

                var dbStates = r is not null ? new Dictionary<string, string> { [r.Id] = SemanticStates.DoubleBlack } : null;
                steps.Add(MakeStep(steps.Count, $"DELETION Violation: Case 3 ({rotCode})",
                    $"Red sibling {y.Key}: rotated {y.Key} over {parent.Key} ({rotCode}), recolored {y.Key}(B) and {parent.Key}(R). New sibling is black.",
                    5, new[] { y.Id, parent.Id }, opCode: "DELETION Violation: Case 3", badge: "Del Case 3",
                    stateOverrides: dbStates,
                    doubleBlackSentinelParentId: r is null ? parent.Id : null,
                    doubleBlackSentinelIsLeft: rIsLeft));

                // Re-evaluate with the same (r, parent, rIsLeft); sibling is now guaranteed Black
                continue;
            }

            // Sibling y is BLACK
            var yIsLeft = rIsLeft ? false : true;
            RbNode? redChild = null;
            if (yIsLeft)
            {
                // Prefer same-side (Left) for single rotation if both children are red
                if (y.Left is not null && y.Left.IsRed) redChild = y.Left;
                else if (y.Right is not null && y.Right.IsRed) redChild = y.Right;
            }
            else
            {
                // Prefer same-side (Right) for single rotation if both children are red
                if (y.Right is not null && y.Right.IsRed) redChild = y.Right;
                else if (y.Left is not null && y.Left.IsRed) redChild = y.Left;
            }

            if (redChild is not null)
            {
                // DELETION Violation: Case 1 (Black sibling y has red child z -> Restructuring)
                ops.Add("DELETION Violation: Case 1");
                var parentWasRed = parent.IsRed;
                var zIsLeft = redChild == y.Left;

                var (top, leftChild, rightChild, rotCode) = RestructureTriad(parent, y, redChild, yIsLeft, zIsLeft, steps, ops, recolorContext: "deletion");
                top.IsRed = parentWasRed;
                leftChild.IsRed = false;
                rightChild.IsRed = false;

                ops.Add(rotCode);
                steps.Add(MakeStep(steps.Count, $"DELETION Violation: Case 1 ({rotCode})",
                    $"Black sibling {y.Key} had red child {redChild.Key}: restructured ({rotCode}), setting {top.Formatted}, {leftChild.Formatted}, {rightChild.Formatted}. Double-black resolved.",
                    3, new[] { top.Id, leftChild.Id, rightChild.Id }, opCode: "DELETION Violation: Case 1", badge: "Del Case 1"));
                break;
            }
            else
            {
                // DELETION Violation: Case 2 (Black sibling y has no red children -> Recoloring)
                ops.Add("DELETION Violation: Case 2");
                y.IsRed = true;

                if (parent.IsRed)
                {
                    parent.IsRed = false;
                    steps.Add(MakeStep(steps.Count, "DELETION Violation: Case 2 (Resolved)",
                        $"Black sibling {y.Key} had only black children: recolored {y.Key}(R) and red parent {parent.Key}(B). Double-black resolved.",
                        4, new[] { y.Id, parent.Id }, opCode: "DELETION Violation: Case 2", badge: "Del Case 2"));
                    break;
                }

                // Parent was Black -> parent becomes double-black and propagates upward
                var nextParent = parent.Parent;
                var nextIsLeft = nextParent is not null && nextParent.Left == parent;
                var dbStates = nextParent is not null
                    ? new Dictionary<string, string> { [parent.Id] = SemanticStates.DoubleBlack }
                    : null;

                steps.Add(MakeStep(steps.Count,
                    nextParent is null ? "DELETION Violation: Case 2 (Root Absorbed)" : $"DELETION Violation: Case 2 (Propagate to {parent.Key})",
                    nextParent is null
                        ? $"Black sibling {y.Key} recolored Red; double-black reached root {parent.Key}(B) and is absorbed."
                        : $"Black sibling {y.Key} recolored Red; double-black propagates upward to black parent {parent.Key}.",
                    4, new[] { y.Id, parent.Id }, opCode: "DELETION Violation: Case 2", badge: "Del Case 2",
                    stateOverrides: dbStates));

                r = parent;
                parent = nextParent;
                rIsLeft = nextIsLeft;
            }
        }

        if (_root is not null)
        {
            _root.IsRed = false;
        }
    }

    private (RbNode Top, RbNode Left, RbNode Right, string OpCode) RestructureTriad(
        RbNode z,
        RbNode y,
        RbNode x,
        bool yIsLeft,
        bool xIsLeft,
        List<TraceStep<TreeStepState>> steps,
        List<string> ops,
        string recolorContext)
    {
        if (Ruleset.RedBlackRestructureMode == RedBlackRestructureMode.StandardRotations)
        {
            if (yIsLeft && xIsLeft)
            {
                RotateRight(y);
                return (y, x, z, "ZIGRIGHT");
            }
            if (!yIsLeft && !xIsLeft)
            {
                RotateLeft(y);
                return (y, z, x, "ZIGLEFT");
            }
            if (yIsLeft && !xIsLeft)
            {
                RotateLeft(x);
                steps.Add(MakeStep(steps.Count, $"Standard Rotation 1/2: Left Rotate {x.Key}",
                    $"Intermediate left rotation of {x.Key} over {y.Key} during {recolorContext} fixup.",
                    3, new[] { x.Id, y.Id }, opCode: "ROTATE_STEP", badge: "Rot 1/2"));
                RotateRight(x);
                return (x, y, z, "ZIGZAGRIGHT");
            }
            else
            {
                RotateRight(x);
                steps.Add(MakeStep(steps.Count, $"Standard Rotation 1/2: Right Rotate {x.Key}",
                    $"Intermediate right rotation of {x.Key} over {y.Key} during {recolorContext} fixup.",
                    3, new[] { x.Id, y.Id }, opCode: "ROTATE_STEP", badge: "Rot 1/2"));
                RotateLeft(x);
                return (x, z, y, "ZIGZAGLEFT");
            }
        }

        // Direct Trinode Restructuring
        RbNode a, b, c;
        RbNode? t0, t1, t2, t3;
        string opCode;

        if (yIsLeft && xIsLeft)
        {
            opCode = "ZIGRIGHT";
            a = x; b = y; c = z;
            t0 = x.Left; t1 = x.Right; t2 = y.Right; t3 = z.Right;
        }
        else if (!yIsLeft && !xIsLeft)
        {
            opCode = "ZIGLEFT";
            a = z; b = y; c = x;
            t0 = z.Left; t1 = y.Left; t2 = x.Left; t3 = x.Right;
        }
        else if (yIsLeft && !xIsLeft)
        {
            opCode = "ZIGZAGRIGHT";
            a = y; b = x; c = z;
            t0 = y.Left; t1 = x.Left; t2 = x.Right; t3 = z.Right;
        }
        else
        {
            opCode = "ZIGZAGLEFT";
            a = z; b = x; c = y;
            t0 = z.Left; t1 = x.Left; t2 = x.Right; t3 = y.Right;
        }

        var grandParent = z.Parent;
        b.Parent = grandParent;
        if (grandParent is null) _root = b;
        else if (grandParent.Left == z) grandParent.Left = b;
        else grandParent.Right = b;

        b.Left = a; a.Parent = b;
        b.Right = c; c.Parent = b;

        a.Left = t0; if (t0 is not null) t0.Parent = a;
        a.Right = t1; if (t1 is not null) t1.Parent = a;
        c.Left = t2; if (t2 is not null) t2.Parent = c;
        c.Right = t3; if (t3 is not null) t3.Parent = c;

        return (b, a, c, opCode);
    }

    private void RotateLeft(RbNode x)
    {
        var p = x.Parent!;
        var g = p.Parent;

        p.Right = x.Left;
        if (x.Left is not null) x.Left.Parent = p;

        x.Left = p;
        p.Parent = x;

        x.Parent = g;
        if (g is null) _root = x;
        else if (g.Left == p) g.Left = x;
        else g.Right = x;
    }

    private void RotateRight(RbNode x)
    {
        var p = x.Parent!;
        var g = p.Parent;

        p.Left = x.Right;
        if (x.Right is not null) x.Right.Parent = p;

        x.Right = p;
        p.Parent = x;

        x.Parent = g;
        if (g is null) _root = x;
        else if (g.Left == p) g.Left = x;
        else g.Right = x;
    }

    private LayoutTreeNode ToLayoutNode(
        RbNode node,
        IReadOnlyDictionary<string, string>? stateOverrides,
        string? dbSentinelParentId,
        bool dbSentinelIsLeft)
    {
        var defaultColor = node.IsRed ? SemanticStates.RedNode : SemanticStates.BlackNode;
        var semantic = stateOverrides is not null && stateOverrides.TryGetValue(node.Id, out var s)
            ? s
            : defaultColor;

        var badge = semantic == SemanticStates.DoubleBlack
            ? "(BB)"
            : (node.IsRed ? "(R)" : "(B)");

        var children = new List<LayoutTreeNode>();
        var edgeLabels = new List<string>();
        var showDbLeftSentinel = dbSentinelParentId == node.Id && dbSentinelIsLeft && node.Left is null;
        var showDbRightSentinel = dbSentinelParentId == node.Id && !dbSentinelIsLeft && node.Right is null;

        if (Ruleset.ShowExternalSentinels || showDbLeftSentinel || showDbRightSentinel)
        {
            if (node.Left is not null)
            {
                children.Add(ToLayoutNode(node.Left, stateOverrides, dbSentinelParentId, dbSentinelIsLeft));
                edgeLabels.Add("L");
            }
            else if (Ruleset.ShowExternalSentinels || showDbLeftSentinel)
            {
                children.Add(TreeLayoutHelpers.CreateSentinel(node.Id, "L", isDoubleBlack: showDbLeftSentinel));
                edgeLabels.Add("L");
            }

            if (node.Right is not null)
            {
                children.Add(ToLayoutNode(node.Right, stateOverrides, dbSentinelParentId, dbSentinelIsLeft));
                edgeLabels.Add("R");
            }
            else if (Ruleset.ShowExternalSentinels || showDbRightSentinel)
            {
                children.Add(TreeLayoutHelpers.CreateSentinel(node.Id, "R", isDoubleBlack: showDbRightSentinel));
                edgeLabels.Add("R");
            }
        }
        else
        {
            var singleChild = (node.Left is null) ^ (node.Right is null);
            if (node.Left is not null)
            {
                children.Add(ToLayoutNode(node.Left, stateOverrides, dbSentinelParentId, dbSentinelIsLeft));
                edgeLabels.Add(singleChild ? "L" : "");
            }
            if (node.Right is not null)
            {
                children.Add(ToLayoutNode(node.Right, stateOverrides, dbSentinelParentId, dbSentinelIsLeft));
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
        IReadOnlyDictionary<string, string>? stateOverrides = null,
        string? doubleBlackSentinelParentId = null,
        bool doubleBlackSentinelIsLeft = false)
    {
        var state = BuildSnapshot(title, stateOverrides, doubleBlackSentinelParentId, doubleBlackSentinelIsLeft);
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

    private static int CountNodes(RbNode? node) =>
        node is null ? 0 : 1 + CountNodes(node.Left) + CountNodes(node.Right);

    private static bool VerifySubtreeParents(RbNode node)
    {
        if (node.Left is not null && (node.Left.Parent != node || !VerifySubtreeParents(node.Left)))
            return false;
        if (node.Right is not null && (node.Right.Parent != node || !VerifySubtreeParents(node.Right)))
            return false;
        return true;
    }

    private static bool VerifyBstRange(RbNode? node, int? min, int? max)
    {
        if (node is null) return true;
        if (min.HasValue && node.Key <= min.Value) return false;
        if (max.HasValue && node.Key >= max.Value) return false;
        return VerifyBstRange(node.Left, min, node.Key) && VerifyBstRange(node.Right, node.Key, max);
    }

    private static bool TryComputeBlackDepth(RbNode? node, out int blackDepth)
    {
        if (node is null)
        {
            blackDepth = 0;
            return true;
        }
        if (!TryComputeBlackDepth(node.Left, out var lb) || !TryComputeBlackDepth(node.Right, out var rb) || lb != rb)
        {
            blackDepth = -1;
            return false;
        }
        blackDepth = lb + (node.IsRed ? 0 : 1);
        return true;
    }

    private static bool VerifyRedAndBlackDepth(RbNode? node, out int blackDepth)
    {
        if (node is null)
        {
            blackDepth = 0;
            return true;
        }

        if (node.IsRed)
        {
            if ((node.Left is not null && node.Left.IsRed) ||
                (node.Right is not null && node.Right.IsRed))
            {
                blackDepth = -1;
                return false;
            }
        }

        if (!VerifyRedAndBlackDepth(node.Left, out var lb) ||
            !VerifyRedAndBlackDepth(node.Right, out var rb) ||
            lb != rb)
        {
            blackDepth = -1;
            return false;
        }

        blackDepth = lb + (node.IsRed ? 0 : 1);
        return true;
    }
}
