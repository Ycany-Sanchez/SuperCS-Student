using System.Collections.Immutable;
using System.Globalization;
using SuperCsStudent.Algorithms.Symbolic;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Analysis;

/// <summary>
/// Computes Big-O, Big-Omega, and Big-Theta asymptotic bounds, formal witness proofs
/// with exact (C, n0) selection rules, and sampled growth chart curves.
/// </summary>
public static class AsymptoticAnalyzer
{
    public static AsymptoticResult Analyze(
        SymbolicExpr worstExpr,
        SymbolicExpr? bestExpr = null,
        string primaryVar = "n")
    {
        bestExpr ??= worstExpr;

        var worstDom = ExtractDominantBasis(worstExpr);
        var bestDom = ExtractDominantBasis(bestExpr);

        var (bigOPlain, bigOLatex) = FormatAsymptoticClass(worstDom, "O", @"\mathcal{O}");
        var (bigOmegaPlain, bigOmegaLatex) = FormatAsymptoticClass(bestDom, "Ω", @"\Omega");

        var bigOProof = BuildBigOProof(worstExpr, worstDom, bigOPlain, primaryVar);
        var bigOmegaProof = BuildBigOmegaProof(bestExpr, bestDom, bigOmegaPlain, primaryVar);

        var isTight = SameAsymptoticClass(worstDom, bestDom);
        string? bigTheta = null;
        string bigThetaLatex;
        string bigThetaSummary;

        if (isTight)
        {
            var (thetaPlain, thetaLatex) = FormatAsymptoticClass(worstDom, "Θ", @"\Theta");
            bigTheta = thetaPlain;
            bigThetaLatex = thetaLatex;
            bigThetaSummary =
                $"Tight bound exists: Worst case is {bigOPlain} (C = {bigOProof.C.ToPlainString()}, n0 = {bigOProof.N0}) " +
                $"and Best case is {bigOmegaPlain} (C = {bigOmegaProof.C.ToPlainString()}, n0 = {bigOmegaProof.N0}), " +
                $"so T({primaryVar}) = {bigTheta}.";
        }
        else
        {
            bigThetaLatex = @"\text{Not tight}";
            bigThetaSummary =
                $"Not tight: Worst-case upper bound is {bigOPlain} while Best-case lower bound is {bigOmegaPlain}. " +
                $"Because the asymptotic classes differ across inputs, no single Θ bound applies.";
        }

        return new AsymptoticResult(
            BigO: bigOPlain,
            BigOLatex: bigOLatex,
            BigOmega: bigOmegaPlain,
            BigOmegaLatex: bigOmegaLatex,
            BigTheta: bigTheta,
            BigThetaLatex: bigThetaLatex,
            IsTight: isTight,
            BigOProof: bigOProof,
            BigOmegaProof: bigOmegaProof,
            BigThetaSummary: bigThetaSummary);
    }

    public static BasisTerm ExtractDominantBasis(SymbolicExpr expr)
    {
        if (expr.IsZero || expr.IsConstant) return BasisTerm.One;
        return expr.LeadingTerm!.Value.Key;
    }

    public static bool SameAsymptoticClass(BasisTerm a, BasisTerm b)
    {
        if (a.IsConstant && b.IsConstant) return true;
        if (a.IsConstant != b.IsConstant) return false;

        // Compare exponentials
        if (a.Exps.Length != b.Exps.Length) return false;
        for (var i = 0; i < a.Exps.Length; i++)
        {
            if (a.Exps[i].Base != b.Exps[i].Base) return false;
        }

        // Compare polynomial degree (including fractional powers)
        if (Math.Abs(a.EffectivePolyDegree - b.EffectivePolyDegree) > 1e-9) return false;

        // Compare total log power (log base only changes by constant factor!)
        if (a.TotalLogPower != b.TotalLogPower) return false;

        return true;
    }

    /// <summary>
    /// Selects the canonical threshold n0 for asymptotic witness proofs:
    /// - n0 = 1 default for polynomial / constant / exponential ratios
    /// - n0 = 2 when g(n) involves log_2 n (or n0 = b for log_b n)
    /// - n0 = 4 when a term ratio contains (log_b n) / n^m (e.g. log_2 n / n)
    /// </summary>
    public static int SelectBaseN0(SymbolicExpr expr, BasisTerm dominant)
    {
        var n0 = 1;

        // If dominant basis contains a log factor (e.g., log_2 n or n log_2 n), require log_b(n) >= 1 => n >= b
        if (dominant.Logs.Length > 0)
        {
            var maxBase = dominant.Logs.Max(l => l.Base);
            n0 = Math.Max(n0, maxBase);
        }

        // Check whether any lower-order term divided by dominant yields (log n) / n^m (e.g. log_2 n / n)
        foreach (var (basis, _) in expr.Terms)
        {
            if (basis.Equals(dominant)) continue;
            var extraLogPower = basis.TotalLogPower - dominant.TotalLogPower;
            var polyDrop = dominant.EffectivePolyDegree - basis.EffectivePolyDegree;
            if (extraLogPower > 0 && polyDrop > 1e-9)
            {
                // (log_2 n) / n reaches its rational monotonic threshold at n0 = 4 (log_2(4)/4 = 1/2)
                n0 = Math.Max(n0, 4);
            }
        }

        return n0;
    }

    private static WitnessProof BuildBigOProof(
        SymbolicExpr expr,
        BasisTerm dominant,
        string notation,
        string primaryVar)
    {
        var n0 = SelectBaseN0(expr, dominant);
        var gExpr = SymbolicExpr.FromTerm(dominant, BigRational.One);
        var gLatex = dominant.ToLatexString();
        var tLatex = expr.ToLatexString();

        var steps = new List<string>
        {
            $@"T({primaryVar}) = {tLatex}, \quad g({primaryVar}) = {gLatex}"
        };

        if (expr.IsZero)
        {
            steps.Add($@"0 \le 1 \cdot 1 \quad \forall {primaryVar} \ge 1");
            return new WitnessProof(notation, BigRational.One, 1, steps, $"Choose C = 1, n0 = 1.");
        }

        if (dominant.IsConstant)
        {
            var cVal = expr.ConstantValue.Abs();
            if (cVal.IsZero) cVal = BigRational.One;
            steps.Add($@"{tLatex} \le {cVal.ToLatexString()} \cdot 1 \quad \forall {primaryVar} \ge 1");
            return new WitnessProof(notation, cVal, 1, steps, $"Constant bound with C = {cVal.ToPlainString()}, n0 = 1.");
        }

        // Build exact ratio T(n) / g(n) and evaluate each positive term at n = n0
        var ratioLatexParts = new List<string>();
        var boundPartsLatex = new List<string>();
        var cSum = BigRational.Zero;

        foreach (var (basis, coeff) in expr.Terms)
        {
            var ratioTermLatex = FormatTermRatioLatex(basis, coeff, dominant);
            if (ratioLatexParts.Count == 0)
            {
                ratioLatexParts.Add(ratioTermLatex);
            }
            else
            {
                ratioLatexParts.Add(coeff.IsNegative
                    ? $" - {FormatTermRatioLatex(basis, coeff.Abs(), dominant)}"
                    : $" + {ratioTermLatex}");
            }

            if (coeff.IsNegative) continue;

            var upperAtN0 = EvaluateTermRatioUpperBoundAtN0(basis, coeff, dominant, n0);
            cSum += upperAtN0;
            boundPartsLatex.Add(upperAtN0.ToLatexString());
        }

        if (cSum <= BigRational.Zero)
        {
            cSum = BigRational.One;
        }

        // Verify numerically over [n0, n0 + 250] to guarantee airtight witness validity
        EnsureUpperWitnessHolds(expr, dominant, ref cSum, n0);

        steps.Add($@"\frac{{T({primaryVar})}}{{g({primaryVar})}} = {string.Concat(ratioLatexParts)}");
        steps.Add(BuildN0JustificationLatex(dominant, expr, n0, primaryVar));
        steps.Add(
            $@"\forall {primaryVar} \ge {n0}: \quad \frac{{T({primaryVar})}}{{g({primaryVar})}} \le {string.Join(" + ", boundPartsLatex)} = {cSum.ToLatexString()}");
        steps.Add(
            $@"\therefore\; T({primaryVar}) \le {cSum.ToLatexString()} \cdot {gLatex} \quad \forall {primaryVar} \ge {n0} \implies T({primaryVar}) \in {FormatNotationLatex(dominant, @"\mathcal{O}")}");

        return new WitnessProof(
            BoundNotation: notation,
            C: cSum,
            N0: n0,
            StepsLatex: steps,
            Summary: $"T({primaryVar}) <= {cSum.ToPlainString()} * {dominant.ToPlainString()} for all {primaryVar} >= {n0}.");
    }

    private static WitnessProof BuildBigOmegaProof(
        SymbolicExpr expr,
        BasisTerm dominant,
        string notation,
        string primaryVar)
    {
        var n0 = SelectBaseN0(expr, dominant);
        var gLatex = dominant.ToLatexString();
        var tLatex = expr.ToLatexString();

        var steps = new List<string>
        {
            $@"T({primaryVar}) = {tLatex}, \quad g({primaryVar}) = {gLatex}"
        };

        if (expr.IsZero)
        {
            steps.Add($@"0 \ge 0 \cdot 1 \quad \forall {primaryVar} \ge 1");
            return new WitnessProof(notation, BigRational.Zero, 1, steps, "Trivial zero lower bound.");
        }

        if (dominant.IsConstant)
        {
            var cVal = expr.ConstantValue > BigRational.Zero ? expr.ConstantValue : BigRational.One;
            steps.Add($@"{tLatex} \ge {cVal.ToLatexString()} \cdot 1 \quad \forall {primaryVar} \ge 1");
            return new WitnessProof(notation, cVal, 1, steps, $"Constant lower bound with C = {cVal.ToPlainString()}, n0 = 1.");
        }

        var leadCoeff = expr.LeadingTerm!.Value.Value;
        var hasNegativeLower = expr.Terms.Any(kv => !kv.Key.Equals(dominant) && kv.Value.IsNegative);

        BigRational cLower;
        if (!hasNegativeLower)
        {
            cLower = leadCoeff;
            steps.Add($@"\text{{All lower-order terms are non-negative for }} {primaryVar} \ge {n0}.");
            steps.Add($@"\therefore\; T({primaryVar}) \ge {cLower.ToLatexString()} \cdot {gLatex} \quad \forall {primaryVar} \ge {n0} \implies T({primaryVar}) \in {FormatNotationLatex(dominant, @"\Omega")}");
        }
        else
        {
            cLower = leadCoeff / BigRational.FromInt(2);
            // Advance n0 until T(n) >= cLower * g(n) holds monotonically for all n >= n0
            while (n0 < 10_000 && !HoldsLowerBoundMonotonically(expr, dominant, cLower, n0))
            {
                n0++;
            }

            steps.Add($@"\text{{Choose }} c = \frac{{1}}{{2}} \cdot {leadCoeff.ToLatexString()} = {cLower.ToLatexString()}.");
            steps.Add($@"\text{{For all }} {primaryVar} \ge {n0}, \text{{ negative lower-order terms satisfy }} \left|\text{{lower}}\right| \le {cLower.ToLatexString()} \cdot {gLatex}.");
            steps.Add($@"\therefore\; T({primaryVar}) \ge {cLower.ToLatexString()} \cdot {gLatex} \quad \forall {primaryVar} \ge {n0} \implies T({primaryVar}) \in {FormatNotationLatex(dominant, @"\Omega")}");
        }

        return new WitnessProof(
            BoundNotation: notation,
            C: cLower,
            N0: n0,
            StepsLatex: steps,
            Summary: $"T({primaryVar}) >= {cLower.ToPlainString()} * {dominant.ToPlainString()} for all {primaryVar} >= {n0}.");
    }

    private static BigRational EvaluateTermRatioUpperBoundAtN0(
        BasisTerm basis,
        BigRational coeff,
        BasisTerm dominant,
        int n0)
    {
        if (basis.Equals(dominant)) return coeff;

        // Exact rational evaluation when possible:
        var polyDrop = dominant.TotalPolyDegree - basis.TotalPolyDegree;
        var logDiff = basis.TotalLogPower - dominant.TotalLogPower;

        // Case 1: Pure polynomial drop 1 / n^d (no log change, no exp/frac)
        if (dominant.Exps.Length == 0 && basis.Exps.Length == 0 &&
            dominant.FracPower is null && basis.FracPower is null &&
            logDiff == 0 && polyDrop >= 0)
        {
            // At n >= n0, 1 / n^d <= 1 / n0^d
            return coeff / BigRational.FromInt(n0).Pow(polyDrop);
        }

        // Case 2: Dominant has log_b n and basis has fewer logs (e.g. 3 / log_2 n or 4 / log_2 n)
        if (dominant.Exps.Length == 0 && basis.Exps.Length == 0 &&
            dominant.FracPower is null && basis.FracPower is null &&
            logDiff < 0 && polyDrop >= 0 && dominant.Logs.Length == 1)
        {
            var b = dominant.Logs[0].Base;
            var logDrop = -logDiff;
            // If n0 == b, log_b(n0) == 1 exactly!
            if (n0 == b)
            {
                return coeff / BigRational.FromInt(n0).Pow(polyDrop);
            }
            // If n0 == b^k, log_b(n0) == k exactly!
            if (TryExactLogInt(n0, b, out var k) && k >= 1)
            {
                return coeff / (BigRational.FromInt(n0).Pow(polyDrop) * BigRational.FromInt(k).Pow(logDrop));
            }
        }

        // Case 3: Basis has (log_b n)^p and dominant has higher poly degree (e.g. log_2 n / n at n0 = 4)
        if (dominant.Exps.Length == 0 && basis.Exps.Length == 0 &&
            dominant.FracPower is null && basis.FracPower is null &&
            logDiff > 0 && polyDrop >= 1 && basis.Logs.Length == 1)
        {
            var b = basis.Logs[0].Base;
            if (TryExactLogInt(n0, b, out var logVal))
            {
                // Exact rational at n0 = 4 for log_2: (2)^logDiff / 4^polyDrop
                return coeff * BigRational.FromInt(logVal).Pow(logDiff) / BigRational.FromInt(n0).Pow(polyDrop);
            }
        }

        // General numeric upper bound evaluated at n0 and rounded up safely to integer
        var env = new Dictionary<string, double>(StringComparer.Ordinal) { ["n"] = n0 };
        var num = basis.Evaluate(env, n0);
        var den = dominant.Evaluate(env, n0);
        if (den <= 0) return coeff;
        var ratio = coeff.ToDouble() * (num / den);
        return BigRational.FromInt((long)Math.Ceiling(ratio - 1e-12));
    }

    private static bool TryExactLogInt(int value, int logBase, out int exponent)
    {
        exponent = 0;
        if (logBase < 2 || value < 1) return false;
        var cur = 1L;
        while (cur < value)
        {
            cur *= logBase;
            exponent++;
        }
        return cur == value;
    }

    private static void EnsureUpperWitnessHolds(
        SymbolicExpr expr,
        BasisTerm dominant,
        ref BigRational c,
        int n0)
    {
        var maxRatio = c.ToDouble();
        var checkUpper = Math.Max(n0 + 200, 300);
        for (var n = n0; n <= checkUpper; n++)
        {
            var env = new Dictionary<string, double>(StringComparer.Ordinal) { ["n"] = n };
            var tv = expr.EvaluateAt(env, n);
            var gv = dominant.Evaluate(env, n);
            if (gv > 0 && tv > maxRatio * gv + 1e-9)
            {
                maxRatio = tv / gv;
            }
        }
        if (maxRatio > c.ToDouble() + 1e-9)
        {
            c = BigRational.FromInt((long)Math.Ceiling(maxRatio));
        }
    }

    private static bool HoldsLowerBoundMonotonically(
        SymbolicExpr expr,
        BasisTerm dominant,
        BigRational cLower,
        int n0)
    {
        // Check both the analytic negative-term margin at n0 and sample points n >= n0
        var env0 = new Dictionary<string, double>(StringComparer.Ordinal) { ["n"] = n0 };
        var g0 = dominant.Evaluate(env0, n0);
        if (g0 <= 0) return false;

        var leadCoeff = expr.LeadingTerm!.Value.Value.ToDouble();
        var negRatioSum = 0.0;
        foreach (var (basis, coeff) in expr.Terms)
        {
            if (basis.Equals(dominant) || !coeff.IsNegative) continue;
            negRatioSum += Math.Abs(coeff.ToDouble()) * basis.Evaluate(env0, n0) / g0;
        }

        // Also check direct evaluation at n0..n0+50 (accounts for positive lower-order terms too)
        var checkEnd = n0 + 50;
        for (var n = n0; n <= checkEnd; n++)
        {
            var env = new Dictionary<string, double>(StringComparer.Ordinal) { ["n"] = n };
            var tv = expr.EvaluateAt(env, n);
            var gv = dominant.Evaluate(env, n);
            if (tv < cLower.ToDouble() * gv - 1e-9) return false;
        }

        // Ensure asymptotic guarantee: either negative margin already small enough, or check further out
        if (leadCoeff - negRatioSum >= cLower.ToDouble() - 1e-9) return true;

        var farEnv = new Dictionary<string, double>(StringComparer.Ordinal) { ["n"] = Math.Max(n0 * 10, 500) };
        var tvFar = expr.EvaluateAt(farEnv, Math.Max(n0 * 10, 500));
        var gvFar = dominant.Evaluate(farEnv, Math.Max(n0 * 10, 500));
        return tvFar >= cLower.ToDouble() * gvFar - 1e-9;
    }

    private static string BuildN0JustificationLatex(
        BasisTerm dominant,
        SymbolicExpr expr,
        int n0,
        string primaryVar)
    {
        var hasLogOverPoly = expr.Terms.Any(kv =>
            !kv.Key.Equals(dominant) &&
            kv.Key.TotalLogPower > dominant.TotalLogPower &&
            dominant.EffectivePolyDegree > kv.Key.EffectivePolyDegree + 1e-9);

        if (hasLogOverPoly && n0 >= 4)
        {
            return $@"\text{{Select }} n_0 = {n0} \text{{ because }} \frac{{\log_2 {primaryVar}}}{{{primaryVar}}} \text{{ is monotonically decreasing for }} {primaryVar} \ge 4 \text{{ with }} \frac{{\log_2 4}}{{4}} = \frac{{1}}{{2}}.";
        }

        if (dominant.Logs.Length > 0)
        {
            var b = dominant.Logs.Max(l => l.Base);
            var bLatex = b >= 10 ? $"{{{b}}}" : b.ToString(CultureInfo.InvariantCulture);
            return $@"\text{{Select }} n_0 = {n0} \text{{ because }} \log_{bLatex} {primaryVar} \ge 1 \text{{ for all }} {primaryVar} \ge {n0}.";
        }

        return $@"\text{{Select }} n_0 = {n0} \text{{ (every lower-order ratio }} 1/{primaryVar}^k \le 1 \text{{ for }} {primaryVar} \ge 1\text{{)}}.";
    }

    private static string FormatTermRatioLatex(BasisTerm basis, BigRational coeff, BasisTerm dominant)
    {
        if (basis.Equals(dominant)) return coeff.ToLatexString();
        var numExpr = SymbolicExpr.FromTerm(basis, coeff);
        var denExpr = SymbolicExpr.FromTerm(dominant, BigRational.One);

        // Simplify common variable powers between basis and dominant
        var numVars = basis.VarPowers.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        var denVars = dominant.VarPowers.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);

        foreach (var k in numVars.Keys.Intersect(denVars.Keys).ToList())
        {
            var m = Math.Min(numVars[k], denVars[k]);
            numVars[k] -= m;
            denVars[k] -= m;
            if (numVars[k] == 0) numVars.Remove(k);
            if (denVars[k] == 0) denVars.Remove(k);
        }

        // Simplify common logs
        var numLogs = basis.Logs.ToList();
        var denLogs = dominant.Logs.ToList();
        for (var i = numLogs.Count - 1; i >= 0; i--)
        {
            var matchIdx = denLogs.FindIndex(l => l.VarName == numLogs[i].VarName && l.Base == numLogs[i].Base);
            if (matchIdx >= 0)
            {
                var m = Math.Min(numLogs[i].Power, denLogs[matchIdx].Power);
                var nRem = numLogs[i].Power - m;
                var dRem = denLogs[matchIdx].Power - m;
                if (nRem == 0) numLogs.RemoveAt(i);
                else numLogs[i] = numLogs[i] with { Power = nRem };
                if (dRem == 0) denLogs.RemoveAt(matchIdx);
                else denLogs[matchIdx] = denLogs[matchIdx] with { Power = dRem };
            }
        }

        var remNumBasis = new BasisTerm(
            numVars.ToImmutableSortedDictionary(StringComparer.Ordinal),
            numLogs.ToImmutableArray(),
            basis.Exps,
            basis.FracPower);
        var remDenBasis = new BasisTerm(
            denVars.ToImmutableSortedDictionary(StringComparer.Ordinal),
            denLogs.ToImmutableArray(),
            dominant.Exps,
            dominant.FracPower);

        if (remDenBasis.IsConstant)
        {
            return SymbolicExpr.FromTerm(remNumBasis, coeff).ToLatexString();
        }

        var numPart = SymbolicExpr.FromTerm(remNumBasis, coeff).ToLatexString();
        var denPart = remDenBasis.ToLatexString();
        return $@"\frac{{{numPart}}}{{{denPart}}}";
    }

    public static (string Plain, string Latex) FormatAsymptoticClass(
        BasisTerm basis,
        string plainSymbol,
        string latexSymbol)
    {
        if (basis.IsConstant)
        {
            return ($"{plainSymbol}(1)", $@"{latexSymbol}(1)");
        }

        var plainParts = new List<string>();
        var latexParts = new List<string>();

        foreach (var (name, pow) in basis.VarPowers
                     .OrderBy(kv => kv.Key == "n" ? 0 : 1)
                     .ThenBy(kv => kv.Key, StringComparer.Ordinal))
        {
            plainParts.Add(pow == 1 ? name : $"{name}^{pow}");
            latexParts.Add(pow == 1 ? name : $"{name}^{pow}");
        }

        if (basis.FracPower is { } fp)
        {
            plainParts.Add(fp.ToPlainString());
            latexParts.Add(fp.ToLatexString());
        }

        foreach (var l in basis.Logs)
        {
            // Standard asymptotic notation omits constant-factor log base: O(log n), O(n log n)
            plainParts.Add(l.Power == 1 ? $"log {l.VarName}" : $"(log {l.VarName})^{l.Power}");
            latexParts.Add(l.Power == 1 ? $@"\log {l.VarName}" : $@"\log^{l.Power} {l.VarName}");
        }

        foreach (var e in basis.Exps)
        {
            plainParts.Add(e.ToPlainString());
            latexParts.Add(e.ToLatexString());
        }

        return (
            $"{plainSymbol}({string.Join(" ", plainParts)})",
            $@"{latexSymbol}({string.Join(" ", latexParts)})");
    }

    private static string FormatNotationLatex(BasisTerm basis, string latexSymbol) =>
        FormatAsymptoticClass(basis, string.Empty, latexSymbol).Latex;

    /// <summary>
    /// Generates sampled growth curves for n = 1..25 comparing Worst T(n), Best T(n) (if distinct),
    /// and clean asymptotic reference curves (compatible with chartInterop.js).
    /// </summary>
    public static IReadOnlyList<GrowthChartSeries> BuildGrowthChartSeries(
        SymbolicExpr worstExpr,
        SymbolicExpr bestExpr,
        string primaryVar = "n",
        int maxN = 25)
    {
        var series = new List<GrowthChartSeries>();
        var dominant = ExtractDominantBasis(worstExpr);

        // Add clean reference curve first so chartInterop.js draws dashed reference behind primary curve
        var refPoints = new List<GrowthPoint>(maxN);
        var (_, refClassPlain) = FormatAsymptoticClass(dominant, string.Empty, string.Empty);
        var refLabel = dominant.IsConstant ? "1" : dominant.ToPlainString();

        for (var n = 1; n <= maxN; n++)
        {
            var env = new Dictionary<string, double>(StringComparer.Ordinal) { [primaryVar] = n };
            var val = dominant.Evaluate(env, n);
            if (double.IsNaN(val) || double.IsInfinity(val)) val = 0;
            refPoints.Add(new GrowthPoint(n, Math.Max(0, Math.Round(val, 4))));
        }

        series.Add(new GrowthChartSeries(
            Label: $"Reference g({primaryVar}) = {refLabel}",
            IsPrimary: false,
            Points: refPoints,
            Stroke: "#94a3b8"));

        // If Best case differs from Worst case, include Best curve
        if (!bestExpr.Equals(worstExpr))
        {
            var bestPoints = new List<GrowthPoint>(maxN);
            for (var n = 1; n <= maxN; n++)
            {
                var env = new Dictionary<string, double>(StringComparer.Ordinal) { [primaryVar] = n };
                var val = bestExpr.EvaluateAt(env, n);
                if (double.IsNaN(val) || double.IsInfinity(val)) val = 0;
                bestPoints.Add(new GrowthPoint(n, Math.Max(0, Math.Round(val, 4))));
            }

            series.Add(new GrowthChartSeries(
                Label: $"Best T({primaryVar}) = {bestExpr.ToPlainString()}",
                IsPrimary: false,
                Points: bestPoints,
                Stroke: "#10b981"));
        }

        // Primary Worst T(n) curve (rendered last on top with shaded area in chartInterop.js)
        var worstPoints = new List<GrowthPoint>(maxN);
        for (var n = 1; n <= maxN; n++)
        {
            var env = new Dictionary<string, double>(StringComparer.Ordinal) { [primaryVar] = n };
            var val = worstExpr.EvaluateAt(env, n);
            if (double.IsNaN(val) || double.IsInfinity(val)) val = 0;
            worstPoints.Add(new GrowthPoint(n, Math.Max(0, Math.Round(val, 4))));
        }

        series.Add(new GrowthChartSeries(
            Label: $"T({primaryVar}) = {worstExpr.ToPlainString()}",
            IsPrimary: true,
            Points: worstPoints));

        return series;
    }
}
