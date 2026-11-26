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
}
