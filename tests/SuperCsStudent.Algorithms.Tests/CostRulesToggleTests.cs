using SuperCsStudent.Algorithms.Analysis;
using SuperCsStudent.Algorithms.Ast;
using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Algorithms.Presets;

namespace SuperCsStudent.Algorithms.Tests;

public sealed class CostRulesToggleTests
{
    [Fact]
    public void UnaryIncrementCost2_IncreasesLoopStepAndUnaryCounts()
    {
        var ast = AlgorithmPresets.Get("find-sum").Ast;
        var baseline = IterativeAnalyzer.Analyze(ast, CostRulesProfile.StandardUnitCost);
        var toggled = IterativeAnalyzer.Analyze(ast, CostRulesProfile.StandardUnitCost with
        {
            Preset = CostProfilePreset.Custom,
            UnaryIncrementCost = 2
        });

        Assert.Equal("5n + 3", baseline.WorstCase.Plain);
        Assert.Equal("6n + 3", toggled.WorstCase.Plain);
    }

    [Fact]
    public void ReturnKeywordCost1_AddsOneToReturnStatements()
    {
        var ast = AlgorithmPresets.Get("find-sum").Ast;
        var toggled = IterativeAnalyzer.Analyze(ast, CostRulesProfile.StandardUnitCost with
        {
            Preset = CostProfilePreset.Custom,
            ReturnKeywordCost = 1
        });

        Assert.Equal("5n + 4", toggled.WorstCase.Plain);
    }

    [Fact]
    public void CompoundAssignCost2_IncreasesCompoundLoopAndBodyCosts()
    {
        var twoLoops = IterativeAnalyzer.Analyze(
            AlgorithmPresets.Get("two-loops").Ast,
            CostRulesProfile.StandardUnitCost with { Preset = CostProfilePreset.Custom, CompoundAssignCost = 2 });
        Assert.Equal("10n + 7", twoLoops.WorstCase.Plain);

        // step-2 has BOTH i += 2 (step) and sum += array[i] (body): +n/2 + n/2 = +n
        var step2 = IterativeAnalyzer.Analyze(
            AlgorithmPresets.Get("step-2").Ast,
            CostRulesProfile.StandardUnitCost with { Preset = CostProfilePreset.Custom, CompoundAssignCost = 2 });
        Assert.Equal("3n + 3", step2.WorstCase.Plain);
    }

    [Fact]
    public void CountLoopBoundArithmetic_CountsOperatorsInsideBoundOnEveryCheck()
    {
        var profile = CostRulesProfile.StandardUnitCost with
        {
            Preset = CostProfilePreset.Custom,
            CountLoopBoundArithmetic = true
        };

        // bound-plus10: cond checks (n + 11) now cost 2 each instead of 1 -> +(n + 11) => 5n + 54
        Assert.Equal("5n + 54", IterativeAnalyzer.Analyze(AlgorithmPresets.Get("bound-plus10").Ast, profile).WorstCase.Plain);
        // bound-times5: +(5n + 1) => 25n + 4
        Assert.Equal("25n + 4", IterativeAnalyzer.Analyze(AlgorithmPresets.Get("bound-times5").Ast, profile).WorstCase.Plain);
        // bound-nsq: +(n^2 + 1) => 5n^2 + 4
        Assert.Equal("5n^2 + 4", IterativeAnalyzer.Analyze(AlgorithmPresets.Get("bound-nsq").Ast, profile).WorstCase.Plain);
    }

    [Fact]
    public void FloorLogPlusOne_ShiftsLogLoopIterationAndCheckCounts()
    {
        var profile = CostRulesProfile.StandardUnitCost with
        {
            Preset = CostProfilePreset.Custom,
            LogLoopConvention = LogLoopConvention.FloorLogPlusOne
        };

        // I = log_2 n + 1, cond = log_2 n + 2, step = log_2 n + 1, body = 2(log_2 n + 1) + 2 setup = 4 log_2 n + 7
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("log-base2").Ast, profile);
        Assert.Equal("4 log_2 n + 7", result.WorstCase.Plain);
    }

    [Fact]
    public void CeilingOffset_ShiftsStridedStepLoopIterationCount()
    {
        var profile = CostRulesProfile.StandardUnitCost with
        {
            Preset = CostProfilePreset.Custom,
            StepLoopConvention = StepLoopConvention.CeilingOffset
        };

        // i = 1..n, i += 2: I = (n - 1)/2 + 1 = n/2 + 1/2 => 4(n/2 + 1/2) + 3 = 2n + 5
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("step-2").Ast, profile);
        Assert.Equal("2n + 5", result.WorstCase.Plain);
    }

    [Fact]
    public void DeclarationArrayCallAndLogicalToggles_BehaveDeterministically()
    {
        var fn = new AlgorithmFunction(
            "compoundLogic",
            new[] { "array[]", "a", "b" },
            new AlgorithmStatement[]
            {
                new DeclareStatement(new[] { "x", "y" }),
                new IfStatement(
                    new BinaryExpr("&&",
                        new BinaryExpr("<", new ArrayAccessExpr("array", "0"), new VariableExpr("a")),
                        new BinaryExpr("<", new VariableExpr("a"), new VariableExpr("b"))),
                    new AlgorithmStatement[] { new CallStatement("print", "a") },
                    Array.Empty<AlgorithmStatement>())
            });

        // Standard: declare=0, array[0]=1, <=1, &&=0, <=1, print=1 => Worst = 4
        var std = IterativeAnalyzer.Analyze(fn, CostRulesProfile.StandardUnitCost);
        Assert.Equal("4", std.WorstCase.Plain);

        // Toggle declaration=1 (2 vars -> +2), logical=1 (+1), array=0 (-1), call=0 (-1) => 4 + 2 + 1 - 1 - 1 = 5
        var custom = IterativeAnalyzer.Analyze(fn, CostRulesProfile.StandardUnitCost with
        {
            Preset = CostProfilePreset.Custom,
            DeclarationCost = 1,
            LogicalOperatorCost = 1,
            ArrayIndexingCost = 0,
            FunctionCallCost = 0
        });
        Assert.Equal("5", custom.WorstCase.Plain);

        // StrictSeparateOp preset should strictly exceed StandardUnitCost on find-sum
        var stdSum = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-sum").Ast, CostRulesProfile.StandardUnitCost);
        var strictSum = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-sum").Ast, CostRulesProfile.StrictSeparateOp);
        Assert.True(strictSum.WorstCase.Expression.EvaluateAt(10) > stdSum.WorstCase.Expression.EvaluateAt(10));
    }
}
