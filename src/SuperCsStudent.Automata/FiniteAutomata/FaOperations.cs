namespace SuperCsStudent.Automata.FiniteAutomata;

public sealed record FaDecisionResult(
    bool Result,
    string? Counterexample,
    string Explanation);

/// <summary>
/// Regular language closure operations and decision algorithms with shortest counterexample witnesses.
/// </summary>
public static class FaOperations
{
    public static Dfa Complement(Dfa dfa, IEnumerable<char>? overAlphabet = null)
    {
        var completed = dfa.Complete(overAlphabet);
        var flippedAccept = completed.States.Where(s => !completed.AcceptStates.Contains(s));
        return new Dfa(completed.States, completed.OrderedAlphabet, completed.Transitions, completed.StartState, flippedAccept)
            .Minimize();
    }

    public static Dfa Union(Dfa first, Dfa second)
    {
        return Product(first, second, (aAcc, bAcc) => aAcc || bAcc).Minimize();
    }

    public static Dfa Intersection(Dfa first, Dfa second)
    {
        return Product(first, second, (aAcc, bAcc) => aAcc && bAcc).Minimize();
    }

    public static Dfa Difference(Dfa first, Dfa second)
    {
        return Product(first, second, (aAcc, bAcc) => aAcc && !bAcc).Minimize();
    }

    public static Dfa SymmetricDifference(Dfa first, Dfa second)
    {
        return Product(first, second, (aAcc, bAcc) => aAcc != bAcc).Minimize();
    }

    public static Dfa Concatenation(Dfa first, Dfa second)
    {
        var alpha = new SortedSet<char>(first.OrderedAlphabet);
        foreach (var c in second.OrderedAlphabet) alpha.Add(c);

        var states = new List<string>();
        var transitions = new List<NfaTransition>();

        foreach (var s in first.OrderedStates) states.Add($"a_{s}");
        foreach (var s in second.OrderedStates) states.Add($"b_{s}");

        foreach (var kv in first.Transitions)
        {
            transitions.Add(new NfaTransition($"a_{kv.Key.State}", kv.Key.Symbol.ToString(), $"a_{kv.Value}"));
        }

        foreach (var kv in second.Transitions)
        {
            transitions.Add(new NfaTransition($"b_{kv.Key.State}", kv.Key.Symbol.ToString(), $"b_{kv.Value}"));
        }

        foreach (var acc in first.AcceptStates)
        {
            transitions.Add(new NfaTransition($"a_{acc}", AutomataSymbols.Empty, $"b_{second.StartState}"));
        }

        var accept = second.AcceptStates.Select(s => $"b_{s}");
        var nfa = new Nfa(states, alpha, transitions, $"a_{first.StartState}", accept);
        return nfa.ToMinimalDfa();
    }

    public static Dfa KleeneStar(Dfa dfa)
    {
        const string newStart = "star_start";
        var states = new List<string> { newStart };
        var transitions = new List<NfaTransition>
        {
            new(newStart, AutomataSymbols.Empty, $"k_{dfa.StartState}")
        };

        foreach (var s in dfa.OrderedStates) states.Add($"k_{s}");
        foreach (var kv in dfa.Transitions)
        {
            transitions.Add(new NfaTransition($"k_{kv.Key.State}", kv.Key.Symbol.ToString(), $"k_{kv.Value}"));
        }

        var accept = new List<string> { newStart };
        foreach (var acc in dfa.AcceptStates)
        {
            var prefixed = $"k_{acc}";
            accept.Add(prefixed);
            transitions.Add(new NfaTransition(prefixed, AutomataSymbols.Empty, $"k_{dfa.StartState}"));
        }

        var nfa = new Nfa(states, dfa.OrderedAlphabet, transitions, newStart, accept);
        return nfa.ToMinimalDfa();
    }

    private static Dfa Product(Dfa first, Dfa second, Func<bool, bool, bool> acceptPredicate)
    {
        var commonAlpha = new SortedSet<char>(first.OrderedAlphabet);
        foreach (var c in second.OrderedAlphabet) commonAlpha.Add(c);

        var a = first.Complete(commonAlpha);
        var b = second.Complete(commonAlpha);

        var startPair = (a.StartState, b.StartState);
        var pairToName = new Dictionary<(string, string), string> { [startPair] = "p0" };
        var queue = new Queue<(string, string)>();
        queue.Enqueue(startPair);

        var transitions = new Dictionary<(string State, char Symbol), string>();
        var accept = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var fromName = pairToName[cur];
            if (acceptPredicate(a.AcceptStates.Contains(cur.Item1), b.AcceptStates.Contains(cur.Item2)))
            {
                accept.Add(fromName);
            }

            foreach (var sym in commonAlpha)
            {
                var nextPair = (a.Transitions[(cur.Item1, sym)], b.Transitions[(cur.Item2, sym)]);
                if (!pairToName.TryGetValue(nextPair, out var toName))
                {
                    toName = $"p{pairToName.Count}";
                    pairToName[nextPair] = toName;
                    queue.Enqueue(nextPair);
                }
                transitions[(fromName, sym)] = toName;
            }
        }

        return new Dfa(pairToName.Values, commonAlpha, transitions, "p0", accept);
    }

    // --- Five Decision Algorithms ---

    public static FaDecisionResult Accepts(Dfa dfa, string? input)
    {
        var raw = AutomataSymbols.NormalizeWord(input, dfa.Alphabet);
        var display = FormatWitness(raw, dfa.Alphabet);
        var accepted = dfa.Accepts(input);
        return accepted
            ? new FaDecisionResult(true, null, $"The DFA accepts '{display}'.")
            : new FaDecisionResult(false, display, $"The DFA rejects '{display}'.");
    }

    public static FaDecisionResult IsEmpty(Dfa dfa)
    {
        var witness = FindShortestAcceptedWord(dfa);
        if (witness is null)
        {
            return new FaDecisionResult(true, null, "L(M) = ∅ (no accepting state is reachable).");
        }

        var display = FormatWitness(witness, dfa.Alphabet);
        return new FaDecisionResult(false, display, $"L(M) ≠ ∅; shortest accepted string is '{display}'.");
    }

    public static FaDecisionResult IsUniversal(Dfa dfa, IEnumerable<char>? overAlphabet = null)
    {
        var comp = Complement(dfa, overAlphabet);
        var witness = FindShortestAcceptedWord(comp);
        if (witness is null)
        {
            return new FaDecisionResult(true, null, "L(M) = Σ* (every string over Σ is accepted).");
        }

        var display = FormatWitness(witness, comp.Alphabet);
        return new FaDecisionResult(false, display, $"L(M) ≠ Σ*; shortest rejected string is '{display}'.");
    }

    public static FaDecisionResult IsSubsetOf(Dfa first, Dfa second)
    {
        var diff = Difference(first, second);
        var witness = FindShortestAcceptedWord(diff);
        if (witness is null)
        {
            return new FaDecisionResult(true, null, "L(M1) ⊆ L(M2) (L(M1) \\ L(M2) = ∅).");
        }

        var display = FormatWitness(witness, diff.Alphabet);
        return new FaDecisionResult(false, display, $"L(M1) ⊄ L(M2); counterexample '{display}' is in L(M1) but not L(M2).");
    }

    public static FaDecisionResult AreEquivalent(Dfa first, Dfa second)
    {
        var symDiff = SymmetricDifference(first, second);
        var witness = FindShortestAcceptedWord(symDiff);
        if (witness is null)
        {
            return new FaDecisionResult(true, null, "L(M1) = L(M2) (symmetric difference is empty).");
        }

        var display = FormatWitness(witness, symDiff.Alphabet);
        var inFirst = first.Accepts(witness);
        var detail = inFirst
            ? $"'{display}' is accepted by M1 but rejected by M2."
            : $"'{display}' is accepted by M2 but rejected by M1.";
        return new FaDecisionResult(false, display, $"L(M1) ≠ L(M2); {detail}");
    }

    /// <summary>
    /// Breadth-first search for the lexicographically shortest string accepted by the DFA.
    /// Returns null if the language is empty; returns "" for the empty string.
    /// </summary>
    public static string? FindShortestAcceptedWord(Dfa dfa)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal) { dfa.StartState };
        var queue = new Queue<(string State, string Word)>();
        queue.Enqueue((dfa.StartState, string.Empty));

        while (queue.Count > 0)
        {
            var (state, word) = queue.Dequeue();
            if (dfa.AcceptStates.Contains(state))
            {
                return word;
            }

            foreach (var sym in dfa.OrderedAlphabet)
            {
                if (dfa.Transitions.TryGetValue((state, sym), out var next) && visited.Add(next))
                {
                    queue.Enqueue((next, word + sym));
                }
            }
        }

        return null;
    }

    private static string FormatWitness(string raw, IReadOnlySet<char> alphabet)
    {
        if (raw.Length == 0)
        {
            return alphabet.Contains('e') ? string.Empty : AutomataSymbols.Empty;
        }
        return raw;
    }
}
