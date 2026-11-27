namespace SuperCsStudent.Core.Diagram;

public static class SemanticStates
{
    public const string Default = "default";
    public const string Active = "active";
    public const string Visited = "visited";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string Frontier = "frontier";
    public const string Violation = "violation";
    public const string RedNode = "rb-red";
    public const string BlackNode = "rb-black";
    public const string DoubleBlack = "double-black";
}

public static class NodeShapes
{
    public const string Circle = "circle";
    public const string DoubleCircle = "double-circle";
    public const string Box = "box";
    public const string Pill = "pill";
    public const string Sentinel = "sentinel";
    public const string MultiKey = "multi-key";
    public const string SchemaNode = "schema-node";
}

public sealed record DiagramNode(
    string Id,
    string Label,
    double X,
    double Y,
    double Width = 48,
    double Height = 48,
    string Shape = NodeShapes.Circle,
    string SemanticState = SemanticStates.Default,
    string? SubLabel = null,
    string? Badge = null,
    bool IsStart = false,
    bool IsAccepting = false,
    bool IsDimmed = false,
    IReadOnlyList<string>? MultiKeys = null);

public sealed record DiagramEdge(
    string Id,
    string SourceId,
    string TargetId,
    string Label = "",
    bool IsDirected = true,
    string SemanticState = SemanticStates.Default,
    bool IsDashed = false,
    bool IsDimmed = false,
    double CurveOffset = 0);

public sealed record DiagramMotion(
    string FromNodeId,
    string ToNodeId,
    string Kind = "traverse",
    string? Color = null);

public sealed record DiagramScene(
    double ViewBoxWidth,
    double ViewBoxHeight,
    IReadOnlyList<DiagramNode> Nodes,
    IReadOnlyList<DiagramEdge> Edges,
    string? Caption = null,
    string? OverlayTag = null,
    string RoutingMode = "orthogonal",
    DiagramMotion? ActiveMotion = null);
