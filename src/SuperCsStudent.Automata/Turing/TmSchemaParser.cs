using System.Text.RegularExpressions;

namespace SuperCsStudent.Automata.Turing;

/// <summary>
/// Parses a concise text DSL for Turing machine schemas:
///   alphabet: a, b
///   > n0: L_# R
///   n1: # R_# S_L
///   n2: L
///   n0 -> n1 : a
///   n1 -> n2
///   n2 --#--> n3
/// </summary>
public static class TmSchemaParser
{
    private static readonly Regex InlineLabeledArrowRegex = new(
        @"^([A-Za-z0-9_]+)\s*--\s*(.+?)\s*-->\s*([A-Za-z0-9_]+)$",
        RegexOptions.Compiled);

    private static readonly Regex ArrowWithColonRegex = new(
        @"^([A-Za-z0-9_]+)\s*(?:->|→)\s*([A-Za-z0-9_]+)(?:\s*:\s*(.*))?$",
        RegexOptions.Compiled);

    public static TmSchema Parse(string dslText, string name = "Custom TM Schema")
    {
        if (string.IsNullOrWhiteSpace(dslText))
        {
            throw new FormatException("TM schema text cannot be empty.");
        }

        var lines = dslText
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith("//", StringComparison.Ordinal))
            .ToList();

        var nodes = new List<TmSchemaNode>();
        var edges = new List<TmSchemaEdge>();
        var alphabet = new SortedSet<char>();
        string? startNodeId = null;
        var blank = AutomataSymbols.DefaultBlank;
        var headStart = HeadStartMode.RightBlank;

        foreach (var line in lines)
        {
            if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                name = line["name:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("alphabet:", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line["alphabet:".Length..];
                foreach (var token in rest.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var t = token.Trim();
                    if (t.Length == 1) alphabet.Add(t[0]);
                }
                continue;
            }
            if (line.StartsWith("start:", StringComparison.OrdinalIgnoreCase))
            {
                startNodeId = line["start:".Length..].Trim();
                continue;
            }
            if (line.StartsWith("blank:", StringComparison.OrdinalIgnoreCase))
            {
                var b = line["blank:".Length..].Trim();
                if (b.Length == 1) blank = b[0];
                continue;
            }
            if (line.StartsWith("head:", StringComparison.OrdinalIgnoreCase))
            {
                var h = line["head:".Length..].Trim();
                if (Enum.TryParse<HeadStartMode>(h, ignoreCase: true, out var parsedMode))
                {
                    headStart = parsedMode;
                }
                continue;
            }

            // Edge form 1: from --label--> to
            var inlineMatch = InlineLabeledArrowRegex.Match(line);
            if (inlineMatch.Success)
            {
                edges.Add(new TmSchemaEdge(
                    inlineMatch.Groups[1].Value.Trim(),
                    inlineMatch.Groups[3].Value.Trim(),
                    inlineMatch.Groups[2].Value.Trim()));
                continue;
            }

            // Edge form 2: from -> to [: label]
            var arrowMatch = ArrowWithColonRegex.Match(line);
            if (arrowMatch.Success)
            {
                var cond = arrowMatch.Groups[3].Success ? arrowMatch.Groups[3].Value.Trim() : "*";
                if (cond.Length == 0) cond = "*";
                edges.Add(new TmSchemaEdge(
                    arrowMatch.Groups[1].Value.Trim(),
                    arrowMatch.Groups[2].Value.Trim(),
                    cond));
                continue;
            }

            // Node form: [>] id: operations
            var isStart = false;
            var nodeLine = line;
            if (nodeLine.StartsWith('>'))
            {
                isStart = true;
                nodeLine = nodeLine[1..].Trim();
            }

            var colonIdx = nodeLine.IndexOf(':');
            if (colonIdx <= 0)
            {
                throw new FormatException($"Invalid TM schema line: '{line}'. Expected 'id: ops' or 'from -> to : label'.");
            }

            var id = nodeLine[..colonIdx].Trim();
            var opsText = nodeLine[(colonIdx + 1)..].Trim();
            nodes.Add(TmSchemaNode.Create(id, opsText));

            if (isStart || startNodeId is null)
            {
                startNodeId = id;
            }
        }

        if (nodes.Count == 0 || startNodeId is null)
        {
            throw new FormatException("TM schema must define at least one node.");
        }

        return new TmSchema(name, nodes, edges, startNodeId, alphabet, blank, headStart);
    }
}
