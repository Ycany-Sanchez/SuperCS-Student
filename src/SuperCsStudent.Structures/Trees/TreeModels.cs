using SuperCsStudent.Core.Diagram;
using SuperCsStudent.Core.Layout;
using SuperCsStudent.Structures.Config;

namespace SuperCsStudent.Structures.Trees;

public sealed record TreeStepState(
    DiagramScene Scene,
    IReadOnlyList<string> PreOrder,
    IReadOnlyList<string> InOrder,
    IReadOnlyList<string> PostOrder,
    IReadOnlyList<string> LevelOrder,
    bool ParentLinksHealthy,
    string StatusSummary,
    DiagramScene? IsomorphicScene = null);

public enum TreeCommandKind
{
    Insert,
    Delete,
    Search,
    Clear
}

public sealed record TreeCommand(TreeCommandKind Kind, int? Key, string RawText);

public static class TreeScriptParser
{
    /// <summary>
    /// Parses compact batch operation scripts such as:
    /// - "insert 52, 27, 35; delete 35; search 14"
    /// - "insert 52, 27, remove 27, insert 19"
    /// - "i 52 / d 35 / s 14"
    /// </summary>
    public static IReadOnlyList<TreeCommand> Parse(string script)
    {
        var commands = new List<TreeCommand>();
        if (string.IsNullOrWhiteSpace(script))
        {
            return commands;
        }

        var segments = script.Split(
            new[] { ';', '/', '\n', '\r', ',' },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var activeKind = TreeCommandKind.Insert;

        foreach (var rawSegment in segments)
        {
            var tokens = rawSegment.Split(
                new[] { ' ', '\t', ':' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (tokens.Length == 0)
            {
                continue;
            }

            var idx = 0;
            if (TryParseVerb(tokens[0], out var parsedKind))
            {
                activeKind = parsedKind;
                idx = 1;

                if (activeKind == TreeCommandKind.Clear)
                {
                    commands.Add(new TreeCommand(TreeCommandKind.Clear, null, rawSegment));
                    continue;
                }
            }

            for (; idx < tokens.Length; idx++)
            {
                var token = tokens[idx];
                if (TryParseVerb(token, out var inlineKind))
                {
                    activeKind = inlineKind;
                    if (activeKind == TreeCommandKind.Clear)
                    {
                        commands.Add(new TreeCommand(TreeCommandKind.Clear, null, token));
                    }
                    continue;
                }

                if (int.TryParse(token, out var key))
                {
                    commands.Add(new TreeCommand(activeKind, key, $"{VerbName(activeKind)} {key}"));
                }
            }
        }

        return commands;
    }

    private static bool TryParseVerb(string token, out TreeCommandKind kind)
    {
        switch (token.Trim().ToLowerInvariant())
        {
            case "i":
            case "ins":
            case "insert":
            case "add":
            case "put":
            case "+":
                kind = TreeCommandKind.Insert;
                return true;
            case "d":
            case "del":
            case "delete":
            case "rem":
            case "remove":
            case "-":
                kind = TreeCommandKind.Delete;
                return true;
            case "s":
            case "search":
            case "find":
            case "get":
            case "?":
                kind = TreeCommandKind.Search;
                return true;
            case "c":
            case "clear":
            case "reset":
                kind = TreeCommandKind.Clear;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string VerbName(TreeCommandKind kind) => kind switch
    {
        TreeCommandKind.Insert => "insert",
        TreeCommandKind.Delete => "delete",
        TreeCommandKind.Search => "search",
        TreeCommandKind.Clear => "clear",
        _ => "op"
    };
}

internal static class TreeLayoutHelpers
{
    public static LayoutTreeNode CreateSentinel(string parentId, string side, bool isDoubleBlack = false)
    {
        return new LayoutTreeNode
        {
            Id = $"{parentId}_nil_{side}",
            Label = "",
            Shape = NodeShapes.Sentinel,
            SemanticState = isDoubleBlack ? SemanticStates.DoubleBlack : SemanticStates.BlackNode,
            Width = 16,
            Height = 16
        };
    }

    public static int EmptyHeight(HeightConvention convention) =>
        convention == HeightConvention.LeafIsOne ? 0 : -1;

    public static int LeafHeight(HeightConvention convention) =>
        convention == HeightConvention.LeafIsOne ? 1 : 0;

    public static string FormatSigned(int value) => value > 0 ? $"+{value}" : value.ToString();
}
