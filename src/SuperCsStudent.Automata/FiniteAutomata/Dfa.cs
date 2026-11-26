using System.Text;
using SuperCsStudent.Core.Diagram;

namespace SuperCsStudent.Automata.FiniteAutomata;

/// <summary>
/// Deterministic Finite Automaton quintuple (States, Alphabet, Transitions, StartState, AcceptStates).
/// </summary>
public sealed class Dfa
{
    public IReadOnlySet<string> States { get; }
    public IReadOnlyList<string> OrderedStates { get; }
    public IReadOnlySet<char> Alphabet { get; }
    public IReadOnlyList<char> OrderedAlphabet { get; }
    public IReadOnlyDictionary<(string State, char Symbol), string> Transitions { get; }
    public string StartState { get; }
    public IReadOnlySet<string> AcceptStates { get; }

    public Dfa(
        IEnumerable<string> states,
        IEnumerable<char> alphabet,
        IReadOnlyDictionary<(string State, char Symbol), string> transitions,
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

        var transCopy = new Dictionary<(string State, char Symbol), string>();
        foreach (var kv in transitions)
        {
            if (!stateSet.Contains(kv.Key.State))
            {
                throw new ArgumentException($"Transition source state '{kv.Key.State}' is not in States.", nameof(transitions));
            }
            if (!stateSet.Contains(kv.Value))
            {
                throw new ArgumentException($"Transition target state '{kv.Value}' is not in States.", nameof(transitions));
            }
            if (!alphaSet.Contains(kv.Key.Symbol))
            {
                throw new ArgumentException($"Transition symbol '{kv.Key.Symbol}' is not in Alphabet.", nameof(transitions));
            }
            transCopy[kv.Key] = kv.Value;
        }

        States = stateSet;
        OrderedStates = stateList;
        Alphabet = alphaSet;
        OrderedAlphabet = alphaList;
        Transitions = transCopy;
        StartState = startState;
        AcceptStates = acceptSet;
    }

    public string? Step(string state, char symbol)
    {
        return Transitions.TryGetValue((state, symbol), out var next) ? next : null;
    }

    public bool IsComplete()
    {
        foreach (var q in OrderedStates)
        {
            foreach (var a in OrderedAlphabet)
            {
                if (!Transitions.ContainsKey((q, a)))
                {
                    return false;
                }
            }
        }
        return true;
    }

    public bool Accepts(string? input)
    {
        var word = AutomataSymbols.NormalizeWord(input, Alphabet);
        var current = StartState;
        foreach (var ch in word)
        {
            if (!Alphabet.Contains(ch))
            {
                return false;
            }
            if (!Transitions.TryGetValue((current, ch), out var next))
            {
                return false;
            }
            current = next;
        }
        return AcceptStates.Contains(current);
    }

    public Dfa Complete(IEnumerable<char>? extraAlphabet = null, string deadStatePrefix = "q_dead")
    {
        var targetAlpha = new SortedSet<char>(OrderedAlphabet);
        if (extraAlphabet is not null)
        {
            foreach (var c in extraAlphabet)
            {
                targetAlpha.Add(c);
            }
        }

        var missingAny = false;
        foreach (var q in OrderedStates)
        {
            foreach (var a in targetAlpha)
            {
                if (!Transitions.ContainsKey((q, a)))
                {
                    missingAny = true;
                    break;
                }
            }
            if (missingAny) break;
        }

        if (!missingAny && targetAlpha.Count == Alphabet.Count)
        {
            return this;
        }

        var states = new HashSet<string>(States, StringComparer.Ordinal);
        var trans = new Dictionary<(string State, char Symbol), string>(Transitions);

        if (missingAny)
        {
            var dead = deadStatePrefix;
            var suffix = 0;
            while (states.Contains(dead))
            {
                dead = $"{deadStatePrefix}_{suffix++}";
            }
            states.Add(dead);

            foreach (var q in states.ToList())
            {
                foreach (var a in targetAlpha)
                {
                    if (!trans.ContainsKey((q, a)))
                    {
                        trans[(q, a)] = dead;
                    }
                }
            }
        }

        return new Dfa(states, targetAlpha, trans, StartState, AcceptStates);
    }

    public Dfa RemoveUnreachableStates()
    {
        var reachable = new HashSet<string>(StringComparer.Ordinal) { StartState };
        var queue = new Queue<string>();
        queue.Enqueue(StartState);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            foreach (var a in OrderedAlphabet)
            {
                if (Transitions.TryGetValue((cur, a), out var nxt) && reachable.Add(nxt))
                {
                    queue.Enqueue(nxt);
                }
            }
        }

        var newTransitions = Transitions
            .Where(kv => reachable.Contains(kv.Key.State) && reachable.Contains(kv.Value))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        var newAccept = AcceptStates.Where(s => reachable.Contains(s));

        return new Dfa(reachable, OrderedAlphabet, newTransitions, StartState, newAccept);
    }

    /// <summary>
    /// Minimizes this DFA via deterministic partition refinement and canonical BFS state naming (q0, q1, ...).
    /// </summary>
    public Dfa Minimize()
    {
        var completed = RemoveUnreachableStates().Complete();
        var states = completed.OrderedStates;
        var alpha = completed.OrderedAlphabet;

        var acceptBlock = states.Where(s => completed.AcceptStates.Contains(s)).ToList();
        var nonAcceptBlock = states.Where(s => !completed.AcceptStates.Contains(s)).ToList();

        var partitions = new List<List<string>>();
        if (acceptBlock.Count > 0) partitions.Add(acceptBlock);
        if (nonAcceptBlock.Count > 0) partitions.Add(nonAcceptBlock);

        Dictionary<string, int> BuildBlockIndex(List<List<string>> blocks)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < blocks.Count; i++)
            {
                foreach (var s in blocks[i])
                {
                    map[s] = i;
                }
            }
            return map;
        }

        bool changed;
        do
        {
            changed = false;
            var blockOf = BuildBlockIndex(partitions);
            var nextPartitions = new List<List<string>>();

            foreach (var block in partitions)
            {
                if (block.Count <= 1)
                {
                    nextPartitions.Add(block);
                    continue;
                }

                var groups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
                foreach (var q in block)
                {
                    var sigBuilder = new StringBuilder();
                    foreach (var a in alpha)
                    {
                        var target = completed.Transitions[(q, a)];
                        sigBuilder.Append(blockOf[target]).Append(',');
                    }
                    var sig = sigBuilder.ToString();
                    if (!groups.TryGetValue(sig, out var list))
                    {
                        list = new List<string>();
                        groups[sig] = list;
                    }
                    list.Add(q);
                }

                if (groups.Count > 1)
                {
                    changed = true;
                }

                foreach (var g in groups.Values)
                {
                    nextPartitions.Add(g);
                }
            }

            partitions = nextPartitions;
        } while (changed);

        var finalBlockOf = BuildBlockIndex(partitions);
        var startBlock = finalBlockOf[completed.StartState];

        // Canonical BFS ordering from the start equivalence class
        var blockToNewName = new Dictionary<int, string>();
        var bfs = new Queue<int>();
        blockToNewName[startBlock] = "q0";
        bfs.Enqueue(startBlock);

        var newTransitions = new Dictionary<(string State, char Symbol), string>();
        var newAccept = new HashSet<string>(StringComparer.Ordinal);

        while (bfs.Count > 0)
        {
            var b = bfs.Dequeue();
            var rep = partitions[b][0];
            var fromName = blockToNewName[b];

            if (completed.AcceptStates.Contains(rep))
            {
                newAccept.Add(fromName);
            }

            foreach (var a in alpha)
            {
                var targetState = completed.Transitions[(rep, a)];
                var targetBlock = finalBlockOf[targetState];
                if (!blockToNewName.TryGetValue(targetBlock, out var toName))
                {
                    toName = $"q{blockToNewName.Count}";
                    blockToNewName[targetBlock] = toName;
                    bfs.Enqueue(targetBlock);
                }
                newTransitions[(fromName, a)] = toName;
            }
        }

        return new Dfa(blockToNewName.Values, alpha, newTransitions, "q0", newAccept);
    }

    public Nfa ToNfa()
    {
        var transitions = Transitions
            .Select(kv => new NfaTransition(kv.Key.State, kv.Key.Symbol.ToString(), kv.Value))
            .ToList();
        return new Nfa(OrderedStates, OrderedAlphabet, transitions, StartState, AcceptStates);
    }

    public DiagramScene ToDiagramScene(
        string? activeState = null,
        (string From, char Symbol, string To)? activeTransition = null,
        string? caption = null)
    {
        return FaTracer.BuildDiagramScene(this, activeState, activeTransition, null, caption);
    }

    public string ToFormalString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"K = {{{string.Join(", ", OrderedStates)}}}");
        sb.AppendLine($"Σ = {{{string.Join(", ", OrderedAlphabet)}}}");
        sb.AppendLine($"s = {StartState}");
        sb.AppendLine($"F = {{{string.Join(", ", AcceptStates.OrderBy(x => x, StringComparer.Ordinal))}}}");
        sb.AppendLine("δ:");
        foreach (var q in OrderedStates)
        {
            foreach (var a in OrderedAlphabet)
            {
                if (Transitions.TryGetValue((q, a), out var nxt))
                {
                    sb.AppendLine($"  δ({q}, {a}) = {nxt}");
                }
            }
        }
        return sb.ToString().TrimEnd();
    }
}
