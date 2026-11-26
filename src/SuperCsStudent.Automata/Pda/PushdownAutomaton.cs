using System.Text;
using System.Text.RegularExpressions;
using SuperCsStudent.Core.Diagram;

namespace SuperCsStudent.Automata.Pda;

/// <summary>
/// Generalized PDA transition ((FromState, ReadInput, PopStack), (ToState, PushStack))
/// where ReadInput ∈ Σ* and PopStack, PushStack ∈ Γ* ("e" denotes empty string).
/// </summary>
public sealed record PdaTransition(
    string FromState,
    string ReadInput,
    string PopStack,
    string ToState,
    string PushStack)
{
    public string NormalizedRead => AutomataSymbols.IsEmptyToken(ReadInput) ? AutomataSymbols.Empty : ReadInput;
    public string NormalizedPop => AutomataSymbols.IsEmptyToken(PopStack) ? AutomataSymbols.Empty : PopStack;
    public string NormalizedPush => AutomataSymbols.IsEmptyToken(PushStack) ? AutomataSymbols.Empty : PushStack;

    public string RawRead => AutomataSymbols.IsEmptyToken(ReadInput) ? string.Empty : ReadInput;
    public string RawPop => AutomataSymbols.IsEmptyToken(PopStack) ? string.Empty : PopStack;
    public string RawPush => AutomataSymbols.IsEmptyToken(PushStack) ? string.Empty : PushStack;

    public string ToTupleString() =>
        $"(({FromState}, {NormalizedRead}, {NormalizedPop}), ({ToState}, {NormalizedPush}))";

    public string ToEdgeLabel() =>
        $"{NormalizedRead}, {NormalizedPop} / {NormalizedPush}";

    public override string ToString() => ToTupleString();
}

/// <summary>
/// Generalized Pushdown Automaton 6-tuple (States, InputAlphabet, StackAlphabet, Transitions, StartState, AcceptStates).
/// Computation starts in (StartState, input, "e") with an empty stack (no bottom marker)
/// and accepts iff it reaches (p, "e", "e") for some p ∈ AcceptStates.
/// </summary>
public sealed class PushdownAutomaton
{
    private static readonly Regex TupleLineRegex = new(
        @"^\(\s*\(\s*([^,]+?)\s*,\s*([^,]*?)\s*,\s*([^,]*?)\s*\)\s*,\s*\(\s*([^,]+?)\s*,\s*([^,]*?)\s*\)\s*\)$",
        RegexOptions.Compiled);

    private static readonly Regex ArrowLineRegex = new(
        @"^([^,]+?)\s*,\s*([^,]*?)\s*,\s*([^,-]*?)\s*(?:->|→)\s*([^,]+?)\s*,\s*(.*)$",
        RegexOptions.Compiled);

    public IReadOnlySet<string> States { get; }
    public IReadOnlyList<string> OrderedStates { get; }
    public IReadOnlySet<char> InputAlphabet { get; }
    public IReadOnlySet<string> StackAlphabet { get; }
    public IReadOnlyList<PdaTransition> Transitions { get; }
    public string StartState { get; }
    public IReadOnlySet<string> AcceptStates { get; }

    public PushdownAutomaton(
        IEnumerable<string> states,
        IEnumerable<char> inputAlphabet,
        IEnumerable<string> stackAlphabet,
        IEnumerable<PdaTransition> transitions,
        string startState,
        IEnumerable<string> acceptStates)
    {
        var stateList = states.Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
        var stateSet = new HashSet<string>(stateList, StringComparer.Ordinal);
        if (!stateSet.Contains(startState))
        {
            throw new ArgumentException($"Start state '{startState}' must be in States.", nameof(startState));
        }

        var acceptSet = new HashSet<string>(acceptStates, StringComparer.Ordinal);
        if (!acceptSet.IsSubsetOf(stateSet))
        {
            throw new ArgumentException("All accept states must be in States.", nameof(acceptStates));
        }

        var inputAlpha = new HashSet<char>(inputAlphabet);
        var stackAlpha = new HashSet<string>(stackAlphabet, StringComparer.Ordinal);

        var normalizedTransitions = new List<PdaTransition>();
        var seen = new HashSet<PdaTransition>();

        foreach (var t in transitions)
        {
            if (!stateSet.Contains(t.FromState))
            {
                throw new ArgumentException($"Transition source '{t.FromState}' not in States.", nameof(transitions));
            }
            if (!stateSet.Contains(t.ToState))
            {
                throw new ArgumentException($"Transition target '{t.ToState}' not in States.", nameof(transitions));
            }

            var read = NormalizeField(t.ReadInput, inputAlpha);
            var pop = NormalizeStackField(t.PopStack, stackAlpha);
            var push = NormalizeStackField(t.PushStack, stackAlpha);

            var norm = new PdaTransition(t.FromState, read, pop, t.ToState, push);
            if (seen.Add(norm))
            {
                normalizedTransitions.Add(norm);
            }
        }

        States = stateSet;
        OrderedStates = stateList;
        InputAlphabet = inputAlpha;
        StackAlphabet = stackAlpha;
        Transitions = normalizedTransitions;
        StartState = startState;
        AcceptStates = acceptSet;
    }

    private static string NormalizeField(string? value, HashSet<char> inputAlpha)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AutomataSymbols.Empty;
        }
        var trimmed = value.Trim();
        if (trimmed is "ε" or "λ")
        {
            return AutomataSymbols.Empty;
        }
        if (trimmed == AutomataSymbols.Empty && !inputAlpha.Contains('e'))
        {
            return AutomataSymbols.Empty;
        }
        return trimmed;
    }

    private static string NormalizeStackField(string? value, HashSet<string> stackAlpha)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AutomataSymbols.Empty;
        }
        var trimmed = value.Trim();
        if (trimmed is "ε" or "λ")
        {
            return AutomataSymbols.Empty;
        }
        if (trimmed == AutomataSymbols.Empty && !stackAlpha.Contains(AutomataSymbols.Empty))
        {
            return AutomataSymbols.Empty;
        }
        return trimmed;
    }

    /// <summary>
    /// Parses a PDA from transition text lines supporting both tuple format:
    ///   ((s, a, e), (s, a))
    /// and arrow format:
    ///   s, a, e -> s, a
    /// Optional metadata headers:
    ///   start: s
    ///   accept: f
    /// </summary>
    public static PushdownAutomaton ParseTransitions(
        string text,
        string? startState = null,
        IEnumerable<string>? acceptStates = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new FormatException("PDA transitions text cannot be empty.");
        }

        var lines = text
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        string? parsedStart = startState;
        List<string>? parsedAccept = acceptStates?.ToList();
        var transitions = new List<PdaTransition>();
        var states = new HashSet<string>(StringComparer.Ordinal);
        var inputAlpha = new SortedSet<char>();
        var stackAlpha = new HashSet<string>(StringComparer.Ordinal);
        string? firstState = null;

        foreach (var line in lines)
        {
            if (line.StartsWith("start:", StringComparison.OrdinalIgnoreCase))
            {
                parsedStart ??= line["start:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("accept:", StringComparison.OrdinalIgnoreCase))
            {
                parsedAccept ??= line["accept:".Length..]
                    .Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .ToList();
                continue;
            }

            PdaTransition parsed;
            var tupleMatch = TupleLineRegex.Match(line);
            if (tupleMatch.Success)
            {
                parsed = new PdaTransition(
                    tupleMatch.Groups[1].Value.Trim(),
                    AutomataSymbols.NormalizeSymbol(tupleMatch.Groups[2].Value),
                    AutomataSymbols.NormalizeSymbol(tupleMatch.Groups[3].Value),
                    tupleMatch.Groups[4].Value.Trim(),
                    AutomataSymbols.NormalizeSymbol(tupleMatch.Groups[5].Value));
            }
            else
            {
                var arrowMatch = ArrowLineRegex.Match(line);
                if (!arrowMatch.Success)
                {
                    throw new FormatException($"Invalid PDA transition line: '{line}'. Expected '((from, read, pop), (to, push))' or 'from, read, pop -> to, push'.");
                }
                parsed = new PdaTransition(
                    arrowMatch.Groups[1].Value.Trim(),
                    AutomataSymbols.NormalizeSymbol(arrowMatch.Groups[2].Value),
                    AutomataSymbols.NormalizeSymbol(arrowMatch.Groups[3].Value),
                    arrowMatch.Groups[4].Value.Trim(),
                    AutomataSymbols.NormalizeSymbol(arrowMatch.Groups[5].Value));
            }

            firstState ??= parsed.FromState;
            states.Add(parsed.FromState);
            states.Add(parsed.ToState);

            if (!AutomataSymbols.IsEmptyToken(parsed.ReadInput))
            {
                foreach (var c in parsed.ReadInput) inputAlpha.Add(c);
            }
            if (!AutomataSymbols.IsEmptyToken(parsed.PopStack))
            {
                foreach (var c in parsed.PopStack) stackAlpha.Add(c.ToString());
            }
            if (!AutomataSymbols.IsEmptyToken(parsed.PushStack))
            {
                foreach (var c in parsed.PushStack) stackAlpha.Add(c.ToString());
            }

            transitions.Add(parsed);
        }

        if (transitions.Count == 0)
        {
            throw new FormatException("No PDA transitions were found.");
        }

        var resolvedStart = parsedStart ?? firstState!;
        states.Add(resolvedStart);

        var resolvedAccept = parsedAccept is { Count: > 0 }
            ? parsedAccept
            : states.Contains("f")
                ? new List<string> { "f" }
                : new List<string> { resolvedStart };

        foreach (var a in resolvedAccept)
        {
            states.Add(a);
        }

        return new PushdownAutomaton(states, inputAlpha, stackAlpha, transitions, resolvedStart, resolvedAccept);
    }

    public bool Accepts(string? input, int maxSteps = 5000, int maxStackDepth = 64)
    {
        return PdaTracer.Trace(this, input, maxSteps, maxStackDepth).Accepted;
    }

    public DiagramScene ToDiagramScene(
        string? activeState = null,
        PdaTransition? activeTransition = null,
        string? caption = null)
    {
        return PdaTracer.BuildDiagramScene(this, activeState, activeTransition, null, caption);
    }

    public string ToTransitionsText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"start: {StartState}");
        sb.AppendLine($"accept: {string.Join(", ", AcceptStates.OrderBy(x => x, StringComparer.Ordinal))}");
        foreach (var t in Transitions)
        {
            sb.AppendLine(t.ToTupleString());
        }
        return sb.ToString().TrimEnd();
    }
}
