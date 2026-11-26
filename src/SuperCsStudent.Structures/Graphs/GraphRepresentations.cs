using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Graphs;

public sealed record EdgeListEntry(
    string EdgeId,
    string From,
    string To,
    double Weight,
    string Label,
    bool IsDirected);

public sealed record AdjacencyListRow(
    string Vertex,
    IReadOnlyList<string> Outgoing,
    IReadOnlyList<string> Incoming);

public sealed record AdjacencyMapRow(
    string Vertex,
    IReadOnlyDictionary<string, string> Entries);

public sealed record AdjacencyMatrixView(
    IReadOnlyList<string> Headers,
    IReadOnlyList<IReadOnlyList<string>> Cells);

public sealed record GraphRepresentations(
    IReadOnlyList<EdgeListEntry> EdgeList,
    IReadOnlyList<AdjacencyListRow> AdjacencyList,
    IReadOnlyList<AdjacencyMapRow> AdjacencyMap,
    AdjacencyMatrixView AdjacencyMatrix);

public static class GraphRepresentationBuilder
{
    public static GraphRepresentations Build(
        GraphModel graph,
        NeighborOrder order = NeighborOrder.AlphabeticalAsc)
    {
        var headers = graph.GetOrderedVertexIds(order);
        var indexByVertex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < headers.Count; i++)
        {
            indexByVertex[headers[i]] = i;
        }

        // 1. Edge List
        var edgeList = graph.Edges
            .Select(e => new EdgeListEntry(
                e.Id,
                e.From,
                e.To,
                e.Weight,
                string.IsNullOrEmpty(e.Label)
                    ? (graph.IsWeighted ? GraphModel.FormatWeight(e.Weight) : "1")
                    : e.Label,
                graph.IsDirected))
            .ToList();

        // 2. Adjacency List & 3. Adjacency Map
        var adjacencyList = new List<AdjacencyListRow>(headers.Count);
        var adjacencyMap = new List<AdjacencyMapRow>(headers.Count);

        foreach (var v in headers)
        {
            var outgoing = graph.GetOutgoingEdges(v, order);
            var incoming = graph.GetIncomingEdges(v, order);

            adjacencyList.Add(new AdjacencyListRow(
                v,
                outgoing.Select(x => graph.IsWeighted
                    ? $"{x.Neighbor} ({GraphModel.FormatWeight(x.Edge.Weight)})"
                    : x.Neighbor).ToList(),
                incoming.Select(x => graph.IsWeighted
                    ? $"{x.Neighbor} ({GraphModel.FormatWeight(x.Edge.Weight)})"
                    : x.Neighbor).ToList()));

            var mapEntries = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (neighbor, edge) in outgoing)
            {
                mapEntries[neighbor] = graph.IsWeighted
                    ? GraphModel.FormatWeight(edge.Weight)
                    : (string.IsNullOrEmpty(edge.Label) ? "1" : edge.Label);
            }
            adjacencyMap.Add(new AdjacencyMapRow(v, mapEntries));
        }

        // 4. Adjacency Matrix
        var emptyCell = graph.IsWeighted ? "∞" : "0";
        var grid = new string[headers.Count][];
        for (var r = 0; r < headers.Count; r++)
        {
            grid[r] = new string[headers.Count];
            for (var c = 0; c < headers.Count; c++)
            {
                grid[r][c] = emptyCell;
            }
        }

        foreach (var edge in graph.Edges)
        {
            if (!indexByVertex.TryGetValue(edge.From, out var r) ||
                !indexByVertex.TryGetValue(edge.To, out var c))
            {
                continue;
            }

            var cellValue = graph.IsWeighted ? GraphModel.FormatWeight(edge.Weight) : "1";
            grid[r][c] = cellValue;
            if (!graph.IsDirected)
            {
                grid[c][r] = cellValue;
            }
        }

        var cells = grid.Select(row => (IReadOnlyList<string>)row).ToList();
        var matrix = new AdjacencyMatrixView(headers, cells);

        return new GraphRepresentations(edgeList, adjacencyList, adjacencyMap, matrix);
    }
}
