using SuperCsStudent.Automata.FiniteAutomata;
using SuperCsStudent.Automata.Presets;
using SuperCsStudent.Automata.Synthesis;
using SuperCsStudent.Automata.Turing;

namespace SuperCsStudent.Automata.Tests;

public class TuringMachineTests
{
    [Fact]
    public void ShiftLeftMacro_CompilesToQuadrupleTableAndShiftsExactRawTapes()
    {
        var schema = AutomataPresets.Get("tm-shift-left").BuildTmSchema!();
        var compiled = TmSchemaCompiler.Compile(schema);

        Assert.NotEmpty(compiled.Machine.Transitions);
        Assert.NotEmpty(compiled.SchemaScene.Nodes);

        // Test 1: # # b a # # (head 4) -> # b a # # # (head 3)
        var trace1 = compiled.TraceOnRawTape("##ba##", initialHeadIndex: 4);
        Assert.True(trace1.HaltedNormally);
        Assert.Equal("#ba###", trace1.FinalTape);
        Assert.Equal(3, trace1.FinalHeadIndex);
        Assert.Equal("h", trace1.FinalState);
        Assert.True(trace1.MacroTrace.Count >= 2);
        Assert.True(trace1.MicroTrace.Count > trace1.MacroTrace.Count);

        // Verify direct TuringMachine.RunOnRawTape matches compiled trace identically
        var direct1 = compiled.Machine.RunOnRawTape("##ba##", initialHeadIndex: 4);
        Assert.Equal(trace1.FinalTape, direct1.FinalTape);
        Assert.Equal(trace1.FinalHeadIndex, direct1.FinalHeadIndex);

        // Test 2: # a a # b b # (head 6) -> # a a b b # # (head 5)
        var trace2 = compiled.TraceOnRawTape("#aa#bb#", initialHeadIndex: 6);
        Assert.True(trace2.HaltedNormally);
        Assert.Equal("#aabb##", trace2.FinalTape);
        Assert.Equal(5, trace2.FinalHeadIndex);
    }

    [Fact]
    public void ShiftRightAndCopyMacros_OperateCorrectlyOnSemiInfiniteTape()
    {
        var srSchema = LanguageSpecCompiler.BuildShiftRightSchema();
        var srCompiled = TmSchemaCompiler.Compile(srSchema);
        var srRes = srCompiled.Trace("ab");
        Assert.True(srRes.HaltedNormally);
        Assert.Equal("##ab#", srRes.TrimmedTape);
        Assert.Equal(4, srRes.FinalHeadIndex);

        var copySchema = new TmSchemaBuilder("Copy Macro").Alphabet('a', 'b').StartNode("c", "C").Build();
        var copyCompiled = TmSchemaCompiler.Compile(copySchema);
        var copyRes = copyCompiled.Trace("ab");
        Assert.True(copyRes.HaltedNormally);
        Assert.Equal("#ab#ab#", copyRes.TrimmedTape);
        Assert.Equal(6, copyRes.FinalHeadIndex);
    }

    [Fact]
    public void TmSchema_AStarBStarAStar_DecidesAcceptedAndRejectedInputs()
    {
        var schema = AutomataPresets.Get("tm-abstar").BuildTmSchema!();
        var compiled = TmSchemaCompiler.Compile(schema);

        foreach (var good in new[] { "abba", "aba", "e", "aaa", "bbb", "ab" })
        {
            var res = compiled.Trace(good);
            Assert.True(res.HaltedNormally);
            Assert.Equal("#y#", res.TrimmedTape);
            Assert.Equal(true, res.AcceptedDecision);
        }

        foreach (var bad in new[] { "babab", "baba", "abab" })
        {
            var res = compiled.Trace(bad);
            Assert.True(res.HaltedNormally);
            Assert.Equal("#n#", res.TrimmedTape);
            Assert.Equal(false, res.AcceptedDecision);
        }
    }

    [Fact]
    public void TmSchema_AnBn_DecidesCorrectlyWithMacroAndMicroTraces()
    {
        var schema = AutomataPresets.Get("tm-anbn").BuildTmSchema!();
        var compiled = TmSchemaCompiler.Compile(schema);

        foreach (var good in new[] { "aabb", "e", "ab", "aaabbb" })
        {
            var res = compiled.Trace(good);
            Assert.True(res.HaltedNormally, $"Expected halt on '{good}'");
            Assert.Equal("#y#", res.TrimmedTape);
            Assert.Equal(true, res.AcceptedDecision);
            Assert.NotEmpty(res.MacroTrace);
            Assert.NotEmpty(res.MicroTrace);
            Assert.Contains("[", res.MicroTrace[0].ConfigString);
        }

        foreach (var bad in new[] { "aab", "abb", "ba", "bab", "a", "b", "aba" })
        {
            var res = compiled.Trace(bad);
            Assert.True(res.HaltedNormally, $"Expected halt on '{bad}'");
            Assert.Equal("#n#", res.TrimmedTape);
            Assert.Equal(false, res.AcceptedDecision);
        }
    }

    [Fact]
    public void TmSchema_Palindrome_EnsuresRBlankPrecedesSLAndDecidesAllCases()
    {
        var schema = AutomataPresets.Get("tm-palindrome").BuildTmSchema!();

        // Verify R_# precedes S_L inside peeling nodes
        Assert.Contains(schema.Nodes, n => n.Label.Contains("R_# S_L", StringComparison.Ordinal));

        var compiled = TmSchemaCompiler.Compile(schema);

        foreach (var good in new[] { "e", "a", "b", "aa", "aba", "abba", "babab" })
        {
            var res = compiled.Trace(good);
            Assert.True(res.HaltedNormally);
            Assert.Equal("#y#", res.TrimmedTape);
            Assert.Equal(true, res.AcceptedDecision);
        }

        foreach (var bad in new[] { "abaa", "ab", "ba", "aabb", "abb" })
        {
            var res = compiled.Trace(bad);
            Assert.True(res.HaltedNormally);
            Assert.Equal("#n#", res.TrimmedTape);
            Assert.Equal(false, res.AcceptedDecision);
        }
    }

    [Fact]
    public void TmSchema_InvertAB_TransformsTapeInPlace()
    {
        var schema = AutomataPresets.Get("tm-invert").BuildTmSchema!();
        var compiled = TmSchemaCompiler.Compile(schema);

        var res1 = compiled.Trace("aab");
        Assert.True(res1.HaltedNormally);
        Assert.Equal("#bba#", res1.TrimmedTape);
        Assert.Equal("bba", res1.OutputContent);

        var resEmpty = compiled.Trace("e");
        Assert.True(resEmpty.HaltedNormally);
        Assert.Equal("##", resEmpty.TrimmedTape);
        Assert.Equal(string.Empty, resEmpty.OutputContent);
    }

    [Fact]
    public void TmSchema_StartsWithA_And_ContainsAba_DecideCorrectly()
    {
        var startsA = TmSchemaCompiler.Compile(AutomataPresets.Get("tm-starts-a").BuildTmSchema!());
        Assert.Equal("#y#", startsA.Trace("abb").TrimmedTape);
        Assert.Equal("#n#", startsA.Trace("bba").TrimmedTape);
        Assert.Equal("#n#", startsA.Trace("e").TrimmedTape);

        var containsAbaDfa = RegexCompiler.CompileToMinimalDfa("(a|b)*aba(a|b)*", new[] { 'a', 'b' });
        var containsAbaTm = TmSchemaCompiler.Compile(LanguageSpecCompiler.DfaToDecidableTmSchema(containsAbaDfa));
        Assert.Equal("#y#", containsAbaTm.Trace("bbabaa").TrimmedTape);
        Assert.Equal("#y#", containsAbaTm.Trace("aba").TrimmedTape);
        Assert.Equal("#n#", containsAbaTm.Trace("bbb").TrimmedTape);
        Assert.Equal("#n#", containsAbaTm.Trace("e").TrimmedTape);
    }

    [Fact]
    public void TmSchema_AnBnCn_DecidesContextSensitiveTriples()
    {
        var compiled = TmSchemaCompiler.Compile(AutomataPresets.Get("tm-anbncn").BuildTmSchema!());

        foreach (var good in new[] { "aabbcc", "abc", "e" })
        {
            var res = compiled.Trace(good);
            Assert.True(res.HaltedNormally);
            Assert.Equal("#y#", res.TrimmedTape);
            Assert.Equal(true, res.AcceptedDecision);
        }

        foreach (var bad in new[] { "aabbc", "abbcc", "abcc", "a", "b", "c", "acb", "ababcc" })
        {
            var res = compiled.Trace(bad);
            Assert.True(res.HaltedNormally);
            Assert.Equal("#n#", res.TrimmedTape);
            Assert.Equal(false, res.AcceptedDecision);
        }
    }

    [Fact]
    public void TmSchemaParser_RoundTripsDslTextAndPreservesBehavior()
    {
        var original = AutomataPresets.Get("tm-palindrome").BuildTmSchema!();
        var dsl = original.ToDslText();
        var reparsed = TmSchemaParser.Parse(dsl, "Parsed Palindrome");
        var compiled = TmSchemaCompiler.Compile(reparsed);

        Assert.Equal("#y#", compiled.Trace("abba").TrimmedTape);
        Assert.Equal("#n#", compiled.Trace("abaa").TrimmedTape);
    }

    [Fact]
    public void TuringMachine_HaltsOnSteppingLeftOffCellZero()
    {
        var leftOnly = TmSchemaCompiler.Compile(
            new TmSchemaBuilder("Crash Left").Alphabet('a').StartNode("go_l", "L").Build());
        var res = leftOnly.Machine.Run("a", headStartOverride: HeadStartMode.LeftBlank);

        Assert.False(res.HaltedNormally);
        Assert.Equal(TmHaltReason.SteppedOffLeft, res.HaltReason);
    }
}
