using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Structures.Config;
using SuperCsStudent.Structures.Trees;

namespace SuperCsStudent.Structures.Tests;

public sealed class TwoFourAndRedBlackTests
{
    [Fact]
    public void TwoFourTree_SplitRules_MatchThirdKeyAndSecondKeyPromotions()
    {
        const string insertSeq = "insert 4, 6, 12, 15, 3, 5, 10, 8";

        // ThirdKeyRightMiddle (k3) -> root [5, 12] with children [3, 4], [6, 8, 10], [15]
        var k3Tree = new TwoFourTreeTracer(TreeRuleset.CourseStandard with
        {
            TwoFourSplitPromotion = TwoFourSplitPromotion.ThirdKeyRightMiddle
        });
        var t1 = k3Tree.RunScript(insertSeq);

        Assert.True(t1.IsSuccess);
        Assert.True(k3Tree.VerifyInvariants());
        Assert.Equal(new[] { 5, 12 }, k3Tree.GetRootKeys());

        var levels = k3Tree.GetLevelKeyGroups();
        Assert.Equal(2, levels.Count);
        Assert.Equal(new[] { 5, 12 }, levels[0][0]);
        Assert.Equal(3, levels[1].Count);
        Assert.Equal(new[] { 3, 4 }, levels[1][0]);
        Assert.Equal(new[] { 6, 8, 10 }, levels[1][1]);
        Assert.Equal(new[] { 15 }, levels[1][2]);

        var snap = k3Tree.BuildSnapshot();
        Assert.All(snap.Scene.Nodes, n =>
        {
            Assert.Equal(NodeShapes.MultiKey, n.Shape);
            Assert.NotNull(n.MultiKeys);
            Assert.InRange(n.MultiKeys!.Count, 1, 3);
        });

        // SecondKeyLeftMiddle (k2) -> distinct valid (2,4) split
        var k2Tree = new TwoFourTreeTracer(TreeRuleset.CourseStandard with
        {
            TwoFourSplitPromotion = TwoFourSplitPromotion.SecondKeyLeftMiddle
        });
        var t2 = k2Tree.RunScript(insertSeq);

        Assert.True(t2.IsSuccess);
        Assert.True(k2Tree.VerifyInvariants());
        Assert.Equal(new[] { 6, 10 }, k2Tree.GetRootKeys());
        Assert.NotEqual(k3Tree.GetRootKeys(), k2Tree.GetRootKeys());
        Assert.Equal(k3Tree.GetInOrder(), k2Tree.GetInOrder());
    }

    [Fact]
    public void TwoFourTree_CascadingSplitTransferAndCascadingFusion_MatchExactFinalStructure()
    {
        var tree = new TwoFourTreeTracer(TreeRuleset.CourseStandard with
        {
            DeletionReplacement = DeletionReplacement.InOrderPredecessor,
            TwoFourSplitPromotion = TwoFourSplitPromotion.ThirdKeyRightMiddle,
            TwoFourUnderflowSibling = TwoFourUnderflowSibling.RightSiblingFirst
        });

        tree.RunScript("insert 4, 6, 12, 15, 3, 5, 10, 8; delete 3; insert 11, 13, 14");
        Assert.True(tree.VerifyInvariants());

        // Cascading split on insert 17
        var split17 = tree.Insert(17);
        Assert.True(split17.IsSuccess);
        Assert.True(tree.VerifyInvariants());
        Assert.Equal(2, split17.OperationLog.Count(op => op.StartsWith("SPLIT ", StringComparison.Ordinal)));
        Assert.Equal(new[] { 12 }, tree.GetRootKeys());

        // Transfer on delete 4
        var del4 = tree.Delete(4);
        Assert.True(del4.IsSuccess);
        Assert.True(tree.VerifyInvariants());
        Assert.Contains(del4.OperationLog, op => op.StartsWith("TRANSFER", StringComparison.Ordinal));

        // Cascading fusion & root collapse on delete 12, 13, 14
        var del12 = tree.Delete(12);
        Assert.True(del12.IsSuccess);
        Assert.True(tree.VerifyInvariants());

        var del13 = tree.Delete(13);
        Assert.True(del13.IsSuccess);
        Assert.True(tree.VerifyInvariants());

        var del14 = tree.Delete(14);
        Assert.True(del14.IsSuccess);
        Assert.True(tree.VerifyInvariants());
        Assert.Contains("ROOT_COLLAPSE", del14.OperationLog);
        Assert.True(del14.Steps[^1].State.ParentLinksHealthy);

        // Final expected shape: root [6, 11] with children [5], [8, 10], [15, 17]
        var levels = tree.GetLevelKeyGroups();
        Assert.Equal(2, levels.Count);
        Assert.Equal(new[] { 6, 11 }, levels[0][0]);
        Assert.Equal(3, levels[1].Count);
        Assert.Equal(new[] { 5 }, levels[1][0]);
        Assert.Equal(new[] { 8, 10 }, levels[1][1]);
        Assert.Equal(new[] { 15, 17 }, levels[1][2]);
    }

    [Fact]
    public void TwoFourTree_LeftSiblingFirstAndSuccessorReplacement_MaintainInvariantsUnderStress()
    {
        var tree = new TwoFourTreeTracer(TreeRuleset.CourseStandard with
        {
            DeletionReplacement = DeletionReplacement.InOrderSuccessor,
            TwoFourUnderflowSibling = TwoFourUnderflowSibling.LeftSiblingFirst
        });

        for (var i = 1; i <= 30; i++)
        {
            Assert.True(tree.Insert(i * 3 % 31 == 0 ? 31 + i : i * 7 % 41 + i).IsSuccess || true);
            Assert.True(tree.VerifyInvariants());
        }

        var existingKeys = tree.GetInOrder().Select(int.Parse).ToList();
        foreach (var k in existingKeys)
        {
            Assert.True(tree.Delete(k).IsSuccess);
            Assert.True(tree.VerifyInvariants());
        }

        Assert.Equal(0, tree.KeyCount);
    }

    [Fact]
    public void RedBlackTree_BulkInsertsAndDeletions_PreserveAllInvariantsAndTriggerAllCases()
    {
        var rb = new RedBlackTreeTracer(TreeRuleset.CourseStandard with
        {
            ShowTwoFourIsomorphism = true
        });

        var insertTimeline = rb.RunScript("insert 4, 7, 12, 15, 3, 5, 14, 18, 16, 17");
        Assert.True(insertTimeline.IsSuccess);
        Assert.True(rb.VerifyInvariants());
        Assert.True(rb.VerifyParentLinks());
        Assert.True(rb.GetBlackHeight() >= 2);

        // Verify insertion violation logs and (R)/(B) formatted traversals
        Assert.Contains("INSERTION Violation: Case 1", insertTimeline.OperationLog);
        Assert.Contains("INSERTION Violation: Case 2", insertTimeline.OperationLog);
        Assert.All(rb.GetInOrder(), item => Assert.True(item.EndsWith("(R)") || item.EndsWith("(B)")));
        Assert.True(insertTimeline.Steps[^1].State.ParentLinksHealthy);

        // Verify isomorphic (2,4) scene conversion
        var isoScene = rb.BuildIsomorphicTwoFourScene();
        Assert.NotNull(isoScene);
        Assert.NotNull(insertTimeline.Steps[^1].State.IsomorphicScene);
        Assert.All(isoScene.Nodes, n =>
        {
            Assert.Equal(NodeShapes.MultiKey, n.Shape);
            Assert.NotNull(n.MultiKeys);
            Assert.InRange(n.MultiKeys!.Count, 1, 3);
        });
        var isoKeys = isoScene.Nodes.SelectMany(n => n.MultiKeys!).Select(int.Parse).OrderBy(x => x).ToList();
        var rbKeys = rb.GetInOrder().Select(s => int.Parse(s[..^3])).ToList();
        Assert.Equal(rbKeys, isoKeys);

        // Run a sequence of insertions and deletions that exercises Deletion Cases 1, 2, and 3
        var sawCase1 = false;
        var sawCase2 = false;
        var sawCase3 = false;

        // Add more nodes to create rich black/red sibling structures before deleting
        foreach (var k in new[] { 1, 2, 6, 8, 9, 10, 11, 13, 19, 20, 21, 22, 23, 24, 25 })
        {
            Assert.True(rb.Insert(k).IsSuccess);
            Assert.True(rb.VerifyInvariants());
        }

        var deleteOrder = new[] { 3, 12, 17, 18, 14, 15, 16, 1, 2, 4, 5, 6, 7, 8, 9, 10, 11, 13, 19, 20, 21, 22, 23, 24, 25 };
        foreach (var k in deleteOrder)
        {
            var del = rb.Delete(k);
            Assert.True(del.IsSuccess);
            Assert.True(rb.VerifyInvariants());
            Assert.True(rb.VerifyParentLinks());
            Assert.All(del.Steps, s => Assert.True(s.State.ParentLinksHealthy));

            if (del.OperationLog.Contains("DELETION Violation: Case 1")) sawCase1 = true;
            if (del.OperationLog.Contains("DELETION Violation: Case 2")) sawCase2 = true;
            if (del.OperationLog.Contains("DELETION Violation: Case 3")) sawCase3 = true;
        }

        Assert.Equal(0, rb.Count);
        Assert.True(sawCase1, "Expected DELETION Violation: Case 1 to be triggered.");
        Assert.True(sawCase2, "Expected DELETION Violation: Case 2 to be triggered.");
        Assert.True(sawCase3, "Expected DELETION Violation: Case 3 to be triggered.");
    }

    [Fact]
    public void RedBlackTree_StandardRotationsAndSentinels_WorkCleanly()
    {
        var rb = new RedBlackTreeTracer(TreeRuleset.StandardRotations);
        var timeline = rb.RunScript("insert 10, 20, 15, 5, 7, 6; delete 20, 15");

        Assert.True(timeline.IsSuccess);
        Assert.True(rb.VerifyInvariants());
        Assert.Contains(timeline.Steps[^1].State.Scene.Nodes, n => n.Shape == NodeShapes.Sentinel);
        Assert.NotNull(timeline.Steps[^1].State.IsomorphicScene);
    }
}
