using SuperCsStudent.Algorithms.Analysis;
using SuperCsStudent.Algorithms.Ast;
using SuperCsStudent.Algorithms.Config;
using SuperCsStudent.Algorithms.Presets;

namespace SuperCsStudent.Algorithms.Tests;

public sealed class IterativeAnalyzerGoldenTests
{
    [Fact]
    public void SequentialStatements_CountExactFiveOps()
    {
        var fn = new AlgorithmFunction(
            "sequentialDemo",
            Array.Empty<string>(),
            new AlgorithmStatement[]
            {
                new AssignStatement("a", "=", new LiteralExpr("5"), "s1"),
                new AssignStatement("b", "=", new LiteralExpr("3"), "s2"),
                new AssignStatement("c", "=", new BinaryExpr("+", new VariableExpr("a"), new VariableExpr("b")), "s3"),
                new CallStatement("print", "c", "s4")
            });

        var result = IterativeAnalyzer.Analyze(fn, CostRulesProfile.StandardUnitCost);

        Assert.Equal("5", result.WorstCase.Plain);
        Assert.Equal("5", result.BestCase.Plain);
        Assert.Equal("O(1)", result.Asymptotic.BigO);
        Assert.Equal("Θ(1)", result.Asymptotic.BigTheta);
    }

    [Fact]
    public void SelectionBranch_ReturnsWorstThree_AndBestTwo()
    {
        var fn = new AlgorithmFunction(
            "selectMin",
            new[] { "a", "b" },
            new AlgorithmStatement[]
            {
                new IfStatement(
                    new BinaryExpr("<", new VariableExpr("a"), new VariableExpr("b")),
                    new AlgorithmStatement[]
                    {
                        new AssignStatement("min", "=", new BinaryExpr("*", new VariableExpr("a"), new VariableExpr("b")), "t1")
                    },
                    new AlgorithmStatement[]
                    {
                        new AssignStatement("min", "=", new VariableExpr("b"), "e1")
                    },
                    "if1")
            });

        var result = IterativeAnalyzer.Analyze(fn, CostRulesProfile.StandardUnitCost);

        Assert.Equal("3", result.WorstCase.Plain);
        Assert.Equal("2", result.BestCase.Plain);
        Assert.True(result.Asymptotic.IsTight);
    }

    [Fact]
    public void SingleLoopWithUnaryAndPrint_Returns3bPlus6()
    {
        var fn = new AlgorithmFunction(
            "countSteps",
            new[] { "b" },
            new AlgorithmStatement[]
            {
                new AssignStatement("a", "=", new LiteralExpr("5"), "b1"),
                new AssignStatement("c", "=", new BinaryExpr("+", new VariableExpr("a"), new VariableExpr("b")), "b2"),
                new ForStatement("i", "1", "<=", "b", "++", "1", new AlgorithmStatement[]
                {
                    new UnaryStatement("c", "++", "b3")
                }, "b_for"),
                new CallStatement("print", "c", "b4")
            });

        var result = IterativeAnalyzer.Analyze(fn, CostRulesProfile.StandardUnitCost);

        Assert.Equal("3b + 6", result.WorstCase.Plain);
        Assert.Equal("b", result.PrimaryVariable);
        Assert.Equal("O(b)", result.Asymptotic.BigO);
    }

    [Fact]
    public void FindSum_Returns5nPlus3()
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-sum").Ast);

        Assert.Equal("5n + 3", result.WorstCase.Plain);
        Assert.Equal("5n + 3", result.BestCase.Plain);
        Assert.Equal("Θ(n)", result.Asymptotic.BigTheta);
    }

    [Fact]
    public void FindSmall_ReturnsWorst6nMinus2_AndBest4n()
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-small").Ast);

        Assert.Equal("6n - 2", result.WorstCase.Plain);
        Assert.Equal("4n", result.BestCase.Plain);
        Assert.Equal("O(n)", result.Asymptotic.BigO);
        Assert.Equal("Ω(n)", result.Asymptotic.BigOmega);
        Assert.Equal("Θ(n)", result.Asymptotic.BigTheta);
        Assert.True(result.Asymptotic.IsTight);
    }

    [Fact]
    public void FindXWithBreak_ReturnsWorst4nPlus3_Best7_AndNotTight()
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("find-x-break").Ast);

        Assert.Equal("4n + 3", result.WorstCase.Plain);
        Assert.Equal("7", result.BestCase.Plain);
        Assert.Equal("O(n)", result.Asymptotic.BigO);
        Assert.Equal("Ω(1)", result.Asymptotic.BigOmega);
        Assert.False(result.Asymptotic.IsTight);
        Assert.Null(result.Asymptotic.BigTheta);
        Assert.Equal("Not tight", result.Asymptotic.BigThetaDisplay);
    }

    [Fact]
    public void TwoSequentialLoops_Returns8nPlus7()
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("two-loops").Ast);

        Assert.Equal("8n + 7", result.WorstCase.Plain);
        Assert.Equal("8n + 7", result.BestCase.Plain);
    }

    [Theory]
    [InlineData("step-2", "2n + 3", "O(n)")]
    [InlineData("step-3", "(4/3)n + 3", "O(n)")]
    [InlineData("log-base2", "4 log_2 n + 3", "O(log n)")]
    [InlineData("log-base10", "4 log_10 n + 3", "O(log n)")]
    [InlineData("bound-plus10", "4n + 43", "O(n)")]
    [InlineData("bound-times5", "20n + 3", "O(n)")]
    [InlineData("bound-nsq", "4n^2 + 3", "O(n^2)")]
    public void StepLogAndModifiedBoundLoops_MatchGoldenFormulas(
        string presetKey,
        string expectedPlain,
        string expectedBigO)
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get(presetKey).Ast);

        Assert.Equal(expectedPlain, result.WorstCase.Plain);
        Assert.Equal(expectedBigO, result.Asymptotic.BigO);
    }

    [Theory]
    [InlineData("nested-square", "4n^2 + 4n + 3", "Θ(n^2)")]
    [InlineData("nested-dependent", "2n^2 + 6n + 3", "Θ(n^2)")]
    [InlineData("nested-step4", "n^2 + n + 3", "Θ(n^2)")]
    [InlineData("nested-log", "4n log_2 n + 4n + 3", "Θ(n log n)")]
    [InlineData("nested-start3", "4n^2 - 4n - 5", "Θ(n^2)")]
    [InlineData("nested-start4", "2n^2 + 6n - 33", "Θ(n^2)")]
    [InlineData("nested-3", "(2/3)n^3 + 4n^2 + (22/3)n + 3", "Θ(n^3)")]
    public void NestedLoops_MatchGoldenFormulas(
        string presetKey,
        string expectedPlain,
        string expectedTheta)
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get(presetKey).Ast);

        Assert.Equal(expectedPlain, result.WorstCase.Plain);
        Assert.Equal(expectedTheta, result.Asymptotic.BigTheta);
    }

    [Fact]
    public void TripleNestedLoop_EmitsExactLatexAndNestedInnerBoxes()
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("nested-3").Ast);

        Assert.Equal(@"\frac{2}{3}n^3 + 4n^2 + \frac{22}{3}n + 3", result.WorstCase.Latex);
        Assert.Equal(2, result.WorstCase.InnerBoxes.Count);
        Assert.Contains(result.WorstCase.InnerBoxes, b => b.Plain == "T_in2(j) = 4j + 2");
        Assert.Contains(result.WorstCase.InnerBoxes, b => b.Plain == "T_in(i) = 2i^2 + 6i + 2");
        Assert.Contains(result.WorstCase.AnnotatedLines, l => l.BoxHeaderLabel == "T_in2(j) = 4j + 2");
        Assert.Contains(result.WorstCase.AnnotatedLines, l => l.BoxHeaderLabel == "T_in(i) = 2i^2 + 6i + 2");
    }

    [Fact]
    public void NestedTripleLoop_PopulatesOuterToInnerBoxGroupChainAndClosingBraceOwnership()
    {
        var result = IterativeAnalyzer.Analyze(AlgorithmPresets.Get("nested-3").Ast);
        var lines = result.WorstCase.AnnotatedLines;

        var outerHeader = lines.Single(l => l.CodeText.StartsWith("for (i ", StringComparison.Ordinal));
        var midHeader = lines.Single(l => l.CodeText.StartsWith("for (j ", StringComparison.Ordinal));
        var innerHeader = lines.Single(l => l.CodeText.StartsWith("for (k ", StringComparison.Ordinal));
        var innerBody = lines.Single(l => l.CodeText.Contains("sum = sum + k", StringComparison.Ordinal));

        Assert.Null(outerHeader.BoxGroupChain);
        Assert.NotNull(midHeader.BoxGroupId);
        Assert.NotNull(innerHeader.BoxGroupId);
        Assert.NotEqual(midHeader.BoxGroupId, innerHeader.BoxGroupId);

        Assert.Equal(new[] { midHeader.BoxGroupId! }, midHeader.BoxGroupChain);
        Assert.Equal(new[] { midHeader.BoxGroupId!, innerHeader.BoxGroupId! }, innerHeader.BoxGroupChain);
        Assert.Equal(new[] { midHeader.BoxGroupId!, innerHeader.BoxGroupId! }, innerBody.BoxGroupChain);

        // Verify closing braces drop the inner rail immediately when exiting the inner loop (3 loop braces + function brace)
        var closingBraces = lines.Where(l => l.CodeText.Trim() == "}").ToList();
        Assert.Equal(4, closingBraces.Count);
        Assert.Equal(new[] { midHeader.BoxGroupId!, innerHeader.BoxGroupId! }, closingBraces[0].BoxGroupChain);
        Assert.Equal(new[] { midHeader.BoxGroupId! }, closingBraces[1].BoxGroupChain);
        Assert.Null(closingBraces[2].BoxGroupChain);
        Assert.Null(closingBraces[3].BoxGroupChain);
    }

    [Fact]
    public void SiblingInnerLoops_ReceiveDistinctBoxGroupIdsAndSingleElementChains()
    {
        var fn = new AlgorithmFunction(
            "siblingLoops",
            new[] { "n" },
            new AlgorithmStatement[]
            {
                new ForStatement("i", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                {
                    new ForStatement("j", "1", "<=", "i", "++", "1", new AlgorithmStatement[]
                    {
                        new UnaryStatement("a", "++", "s_a")
                    }, "loop_j"),
                    new ForStatement("k", "1", "<=", "n", "++", "1", new AlgorithmStatement[]
                    {
                        new UnaryStatement("b", "++", "s_b")
                    }, "loop_k")
                }, "loop_i")
            });

        var result = IterativeAnalyzer.Analyze(fn);
        var lines = result.WorstCase.AnnotatedLines;

        Assert.Equal(2, result.WorstCase.InnerBoxes.Count);
        var jHeader = lines.Single(l => l.CodeText.StartsWith("for (j ", StringComparison.Ordinal));
        var kHeader = lines.Single(l => l.CodeText.StartsWith("for (k ", StringComparison.Ordinal));

        Assert.NotNull(jHeader.BoxGroupId);
        Assert.NotNull(kHeader.BoxGroupId);
        Assert.NotEqual(jHeader.BoxGroupId, kHeader.BoxGroupId);
        Assert.Equal(new[] { jHeader.BoxGroupId! }, jHeader.BoxGroupChain);
        Assert.Equal(new[] { kHeader.BoxGroupId! }, kHeader.BoxGroupChain);
    }

    [Fact]
    public void BlocklyInteropJsonContract_DeserializesAndPreservesBlockIds()
    {
        // Exact JSON shape emitted by blocklyInterop.js extractAst() (including out-of-order discriminator test)
        const string json = """
        {
          "name": "findSum",
          "parameters": ["array[]", "n"],
          "body": [
            { "id": "blk_1", "kind": "declare", "names": ["i", "sum"] },
            { "kind": "assign", "id": "blk_2", "target": "sum", "op": "=", "value": { "kind": "literal", "value": "0" } },
            {
              "kind": "for",
              "id": "blk_3",
              "iterator": "i",
              "startExpr": "0",
              "comparator": "<",
              "boundExpr": "n",
              "stepOp": "++",
              "stepValue": "1",
              "body": [
                {
                  "kind": "assign",
                  "id": "blk_4",
                  "target": "sum",
                  "op": "=",
                  "value": {
                    "op": "+",
                    "kind": "binary",
                    "left": { "kind": "variable", "name": "sum" },
                    "right": { "kind": "arrayAccess", "arrayName": "array", "indexExpr": "i" }
                  }
                }
              ]
            },
            { "kind": "return", "id": "blk_5", "value": { "kind": "variable", "name": "sum" } }
          ]
        }
        """;

        var fn = AlgorithmAstSerializer.Deserialize(json);
        var result = IterativeAnalyzer.Analyze(fn);

        Assert.Equal("5n + 3", result.WorstCase.Plain);
        Assert.Contains(result.WorstCase.AnnotatedLines, l => l.BlockId == "blk_3");
        Assert.Contains(result.WorstCase.AnnotatedLines, l => l.BlockId == "blk_4");

        // Round-trip serialization
        var roundTripped = AlgorithmAstSerializer.Deserialize(AlgorithmAstSerializer.Serialize(fn));
        Assert.Equal("5n + 3", IterativeAnalyzer.Analyze(roundTripped).WorstCase.Plain);
    }

    [Fact]
    public void WhileSwitchAndDescendingFor_AnalyzeCleanly()
    {
        var whileFn = new AlgorithmFunction(
            "whileScan",
            new[] { "n" },
            new AlgorithmStatement[]
            {
                new WhileStatement(
                    new BinaryExpr("<", new VariableExpr("i"), new VariableExpr("n")),
                    "n",
                    new AlgorithmStatement[] { new UnaryStatement("i", "++", "w_inc") },
                    "w_loop")
            });
        Assert.Equal("2n + 1", IterativeAnalyzer.Analyze(whileFn).WorstCase.Plain);

        var switchFn = new AlgorithmFunction(
            "switchSelect",
            new[] { "x" },
            new AlgorithmStatement[]
            {
                new SwitchStatement("x", new[]
                {
                    new SwitchCase("1", false, new AlgorithmStatement[] { new AssignStatement("y", "=", new LiteralExpr("10")) }),
                    new SwitchCase("2", false, new AlgorithmStatement[] { new AssignStatement("y", "=", new BinaryExpr("+", new VariableExpr("x"), new LiteralExpr("1"))) }),
                    new SwitchCase("default", true, new AlgorithmStatement[] { new AssignStatement("y", "=", new LiteralExpr("0")) })
                }, "sw_1")
            });
        var swResult = IterativeAnalyzer.Analyze(switchFn);
        Assert.Equal("4", swResult.WorstCase.Plain); // case 2: 2 comparisons + 2 ops = 4
        Assert.Equal("2", swResult.BestCase.Plain);  // case 1: 1 comparison + 1 op = 2

        var descFn = new AlgorithmFunction(
            "countDown",
            new[] { "n" },
            new AlgorithmStatement[]
            {
                new ForStatement("i", "n", ">=", "1", "--", "1", new AlgorithmStatement[]
                {
                    new UnaryStatement("c", "++")
                })
            });
        Assert.Equal("3n + 2", IterativeAnalyzer.Analyze(descFn).WorstCase.Plain);
    }
}
