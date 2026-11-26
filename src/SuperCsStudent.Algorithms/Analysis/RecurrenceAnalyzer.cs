using System.Globalization;
using System.Text.RegularExpressions;
using SuperCsStudent.Algorithms.Ast;
using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Algorithms.Symbolic;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Analysis;

/// <summary>
/// Solves divide-and-conquer and subtraction recurrence relations via:
/// 1) Step-by-step iterative unrolling (Base Case, i=1, i=2, i=3, general i, stopping condition, closed form),
/// 2) Master Theorem (Cases 1, 2, 3 + Extended Logarithmic Case),
/// 3) Subtraction Master Theorem (T(n) = aT(n-c) + f(n)),
/// 4) Recursion Tree work-ratio explanation, and Akra-Bazzi integral confirmation.
/// </summary>
public static partial class RecurrenceAnalyzer
{
    [GeneratedRegex(@"^\s*[a-zA-Z]\w*\s*/\s*(\d+)\s*$")]
    private static partial Regex DividePattern();

    [GeneratedRegex(@"^\s*[a-zA-Z]\w*\s*-\s*(\d+)\s*$")]
    private static partial Regex SubtractPattern();

    public static RecurrenceAnalysisResult Solve(RecurrenceSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (spec.DivideByB is { } b && b >= 2)
        {
            return SolveDivideRecurrence(spec, b);
        }

        if (spec.SubtractC is { } c && c >= 1)
        {
            return SolveSubtractRecurrence(spec, c);
        }

        throw new ArgumentException(
            "RecurrenceSpec must specify either DivideByB >= 2 or SubtractC >= 1.", nameof(spec));
    }

    /// <summary>
    /// Extracts a RecurrenceSpec from an AlgorithmFunction containing recursiveCall blocks
    /// and solves the resulting recurrence relation.
    /// </summary>
    public static RecurrenceAnalysisResult AnalyzeFunction(
        AlgorithmFunction fn,
        CostRulesProfile? profile = null)
    {
        ArgumentNullException.ThrowIfNull(fn);
        profile ??= CostRulesProfile.StandardUnitCost;

        var calls = new List<RecursiveCallStatement>();
        CollectRecursiveCalls(fn.Body, calls);
        if (calls.Count == 0)
        {
            throw new InvalidOperationException($"Function '{fn.Name}' contains no recursiveCall statements.");
        }

        var totalBranches = calls.Sum(c => Math.Max(1, c.Branches));
        var subproblem = calls[0].Subproblem.Trim();

        int? divideB = null;
        int? subtractC = null;

        var divMatch = DividePattern().Match(subproblem);
        var subMatch = SubtractPattern().Match(subproblem);

        if (divMatch.Success)
        {
            divideB = int.Parse(divMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else if (subMatch.Success)
        {
            subtractC = int.Parse(subMatch.Groups[1].Value, CultureInfo.InvariantCulture);
        }
        else
        {
            subtractC = 1;
        }

        // Measure non-recursive work f(n) from the AST with recursive calls stripped
        var strippedBody = StripRecursiveCalls(fn.Body);
        var strippedFn = fn with { Body = strippedBody };
        var workResult = IterativeAnalyzer.Analyze(strippedFn, profile);
        var workExpr = workResult.WorstCase.Expression;
        if (workExpr.IsZero) workExpr = SymbolicExpr.One;

        var dominant = AsymptoticAnalyzer.ExtractDominantBasis(workExpr);
        var degreeK = dominant.TotalPolyDegree;
        var logPowerP = dominant.TotalLogPower;
        var logBase = dominant.PrimaryLog?.Base ?? 2;

        var workLatex = FormatCanonicalWorkLatex(degreeK, logPowerP, logBase);

        var spec = new RecurrenceSpec(
            BaseN: divideB is not null ? 1 : 0,
            BaseCost: 1,
            BranchesA: totalBranches,
            DivideByB: divideB,
            SubtractC: subtractC,
            NonRecursiveWorkLatex: workLatex,
            WorkPolyDegreeK: degreeK,
            WorkLogPowerP: logPowerP,
            LogBase: logBase,
            ExactWorkExpr: workExpr);

        return Solve(spec);
    }

    private static RecurrenceAnalysisResult SolveDivideRecurrence(RecurrenceSpec spec, int b)
    {
        var a = spec.BranchesA;
        var k = spec.WorkPolyDegreeK;
        var p = spec.WorkLogPowerP;
        var baseN = Math.Max(1, spec.BaseN);
        var baseCost = Math.Max(1, spec.BaseCost);
        var fLatex = string.IsNullOrWhiteSpace(spec.NonRecursiveWorkLatex)
            ? FormatCanonicalWorkLatex(k, p, spec.LogBase)
            : spec.NonRecursiveWorkLatex;

        var aPrefix = a == 1 ? string.Empty : $"{a}";
        var recurrenceLatex = $@"T(n) = {aPrefix}T\!\left(\frac{{n}}{{{b}}}\right) + {fLatex}";
        var recurrencePlain = $"T(n) = {aPrefix}T(n/{b}) + {FormatCanonicalWorkPlain(k, p, spec.LogBase)}";

        var bPowK = PowInt(b, k);
        var ratioNum = new BigRational(a, bPowK);

        // 1. Iterative unrolling steps
        var steps = new List<UnrollingStep>
        {
            new("Base Case", $@"T({baseN}) = {baseCost}", $"T({baseN}) = {baseCost}",
                "Recursion terminates when the subproblem reaches base size."),
            new("i = 1", $@"T(n) = {aPrefix}T\!\left(\frac{{n}}{{{b}}}\right) + {fLatex}",
                $"T(n) = {aPrefix}T(n/{b}) + {FormatCanonicalWorkPlain(k, p, spec.LogBase)}",
                "Initial recurrence expansion (level 1)."),
            new("i = 2", BuildDivideStepLatex(a, b, k, p, 2, fLatex),
                $"T(n) = {a * a}T(n/{b * b}) + work(levels 0..1)",
                "Substitute T(n/b) into the recurrence."),
            new("i = 3", BuildDivideStepLatex(a, b, k, p, 3, fLatex),
                $"T(n) = {a * a * a}T(n/{b * b * b}) + work(levels 0..2)",
                "Substitute T(n/b^2) into the recurrence."),
            new("In general (step i)", BuildDivideGeneralStepLatex(a, b, k, p, fLatex),
                $"T(n) = {a}^i T(n/{b}^i) + sum_{{j=0}}^{{i-1}} {a}^j f(n/{b}^j)",
                "General form after i unrolling steps."),
            new("Stopping condition",
                $@"\frac{{n}}{{{b}^i}} = {baseN} \implies i = \log_{FormatLogBaseLatex(b)} n",
                $"n / {b}^i = {baseN} => i = log_{b} n",
                "Solve for depth i where subproblem reaches the base case.")
        };

        // 2. Closed form & Master Theorem classification
        var (closedExpr, closedLatex, closedPlain, bigO, bigOLatex, bigTheta, bigThetaLatex, caseLabel, cmpLatex, explanation, treeExplanation) =
            ClassifyDivideMasterTheorem(a, b, k, p, bPowK, ratioNum, baseCost);

        steps.Add(new UnrollingStep(
            "Closed-form T(n)",
            $@"T(n) = {closedLatex} = {bigThetaLatex}",
            $"T(n) = {closedPlain} = {bigTheta}",
            "Substitute stopping depth i into the general form and simplify."));

        var ratioLatex = $@"\frac{{a}}{{b^k}} = \frac{{{a}}}{{{b}^{k}}} = {ratioNum.ToLatexString()}";
        var akraBazziNote = BuildAkraBazziNote(a, b, k, p, bigThetaLatex);

        var master = new MasterTheoremResult(
            TheoremName: p != 0 && a == bPowK ? "Extended Master Theorem" : "Master Theorem",
            CaseLabel: caseLabel,
            ComparisonLatex: cmpLatex,
            BigO: bigO,
            BigOLatex: bigOLatex,
            BigTheta: bigTheta,
            BigThetaLatex: bigThetaLatex,
            Explanation: explanation,
            RecursionTreeWorkRatioLatex: ratioLatex,
            RecursionTreeExplanation: treeExplanation,
            AkraBazziNote: akraBazziNote);

        return new RecurrenceAnalysisResult(
            Spec: spec,
            RecurrenceLatex: recurrenceLatex,
            RecurrencePlain: recurrencePlain,
            UnrollingSteps: steps,
            MasterTheorem: master,
            ClosedFormLatex: closedLatex,
            ClosedFormPlain: closedPlain,
            BigO: bigO,
            BigTheta: bigTheta,
            ClosedFormExpr: closedExpr);
    }

    private static (
        SymbolicExpr? ClosedExpr,
        string ClosedLatex,
        string ClosedPlain,
        string BigO,
        string BigOLatex,
        string BigTheta,
        string BigThetaLatex,
        string CaseLabel,
        string CmpLatex,
        string Explanation,
        string TreeExplanation)
        ClassifyDivideMasterTheorem(
            int a, int b, int k, int p, int bPowK, BigRational ratio, int baseCost)
    {
        var bBaseLatex = FormatLogBaseLatex(b);

        // Case 1: a > b^k (Leaf-dominated) -> Theta(n^{log_b a})
        if (a > bPowK)
        {
            SymbolicExpr? closedExpr = null;
            string closedLatex;
            string closedPlain;
            string bigO;
            string bigOLatex;
            string bigTheta;
            string bigThetaLatex;

            if (TryExactLogInt(a, b, out var exactExp))
            {
                var basis = BasisTerm.Variable("n", exactExp);
                var (oP, oL) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "O", @"\mathcal{O}");
                var (tP, tL) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "Θ", @"\Theta");
                bigO = oP;
                bigOLatex = oL;
                bigTheta = tP;
                bigThetaLatex = tL;

                if (k == 0 && p == 0)
                {
                    var r = BigRational.FromInt(a);
                    closedExpr = SymbolicExpr.Constant(BigRational.FromInt(baseCost) + BigRational.One / (r - BigRational.One)) *
                                 SymbolicExpr.Variable("n", exactExp) -
                                 SymbolicExpr.Constant(BigRational.One / (r - BigRational.One));
                    closedLatex = closedExpr.ToLatexString();
                    closedPlain = closedExpr.ToPlainString();
                }
                else
                {
                    closedExpr = SymbolicExpr.Variable("n", exactExp);
                    closedLatex = $@"c_1 {closedExpr.ToLatexString()} + \text{{lower}}";
                    closedPlain = closedExpr.ToPlainString();
                }
            }
            else
            {
                var fracBasis = BasisTerm.Fractional("n", b, a);
                bigO = $"O({fracBasis.ToPlainString()})";
                bigOLatex = $@"\mathcal{{O}}({fracBasis.ToLatexString()})";
                bigTheta = $"Θ({fracBasis.ToPlainString()})";
                bigThetaLatex = $@"\Theta({fracBasis.ToLatexString()})";

                if (k == 0 && p == 0)
                {
                    // T(n) = a^{log_b n} * baseCost + (a^{log_b n} - 1)/(a - 1)
                    var denom = BigRational.FromInt(a - 1);
                    var lead = BigRational.FromInt(baseCost) + BigRational.One / denom;
                    var tail = BigRational.One / denom;
                    closedExpr = SymbolicExpr.FromTerm(fracBasis, lead) - SymbolicExpr.Constant(tail);
                    closedLatex = closedExpr.ToLatexString();
                    closedPlain = closedExpr.ToPlainString();
                }
                else
                {
                    closedExpr = SymbolicExpr.FractionalPower("n", b, a);
                    closedLatex = $@"\Theta({fracBasis.ToLatexString()})";
                    closedPlain = fracBasis.ToPlainString();
                }
            }

            return (
                closedExpr,
                closedLatex,
                closedPlain,
                bigO,
                bigOLatex,
                bigTheta,
                bigThetaLatex,
                CaseLabel: "Case 1 (Leaf-Dominated)",
                CmpLatex: $@"a = {a} > b^k = {b}^{k} = {bPowK}",
                Explanation: $"Because a > b^k ({a} > {bPowK}), the number of subproblems grows faster than per-node work shrinks; total cost is dominated by the leaves: {bigTheta}.",
                TreeExplanation: $"Work per recursion-tree level grows geometrically by factor a/b^k = {ratio.ToPlainString()} > 1, so the bottom leaf level dominates total work.");
        }

        // Case 2: a == b^k (Balanced — including Extended Logarithmic Master Theorem!)
        if (a == bPowK)
        {
            if (p > -1)
            {
                var newLogPow = p + 1;
                var basis = BuildPolyLogBasis("n", k, b, newLogPow);
                var (oP, oL) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "O", @"\mathcal{O}");
                var (tP, tL) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "Θ", @"\Theta");

                SymbolicExpr closedExpr;
                if (p == 0)
                {
                    // T(n) = baseCost * n^k + n^k * log_b n
                    var polyPart = k == 0 ? SymbolicExpr.One : SymbolicExpr.Variable("n", k);
                    closedExpr = polyPart * SymbolicExpr.Log("n", b) + SymbolicExpr.Constant(baseCost) * polyPart;
                }
                else
                {
                    closedExpr = SymbolicExpr.FromTerm(basis, BigRational.One);
                }

                var label = p == 0
                    ? "Case 2 (Balanced Levels)"
                    : $"Extended Case 2a (Balanced, p = {p} > -1)";

                return (
                    closedExpr,
                    closedExpr.ToLatexString(),
                    closedExpr.ToPlainString(),
                    oP,
                    oL,
                    tP,
                    tL,
                    CaseLabel: label,
                    CmpLatex: $@"a = {a} = b^k = {b}^{k} = {bPowK}, \quad p = {p} > -1",
                    Explanation: $"Because a = b^k ({a} = {bPowK}) and p = {p} > -1, every level contributes balanced work across log_{b}(n) levels, giving {tP}.",
                    TreeExplanation: $"Work ratio per level is a/b^k = 1, so all log_{b}(n) levels contribute equally.");
            }

            if (p == -1)
            {
                var polyPlain = k == 0 ? string.Empty : (k == 1 ? "n " : $"n^{k} ");
                var polyLatex = k == 0 ? string.Empty : (k == 1 ? "n " : $"n^{k} ");
                var bigO = $"O({polyPlain}log log n)";
                var bigOLatex = $@"\mathcal{{O}}({polyLatex}\log \log n)";
                var bigTheta = $"Θ({polyPlain}log log n)";
                var bigThetaLatex = $@"\Theta({polyLatex}\log \log n)";

                return (
                    null,
                    $@"{polyLatex}\log \log n",
                    $"{polyPlain}log log n",
                    bigO,
                    bigOLatex,
                    bigTheta,
                    bigThetaLatex,
                    CaseLabel: "Extended Case 2b (Harmonic Boundary, p = -1)",
                    CmpLatex: $@"a = {a} = b^k = {bPowK}, \quad p = -1",
                    Explanation: $"Because a = b^k and p = -1, summing 1/j across log_{b}(n) levels produces a harmonic factor log log n, giving {bigTheta}.",
                    TreeExplanation: "Work per level scales as 1/(level), whose sum over log n levels is Θ(log log n).");
            }

            // p < -1: convergent p-series across levels -> Theta(n^k)
            var convBasis = k == 0 ? BasisTerm.One : BasisTerm.Variable("n", k);
            var (coP, coL) = AsymptoticAnalyzer.FormatAsymptoticClass(convBasis, "O", @"\mathcal{O}");
            var (ctP, ctL) = AsymptoticAnalyzer.FormatAsymptoticClass(convBasis, "Θ", @"\Theta");
            var convExpr = SymbolicExpr.FromTerm(convBasis, BigRational.One);

            return (
                convExpr,
                convExpr.ToLatexString(),
                convExpr.ToPlainString(),
                coP,
                coL,
                ctP,
                ctL,
                CaseLabel: $"Extended Case 2c (Convergent Log Series, p = {p} < -1)",
                CmpLatex: $@"a = {a} = b^k = {bPowK}, \quad p = {p} < -1",
                Explanation: $"Because a = b^k and p = {p} < -1, the logarithmic series converges to a constant multiple of the leaf count, yielding {ctP}.",
                TreeExplanation: "Summing level factors (log n)^p with p < -1 converges to O(1), leaving Θ(n^k).");
        }

        // Case 3: a < b^k (Root-dominated) -> Theta(f(n))
        {
            var basis = BuildPolyLogBasis("n", k, b, Math.Max(0, p));
            var (oP, oL) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "O", @"\mathcal{O}");
            var (tP, tL) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "Θ", @"\Theta");

            SymbolicExpr closedExpr;
            string closedLatex;
            string closedPlain;

            if (p == 0 && k >= 1 && a == 1)
            {
                // Exact closed form for T(n) = T(n/b) + n^k:
                // sum_{j=0}^{log_b n - 1} (n/b^j)^k = n^k * (1 - 1/n^k) / (1 - 1/b^k)
                var factor = BigRational.FromInt(bPowK) / BigRational.FromInt(bPowK - 1);
                var constShift = BigRational.FromInt(baseCost) - factor;
                closedExpr = SymbolicExpr.Constant(factor) * SymbolicExpr.Variable("n", k) + SymbolicExpr.Constant(constShift);
                closedLatex = closedExpr.ToLatexString();
                closedPlain = closedExpr.ToPlainString();
            }
            else if (p == 0 && k >= 1)
            {
                // T(n) = aT(n/b) + n^k with a < b^k: leading coefficient b^k / (b^k - a)
                var factor = BigRational.FromInt(bPowK) / BigRational.FromInt(bPowK - a);
                closedExpr = SymbolicExpr.Constant(factor) * SymbolicExpr.Variable("n", k);
                closedLatex = $@"{factor.ToLatexString()} n{(k == 1 ? string.Empty : $"^{k}")} + \text{{lower}}";
                closedPlain = $"{closedExpr.ToPlainString()} + lower";
            }
            else
            {
                closedExpr = SymbolicExpr.FromTerm(basis, BigRational.One);
                closedLatex = closedExpr.ToLatexString();
                closedPlain = closedExpr.ToPlainString();
            }

            return (
                closedExpr,
                closedLatex,
                closedPlain,
                oP,
                oL,
                tP,
                tL,
                CaseLabel: "Case 3 (Root-Dominated)",
                CmpLatex: $@"a = {a} < b^k = {b}^{k} = {bPowK}",
                Explanation: $"Because a < b^k ({a} < {bPowK}), the regularity condition holds (a*f(n/b) <= c*f(n) for c = {ratio.ToPlainString()} < 1) and work is dominated by the root: {tP}.",
                TreeExplanation: $"Work per recursion-tree level shrinks geometrically by factor a/b^k = {ratio.ToPlainString()} < 1, so the root level dominates total work.");
        }
    }

    private static RecurrenceAnalysisResult SolveSubtractRecurrence(RecurrenceSpec spec, int c)
    {
        var a = spec.BranchesA;
        var k = spec.WorkPolyDegreeK;
        var p = spec.WorkLogPowerP;
        var baseN = Math.Max(0, spec.BaseN);
        var baseCost = Math.Max(1, spec.BaseCost);
        var logBase = spec.LogBase >= 2 ? spec.LogBase : 2;
        var fLatex = string.IsNullOrWhiteSpace(spec.NonRecursiveWorkLatex)
            ? FormatCanonicalWorkLatex(k, p, logBase)
            : spec.NonRecursiveWorkLatex;
        var fPlain = FormatCanonicalWorkPlain(k, p, logBase);

        var aPrefix = a == 1 ? string.Empty : $"{a}";
        var recurrenceLatex = $@"T(n) = {aPrefix}T(n - {c}) + {fLatex}";
        var recurrencePlain = $"T(n) = {aPrefix}T(n - {c}) + {fPlain}";

        var steps = new List<UnrollingStep>
        {
            new("Base Case", $@"T({baseN}) = {baseCost}", $"T({baseN}) = {baseCost}",
                "Stopping base case of the recursion."),
            new("i = 1", $@"T(n) = {aPrefix}T(n - {c}) + {fLatex}",
                $"T(n) = {aPrefix}T(n - {c}) + {fPlain}",
                "Step 1 expansion."),
            new("i = 2", BuildSubtractStepLatex(a, c, k, p, logBase, 2),
                $"T(n) = {a * a}T(n - {2 * c}) + work(0..1)",
                "Substitute T(n - c)."),
            new("i = 3", BuildSubtractStepLatex(a, c, k, p, logBase, 3),
                $"T(n) = {a * a * a}T(n - {3 * c}) + work(0..2)",
                "Substitute T(n - 2c)."),
            new("In general (step i)", BuildSubtractGeneralStepLatex(a, c, k, p, logBase),
                $"T(n) = {a}^i T(n - {c}i) + sum_{{j=0}}^{{i-1}} {a}^j f(n - {c}j)",
                "General pattern after i unrolling steps."),
            new("Stopping condition",
                c == 1
                    ? $@"n - i = {baseN} \implies i = {(baseN == 0 ? "n" : $"n - {baseN}")}"
                    : $@"n - {c}i = {baseN} \implies i = \frac{{n{(baseN == 0 ? string.Empty : $" - {baseN}")}}}{{{c}}}",
                c == 1 ? $"n - i = {baseN} => i = n - {baseN}" : $"n - {c}i = {baseN} => i = (n - {baseN})/{c}",
                "Solve for i when subproblem reaches the base case.")
        };

        // Solve closed form & Subtraction Master Theorem
        SymbolicExpr closedExpr;
        string bigO;
        string bigOLatex;
        string bigTheta;
        string bigThetaLatex;
        string caseLabel;
        string cmpLatex;
        string explanation;
        string treeRatioLatex;
        string treeExplanation;

        if (a == 1)
        {
            // Linear decrease with single branch: T(n) = T(n - c) + f(n) => sum of f(n - c*j)
            var nVar = SymbolicExpr.Variable("n");
            var idx = SymbolicExpr.Variable("j");
            if (p == 0)
            {
                // Exact power sum using SummationEngine!
                var body = k == 0 ? SymbolicExpr.One : idx.Pow(k);
                var sumRes = SummationEngine.Sum(body, "j", SymbolicExpr.One, nVar, step: c);
                closedExpr = sumRes.Result + SymbolicExpr.Constant(baseN == 0 ? baseCost : 0);
            }
            else if (k == 0 && p == 1)
            {
                // sum_{j=1}^{n} log_b(j) ~ n log_b(n) via Stirling's approximation!
                var logBody = SymbolicExpr.Log("j", logBase);
                var sumRes = SummationEngine.Sum(logBody, "j", SymbolicExpr.One, nVar, step: c);
                closedExpr = sumRes.Result;
            }
            else
            {
                var basis = BuildPolyLogBasis("n", k + 1, logBase, p);
                closedExpr = SymbolicExpr.FromTerm(basis, BigRational.One / BigRational.FromInt(c));
            }

            var dom = AsymptoticAnalyzer.ExtractDominantBasis(closedExpr);
            (bigO, bigOLatex) = AsymptoticAnalyzer.FormatAsymptoticClass(dom, "O", @"\mathcal{O}");
            (bigTheta, bigThetaLatex) = AsymptoticAnalyzer.FormatAsymptoticClass(dom, "Θ", @"\Theta");

            caseLabel = "Subtraction Master Case 2 (a = 1, Linear Recursion Depth)";
            cmpLatex = $@"a = 1, \; c = {c}, \; f(n) = \Theta({fLatex})";
            explanation = $"For T(n) = aT(n - c) + f(n) with a = 1, the recursion chain has depth n/{c}, multiplying per-step work by n: T(n) = Θ(n * f(n)) = {bigTheta}.";
            treeRatioLatex = "a = 1";
            treeExplanation = $"Recursion tree is a single chain of depth n/{c} with 1 node per level; summing f(n - {c}j) yields {bigTheta}.";
        }
        else if (a > 1)
        {
            // Exponential branching: T(n) = aT(n - c) + f(n) => Theta(a^{n/c})
            if (c == 1)
            {
                var r = BigRational.FromInt(a);
                if (k == 0 && p == 0)
                {
                    // Exact geometric sum: a^{n - baseN} * baseCost + (a^{n - baseN} - 1)/(a - 1)
                    var geom = SummationEngine.SumGeometric(
                        BigRational.One, r, "j", SymbolicExpr.Zero, SymbolicExpr.Variable("n") - SymbolicExpr.Constant(baseN + 1));
                    var baseTerm = SymbolicExpr.Constant(baseCost) * SymbolicExpr.ExpOf(r, SymbolicExpr.Variable("n") - SymbolicExpr.Constant(baseN));
                    closedExpr = baseTerm + geom.Result;
                }
                else
                {
                    closedExpr = SymbolicExpr.Exp(r, "n");
                }
                var expBasis = BasisTerm.Exp(r, "n");
                (bigO, bigOLatex) = AsymptoticAnalyzer.FormatAsymptoticClass(expBasis, "O", @"\mathcal{O}");
                (bigTheta, bigThetaLatex) = AsymptoticAnalyzer.FormatAsymptoticClass(expBasis, "Θ", @"\Theta");
            }
            else
            {
                var r = BigRational.FromInt(a);
                closedExpr = SymbolicExpr.Exp(r, "n");
                bigO = $"O({a}^(n/{c}))";
                bigOLatex = $@"\mathcal{{O}}({a}^{{n/{c}}})";
                bigTheta = $"Θ({a}^(n/{c}))";
                bigThetaLatex = $@"\Theta({a}^{{n/{c}}})";
            }

            caseLabel = "Subtraction Master Case 3 (a > 1, Exponential Branching)";
            cmpLatex = $@"a = {a} > 1, \; c = {c}";
            explanation = $"For T(n) = aT(n - c) + f(n) with a = {a} > 1, each decrement multiplies active subproblems by {a}, yielding exponential complexity {bigTheta}.";
            treeRatioLatex = $@"a = {a} > 1";
            treeExplanation = $"Each level multiplies node count by {a} across depth n/{c}, so leaves dominate with {bigTheta}.";
        }
        else
        {
            var basis = BuildPolyLogBasis("n", k, logBase, p);
            closedExpr = SymbolicExpr.FromTerm(basis, BigRational.One);
            (bigO, bigOLatex) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "O", @"\mathcal{O}");
            (bigTheta, bigThetaLatex) = AsymptoticAnalyzer.FormatAsymptoticClass(basis, "Θ", @"\Theta");
            caseLabel = "Subtraction Master Case 1 (a < 1)";
            cmpLatex = $@"a = {a} < 1";
            explanation = $"Subproblem count shrinks at every step, leaving root work {bigTheta}.";
            treeRatioLatex = $@"a = {a} < 1";
            treeExplanation = "Root work dominates.";
        }

        var closedLatex = closedExpr.ToLatexString();
        var closedPlain = closedExpr.ToPlainString();

        steps.Add(new UnrollingStep(
            "Closed-form T(n)",
            $@"T(n) = {closedLatex} = {bigThetaLatex}",
            $"T(n) = {closedPlain} = {bigTheta}",
            "Evaluate the resulting summation in closed form."));

        var master = new MasterTheoremResult(
            TheoremName: "Subtraction Master Theorem",
            CaseLabel: caseLabel,
            ComparisonLatex: cmpLatex,
            BigO: bigO,
            BigOLatex: bigOLatex,
            BigTheta: bigTheta,
            BigThetaLatex: bigThetaLatex,
            Explanation: explanation,
            RecursionTreeWorkRatioLatex: treeRatioLatex,
            RecursionTreeExplanation: treeExplanation,
            AkraBazziNote: "Akra-Bazzi applies to fractional divide-and-conquer recurrences T(n/b); for decrease-by-constant recurrences T(n - c), the Subtraction Master Theorem and direct summation yield the exact bound.");

        return new RecurrenceAnalysisResult(
            Spec: spec,
            RecurrenceLatex: recurrenceLatex,
            RecurrencePlain: recurrencePlain,
            UnrollingSteps: steps,
            MasterTheorem: master,
            ClosedFormLatex: closedLatex,
            ClosedFormPlain: closedPlain,
            BigO: bigO,
            BigTheta: bigTheta,
            ClosedFormExpr: closedExpr);
    }

    private static string BuildDivideStepLatex(int a, int b, int k, int p, int stepI, string fLatex)
    {
        var aPow = PowInt(a, stepI);
        var bPow = PowInt(b, stepI);
        var aPart = aPow == 1 ? string.Empty : $"{aPow}";

        if (k == 0 && p == 0)
        {
            var terms = Enumerable.Range(0, stepI).Reverse().Select(j => PowInt(a, j).ToString(CultureInfo.InvariantCulture));
            return $@"T(n) = {aPart}T\!\left(\frac{{n}}{{{bPow}}}\right) + {string.Join(" + ", terms)}";
        }

        var workParts = new List<string>();
        for (var j = stepI - 1; j >= 0; j--)
        {
            var aj = PowInt(a, j);
            var bj = PowInt(b, j);
            var coeff = new BigRational(aj, PowInt(bj, k));
            if (j == 0)
            {
                workParts.Add(fLatex);
            }
            else if (p == 0)
            {
                workParts.Add(coeff.IsOne ? fLatex : $@"{coeff.ToLatexString()} {fLatex}");
            }
            else
            {
                var ajStr = aj == 1 ? string.Empty : $"{aj}";
                workParts.Add($@"{ajStr} f\!\left(\frac{{n}}{{{bj}}}\right)");
            }
        }
        return $@"T(n) = {aPart}T\!\left(\frac{{n}}{{{bPow}}}\right) + {string.Join(" + ", workParts)}";
    }

    private static string BuildDivideGeneralStepLatex(int a, int b, int k, int p, string fLatex)
    {
        var aPow = a == 1 ? string.Empty : $"{a}^i ";
        if (k == 0 && p == 0)
        {
            var sumPart = a == 1 ? "i" : $@"\sum_{{j=0}}^{{i-1}} {a}^j";
            return $@"T(n) = {aPow}T\!\left(\frac{{n}}{{{b}^i}}\right) + {sumPart}";
        }
        if (p == 0)
        {
            var r = new BigRational(a, PowInt(b, k));
            if (r.IsOne)
            {
                return $@"T(n) = {aPow}T\!\left(\frac{{n}}{{{b}^i}}\right) + i \cdot {fLatex}";
            }
            return $@"T(n) = {aPow}T\!\left(\frac{{n}}{{{b}^i}}\right) + {fLatex} \sum_{{j=0}}^{{i-1}} \left({r.ToLatexString()}\right)^j";
        }
        return $@"T(n) = {aPow}T\!\left(\frac{{n}}{{{b}^i}}\right) + \sum_{{j=0}}^{{i-1}} {a}^j f\!\left(\frac{{n}}{{{b}^j}}\right)";
    }

    private static string BuildSubtractStepLatex(int a, int c, int k, int p, int logBase, int stepI)
    {
        var aPow = PowInt(a, stepI);
        var aPart = aPow == 1 ? string.Empty : $"{aPow}";
        var subTerms = new List<string>();

        for (var j = stepI - 1; j >= 0; j--)
        {
            var aj = PowInt(a, j);
            var shift = j * c;
            var arg = shift == 0 ? "n" : $"(n - {shift})";
            string term;
            if (k == 0 && p == 0)
            {
                term = aj.ToString(CultureInfo.InvariantCulture);
            }
            else if (k == 0 && p == 1)
            {
                var logTerm = $@"\log_{FormatLogBaseLatex(logBase)} {arg}";
                term = aj == 1 ? logTerm : $@"{aj} {logTerm}";
            }
            else if (p == 0)
            {
                var polyTerm = k == 1 ? arg : $"{arg}^{k}";
                term = aj == 1 ? polyTerm : $@"{aj}{polyTerm}";
            }
            else
            {
                term = aj == 1 ? $"f({arg})" : $"{aj}f({arg})";
            }
            subTerms.Add(term);
        }

        return $@"T(n) = {aPart}T(n - {stepI * c}) + {string.Join(" + ", subTerms)}";
    }

    private static string BuildSubtractGeneralStepLatex(int a, int c, int k, int p, int logBase)
    {
        var aPow = a == 1 ? string.Empty : $"{a}^i ";
        var cStep = c == 1 ? "i" : $"{c}i";
        var jStep = c == 1 ? "j" : $"{c}j";

        if (k == 0 && p == 0)
        {
            var sumPart = a == 1 ? "i" : $@"\sum_{{j=0}}^{{i-1}} {a}^j";
            return $@"T(n) = {aPow}T(n - {cStep}) + {sumPart}";
        }
        if (k == 0 && p == 1 && a == 1)
        {
            return $@"T(n) = T(n - {cStep}) + \sum_{{j=0}}^{{i-1}} \log_{FormatLogBaseLatex(logBase)}(n - {jStep})";
        }
        if (p == 0)
        {
            var aJ = a == 1 ? string.Empty : $"{a}^j ";
            var pow = k == 1 ? $"(n - {jStep})" : $"(n - {jStep})^{k}";
            return $@"T(n) = {aPow}T(n - {cStep}) + \sum_{{j=0}}^{{i-1}} {aJ}{pow}";
        }
        return $@"T(n) = {aPow}T(n - {cStep}) + \sum_{{j=0}}^{{i-1}} {(a == 1 ? string.Empty : $"{a}^j ")}f(n - {jStep})";
    }

    private static string BuildAkraBazziNote(int a, int b, int k, int p, string thetaLatex)
    {
        var pCrit = Math.Log(a) / Math.Log(b);
        return $@"Akra-Bazzi critical exponent satisfies {a} \cdot (1/{b})^{{p}} = 1 \implies p = \log_{FormatLogBaseLatex(b)} {a} \approx {pCrit:0.###}; evaluating \Theta\!\left(n^p\!\left(1 + \int_1^n \frac{{f(u)}}{{u^{{p+1}}}}\,du\right)\right) confirms {thetaLatex}.";
    }

    private static BasisTerm BuildPolyLogBasis(string varName, int degreeK, int logBase, int logPowerP)
    {
        var result = degreeK > 0 ? BasisTerm.Variable(varName, degreeK) : BasisTerm.One;
        if (logPowerP > 0)
        {
            result = result.Multiply(BasisTerm.Log(varName, logBase, logPowerP));
        }
        return result;
    }

    private static string FormatCanonicalWorkLatex(int k, int p, int logBase)
    {
        var bPart = FormatLogBaseLatex(logBase);
        if (k == 0 && p == 0) return "1";
        if (k == 0 && p == 1) return $@"\log_{bPart} n";
        if (k == 0 && p > 1) return $@"\log_{bPart}^{p} n";
        var poly = k == 1 ? "n" : $"n^{k}";
        if (p == 0) return poly;
        if (p == 1) return $@"{poly} \log_{bPart} n";
        return $@"{poly} \log_{bPart}^{p} n";
    }

    private static string FormatCanonicalWorkPlain(int k, int p, int logBase)
    {
        if (k == 0 && p == 0) return "1";
        if (k == 0 && p == 1) return $"log_{logBase} n";
        if (k == 0 && p > 1) return $"(log_{logBase} n)^{p}";
        var poly = k == 1 ? "n" : $"n^{k}";
        if (p == 0) return poly;
        if (p == 1) return $"{poly} log_{logBase} n";
        return $"{poly} (log_{logBase} n)^{p}";
    }

    private static string FormatLogBaseLatex(int b) =>
        b >= 10 ? $"{{{b}}}" : b.ToString(CultureInfo.InvariantCulture);

    private static int PowInt(int baseVal, int exp)
    {
        var r = 1;
        for (var i = 0; i < exp; i++) r *= baseVal;
        return r;
    }

    private static bool TryExactLogInt(int arg, int logBase, out int exponent)
    {
        exponent = 0;
        if (logBase <= 1 || arg <= 0) return false;
        if (arg == 1) return true;
        var cur = 1L;
        while (cur < arg)
        {
            cur *= logBase;
            exponent++;
        }
        return cur == arg;
    }

    private static void CollectRecursiveCalls(
        IReadOnlyList<AlgorithmStatement> stmts,
        List<RecursiveCallStatement> output)
    {
        foreach (var s in stmts)
        {
            switch (s)
            {
                case RecursiveCallStatement rec:
                    output.Add(rec);
                    break;
                case ForStatement f:
                    CollectRecursiveCalls(f.Body, output);
                    break;
                case WhileStatement w:
                    CollectRecursiveCalls(w.Body, output);
                    break;
                case IfStatement i:
                    CollectRecursiveCalls(i.ThenBody, output);
                    CollectRecursiveCalls(i.ElseBody, output);
                    break;
                case SwitchStatement sw:
                    foreach (var c in sw.Cases) CollectRecursiveCalls(c.Body, output);
                    break;
            }
        }
    }

    private static IReadOnlyList<AlgorithmStatement> StripRecursiveCalls(IReadOnlyList<AlgorithmStatement> stmts)
    {
        var result = new List<AlgorithmStatement>();
        foreach (var s in stmts)
        {
            switch (s)
            {
                case RecursiveCallStatement:
                    continue;
                case ForStatement f:
                    result.Add(f with { Body = StripRecursiveCalls(f.Body) });
                    break;
                case WhileStatement w:
                    result.Add(w with { Body = StripRecursiveCalls(w.Body) });
                    break;
                case IfStatement i:
                    result.Add(i with
                    {
                        ThenBody = StripRecursiveCalls(i.ThenBody),
                        ElseBody = StripRecursiveCalls(i.ElseBody)
                    });
                    break;
                case SwitchStatement sw:
                    result.Add(sw with
                    {
                        Cases = sw.Cases.Select(c => c with { Body = StripRecursiveCalls(c.Body) }).ToList()
                    });
                    break;
                default:
                    result.Add(s);
                    break;
            }
        }
        return result;
    }
}
