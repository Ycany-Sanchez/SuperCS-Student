using SuperCsStudent.Automata.Grammars;

namespace SuperCsStudent.Automata.Pda;

/// <summary>
/// Converts any Context-Free Grammar G = (V, Σ, R, S) into an equivalent Pushdown Automaton
/// using the standard 3-rule top-down expansion construction:
///   1. ((s, e, e), (f, S))
///   2. ((f, e, A), (f, x))   for each production A -> x in R
///   3. ((f, a, a), (f, e))   for each terminal a in Σ
/// </summary>
public static class CfgToPdaConverter
{
    public static PushdownAutomaton Convert(Cfg grammar)
    {
        const string startState = "s";
        const string acceptState = "f";

        var inputAlphabet = new SortedSet<char>();
        foreach (var term in grammar.Terminals)
        {
            foreach (var c in term)
            {
                inputAlphabet.Add(c);
            }
        }

        var stackAlphabet = new HashSet<string>(grammar.Variables, StringComparer.Ordinal);
        var transitions = new List<PdaTransition>
        {
            // Rule 1: push start symbol S onto empty stack
            new(startState, AutomataSymbols.Empty, AutomataSymbols.Empty, acceptState, grammar.StartSymbol)
        };

        // Rule 2: expand each production A -> x on top of stack
        foreach (var rule in grammar.Rules)
        {
            var push = rule.Right.Count == 0
                ? AutomataSymbols.Empty
                : string.Concat(rule.Right);
            transitions.Add(new PdaTransition(acceptState, AutomataSymbols.Empty, rule.Left, acceptState, push));
        }

        // Rule 3: match terminal on input with terminal on top of stack
        foreach (var term in grammar.Terminals.OrderBy(t => t, StringComparer.Ordinal))
        {
            transitions.Add(new PdaTransition(acceptState, term, term, acceptState, AutomataSymbols.Empty));
        }

        return new PushdownAutomaton(
            states: new[] { startState, acceptState },
            inputAlphabet: inputAlphabet,
            stackAlphabet: stackAlphabet,
            transitions: transitions,
            startState: startState,
            acceptStates: new[] { acceptState });
    }
}
