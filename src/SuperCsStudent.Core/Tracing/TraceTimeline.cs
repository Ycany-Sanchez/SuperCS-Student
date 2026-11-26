namespace SuperCsStudent.Core.Tracing;

public sealed record TraceStep<TState>(
    int StepIndex,
    string Title,
    string Explanation,
    int? ActivePseudocodeLine,
    TState State,
    IReadOnlyList<string> HighlightedNodeIds,
    IReadOnlyList<string> HighlightedEdgeIds,
    string? OperationCode = null,
    string? BadgeLabel = null);

public sealed class TraceTimeline<TState>
{
    public IReadOnlyList<TraceStep<TState>> Steps { get; }
    public IReadOnlyList<string> PseudocodeLines { get; }
    public IReadOnlyList<string> OperationLog { get; }
    public string Summary { get; }
    public bool IsSuccess { get; }

    public TraceTimeline(
        IReadOnlyList<TraceStep<TState>> steps,
        IReadOnlyList<string>? pseudocodeLines = null,
        IReadOnlyList<string>? operationLog = null,
        string summary = "",
        bool isSuccess = true)
    {
        Steps = steps;
        PseudocodeLines = pseudocodeLines ?? Array.Empty<string>();
        OperationLog = operationLog ?? Array.Empty<string>();
        Summary = summary;
        IsSuccess = isSuccess;
    }

    public int Count => Steps.Count;

    public TraceStep<TState>? StepAt(int index)
    {
        if (Steps.Count == 0) return null;
        var clamped = System.Math.Clamp(index, 0, Steps.Count - 1);
        return Steps[clamped];
    }
}
