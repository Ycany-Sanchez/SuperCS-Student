namespace SuperCsStudent.Automata;

/// <summary>
/// Canonical symbol normalization utilities for formal languages and automata.
/// Empty string is canonically represented as "e" (accepting "e", "ε", "λ", and "" as input aliases).
/// </summary>
public static class AutomataSymbols
{
    public const string Empty = "e";
    public const char DefaultBlank = '#';
    public const string DefaultHaltState = "h";

    public static bool IsEmptyToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return true;
        }

        var trimmed = token.Trim();
        return trimmed is "e" or "ε" or "λ";
    }

    public static string NormalizeSymbol(string? token)
    {
        if (IsEmptyToken(token))
        {
            return Empty;
        }

        return token!.Trim();
    }

    public static string NormalizeWord(string? input, IReadOnlySet<char>? alphabet = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (input is "ε" or "λ")
        {
            return string.Empty;
        }

        if (input == Empty && (alphabet is null || !alphabet.Contains('e')))
        {
            return string.Empty;
        }

        return input;
    }

    public static string NormalizeWord(string? input, IReadOnlySet<string>? terminals)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        if (input is "ε" or "λ")
        {
            return string.Empty;
        }

        if (input == Empty && (terminals is null || !terminals.Contains(Empty)))
        {
            return string.Empty;
        }

        return input;
    }

    public static string FormatWord(string? rawWord, IReadOnlySet<char>? alphabet = null)
    {
        if (string.IsNullOrEmpty(rawWord))
        {
            return Empty;
        }

        return rawWord;
    }
}
