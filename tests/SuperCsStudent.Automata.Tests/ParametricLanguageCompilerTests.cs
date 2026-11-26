using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Automata.Pda;
using SuperCsStudent.Automata.Synthesis;

namespace SuperCsStudent.Automata.Tests;

public class ParametricLanguageCompilerTests
{
    private static void AssertTwoBlockExhaustive(
        string spec,
        char s1,
        char s2,
        Func<int, int, bool> oracle,
        LanguageClass expectedClass = LanguageClass.ContextFree,
        int maxCount = 5,
        bool checkPda = true)
    {
        var result = LanguageSpecCompiler.Compile(spec);
        Assert.Equal(expectedClass, result.LanguageClass);
        Assert.NotNull(result.SuggestedTests);
        Assert.NotEmpty(result.SuggestedTests!);

        if (expectedClass == LanguageClass.ContextFree)
        {
            Assert.NotNull(result.Grammar);
            Assert.NotNull(result.Pda);
        }
        else if (expectedClass == LanguageClass.Regular)
        {
            Assert.NotNull(result.MinimalDfa);
            Assert.NotNull(result.Grammar);
        }

        // Exhaustively verify every (x, y) in [0..maxCount]^2
        for (var x = 0; x <= maxCount; x++)
        {
            for (var y = 0; y <= maxCount; y++)
            {
                var word = new string(s1, x) + new string(s2, y);
                var expected = oracle(x, y);

                var tmRes = result.CompiledTm.Trace(word);
                Assert.True(tmRes.HaltedNormally, $"TM did not halt on '{word}' for '{spec}'.");
                Assert.Equal(expected, tmRes.AcceptedDecision);

                if (result.MinimalDfa is not null)
                {
                    Assert.Equal(expected, result.MinimalDfa.Accepts(word));
                }

                if (result.Grammar is not null)
                {
                    var parsed = CfgParser.Parse(result.Grammar, word);
                    Assert.Equal(expected, parsed.Accepted);
                }

                if (checkPda && result.Pda is not null && x + y <= 5)
                {
                    var pdaRes = PdaTracer.Trace(result.Pda, word);
                    Assert.Equal(expected, pdaRes.Accepted);
                }
            }
        }

        // Verify out-of-order strings are always rejected
        foreach (var bad in new[] { $"{s2}{s1}", $"{s1}{s2}{s1}", $"{s2}{s1}{s2}", $"{s1}{s1}{s2}{s1}" })
        {
            Assert.Equal(false, result.CompiledTm.Trace(bad).AcceptedDecision);
            if (result.MinimalDfa is not null) Assert.False(result.MinimalDfa.Accepts(bad));
            if (result.Grammar is not null) Assert.False(CfgParser.Parse(result.Grammar, bad).Accepted);
        }

        // Verify auto-generated suggested tests all match the compiled TM
        foreach (var tc in result.SuggestedTests!)
        {
            var actual = result.CompiledTm.Trace(tc.Input).AcceptedDecision;
            Assert.Equal(tc.ExpectedAccept, actual);
        }
    }

    private static void AssertThreeBlockExhaustive(
        string spec,
        char s1,
        char s2,
        char s3,
        Func<int, int, int, bool> oracle,
        LanguageClass expectedClass,
        int maxCount = 3)
    {
        var result = LanguageSpecCompiler.Compile(spec);
        Assert.Equal(expectedClass, result.LanguageClass);
        Assert.NotNull(result.SuggestedTests);
        Assert.NotEmpty(result.SuggestedTests!);

        if (expectedClass == LanguageClass.ContextSensitive)
        {
            Assert.Null(result.Grammar);
            Assert.Null(result.Pda);
        }
        else if (expectedClass == LanguageClass.ContextFree)
        {
            Assert.NotNull(result.Grammar);
            Assert.NotNull(result.Pda);
        }

        for (var i = 0; i <= maxCount; i++)
        {
            for (var j = 0; j <= maxCount; j++)
            {
                for (var k = 0; k <= maxCount; k++)
                {
                    // Skip ambiguous counts when outer symbol repeats and middle is empty (s1^i s2^0 s1^k = s1^(i+k))
                    if (s1 == s3 && j == 0) continue;

                    var word = new string(s1, i) + new string(s2, j) + new string(s3, k);
                    var expected = oracle(i, j, k);

                    var tmRes = result.CompiledTm.Trace(word);
                    Assert.True(tmRes.HaltedNormally, $"TM did not halt on '{word}' for '{spec}'.");
                    Assert.Equal(expected, tmRes.AcceptedDecision);

                    if (result.Grammar is not null)
                    {
                        var parsed = CfgParser.Parse(result.Grammar, word);
                        Assert.Equal(expected, parsed.Accepted);
                    }
                }
            }
        }

        // Out-of-order checks
        var badSamples = s1 == s3
            ? new[] { $"{s2}{s1}{s2}", $"{s1}{s2}{s1}{s2}" }
            : new[] { $"{s1}{s3}{s2}", $"{s2}{s1}{s3}", $"{s3}{s2}{s1}" };
        foreach (var bad in badSamples)
        {
            Assert.Equal(false, result.CompiledTm.Trace(bad).AcceptedDecision);
            if (result.Grammar is not null) Assert.False(CfgParser.Parse(result.Grammar, bad).Accepted);
        }

        foreach (var tc in result.SuggestedTests!)
        {
            Assert.Equal(tc.ExpectedAccept, result.CompiledTm.Trace(tc.Input).AcceptedDecision);
        }
    }

    [Fact]
    public void TwoBlock_CoreComparisonsAndSyntaxVariants_MatchOracleExhaustively()
    {
        // Primary user request: a^n b^m where n > m (plus set-builder, custom symbols, unicode, parens)
        AssertTwoBlockExhaustive("a^n b^m where n > m", 'a', 'b', (n, m) => n > m);
        AssertTwoBlockExhaustive("{ a^n b^m | n > m }", 'a', 'b', (n, m) => n > m);
        AssertTwoBlockExhaustive("L = { x^i y^j | i > j }", 'x', 'y', (i, j) => i > j);
        AssertTwoBlockExhaustive("0^n 1^m (n > m)", '0', '1', (n, m) => n > m);

        // All six operators
        AssertTwoBlockExhaustive("a^n b^m where n >= m", 'a', 'b', (n, m) => n >= m);
        AssertTwoBlockExhaustive("a^n b^m where n < m", 'a', 'b', (n, m) => n < m);
        AssertTwoBlockExhaustive("a^n b^m where n <= m", 'a', 'b', (n, m) => n <= m);
        AssertTwoBlockExhaustive("a^n b^m where n = m", 'a', 'b', (n, m) => n == m);
        AssertTwoBlockExhaustive("a^n b^m where n != m", 'a', 'b', (n, m) => n != m);
    }

    [Fact]
    public void TwoBlock_LowerBoundsChainedComparisonsRatiosAndInlineAffine_MatchOracleExhaustively()
    {
        AssertTwoBlockExhaustive("a^n b^m where n > m >= 1", 'a', 'b', (n, m) => n > m && m >= 1);
        AssertTwoBlockExhaustive("a^n b^m where n, m >= 1 and n <= m", 'a', 'b', (n, m) => n >= 1 && m >= 1 && n <= m);
        AssertTwoBlockExhaustive("a^n b^m where n = 2m + 1", 'a', 'b', (n, m) => n == 2 * m + 1);
        AssertTwoBlockExhaustive("a^n b^m where n >= 2m", 'a', 'b', (n, m) => n >= 2 * m);
        AssertTwoBlockExhaustive("a^n b^m where 2n > 3m", 'a', 'b', (n, m) => 2 * n > 3 * m);
        AssertTwoBlockExhaustive("a^(n+1) b^n", 'a', 'b', (n, m) => n == m + 1);
        AssertTwoBlockExhaustive("x^(2n) y^n", 'x', 'y', (n, m) => n == 2 * m);
        AssertTwoBlockExhaustive("a^(2n) b^(2n)", 'a', 'b', (n, m) => n == m && n % 2 == 0);
    }

    [Fact]
    public void TwoBlock_DisjunctionAndSandwichCone_MatchOracleExhaustively()
    {
        AssertTwoBlockExhaustive("a^n b^m where n = m or n = 2m", 'a', 'b', (n, m) => n == m || n == 2 * m);
        AssertTwoBlockExhaustive("a^n b^m where m < n < 2m", 'a', 'b', (n, m) => m < n && n < 2 * m, maxCount: 6, checkPda: false);
        AssertTwoBlockExhaustive("a^n b^m where m <= n <= 2m", 'a', 'b', (n, m) => m <= n && n <= 2 * m, maxCount: 6, checkPda: false);
    }

    [Fact]
    public void IndependentAndFiniteBoundedBlocks_ClassifyAsRegularWithDfaAndTm()
    {
        AssertTwoBlockExhaustive(
            "a^n b^m where n >= 2 and m <= 3", 'a', 'b',
            (n, m) => n >= 2 && m <= 3,
            expectedClass: LanguageClass.Regular,
            maxCount: 5,
            checkPda: false);

        AssertTwoBlockExhaustive(
            "x^n y^m where n <= 2 and n < m", 'x', 'y',
            (n, m) => n <= 2 && n < m,
            expectedClass: LanguageClass.Regular,
            maxCount: 5,
            checkPda: false);
    }

    [Fact]
    public void ThreeBlock_ContextFreeTwoVarAdditiveAndDisjunction_MatchOracleExhaustively()
    {
        // 2-of-3 constrained, 1 free
        AssertThreeBlockExhaustive("a^n b^m c^k where n > m", 'a', 'b', 'c', (i, j, k) => i > j, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m c^k where n = k", 'a', 'b', 'c', (i, j, k) => i == k, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m a^k where n > k", 'a', 'b', 'a', (i, j, k) => i > k, LanguageClass.ContextFree);

        // 3-variable additive
        AssertThreeBlockExhaustive("a^n b^m c^k where n + m = k", 'a', 'b', 'c', (i, j, k) => i + j == k, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m c^(n+m)", 'a', 'b', 'c', (i, j, k) => i + j == k, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m c^k where n + m > k", 'a', 'b', 'c', (i, j, k) => i + j > k, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m c^k where n + m < k", 'a', 'b', 'c', (i, j, k) => i + j < k, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m c^k where n = m + k", 'a', 'b', 'c', (i, j, k) => i == j + k, LanguageClass.ContextFree);
        AssertThreeBlockExhaustive("a^n b^m c^k where m = n + k", 'a', 'b', 'c', (i, j, k) => j == i + k, LanguageClass.ContextFree);

        // Disjunction (OR -> ContextFree)
        AssertThreeBlockExhaustive("a^i b^j c^k where i = j or j = k", 'a', 'b', 'c', (i, j, k) => i == j || j == k, LanguageClass.ContextFree);
    }

    [Fact]
    public void ThreeBlock_CoupledConjunctions_ClassifyAsContextSensitiveWithExactTm()
    {
        AssertThreeBlockExhaustive("a^n b^m c^k where n > m > k", 'a', 'b', 'c', (i, j, k) => i > j && j > k, LanguageClass.ContextSensitive);
        AssertThreeBlockExhaustive("x^i y^j z^k where i >= j >= k", 'x', 'y', 'z', (i, j, k) => i >= j && j >= k, LanguageClass.ContextSensitive);
        AssertThreeBlockExhaustive("a^n b^m c^k where n = 2m and m = k", 'a', 'b', 'c', (i, j, k) => i == 2 * j && j == k, LanguageClass.ContextSensitive);
    }

    [Fact]
    public void UnorderedSymbolCount_SupportsAllComparisonsWeightsAndNeutralSymbols()
    {
        var cases = new (string Spec, char[] Alpha, Func<string, bool> Oracle, bool HasCfg)[]
        {
            ("#x >= #y", ['x', 'y'], w => w.Count(c => c == 'x') >= w.Count(c => c == 'y'), true),
            ("#0 < #1", ['0', '1'], w => w.Count(c => c == '0') < w.Count(c => c == '1'), true),
            ("#a != #b", ['a', 'b'], w => w.Count(c => c == 'a') != w.Count(c => c == 'b'), true),
            ("{ w in {x,y,z}* | #x(w) > #y(w) }", ['x', 'y', 'z'], w => w.Count(c => c == 'x') > w.Count(c => c == 'y'), true),
            ("#a = 2*#b", ['a', 'b'], w => w.Count(c => c == 'a') == 2 * w.Count(c => c == 'b'), false)
        };

        foreach (var (spec, alpha, oracle, hasCfg) in cases)
        {
            var res = LanguageSpecCompiler.Compile(spec);
            Assert.Equal(LanguageClass.ContextFree, res.LanguageClass);
            Assert.NotNull(res.Pda);
            if (hasCfg) Assert.NotNull(res.Grammar);

            // Exhaustively test all strings over alpha up to length 4 (or 3 for 3-char alphabet)
            var maxLen = alpha.Length <= 2 ? 4 : 3;
            var queue = new Queue<string>();
            queue.Enqueue("");
            while (queue.Count > 0)
            {
                var w = queue.Dequeue();
                var expected = oracle(w);
                Assert.Equal(expected, res.CompiledTm.Trace(w).AcceptedDecision);
                if (res.Grammar is not null)
                {
                    Assert.Equal(expected, CfgParser.Parse(res.Grammar, w).Accepted);
                }
                if (w.Length <= 3)
                {
                    Assert.Equal(expected, PdaTracer.Trace(res.Pda!, w).Accepted);
                }

                if (w.Length < maxLen)
                {
                    foreach (var c in alpha) queue.Enqueue(w + c);
                }
            }
        }
    }

    [Fact]
    public void InvalidOrUnsatisfiableParametricSpecs_ThrowDescriptiveFormatException()
    {
        Assert.Throws<FormatException>(() => LanguageSpecCompiler.Compile("a^n b^m where 2n - 2m = 1"));
        Assert.Throws<FormatException>(() => LanguageSpecCompiler.Compile("a^n b^m where n = 50m"));
        Assert.Throws<FormatException>(() => LanguageSpecCompiler.Compile("a^n b^m where n >= 5 and n <= 2"));
        Assert.Throws<FormatException>(() => LanguageSpecCompiler.Compile("a^n b^m where n > z"));
    }
}
