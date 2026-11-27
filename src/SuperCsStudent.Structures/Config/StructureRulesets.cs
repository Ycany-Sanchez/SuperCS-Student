namespace SuperCsStudent.Structures.Config;

public enum DeletionReplacement
{
    InOrderSuccessor,
    InOrderPredecessor
}

public enum HeightConvention
{
    LeafIsOne,
    LeafIsZero
}

public enum AvlEqualHeightTieBreaker
{
    SameSideSingleRotation,
    OppositeSideDoubleRotation
}

public enum SplayDeleteStrategy
{
    BstDeleteThenSplayParent,
    SplayTargetRemoveThenSplayMaxLeft,
    SplayTargetRemoveThenSplayMinRight
}

public enum TwoFourSplitPromotion
{
    ThirdKeyRightMiddle,
    SecondKeyLeftMiddle
}

public enum TwoFourUnderflowSibling
{
    RightSiblingFirst,
    LeftSiblingFirst
}

public enum RedBlackRestructureMode
{
    TrinodeRestructuring,
    StandardRotations
}

public enum RedBlackCompareView
{
    None,
    TwoFourIsomorphism,
    SplayTree
}

public enum NeighborOrder
{
    AlphabeticalAsc,
    AlphabeticalDesc,
    InsertionOrder
}

public enum DfsMode
{
    Recursive,
    IterativeStack
}

public enum BfsMode
{
    QueueBased,
    LevelByLevel
}

public sealed record TreeRuleset(
    string Name = "CourseStandard",
    DeletionReplacement DeletionReplacement = DeletionReplacement.InOrderSuccessor,
    HeightConvention HeightConvention = HeightConvention.LeafIsOne,
    bool ShowExternalSentinels = false,
    AvlEqualHeightTieBreaker AvlEqualHeightTieBreaker = AvlEqualHeightTieBreaker.SameSideSingleRotation,
    SplayDeleteStrategy SplayDeleteStrategy = SplayDeleteStrategy.BstDeleteThenSplayParent,
    TwoFourSplitPromotion TwoFourSplitPromotion = TwoFourSplitPromotion.ThirdKeyRightMiddle,
    TwoFourUnderflowSibling TwoFourUnderflowSibling = TwoFourUnderflowSibling.RightSiblingFirst,
    RedBlackRestructureMode RedBlackRestructureMode = RedBlackRestructureMode.TrinodeRestructuring,
    bool ShowTwoFourIsomorphism = false)
{
    public static TreeRuleset CourseStandard { get; } = new(
        Name: "CourseStandard",
        DeletionReplacement: DeletionReplacement.InOrderSuccessor,
        HeightConvention: HeightConvention.LeafIsOne,
        ShowExternalSentinels: false,
        AvlEqualHeightTieBreaker: AvlEqualHeightTieBreaker.SameSideSingleRotation,
        SplayDeleteStrategy: SplayDeleteStrategy.BstDeleteThenSplayParent,
        TwoFourSplitPromotion: TwoFourSplitPromotion.ThirdKeyRightMiddle,
        TwoFourUnderflowSibling: TwoFourUnderflowSibling.RightSiblingFirst,
        RedBlackRestructureMode: RedBlackRestructureMode.TrinodeRestructuring,
        ShowTwoFourIsomorphism: false);

    public static TreeRuleset ClassicTextbook { get; } = new(
        Name: "ClassicTextbook",
        DeletionReplacement: DeletionReplacement.InOrderPredecessor,
        HeightConvention: HeightConvention.LeafIsOne,
        ShowExternalSentinels: false,
        AvlEqualHeightTieBreaker: AvlEqualHeightTieBreaker.SameSideSingleRotation,
        SplayDeleteStrategy: SplayDeleteStrategy.SplayTargetRemoveThenSplayMaxLeft,
        TwoFourSplitPromotion: TwoFourSplitPromotion.SecondKeyLeftMiddle,
        TwoFourUnderflowSibling: TwoFourUnderflowSibling.LeftSiblingFirst,
        RedBlackRestructureMode: RedBlackRestructureMode.TrinodeRestructuring,
        ShowTwoFourIsomorphism: false);

    public static TreeRuleset StandardRotations { get; } = new(
        Name: "StandardRotations",
        DeletionReplacement: DeletionReplacement.InOrderSuccessor,
        HeightConvention: HeightConvention.LeafIsZero,
        ShowExternalSentinels: true,
        AvlEqualHeightTieBreaker: AvlEqualHeightTieBreaker.SameSideSingleRotation,
        SplayDeleteStrategy: SplayDeleteStrategy.SplayTargetRemoveThenSplayMaxLeft,
        TwoFourSplitPromotion: TwoFourSplitPromotion.ThirdKeyRightMiddle,
        TwoFourUnderflowSibling: TwoFourUnderflowSibling.RightSiblingFirst,
        RedBlackRestructureMode: RedBlackRestructureMode.StandardRotations,
        ShowTwoFourIsomorphism: true);
}

public sealed record GraphRuleset(
    string Name = "CourseStandard",
    NeighborOrder NeighborOrder = NeighborOrder.AlphabeticalAsc,
    DfsMode DfsMode = DfsMode.Recursive,
    BfsMode BfsMode = BfsMode.QueueBased,
    bool TraverseDisconnected = false,
    bool ShowEdgeClassification = true)
{
    public static GraphRuleset CourseStandard { get; } = new(
        Name: "CourseStandard",
        NeighborOrder: NeighborOrder.AlphabeticalAsc,
        DfsMode: DfsMode.Recursive,
        BfsMode: BfsMode.QueueBased,
        TraverseDisconnected: false,
        ShowEdgeClassification: true);

    public static GraphRuleset ClassicTextbook { get; } = new(
        Name: "ClassicTextbook",
        NeighborOrder: NeighborOrder.AlphabeticalAsc,
        DfsMode: DfsMode.Recursive,
        BfsMode: BfsMode.LevelByLevel,
        TraverseDisconnected: true,
        ShowEdgeClassification: true);

    public static GraphRuleset Default => CourseStandard;
}
