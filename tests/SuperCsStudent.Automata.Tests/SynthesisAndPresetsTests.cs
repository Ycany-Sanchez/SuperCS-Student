using SuperCsStudent.Automata.FiniteAutomata;
using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Automata.Pda;
using SuperCsStudent.Automata.Presets;
using SuperCsStudent.Automata.Synthesis;
using SuperCsStudent.Automata.Turing;

namespace SuperCsStudent.Automata.Tests;

public class SynthesisAndPresetsTests
{
    [Fact]
    public void LanguageSpecCompiler_BooleanRegularConstraintsCompileToExactDfaAndTm()
    {
        var res1 = LanguageSpecCompiler.Compile("starts with bb AND contains bab AND ends with aa");
        Assert.Equal(LanguageSpecMode.Constraint, res1.ResolvedMode);
        Assert.Equal(LanguageClass.Regular, res1.LanguageClass);
        Assert.NotNull(res1.MinimalDfa);

        Assert.True(res1.MinimalDfa!.Accepts("bbabaa"));
        Assert.True(res1.MinimalDfa.Accepts("bbbabaa"));
        Assert.False(res1.MinimalDfa.Accepts("bbaa"));
        Assert.False(res1.MinimalDfa.Accepts("bbaba"));
        Assert.False(res1.MinimalDfa.Accepts("abaa"));

        // Verify the synthesized decidable TM agrees with the DFA
        Assert.Equal(true, res1.CompiledTm.Trace("bbabaa").AcceptedDecision);
        Assert.Equal(false, res1.CompiledTm.Trace("bbaa").AcceptedDecision);

        var res2 = LanguageSpecCompiler.Compile("does not contain aba");
        Assert.True(res2.MinimalDfa!.Accepts("e"));
        Assert.True(res2.MinimalDfa.Accepts("aabb"));
        Assert.False(res2.MinimalDfa.Accepts("aba"));
        Assert.False(res2.MinimalDfa.Accepts("bbabaa"));
        Assert.Equal(true, res2.CompiledTm.Trace("aabb").AcceptedDecision);
        Assert.Equal(false, res2.CompiledTm.Trace("aba").AcceptedDecision);

        var res3 = LanguageSpecCompiler.Compile("(even a AND odd b) OR length divisible by 3");
        Assert.True(res3.MinimalDfa!.Accepts("b"));      // 0 a (even), 1 b (odd)
        Assert.True(res3.MinimalDfa.Accepts("aaa"));     // length 3
        Assert.False(res3.MinimalDfa.Accepts("a"));      // 1 a, 0 b, length 1

        var res4 = LanguageSpecCompiler.Compile("at least 2 a AND exactly 1 b AND does not end with b");
        Assert.True(res4.MinimalDfa!.Accepts("aba"));
        Assert.True(res4.MinimalDfa.Accepts("baa"));
        Assert.False(res4.MinimalDfa.Accepts("aab"));
        Assert.False(res4.MinimalDfa.Accepts("abba"));
    }

    [Fact]
    public void LanguageSpecCompiler_ParametricNonRegularLanguagesAndTransducersWork()
    {
        // a^2n b^n
        var a2nbn = LanguageSpecCompiler.Compile("a^2n b^n");
        Assert.Equal(true, a2nbn.CompiledTm.Trace("e").AcceptedDecision);
        Assert.Equal(true, a2nbn.CompiledTm.Trace("aab").AcceptedDecision);
        Assert.Equal(true, a2nbn.CompiledTm.Trace("aaaabb").AcceptedDecision);
        Assert.Equal(false, a2nbn.CompiledTm.Trace("ab").AcceptedDecision);
        Assert.Equal(false, a2nbn.CompiledTm.Trace("aabb").AcceptedDecision);
        Assert.True(a2nbn.Pda!.Accepts("aaaabb"));
        Assert.False(a2nbn.Pda.Accepts("aabb"));

        // a^2n b^m c^n
        var a2nbmcn = LanguageSpecCompiler.Compile("a^2n b^m c^n");
        Assert.Equal(true, a2nbmcn.CompiledTm.Trace("aabbbc").AcceptedDecision);
        Assert.Equal(true, a2nbmcn.CompiledTm.Trace("bbb").AcceptedDecision);
        Assert.Equal(false, a2nbmcn.CompiledTm.Trace("abbc").AcceptedDecision);

        // a^n b^n c^m
        var anbncm = LanguageSpecCompiler.Compile("a^n b^n c^m");
        Assert.Equal(true, anbncm.CompiledTm.Trace("aabbccc").AcceptedDecision);
        Assert.Equal(true, anbncm.CompiledTm.Trace("ccc").AcceptedDecision);
        Assert.Equal(false, anbncm.CompiledTm.Trace("aabccc").AcceptedDecision);

        // even palindromes
        var evenPal = LanguageSpecCompiler.Compile("even palindromes");
        Assert.Equal(true, evenPal.CompiledTm.Trace("abba").AcceptedDecision);
        Assert.Equal(true, evenPal.CompiledTm.Trace("e").AcceptedDecision);
        Assert.Equal(false, evenPal.CompiledTm.Trace("aba").AcceptedDecision);
        Assert.Equal(false, evenPal.CompiledTm.Trace("a").AcceptedDecision);

        // balanced ()
        var parens = LanguageSpecCompiler.Compile("balanced ()");
        Assert.Equal(true, parens.CompiledTm.Trace("(()())").AcceptedDecision);
        Assert.Equal(false, parens.CompiledTm.Trace("(()").AcceptedDecision);

        // #a > #b and #a = #b
        var moreA = LanguageSpecCompiler.Compile("#a > #b");
        Assert.Equal(true, moreA.CompiledTm.Trace("aba").AcceptedDecision);
        Assert.Equal(true, moreA.CompiledTm.Trace("baa").AcceptedDecision);
        Assert.Equal(false, moreA.CompiledTm.Trace("ab").AcceptedDecision);
        Assert.Equal(false, moreA.CompiledTm.Trace("e").AcceptedDecision);

        var eqAB = LanguageSpecCompiler.Compile("#a = #b");
        Assert.Equal(true, eqAB.CompiledTm.Trace("baba").AcceptedDecision);
        Assert.Equal(true, eqAB.CompiledTm.Trace("e").AcceptedDecision);
        Assert.Equal(false, eqAB.CompiledTm.Trace("aab").AcceptedDecision);

        // m >= n and m <= n
        var mGeqN = LanguageSpecCompiler.Compile("m >= n");
        Assert.Equal(true, mGeqN.CompiledTm.Trace("aab").AcceptedDecision);
        Assert.Equal(true, mGeqN.CompiledTm.Trace("aabb").AcceptedDecision);
        Assert.Equal(false, mGeqN.CompiledTm.Trace("abb").AcceptedDecision);
        Assert.Equal(false, mGeqN.CompiledTm.Trace("aba").AcceptedDecision);

        var mLeqN = LanguageSpecCompiler.Compile("m <= n");
        Assert.Equal(true, mLeqN.CompiledTm.Trace("abb").AcceptedDecision);
        Assert.Equal(true, mLeqN.CompiledTm.Trace("aabb").AcceptedDecision);
        Assert.Equal(false, mLeqN.CompiledTm.Trace("aab").AcceptedDecision);

        // Binary AND & XOR transducers
        var andTm = LanguageSpecCompiler.Compile("AND");
        Assert.Equal("1", andTm.CompiledTm.Trace("11").OutputContent);
        Assert.Equal("0", andTm.CompiledTm.Trace("10").OutputContent);
        Assert.Equal("0", andTm.CompiledTm.Trace("01").OutputContent);
        Assert.Equal("0", andTm.CompiledTm.Trace("00").OutputContent);

        var xorTm = LanguageSpecCompiler.Compile("XOR");
        Assert.Equal("0", xorTm.CompiledTm.Trace("00").OutputContent);
        Assert.Equal("1", xorTm.CompiledTm.Trace("01").OutputContent);
        Assert.Equal("1", xorTm.CompiledTm.Trace("10").OutputContent);
        Assert.Equal("0", xorTm.CompiledTm.Trace("11").OutputContent);

        // snake_case to camelCase transducer
        var snake = LanguageSpecCompiler.Compile("snake_case to camelCase");
        Assert.Equal("snakeCase", snake.CompiledTm.Trace("snake_case").OutputContent);
        Assert.Equal("helloWorldTest", snake.CompiledTm.Trace("hello_world_test").OutputContent);
        Assert.Equal("camelReady", snake.CompiledTm.Trace("camelReady").OutputContent);
    }

    [Fact]
    public void AutomataPresets_ContainsAllRequiredKeysAndAllPresetsBuildAndExecute()
    {
        var requiredKeys = new[]
        {
            "tm-palindrome",
            "pda-anbn",
            "dfa-contains-aba",
            "cfg-arith",
            "tm-anbn",
            "tm-anbncn",
            "tm-a2nbn",
            "tm-abstar",
            "tm-invert",
            "tm-shift-left",
            "tm-starts-a",
            "pda-palindrome",
            "pda-parens",
            "dfa-even-b",
            "dfa-bb-bab-aa"
        };

        foreach (var key in requiredKeys)
        {
            Assert.True(AutomataPresets.ByKey.ContainsKey(key), $"Missing required preset key '{key}'.");
            var preset = AutomataPresets.Get(key);
            Assert.False(string.IsNullOrWhiteSpace(preset.Title));

            switch (preset.Kind)
            {
                case AutomataPresetKind.FiniteAutomaton:
                {
                    var dfa = preset.BuildDfa!();
                    var trace = FaTracer.Trace(dfa, preset.DefaultInput);
                    Assert.True(trace.IsSuccess, $"Preset '{key}' should accept its default input '{preset.DefaultInput}'.");
                    break;
                }
                case AutomataPresetKind.ContextFreeGrammar:
                {
                    var cfg = preset.BuildCfg!();
                    var parsed = CfgParser.Parse(cfg, preset.DefaultInput);
                    Assert.True(parsed.Accepted, $"Preset '{key}' should accept its default input '{preset.DefaultInput}'.");
                    break;
                }
                case AutomataPresetKind.PushdownAutomaton:
                {
                    var pda = preset.BuildPda!();
                    var trace = PdaTracer.Trace(pda, preset.DefaultInput);
                    Assert.True(trace.Accepted, $"Preset '{key}' should accept its default input '{preset.DefaultInput}'.");
                    break;
                }
                case AutomataPresetKind.TuringMachine:
                {
                    var schema = preset.BuildTmSchema!();
                    var compiled = TmSchemaCompiler.Compile(schema);
                    var res = compiled.Trace(preset.DefaultInput);
                    Assert.True(res.HaltedNormally, $"Preset '{key}' should halt normally on '{preset.DefaultInput}'.");
                    break;
                }
            }
        }
    }
}
