using System.Collections.Immutable;
using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Symbolic;

/// <summary>
/// A single structured step in an algebraic runtime or recurrence derivation.
/// </summary>
public sealed record DerivationStep(
    string Label,
    string Latex,
    string Plain,
    string? BlockId = null,
    string? Note = null);

/// <summary>
/// Result of evaluating a closed-form symbolic summation along with its intermediate derivation steps.
/// </summary>
public sealed record SummationResult(
    SymbolicExpr Result,
    IReadOnlyList<DerivationStep> Steps);

/// <summary>
/// Exact closed-form symbolic summation engine built on SymbolicExpr and BigRational.
/// Supports linearity, constant sums, power sums (k^0..k^4), arbitrary lower-bound splitting,
/// k = 0 offset handling, strided outer steps, multiplicative logarithmic loops,
/// Stirling's approximation for log sums, and exact geometric series.
/// </summary>
public static class SummationEngine
{
    public static SummationResult Sum(
        SymbolicExpr body,
        string indexVar,
        SymbolicExpr lowerExpr,
        SymbolicExpr upperExpr) =>
        Sum(body, indexVar, lowerExpr, upperExpr, step: 1, StepLoopConvention.AlgebraicRatio);

    public static SummationResult Sum(
        SymbolicExpr body,
        string indexVar,
        SymbolicExpr lowerExpr,
        SymbolicExpr upperExpr,
        int step,
        StepLoopConvention stepConvention = StepLoopConvention.AlgebraicRatio,
        string? blockId = null)
    {
        if (step <= 0) throw new ArgumentOutOfRangeException(nameof(step), "Loop step must be positive.");

        var steps = new List<DerivationStep>();

        if (body.IsZero)
        {
            steps.Add(new DerivationStep("Zero Sum", "0", "0", blockId));
            return new SummationResult(SymbolicExpr.Zero, steps);
        }

        // Case 1: Outer step s > 1
        if (step > 1)
        {
            var iterCount = stepConvention == StepLoopConvention.AlgebraicRatio
                ? (upperExpr - lowerExpr + SymbolicExpr.One) / BigRational.FromInt(step)
                : (upperExpr - lowerExpr) / BigRational.FromInt(step) + SymbolicExpr.One;

            if (!body.ContainsVariable(indexVar))
            {
                var res = body * iterCount;
                steps.Add(new DerivationStep(
                    "Strided Constant Sum",
                    $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()},\, \text{{step }} {step}}}^{{{upperExpr.ToLatexString()}}} \left({body.ToLatexString()}\right) = \left({iterCount.ToLatexString()}\right)\left({body.ToLatexString()}\right) = {res.ToLatexString()}",
                    $"sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}, step {step}) of ({body.ToPlainString()}) = ({iterCount.ToPlainString()})*({body.ToPlainString()}) = {res.ToPlainString()}",
                    blockId));
                return new SummationResult(res, steps);
            }

            // Re-index k = L + s*(m - 1) for m = 1..iterCount
            var reindexVar = $"_m_{indexVar}";
            var substituted = body.Substitute(
                indexVar,
                lowerExpr + SymbolicExpr.Constant(step) * (SymbolicExpr.Variable(reindexVar) - SymbolicExpr.One));

            steps.Add(new DerivationStep(
                "Step Substitution",
                $@"\text{{Substitute }} {indexVar} = {lowerExpr.ToLatexString()} + {step}(m - 1),\; m = 1 \ldots {iterCount.ToLatexString()}",
                $"Substitute {indexVar} = {lowerExpr.ToPlainString()} + {step}(m - 1), m = 1..{iterCount.ToPlainString()}",
                blockId));

            var inner = Sum(substituted, reindexVar, SymbolicExpr.One, iterCount, 1, stepConvention, blockId);
            steps.AddRange(inner.Steps);
            return new SummationResult(inner.Result, steps);
        }

        // Case 2: Body independent of indexVar (constant sum)
        if (!body.ContainsVariable(indexVar))
        {
            var count = upperExpr - lowerExpr + SymbolicExpr.One;
            var res = body * count;
            steps.Add(new DerivationStep(
                "Constant Sum",
                $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} \left({body.ToLatexString()}\right) = \left({body.ToLatexString()}\right)\left({count.ToLatexString()}\right) = {res.ToLatexString()}",
                $"sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}) of ({body.ToPlainString()}) = ({body.ToPlainString()})*({count.ToPlainString()}) = {res.ToPlainString()}",
                blockId));
            return new SummationResult(res, steps);
        }

        // Case 3: Split sum by linearity across basis terms
        var sumLatex = $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} \left({body.ToLatexString()}\right)";
        var sumPlain = $"sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}) ({body.ToPlainString()})";

        if (body.Terms.Count > 1)
        {
            var splitLatexParts = new List<string>();
            var splitPlainParts = new List<string>();
            foreach (var (basis, coeff) in body.Terms)
            {
                var termExpr = SymbolicExpr.FromTerm(basis, coeff);
                splitLatexParts.Add($@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} \left({termExpr.ToLatexString()}\right)");
                splitPlainParts.Add($"sum({termExpr.ToPlainString()})");
            }
            steps.Add(new DerivationStep(
                "Linearity",
                $"{sumLatex} = {string.Join(" + ", splitLatexParts)}",
                $"{sumPlain} = {string.Join(" + ", splitPlainParts)}",
                blockId));
        }

        var total = SymbolicExpr.Zero;
        foreach (var (basis, coeff) in body.Terms)
        {
            var termRes = SumSingleTerm(basis, coeff, indexVar, lowerExpr, upperExpr, blockId, steps);
            total += termRes;
        }

        steps.Add(new DerivationStep(
            "Closed Form",
            $"{sumLatex} = {total.ToLatexString()}",
            $"{sumPlain} = {total.ToPlainString()}",
            blockId));

        return new SummationResult(total, steps);
    }

    /// <summary>
    /// Exact geometric series sum: sum_{k=L}^{U} a * r^k = a * (r^{U+1} - r^L) / (r - 1).
    /// </summary>
    public static SummationResult SumGeometric(
        BigRational a,
        BigRational r,
        string indexVar,
        SymbolicExpr lowerExpr,
        SymbolicExpr upperExpr,
        string? blockId = null)
    {
        var steps = new List<DerivationStep>();
        if (a.IsZero)
        {
            return new SummationResult(SymbolicExpr.Zero, steps);
        }

        if (r.IsOne)
        {
            var count = upperExpr - lowerExpr + SymbolicExpr.One;
            var constRes = SymbolicExpr.Constant(a) * count;
            steps.Add(new DerivationStep(
                "Geometric Series (r = 1)",
                $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} {a.ToLatexString()} = {constRes.ToLatexString()}",
                $"sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}) {a.ToPlainString()} = {constRes.ToPlainString()}",
                blockId));
            return new SummationResult(constRes, steps);
        }

        var rUpperPlusOne = SymbolicExpr.ExpOf(r, upperExpr + SymbolicExpr.One);
        var rLower = SymbolicExpr.ExpOf(r, lowerExpr);
        var result = SymbolicExpr.Constant(a) * (rUpperPlusOne - rLower) / (r - BigRational.One);

        var rLatex = r.IsInteger ? r.ToLatexString() : $@"\left({r.ToLatexString()}\right)";
        var aPrefixLatex = a.IsOne ? string.Empty : $@"{a.ToLatexString()} \cdot ";

        steps.Add(new DerivationStep(
            "Geometric Series",
            $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} {aPrefixLatex}{rLatex}^{{{indexVar}}} = \frac{{{aPrefixLatex}{rLatex}^{{{(upperExpr + SymbolicExpr.One).ToLatexString()}}} - {aPrefixLatex}{rLatex}^{{{lowerExpr.ToLatexString()}}}}}{{{r.ToLatexString()} - 1}} = {result.ToLatexString()}",
            $"sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}) {a}*{r}^{indexVar} = {result.ToPlainString()}",
            blockId));

        return new SummationResult(result, steps);
    }

    /// <summary>
    /// Evaluates a multiplicative loop summation (e.g., i = start; i &lt;= bound; i *= baseVal).
    /// Preserves the exact logarithm base.
    /// </summary>
    public static SummationResult SumMultiplicative(
        SymbolicExpr body,
        string indexVar,
        SymbolicExpr boundExpr,
        int baseVal,
        LogLoopConvention logConvention = LogLoopConvention.ExactLogBase,
        string? blockId = null)
    {
        if (baseVal < 2) throw new ArgumentOutOfRangeException(nameof(baseVal), "Multiplicative base must be >= 2.");

        var steps = new List<DerivationStep>();
        var logExpr = SymbolicExpr.LogOf(boundExpr, baseVal);
        var iterations = logConvention == LogLoopConvention.ExactLogBase
            ? logExpr
            : logExpr + SymbolicExpr.One;

        if (!body.ContainsVariable(indexVar))
        {
            var result = body * iterations;
            steps.Add(new DerivationStep(
                "Multiplicative Loop Sum",
                $@"I = {iterations.ToLatexString()} \implies \left({iterations.ToLatexString()}\right)\left({body.ToLatexString()}\right) = {result.ToLatexString()}",
                $"I = {iterations.ToPlainString()} => ({iterations.ToPlainString()})*({body.ToPlainString()}) = {result.ToPlainString()}",
                blockId));
            return new SummationResult(result, steps);
        }

        // Body depends on indexVar (e.g. inner loop bounded by i while outer loop multiplies i *= b):
        // Substitute i = b^m for m = 0..(iterations - 1) and evaluate geometric series!
        var total = SymbolicExpr.Zero;
        foreach (var (basis, coeff) in body.Terms)
        {
            if (!basis.ContainsVariable(indexVar))
            {
                total += SymbolicExpr.FromTerm(basis, coeff) * iterations;
                continue;
            }

            if (basis.VarPowers.TryGetValue(indexVar, out var p) && p >= 1 &&
                basis.Logs.All(l => l.VarName != indexVar) &&
                basis.Exps.All(e => e.VarName != indexVar))
            {
                var r = BigRational.FromInt(baseVal).Pow(p);
                var otherFactor = StripVariablePower(basis, indexVar);
                // sum_{m=0}^{I-1} (b^p)^m = ((b^p)^I - 1) / (b^p - 1)
                // For I = log_b(bound), (b^p)^{log_b(bound)} = bound^p
                var upperPower = logConvention == LogLoopConvention.ExactLogBase
                    ? boundExpr.Pow(p)
                    : SymbolicExpr.Constant(r) * boundExpr.Pow(p);
                var geom = (upperPower - SymbolicExpr.One) / (r - BigRational.One);
                total += SymbolicExpr.FromTerm(otherFactor, coeff) * geom;
            }
            else
            {
                // Fallback: substitute boundExpr conservatively
                total += SymbolicExpr.FromTerm(basis, coeff).Substitute(indexVar, boundExpr) * iterations;
            }
        }

        steps.Add(new DerivationStep(
            "Multiplicative Geometric Sum",
            $@"\sum_{{m=0}}^{{{iterations.ToLatexString()} - 1}} \left. {body.ToLatexString()} \right|_{{{indexVar}={baseVal}^m}} = {total.ToLatexString()}",
            $"Geometric multiplicative sum = {total.ToPlainString()}",
            blockId));

        return new SummationResult(total, steps);
    }

    private static SymbolicExpr SumSingleTerm(
        BasisTerm basis,
        BigRational coeff,
        string indexVar,
        SymbolicExpr lowerExpr,
        SymbolicExpr upperExpr,
        string? blockId,
        List<DerivationStep> steps)
    {
        // Independent of indexVar -> constant sum
        if (!basis.ContainsVariable(indexVar))
        {
            var count = upperExpr - lowerExpr + SymbolicExpr.One;
            var res = SymbolicExpr.FromTerm(basis, coeff) * count;
            steps.Add(new DerivationStep(
                "Constant Term",
                $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} {SymbolicExpr.FromTerm(basis, coeff).ToLatexString()} = {res.ToLatexString()}",
                $"sum({SymbolicExpr.FromTerm(basis, coeff).ToPlainString()}) = {res.ToPlainString()}",
                blockId));
            return res;
        }

        // Check exponential term a * r^{indexVar}
        var expMatch = basis.Exps.FirstOrDefault(e => e.VarName == indexVar);
        if (expMatch.VarName == indexVar &&
            !basis.VarPowers.ContainsKey(indexVar) &&
            basis.Logs.All(l => l.VarName != indexVar))
        {
            var remainingExps = basis.Exps.Where(e => e.VarName != indexVar).ToImmutableArray();
            var outerBasis = new BasisTerm(basis.VarPowers, basis.Logs, remainingExps, basis.FracPower);
            var geom = SumGeometric(coeff, expMatch.Base, indexVar, lowerExpr, upperExpr, blockId);
            steps.AddRange(geom.Steps);
            return SymbolicExpr.FromTerm(outerBasis, BigRational.One) * geom.Result;
        }

        // Check Stirling's approximation for log_b(indexVar)
        var logMatch = basis.Logs.FirstOrDefault(l => l.VarName == indexVar);
        if (logMatch.VarName == indexVar &&
            logMatch.Power == 1 &&
            !basis.VarPowers.ContainsKey(indexVar) &&
            basis.Exps.All(e => e.VarName != indexVar))
        {
            var remainingLogs = basis.Logs.Where(l => l.VarName != indexVar).ToImmutableArray();
            var outerBasis = new BasisTerm(basis.VarPowers, remainingLogs, basis.Exps, basis.FracPower);
            var upperLog = upperExpr * SymbolicExpr.LogOf(upperExpr, logMatch.Base);

            SymbolicExpr stirlingResult;
            if (lowerExpr.IsConstant && lowerExpr.ConstantValue <= BigRational.One)
            {
                stirlingResult = upperLog;
            }
            else if (lowerExpr.IsConstant)
            {
                // Subtract constant lower portion if integer > 1
                var lowMinusOne = lowerExpr - SymbolicExpr.One;
                stirlingResult = upperLog - lowMinusOne * SymbolicExpr.LogOf(lowMinusOne, logMatch.Base);
            }
            else
            {
                var lowMinusOne = lowerExpr - SymbolicExpr.One;
                stirlingResult = upperLog - lowMinusOne * SymbolicExpr.LogOf(lowMinusOne, logMatch.Base);
            }

            var scaled = SymbolicExpr.FromTerm(outerBasis, coeff) * stirlingResult;
            var baseLatex = logMatch.Base >= 10 ? $"{{{logMatch.Base}}}" : logMatch.Base.ToString();
            steps.Add(new DerivationStep(
                "Stirling's Approximation",
                $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} \log_{baseLatex} {indexVar} = \log_{baseLatex}({upperExpr.ToLatexString()}!) \approx {stirlingResult.ToLatexString()}",
                $"Stirling: sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}) log_{logMatch.Base} {indexVar} ~ {stirlingResult.ToPlainString()}",
                blockId));
            return scaled;
        }

        // Standard polynomial power sum k^p
        if (basis.VarPowers.TryGetValue(indexVar, out var power) &&
            basis.Logs.All(l => l.VarName != indexVar) &&
            basis.Exps.All(e => e.VarName != indexVar))
        {
            var outerBasis = StripVariablePower(basis, indexVar);
            var outerFactor = SymbolicExpr.FromTerm(outerBasis, coeff);
            var powerSum = EvaluatePowerSum(power, indexVar, lowerExpr, upperExpr, blockId, steps);
            return outerFactor * powerSum;
        }

        throw new NotSupportedException(
            $"Unsupported summation term '{basis.ToPlainString()}' over index '{indexVar}'.");
    }

    private static SymbolicExpr EvaluatePowerSum(
        int power,
        string indexVar,
        SymbolicExpr lowerExpr,
        SymbolicExpr upperExpr,
        string? blockId,
        List<DerivationStep> steps)
    {
        var powExprLatex = power == 1 ? indexVar : $"{indexVar}^{power}";
        var powExprPlain = power == 1 ? indexVar : $"{indexVar}^{power}";

        // Lower bound L = 1
        if (lowerExpr.IsConstant && lowerExpr.ConstantValue.IsOne)
        {
            var sum1ToU = FaulhaberFromOne(power, upperExpr);
            steps.Add(new DerivationStep(
                $"Power Sum (k^{power})",
                $@"\sum_{{{indexVar}=1}}^{{{upperExpr.ToLatexString()}}} {powExprLatex} = {FaulhaberFormulaLatex(power, upperExpr)} = {sum1ToU.ToLatexString()}",
                $"sum({indexVar}=1..{upperExpr.ToPlainString()}) {powExprPlain} = {sum1ToU.ToPlainString()}",
                blockId));
            return sum1ToU;
        }

        // Lower bound L = 0 (since 0^p = 0 for p >= 1)
        if (lowerExpr.IsZero)
        {
            var sum1ToU = FaulhaberFromOne(power, upperExpr);
            steps.Add(new DerivationStep(
                $"Zero-Offset Power Sum (k^{power})",
                $@"\sum_{{{indexVar}=0}}^{{{upperExpr.ToLatexString()}}} {powExprLatex} = 0 + \sum_{{{indexVar}=1}}^{{{upperExpr.ToLatexString()}}} {powExprLatex} = {sum1ToU.ToLatexString()}",
                $"sum({indexVar}=0..{upperExpr.ToPlainString()}) {powExprPlain} = {sum1ToU.ToPlainString()}",
                blockId));
            return sum1ToU;
        }

        // Arbitrary lower bound L > 1 (numeric or symbolic): split as sum(1..U) - sum(1..L-1)
        var upperSum = FaulhaberFromOne(power, upperExpr);
        var lowerMinusOne = lowerExpr - SymbolicExpr.One;
        var lowerSum = FaulhaberFromOne(power, lowerMinusOne);
        var diff = upperSum - lowerSum;

        steps.Add(new DerivationStep(
            "Lower-Bound Splitting",
            $@"\sum_{{{indexVar}={lowerExpr.ToLatexString()}}}^{{{upperExpr.ToLatexString()}}} {powExprLatex} = \sum_{{{indexVar}=1}}^{{{upperExpr.ToLatexString()}}} {powExprLatex} - \sum_{{{indexVar}=1}}^{{{lowerMinusOne.ToLatexString()}}} {powExprLatex} = \left({upperSum.ToLatexString()}\right) - \left({lowerSum.ToLatexString()}\right) = {diff.ToLatexString()}",
            $"sum({indexVar}={lowerExpr.ToPlainString()}..{upperExpr.ToPlainString()}) {powExprPlain} = ({upperSum.ToPlainString()}) - ({lowerSum.ToPlainString()}) = {diff.ToPlainString()}",
            blockId));

        return diff;
    }

    /// <summary>
    /// Exact Faulhaber closed form for sum_{k=1}^{U} k^p (p = 0, 1, 2, 3, 4).
    /// </summary>
    public static SymbolicExpr FaulhaberFromOne(int power, SymbolicExpr u)
    {
        return power switch
        {
            0 => u,
            1 => u * (u + SymbolicExpr.One) / BigRational.FromInt(2),
            2 => u * (u + SymbolicExpr.One) * (SymbolicExpr.Constant(2) * u + SymbolicExpr.One) / BigRational.FromInt(6),
            3 => u.Pow(2) * (u + SymbolicExpr.One).Pow(2) / BigRational.FromInt(4),
            4 => u * (u + SymbolicExpr.One) * (SymbolicExpr.Constant(2) * u + SymbolicExpr.One) *
                 (SymbolicExpr.Constant(3) * u.Pow(2) + SymbolicExpr.Constant(3) * u - SymbolicExpr.One) / BigRational.FromInt(30),
            _ => throw new NotSupportedException($"Closed-form power sum for degree {power} is not supported.")
        };
    }

    private static string FaulhaberFormulaLatex(int power, SymbolicExpr u)
    {
        var us = u.Terms.Count > 1 ? $@"\left({u.ToLatexString()}\right)" : u.ToLatexString();
        var uPlus1 = (u + SymbolicExpr.One).ToLatexString();
        var twoUPlus1 = (SymbolicExpr.Constant(2) * u + SymbolicExpr.One).ToLatexString();
        return power switch
        {
            1 => $@"\frac{{{us}({uPlus1})}}{{2}}",
            2 => $@"\frac{{{us}({uPlus1})({twoUPlus1})}}{{6}}",
            3 => $@"\frac{{{us}^2({uPlus1})^2}}{{4}}",
            _ => FaulhaberFromOne(power, u).ToLatexString()
        };
    }

    private static BasisTerm StripVariablePower(BasisTerm basis, string indexVar)
    {
        var remainingVars = basis.VarPowers.Remove(indexVar);
        return new BasisTerm(remainingVars, basis.Logs, basis.Exps, basis.FracPower);
    }
}
