using System.Text;
using SuperCsStudent.Core.Diagram;

namespace SuperCsStudent.Automata.FiniteAutomata;

public sealed record NfaTransition(string FromState, string Symbol, string ToState)
{
    public bool IsEpsilon => AutomataSymbols.IsEmptyToken(Symbol);
}

/// <summary>
/// Nondeterministic Finite Automaton quintuple (States, Alphabet, Transitions, StartState, AcceptStates)
/// supporting epsilon transitions normalized to "e".
/// </summary>
public sealed class Nfa
{
    public IReadOnlySet<string> States { get; }
    public IReadOnlyList<string> OrderedStates { get; }
    public IReadOnlySet<char> Alphabet { get; }
    public IReadOnlyList<char> OrderedAlphabet { get; }
    public IReadOnlyList<NfaTransition> Transitions { get; }
    public string StartState { get; }
    public IReadOnlySet<string> AcceptStates { get; }

    private readonly Dictionary<(string State, string Symbol), List<string>> _adj;

    public Nfa(
        IEnumerable<string> states,
        IEnumerable<char> alphabet,
        IEnumerable<NfaTransition> transitions,
        string startState,
        IEnumerable<string> acceptStates)
    {
        var stateList = states.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var stateSet = new HashSet<string>(stateList, StringComparer.Ordinal);
        if (!stateSet.Contains(startState))
        {
            throw new ArgumentException($"Start state '{startState}' must be in States.", nameof(startState));
        }

        var alphaList = alphabet.Distinct().OrderBy(c => c).ToList();
        var alphaSet = new HashSet<char>(alphaList);

        var acceptSet = new HashSet<string>(acceptStates, StringComparer.Ordinal);
        if (!acceptSet.IsSubsetOf(stateSet))
        {
            throw new ArgumentException("All accept states must be in States.", nameof(acceptStates));
        }

        var normalizedTransitions = new List<NfaTransition>();
        var seen = new HashSet<NfaTransition>();
        _adj = new Dictionary<(string State, string Symbol), List<string>>();

        foreach (var raw in transitions)
        {
            if (!stateSet.Contains(raw.FromState))
            {
                throw new ArgumentException($"Transition source '{raw.FromState}' is not in States.", nameof(transitions));
            }
            if (!stateSet.Contains(raw.ToState))
            {
                throw new ArgumentException($"Transition target '{raw.ToState}' is not in States.", nameof(transitions));
            }

            var normSym = raw.Symbol.Length == 1 && alphaSet.Contains(raw.Symbol[0])
                ? raw.Symbol
                : AutomataSymbols.NormalizeSymbol(raw.Symbol);

            if (normSym != AutomataSymbols.Empty)
            {
                if (normSym.Length != 1 || !alphaSet.Contains(normSym[0]))
                {
                    throw new ArgumentException($"Transition symbol '{raw.Symbol}' is not in Alphabet or epsilon.", nameof(transitions));
                }
            }

            var t = new NfaTransition(raw.FromState, normSym, raw.ToState);
            if (seen.Add(t))
            {
                normalizedTransitions.Add(t);
            }
        }

        normalizedTransitions.Sort((x, y) =>
        {
            var c = StringComparer.Ordinal.Compare(x.FromState, y.FromState);
            if (c != 0) return c;
            c = StringComparer.Ordinal.Compare(x.Symbol, y.Symbol);
            if (c != 0) return c;
            return StringComparer.Ordinal.Compare(x.ToState, y.ToState);
        });

        foreach (var t in normalizedTransitions)
        {
            var key = (t.FromState, t.Symbol);
            if (!_adj.TryGetValue(key, out var list))
            {
                list = new List<string>();
                _adj[key] = list;
            }
            list.Add(t.ToState);
        }

        States = stateSet;
        OrderedStates = stateList;
        Alphabet = alphaSet;
        OrderedAlphabet = alphaList;
        Transitions = normalizedTransitions;
        StartState = startState;
        AcceptStates = acceptSet;
    }

    public IReadOnlySet<string> EpsilonClosure(string state) => EpsilonClosure(new[] { state });

    public IReadOnlySet<string> EpsilonClosure(IEnumerable<string> states)
    {
        var closure = new SortedSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        foreach (var s in states)
        {
            if (States.Contains(s) && closure.Add(s))
            {
                stack.Push(s);
            }
        }

        while (stack.Count > 0)
        {
            var cur = stack.Pop();
            if (_adj.TryGetValue((cur, AutomataSymbols.Empty), out var nexts))
            {
                foreach (var nxt in nexts)
                {
                    if (closure.Add(nxt))
                    {
                        stack.Push(nxt);
                    }
                }
            }
        }

        return closure;
    }

    public IReadOnlySet<string> Move(IEnumerable<string> states, char symbol)
    {
        var result = new SortedSet<string>(StringComparer.Ordinal);
        var symStr = symbol.ToString();
        foreach (var s in states)
        {
            if (_adj.TryGetValue((s, symStr), out var nexts))
            {
                foreach (var nxt in nexts)
                {
                    result.Add(nxt);
                }
            }
        }
        return result;
    }

    public bool Accepts(string? input)
    {
        var word = AutomataSymbols.NormalizeWord(input, Alphabet);
        var active = EpsilonClosure(StartState);
        foreach (var ch in word)
        {
            if (!Alphabet.Contains(ch))
            {
                return false;
            }
            active = EpsilonClosure(Move(active, ch));
            if (active.Count == 0)
            {
                return false;
            }
        }
        return active.Any(s => AcceptStates.Contains(s));
    }

    /// <summary>
    /// Converts this NFA to an equivalent deterministic finite automaton via subset construction.
    /// </summary>
    public Dfa ToDfa(bool renameStates = true)
    {
        return ToDfaWithSubsetMap(out _, renameStates);
    }

    public Dfa ToDfaWithSubsetMap(out IReadOnlyDictionary<string, IReadOnlyList<string>> stateMap, bool renameStates = true)
    {
        var startSubset = EpsilonClosure(StartState).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var subsetKeyToName = new Dictionary<string, string>(StringComparer.Ordinal);
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        var queue = new Queue<List<string>>();

        string FormatSubsetKey(List<string> subset) =>
            subset.Count == 0 ? "∅" : "{" + string.Join(",", subset) + "}";

        string RegisterSubset(List<string> subset)
        {
            var key = FormatSubsetKey(subset);
            if (!subsetKeyToName.TryGetValue(key, out var name))
            {
                name = renameStates ? $"q{subsetKeyToName.Count}" : key;
                subsetKeyToName[key] = name;
                map[name] = subset;
                queue.Enqueue(subset);
            }
            return name;
        }

        var startName = RegisterSubset(startSubset);
        var dfaTransitions = new Dictionary<(string State, char Symbol), string>();
        var dfaAccept = new HashSet<string>(StringComparer.Ordinal);

        while (queue.Count > 0)
        {
            var currentSubset = queue.Dequeue();
            var fromName = subsetKeyToName[FormatSubsetKey(currentSubset)];

            if (currentSubset.Any(s => AcceptStates.Contains(s)))
            {
                dfaAccept.Add(fromName);
            }

            foreach (var a in OrderedAlphabet)
            {
                var nextSubset = EpsilonClosure(Move(currentSubset, a))
                    .OrderBy(s => s, StringComparer.Ordinal)
                    .ToList();
                var toName = RegisterSubset(nextSubset);
                dfaTransitions[(fromName, a)] = toName;
            }
        }

        stateMap = map;
        return new Dfa(subsetKeyToName.Values, OrderedAlphabet, dfaTransitions, startName, dfaAccept);
    }

    public Dfa ToMinimalDfa() => ToDfa(renameStates: true).Minimize();

    public DiagramScene ToDiagramScene(
        IReadOnlySet<string>? activeStates = null,
        IReadOnlySet<NfaTransition>? activeTransitions = null,
        string? caption = null)
    {
        return FaTracer.BuildDiagramScene(this, activeStates, activeTransitions, null, caption);
    }

    public string ToFormalString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"K = {{{string.Join(", ", OrderedStates)}}}");
        sb.AppendLine($"Σ = {{{string.Join(", ", OrderedAlphabet)}}}");
        sb.AppendLine($"s = {StartState}");
        sb.AppendLine($"F = {{{string.Join(", ", AcceptStates.OrderBy(x => x, StringComparer.Ordinal))}}}");
        sb.AppendLine("Δ:");
        foreach (var t in Transitions)
        {
            sb.AppendLine($"  ({t.FromState}, {t.Symbol}, {t.ToState})");
        }
        return sb.ToString().TrimEnd();
    }
}
