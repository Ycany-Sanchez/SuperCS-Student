using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed class TwoFourTreeTracer
{
    private sealed class TwoFourNode
    {
        public string Id { get; }
        public List<int> Keys { get; } = new();
        public List<TwoFourNode> Children { get; } = new();
        public TwoFourNode? Parent { get; set; }
        public bool IsLeaf => Children.Count == 0;

        public TwoFourNode(string id)
        {
            Id = id;
        }

        public string FormatKeys() => Keys.Count == 0 ? "[]" : $"[{string.Join(", ", Keys)}]";
    }

    private static readonly IReadOnlyList<string> InsertPseudocode =
    [
        "TwoFourInsert(key):",
        "  descend to target leaf node",
        "  insert key in sorted position",
        "  while node has 4 keys (5-node overflow):",
        "    split node, promoting k3 (ThirdKey) or k2 (SecondKey) to parent"
    ];

    private static readonly IReadOnlyList<string> DeletePseudocode =
    [
        "TwoFourDelete(key):",
        "  locate node containing key; if internal, swap with leaf replacement",
        "  remove key from leaf",
        "  while node != root and node has 0 keys (underflow):",
        "    if adjacent sibling has >= 2 keys: TRANSFER via parent separator",
        "    else: FUSE with sibling + parent separator (collapse root if empty)"
    ];

    private TwoFourNode? _root;
    private int _nextId = 1;

    public TreeRuleset Ruleset { get; set; }
    public int KeyCount => CountKeys(_root);

    public TwoFourTreeTracer(TreeRuleset? ruleset = null)
    {
        Ruleset = ruleset ?? (TreeRuleset.CourseStandard with { DeletionReplacement = DeletionReplacement.InOrderPredecessor });
    }

    public void Clear()
    {
        _root = null;
        _nextId = 1;
    }

    public IReadOnlyList<int> GetRootKeys() =>
        _root is null ? Array.Empty<int>() : _root.Keys.ToList();

    public IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> GetLevelKeyGroups()
    {
        var levels = new List<IReadOnlyList<IReadOnlyList<int>>>();
        if (_root is null) return levels;

        var current = new List<TwoFourNode> { _root };
        while (current.Count > 0)
        {
            levels.Add(current.Select(n => (IReadOnlyList<int>)n.Keys.ToList()).ToList());
            var next = new List<TwoFourNode>();
            foreach (var n in current)
            {
                next.AddRange(n.Children);
            }
            current = next;
        }
        return levels;
    }

    public bool VerifyParentLinks()
    {
        if (_root is null) return true;
        if (_root.Parent is not null) return false;
        return VerifySubtreeParents(_root);
    }

    public bool VerifyInvariants()
    {
        if (_root is null) return true;
        if (!VerifyParentLinks()) return false;
        int? leafDepth = null;
        return VerifyNode(_root, 0, null, null, ref leafDepth);
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
            inserted ? $"Inserted {key} into (2,4) tree." : $"Duplicate key {key} ignored.",
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
            deleted ? $"Deleted {key} from (2,4) tree." : $"Key {key} not found.",
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
                    steps.Add(MakeStep(steps.Count, "Clear (2,4) Tree", "Reset (2,4) tree to empty.", null, Array.Empty<string>(), opCode: "CLEAR"));
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
            steps.Add(MakeStep(0, "No Operations", "No valid (2,4) tree commands parsed.", null, Array.Empty<string>()));
        }

        return new TraceTimeline<TreeStepState>(
            steps,
            InsertPseudocode,
            ops,
            $"Executed {commands.Count} (2,4) operation(s). Keys: {KeyCount}.",
            isSuccess: allOk);
    }

    public TreeStepState BuildSnapshot(
        string summary = "(2,4) tree snapshot",
        IReadOnlyDictionary<string, string>? stateOverrides = null)
    {
        var layoutRoot = _root is null ? null : ToLayoutNode(_root, stateOverrides);
        var scene = DiagramLayout.LayoutTidyTree(layoutRoot, horizontalGap: 28, levelGap: 88, caption: summary);

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
        void Walk(TwoFourNode? n)
        {
            if (n is null) return;
            list.Add(n.FormatKeys());
            foreach (var ch in n.Children) Walk(ch);
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetInOrder()
    {
        var list = new List<string>();
        void Walk(TwoFourNode? n)
        {
            if (n is null) return;
            for (var i = 0; i < n.Keys.Count; i++)
            {
                if (i < n.Children.Count) Walk(n.Children[i]);
                list.Add(n.Keys[i].ToString());
            }
            if (n.Children.Count > n.Keys.Count)
            {
                Walk(n.Children[^1]);
            }
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetPostOrder()
    {
        var list = new List<string>();
        void Walk(TwoFourNode? n)
        {
            if (n is null) return;
            foreach (var ch in n.Children) Walk(ch);
            list.Add(n.FormatKeys());
        }
        Walk(_root);
        return list;
    }

    public IReadOnlyList<string> GetLevelOrder()
    {
        var list = new List<string>();
        if (_root is null) return list;
        var q = new Queue<TwoFourNode>();
        q.Enqueue(_root);
        while (q.Count > 0)
        {
            var cur = q.Dequeue();
            list.Add(cur.FormatKeys());
            foreach (var ch in cur.Children) q.Enqueue(ch);
        }
        return list;
    }

    private void ExecuteSearch(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool found)
    {
        var visited = new Dictionary<string, string>();
        var cur = _root;
        while (cur is not null)
        {
            var i = 0;
            while (i < cur.Keys.Count && key > cur.Keys[i]) i++;

            if (i < cur.Keys.Count && cur.Keys[i] == key)
            {
                var states = new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Accepted };
                ops.Add($"SEARCH_HIT {key}");
                steps.Add(MakeStep(steps.Count, $"Found {key}",
                    $"Found key {key} inside node {cur.FormatKeys()}.",
                    1, new[] { cur.Id }, opCode: "SEARCH_HIT", badge: "Found", stateOverrides: states));
                found = true;
                return;
            }

            visited[cur.Id] = SemanticStates.Visited;
            if (cur.IsLeaf) break;
            cur = cur.Children[i];
        }

        ops.Add($"SEARCH_MISS {key}");
        steps.Add(MakeStep(steps.Count, $"Key {key} Not Found",
            $"Reached leaf without finding key {key}.",
            1, Array.Empty<string>(), opCode: "SEARCH_MISS", badge: "Miss", stateOverrides: visited));
        found = false;
    }

    private void ExecuteInsert(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool inserted)
    {
        if (_root is null)
        {
            _root = new TwoFourNode($"tf_{_nextId++}");
            _root.Keys.Add(key);
            ops.Add($"INSERT {key}");
            steps.Add(MakeStep(steps.Count, $"Insert Root {key}",
                $"Created root node [{key}].",
                2, new[] { _root.Id }, opCode: "INSERT", badge: "Root",
                stateOverrides: new Dictionary<string, string> { [_root.Id] = SemanticStates.Accepted }));
            inserted = true;
            return;
        }

        var visited = new Dictionary<string, string>();
        var cur = _root;
        while (true)
        {
            var i = 0;
            while (i < cur.Keys.Count && key > cur.Keys[i]) i++;

            if (i < cur.Keys.Count && cur.Keys[i] == key)
            {
                ops.Add($"INSERT_DUPLICATE {key}");
                steps.Add(MakeStep(steps.Count, $"Duplicate Key {key}",
                    $"Key {key} already exists in {cur.FormatKeys()}.",
                    1, new[] { cur.Id }, opCode: "DUPLICATE", badge: "Dup",
                    stateOverrides: new Dictionary<string, string>(visited) { [cur.Id] = SemanticStates.Violation }));
                inserted = false;
                return;
            }

            visited[cur.Id] = SemanticStates.Visited;
            if (cur.IsLeaf)
            {
                cur.Keys.Insert(i, key);
                break;
            }
            cur = cur.Children[i];
        }

        ops.Add($"INSERT {key}");
        var afterInsertState = cur.Keys.Count == 4 ? SemanticStates.Violation : SemanticStates.Accepted;
        steps.Add(MakeStep(steps.Count, $"Insert {key} into {cur.FormatKeys()}",
            cur.Keys.Count == 4
                ? $"Inserted {key}, creating a 5-node overflow {cur.FormatKeys()}."
                : $"Inserted {key} into leaf {cur.FormatKeys()}.",
            2, new[] { cur.Id }, opCode: cur.Keys.Count == 4 ? "OVERFLOW" : "INSERT",
            badge: cur.Keys.Count == 4 ? "Overflow" : "Inserted",
            stateOverrides: new Dictionary<string, string>(visited) { [cur.Id] = afterInsertState }));

        ResolveOverflow(cur, steps, ops);
        inserted = true;
    }

    private void ResolveOverflow(TwoFourNode node, List<TraceStep<TreeStepState>> steps, List<string> ops)
    {
        while (node.Keys.Count >= 4)
        {
            var promoIdx = Ruleset.TwoFourSplitPromotion == TwoFourSplitPromotion.ThirdKeyRightMiddle ? 2 : 1;
            var promoKey = node.Keys[promoIdx];
            var leftNode = new TwoFourNode($"tf_{_nextId++}");
            var rightNode = new TwoFourNode($"tf_{_nextId++}");

            for (var i = 0; i < promoIdx; i++) leftNode.Keys.Add(node.Keys[i]);
            for (var i = promoIdx + 1; i < node.Keys.Count; i++) rightNode.Keys.Add(node.Keys[i]);

            if (!node.IsLeaf)
            {
                for (var i = 0; i <= promoIdx; i++)
                {
                    var ch = node.Children[i];
                    ch.Parent = leftNode;
                    leftNode.Children.Add(ch);
                }
                for (var i = promoIdx + 1; i < node.Children.Count; i++)
                {
                    var ch = node.Children[i];
                    ch.Parent = rightNode;
                    rightNode.Children.Add(ch);
                }
            }

            var parent = node.Parent;
            if (parent is null)
            {
                var newRoot = new TwoFourNode($"tf_{_nextId++}");
                newRoot.Keys.Add(promoKey);
                newRoot.Children.Add(leftNode);
                newRoot.Children.Add(rightNode);
                leftNode.Parent = newRoot;
                rightNode.Parent = newRoot;
                _root = newRoot;
                parent = newRoot;
            }
            else
            {
                var childIdx = parent.Children.IndexOf(node);
                parent.Keys.Insert(childIdx, promoKey);
                parent.Children[childIdx] = leftNode;
                parent.Children.Insert(childIdx + 1, rightNode);
                leftNode.Parent = parent;
                rightNode.Parent = parent;
            }

            ops.Add($"SPLIT {promoKey}");
            var promoRuleLabel = promoIdx == 2 ? "k3 (3rd key)" : "k2 (2nd key)";
            var parentOverflow = parent.Keys.Count >= 4;
            steps.Add(MakeStep(steps.Count, $"Split & Promote {promoKey}",
                $"Promoted {promoRuleLabel} = {promoKey} into {parent.FormatKeys()}; left={leftNode.FormatKeys()}, right={rightNode.FormatKeys()}.",
                4, new[] { parent.Id, leftNode.Id, rightNode.Id }, opCode: "SPLIT",
                badge: parentOverflow ? "Cascade Split" : $"Promote {promoKey}",
                stateOverrides: new Dictionary<string, string>
                {
                    [parent.Id] = parentOverflow ? SemanticStates.Violation : SemanticStates.Accepted,
                    [leftNode.Id] = SemanticStates.Visited,
                    [rightNode.Id] = SemanticStates.Visited
                }));

            node = parent;
        }
    }

    private void ExecuteDelete(int key, List<TraceStep<TreeStepState>> steps, List<string> ops, out bool deleted)
    {
        var cur = _root;
        var keyIdx = -1;
        while (cur is not null)
        {
            var i = 0;
            while (i < cur.Keys.Count && key > cur.Keys[i]) i++;

            if (i < cur.Keys.Count && cur.Keys[i] == key)
            {
                keyIdx = i;
                break;
            }

            if (cur.IsLeaf)
            {
                cur = null;
                break;
            }
            cur = cur.Children[i];
        }

        if (cur is null)
        {
            ops.Add($"DELETE_MISS {key}");
            steps.Add(MakeStep(steps.Count, $"Delete Miss {key}",
                $"Key {key} is not in the (2,4) tree.",
                1, Array.Empty<string>(), opCode: "DELETE_MISS", badge: "Miss"));
            deleted = false;
            return;
        }

        TwoFourNode leafNode;
        int leafKeyIdx;

        if (!cur.IsLeaf)
        {
            if (Ruleset.DeletionReplacement == DeletionReplacement.InOrderPredecessor)
            {
                leafNode = cur.Children[keyIdx];
                while (!leafNode.IsLeaf) leafNode = leafNode.Children[^1];
                leafKeyIdx = leafNode.Keys.Count - 1;
            }
            else
            {
                leafNode = cur.Children[keyIdx + 1];
                while (!leafNode.IsLeaf) leafNode = leafNode.Children[0];
                leafKeyIdx = 0;
            }

            var repKey = leafNode.Keys[leafKeyIdx];
            var role = Ruleset.DeletionReplacement == DeletionReplacement.InOrderPredecessor ? "predecessor" : "successor";
            steps.Add(MakeStep(steps.Count, $"Swap {key} with {role} {repKey}",
                $"Key {key} is in internal node {cur.FormatKeys()}; swapping with leaf {role} {repKey} in {leafNode.FormatKeys()}.",
                1, new[] { cur.Id, leafNode.Id }, opCode: "SWAP",
                badge: role == "predecessor" ? "Pred" : "Succ",
                stateOverrides: new Dictionary<string, string>
                {
                    [cur.Id] = SemanticStates.Active,
                    [leafNode.Id] = SemanticStates.Frontier
                }));

            cur.Keys[keyIdx] = repKey;
            leafNode.Keys[leafKeyIdx] = key;
        }
        else
        {
            leafNode = cur;
            leafKeyIdx = keyIdx;
        }

        leafNode.Keys.RemoveAt(leafKeyIdx);
        ops.Add($"DELETE {key}");

        if (leafNode == _root && leafNode.Keys.Count == 0)
        {
            _root = null;
            steps.Add(MakeStep(steps.Count, $"Deleted Last Key {key}",
                $"Removed {key}; the (2,4) tree is now empty.",
                2, Array.Empty<string>(), opCode: "DELETE", badge: "Empty"));
            deleted = true;
            return;
        }

        var underflow = leafNode != _root && leafNode.Keys.Count == 0;
        steps.Add(MakeStep(steps.Count, underflow ? $"Underflow after deleting {key}" : $"Deleted {key}",
            underflow
                ? $"Removed {key}, causing underflow in leaf node."
                : $"Removed {key} from leaf {leafNode.FormatKeys()}.",
            2, new[] { leafNode.Id }, opCode: underflow ? "UNDERFLOW" : "DELETE",
            badge: underflow ? "Underflow" : "Removed",
            stateOverrides: new Dictionary<string, string>
            {
                [leafNode.Id] = underflow ? SemanticStates.Violation : SemanticStates.Accepted
            }));

        if (underflow)
        {
            ResolveUnderflow(leafNode, steps, ops);
        }

        deleted = true;
    }

    private void ResolveUnderflow(TwoFourNode node, List<TraceStep<TreeStepState>> steps, List<string> ops)
    {
        while (node != _root && node.Keys.Count == 0)
        {
            var parent = node.Parent!;
            var idx = parent.Children.IndexOf(node);
            var hasLeft = idx > 0;
            var hasRight = idx + 1 < parent.Children.Count;
            var leftCanTransfer = hasLeft && parent.Children[idx - 1].Keys.Count >= 2;
            var rightCanTransfer = hasRight && parent.Children[idx + 1].Keys.Count >= 2;

            // 1. Transfer before Fusion, honoring TwoFourUnderflowSibling
            bool transferFromRight = false;
            bool transferFromLeft = false;

            if (Ruleset.TwoFourUnderflowSibling == TwoFourUnderflowSibling.RightSiblingFirst)
            {
                if (rightCanTransfer) transferFromRight = true;
                else if (leftCanTransfer) transferFromLeft = true;
            }
            else
            {
                if (leftCanTransfer) transferFromLeft = true;
                else if (rightCanTransfer) transferFromRight = true;
            }

            if (transferFromRight)
            {
                var rightSib = parent.Children[idx + 1];
                var sep = parent.Keys[idx];
                var borrowed = rightSib.Keys[0];

                node.Keys.Add(sep);
                parent.Keys[idx] = borrowed;
                rightSib.Keys.RemoveAt(0);

                if (!rightSib.IsLeaf)
                {
                    var movedChild = rightSib.Children[0];
                    rightSib.Children.RemoveAt(0);
                    movedChild.Parent = node;
                    node.Children.Add(movedChild);
                }

                ops.Add($"TRANSFER_RIGHT {borrowed}");
                steps.Add(MakeStep(steps.Count, $"Transfer from Right Sibling ({borrowed})",
                    $"Moved separator {sep} down into {node.FormatKeys()} and promoted {borrowed} from right sibling to {parent.FormatKeys()}.",
                    4, new[] { node.Id, parent.Id, rightSib.Id }, opCode: "TRANSFER", badge: "Transfer",
                    stateOverrides: new Dictionary<string, string>
                    {
                        [node.Id] = SemanticStates.Accepted,
                        [parent.Id] = SemanticStates.Active,
                        [rightSib.Id] = SemanticStates.Visited
                    }));
                return;
            }

            if (transferFromLeft)
            {
                var leftSib = parent.Children[idx - 1];
                var sep = parent.Keys[idx - 1];
                var borrowed = leftSib.Keys[^1];

                node.Keys.Insert(0, sep);
                parent.Keys[idx - 1] = borrowed;
                leftSib.Keys.RemoveAt(leftSib.Keys.Count - 1);

                if (!leftSib.IsLeaf)
                {
                    var movedChild = leftSib.Children[^1];
                    leftSib.Children.RemoveAt(leftSib.Children.Count - 1);
                    movedChild.Parent = node;
                    node.Children.Insert(0, movedChild);
                }

                ops.Add($"TRANSFER_LEFT {borrowed}");
                steps.Add(MakeStep(steps.Count, $"Transfer from Left Sibling ({borrowed})",
                    $"Moved separator {sep} down into {node.FormatKeys()} and promoted {borrowed} from left sibling to {parent.FormatKeys()}.",
                    4, new[] { node.Id, parent.Id, leftSib.Id }, opCode: "TRANSFER", badge: "Transfer",
                    stateOverrides: new Dictionary<string, string>
                    {
                        [node.Id] = SemanticStates.Accepted,
                        [parent.Id] = SemanticStates.Active,
                        [leftSib.Id] = SemanticStates.Visited
                    }));
                return;
            }

            // 2. Fusion honoring TwoFourUnderflowSibling
            var fuseWithRight = Ruleset.TwoFourUnderflowSibling == TwoFourUnderflowSibling.RightSiblingFirst
                ? hasRight
                : !hasLeft;

            TwoFourNode fused;
            int sepKey;
            if (fuseWithRight)
            {
                var rightSib = parent.Children[idx + 1];
                sepKey = parent.Keys[idx];

                node.Keys.Add(sepKey);
                node.Keys.AddRange(rightSib.Keys);
                foreach (var ch in rightSib.Children)
                {
                    ch.Parent = node;
                    node.Children.Add(ch);
                }

                parent.Keys.RemoveAt(idx);
                parent.Children.RemoveAt(idx + 1);
                fused = node;
            }
            else
            {
                var leftSib = parent.Children[idx - 1];
                sepKey = parent.Keys[idx - 1];

                leftSib.Keys.Add(sepKey);
                leftSib.Keys.AddRange(node.Keys);
                foreach (var ch in node.Children)
                {
                    ch.Parent = leftSib;
                    leftSib.Children.Add(ch);
                }

                parent.Keys.RemoveAt(idx - 1);
                parent.Children.RemoveAt(idx);
                fused = leftSib;
            }

            ops.Add($"FUSION {sepKey}");

            if (parent == _root && parent.Keys.Count == 0)
            {
                _root = fused;
                _root.Parent = null;
                ops.Add("ROOT_COLLAPSE");
                steps.Add(MakeStep(steps.Count, $"Fusion ({sepKey}) & Root Collapse",
                    $"Pulled root separator {sepKey} down into {fused.FormatKeys()}; empty root collapsed, making {fused.FormatKeys()} the new root.",
                    5, new[] { fused.Id }, opCode: "ROOT_COLLAPSE", badge: "Collapse",
                    stateOverrides: new Dictionary<string, string> { [fused.Id] = SemanticStates.Accepted }));
                return;
            }

            var parentUnderflow = parent != _root && parent.Keys.Count == 0;
            steps.Add(MakeStep(steps.Count, parentUnderflow ? $"Fusion ({sepKey}) -> Cascading Underflow" : $"Fusion via Separator {sepKey}",
                $"Merged adjacent sibling with separator {sepKey} into {fused.FormatKeys()}.",
                5, new[] { fused.Id, parent.Id }, opCode: "FUSION",
                badge: parentUnderflow ? "Cascade" : "Fusion",
                stateOverrides: new Dictionary<string, string>
                {
                    [fused.Id] = SemanticStates.Accepted,
                    [parent.Id] = parentUnderflow ? SemanticStates.Violation : SemanticStates.Visited
                }));

            node = parent;
        }
    }

    private LayoutTreeNode ToLayoutNode(TwoFourNode node, IReadOnlyDictionary<string, string>? stateOverrides)
    {
        var semantic = stateOverrides is not null && stateOverrides.TryGetValue(node.Id, out var s)
            ? s
            : SemanticStates.Default;

        var multiKeys = node.Keys.Count == 0
            ? new List<string> { "∅" }
            : node.Keys.Select(k => k.ToString()).ToList();

        var children = new List<LayoutTreeNode>();
        if (!node.IsLeaf)
        {
            foreach (var ch in node.Children)
            {
                children.Add(ToLayoutNode(ch, stateOverrides));
            }
        }
        else if (Ruleset.ShowExternalSentinels)
        {
            var sentinelCount = Math.Max(2, node.Keys.Count + 1);
            for (var i = 0; i < sentinelCount; i++)
            {
                children.Add(TreeLayoutHelpers.CreateSentinel(node.Id, $"s{i}"));
            }
        }

        return new LayoutTreeNode
        {
            Id = node.Id,
            Label = string.Join(" | ", multiKeys),
            Shape = NodeShapes.MultiKey,
            SemanticState = semantic,
            Width = Math.Max(48, multiKeys.Count * 36),
            Height = 36,
            MultiKeys = multiKeys,
            Children = children
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

    private static int CountKeys(TwoFourNode? node)
    {
        if (node is null) return 0;
        var sum = node.Keys.Count;
        foreach (var ch in node.Children) sum += CountKeys(ch);
        return sum;
    }

    private static bool VerifySubtreeParents(TwoFourNode node)
    {
        foreach (var ch in node.Children)
        {
            if (ch.Parent != node || !VerifySubtreeParents(ch))
                return false;
        }
        return true;
    }

    private static bool VerifyNode(TwoFourNode node, int depth, int? min, int? max, ref int? leafDepth)
    {
        if (node.Keys.Count < 1 || node.Keys.Count > 3) return false;

        for (var i = 0; i < node.Keys.Count; i++)
        {
            var k = node.Keys[i];
            if (min.HasValue && k <= min.Value) return false;
            if (max.HasValue && k >= max.Value) return false;
            if (i > 0 && k <= node.Keys[i - 1]) return false;
        }

        if (node.IsLeaf)
        {
            if (leafDepth is null) leafDepth = depth;
            else if (leafDepth.Value != depth) return false;
            return true;
        }

        if (node.Children.Count != node.Keys.Count + 1) return false;

        for (var i = 0; i < node.Children.Count; i++)
        {
            var childMin = i == 0 ? min : node.Keys[i - 1];
            var childMax = i == node.Keys.Count ? max : node.Keys[i];
            if (!VerifyNode(node.Children[i], depth + 1, childMin, childMax, ref leafDepth))
                return false;
        }

        return true;
    }
}
