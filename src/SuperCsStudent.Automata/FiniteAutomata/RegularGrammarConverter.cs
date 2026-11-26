using SuperCsStudent.Automata.Grammars;

namespace SuperCsStudent.Automata.FiniteAutomata;

/// <summary>
/// Equivalence conversions between Finite Automata and right-linear Regular Grammars.
/// </summary>
public static class RegularGrammarConverter
{
    /// <summary>
    /// Converts a DFA into an equivalent right-linear regular grammar:
    ///   - For each transition δ(q, a) = p: rule q -> a p
    ///   - For each accept state q ∈ F: rule q -> e
    /// </summary>
    public static Cfg DfaToRegularGrammar(Dfa dfa)
    {
        var terminals = new HashSet<string>(dfa.OrderedAlphabet.Select(c => c.ToString()), StringComparer.Ordinal);
        var stateNames = new Dictionary<string, string>(StringComparer.Ordinal);

        // Ensure state non-terminals never collide with terminals
        foreach (var q in dfa.OrderedStates)
        {
            var candidate = q;
            while (terminals.Contains(candidate))
            {
                candidate = $"Q_{candidate}";
            }
            stateNames[q] = candidate;
        }

        var variables = new HashSet<string>(terminals, StringComparer.Ordinal);
        foreach (var nt in stateNames.Values)
        {
            variables.Add(nt);
        }

        var rules = new List<CfgRule>();
        foreach (var q in dfa.OrderedStates)
        {
            var lhs = stateNames[q];
            foreach (var a in dfa.OrderedAlphabet)
            {
                if (dfa.Transitions.TryGetValue((q, a), out var p))
                {
                    rules.Add(new CfgRule(lhs, new[] { a.ToString(), stateNames[p] }));
                }
            }

            if (dfa.AcceptStates.Contains(q))
            {
                rules.Add(new CfgRule(lhs, Array.Empty<string>()));
            }
        }

        return new Cfg(variables, terminals, rules, stateNames[dfa.StartState]);
    }

    /// <summary>
    /// Converts a right-linear regular grammar G = (V, Σ, R, S) into an equivalent NFA:
    ///   K = (V - Σ) ∪ {f} (plus any auxiliary states if a rule emits multiple terminals), F = {f}.
    /// </summary>
    public static Nfa RegularGrammarToNfa(Cfg grammar)
    {
        if (!grammar.IsRegularGrammar())
        {
            throw new ArgumentException("Grammar is not a right-linear regular grammar.", nameof(grammar));
        }

        var states = new HashSet<string>(grammar.NonTerminals, StringComparer.Ordinal);
        var finalState = "f";
        var suffix = 0;
        while (states.Contains(finalState))
        {
            finalState = $"f_{suffix++}";
        }
        states.Add(finalState);

        var alphabet = new SortedSet<char>();
        foreach (var t in grammar.Terminals)
        {
            if (t.Length != 1)
            {
                throw new ArgumentException($"Terminal '{t}' must be a single character for NFA conversion.", nameof(grammar));
            }
            alphabet.Add(t[0]);
        }

        var transitions = new List<NfaTransition>();
        var auxCounter = 0;

        foreach (var rule in grammar.Rules)
        {
            if (rule.Right.Count == 0)
            {
                // A -> e  ==>  (A, e, f)
                transitions.Add(new NfaTransition(rule.Left, AutomataSymbols.Empty, finalState));
                continue;
            }

            var last = rule.Right[^1];
            var endsWithNonTerminal = grammar.NonTerminals.Contains(last);
            var targetState = endsWithNonTerminal ? last : finalState;
            var terminalCount = endsWithNonTerminal ? rule.Right.Count - 1 : rule.Right.Count;

            if (terminalCount == 0)
            {
                // A -> B  ==>  (A, e, B)
                transitions.Add(new NfaTransition(rule.Left, AutomataSymbols.Empty, targetState));
            }
            else if (terminalCount == 1)
            {
                // A -> a B  or  A -> a
                transitions.Add(new NfaTransition(rule.Left, rule.Right[0], targetState));
            }
            else
            {
                // Extended right-linear rule A -> a1 a2 ... ak [B]
                var cur = rule.Left;
                for (var i = 0; i < terminalCount - 1; i++)
                {
                    string mid;
                    do
                    {
                        mid = $"aux_{auxCounter++}";
                    } while (!states.Add(mid));

                    transitions.Add(new NfaTransition(cur, rule.Right[i], mid));
                    cur = mid;
                }
                transitions.Add(new NfaTransition(cur, rule.Right[terminalCount - 1], targetState));
            }
        }

        return new Nfa(states, alphabet, transitions, grammar.StartSymbol, new[] { finalState });
    }
}
