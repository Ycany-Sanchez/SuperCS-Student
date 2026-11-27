using SuperCsStudent.Automata.Grammars;

namespace SuperCsStudent.Automata.Tests;

public class GrammarsTests
{
    [Fact]
    public void Cfg_ParsesQuadrupleAndIdentifiesRegularVsNonRegular()
    {
        var anbn = Cfg.ParseFromText("S -> aSb | e");
        Assert.Equal("S", anbn.StartSymbol);
        Assert.Contains("a", anbn.Terminals);
        Assert.Contains("b", anbn.Terminals);
        Assert.Contains("S", anbn.NonTerminals);
        Assert.True(anbn.Terminals.IsSubsetOf(anbn.Variables));
        Assert.False(anbn.IsRegularGrammar());

        var reg = Cfg.ParseFromText("S -> aA | e\nA -> bS");
        Assert.True(reg.IsRegularGrammar());
    }

    [Fact]
    public void CfgParser_ParsesAnBnWithExactDerivationAndParseTree()
    {
        var cfg = Cfg.ParseFromText("S -> aSb | e");

        var acc = CfgParser.Parse(cfg, "aabb");
        Assert.True(acc.Accepted);
        Assert.NotNull(acc.Root);
        Assert.Equal(new[] { "S", "aSb", "aaSbb", "aabb" }, acc.DerivationSteps);
        Assert.NotEmpty(acc.ParseTreeScene.Nodes);
        Assert.NotEmpty(acc.ParseTreeScene.Edges);

        var empty = CfgParser.Parse(cfg, "e");
        Assert.True(empty.Accepted);
        Assert.Equal(new[] { "S", "e" }, empty.DerivationSteps);

        var rej = CfgParser.Parse(cfg, "aab");
        Assert.False(rej.Accepted);
        Assert.Null(rej.Root);
    }

    [Fact]
    public void CfgParser_HandlesPalindromesAndAmbiguousBalancedParens()
    {
        var pal = Cfg.ParseFromText("S -> aSa | bSb | a | b | e");
        foreach (var good in new[] { "e", "a", "b", "aba", "abba", "aabaa" })
        {
            var res = CfgParser.Parse(pal, good);
            Assert.True(res.Accepted, $"Expected palindrome '{good}' to be accepted.");
            Assert.Equal("S", res.DerivationSteps[0]);
            Assert.Equal(good, res.DerivationSteps[^1]);
        }
        foreach (var bad in new[] { "ab", "abaa", "aab" })
        {
            Assert.False(CfgParser.Parse(pal, bad).Accepted, $"Expected '{bad}' to be rejected.");
        }

        var parens = Cfg.ParseFromText("S -> (S) | SS | e");
        foreach (var good in new[] { "e", "()", "(())", "()()", "(()())" })
        {
            var res = CfgParser.Parse(parens, good);
            Assert.True(res.Accepted, $"Expected balanced parens '{good}' to be accepted.");
            Assert.Equal("S", res.DerivationSteps[0]);
            Assert.Equal(good, res.DerivationSteps[^1]);
        }
        foreach (var bad in new[] { "(", ")", ")(", "(()" })
        {
            Assert.False(CfgParser.Parse(parens, bad).Accepted, $"Expected '{bad}' to be rejected.");
        }
    }

    [Fact]
    public void CfgParser_HandlesLeftRecursiveArithmeticGrammar()
    {
        var arith = Cfg.ParseFromText("E -> E+T | T\nT -> T*F | F\nF -> (E) | x | y");

        var res = CfgParser.Parse(arith, "(x+y)+x");
        Assert.True(res.Accepted);
        Assert.NotNull(res.Root);
        Assert.Equal("E", res.DerivationSteps[0]);
        Assert.Equal("(x+y)+x", res.DerivationSteps[^1]);
        Assert.True(res.ParseTreeScene.Nodes.Count >= 12);

        Assert.True(CfgParser.Parse(arith, "x+y*x").Accepted);
        Assert.True(CfgParser.Parse(arith, "((x*y)+(y))").Accepted);
        Assert.False(CfgParser.Parse(arith, "x+").Accepted);
        Assert.False(CfgParser.Parse(arith, "(x+y").Accepted);
    }

    [Fact]
    public void CfgClosure_UnionConcatenationAndKleeneStarWorkCorrectly()
    {
        var g1 = Cfg.ParseFromText("S -> aSb | ab");
        var g2 = Cfg.ParseFromText("S -> cSd | cd");

        var union = CfgClosure.Union(g1, g2);
        Assert.True(CfgParser.Parse(union, "aabb").Accepted);
        Assert.True(CfgParser.Parse(union, "ccdd").Accepted);
        Assert.False(CfgParser.Parse(union, "abcd").Accepted);

        var concat = CfgClosure.Concatenation(g1, g2);
        Assert.True(CfgParser.Parse(concat, "aabbcd").Accepted);
        Assert.False(CfgParser.Parse(concat, "aabb").Accepted);

        var star = CfgClosure.KleeneStar(g1);
        Assert.True(CfgParser.Parse(star, "e").Accepted);
        Assert.True(CfgParser.Parse(star, "ab").Accepted);
        Assert.True(CfgParser.Parse(star, "abaabbab").Accepted);
        Assert.False(CfgParser.Parse(star, "aba").Accepted);
    }

    [Fact]
    public void CfgParser_EmitsStructuredFifoQueueStepsMatchingLeftmostDerivation()
    {
        var g = Cfg.ParseFromText("S -> aSb | aA\nA -> aA | e");

        // Multi-step derivation on "aab"
        var res = CfgParser.Parse(g, "aab");
        Assert.True(res.Accepted);
        Assert.NotNull(res.QueueSteps);
        Assert.Equal(res.DerivationSteps.Count, res.QueueSteps!.Count);

        // Step 0: empty output, queue = [S (HEAD)]
        Assert.Empty(res.QueueSteps[0].ResolvedOutput);
        Assert.Single(res.QueueSteps[0].QueueSymbols);
        Assert.Equal("S", res.QueueSteps[0].QueueSymbols[0].Symbol);
        Assert.True(res.QueueSteps[0].QueueSymbols[0].IsExpandHead);

        // Final step: empty queue, full matched output ["a", "a", "b"], IsComplete == true
        var last = res.QueueSteps[^1];
        Assert.True(last.IsComplete);
        Assert.Empty(last.QueueSymbols);
        Assert.Equal(new[] { "a", "a", "b" }, last.ResolvedOutput);

        // Longer string "aaaabbb"
        var longRes = CfgParser.Parse(g, "aaaabbb");
        Assert.True(longRes.Accepted);
        Assert.Equal(new[] { "a", "a", "a", "a", "b", "b", "b" }, longRes.QueueSteps![^1].ResolvedOutput);
        Assert.Empty(longRes.QueueSteps[^1].QueueSymbols);

        // Left-recursive arithmetic grammar
        var arith = Cfg.ParseFromText("E -> E+T | T\nT -> T*F | F\nF -> (E) | x | y");
        var arithRes = CfgParser.Parse(arith, "(x+y)+x");
        Assert.True(arithRes.Accepted);
        Assert.Equal(arithRes.DerivationSteps.Count, arithRes.QueueSteps!.Count);
        Assert.True(arithRes.QueueSteps[^1].IsComplete);
        Assert.Equal("(x+y)+x", string.Concat(arithRes.QueueSteps[^1].ResolvedOutput));

        // Epsilon derivation "e"
        var epsG = Cfg.ParseFromText("S -> aSb | e");
        var epsRes = CfgParser.Parse(epsG, "e");
        Assert.True(epsRes.Accepted);
        Assert.Equal(2, epsRes.QueueSteps!.Count);
        Assert.True(epsRes.QueueSteps[^1].IsComplete);
        Assert.Empty(epsRes.QueueSteps[^1].ResolvedOutput);
        Assert.Empty(epsRes.QueueSteps[^1].QueueSymbols);

        // Rejected input has null/empty QueueSteps
        var rej = CfgParser.Parse(arith, "x+");
        Assert.False(rej.Accepted);
        Assert.True(rej.QueueSteps is null or { Count: 0 });
    }
}
