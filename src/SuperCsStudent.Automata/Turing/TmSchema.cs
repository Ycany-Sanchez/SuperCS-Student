using System.Text;

namespace SuperCsStudent.Automata.Turing;

/// <summary>
/// A node in a composable Turing machine schema, executing one or more chained
/// primitive or macro operations (e.g., "L_# R", "# R_# S_L", "R_# E Y R", "E N R").
/// </summary>
public sealed record TmSchemaNode(
    string Id,
    string Label,
    IReadOnlyList<string> Operations)
{
    public static TmSchemaNode Create(string id, string operationsText)
    {
        var ops = ParseOperationTokens(operationsText);
        if (ops.Count == 0)
        {
            throw new ArgumentException($"Schema node '{id}' must specify at least one operation.", nameof(operationsText));
        }
        var normalizedLabel = string.Join(" ", ops);
        return new TmSchemaNode(id.Trim(), normalizedLabel, ops);
    }

    public static IReadOnlyList<string> ParseOperationTokens(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }
        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .ToArray();
    }
}

/// <summary>
/// Labeled routing arrow in a Turing machine schema matching the currently scanned tape symbol.
/// Supports single symbols ("a", "#"), comma-separated lists ("a, b"), negation ("~#", "!a"),
/// fallback ("other"), or wildcard ("*" / "").
/// </summary>
public sealed record TmSchemaEdge(
    string FromNodeId,
    string ToNodeId,
    string Condition = "*");

/// <summary>
/// Composable machine-schema graph for high-level Turing machine design.
/// </summary>
public sealed class TmSchema
{
    public string Name { get; }
    public IReadOnlyList<TmSchemaNode> Nodes { get; }
    public IReadOnlyList<TmSchemaEdge> Edges { get; }
    public string StartNodeId { get; }
    public IReadOnlySet<char> TapeAlphabet { get; }
    public char BlankSymbol { get; }
    public HeadStartMode HeadStart { get; }

    public TmSchema(
        string name,
        IEnumerable<TmSchemaNode> nodes,
        IEnumerable<TmSchemaEdge> edges,
        string startNodeId,
        IEnumerable<char>? tapeAlphabet = null,
        char blankSymbol = AutomataSymbols.DefaultBlank,
        HeadStartMode headStart = HeadStartMode.RightBlank)
    {
        var nodeList = nodes.ToList();
        var nodeIds = new HashSet<string>(nodeList.Select(n => n.Id), StringComparer.Ordinal);
        if (!nodeIds.Contains(startNodeId))
        {
            throw new ArgumentException($"Start node '{startNodeId}' must exist in Nodes.", nameof(startNodeId));
        }

        var edgeList = edges.ToList();
        foreach (var e in edgeList)
        {
            if (!nodeIds.Contains(e.FromNodeId))
            {
                throw new ArgumentException($"Edge source '{e.FromNodeId}' not in Nodes.", nameof(edges));
            }
            if (!nodeIds.Contains(e.ToNodeId))
            {
                throw new ArgumentException($"Edge target '{e.ToNodeId}' not in Nodes.", nameof(edges));
            }
        }

        var alpha = new SortedSet<char> { blankSymbol };
        if (tapeAlphabet is not null)
        {
            foreach (var c in tapeAlphabet)
            {
                alpha.Add(c);
            }
        }

        // Automatically include symbols referenced by operations and edge labels
        foreach (var node in nodeList)
        {
            foreach (var op in node.Operations)
            {
                CollectOperationSymbols(op, alpha);
            }
        }

        foreach (var e in edgeList)
        {
            CollectConditionSymbols(e.Condition, alpha);
        }

        Name = name;
        Nodes = nodeList;
        Edges = edgeList;
        StartNodeId = startNodeId;
        TapeAlphabet = alpha;
        BlankSymbol = blankSymbol;
        HeadStart = headStart;
    }

    private static void CollectOperationSymbols(string op, SortedSet<char> alpha)
    {
        if (op is "L" or "R" or "E" or "C" or "S_L" or "S_R")
        {
            return;
        }
        if (op == "Y")
        {
            alpha.Add('y');
            return;
        }
        if (op == "N")
        {
            alpha.Add('n');
            return;
        }
        if ((op.StartsWith("L_", StringComparison.Ordinal) || op.StartsWith("R_", StringComparison.Ordinal)) && op.Length == 3)
        {
            alpha.Add(op[2]);
            return;
        }
        if (op.Length == 3 && op[0] == '\'' && op[2] == '\'')
        {
            alpha.Add(op[1]);
            return;
        }
        if (op.StartsWith("W:", StringComparison.OrdinalIgnoreCase) && op.Length == 3)
        {
            alpha.Add(op[2]);
            return;
        }
        if (op.Length == 1)
        {
            alpha.Add(op[0]);
        }
    }

    private static void CollectConditionSymbols(string condition, SortedSet<char> alpha)
    {
        var trimmed = condition.Trim();
        if (trimmed.Length == 0 || trimmed == "*" ||
            trimmed.Equals("other", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("else", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if ((trimmed.StartsWith('~') || trimmed.StartsWith('!')) && trimmed.Length == 2)
        {
            alpha.Add(trimmed[1]);
            return;
        }

        if (trimmed.StartsWith("not ", StringComparison.OrdinalIgnoreCase) && trimmed.Length == 5)
        {
            alpha.Add(trimmed[4]);
            return;
        }

        foreach (var part in trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Trim();
            if (p.Length == 1)
            {
                alpha.Add(p[0]);
            }
            else if (p.Length == 3 && p[0] == '\'' && p[2] == '\'')
            {
                alpha.Add(p[1]);
            }
        }
    }

    public string ToDslText()
    {
        var sb = new StringBuilder();
        var nonBlankAlpha = TapeAlphabet.Where(c => c != BlankSymbol).OrderBy(c => c).ToList();
        if (nonBlankAlpha.Count > 0)
        {
            sb.AppendLine($"alphabet: {string.Join(", ", nonBlankAlpha)}");
        }
        if (HeadStart != HeadStartMode.RightBlank)
        {
            sb.AppendLine($"head: {HeadStart}");
        }
        foreach (var node in Nodes)
        {
            var prefix = node.Id == StartNodeId ? "> " : string.Empty;
            sb.AppendLine($"{prefix}{node.Id}: {node.Label}");
        }
        foreach (var edge in Edges)
        {
            var cond = string.IsNullOrWhiteSpace(edge.Condition) ? "*" : edge.Condition.Trim();
            sb.AppendLine(cond == "*"
                ? $"{edge.FromNodeId} -> {edge.ToNodeId}"
                : $"{edge.FromNodeId} -> {edge.ToNodeId} : {cond}");
        }
        return sb.ToString().TrimEnd();
    }
}

/// <summary>
/// Fluent builder for constructing Turing machine schemas concisely in C#.
/// </summary>
public sealed class TmSchemaBuilder
{
    private readonly string _name;
    private readonly List<TmSchemaNode> _nodes = new();
    private readonly List<TmSchemaEdge> _edges = new();
    private readonly SortedSet<char> _alphabet = new();
    private string? _startNodeId;
    private char _blank = AutomataSymbols.DefaultBlank;
    private HeadStartMode _headStart = HeadStartMode.RightBlank;

    public TmSchemaBuilder(string name)
    {
        _name = name;
    }

    public TmSchemaBuilder Alphabet(params char[] symbols)
    {
        foreach (var s in symbols) _alphabet.Add(s);
        return this;
    }

    public TmSchemaBuilder Alphabet(IEnumerable<char> symbols)
    {
        foreach (var s in symbols) _alphabet.Add(s);
        return this;
    }

    public TmSchemaBuilder Blank(char blank)
    {
        _blank = blank;
        return this;
    }

    public TmSchemaBuilder Head(HeadStartMode mode)
    {
        _headStart = mode;
        return this;
    }

    public TmSchemaBuilder StartNode(string id, string operations)
    {
        _startNodeId = id;
        return Node(id, operations);
    }

    public TmSchemaBuilder Node(string id, string operations)
    {
        _nodes.Add(TmSchemaNode.Create(id, operations));
        _startNodeId ??= id;
        return this;
    }

    public TmSchemaBuilder Edge(string fromId, string toId, string condition = "*")
    {
        _edges.Add(new TmSchemaEdge(fromId, toId, condition));
        return this;
    }

    public TmSchema Build()
    {
        if (_startNodeId is null)
        {
            throw new InvalidOperationException("Schema must contain at least one node.");
        }
        return new TmSchema(_name, _nodes, _edges, _startNodeId, _alphabet, _blank, _headStart);
    }
}
