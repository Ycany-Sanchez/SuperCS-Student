using System.Globalization;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Graphs;

public sealed record GraphVertex(string Id, string Label);

public sealed record GraphEdge(
    string Id,
    string From,
    string To,
    double Weight = 1.0,
    string Label = "");

public sealed class GraphModel
{
    private readonly List<GraphVertex> _vertices = new();
    private readonly Dictionary<string, GraphVertex> _vertexMap = new(StringComparer.Ordinal);
    private readonly List<GraphEdge> _edges = new();
    private int _nextEdgeId = 1;

    public bool IsDirected { get; set; }
    public bool IsWeighted { get; set; }
    public IReadOnlyList<GraphVertex> Vertices => _vertices;
    public IReadOnlyList<GraphEdge> Edges => _edges;

    public GraphModel(bool isDirected = false, bool isWeighted = false)
    {
        IsDirected = isDirected;
        IsWeighted = isWeighted;
    }

    public bool ContainsVertex(string id) => _vertexMap.ContainsKey(id.Trim());

    public GraphVertex AddVertex(string id, string? label = null)
    {
        var cleanId = id.Trim();
        if (string.IsNullOrEmpty(cleanId))
        {
            throw new ArgumentException("Vertex id cannot be empty.", nameof(id));
        }

        if (_vertexMap.TryGetValue(cleanId, out var existing))
        {
            return existing;
        }

        var vertex = new GraphVertex(cleanId, string.IsNullOrWhiteSpace(label) ? cleanId : label.Trim());
        _vertices.Add(vertex);
        _vertexMap[cleanId] = vertex;
        return vertex;
    }

    public GraphEdge AddEdge(string from, string to, double weight = 1.0, string? label = null)
    {
        var u = AddVertex(from).Id;
        var v = AddVertex(to).Id;
        var effectiveLabel = label ?? (IsWeighted ? FormatWeight(weight) : "");

        var existingIdx = _edges.FindIndex(e => MatchesEndpoints(e, u, v));
        if (existingIdx >= 0)
        {
            var updated = _edges[existingIdx] with { Weight = weight, Label = effectiveLabel };
            _edges[existingIdx] = updated;
            return updated;
        }

        var edge = new GraphEdge($"e_{_nextEdgeId++}", u, v, weight, effectiveLabel);
        _edges.Add(edge);
        return edge;
    }

    public bool RemoveVertex(string id)
    {
        var cleanId = id.Trim();
        if (!_vertexMap.Remove(cleanId)) return false;
        _vertices.RemoveAll(v => string.Equals(v.Id, cleanId, StringComparison.Ordinal));
        _edges.RemoveAll(e => string.Equals(e.From, cleanId, StringComparison.Ordinal) ||
                              string.Equals(e.To, cleanId, StringComparison.Ordinal));
        return true;
    }

    public bool RemoveEdge(string from, string to)
    {
        var u = from.Trim();
        var v = to.Trim();
        var removed = _edges.RemoveAll(e => MatchesEndpoints(e, u, v));
        return removed > 0;
    }

    public bool RemoveEdgeById(string edgeId) =>
        _edges.RemoveAll(e => string.Equals(e.Id, edgeId, StringComparison.Ordinal)) > 0;

    public IReadOnlyList<string> GetOrderedVertexIds(NeighborOrder order) => order switch
    {
        NeighborOrder.AlphabeticalAsc => _vertices.Select(v => v.Id).OrderBy(id => id, StringComparer.Ordinal).ToList(),
        NeighborOrder.AlphabeticalDesc => _vertices.Select(v => v.Id).OrderByDescending(id => id, StringComparer.Ordinal).ToList(),
        NeighborOrder.InsertionOrder => _vertices.Select(v => v.Id).ToList(),
        _ => _vertices.Select(v => v.Id).OrderBy(id => id, StringComparer.Ordinal).ToList()
    };

    public IReadOnlyList<(string Neighbor, GraphEdge Edge)> GetOutgoingEdges(string vertexId, NeighborOrder order)
    {
        var list = new List<(string Neighbor, GraphEdge Edge)>();
        foreach (var e in _edges)
        {
            if (string.Equals(e.From, vertexId, StringComparison.Ordinal))
            {
                list.Add((e.To, e));
            }
            else if (!IsDirected && string.Equals(e.To, vertexId, StringComparison.Ordinal))
            {
                list.Add((e.From, e));
            }
        }

        return SortNeighbors(list, order);
    }

    public IReadOnlyList<(string Neighbor, GraphEdge Edge)> GetIncomingEdges(string vertexId, NeighborOrder order)
    {
        if (!IsDirected)
        {
            return GetOutgoingEdges(vertexId, order);
        }

        var list = new List<(string Neighbor, GraphEdge Edge)>();
        foreach (var e in _edges)
        {
            if (string.Equals(e.To, vertexId, StringComparison.Ordinal))
            {
                list.Add((e.From, e));
            }
        }

        return SortNeighbors(list, order);
    }

    public GraphModel Clone()
    {
        var copy = new GraphModel(IsDirected, IsWeighted);
        foreach (var v in _vertices)
        {
            copy.AddVertex(v.Id, v.Label);
        }
        foreach (var e in _edges)
        {
            copy._edges.Add(e);
        }
        copy._nextEdgeId = _nextEdgeId;
        return copy;
    }

    /// <summary>
    /// Parses edge lists such as "A B 4", "DVO -> CEB", "PVD -- JFK 144", or single isolated vertex lines ("SUG").
    /// </summary>
    public static GraphModel ParseEdgeList(string text, bool isDirected = false, bool isWeighted = false)
    {
        var graph = new GraphModel(isDirected, isWeighted);
        if (string.IsNullOrWhiteSpace(text))
        {
            return graph;
        }

        var lines = text.Split(new[] { '\n', '\r', ';' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine;
            var hashIdx = line.IndexOf('#');
            if (hashIdx >= 0) line = line[..hashIdx];
            var slashIdx = line.IndexOf("//", StringComparison.Ordinal);
            if (slashIdx >= 0) line = line[..slashIdx];

            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // Normalize arrow/undirected edge separators into whitespace without breaking negative numbers
            var normalized = line
                .Replace("->", " ")
                .Replace("<->", " ")
                .Replace("--", " ")
                .Replace(",", " ")
                .Replace(":", " ");

            var rawTokens = normalized.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            var tokens = rawTokens.Where(t => t != "-").ToArray();

            if (tokens.Length == 0) continue;

            if (tokens.Length == 1)
            {
                graph.AddVertex(tokens[0]);
                continue;
            }

            var from = tokens[0];
            var to = tokens[1];
            var weight = 1.0;

            if (tokens.Length >= 3 &&
                double.TryParse(tokens[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedWeight))
            {
                weight = parsedWeight;
            }

            graph.AddEdge(from, to, weight);
        }

        return graph;
    }

    public static string FormatWeight(double weight)
    {
        if (double.IsPositiveInfinity(weight)) return "∞";
        if (double.IsNegativeInfinity(weight)) return "-∞";
        if (Math.Abs(weight - Math.Round(weight)) < 1e-9)
        {
            return Math.Round(weight).ToString(CultureInfo.InvariantCulture);
        }
        return weight.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private bool MatchesEndpoints(GraphEdge e, string u, string v)
    {
        if (string.Equals(e.From, u, StringComparison.Ordinal) &&
            string.Equals(e.To, v, StringComparison.Ordinal))
        {
            return true;
        }

        if (!IsDirected &&
            string.Equals(e.From, v, StringComparison.Ordinal) &&
            string.Equals(e.To, u, StringComparison.Ordinal))
        {
            return true;
        }

        return false;
    }

    private static IReadOnlyList<(string Neighbor, GraphEdge Edge)> SortNeighbors(
        List<(string Neighbor, GraphEdge Edge)> list,
        NeighborOrder order) => order switch
    {
        NeighborOrder.AlphabeticalAsc => list.OrderBy(x => x.Neighbor, StringComparer.Ordinal).ToList(),
        NeighborOrder.AlphabeticalDesc => list.OrderByDescending(x => x.Neighbor, StringComparer.Ordinal).ToList(),
        NeighborOrder.InsertionOrder => list,
        _ => list.OrderBy(x => x.Neighbor, StringComparer.Ordinal).ToList()
    };
}
