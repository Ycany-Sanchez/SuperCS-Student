using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Core.Tracing;

namespace SuperCsStudent.Automata.FiniteAutomata;

public sealed record FaStepState(
    string CurrentState,
    IReadOnlyList<string> ActiveStates,
    string ConsumedInput,
    string RemainingInput,
    string Configuration,
    bool? IsAccepted,
    DiagramScene Scene);

/// <summary>
/// Step-by-step deterministic and nondeterministic finite automaton execution tracer and diagram builder.
/// </summary>
public static class FaTracer
{
    private static readonly IReadOnlyList<string> DfaPseudocode = new[]
    {
        "q ← startState",
        "for each symbol a in w:",
        "    q ← δ(q, a)",
        "return q ∈ F"
    };

    private static readonly IReadOnlyList<string> NfaPseudocode = new[]
    {
        "S ← E(startState)",
        "for each symbol a in w:",
        "    S ← E(Move(S, a))",
        "return S ∩ F ≠ ∅"
    };

    public static TraceTimeline<FaStepState> Trace(Dfa dfa, string? input)
    {
        var word = AutomataSymbols.NormalizeWord(input, dfa.Alphabet);
        var steps = new List<TraceStep<FaStepState>>();
        var opLog = new List<string>();

        var current = dfa.StartState;
        var initialRem = AutomataSymbols.FormatWord(word, dfa.Alphabet);
        var initialConfig = $"({current}, {initialRem})";
        var initialScene = BuildDiagramScene(dfa, current, null, null, $"Config: {initialConfig}");

        steps.Add(new TraceStep<FaStepState>(
            StepIndex: 0,
            Title: "Initial Configuration",
            Explanation: $"Start in state {current} with remaining input '{initialRem}'.",
            ActivePseudocodeLine: 0,
            State: new FaStepState(current, new[] { current }, string.Empty, initialRem, initialConfig, null, initialScene),
            HighlightedNodeIds: new[] { current },
            HighlightedEdgeIds: Array.Empty<string>(),
            OperationCode: "INIT",
            BadgeLabel: initialConfig));
        opLog.Add(initialConfig);

        var haltedEarly = false;
        var consumed = string.Empty;

        for (var i = 0; i < word.Length; i++)
        {
            var sym = word[i];
            var prev = current;
            var next = dfa.Alphabet.Contains(sym) ? dfa.Step(prev, sym) : null;
            var nextConsumed = consumed + sym;
            var nextRemRaw = word[(i + 1)..];
            var nextRem = AutomataSymbols.FormatWord(nextRemRaw, dfa.Alphabet);

            if (next is null)
            {
                haltedEarly = true;
                var rejConfig = $"({prev}, {AutomataSymbols.FormatWord(word[i..], dfa.Alphabet)})";
                var rejScene = BuildDiagramScene(dfa, prev, null, false, $"No transition δ({prev}, {sym}) — Rejected");
                steps.Add(new TraceStep<FaStepState>(
                    StepIndex: steps.Count,
                    Title: $"Missing Transition on '{sym}'",
                    Explanation: $"No transition defined for δ({prev}, {sym}). Input rejected.",
                    ActivePseudocodeLine: 2,
                    State: new FaStepState(prev, new[] { prev }, consumed, AutomataSymbols.FormatWord(word[i..], dfa.Alphabet), rejConfig, false, rejScene),
                    HighlightedNodeIds: new[] { prev },
                    HighlightedEdgeIds: Array.Empty<string>(),
                    OperationCode: "REJECT",
                    BadgeLabel: "Rejected"));
                opLog.Add($"No δ({prev}, {sym}) -> Reject");
                break;
            }

            current = next;
            consumed = nextConsumed;
            var config = $"({current}, {nextRem})";
            var edgeId = EdgeId(prev, current);
            var scene = BuildDiagramScene(dfa, current, (prev, sym, current), null, $"δ({prev}, {sym}) = {current} | {config}");

            steps.Add(new TraceStep<FaStepState>(
                StepIndex: steps.Count,
                Title: $"Read '{sym}': {prev} → {current}",
                Explanation: $"Consumed '{sym}' via δ({prev}, {sym}) = {current}. Configuration is {config}.",
                ActivePseudocodeLine: 2,
                State: new FaStepState(current, new[] { current }, consumed, nextRem, config, null, scene),
                HighlightedNodeIds: new[] { current },
                HighlightedEdgeIds: new[] { edgeId },
                OperationCode: "STEP",
                BadgeLabel: config));
            opLog.Add($"({prev}, {AutomataSymbols.FormatWord(word[i..], dfa.Alphabet)}) ⊢ {config}");
        }

        if (!haltedEarly)
        {
            var accepted = dfa.AcceptStates.Contains(current);
            var finalConfig = $"({current}, {AutomataSymbols.Empty})";
            var verdict = accepted ? "Accepted" : "Rejected";
            var finalScene = BuildDiagramScene(dfa, current, null, accepted, $"{finalConfig} — {verdict}");
            var explanation = accepted
                ? $"Input exhausted in accepting state {current} ∈ F. String accepted."
                : $"Input exhausted in non-accepting state {current} ∉ F. String rejected.";

            steps.Add(new TraceStep<FaStepState>(
                StepIndex: steps.Count,
                Title: accepted ? $"Accepted in {current}" : $"Rejected in {current}",
                Explanation: explanation,
                ActivePseudocodeLine: 3,
                State: new FaStepState(current, new[] { current }, consumed, AutomataSymbols.Empty, finalConfig, accepted, finalScene),
                HighlightedNodeIds: new[] { current },
                HighlightedEdgeIds: Array.Empty<string>(),
                OperationCode: accepted ? "ACCEPT" : "REJECT",
                BadgeLabel: verdict));
            opLog.Add($"{finalConfig} -> {verdict}");

            return new TraceTimeline<FaStepState>(steps, DfaPseudocode, opLog, $"{verdict}: {finalConfig}", accepted);
        }

        return new TraceTimeline<FaStepState>(steps, DfaPseudocode, opLog, $"Rejected on input '{AutomataSymbols.FormatWord(word, dfa.Alphabet)}'", false);
    }

    public static TraceTimeline<FaStepState> Trace(Nfa nfa, string? input)
    {
        var word = AutomataSymbols.NormalizeWord(input, nfa.Alphabet);
        var steps = new List<TraceStep<FaStepState>>();
        var opLog = new List<string>();

        var active = nfa.EpsilonClosure(nfa.StartState);
        var activeList = active.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var setLabel = "{" + string.Join(", ", activeList) + "}";
        var initialRem = AutomataSymbols.FormatWord(word, nfa.Alphabet);
        var initialConfig = $"({setLabel}, {initialRem})";
        var initialScene = BuildDiagramScene(nfa, active, null, null, $"Config: {initialConfig}");

        steps.Add(new TraceStep<FaStepState>(
            StepIndex: 0,
            Title: "Initial Epsilon Closure",
            Explanation: $"Start with E({nfa.StartState}) = {setLabel} and remaining input '{initialRem}'.",
            ActivePseudocodeLine: 0,
            State: new FaStepState(setLabel, activeList, string.Empty, initialRem, initialConfig, null, initialScene),
            HighlightedNodeIds: activeList,
            HighlightedEdgeIds: Array.Empty<string>(),
            OperationCode: "INIT",
            BadgeLabel: initialConfig));
        opLog.Add(initialConfig);

        var consumed = string.Empty;
        for (var i = 0; i < word.Length; i++)
        {
            var sym = word[i];
            var nextConsumed = consumed + sym;
            var nextRem = AutomataSymbols.FormatWord(word[(i + 1)..], nfa.Alphabet);

            var usedTransitions = new HashSet<NfaTransition>();
            IReadOnlySet<string> nextActive;
            if (!nfa.Alphabet.Contains(sym))
            {
                nextActive = new HashSet<string>(StringComparer.Ordinal);
            }
            else
            {
                var moved = nfa.Move(active, sym);
                var symStr = sym.ToString();
                foreach (var t in nfa.Transitions)
                {
                    if (active.Contains(t.FromState) && t.Symbol == symStr && moved.Contains(t.ToState))
                    {
                        usedTransitions.Add(t);
                    }
                }
                nextActive = nfa.EpsilonClosure(moved);
            }

            active = nextActive;
            activeList = active.OrderBy(s => s, StringComparer.Ordinal).ToList();
            setLabel = activeList.Count == 0 ? "∅" : "{" + string.Join(", ", activeList) + "}";
            consumed = nextConsumed;

            var config = $"({setLabel}, {nextRem})";
            var edgeIds = usedTransitions.Select(t => EdgeId(t.FromState, t.ToState)).Distinct().ToList();
            var scene = BuildDiagramScene(nfa, active, usedTransitions, null, $"Read '{sym}' | {config}");

            steps.Add(new TraceStep<FaStepState>(
                StepIndex: steps.Count,
                Title: $"Read '{sym}' → {setLabel}",
                Explanation: $"Active states after reading '{sym}' and taking ε-closure: {setLabel}.",
                ActivePseudocodeLine: 2,
                State: new FaStepState(setLabel, activeList, consumed, nextRem, config, null, scene),
                HighlightedNodeIds: activeList,
                HighlightedEdgeIds: edgeIds,
                OperationCode: "STEP",
                BadgeLabel: config));
            opLog.Add($"Read '{sym}' -> {config}");
        }

        var accepted = active.Any(s => nfa.AcceptStates.Contains(s));
        var finalConfig = $"({setLabel}, {AutomataSymbols.Empty})";
        var verdict = accepted ? "Accepted" : "Rejected";
        var finalScene = BuildDiagramScene(nfa, active, null, accepted, $"{finalConfig} — {verdict}");

        steps.Add(new TraceStep<FaStepState>(
            StepIndex: steps.Count,
            Title: accepted ? "Accepted" : "Rejected",
            Explanation: accepted
                ? $"Active set {setLabel} intersects F. String accepted."
                : $"Active set {setLabel} contains no accepting state. String rejected.",
            ActivePseudocodeLine: 3,
            State: new FaStepState(setLabel, activeList, consumed, AutomataSymbols.Empty, finalConfig, accepted, finalScene),
            HighlightedNodeIds: activeList,
            HighlightedEdgeIds: Array.Empty<string>(),
            OperationCode: accepted ? "ACCEPT" : "REJECT",
            BadgeLabel: verdict));
        opLog.Add($"{finalConfig} -> {verdict}");

        return new TraceTimeline<FaStepState>(steps, NfaPseudocode, opLog, $"{verdict}: {finalConfig}", accepted);
    }

    public static DiagramScene BuildDiagramScene(
        Dfa dfa,
        string? activeState = null,
        (string From, char Symbol, string To)? activeTransition = null,
        bool? verdict = null,
        string? caption = null)
    {
        var nodes = dfa.OrderedStates.Select(q =>
        {
            var isAccept = dfa.AcceptStates.Contains(q);
            var isStart = q == dfa.StartState;
            var sem = SemanticStates.Default;
            if (q == activeState)
            {
                sem = verdict switch
                {
                    true => SemanticStates.Accepted,
                    false => SemanticStates.Rejected,
                    null => SemanticStates.Active
                };
            }

            return new GraphNodeInput(
                Id: q,
                Label: q,
                Shape: isAccept ? NodeShapes.DoubleCircle : NodeShapes.Circle,
                SemanticState: sem,
                Badge: isStart ? ">" : null,
                IsStart: isStart,
                IsAccepting: isAccept);
        }).ToList();

        var groupedEdges = dfa.Transitions
            .GroupBy(kv => (kv.Key.State, kv.Value))
            .OrderBy(g => g.Key.State, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Value, StringComparer.Ordinal)
            .Select(g =>
            {
                var syms = g.Select(kv => kv.Key.Symbol).OrderBy(c => c).ToList();
                var isActive = activeTransition.HasValue
                    && activeTransition.Value.From == g.Key.State
                    && activeTransition.Value.To == g.Key.Value
                    && syms.Contains(activeTransition.Value.Symbol);
                return new GraphEdgeInput(
                    Id: EdgeId(g.Key.State, g.Key.Value),
                    SourceId: g.Key.State,
                    TargetId: g.Key.Value,
                    Label: string.Join(", ", syms),
                    IsDirected: true,
                    SemanticState: isActive ? SemanticStates.Active : SemanticStates.Default);
            })
            .ToList();

        return DiagramLayout.LayoutStateGraph(nodes, groupedEdges, dfa.StartState, caption: caption, overlayTag: "DFA");
    }

    public static DiagramScene BuildDiagramScene(
        Nfa nfa,
        IReadOnlySet<string>? activeStates = null,
        IReadOnlySet<NfaTransition>? activeTransitions = null,
        bool? verdict = null,
        string? caption = null)
    {
        var nodes = nfa.OrderedStates.Select(q =>
        {
            var isAccept = nfa.AcceptStates.Contains(q);
            var isStart = q == nfa.StartState;
            var isActive = activeStates is not null && activeStates.Contains(q);
            var sem = SemanticStates.Default;
            if (isActive)
            {
                sem = verdict switch
                {
                    true => isAccept ? SemanticStates.Accepted : SemanticStates.Visited,
                    false => SemanticStates.Rejected,
                    null => SemanticStates.Active
                };
            }

            return new GraphNodeInput(
                Id: q,
                Label: q,
                Shape: isAccept ? NodeShapes.DoubleCircle : NodeShapes.Circle,
                SemanticState: sem,
                Badge: isStart ? ">" : null,
                IsStart: isStart,
                IsAccepting: isAccept);
        }).ToList();

        var groupedEdges = nfa.Transitions
            .GroupBy(t => (t.FromState, t.ToState))
            .OrderBy(g => g.Key.FromState, StringComparer.Ordinal)
            .ThenBy(g => g.Key.ToState, StringComparer.Ordinal)
            .Select(g =>
            {
                var syms = g.Select(t => t.Symbol).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal).ToList();
                var isActive = activeTransitions is not null && g.Any(t => activeTransitions.Contains(t));
                var allEpsilon = syms.All(s => s == AutomataSymbols.Empty);
                return new GraphEdgeInput(
                    Id: EdgeId(g.Key.FromState, g.Key.ToState),
                    SourceId: g.Key.FromState,
                    TargetId: g.Key.ToState,
                    Label: string.Join(", ", syms),
                    IsDirected: true,
                    SemanticState: isActive ? SemanticStates.Active : SemanticStates.Default,
                    IsDashed: allEpsilon);
            })
            .ToList();

        return DiagramLayout.LayoutStateGraph(nodes, groupedEdges, nfa.StartState, caption: caption, overlayTag: "NFA");
    }

    private static string EdgeId(string from, string to) => $"{from}->{to}";
}
