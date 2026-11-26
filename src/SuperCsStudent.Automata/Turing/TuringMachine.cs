using System.Text;

namespace SuperCsStudent.Automata.Turing;

public enum TmActionKind
{
    Write,
    MoveLeft,
    MoveRight
}

public enum HeadStartMode
{
    /// <summary>Head starts on the rightmost delimiter blank '#' of #w#.</summary>
    RightBlank,
    /// <summary>Head starts on cell 0 (leftmost boundary blank '#') of #w#.</summary>
    LeftBlank,
    /// <summary>Head starts on cell 1 (first symbol of w, or rightmost '#' if w is empty).</summary>
    FirstSymbol
}

public enum TmHaltReason
{
    Running,
    EnteredHaltState,
    NoTransition,
    SteppedOffLeft,
    StepLimitExceeded
}

/// <summary>
/// Strict write-OR-move Turing machine action: either writes a single symbol to the tape
/// OR moves the head one cell left or right (never both in one primitive step).
/// </summary>
public readonly record struct TmAction(TmActionKind Kind, char WriteSymbol = '\0')
{
    public static TmAction Left => new(TmActionKind.MoveLeft);
    public static TmAction Right => new(TmActionKind.MoveRight);
    public static TmAction Write(char symbol) => new(TmActionKind.Write, symbol);

    public override string ToString() => Kind switch
    {
        TmActionKind.MoveLeft => "L",
        TmActionKind.MoveRight => "R",
        TmActionKind.Write => WriteSymbol.ToString(),
        _ => "?"
    };
}

public sealed record TmTransitionResult(string NextState, TmAction Action);

public sealed record TmMicroStep(
    int Step,
    string State,
    string Tape,
    int HeadIndex,
    string ConfigString,
    string TransitionText);

public sealed record TmExecutionResult(
    bool HaltedNormally,
    TmHaltReason HaltReason,
    string FinalState,
    string FinalTape,
    string TrimmedTape,
    string OutputContent,
    int FinalHeadIndex,
    bool? AcceptedDecision,
    IReadOnlyList<TmMicroStep> MicroSteps);

/// <summary>
/// Deterministic Turing Machine quadruple (States, TapeAlphabet, Transitions, StartState, BlankSymbol='#', HaltState="h")
/// with strict write-OR-move semantics on a semi-infinite tape #w#.
/// </summary>
public sealed class TuringMachine
{
    public IReadOnlySet<string> States { get; }
    public IReadOnlyList<string> OrderedStates { get; }
    public IReadOnlySet<char> TapeAlphabet { get; }
    public IReadOnlyList<char> OrderedTapeAlphabet { get; }
    public IReadOnlyDictionary<(string State, char Symbol), TmTransitionResult> Transitions { get; }
    public string StartState { get; }
    public char BlankSymbol { get; }
    public string HaltState { get; }
    public HeadStartMode DefaultHeadStart { get; }

    public TuringMachine(
        IEnumerable<string> states,
        IEnumerable<char> tapeAlphabet,
        IReadOnlyDictionary<(string State, char Symbol), TmTransitionResult> transitions,
        string startState,
        char blankSymbol = AutomataSymbols.DefaultBlank,
        string haltState = AutomataSymbols.DefaultHaltState,
        HeadStartMode defaultHeadStart = HeadStartMode.RightBlank)
    {
        var stateSet = new HashSet<string>(states, StringComparer.Ordinal) { haltState };
        if (!stateSet.Contains(startState))
        {
            throw new ArgumentException($"Start state '{startState}' must be in States.", nameof(startState));
        }

        var alphaSet = new HashSet<char>(tapeAlphabet) { blankSymbol };
        var orderedAlpha = alphaSet.OrderBy(c => c == blankSymbol ? 0 : 1).ThenBy(c => c).ToList();
        var orderedStates = stateSet
            .OrderBy(s => s == startState ? 0 : s == haltState ? 2 : 1)
            .ThenBy(s => s, NaturalStateComparer.Instance)
            .ToList();

        var transCopy = new Dictionary<(string State, char Symbol), TmTransitionResult>();
        foreach (var kv in transitions)
        {
            if (!stateSet.Contains(kv.Key.State))
            {
                throw new ArgumentException($"Transition source '{kv.Key.State}' not in States.", nameof(transitions));
            }
            if (!stateSet.Contains(kv.Value.NextState))
            {
                throw new ArgumentException($"Transition target '{kv.Value.NextState}' not in States.", nameof(transitions));
            }
            if (!alphaSet.Contains(kv.Key.Symbol))
            {
                throw new ArgumentException($"Transition read symbol '{kv.Key.Symbol}' not in TapeAlphabet.", nameof(transitions));
            }
            if (kv.Value.Action.Kind == TmActionKind.Write && !alphaSet.Contains(kv.Value.Action.WriteSymbol))
            {
                throw new ArgumentException($"Transition write symbol '{kv.Value.Action.WriteSymbol}' not in TapeAlphabet.", nameof(transitions));
            }
            transCopy[kv.Key] = kv.Value;
        }

        States = stateSet;
        OrderedStates = orderedStates;
        TapeAlphabet = alphaSet;
        OrderedTapeAlphabet = orderedAlpha;
        Transitions = transCopy;
        StartState = startState;
        BlankSymbol = blankSymbol;
        HaltState = haltState;
        DefaultHeadStart = defaultHeadStart;
    }

    public TmExecutionResult Run(
        string? input,
        int maxSteps = 20000,
        HeadStartMode? headStartOverride = null)
    {
        var mode = headStartOverride ?? DefaultHeadStart;
        var word = AutomataSymbols.NormalizeWord(input, TapeAlphabet);
        var initialTape = $"{BlankSymbol}{word}{BlankSymbol}";
        var head = mode switch
        {
            HeadStartMode.LeftBlank => 0,
            HeadStartMode.FirstSymbol => word.Length > 0 ? 1 : 1,
            _ => word.Length + 1
        };

        return RunOnRawTape(initialTape, head, maxSteps);
    }

    public TmExecutionResult RunOnRawTape(string rawTape, int initialHeadIndex, int maxSteps = 20000)
    {
        if (string.IsNullOrEmpty(rawTape))
        {
            rawTape = $"{BlankSymbol}{BlankSymbol}";
        }
        if (initialHeadIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialHeadIndex), "Initial head index must be non-negative.");
        }

        var tape = new List<char>(rawTape);
        while (tape.Count <= initialHeadIndex)
        {
            tape.Add(BlankSymbol);
        }

        var state = StartState;
        var head = initialHeadIndex;
        var steps = new List<TmMicroStep>();

        var initTapeStr = new string(tape.ToArray());
        steps.Add(new TmMicroStep(
            Step: 0,
            State: state,
            Tape: initTapeStr,
            HeadIndex: head,
            ConfigString: FormatConfig(state, tape, head),
            TransitionText: "Initial configuration"));

        var reason = TmHaltReason.Running;
        var stepCount = 0;

        while (state != HaltState && stepCount < maxSteps)
        {
            var scanned = tape[head];
            if (!Transitions.TryGetValue((state, scanned), out var tr))
            {
                reason = TmHaltReason.NoTransition;
                break;
            }

            var prevState = state;
            var transText = $"δ({prevState}, {scanned}) = ({tr.NextState}, {tr.Action})";

            switch (tr.Action.Kind)
            {
                case TmActionKind.Write:
                    tape[head] = tr.Action.WriteSymbol;
                    break;
                case TmActionKind.MoveLeft:
                    head--;
                    if (head < 0)
                    {
                        state = tr.NextState;
                        stepCount++;
                        reason = TmHaltReason.SteppedOffLeft;
                        steps.Add(new TmMicroStep(
                            Step: stepCount,
                            State: state,
                            Tape: new string(tape.ToArray()),
                            HeadIndex: -1,
                            ConfigString: $"({state}, <OFF-LEFT>{new string(tape.ToArray())})",
                            TransitionText: $"{transText} [Stepped left off cell 0]"));
                        goto Done;
                    }
                    break;
                case TmActionKind.MoveRight:
                    head++;
                    if (head >= tape.Count)
                    {
                        tape.Add(BlankSymbol);
                    }
                    break;
            }

            state = tr.NextState;
            stepCount++;

            steps.Add(new TmMicroStep(
                Step: stepCount,
                State: state,
                Tape: new string(tape.ToArray()),
                HeadIndex: head,
                ConfigString: FormatConfig(state, tape, head),
                TransitionText: transText));
        }

        if (state == HaltState)
        {
            reason = TmHaltReason.EnteredHaltState;
        }
        else if (reason == TmHaltReason.Running)
        {
            reason = TmHaltReason.StepLimitExceeded;
        }

    Done:
        var finalTape = new string(tape.ToArray());
        var trimmed = TrimTape(finalTape, System.Math.Max(0, head), BlankSymbol);
        var content = ExtractOutputContent(finalTape, BlankSymbol);
        bool? decision = reason == TmHaltReason.EnteredHaltState
            ? content == "y" ? true : content == "n" ? false : null
            : null;

        return new TmExecutionResult(
            HaltedNormally: reason == TmHaltReason.EnteredHaltState,
            HaltReason: reason,
            FinalState: state,
            FinalTape: finalTape,
            TrimmedTape: trimmed,
            OutputContent: content,
            FinalHeadIndex: head,
            AcceptedDecision: decision,
            MicroSteps: steps);
    }

    public static string FormatConfig(string state, IReadOnlyList<char> tape, int headIndex)
    {
        var sb = new StringBuilder(tape.Count + state.Length + 8);
        sb.Append('(').Append(state).Append(", ");
        for (var i = 0; i < tape.Count; i++)
        {
            if (i == headIndex)
            {
                sb.Append('[').Append(tape[i]).Append(']');
            }
            else
            {
                sb.Append(tape[i]);
            }
        }
        sb.Append(')');
        return sb.ToString();
    }

    public static string TrimTape(string tape, int headIndex, char blankSymbol = AutomataSymbols.DefaultBlank)
    {
        var lastNonBlank = -1;
        for (var i = tape.Length - 1; i >= 0; i--)
        {
            if (tape[i] != blankSymbol)
            {
                lastNonBlank = i;
                break;
            }
        }

        var keepEnd = System.Math.Max(headIndex, lastNonBlank + 1);
        keepEnd = System.Math.Max(keepEnd, 1);
        if (keepEnd >= tape.Length)
        {
            return tape + new string(blankSymbol, keepEnd - tape.Length + 1);
        }
        return tape[..(keepEnd + 1)];
    }

    public static string ExtractOutputContent(string tape, char blankSymbol = AutomataSymbols.DefaultBlank)
    {
        var start = 0;
        while (start < tape.Length && tape[start] == blankSymbol)
        {
            start++;
        }
        var end = tape.Length - 1;
        while (end >= start && tape[end] == blankSymbol)
        {
            end--;
        }
        return start > end ? string.Empty : tape[start..(end + 1)];
    }

    public string ToTransitionTableText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"States: {string.Join(", ", OrderedStates)}");
        sb.AppendLine($"TapeAlphabet: {string.Join(", ", OrderedTapeAlphabet)}");
        sb.AppendLine($"Start: {StartState} | Halt: {HaltState} | Blank: {BlankSymbol}");
        foreach (var q in OrderedStates)
        {
            if (q == HaltState) continue;
            foreach (var sym in OrderedTapeAlphabet)
            {
                if (Transitions.TryGetValue((q, sym), out var res))
                {
                    sb.AppendLine($"δ({q}, {sym}) = ({res.NextState}, {res.Action})");
                }
            }
        }
        return sb.ToString().TrimEnd();
    }

    private sealed class NaturalStateComparer : IComparer<string>
    {
        public static readonly NaturalStateComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (x is null || y is null) return StringComparer.Ordinal.Compare(x, y);
            if (x.StartsWith('q') && y.StartsWith('q') &&
                int.TryParse(x.AsSpan(1), out var nx) &&
                int.TryParse(y.AsSpan(1), out var ny))
            {
                return nx.CompareTo(ny);
            }
            return StringComparer.Ordinal.Compare(x, y);
        }
    }
}
