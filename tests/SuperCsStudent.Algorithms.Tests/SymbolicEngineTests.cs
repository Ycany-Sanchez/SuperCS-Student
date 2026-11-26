using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Algorithms.Symbolic;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Tests;

public sealed class SymbolicEngineTests
{
    [Fact]
    public void SymbolicExpr_RingOperations_AndFormatting_AreExact()
    {
        var n = SymbolicExpr.Variable("n");
        var expr = (SymbolicExpr.Constant(new BigRational(2, 3)) * n.Pow(3))
                   + (SymbolicExpr.Constant(4) * n.Pow(2))
                   + (SymbolicExpr.Constant(new BigRational(22, 3)) * n)
                   + SymbolicExpr.Constant(3);

        Assert.Equal("(2/3)n^3 + 4n^2 + (22/3)n + 3", expr.ToPlainString());
        Assert.Equal(@"\frac{2}{3}n^3 + 4n^2 + \frac{22}{3}n + 3", expr.ToLatexString());
        Assert.Equal(15.0, expr.EvaluateAt(1), precision: 6);
        Assert.Equal((2.0 / 3.0) * 8 + 16 + (44.0 / 3.0) + 3, expr.EvaluateAt(2), precision: 6);
        Assert.Equal((2.0 / 3.0) * 27 + 36 + 22 + 3, expr.EvaluateAt(3), precision: 6);
    }

    [Fact]
    public void SymbolicExpr_LogarithmicAndExponentialFormatting_PreservesExactBase()
    {
        var log2Expr = SymbolicExpr.Constant(4) * SymbolicExpr.Log("n", 2) + 3;
        var log10Expr = SymbolicExpr.Constant(4) * SymbolicExpr.Log("n", 10) + 3;
        var nLog2Expr = SymbolicExpr.Constant(4) * SymbolicExpr.Variable("n") * SymbolicExpr.Log("n", 2)
                        + SymbolicExpr.Constant(4) * SymbolicExpr.Variable("n") + 3;
        var fracExpr = SymbolicExpr.FractionalPower("n", 3, 2);
        var expExpr = SymbolicExpr.Exp(3, "n");

        Assert.Equal("4 log_2 n + 3", log2Expr.ToPlainString());
        Assert.Equal(@"4 \log_2 n + 3", log2Expr.ToLatexString());

        Assert.Equal("4 log_10 n + 3", log10Expr.ToPlainString());
        Assert.Equal(@"4 \log_{10} n + 3", log10Expr.ToLatexString());

        Assert.Equal("4n log_2 n + 4n + 3", nLog2Expr.ToPlainString());
        Assert.Equal(@"4n \log_2 n + 4n + 3", nLog2Expr.ToLatexString());

        Assert.Equal("n^{log_3 2}", fracExpr.ToPlainString());
        Assert.Equal(@"n^{\log_3 2}", fracExpr.ToLatexString());

        Assert.Equal("3^n", expExpr.ToPlainString());
        Assert.Equal("3^{n}", expExpr.ToLatexString());
    }

    [Fact]
    public void SymbolicExpr_SubstitutionAndMonomialDivision_WorkExactly()
    {
        var i = SymbolicExpr.Variable("i");
        var n = SymbolicExpr.Variable("n");
        var poly = SymbolicExpr.Constant(4) * i + 2;

        var sub = poly.Substitute("i", n + 1);
        Assert.Equal("4n + 6", sub.ToPlainString());

        var quotient = (SymbolicExpr.Constant(2) * n.Pow(2) + SymbolicExpr.Constant(4) * n) / n;
        Assert.Equal("2n + 4", quotient.ToPlainString());
    }

    [Fact]
    public void SymbolicParser_ParsesBoundsAndCountsArithmeticOps()
    {
        Assert.Equal("n + 10", SymbolicParser.Parse("n+10").ToPlainString());
        Assert.Equal("5n", SymbolicParser.Parse("n*5").ToPlainString());
        Assert.Equal("n^2", SymbolicParser.Parse("n*n").ToPlainString());
        Assert.Equal("log_2 n", SymbolicParser.Parse("log_2(n)").ToPlainString());

        Assert.Equal(1, SymbolicParser.CountArithmeticOps("n+10"));
        Assert.Equal(1, SymbolicParser.CountArithmeticOps("n*5"));
        Assert.Equal(1, SymbolicParser.CountArithmeticOps("n*n"));
        Assert.Equal(0, SymbolicParser.CountArithmeticOps("n"));
        Assert.Equal(0, SymbolicParser.CountArithmeticOps("-1"));
    }

    [Fact]
    public void SummationEngine_PowerSums_OneThroughThree_AreExact()
    {
        var k = SymbolicExpr.Variable("k");
        var n = SymbolicExpr.Variable("n");

        var s1 = SummationEngine.Sum(k, "k", 1, n);
        Assert.Equal("(1/2)n^2 + (1/2)n", s1.Result.ToPlainString());
        Assert.NotEmpty(s1.Steps);

        var s2 = SummationEngine.Sum(k.Pow(2), "k", 1, n);
        Assert.Equal("(1/3)n^3 + (1/2)n^2 + (1/6)n", s2.Result.ToPlainString());

        var s3 = SummationEngine.Sum(k.Pow(3), "k", 1, n);
        Assert.Equal("(1/4)n^4 + (1/2)n^3 + (1/4)n^2", s3.Result.ToPlainString());
    }

    [Fact]
    public void SummationEngine_LowerBoundSplitting_AndZeroOffset_AreExact()
    {
        var i = SymbolicExpr.Variable("i");
        var n = SymbolicExpr.Variable("n");

        var split = SummationEngine.Sum(SymbolicExpr.Constant(4) * i + 2, "i", 4, n);
        Assert.Equal("2n^2 + 4n - 30", split.Result.ToPlainString());
        Assert.Contains(split.Steps, s => s.Label.Contains("Lower-Bound", StringComparison.OrdinalIgnoreCase));

        var zeroConst = SummationEngine.Sum(SymbolicExpr.Constant(3), "i", 0, n);
        Assert.Equal("3n + 3", zeroConst.Result.ToPlainString());

        var zeroLinear = SummationEngine.Sum(i, "i", 0, n);
        Assert.Equal("(1/2)n^2 + (1/2)n", zeroLinear.Result.ToPlainString());
    }

    [Fact]
    public void SummationEngine_StridedStep_Multiplicative_Stirling_AndGeometric_AreExact()
    {
        var k = SymbolicExpr.Variable("k");
        var i = SymbolicExpr.Variable("i");
        var n = SymbolicExpr.Variable("n");

        // Outer step s = 4 with constant body 4n + 2
        var stridedConst = SummationEngine.Sum(
            SymbolicExpr.Constant(4) * n + 2, "i", 1, n, step: 4, StepLoopConvention.AlgebraicRatio);
        Assert.Equal("n^2 + (1/2)n", stridedConst.Result.ToPlainString());

        // Outer step s = 4 with dependent body i: verifies exact match at n = 4, 8, 12
        var stridedDep = SummationEngine.Sum(i, "i", 1, n, step: 4, StepLoopConvention.AlgebraicRatio);
        Assert.Equal(1.0, stridedDep.Result.EvaluateAt(4), precision: 6);
        Assert.Equal(6.0, stridedDep.Result.EvaluateAt(8), precision: 6);
        Assert.Equal(15.0, stridedDep.Result.EvaluateAt(12), precision: 6);

        // Multiplicative base-2 & base-10
        var mult2 = SummationEngine.SumMultiplicative(SymbolicExpr.Constant(4), "i", n, 2, LogLoopConvention.ExactLogBase);
        Assert.Equal("4 log_2 n", mult2.Result.ToPlainString());

        var mult10 = SummationEngine.SumMultiplicative(SymbolicExpr.Constant(4), "i", n, 10, LogLoopConvention.FloorLogPlusOne);
        Assert.Equal("4 log_10 n + 4", mult10.Result.ToPlainString());

        // Stirling's approximation: sum_{i=1}^n log_2 i ~ n log_2 n
        var stirling = SummationEngine.Sum(SymbolicExpr.Log("i", 2), "i", 1, n);
        Assert.Equal("n log_2 n", stirling.Result.ToPlainString());
        Assert.Contains(stirling.Steps, s => s.Label.Contains("Stirling", StringComparison.OrdinalIgnoreCase));

        // Geometric series: sum_{k=i}^n a * r^k = (a r^{n+1} - a r^i) / (r - 1)
        var geom = SummationEngine.SumGeometric(3, 2, "k", i, n);
        // 3*(2^{n+1} - 2^i)/(2 - 1) = 6 * 2^n - 3 * 2^i
        var env = new Dictionary<string, double> { ["n"] = 4, ["i"] = 1 };
        // Direct sum: 3*(2 + 4 + 8 + 16) = 90
        Assert.Equal(90.0, geom.Result.EvaluateAt(env), precision: 6);

        // Geometric sum via general Sum() dispatch: sum_{k=0}^{n-1} 3^k = (1/2) * 3^n - 1/2
        var geom3 = SummationEngine.Sum(SymbolicExpr.Exp(3, "k"), "k", 0, n - 1);
        Assert.Equal(13.0, geom3.Result.EvaluateAt(3), precision: 6);
        Assert.Equal(40.0, geom3.Result.EvaluateAt(4), precision: 6);
    }
}
