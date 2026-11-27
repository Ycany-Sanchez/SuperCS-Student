using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Automata.Pda;
using SuperCsStudent.Automata.Presets;

namespace SuperCsStudent.Automata.Tests;

public class PdaTests
{
    [Fact]
    public void Pda_AnBn_ProducesExactFiveColumnTracesForAcceptEmptyAndReject()
    {
        var pda = AutomataPresets.Get("pda-anbn").BuildPda!();

        // Accept "aabb"
        var acc = PdaTracer.Trace(pda, "aabb");
        Assert.True(acc.Accepted);
        Assert.True(acc.Timeline.IsSuccess);
        Assert.Equal("s", acc.Rows[0].State);
        Assert.Equal("aabb", acc.Rows[0].RemainingInput);
        Assert.Equal("e", acc.Rows[0].Stack);

        var last = acc.Rows[^1];
        Assert.Equal("f", last.State);
        Assert.Equal("e", last.RemainingInput);
        Assert.Equal("e", last.Stack);
        Assert.Empty(acc.Timeline.Steps[^1].State.StackFrames);
        Assert.NotEmpty(acc.Timeline.Steps[^1].State.Scene.Nodes);

        // Verify intermediate live stack frames contain ["a", "a"] after reading "aa"
        Assert.Contains(acc.Timeline.Steps, s => s.State.Stack == "aa" && s.State.StackFrames.SequenceEqual(new[] { "a", "a" }));

        // Accept "e"
        var empty = PdaTracer.Trace(pda, "e");
        Assert.True(empty.Accepted);
        Assert.Equal("f", empty.Rows[^1].State);
        Assert.Equal("e", empty.Rows[^1].RemainingInput);
        Assert.Equal("e", empty.Rows[^1].Stack);

        // Reject "abb"
        var rej = PdaTracer.Trace(pda, "abb");
        Assert.False(rej.Accepted);
        Assert.False(rej.Timeline.IsSuccess);
        Assert.Contains("Rejected", rej.Rows[^1].Remarks);
    }

    [Fact]
    public void Pda_Palindromes_AcceptsOddAndEvenAndRejectsNonPalindromes()
    {
        var pda = AutomataPresets.Get("pda-palindrome").BuildPda!();

        foreach (var good in new[] { "e", "a", "b", "aa", "aba", "abba", "babab" })
        {
            var trace = PdaTracer.Trace(pda, good);
            Assert.True(trace.Accepted, $"Expected palindrome '{good}' to be accepted by PDA.");
            Assert.Equal("e", trace.Rows[^1].RemainingInput);
            Assert.Equal("e", trace.Rows[^1].Stack);
        }

        foreach (var bad in new[] { "ab", "ba", "abaa", "aabb" })
        {
            Assert.False(PdaTracer.Trace(pda, bad).Accepted, $"Expected '{bad}' to be rejected by palindrome PDA.");
        }
    }

    [Fact]
    public void Pda_BalancedParentheses_ParsesParenSymbolsInTransitionTextAndTracesCorrectly()
    {
        var pda = AutomataPresets.Get("pda-parens").BuildPda!();

        foreach (var good in new[] { "e", "()", "(())", "()()", "(()())" })
        {
            Assert.True(PdaTracer.Trace(pda, good).Accepted, $"Expected '{good}' to be accepted.");
        }

        foreach (var bad in new[] { "(", ")", ")(", "(()", "())" })
        {
            Assert.False(PdaTracer.Trace(pda, bad).Accepted, $"Expected '{bad}' to be rejected.");
        }
    }

    [Fact]
    public void CfgToPdaConverter_ConstructsThreeRulePdaForAnBn()
    {
        var cfg = Cfg.ParseFromText("S -> aSb | e");
        var pda = CfgToPdaConverter.Convert(cfg);

        // Verify the exact 3-rule structure: ((s,e,e),(f,S)), ((f,e,A),(f,x)), ((f,a,a),(f,e))
        Assert.Contains(pda.Transitions, t => t.ToTupleString() == "((s, e, e), (f, S))");
        Assert.Contains(pda.Transitions, t => t.ToTupleString() == "((f, e, S), (f, aSb))");
        Assert.Contains(pda.Transitions, t => t.ToTupleString() == "((f, e, S), (f, e))");
        Assert.Contains(pda.Transitions, t => t.ToTupleString() == "((f, a, a), (f, e))");
        Assert.Contains(pda.Transitions, t => t.ToTupleString() == "((f, b, b), (f, e))");

        Assert.True(PdaTracer.Trace(pda, "e").Accepted);
        Assert.True(PdaTracer.Trace(pda, "ab").Accepted);
        Assert.True(PdaTracer.Trace(pda, "aabb").Accepted);
        Assert.False(PdaTracer.Trace(pda, "abb").Accepted);
        Assert.False(PdaTracer.Trace(pda, "ba").Accepted);
    }

    [Fact]
    public void Pda_ParseTransitions_SupportsArrowAndTupleSyntaxRoundTrip()
    {
        var pda = PushdownAutomaton.ParseTransitions(
            "start: s\naccept: f\ns, a, e -> s, a\ns, e, e -> f, e\nf, b, a -> f, e");
        var roundTripped = PushdownAutomaton.ParseTransitions(pda.ToTransitionsText());

        Assert.True(roundTripped.Accepts("aabb"));
        Assert.False(roundTripped.Accepts("abb"));
    }

    [Fact]
    public void PdaTracer_EmitsStructuredPushPopAndInputDeltasForStackVisualization()
    {
        var cfg = Cfg.ParseFromText("S -> aSb | aA\nA -> aA | e");
        var pda = CfgToPdaConverter.Convert(cfg);

        var trace = PdaTracer.Trace(pda, "aab");
        Assert.True(trace.Accepted);

        var steps = trace.Timeline.Steps;
        Assert.Equal("INIT", steps[0].State.OperationKind);
        Assert.Equal("aab", steps[0].State.FullInput);
        Assert.Equal(0, steps[0].State.InputIndex);

        // Step 1 pushes start symbol S
        Assert.Equal("PUSH", steps[1].State.OperationKind);
        Assert.Equal(new[] { "S" }, steps[1].State.PushedFrames);
        Assert.Empty(steps[1].State.PoppedFrames!);

        // Contains a REPLACE expansion (Pop S -> Push aSb) and MATCH steps (Read 'a', Pop 'a')
        Assert.Contains(steps, s =>
            s.State.OperationKind == "REPLACE" &&
            s.State.PoppedFrames!.SequenceEqual(new[] { "S" }) &&
            s.State.PushedFrames!.SequenceEqual(new[] { "a", "S", "b" }));

        Assert.Contains(steps, s =>
            s.State.OperationKind == "MATCH" &&
            s.State.ReadSymbols == "a" &&
            s.State.PoppedFrames!.SequenceEqual(new[] { "a" }));

        // Final step reached end of input with empty stack
        Assert.Equal(3, steps[^1].State.InputIndex);
        Assert.Empty(steps[^1].State.StackFrames);
        Assert.Equal(true, steps[^1].State.IsAccepted);
    }
}
