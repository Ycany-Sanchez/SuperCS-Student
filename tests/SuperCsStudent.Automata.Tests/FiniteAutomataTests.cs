using SuperCsStudent.Automata.FiniteAutomata;
using SuperCsStudent.Automata.Grammars;
using SuperCsStudent.Core.Diagram;

namespace SuperCsStudent.Automata.Tests;

public class FiniteAutomataTests
{
    [Fact]
    public void RegexCompiler_CompilesUnionConcatStarPlusOptionalAndEpsilonAliases()
    {
        var compiled = RegexCompiler.Compile("(a|b)*aba(a|b)*", new[] { 'a', 'b' });

        Assert.Equal(4, compiled.MinimalDfa.States.Count);
        Assert.True(compiled.Nfa.Accepts("aba"));
        Assert.True(compiled.Dfa.Accepts("bbabaa"));
        Assert.True(compiled.MinimalDfa.Accepts("bbabaa"));
        Assert.False(compiled.MinimalDfa.Accepts("aabb"));
        Assert.False(compiled.MinimalDfa.Accepts("e"));
        Assert.False(compiled.MinimalDfa.Accepts("ε"));
        Assert.False(compiled.MinimalDfa.Accepts("λ"));
        Assert.False(compiled.MinimalDfa.Accepts(""));

        // Algebraic '+' union vs postfix '+' one-or-more, '∪', explicit '.', and '?'
        var plusUnion = RegexCompiler.CompileToMinimalDfa("(a+b)*aba(a+b)*", new[] { 'a', 'b' });
        var cupUnion = RegexCompiler.CompileToMinimalDfa("(a∪b)*a.b.a(a∪b)*", new[] { 'a', 'b' });
        Assert.True(FaOperations.AreEquivalent(compiled.MinimalDfa, plusUnion).Result);
        Assert.True(FaOperations.AreEquivalent(compiled.MinimalDfa, cupUnion).Result);

        var oneOrMore = RegexCompiler.CompileToMinimalDfa("(a|b)+", new[] { 'a', 'b' });
        Assert.False(oneOrMore.Accepts("e"));
        Assert.True(oneOrMore.Accepts("a"));
        Assert.True(oneOrMore.Accepts("ab"));

        var optional = RegexCompiler.CompileToMinimalDfa("a?b", new[] { 'a', 'b' });
        Assert.True(optional.Accepts("b"));
        Assert.True(optional.Accepts("ab"));
        Assert.False(optional.Accepts("aab"));
        Assert.False(optional.Accepts("e"));

        var eps = RegexCompiler.CompileToMinimalDfa("e", new[] { 'a', 'b' });
        Assert.True(eps.Accepts("e"));
        Assert.True(eps.Accepts("ε"));
        Assert.True(eps.Accepts("λ"));
        Assert.True(eps.Accepts(""));
        Assert.False(eps.Accepts("a"));
    }

    [Fact]
    public void FaOperations_ClosureOperationsPreserveRegularLanguageSemantics()
    {
        var startsWithA = RegexCompiler.CompileToMinimalDfa("a(a|b)*", new[] { 'a', 'b' });
        var endsWithB = RegexCompiler.CompileToMinimalDfa("(a|b)*b", new[] { 'a', 'b' });

        var union = FaOperations.Union(startsWithA, endsWithB);
        Assert.True(union.Accepts("a"));
        Assert.True(union.Accepts("b"));
        Assert.True(union.Accepts("ab"));
        Assert.False(union.Accepts("ba"));
        Assert.False(union.Accepts("e"));

        var inter = FaOperations.Intersection(startsWithA, endsWithB);
        Assert.True(inter.Accepts("ab"));
        Assert.True(inter.Accepts("aab"));
        Assert.False(inter.Accepts("a"));
        Assert.False(inter.Accepts("b"));

        var comp = FaOperations.Complement(startsWithA);
        Assert.True(comp.Accepts("e"));
        Assert.True(comp.Accepts("b"));
        Assert.True(comp.Accepts("ba"));
        Assert.False(comp.Accepts("a"));
        Assert.False(comp.Accepts("ab"));

        var exactA = RegexCompiler.CompileToMinimalDfa("a", new[] { 'a', 'b' });
        var exactB = RegexCompiler.CompileToMinimalDfa("b", new[] { 'a', 'b' });
        var concat = FaOperations.Concatenation(exactA, exactB);
        Assert.True(concat.Accepts("ab"));
        Assert.False(concat.Accepts("a"));
        Assert.False(concat.Accepts("ba"));

        var star = FaOperations.KleeneStar(concat);
        Assert.True(star.Accepts("e"));
        Assert.True(star.Accepts("ab"));
        Assert.True(star.Accepts("ababab"));
        Assert.False(star.Accepts("aba"));
    }

    [Fact]
    public void FaOperations_AllFiveDecisionAlgorithmsReturnValidCounterexamples()
    {
        var aStar = RegexCompiler.CompileToMinimalDfa("a*", new[] { 'a', 'b' });
        var sigmaStar = RegexCompiler.CompileToMinimalDfa("(a|b)*", new[] { 'a', 'b' });
        var emptyLang = FaOperations.Intersection(
            RegexCompiler.CompileToMinimalDfa("a", new[] { 'a', 'b' }),
            RegexCompiler.CompileToMinimalDfa("b", new[] { 'a', 'b' }));
        var containsA = RegexCompiler.CompileToMinimalDfa("(a|b)*a(a|b)*", new[] { 'a', 'b' });

        // 1. Accepts
        Assert.True(FaOperations.Accepts(aStar, "aaa").Result);
        Assert.False(FaOperations.Accepts(aStar, "ab").Result);

        // 2. IsEmpty
        var emptyTrue = FaOperations.IsEmpty(emptyLang);
        Assert.True(emptyTrue.Result);
        Assert.Null(emptyTrue.Counterexample);

        var emptyFalse = FaOperations.IsEmpty(containsA);
        Assert.False(emptyFalse.Result);
        Assert.Equal("a", emptyFalse.Counterexample);
        Assert.True(containsA.Accepts(emptyFalse.Counterexample));

        // 3. IsUniversal
        var univTrue = FaOperations.IsUniversal(sigmaStar);
        Assert.True(univTrue.Result);
        Assert.Null(univTrue.Counterexample);

        var univFalse = FaOperations.IsUniversal(aStar);
        Assert.False(univFalse.Result);
        Assert.Equal("b", univFalse.Counterexample);
        Assert.False(aStar.Accepts(univFalse.Counterexample));

        // 4. IsSubsetOf
        var subTrue = FaOperations.IsSubsetOf(aStar, sigmaStar);
        Assert.True(subTrue.Result);
        Assert.Null(subTrue.Counterexample);

        var subFalse = FaOperations.IsSubsetOf(sigmaStar, aStar);
        Assert.False(subFalse.Result);
        Assert.Equal("b", subFalse.Counterexample);
        Assert.True(sigmaStar.Accepts(subFalse.Counterexample));
        Assert.False(aStar.Accepts(subFalse.Counterexample));

        // 5. AreEquivalent
        var aStarAlt = RegexCompiler.CompileToMinimalDfa("e|a+", new[] { 'a', 'b' });
        var eqTrue = FaOperations.AreEquivalent(aStar, aStarAlt);
        Assert.True(eqTrue.Result);
        Assert.Null(eqTrue.Counterexample);

        var eqFalse = FaOperations.AreEquivalent(aStar, containsA);
        Assert.False(eqFalse.Result);
        Assert.Equal("e", eqFalse.Counterexample);
        Assert.NotEqual(aStar.Accepts(eqFalse.Counterexample), containsA.Accepts(eqFalse.Counterexample));
    }

    [Fact]
    public void RegularGrammarConverter_RoundTripsDfaToGrammarToNfaEquivalently()
    {
        var originalDfa = RegexCompiler.CompileToMinimalDfa("(a|b)*aba(a|b)*", new[] { 'a', 'b' });
        var grammar = RegularGrammarConverter.DfaToRegularGrammar(originalDfa);

        Assert.True(grammar.IsRegularGrammar());
        Assert.Contains(grammar.Rules, r => r.IsEpsilon);

        var nfaFromGrammar = RegularGrammarConverter.RegularGrammarToNfa(grammar);
        Assert.Equal(grammar.NonTerminals.Count + 1, nfaFromGrammar.States.Count);
        Assert.Single(nfaFromGrammar.AcceptStates);

        var roundTrippedDfa = nfaFromGrammar.ToMinimalDfa();
        Assert.True(FaOperations.AreEquivalent(originalDfa, roundTrippedDfa).Result);

        // Also check direct CFG parser matches same language on sample strings
        Assert.True(CfgParser.Parse(grammar, "bbabaa").Accepted);
        Assert.False(CfgParser.Parse(grammar, "aabb").Accepted);
    }

    [Fact]
    public void FaTracer_EmitsTimelineWithDiagramSceneStartAndAcceptMarkers()
    {
        var dfa = RegexCompiler.CompileToMinimalDfa("(a|b)*aba(a|b)*", new[] { 'a', 'b' });
        var timeline = FaTracer.Trace(dfa, "aba");

        Assert.True(timeline.IsSuccess);
        Assert.Equal(5, timeline.Count); // init + 3 symbols + accept verdict
        Assert.Equal("(q0, aba)", timeline.Steps[0].State.Configuration);
        Assert.Equal("e", timeline.Steps[^1].State.RemainingInput);
        Assert.True(timeline.Steps[^1].State.IsAccepted);

        var scene = timeline.Steps[0].State.Scene;
        Assert.Contains(scene.Nodes, n => n.IsStart && n.Badge == ">");
        Assert.Contains(scene.Nodes, n => n.IsAccepting && n.Shape == NodeShapes.DoubleCircle);
        Assert.NotEmpty(scene.Edges);
    }

    [Fact]
    public void ConstraintNfa_BuildsCompactStringEdgeNfaAndOnlyDeterminizesOnNegation()
    {
        // starts with aba -> compact 2-state NFA: q0 --aba--> q1 (self-loop a, b), NO dead state!
        var startsRes = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("starts with aba");
        Assert.False(startsRes.RequiredDeterminization);
        Assert.Equal(2, startsRes.Nfa.States.Count);
        Assert.True(startsRes.Nfa.HasStringTransitions);
        Assert.Contains(startsRes.Nfa.Transitions, t => t.FromState == "q0" && t.Symbol == "aba" && t.ToState == "q1");

        // Compare against complete minimal DFA (5 states including dead sink) — must accept exact same language!
        var startsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("starts with aba");
        Assert.Equal(5, startsDfa.States.Count);

        var endsRes = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("ends with aba");
        Assert.False(endsRes.RequiredDeterminization);
        Assert.Equal(2, endsRes.Nfa.States.Count);
        var endsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("ends with aba");

        var containsRes = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("contains aba");
        Assert.False(containsRes.RequiredDeterminization);
        Assert.Equal(2, containsRes.Nfa.States.Count);
        var containsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("contains aba");

        // Union OR stays true nondeterministic NFA without determinization
        var orRes = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("starts with aba OR ends with bab");
        Assert.False(orRes.RequiredDeterminization);
        var orDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("starts with aba OR ends with bab");

        // Negated constraints MUST determinize to complete DFA before complementing
        var notStart = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("does not start with aba");
        var notEnd = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("does not end with aba");
        var notContain = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("does not contain aba");
        Assert.True(notStart.RequiredDeterminization);
        Assert.True(notEnd.RequiredDeterminization);
        Assert.True(notContain.RequiredDeterminization);
        Assert.NotNull(notStart.DeterminizationReason);

        var notStartDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("does not start with aba");
        var notEndDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("does not end with aba");
        var notContainDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("does not contain aba");

        // Exhaustively check all strings over {a, b} up to length 5 across NFA, FaTracer, and DFA
        var queue = new Queue<string>();
        queue.Enqueue("");
        while (queue.Count > 0)
        {
            var w = queue.Dequeue();
            Assert.Equal(startsDfa.Accepts(w), startsRes.Nfa.Accepts(w));
            Assert.Equal(endsDfa.Accepts(w), endsRes.Nfa.Accepts(w));
            Assert.Equal(containsDfa.Accepts(w), containsRes.Nfa.Accepts(w));
            Assert.Equal(orDfa.Accepts(w), orRes.Nfa.Accepts(w));
            Assert.Equal(notStartDfa.Accepts(w), notStart.Nfa.Accepts(w));
            Assert.Equal(notEndDfa.Accepts(w), notEnd.Nfa.Accepts(w));
            Assert.Equal(notContainDfa.Accepts(w), notContain.Nfa.Accepts(w));

            if (w.Length <= 4)
            {
                Assert.Equal(endsDfa.Accepts(w), FaTracer.Trace(endsRes.Nfa, w).IsSuccess);
                Assert.Equal(startsDfa.Accepts(w), FaTracer.Trace(startsRes.Nfa, w).IsSuccess);
            }

            if (w.Length < 5)
            {
                queue.Enqueue(w + "a");
                queue.Enqueue(w + "b");
            }
        }

        // Verify FaTracer highlights intermediate string-edge progress cleanly on "ababa"
        var trace = FaTracer.Trace(startsRes.Nfa, "ababa");
        Assert.True(trace.IsSuccess);
        Assert.Contains(trace.Steps, s => s.State.ActiveStates.Any(st => st.Contains("a·ba")));
        Assert.Contains(trace.Steps, s => s.State.ActiveStates.Any(st => st.Contains("ab·a")));
    }

    [Fact]
    public void ExtendedRegularConstraints_CompileExactDfaCompactNfaAndDoesNotNegation()
    {
        // 1. 3rd from right is 1 -> 4-state compact NFA vs 8-state Minimal DFA
        var kthRightNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("3rd from right is 1", new[] { '0', '1' });
        var kthRightDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("3rd from right is 1", new[] { '0', '1' });
        Assert.False(kthRightNfa.RequiredDeterminization);
        Assert.Equal(4, kthRightNfa.Nfa.States.Count);
        Assert.Equal(8, kthRightDfa.States.Count);

        // Exhaustive binary check up to length 6
        var binQueue = new Queue<string>();
        binQueue.Enqueue("");
        while (binQueue.Count > 0)
        {
            var w = binQueue.Dequeue();
            var expected = w.Length >= 3 && w[^3] == '1';
            Assert.Equal(expected, kthRightDfa.Accepts(w));
            Assert.Equal(expected, kthRightNfa.Nfa.Accepts(w));
            if (w.Length < 6)
            {
                binQueue.Enqueue(w + "0");
                binQueue.Enqueue(w + "1");
            }
        }

        // 2. 2nd symbol is x over {x, y, z} -> 3-state compact NFA (zero dead trap)
        var kthLeftNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("2nd symbol is x", new[] { 'x', 'y', 'z' });
        var kthLeftDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("2nd symbol is x", new[] { 'x', 'y', 'z' });
        Assert.False(kthLeftNfa.RequiredDeterminization);
        Assert.Equal(3, kthLeftNfa.Nfa.States.Count);
        Assert.True(kthLeftDfa.Accepts("yxz"));
        Assert.True(kthLeftNfa.Nfa.Accepts("yxz"));
        Assert.False(kthLeftDfa.Accepts("xyz"));
        Assert.False(kthLeftNfa.Nfa.Accepts("xyz"));
        Assert.False(kthLeftDfa.Accepts("x"));

        // 3. Modulo, strict length/counts, every X followed by Y, same/different endpoints, equals, and does-not forms
        var modCount = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("#a mod 3 = 1", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto);
        var divCount = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("#a divisible by 3", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto);
        var lenMod = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("length mod 3 = 1");
        var lenStrict = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("length > 2 AND length < 5");
        var countStrict = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("more than 1 a AND fewer than 2 b");
        var everyAbNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("every a is followed by b");
        var everyAbDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("every a is followed by b");
        var noConsecNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("no consecutive a");
        var noConsecDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("no consecutive a");
        var sameEndsNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("starts and ends with same symbol");
        var sameEndsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("starts and ends with same symbol");
        var diffEndsNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("starts and ends with different symbol");
        var diffEndsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("starts and ends with different symbol");
        var equalsNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("equals aba");
        var doesNotEqualNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("does not equal aba");
        var doesNotHaveNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("does not have 2nd symbol is a");
        var compoundDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("starts with ab AND does not contain aba AND even b");

        Assert.False(everyAbNfa.RequiredDeterminization);
        Assert.Single(everyAbNfa.Nfa.States);
        Assert.True(noConsecNfa.RequiredDeterminization);
        Assert.True(doesNotEqualNfa.RequiredDeterminization);
        Assert.True(doesNotHaveNfa.RequiredDeterminization);
        Assert.False(sameEndsNfa.RequiredDeterminization);
        Assert.False(diffEndsNfa.RequiredDeterminization);
        Assert.Equal(2, equalsNfa.Nfa.States.Count);

        var abQueue = new Queue<string>();
        abQueue.Enqueue("");
        while (abQueue.Count > 0)
        {
            var w = abQueue.Dequeue();
            var countA = w.Count(c => c == 'a');
            var countB = w.Count(c => c == 'b');
            var expectedEveryAb = !Enumerable.Range(0, w.Length).Any(i => w[i] == 'a' && (i + 1 >= w.Length || w[i + 1] != 'b'));
            var expectedNoConsecA = !w.Contains("aa", StringComparison.Ordinal);
            var expectedSameEnds = w.Length >= 1 && w[0] == w[^1];
            var expectedDiffEnds = w.Length >= 2 && w[0] != w[^1];
            var expectedCompound = w.StartsWith("ab", StringComparison.Ordinal) && !w.Contains("aba", StringComparison.Ordinal) && countB % 2 == 0;

            Assert.Equal(countA % 3 == 1, modCount.MinimalDfa!.Accepts(w));
            Assert.Equal(countA % 3 == 0, divCount.MinimalDfa!.Accepts(w));
            Assert.Equal(w.Length % 3 == 1, lenMod.Accepts(w));
            Assert.Equal(w.Length > 2 && w.Length < 5, lenStrict.Accepts(w));
            Assert.Equal(countA > 1 && countB < 2, countStrict.Accepts(w));
            Assert.Equal(expectedEveryAb, everyAbDfa.Accepts(w));
            Assert.Equal(expectedEveryAb, everyAbNfa.Nfa.Accepts(w));
            Assert.Equal(expectedNoConsecA, noConsecDfa.Accepts(w));
            Assert.Equal(expectedNoConsecA, noConsecNfa.Nfa.Accepts(w));
            Assert.Equal(expectedSameEnds, sameEndsDfa.Accepts(w));
            Assert.Equal(expectedSameEnds, sameEndsNfa.Nfa.Accepts(w));
            Assert.Equal(expectedDiffEnds, diffEndsDfa.Accepts(w));
            Assert.Equal(expectedDiffEnds, diffEndsNfa.Nfa.Accepts(w));
            Assert.Equal(w == "aba", equalsNfa.Nfa.Accepts(w));
            Assert.Equal(w != "aba", doesNotEqualNfa.Nfa.Accepts(w));
            Assert.Equal(!(w.Length >= 2 && w[1] == 'a'), doesNotHaveNfa.Nfa.Accepts(w));
            Assert.Equal(expectedCompound, compoundDfa.Accepts(w));

            if (w.Length < 5)
            {
                abQueue.Enqueue(w + "a");
                abQueue.Enqueue(w + "b");
            }
        }
    }

    [Fact]
    public void ConstraintKleeneStarAndPlus_WorkInTextSpecForDfaAndNfa()
    {
        var starStartsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("(starts with ab)*", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto).MinimalDfa!;
        var starStartsNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("(starts with ab)*");
        Assert.False(starStartsNfa.RequiredDeterminization);

        var plusStartsDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("(starts with ab)+", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto).MinimalDfa!;
        var plusStartsNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("(starts with ab)+");
        Assert.False(plusStartsNfa.RequiredDeterminization);

        var evenLenByStarDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("(length = 2)*", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto).MinimalDfa!;
        var prefixStarDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("star(starts with ab)", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto).MinimalDfa!;
        var negatedStarNfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToNfa("(does not contain aba)*");
        var negatedStarDfa = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.CompileConstraintToDfa("(does not contain aba)*");
        Assert.True(negatedStarNfa.RequiredDeterminization);

        // Regex star patterns still route cleanly to RegexCompiler in Auto mode
        var regexStar = SuperCsStudent.Automata.Synthesis.LanguageSpecCompiler.Compile("(ab)*", SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Auto);
        Assert.Equal(SuperCsStudent.Automata.Synthesis.LanguageSpecMode.Regex, regexStar.ResolvedMode);

        // Check exact language behavior
        Assert.True(starStartsDfa.Accepts(""));
        Assert.True(starStartsDfa.Accepts("ab"));
        Assert.True(starStartsDfa.Accepts("abab"));
        Assert.True(starStartsDfa.Accepts("abbab"));
        Assert.False(starStartsDfa.Accepts("b"));
        Assert.False(starStartsDfa.Accepts("ba"));
        Assert.False(starStartsDfa.Accepts("a"));

        Assert.False(plusStartsDfa.Accepts(""));
        Assert.True(plusStartsDfa.Accepts("ab"));
        Assert.True(plusStartsDfa.Accepts("abab"));

        var q = new Queue<string>();
        q.Enqueue("");
        while (q.Count > 0)
        {
            var w = q.Dequeue();
            Assert.Equal(starStartsDfa.Accepts(w), starStartsNfa.Nfa.Accepts(w));
            Assert.Equal(starStartsDfa.Accepts(w), prefixStarDfa.Accepts(w));
            Assert.Equal(plusStartsDfa.Accepts(w), plusStartsNfa.Nfa.Accepts(w));
            Assert.Equal(w.Length % 2 == 0, evenLenByStarDfa.Accepts(w));
            Assert.Equal(negatedStarDfa.Accepts(w), negatedStarNfa.Nfa.Accepts(w));
            if (w.Length < 5)
            {
                q.Enqueue(w + "a");
                q.Enqueue(w + "b");
            }
        }
    }
}
