using SuperCsStudent.Structures.Config;
using SuperCsStudent.Structures.Trees;

namespace SuperCsStudent.Structures.Tests;

public sealed class AvlAndSplayTests
{
    [Fact]
    public void Avl_Insert54ThenDelete32_EmitsZigZagRightAndEqualHeightZigLeft()
    {
        var avl = new AvlTracer(TreeRuleset.CourseStandard);
        avl.RunScript("insert 44, 17, 78, 32, 50, 88, 48, 62");

        Assert.True(avl.VerifyAvlInvariants());
        Assert.Equal(44, avl.RootKey);

        // Insert 54 -> ZIGZAGRIGHT with 62 replacing 78
        var insert54 = avl.Insert(54);
        Assert.True(insert54.IsSuccess);
        Assert.Contains("ZIGZAGRIGHT", insert54.OperationLog);
        Assert.True(avl.VerifyAvlInvariants());
        Assert.Equal(44, avl.RootKey);
        Assert.Equal(new[] { "44", "17", "32", "62", "50", "48", "54", "78", "88" }, avl.GetPreOrder());

        // Delete 32 -> equal-height tie-break picks 78 (ZIGLEFT), making 62 root
        var delete32 = avl.Delete(32);
        Assert.True(delete32.IsSuccess);
        Assert.Contains("ZIGLEFT", delete32.OperationLog);
        Assert.True(avl.VerifyAvlInvariants());
        Assert.Equal(62, avl.RootKey);
        Assert.True(delete32.Steps[^1].State.ParentLinksHealthy);
    }

    [Fact]
    public void Avl_OppositeSideDoubleRotationTieBreaker_ProducesValidAlternative()
    {
        var ruleset = TreeRuleset.CourseStandard with
        {
            AvlEqualHeightTieBreaker = AvlEqualHeightTieBreaker.OppositeSideDoubleRotation
        };
        var avl = new AvlTracer(ruleset);
        avl.RunScript("insert 44, 17, 78, 32, 50, 88, 48, 62, 54");

        var delete32 = avl.Delete(32);
        Assert.True(delete32.IsSuccess);
        Assert.Contains("ZIGZAGLEFT", delete32.OperationLog);
        Assert.True(avl.VerifyAvlInvariants());
        Assert.Equal(50, avl.RootKey);
    }

    [Fact]
    public void Avl_HeightConventionAndCascadingDeletions_PreserveAllInvariants()
    {
        var avl = new AvlTracer(TreeRuleset.CourseStandard with { HeightConvention = HeightConvention.LeafIsZero });
        // Build a deep AVL tree and delete several leaves to trigger cascading upward rotations
        var keys = new[] { 50, 25, 75, 15, 35, 65, 85, 10, 20, 30, 40, 60, 70, 80, 90, 5, 28, 32, 45, 55, 68 };
        foreach (var k in keys)
        {
            Assert.True(avl.Insert(k).IsSuccess);
            Assert.True(avl.VerifyAvlInvariants());
        }

        // Verify height badge reflects LeafIsZero on leaf nodes
        var snap = avl.BuildSnapshot();
        Assert.Contains(snap.Scene.Nodes, n => n.Badge is not null && n.Badge.StartsWith("h=0"));

        foreach (var del in new[] { 90, 85, 80, 75, 70, 68, 65, 60, 55, 50 })
        {
            Assert.True(avl.Delete(del).IsSuccess);
            Assert.True(avl.VerifyAvlInvariants());
        }
    }

    [Fact]
    public void Splay_Insert1324_Splays4ToRootWithExpectedRotations()
    {
        var splay = new SplayTracer(TreeRuleset.CourseStandard);
        var timeline = splay.RunScript("insert 1, 3, 2, 4");

        Assert.True(timeline.IsSuccess);
        Assert.Equal(4, splay.RootKey);
        Assert.True(splay.VerifyBstInvariants());
        Assert.Contains("ZIGLEFT", timeline.OperationLog);
        Assert.Contains("ZIGZAGRIGHT", timeline.OperationLog);
        Assert.Contains("ZIGZIGLEFT", timeline.OperationLog);
    }

    [Fact]
    public void Splay_Delete8_HonorsAllThreeDeletionStrategies()
    {
        const string setup = "insert 12, 10, 4, 7, 6, 8";

        // Strategy 1a: BstDeleteThenSplayParent with InOrderPredecessor -> root 6
        var s1Pred = new SplayTracer(TreeRuleset.CourseStandard with
        {
            SplayDeleteStrategy = SplayDeleteStrategy.BstDeleteThenSplayParent,
            DeletionReplacement = DeletionReplacement.InOrderPredecessor
        });
        s1Pred.RunScript(setup);
        var del1Pred = s1Pred.Delete(8);
        Assert.True(del1Pred.IsSuccess);
        Assert.True(s1Pred.VerifyBstInvariants());
        Assert.Equal(6, s1Pred.RootKey);
        Assert.Equal(new[] { "4", "6", "7", "10", "12" }, s1Pred.GetInOrder());

        // Strategy 1b: BstDeleteThenSplayParent with InOrderSuccessor -> valid BST + splay
        var s1Succ = new SplayTracer(TreeRuleset.CourseStandard with
        {
            SplayDeleteStrategy = SplayDeleteStrategy.BstDeleteThenSplayParent,
            DeletionReplacement = DeletionReplacement.InOrderSuccessor
        });
        s1Succ.RunScript(setup);
        var del1Succ = s1Succ.Delete(8);
        Assert.True(del1Succ.IsSuccess);
        Assert.True(s1Succ.VerifyBstInvariants());
        Assert.Equal(10, s1Succ.RootKey);
        Assert.Equal(new[] { "4", "6", "7", "10", "12" }, s1Succ.GetInOrder());

        // Strategy 2: SplayTargetRemoveThenSplayMaxLeft -> root 7
        var s2MaxLeft = new SplayTracer(TreeRuleset.CourseStandard with
        {
            SplayDeleteStrategy = SplayDeleteStrategy.SplayTargetRemoveThenSplayMaxLeft
        });
        s2MaxLeft.RunScript(setup);
        var del2 = s2MaxLeft.Delete(8);
        Assert.True(del2.IsSuccess);
        Assert.True(s2MaxLeft.VerifyBstInvariants());
        Assert.Equal(7, s2MaxLeft.RootKey);
        Assert.Equal(new[] { "4", "6", "7", "10", "12" }, s2MaxLeft.GetInOrder());

        // Strategy 3: SplayTargetRemoveThenSplayMinRight -> root 10
        var s3MinRight = new SplayTracer(TreeRuleset.CourseStandard with
        {
            SplayDeleteStrategy = SplayDeleteStrategy.SplayTargetRemoveThenSplayMinRight
        });
        s3MinRight.RunScript(setup);
        var del3 = s3MinRight.Delete(8);
        Assert.True(del3.IsSuccess);
        Assert.True(s3MinRight.VerifyBstInvariants());
        Assert.Equal(10, s3MinRight.RootKey);
        Assert.Equal(new[] { "4", "6", "7", "10", "12" }, s3MinRight.GetInOrder());
    }

    [Fact]
    public void Splay_SearchHitAndMiss_SplayCorrectTargetNodes()
    {
        var splay = new SplayTracer();
        splay.RunScript("insert 10, 20, 30, 40, 50");

        // Hit splays found node to root
        var hit = splay.Search(20);
        Assert.True(hit.IsSuccess);
        Assert.Equal(20, splay.RootKey);
        Assert.True(splay.VerifyBstInvariants());

        // Miss splays last visited node to root
        var miss = splay.Search(35);
        Assert.False(miss.IsSuccess);
        Assert.True(splay.RootKey == 30 || splay.RootKey == 40);
        Assert.True(splay.VerifyBstInvariants());
    }

    [Fact]
    public void Avl_AllFourRotations_EmitExactCodes()
    {
        var rr = new AvlTracer();
        Assert.Contains("ZIGLEFT", rr.RunScript("insert 10, 20, 30").OperationLog);
        Assert.True(rr.VerifyAvlInvariants());

        var ll = new AvlTracer();
        Assert.Contains("ZIGRIGHT", ll.RunScript("insert 30, 20, 10").OperationLog);
        Assert.True(ll.VerifyAvlInvariants());

        var rl = new AvlTracer();
        Assert.Contains("ZIGZAGLEFT", rl.RunScript("insert 10, 30, 20").OperationLog);
        Assert.True(rl.VerifyAvlInvariants());

        var lr = new AvlTracer();
        Assert.Contains("ZIGZAGRIGHT", lr.RunScript("insert 30, 10, 20").OperationLog);
        Assert.True(lr.VerifyAvlInvariants());
    }

    [Fact]
    public void Splay_Rotate14Preset_ExecutesZigZigAndZigZagCleanly()
    {
        var splay = new SplayTracer();
        var timeline = splay.RunScript("insert 50, 40, 30, 20, 10; search 50; search 14");

        Assert.True(splay.VerifyBstInvariants());
        Assert.Contains("ZIGZIGRIGHT", timeline.OperationLog);
        Assert.Contains("ZIGZIGLEFT", timeline.OperationLog);
    }
}
