namespace SuperCsStudent.Algorithms.Config;

public enum CostProfilePreset
{
    StandardUnitCost,
    StrictSeparateOp,
    Custom
}

/// <summary>
/// Iteration counting convention for multiplicative loops (e.g. i = 1; i &lt;= n; i *= b).
/// </summary>
public enum LogLoopConvention
{
    /// <summary>
    /// Exact logarithmic count: I = log_b(n), condition checks = log_b(n) + 1.
    /// </summary>
    ExactLogBase,

    /// <summary>
    /// Inclusive floor convention: I = log_b(n) + 1, condition checks = log_b(n) + 2.
    /// </summary>
    FloorLogPlusOne
}

/// <summary>
/// Iteration counting convention for strided additive loops (e.g. i = 1; i &lt;= n; i += s).
/// </summary>
public enum StepLoopConvention
{
    /// <summary>
    /// Algebraic ratio convention: I = (U - L + 1) / s (gives n / s for i = 1..n).
    /// </summary>
    AlgebraicRatio,

    /// <summary>
    /// Ceiling-offset convention: I = (U - L) / s + 1 (gives n / s + (s - 1) / s for i = 1..n).
    /// </summary>
    CeilingOffset
}

/// <summary>
/// Configurable elementary-operation cost model used by the runtime analyzer.
/// </summary>
public sealed record CostRulesProfile
{
    public CostProfilePreset Preset { get; init; } = CostProfilePreset.StandardUnitCost;

    /// <summary>Cost of unary increment/decrement (++ / --). Default: 1 (configurable 1 or 2).</summary>
    public int UnaryIncrementCost { get; init; } = 1;

    /// <summary>Cost of compound assignment (+=, -=, *=, /=). Default: 1 (configurable 1 or 2).</summary>
    public int CompoundAssignCost { get; init; } = 1;

    /// <summary>Cost of the return keyword itself (expression ops still counted). Default: 0 (0 or 1).</summary>
    public int ReturnKeywordCost { get; init; } = 0;

    /// <summary>Cost of uninitialized variable declarations. Default: 0 (0 or 1).</summary>
    public int DeclarationCost { get; init; } = 0;

    /// <summary>Cost of array element indexing (array[i]). Default: 1 (0 or 1).</summary>
    public int ArrayIndexingCost { get; init; } = 1;

    /// <summary>Cost of a non-recursive function call. Default: 1 (0 or 1).</summary>
    public int FunctionCallCost { get; init; } = 1;

    /// <summary>
    /// Whether arithmetic inside loop bounds (e.g. i &lt;= n + 10, i &lt;= n * 5, i &lt;= n * n)
    /// is counted separately on each condition evaluation. Default: false.
    /// </summary>
    public bool CountLoopBoundArithmetic { get; init; } = false;

    /// <summary>Iteration convention for multiplicative (logarithmic) loops. Default: ExactLogBase.</summary>
    public LogLoopConvention LogLoopConvention { get; init; } = LogLoopConvention.ExactLogBase;

    /// <summary>Iteration convention for strided additive loops. Default: AlgebraicRatio.</summary>
    public StepLoopConvention StepLoopConvention { get; init; } = StepLoopConvention.AlgebraicRatio;

    /// <summary>Additional cost for logical connectives (&amp;&amp;, ||). Default: 0 (0 or 1).</summary>
    public int LogicalOperatorCost { get; init; } = 0;

    /// <summary>Comparison cost per switch case checked. Default: 1.</summary>
    public int SwitchCaseComparisonCost { get; init; } = 1;

    public static CostRulesProfile StandardUnitCost { get; } = new()
    {
        Preset = CostProfilePreset.StandardUnitCost,
        UnaryIncrementCost = 1,
        CompoundAssignCost = 1,
        ReturnKeywordCost = 0,
        DeclarationCost = 0,
        ArrayIndexingCost = 1,
        FunctionCallCost = 1,
        CountLoopBoundArithmetic = false,
        LogLoopConvention = LogLoopConvention.ExactLogBase,
        StepLoopConvention = StepLoopConvention.AlgebraicRatio,
        LogicalOperatorCost = 0,
        SwitchCaseComparisonCost = 1
    };

    /// <summary>
    /// Strict separate-operation model counting increment/compound assign as read+op+write (2 ops),
    /// return/declaration keywords as 1 op, bound arithmetic separately, logical ops as 1, and floor+1 logs.
    /// </summary>
    public static CostRulesProfile StrictSeparateOp { get; } = new()
    {
        Preset = CostProfilePreset.StrictSeparateOp,
        UnaryIncrementCost = 2,
        CompoundAssignCost = 2,
        ReturnKeywordCost = 1,
        DeclarationCost = 1,
        ArrayIndexingCost = 1,
        FunctionCallCost = 1,
        CountLoopBoundArithmetic = true,
        LogLoopConvention = LogLoopConvention.FloorLogPlusOne,
        StepLoopConvention = StepLoopConvention.CeilingOffset,
        LogicalOperatorCost = 1,
        SwitchCaseComparisonCost = 1
    };

    public static CostRulesProfile FromPreset(CostProfilePreset preset) => preset switch
    {
        CostProfilePreset.StandardUnitCost => StandardUnitCost,
        CostProfilePreset.StrictSeparateOp => StrictSeparateOp,
        CostProfilePreset.Custom => StandardUnitCost with { Preset = CostProfilePreset.Custom },
        _ => StandardUnitCost
    };
}
