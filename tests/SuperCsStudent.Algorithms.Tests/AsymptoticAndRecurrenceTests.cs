using System.Text.Json;
using SuperCsStudent.Algorithms.Analysis;
using SuperCsStudent.Algorithms.Ast;
using SuperCsStudent.Algorithms.Presets;
using SuperCsStudent.Algorithms.Symbolic;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Tests;

public sealed class AsymptoticAndRecurrenceTests
{
    [Fact]
    public void AsymptoticProofs_FollowExactN0SelectionRules()
    {
        var n = SymbolicExpr.Variable("n");

        // 1. Default polynomial: n0 = 1
        var poly = SymbolicExpr.Constant(5) * n + 3;
        var polyRes = AsymptoticAnalyzer.Analyze(poly);
        Assert.Equal("O(n)", polyRes.BigO);
        Assert.Equal("Θ(n)", polyRes.BigTheta);
        Assert.Equal(1, polyRes.BigOProof.N0);
        Assert.Equal(BigRational.FromInt(8), polyRes.BigOProof.C);
        Assert.Equal(1, polyRes.BigOmegaProof.N0);
        Assert.Equal(BigRational.FromInt(5), polyRes.BigOmegaProof.C);
        VerifyWitnessHolds(poly, BasisTerm.Variable("n"), polyRes.BigOProof, isUpper: true);
        VerifyWitnessHolds(poly, BasisTerm.Variable("n"), polyRes.BigOmegaProof, isUpper: false);

        // 2. Logarithmic (log_2 n): n0 = 2
        var logExpr = SymbolicExpr.Constant(4) * SymbolicExpr.Log("n", 2) + 3;
        var logRes = AsymptoticAnalyzer.Analyze(logExpr);
        Assert.Equal("O(log n)", logRes.BigO);
        Assert.Equal("Θ(log n)", logRes.BigTheta);
        Assert.Equal(2, logRes.BigOProof.N0);
        Assert.Equal(BigRational.FromInt(7), logRes.BigOProof.C);
        Assert.Equal(2, logRes.BigOmegaProof.N0);
        Assert.Equal(BigRational.FromInt(4), logRes.BigOmegaProof.C);
        VerifyWitnessHolds(logExpr, BasisTerm.Log("n", 2), logRes.BigOProof, isUpper: true);
        VerifyWitnessHolds(logExpr, BasisTerm.Log("n", 2), logRes.BigOmegaProof, isUpper: false);

        // 3. Lower-order log_2 n / n ratio: n0 = 4!
        var logOverN = SymbolicExpr.Constant(3) * n + SymbolicExpr.Constant(5) * SymbolicExpr.Log("n", 2) + 2;
        var logOverNRes = AsymptoticAnalyzer.Analyze(logOverN);
        Assert.Equal("O(n)", logOverNRes.BigO);
        Assert.Equal(4, logOverNRes.BigOProof.N0);
        Assert.Equal(BigRational.FromInt(6), logOverNRes.BigOProof.C);
        VerifyWitnessHolds(logOverN, BasisTerm.Variable("n"), logOverNRes.BigOProof, isUpper: true);

        // Also quadratic + n log_2 n -> ratio log_2 n / n => n0 = 4
        var quadLog = n.Pow(2) + SymbolicExpr.Constant(4) * n * SymbolicExpr.Log("n", 2);
        var quadLogRes = AsymptoticAnalyzer.Analyze(quadLog);
        Assert.Equal("O(n^2)", quadLogRes.BigO);
        Assert.Equal(4, quadLogRes.BigOProof.N0);
        VerifyWitnessHolds(quadLog, BasisTerm.Variable("n", 2), quadLogRes.BigOProof, isUpper: true);

        // 4. Negative lower-order terms still produce valid Big-Omega witnesses
        var negQuad = SymbolicExpr.Constant(4) * n.Pow(2) - SymbolicExpr.Constant(4) * n - 5;
        var negQuadRes = AsymptoticAnalyzer.Analyze(negQuad);
        VerifyWitnessHolds(negQuad, BasisTerm.Variable("n", 2), negQuadRes.BigOProof, isUpper: true);
        VerifyWitnessHolds(negQuad, BasisTerm.Variable("n", 2), negQuadRes.BigOmegaProof, isUpper: false);
    }

    [Fact]
    public void GrowthChartSeries_SamplesPointsFrom1To25_AndIncludesBestCurveWhenDistinct()
    {
        var findSmall = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-small").Ast);

        // Reference + Best + Primary Worst = 3 series
        Assert.Equal(3, findSmall.GrowthChartSeries.Count);
        Assert.Single(findSmall.GrowthChartSeries, s => s.IsPrimary);
        Assert.All(findSmall.GrowthChartSeries, s =>
        {
            Assert.Equal(25, s.Points.Count);
            Assert.Equal(1, s.Points[0].N);
            Assert.Equal(25, s.Points[^1].N);
        });

        var findSum = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-sum").Ast);
        // Reference + Primary Worst = 2 series (Best == Worst)
        Assert.Equal(2, findSum.GrowthChartSeries.Count);
    }

    [Theory]
    [InlineData(1, null, 1, "1", 0, 0, "O(n)")]           // T(n) = 1 + T(n - 1)
    [InlineData(1, null, 1, "n", 1, 0, "O(n^2)")]         // T(n) = n + T(n - 1)
    [InlineData(1, null, 1, "n^2", 2, 0, "O(n^3)")]       // T(n) = n^2 + T(n - 1)
    [InlineData(1, null, 1, @"\log_2 n", 0, 1, "O(n log n)")] // T(n) = log_2 n + T(n - 1)
    [InlineData(1, 2, null, "n", 1, 0, "O(n)")]           // T(n) = n + T(n / 2)
    [InlineData(2, 2, null, "n", 1, 0, "O(n log n)")]     // T(n) = n + 2T(n / 2)
    [InlineData(2, 3, null, "1", 0, 0, "O(n^{log_3 2})")] // T(n) = 1 + 2T(n / 3)
    [InlineData(3, null, 1, "1", 0, 0, "O(3^n)")]         // T(n) = 1 + 3T(n - 1)
    [InlineData(1, 2, null, "1", 0, 0, "O(log n)")]       // T(n) = 1 + T(n / 2)
    public void RecurrenceAnalyzer_GoldenRecurrences_MatchExactAsymptoticsAndUnrolling(
        int a,
        int? b,
        int? c,
        string workLatex,
        int k,
        int p,
        string expectedBigO)
    {
        var spec = new RecurrenceSpec(
            BaseN: b.HasValue ? 1 : 0,
            BaseCost: 1,
            BranchesA: a,
            DivideByB: b,
            SubtractC: c,
            NonRecursiveWorkLatex: workLatex,
            WorkPolyDegreeK: k,
            WorkLogPowerP: p);

        var result = RecurrenceAnalyzer.Solve(spec);

        Assert.Equal(expectedBigO, result.BigO);
        Assert.Equal(expectedBigO, result.MasterTheorem.BigO);
        Assert.Equal(7, result.UnrollingSteps.Count);
        Assert.Equal("Base Case", result.UnrollingSteps[0].Label);
        Assert.Equal("i = 1", result.UnrollingSteps[1].Label);
        Assert.Equal("i = 2", result.UnrollingSteps[2].Label);
        Assert.Equal("i = 3", result.UnrollingSteps[3].Label);
        Assert.Equal("In general (step i)", result.UnrollingSteps[4].Label);
        Assert.Equal("Stopping condition", result.UnrollingSteps[5].Label);
        Assert.Equal("Closed-form T(n)", result.UnrollingSteps[6].Label);
        Assert.False(string.IsNullOrWhiteSpace(result.MasterTheorem.RecursionTreeWorkRatioLatex));
        Assert.False(string.IsNullOrWhiteSpace(result.MasterTheorem.AkraBazziNote));
    }

    [Fact]
    public void RecurrenceAnalyzer_ExtendedLogarithmicMasterCases_WorkCorrectly()
    {
        // T(n) = 2T(n/2) + n log n => p = 1 > -1 => O(n (log n)^2)
        var case2a = RecurrenceAnalyzer.Solve(new RecurrenceSpec(1, 1, 2, 2, null, @"n \log_2 n", 1, 1));
        Assert.Equal("O(n (log n)^2)", case2a.BigO);
        Assert.Contains("Extended", case2a.MasterTheorem.TheoremName);

        // T(n) = 2T(n/2) + n / log n => p = -1 => O(n log log n)
        var case2b = RecurrenceAnalyzer.Solve(new RecurrenceSpec(1, 1, 2, 2, null, @"\frac{n}{\log_2 n}", 1, -1));
        Assert.Equal("O(n log log n)", case2b.BigO);

        // T(n) = 2T(n/2) + n / (log n)^2 => p = -2 < -1 => O(n)
        var case2c = RecurrenceAnalyzer.Solve(new RecurrenceSpec(1, 1, 2, 2, null, @"\frac{n}{\log_2^2 n}", 1, -2));
        Assert.Equal("O(n)", case2c.BigO);
    }

    [Fact]
    public void All24Presets_ExistAnalyzeCleanly_AndEmitValidBlocklyWorkspaceJson()
    {
        string[] requiredKeys =
        [
            "nested-3", "log-base2", "find-small", "recur-nlogn", "find-sum", "find-x-break",
            "two-loops", "step-2", "step-3", "log-base10", "bound-plus10", "bound-times5",
            "bound-nsq", "nested-square", "nested-dependent", "nested-step4", "nested-log",
            "nested-start3", "nested-start4", "recur-factorial", "recur-n2", "recur-n3",
            "recur-log", "recur-linear-div2"
        ];

        Assert.Equal(24, AlgorithmPresets.All.Count);

        foreach (var key in requiredKeys)
        {
            Assert.True(AlgorithmPresets.TryGet(key, out var preset), $"Missing preset '{key}'.");
            Assert.NotNull(preset);

            // Validate Blockly JSON structure matches Blockly.serialization.workspaces.load()
            using var doc = JsonDocument.Parse(preset!.BlocklyStateJson);
            var blocksObj = doc.RootElement.GetProperty("blocks");
            Assert.Equal(0, blocksObj.GetProperty("languageVersion").GetInt32());
            var rootBlock = blocksObj.GetProperty("blocks")[0];
            Assert.Equal("daa_function", rootBlock.GetProperty("type").GetString());

            // Validate AST analysis works end-to-end
            var analysis = IterativeAnalyzer.Analyze(preset.Ast);
            Assert.NotNull(analysis.WorstCase);
            Assert.NotNull(analysis.Asymptotic);

            if (preset.IsRecursive)
            {
                Assert.NotNull(preset.RecurrenceSpec);
                Assert.NotNull(analysis.Recurrence);
                var direct = RecurrenceAnalyzer.Solve(preset.RecurrenceSpec!);
                Assert.Equal(direct.BigO, analysis.Recurrence!.BigO);
            }
        }
    }

    private static void VerifyWitnessHolds(
        SymbolicExpr expr,
        BasisTerm dominant,
        WitnessProof proof,
        bool isUpper)
    {
        Assert.NotEmpty(proof.StepsLatex);
        var c = proof.C.ToDouble();
        for (var n = proof.N0; n <= proof.N0 + 100; n++)
        {
            var env = new Dictionary<string, double> { ["n"] = n };
            var tv = expr.EvaluateAt(env, n);
            var gv = dominant.Evaluate(env, n);
            if (isUpper)
                Assert.True(tv <= c * gv + 1e-6, $"Big-O failed at n={n}: T({n})={tv} > {c}*{gv}");
            else
                Assert.True(tv >= c * gv - 1e-6, $"Big-Omega failed at n={n}: T({n})={tv} < {c}*{gv}");
        }
    }
}
