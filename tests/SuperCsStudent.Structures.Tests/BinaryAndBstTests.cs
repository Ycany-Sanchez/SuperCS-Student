using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Structures.Config;
using SuperCsStudent.Structures.Presets;
using SuperCsStudent.Structures.Trees;

namespace SuperCsStudent.Structures.Tests;

public sealed class BinaryAndBstTests
{
    [Fact]
    public void BinaryTree_TraversalsAndHeight_MatchExpectedSequences()
    {
        var preset = StructurePresets.GetByKey("binary-traversals")!;
        var tree = StructurePresets.BuildBinaryTreeFromPreset(preset, TreeRuleset.CourseStandard);

        Assert.Equal(10, tree.Count);
        Assert.True(tree.VerifyParentLinks());
        Assert.Equal(5, tree.GetHeight()); // LeafIsOne

        tree.Ruleset = tree.Ruleset with { HeightConvention = HeightConvention.LeafIsZero };
        Assert.Equal(4, tree.GetHeight()); // LeafIsZero

        var expectedPre = new[] { "G", "B", "A", "F", "D", "S", "K", "J", "M", "P" };
        var expectedIn = new[] { "A", "B", "D", "F", "G", "J", "K", "M", "P", "S" };
        var expectedPost = new[] { "A", "D", "F", "B", "J", "P", "M", "K", "S", "G" };
        var expectedBfs = new[] { "G", "B", "S", "A", "F", "K", "D", "J", "M", "P" };

        var snap = tree.BuildSnapshot();
        Assert.Equal(expectedPre, snap.PreOrder);
        Assert.Equal(expectedIn, snap.InOrder);
        Assert.Equal(expectedPost, snap.PostOrder);
        Assert.Equal(expectedBfs, snap.LevelOrder);
        Assert.True(snap.ParentLinksHealthy);

        var preTrace = tree.TracePreOrder();
        var inTrace = tree.TraceInOrder();
        var postTrace = tree.TracePostOrder();
        var bfsTrace = tree.TraceBreadthFirst();
        var eulerTrace = tree.TraceEulerTour();

        Assert.Equal(10, preTrace.Count);
        Assert.Equal(10, inTrace.Count);
        Assert.Equal(10, postTrace.Count);
        Assert.Equal(10, bfsTrace.Count);
        Assert.Equal(30, eulerTrace.Count); // 3 Euler visits per node (Left, Below, Right)

        Assert.Equal(expectedPre.Select(x => $"VISIT {x}"), preTrace.OperationLog);
        Assert.Equal(expectedIn.Select(x => $"VISIT {x}"), inTrace.OperationLog);
        Assert.Equal(expectedPost.Select(x => $"VISIT {x}"), postTrace.OperationLog);
        Assert.Equal(expectedBfs.Select(x => $"VISIT {x}"), bfsTrace.OperationLog);
        Assert.Equal("LEFT G", eulerTrace.OperationLog[0]);
        Assert.Equal("RIGHT G", eulerTrace.OperationLog[^1]);
    }

    [Fact]
    public void BinaryTree_SentinelsAndRemove_BehaveCorrectly()
    {
        var tree = new BinaryTreeTracer(TreeRuleset.CourseStandard with { ShowExternalSentinels = true });
        Assert.True(tree.AddRoot("A").IsSuccess);
        Assert.True(tree.AddLeft("A", "B").IsSuccess);
        Assert.True(tree.AddRight("A", "C").IsSuccess);
        Assert.True(tree.AddLeft("B", "D").IsSuccess);

        var snap = tree.BuildSnapshot();
        Assert.Contains(snap.Scene.Nodes, n => n.Shape == NodeShapes.Sentinel);

        // Removing internal node with 2 children is rejected
        Assert.False(tree.Remove("A").IsSuccess);

        // Removing node with 1 child promotes its child
        Assert.True(tree.Remove("B").IsSuccess);
        Assert.Equal(new[] { "A", "D", "C" }, tree.GetPreOrder());
        Assert.True(tree.VerifyParentLinks());
    }

    [Fact]
    public void TreeScriptParser_ParsesMultipleBatchFormats()
    {
        var cmds1 = TreeScriptParser.Parse("insert 52, 27, 35; delete 35; search 14");
        Assert.Equal(5, cmds1.Count);
        Assert.Equal((TreeCommandKind.Insert, 52), (cmds1[0].Kind, cmds1[0].Key));
        Assert.Equal((TreeCommandKind.Insert, 27), (cmds1[1].Kind, cmds1[1].Key));
        Assert.Equal((TreeCommandKind.Insert, 35), (cmds1[2].Kind, cmds1[2].Key));
        Assert.Equal((TreeCommandKind.Delete, 35), (cmds1[3].Kind, cmds1[3].Key));
        Assert.Equal((TreeCommandKind.Search, 14), (cmds1[4].Kind, cmds1[4].Key));

        var cmds2 = TreeScriptParser.Parse("i 52 / d 35 / s 14");
        Assert.Equal(3, cmds2.Count);
        Assert.Equal((TreeCommandKind.Insert, 52), (cmds2[0].Kind, cmds2[0].Key));
        Assert.Equal((TreeCommandKind.Delete, 35), (cmds2[1].Kind, cmds2[1].Key));
        Assert.Equal((TreeCommandKind.Search, 14), (cmds2[2].Kind, cmds2[2].Key));
    }

    [Fact]
    public void Bst_31OperationSequence_MatchesExactPreorderUnderSuccessorAndDiffersUnderPredecessor()
    {
        const string script =
            "insert 52, 27, 35, 17, 67, 97, remove 35, insert 19, 55, remove 67, " +
            "insert 30, 22, 34, 47, 18, remove 27, insert 53, 83, 10, 85, 88, 4, " +
            "remove 97, insert 6, 70, remove 52, remove 47, insert 8, remove 22, insert 96, 2";

        var successorBst = new BstTracer(TreeRuleset.CourseStandard);
        var succTimeline = successorBst.RunScript(script);

        Assert.True(succTimeline.IsSuccess);
        Assert.True(successorBst.VerifyBstInvariants());
        Assert.True(succTimeline.Steps[^1].State.ParentLinksHealthy);

        var expectedPreorder = new[]
        {
            "53", "30", "17", "10", "4", "2", "6", "8", "19", "18", "34", "55", "83", "70", "85", "88", "96"
        };
        Assert.Equal(expectedPreorder, successorBst.GetPreOrder());

        // Compare against InOrderPredecessor mode
        var predecessorBst = new BstTracer(TreeRuleset.ClassicTextbook);
        var predTimeline = predecessorBst.RunScript(script);

        Assert.True(predTimeline.IsSuccess);
        Assert.True(predecessorBst.VerifyBstInvariants());
        Assert.True(predTimeline.Steps[^1].State.ParentLinksHealthy);

        // Exact same sorted keys, distinct tree shape because 2-child deletions used predecessor
        Assert.Equal(successorBst.GetInOrder(), predecessorBst.GetInOrder());
        Assert.NotEqual(successorBst.GetPreOrder(), predecessorBst.GetPreOrder());
        Assert.Equal(34, predecessorBst.RootKey); // Predecessor of 52 was 47? Wait: 47 was still there when 52 removed -> root 47 -> then 47 removed -> predecessor of 47 is 34!
    }

    [Fact]
    public void Bst_SearchAndDuplicateHandling_WorkCorrectly()
    {
        var bst = new BstTracer();
        bst.RunScript("insert 20, 10, 30, 5, 15");

        Assert.False(bst.Insert(20).IsSuccess);
        Assert.True(bst.Search(10).IsSuccess);
        Assert.False(bst.Search(25).IsSuccess);
        Assert.False(bst.Delete(99).IsSuccess);
        Assert.True(bst.VerifyBstInvariants());

        Assert.Equal(5, bst.TracePreOrder().Count);
        Assert.Equal(5, bst.TraceInOrder().Count);
        Assert.Equal(5, bst.TracePostOrder().Count);
        Assert.Equal(5, bst.TraceBreadthFirst().Count);
    }

    [Fact]
    public void TreeTraversalTracer_PostOrderWalk_StepsContinuouslyAlongTreeEdgesAndVisitsInPostOrder()
    {
        var preset = StructurePresets.GetByKey("binary-traversals")!;
        var tree = StructurePresets.BuildBinaryTreeFromPreset(preset, TreeRuleset.CourseStandard);
        var expectedPost = new[] { "A", "D", "F", "B", "J", "P", "M", "K", "S", "G" };

        var walk = tree.TraceWalk(TreeTraversalMode.PostOrder);

        Assert.True(walk.Count > expectedPost.Length, $"Expected recursive walk steps > {expectedPost.Length}, got {walk.Count}");

        var visitedOps = walk.OperationLog
            .Where(op => op.StartsWith("VISIT ", StringComparison.Ordinal))
            .Select(op => op["VISIT ".Length..])
            .ToArray();
        Assert.Equal(expectedPost, visitedOps);

        // Every inter-node motion step must travel along a real tree edge (no teleporting)
        var moveSteps = walk.Steps
            .Where(s => s.State.Scene.ActiveMotion is { } m && m.FromNodeId != m.ToNodeId)
            .ToList();
        Assert.NotEmpty(moveSteps);

        foreach (var step in moveSteps)
        {
            var motion = step.State.Scene.ActiveMotion!;
            var hasEdge = step.State.Scene.Edges.Any(e =>
                (e.SourceId == motion.FromNodeId && e.TargetId == motion.ToNodeId) ||
                (e.SourceId == motion.ToNodeId && e.TargetId == motion.FromNodeId));
            Assert.True(hasEdge, $"Step '{step.Title}' teleported from {motion.FromNodeId} to {motion.ToNodeId} without a tree edge.");
        }

        // Final step marks all 10 nodes with #1..#10 visit order sub-labels
        var finalSubLabels = walk.Steps[^1].State.Scene.Nodes
            .Where(n => n.Shape != NodeShapes.Sentinel)
            .Select(n => n.SubLabel)
            .ToHashSet();
        for (var i = 1; i <= 10; i++)
        {
            Assert.Contains($"#{i}", finalSubLabels);
        }

        // Visit steps must not leave an unrelated parent->child edge marked Active (which would fire a second stray dot)
        var visitF = walk.Steps.Single(s => s.Title == "Post-order Visit F");
        Assert.DoesNotContain(visitF.State.Scene.Edges, e => e.SemanticState == SemanticStates.Active);
    }

    [Fact]
    public void TreeTraversalTracer_HandlesEmptySingleNodeAndAllBinaryTreeTypes()
    {
        // Empty binary tree
        var emptyTree = new BinaryTreeTracer();
        var emptyWalk = emptyTree.TraceWalk(TreeTraversalMode.PostOrder);
        Assert.Equal(1, emptyWalk.Count);

        // Single-node BST
        var singleBst = new BstTracer();
        singleBst.Insert(42);
        foreach (var mode in Enum.GetValues<TreeTraversalMode>())
        {
            var w = singleBst.TraceWalk(mode);
            Assert.True(w.Count >= 1);
        }

        // Populated AVL, Splay, and Red-Black (preserving RB node colors)
        var avl = new AvlTracer();
        avl.RunScript("insert 30, 20, 40, 10");
        var avlPre = avl.TraceWalk(TreeTraversalMode.PreOrder);
        Assert.Equal(
            avl.GetPreOrder(),
            avlPre.OperationLog.Where(o => o.StartsWith("VISIT ", StringComparison.Ordinal)).Select(o => o["VISIT ".Length..]).ToArray());

        var splay = new SplayTracer();
        splay.RunScript("insert 20, 10, 30");
        var splayIn = splay.TraceWalk(TreeTraversalMode.InOrder);
        Assert.Equal(
            splay.GetInOrder(),
            splayIn.OperationLog.Where(o => o.StartsWith("VISIT ", StringComparison.Ordinal)).Select(o => o["VISIT ".Length..]).ToArray());

        var rb = new RedBlackTreeTracer();
        rb.RunScript("insert 20, 10, 30, 5");
        var rbPost = rb.TraceWalk(TreeTraversalMode.PostOrder);
        Assert.True(rbPost.Count > 4);
        var rbNodes = rbPost.Steps[^1].State.Scene.Nodes.Where(n => n.Shape != NodeShapes.Sentinel).ToList();
        Assert.All(rbNodes, n => Assert.True(n.SemanticState is SemanticStates.RedNode or SemanticStates.BlackNode));
    }
}
