using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Algorithms.Symbolic;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Analysis;

/// <summary>
/// Inline badge showing a single counted elementary operation token and its execution frequency.
/// </summary>
public sealed record InlineOpBadge(
    string Token,
    string Frequency,
    string? Description = null);

/// <summary>
/// A single line of annotated pseudocode linked to its originating AST/Blockly block.
/// </summary>
public sealed record AnnotatedCodeLine(
    int LineNumber,
    string? BlockId,
    int IndentLevel,
    string CodeText,
    IReadOnlyList<InlineOpBadge> InlineBadges,
    string LineCostLatex,
    string? BoxGroupId = null,
    string? BoxHeaderLabel = null);

/// <summary>
/// Summary of a nested inner-loop box (e.g. T_in(i) = 4i + 2, T_in2(j) = 4j + 2).
/// </summary>
public sealed record InnerLoopBox(
    string BoxGroupId,
    string Label,
    string Latex,
    string Plain,
    SymbolicExpr Expression,
    int NestingDepth,
    string? BlockId = null);

/// <summary>
/// Exact symbolic runtime analysis for either Worst Case or Best Case.
/// </summary>
public sealed record CaseAnalysis(
    string CaseName,
    SymbolicExpr Expression,
    string Latex,
    string Plain,
    IReadOnlyList<DerivationStep> DerivationSteps,
    IReadOnlyList<AnnotatedCodeLine> AnnotatedLines,
    IReadOnlyList<InnerLoopBox> InnerBoxes);

/// <summary>
/// Formal asymptotic witness proof (constants C and n0 with step-by-step LaTeX verification).
/// </summary>
public sealed record WitnessProof(
    string BoundNotation,
    BigRational C,
    int N0,
    IReadOnlyList<string> StepsLatex,
    string Summary);

/// <summary>
/// Complete Big-O, Big-Omega, and Big-Theta classification with formal witness proofs.
/// </summary>
public sealed record AsymptoticResult(
    string BigO,
    string BigOLatex,
    string BigOmega,
    string BigOmegaLatex,
    string? BigTheta,
    string BigThetaLatex,
    bool IsTight,
    WitnessProof BigOProof,
    WitnessProof BigOmegaProof,
    string BigThetaSummary)
{
    public string BigThetaDisplay => BigTheta ?? "Not tight";
}

/// <summary>
/// Single sampled coordinate for complexity growth charts (compatible with chartInterop.js).
/// </summary>
public sealed record GrowthPoint(int N, double Value);

/// <summary>
/// Named curve series for complexity growth charts (compatible with chartInterop.js).
/// </summary>
public sealed record GrowthChartSeries(
    string Label,
    bool IsPrimary,
    IReadOnlyList<GrowthPoint> Points,
    string? Stroke = null);

/// <summary>
/// Complete result returned by IterativeAnalyzer.Analyze().
/// </summary>
public sealed record RuntimeAnalysisResult(
    string FunctionName,
    string PrimaryVariable,
    CostRulesProfile Profile,
    CaseAnalysis WorstCase,
    CaseAnalysis BestCase,
    AsymptoticResult Asymptotic,
    IReadOnlyList<GrowthChartSeries> GrowthChartSeries,
    RecurrenceAnalysisResult? Recurrence = null);

/// <summary>
/// Specification for a recurrence relation:
/// Divide-and-conquer: T(n) = BranchesA * T(n / DivideByB) + f(n)
/// Subtraction:        T(n) = BranchesA * T(n - SubtractC) + f(n)
/// where f(n) = Theta(n^WorkPolyDegreeK * (log n)^WorkLogPowerP).
/// </summary>
public sealed record RecurrenceSpec(
    int BaseN,
    int BaseCost,
    int BranchesA,
    int? DivideByB,
    int? SubtractC,
    string NonRecursiveWorkLatex,
    int WorkPolyDegreeK,
    int WorkLogPowerP = 0,
    int LogBase = 2,
    SymbolicExpr? ExactWorkExpr = null);

/// <summary>
/// A single step in iterative recurrence unrolling (Base Case, i=1, i=2, i=3, general i, stopping, closed form).
/// </summary>
public sealed record UnrollingStep(
    string Label,
    string Latex,
    string Plain,
    string? Explanation = null);

/// <summary>
/// Master Theorem / Subtraction Master Theorem / Akra-Bazzi / Recursion Tree analysis summary.
/// </summary>
public sealed record MasterTheoremResult(
    string TheoremName,
    string CaseLabel,
    string ComparisonLatex,
    string BigO,
    string BigOLatex,
    string BigTheta,
    string BigThetaLatex,
    string Explanation,
    string RecursionTreeWorkRatioLatex,
    string RecursionTreeExplanation,
    string AkraBazziNote);

/// <summary>
/// Complete solution of a recurrence relation including iterative unrolling and Master Theorem analysis.
/// </summary>
public sealed record RecurrenceAnalysisResult(
    RecurrenceSpec Spec,
    string RecurrenceLatex,
    string RecurrencePlain,
    IReadOnlyList<UnrollingStep> UnrollingSteps,
    MasterTheoremResult MasterTheorem,
    string ClosedFormLatex,
    string ClosedFormPlain,
    string BigO,
    string BigTheta,
    SymbolicExpr? ClosedFormExpr = null);
