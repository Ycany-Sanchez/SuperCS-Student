using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed class SplayTracer
{
    private sealed class SplayNode
    {
        public string Id { get; }
        public int Key { get; set; }
        public SplayNode? Left { get; set; }
        public SplayNode? Right { get; set; }
        public SplayNode? Parent { get; set; }

        public SplayNode(string id, int key)
        {
            Id = id;
            Key = key;
        }
    }

    private static readonly IReadOnlyList<string> SplayPseudocode =
    [
        "Splay(x):",
        "  while x.Parent != null:",
        "    if x.Parent == root: ZIGLEFT or ZIGRIGHT",
        "    else if same side: ZIGZIG (rotate y over z, then x over y)",
        "    else (opposite sides): ZIGZAG (rotate x over y, then x over z)"
    ];

    private static readonly IReadOnlyList<string> DeletePseudocode =
    [
        "SplayDelete(key):",
        "  Strategy A (BstDeleteThenSplayParent): BST delete then splay parent of removed node",
        "  Strategy B (SplayTargetRemoveThenSplayMaxLeft): splay target, split, splay max(L), attach R",
        "  Strategy C (SplayTargetRemoveThenSplayMinRight): splay target, split, splay min(R), attach L"
    ];

    private SplayNode? _root;
    private int _nextId = 1;

    public TreeRuleset Ruleset { get; set; }
    public int Count => CountNodes(_root);
    public int? RootKey => _root?.Key;

    public SplayTracer(TreeRuleset? ruleset = null)
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
            SplayPseudocode,
            ops,
            found ? $"Found {key} and splayed it to root." : $"Key {key} not found; splayed last visited node.",
            isSuccess: found);
    }

    public TraceTimeline<TreeStepState> Insert(int key)
    {
        var steps = new List<TraceStep<TreeStepState>>();
        var ops = new List<string>();
        ExecuteInsert(key, steps, ops, out var inserted);
        return new TraceTimeline<TreeStepState>(
            steps,
            SplayPseudocode,
            ops,
            inserted ? $"Inserted {key} and splayed it to root." : $"Key {key} already exists; splayed existing node to root.",
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
            deleted ? $"Deleted {key} using {Ruleset.SplayDeleteStrategy}." : $"Key {key} not found; splayed last visited node.",
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
                    steps.Add(MakeStep(steps.Count, "Clear Splay Tree", "Reset splay tree to empty.", null, Array.Empty<string>(), opCode: "CLEAR"));
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
            steps.Add(MakeStep(0, "No Operations", "No valid splay commands parsed.", null, Array.Empty<string>()));
        }

        return new TraceTimeline<TreeStepState>(
            steps,
            SplayPseudocode,
            ops,
            $"Executed {commands.Count} splay operation(s). Root: {RootKey?.ToString() ?? "none"}.",
            isSuccess: allOk);
    }

    public TreeStepState BuildSnapshot(
        string summary = "Splay tree snapshot",
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
        void Walk(SplayNode? n)
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
        void Walk(SplayNode? n)
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
        void Walk(SplayNode? n)
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
        var q = new Queue<SplayNode>();
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
        if (_root is null)
        {
            ops.Add($"SEARCH_MISS {key}");
            steps.Add(MakeStep(steps.Count, $"Search Miss {key}", "Tree is empty.", 0, Array.Empty<string>(), opCode: "SEARCH_MISS", badge: "Miss"));
            found = false;
            return;
        }

        var visited = new Dictionary<string, string>();
        var cur = _root;
        SplayNode last = _root;
        while (cur is not null)
        {
            last = cur;
            if (key == cur.Key)
            {
                var hitStates = new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Active };
                ops.Add($"SEARCH_HIT {key}");
                steps.Add(MakeStep(steps.Count, $"Found {key}",
                    $"Found key {key}; splaying {key} to root.",
                    0, new[] { cur.Id }, opCode: "SEARCH_HIT", badge: "Found", stateOverrides: hitStates));
                Splay(cur, steps, ops);
                found = true;
                return;
            }

            visited[cur.Id] = SemanticStates.Visited;
            cur = key < cur.Key ? cur.Left : cur.Right;
        }

        var missStates = new Dictionary<string, string>(visited) { [last.Id] = SemanticStates.Frontier };
        ops.Add($"SEARCH_MISS {key}");
        steps.Add(MakeStep(steps.Count, $"Key {key} Not Found",
            $"Key {key} is absent; splaying last visited node {last.Key} to root.",
            0, new[] { last.Id }, opCode: "SEARCH_MISS", badge: "Miss", stateOverrides: missStates));
        Splay(last, steps, ops);
        found = false;
    }

    private void ExecuteInsert(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool inserted)
    {
        if (_root is null)
        {
            _root = new SplayNode($"sp_{_nextId++}", key);
            ops.Add($"INSERT {key}");
            steps.Add(MakeStep(steps.Count, $"Insert Root {key}",
                $"Created root node {key}.",
                0, new[] { _root.Id }, opCode: "INSERT", badge: "Root",
                stateOverrides: new Dictionary<string, string> { [_root.Id] = SemanticStates.Accepted }));
            inserted = true;
            return;
        }

        var visited = new Dictionary<string, string>();
        var cur = _root;
        SplayNode parent = _root;
        var goLeft = false;

        while (cur is not null)
        {
            parent = cur;
            if (key == cur.Key)
            {
                ops.Add($"INSERT_DUPLICATE {key}");
                steps.Add(MakeStep(steps.Count, $"Duplicate Key {key}",
                    $"Key {key} already exists; splaying existing node {key} to root.",
                    0, new[] { cur.Id }, opCode: "DUPLICATE", badge: "Dup",
                    stateOverrides: new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Active }));
                Splay(cur, steps, ops);
                inserted = false;
                return;
            }

            goLeft = key < cur.Key;
            visited[cur.Id] = SemanticStates.Visited;
            cur = goLeft ? cur.Left : cur.Right;
        }

        var node = new SplayNode($"sp_{_nextId++}", key) { Parent = parent };
        if (goLeft) parent.Left = node;
        else parent.Right = node;

        ops.Add($"INSERT {key}");
        steps.Add(MakeStep(steps.Count, $"Insert {key}",
            $"Inserted {key} as {(goLeft ? "left" : "right")} child of {parent.Key}; splaying {key} to root.",
            0, new[] { node.Id }, new[] { $"{parent.Id}->{node.Id}" }, "INSERT", "New",
            new Dictionary<string, string>(visited) { [node.Id] = SemanticStates.Active }));

        Splay(node, steps, ops);
        inserted = true;
    }

    private void ExecuteDelete(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool deleted)
    {
        if (_root is null)
        {
            ops.Add($"DELETE_MISS {key}");
            steps.Add(MakeStep(steps.Count, $"Delete Miss {key}", "Tree is empty.", 0, Array.Empty<string>(), opCode: "DELETE_MISS", badge: "Miss"));
            deleted = false;
            return;
        }

        var cur = _root;
        SplayNode last = _root;
        while (cur is not null && cur.Key != key)
        {
            last = cur;
            cur = key < cur.Key ? cur.Left : cur.Right;
        }

        if (cur is null)
        {
            ops.Add($"DELETE_MISS {key}");
            steps.Add(MakeStep(steps.Count, $"Delete Miss {key}",
                $"Key {key} not found; splaying last visited node {last.Key}.",
                0, new[] { last.Id }, opCode: "DELETE_MISS", badge: "Miss",
                stateOverrides: new Dictionary<string, string> { [last.Id] = SemanticStates.Frontier }));
            Splay(last, steps, ops);
            deleted = false;
            return;
        }

        switch (Ruleset.SplayDeleteStrategy)
        {
            case SplayDeleteStrategy.BstDeleteThenSplayParent:
                DeleteViaBstThenSplayParent(cur, steps, ops);
                break;
            case SplayDeleteStrategy.SplayTargetRemoveThenSplayMaxLeft:
                DeleteViaSplaySplitJoin(cur, preferMaxLeft: true, steps, ops);
                break;
            case SplayDeleteStrategy.SplayTargetRemoveThenSplayMinRight:
                DeleteViaSplaySplitJoin(cur, preferMaxLeft: false, steps, ops);
                break;
        }

        deleted = true;
    }

    private void DeleteViaBstThenSplayParent(SplayNode target, List<TraceStep<TreeStepState>> steps, List<string> ops)
    {
        var deletedKey = target.Key;
        steps.Add(MakeStep(steps.Count, $"Locate {deletedKey} (BST-Delete Strategy)",
            $"Found {deletedKey}; deleting via BST replacement ({Ruleset.DeletionReplacement}) then splaying removed node's parent.",
            1, new[] { target.Id }, opCode: "LOCATE", badge: "Target",
            stateOverrides: new Dictionary<string, string> { [target.Id] = SemanticStates.Active }));

        var physicallyRemoved = target;
        if (target.Left is not null && target.Right is not null)
        {
            SplayNode replacement;
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
            steps.Add(MakeStep(steps.Count, $"Replace {deletedKey} with {repKey}",
                $"Copied {(Ruleset.DeletionReplacement == DeletionReplacement.InOrderSuccessor ? "in-order successor" : "in-order predecessor")} {repKey} into node {deletedKey}.",
                1, new[] { target.Id, replacement.Id }, opCode: "REPLACE",
                stateOverrides: new Dictionary<string, string>
                {
                    [target.Id] = SemanticStates.Active,
                    [replacement.Id] = SemanticStates.Frontier
                }));

            target.Key = repKey;
            physicallyRemoved = replacement;
        }

        var parentToSplay = physicallyRemoved.Parent;
        var child = physicallyRemoved.Left ?? physicallyRemoved.Right;

        if (parentToSplay is null)
        {
            _root = child;
            if (child is not null) child.Parent = null;
        }
        else if (parentToSplay.Left == physicallyRemoved)
        {
            parentToSplay.Left = child;
            if (child is not null) child.Parent = parentToSplay;
        }
        else
        {
            parentToSplay.Right = child;
            if (child is not null) child.Parent = parentToSplay;
        }

        physicallyRemoved.Parent = null;
        physicallyRemoved.Left = null;
        physicallyRemoved.Right = null;

        ops.Add($"DELETE {deletedKey}");
        steps.Add(MakeStep(steps.Count, $"Removed Node for {deletedKey}",
            parentToSplay is not null
                ? $"Physically removed node; now splaying its parent {parentToSplay.Key} to root."
                : $"Removed root node {deletedKey}.",
            1, parentToSplay is not null ? new[] { parentToSplay.Id } : Array.Empty<string>(),
            opCode: "DELETE", badge: "Removed",
            stateOverrides: parentToSplay is not null
                ? new Dictionary<string, string> { [parentToSplay.Id] = SemanticStates.Active }
                : null));

        if (parentToSplay is not null)
        {
            Splay(parentToSplay, steps, ops);
        }
    }

    private void DeleteViaSplaySplitJoin(
        SplayNode target,
        bool preferMaxLeft,
        List<TraceStep<TreeStepState>> steps,
        List<string> ops)
    {
        var deletedKey = target.Key;
        var line = preferMaxLeft ? 2 : 3;

        steps.Add(MakeStep(steps.Count, $"Splay Target {deletedKey} to Root",
            $"Splaying target {deletedKey} to root before splitting into left and right subtrees.",
            line, new[] { target.Id }, opCode: "LOCATE", badge: "Target",
            stateOverrides: new Dictionary<string, string> { [target.Id] = SemanticStates.Active }));

        Splay(target, steps, ops);

        var leftSubtree = target.Left;
        var rightSubtree = target.Right;
        if (leftSubtree is not null) leftSubtree.Parent = null;
        if (rightSubtree is not null) rightSubtree.Parent = null;
        target.Left = null;
        target.Right = null;
        target.Parent = null;

        ops.Add($"DELETE {deletedKey}");

        if (leftSubtree is null)
        {
            _root = rightSubtree;
            steps.Add(MakeStep(steps.Count, $"Removed {deletedKey} (No Left Subtree)",
                rightSubtree is not null
                    ? $"Left subtree is empty; right subtree root {rightSubtree.Key} becomes root."
                    : $"Tree is now empty after deleting {deletedKey}.",
                line, rightSubtree is not null ? new[] { rightSubtree.Id } : Array.Empty<string>(),
                opCode: "DELETE", badge: "Done",
                stateOverrides: rightSubtree is not null
                    ? new Dictionary<string, string> { [rightSubtree.Id] = SemanticStates.Accepted }
                    : null));
            return;
        }

        if (rightSubtree is null)
        {
            _root = leftSubtree;
            steps.Add(MakeStep(steps.Count, $"Removed {deletedKey} (No Right Subtree)",
                $"Right subtree is empty; left subtree root {leftSubtree.Key} becomes root.",
                line, new[] { leftSubtree.Id }, opCode: "DELETE", badge: "Done",
                stateOverrides: new Dictionary<string, string> { [leftSubtree.Id] = SemanticStates.Accepted }));
            return;
        }

        if (preferMaxLeft)
        {
            _root = leftSubtree;
            var maxLeft = leftSubtree;
            while (maxLeft.Right is not null) maxLeft = maxLeft.Right;

            steps.Add(MakeStep(steps.Count, $"Splay Max of Left Subtree ({maxLeft.Key})",
                $"Removed {deletedKey}; splaying largest key {maxLeft.Key} in left subtree to become the new root.",
                line, new[] { maxLeft.Id }, opCode: "SPLIT_LEFT", badge: "MaxLeft",
                stateOverrides: new Dictionary<string, string> { [maxLeft.Id] = SemanticStates.Active }));

            Splay(maxLeft, steps, ops);

            _root!.Right = rightSubtree;
            rightSubtree.Parent = _root;

            steps.Add(MakeStep(steps.Count, $"Attach Right Subtree to {_root.Key}",
                $"Attached right subtree (root {rightSubtree.Key}) as right child of {_root.Key}.",
                line, new[] { _root.Id, rightSubtree.Id }, new[] { $"{_root.Id}->{rightSubtree.Id}" },
                "ATTACH_RIGHT", "Joined",
                new Dictionary<string, string>
                {
                    [_root.Id] = SemanticStates.Accepted,
                    [rightSubtree.Id] = SemanticStates.Visited
                }));
        }
        else
        {
            _root = rightSubtree;
            var minRight = rightSubtree;
            while (minRight.Left is not null) minRight = minRight.Left;

            steps.Add(MakeStep(steps.Count, $"Splay Min of Right Subtree ({minRight.Key})",
                $"Removed {deletedKey}; splaying smallest key {minRight.Key} in right subtree to become the new root.",
                line, new[] { minRight.Id }, opCode: "SPLIT_RIGHT", badge: "MinRight",
                stateOverrides: new Dictionary<string, string> { [minRight.Id] = SemanticStates.Active }));

            Splay(minRight, steps, ops);

            _root!.Left = leftSubtree;
            leftSubtree.Parent = _root;

            steps.Add(MakeStep(steps.Count, $"Attach Left Subtree to {_root.Key}",
                $"Attached left subtree (root {leftSubtree.Key}) as left child of {_root.Key}.",
                line, new[] { _root.Id, leftSubtree.Id }, new[] { $"{_root.Id}->{leftSubtree.Id}" },
                "ATTACH_LEFT", "Joined",
                new Dictionary<string, string>
                {
                    [_root.Id] = SemanticStates.Accepted,
                    [leftSubtree.Id] = SemanticStates.Visited
                }));
        }
    }

    private void Splay(SplayNode x, List<TraceStep<TreeStepState>> steps, List<string> ops)
    {
        while (x.Parent is not null)
        {
            var p = x.Parent;
            var g = p.Parent;

            if (g is null)
            {
                if (x == p.Right)
                {
                    RotateLeft(x);
                    ops.Add("ZIGLEFT");
                    steps.Add(MakeStep(steps.Count, $"ZIGLEFT on {x.Key}",
                        $"Rotated {x.Key} left over root {p.Key}.",
                        2, new[] { x.Id, p.Id }, opCode: "ZIGLEFT", badge: "ZIGLEFT",
                        stateOverrides: new Dictionary<string, string>
                        {
                            [x.Id] = SemanticStates.Accepted,
                            [p.Id] = SemanticStates.Visited
                        }));
                }
                else
                {
                    RotateRight(x);
                    ops.Add("ZIGRIGHT");
                    steps.Add(MakeStep(steps.Count, $"ZIGRIGHT on {x.Key}",
                        $"Rotated {x.Key} right over root {p.Key}.",
                        2, new[] { x.Id, p.Id }, opCode: "ZIGRIGHT", badge: "ZIGRIGHT",
                        stateOverrides: new Dictionary<string, string>
                        {
                            [x.Id] = SemanticStates.Accepted,
                            [p.Id] = SemanticStates.Visited
                        }));
                }
            }
            else if (p == g.Right && x == p.Right)
            {
                // ZIGZIGLEFT: rotate y (p) over z (g), then x over y (p)
                RotateLeft(p);
                RotateLeft(x);
                ops.Add("ZIGZIGLEFT");
                steps.Add(MakeStep(steps.Count, $"ZIGZIGLEFT on {x.Key}",
                    $"Rotated {p.Key} left over {g.Key}, then {x.Key} left over {p.Key}.",
                    3, new[] { x.Id, p.Id, g.Id }, opCode: "ZIGZIGLEFT", badge: "ZIGZIGLEFT",
                    stateOverrides: new Dictionary<string, string>
                    {
                        [x.Id] = SemanticStates.Accepted,
                        [p.Id] = SemanticStates.Visited,
                        [g.Id] = SemanticStates.Visited
                    }));
            }
            else if (p == g.Left && x == p.Left)
            {
                // ZIGZIGRIGHT: rotate y (p) over z (g), then x over y (p)
                RotateRight(p);
                RotateRight(x);
                ops.Add("ZIGZIGRIGHT");
                steps.Add(MakeStep(steps.Count, $"ZIGZIGRIGHT on {x.Key}",
                    $"Rotated {p.Key} right over {g.Key}, then {x.Key} right over {p.Key}.",
                    3, new[] { x.Id, p.Id, g.Id }, opCode: "ZIGZIGRIGHT", badge: "ZIGZIGRIGHT",
                    stateOverrides: new Dictionary<string, string>
                    {
                        [x.Id] = SemanticStates.Accepted,
                        [p.Id] = SemanticStates.Visited,
                        [g.Id] = SemanticStates.Visited
                    }));
            }
            else if (p == g.Right && x == p.Left)
            {
                // ZIGZAGLEFT: rotate x over y (right), then x over z (left)
                RotateRight(x);
                RotateLeft(x);
                ops.Add("ZIGZAGLEFT");
                steps.Add(MakeStep(steps.Count, $"ZIGZAGLEFT on {x.Key}",
                    $"Rotated {x.Key} right over {p.Key}, then {x.Key} left over {g.Key}.",
                    4, new[] { x.Id, p.Id, g.Id }, opCode: "ZIGZAGLEFT", badge: "ZIGZAGLEFT",
                    stateOverrides: new Dictionary<string, string>
                    {
                        [x.Id] = SemanticStates.Accepted,
                        [p.Id] = SemanticStates.Visited,
                        [g.Id] = SemanticStates.Visited
                    }));
            }
            else
            {
                // p == g.Left && x == p.Right -> ZIGZAGRIGHT: rotate x over y (left), then x over z (right)
                RotateLeft(x);
                RotateRight(x);
                ops.Add("ZIGZAGRIGHT");
                steps.Add(MakeStep(steps.Count, $"ZIGZAGRIGHT on {x.Key}",
                    $"Rotated {x.Key} left over {p.Key}, then {x.Key} right over {g.Key}.",
                    4, new[] { x.Id, p.Id, g.Id }, opCode: "ZIGZAGRIGHT", badge: "ZIGZAGRIGHT",
                    stateOverrides: new Dictionary<string, string>
                    {
                        [x.Id] = SemanticStates.Accepted,
                        [p.Id] = SemanticStates.Visited,
                        [g.Id] = SemanticStates.Visited
                    }));
            }
        }
    }

    private void RotateLeft(SplayNode x)
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

    private void RotateRight(SplayNode x)
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

    private LayoutTreeNode ToLayoutNode(SplayNode node, IReadOnlyDictionary<string, string>? stateOverrides)
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

    private static int CountNodes(SplayNode? node) =>
        node is null ? 0 : 1 + CountNodes(node.Left) + CountNodes(node.Right);

    private static int ComputeHeight(SplayNode? node, HeightConvention convention)
    {
        if (node is null) return TreeLayoutHelpers.EmptyHeight(convention);
        if (node.Left is null && node.Right is null) return TreeLayoutHelpers.LeafHeight(convention);
        return 1 + Math.Max(ComputeHeight(node.Left, convention), ComputeHeight(node.Right, convention));
    }

    private static bool VerifySubtreeParents(SplayNode node)
    {
        if (node.Left is not null && (node.Left.Parent != node || !VerifySubtreeParents(node.Left)))
            return false;
        if (node.Right is not null && (node.Right.Parent != node || !VerifySubtreeParents(node.Right)))
            return false;
        return true;
    }

    private static bool VerifyBstRange(SplayNode? node, int? min, int? max)
    {
        if (node is null) return true;
        if (min.HasValue && node.Key <= min.Value) return false;
        if (max.HasValue && node.Key >= max.Value) return false;
        return VerifyBstRange(node.Left, min, node.Key) && VerifyBstRange(node.Right, node.Key, max);
    }
}
