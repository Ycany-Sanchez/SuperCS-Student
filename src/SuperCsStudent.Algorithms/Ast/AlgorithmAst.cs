using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace SuperCsStudent.Algorithms.Ast;

/// <summary>
/// Root AST node representing an analyzable algorithm function.
/// Matches the JSON payload emitted by blocklyInterop.js extractAst().
/// </summary>
public sealed record AlgorithmFunction(
    string Name,
    IReadOnlyList<string> Parameters,
    IReadOnlyList<AlgorithmStatement> Body);

/// <summary>
/// Base type for all algorithm statements. Every statement carries an optional Blockly block Id
/// for synchronized hover highlighting across blocks, pseudocode, and derivation steps.
/// </summary>
[JsonConverter(typeof(AlgorithmStatementJsonConverter))]
public abstract record AlgorithmStatement(string? Id = null)
{
    public abstract string Kind { get; }
}

public sealed record DeclareStatement(
    IReadOnlyList<string> Names,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "declare";
}

public sealed record AssignStatement(
    string Target,
    string Op,
    AlgorithmExpr? Value,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "assign";
}

public sealed record UnaryStatement(
    string Variable,
    string Op,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "unary";
}

public sealed record ForStatement(
    string Iterator,
    string StartExpr,
    string Comparator,
    string BoundExpr,
    string StepOp,
    string StepValue,
    IReadOnlyList<AlgorithmStatement> Body,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "for";
}

public sealed record WhileStatement(
    AlgorithmExpr? Condition,
    string IterationsExpr,
    IReadOnlyList<AlgorithmStatement> Body,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "while";
}

public sealed record IfStatement(
    AlgorithmExpr? Condition,
    IReadOnlyList<AlgorithmStatement> ThenBody,
    IReadOnlyList<AlgorithmStatement> ElseBody,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "if";
}

public sealed record SwitchCase(
    string Label,
    bool IsDefault,
    IReadOnlyList<AlgorithmStatement> Body);

public sealed record SwitchStatement(
    string Variable,
    IReadOnlyList<SwitchCase> Cases,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "switch";
}

public sealed record ReturnStatement(
    AlgorithmExpr? Value,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "return";
}

public sealed record BreakStatement(string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "break";
}

public sealed record CallStatement(
    string FunctionName,
    string Args,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "call";
}

public sealed record RecursiveCallStatement(
    int Branches,
    string Subproblem,
    string? Id = null) : AlgorithmStatement(Id)
{
    public override string Kind => "recursiveCall";
}

/// <summary>
/// Base type for expression nodes inside assignments, conditions, and return statements.
/// </summary>
[JsonConverter(typeof(AlgorithmExprJsonConverter))]
public abstract record AlgorithmExpr
{
    public abstract string Kind { get; }
}

public sealed record BinaryExpr(
    string Op,
    AlgorithmExpr? Left,
    AlgorithmExpr? Right) : AlgorithmExpr
{
    public override string Kind => "binary";
}

public sealed record ArrayAccessExpr(
    string ArrayName,
    string IndexExpr) : AlgorithmExpr
{
    public override string Kind => "arrayAccess";
}

public sealed record VariableExpr(string Name) : AlgorithmExpr
{
    public override string Kind => "variable";
}

public sealed record LiteralExpr(string Value) : AlgorithmExpr
{
    public override string Kind => "literal";
}

/// <summary>
/// JSON serializer and deserializer matching blocklyInterop.js extractAst() output.
/// Handles discriminator keys in any property order and tolerates missing optional fields.
/// </summary>
public static class AlgorithmAstSerializer
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };
        options.Converters.Add(new AlgorithmStatementJsonConverter());
        options.Converters.Add(new AlgorithmExprJsonConverter());
        return options;
    }

    public static AlgorithmFunction Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("AST JSON cannot be empty.", nameof(json));
        }

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("AST JSON root must be a JSON object.");
        }

        var name = JsonReadHelpers.GetString(root, "name") ?? "algorithm";
        var parameters = JsonReadHelpers.GetStringList(root, "parameters");
        var body = JsonReadHelpers.GetStatementList(root, "body", Options);

        return new AlgorithmFunction(name, parameters, body);
    }

    public static string Serialize(AlgorithmFunction function) =>
        JsonSerializer.Serialize(function, Options);

    /// <summary>
    /// Converts an AlgorithmFunction AST into Blockly workspace serialization JSON
    /// compatible with Blockly.serialization.workspaces.load() in blocklyInterop.js.
    /// </summary>
    public static string ToBlocklyWorkspaceJson(AlgorithmFunction function)
    {
        var idCounter = 0;
        string NextId(string? existing) =>
            !string.IsNullOrWhiteSpace(existing) ? existing : $"blk_{++idCounter}";

        var fnBlock = new JsonObject
        {
            ["type"] = "daa_function",
            ["id"] = "fn_root",
            ["x"] = 24,
            ["y"] = 24,
            ["fields"] = new JsonObject
            {
                ["NAME"] = function.Name,
                ["PARAMS"] = string.Join(", ", function.Parameters)
            }
        };

        var bodyChain = BuildStatementChain(function.Body, NextId);
        if (bodyChain is not null)
        {
            fnBlock["inputs"] = new JsonObject
            {
                ["BODY"] = new JsonObject { ["block"] = bodyChain }
            };
        }

        var workspace = new JsonObject
        {
            ["blocks"] = new JsonObject
            {
                ["languageVersion"] = 0,
                ["blocks"] = new JsonArray(fnBlock)
            }
        };

        return workspace.ToJsonString();
    }

    private static JsonObject? BuildStatementChain(
        IReadOnlyList<AlgorithmStatement> statements,
        Func<string?, string> nextId)
    {
        JsonObject? head = null;
        JsonObject? tail = null;

        foreach (var stmt in statements)
        {
            var block = BuildStatementBlock(stmt, nextId);
            if (block is null) continue;

            if (head is null)
            {
                head = block;
                tail = block;
            }
            else
            {
                tail!["next"] = new JsonObject { ["block"] = block };
                tail = block;
            }
        }

        return head;
    }

    private static JsonObject? BuildStatementBlock(
        AlgorithmStatement stmt,
        Func<string?, string> nextId)
    {
        switch (stmt)
        {
            case DeclareStatement decl:
                return new JsonObject
                {
                    ["type"] = "daa_declare",
                    ["id"] = nextId(decl.Id),
                    ["fields"] = new JsonObject
                    {
                        ["VARS"] = string.Join(", ", decl.Names)
                    }
                };

            case AssignStatement assign:
            {
                var obj = new JsonObject
                {
                    ["type"] = "daa_assign",
                    ["id"] = nextId(assign.Id),
                    ["fields"] = new JsonObject
                    {
                        ["TARGET"] = assign.Target,
                        ["OP"] = assign.Op
                    }
                };
                var valBlock = BuildExprBlock(assign.Value, nextId);
                if (valBlock is not null)
                {
                    obj["inputs"] = new JsonObject
                    {
                        ["VALUE"] = new JsonObject { ["block"] = valBlock }
                    };
                }
                return obj;
            }

            case UnaryStatement unary:
                return new JsonObject
                {
                    ["type"] = "daa_unary",
                    ["id"] = nextId(unary.Id),
                    ["fields"] = new JsonObject
                    {
                        ["VAR"] = unary.Variable,
                        ["OP"] = unary.Op
                    }
                };

            case ForStatement forStmt:
            {
                var obj = new JsonObject
                {
                    ["type"] = "daa_for",
                    ["id"] = nextId(forStmt.Id),
                    ["fields"] = new JsonObject
                    {
                        ["VAR"] = forStmt.Iterator,
                        ["START"] = forStmt.StartExpr,
                        ["VAR_MIRROR"] = forStmt.Iterator,
                        ["CMP"] = forStmt.Comparator,
                        ["BOUND"] = forStmt.BoundExpr,
                        ["VAR_STEP"] = forStmt.Iterator,
                        ["STEP_OP"] = forStmt.StepOp,
                        ["STEP_VAL"] = forStmt.StepValue
                    }
                };
                var bodyChain = BuildStatementChain(forStmt.Body, nextId);
                if (bodyChain is not null)
                {
                    obj["inputs"] = new JsonObject
                    {
                        ["BODY"] = new JsonObject { ["block"] = bodyChain }
                    };
                }
                return obj;
            }

            case WhileStatement whileStmt:
            {
                var obj = new JsonObject
                {
                    ["type"] = "daa_while",
                    ["id"] = nextId(whileStmt.Id),
                    ["fields"] = new JsonObject
                    {
                        ["ITERS"] = whileStmt.IterationsExpr
                    }
                };
                var inputs = new JsonObject();
                var condBlock = BuildExprBlock(whileStmt.Condition, nextId);
                if (condBlock is not null)
                {
                    inputs["COND"] = new JsonObject { ["block"] = condBlock };
                }
                var bodyChain = BuildStatementChain(whileStmt.Body, nextId);
                if (bodyChain is not null)
                {
                    inputs["BODY"] = new JsonObject { ["block"] = bodyChain };
                }
                if (inputs.Count > 0) obj["inputs"] = inputs;
                return obj;
            }

            case IfStatement ifStmt:
            {
                var obj = new JsonObject
                {
                    ["type"] = "daa_if",
                    ["id"] = nextId(ifStmt.Id)
                };
                var inputs = new JsonObject();
                var condBlock = BuildExprBlock(ifStmt.Condition, nextId);
                if (condBlock is not null)
                {
                    inputs["COND"] = new JsonObject { ["block"] = condBlock };
                }
                var thenChain = BuildStatementChain(ifStmt.ThenBody, nextId);
                if (thenChain is not null)
                {
                    inputs["THEN"] = new JsonObject { ["block"] = thenChain };
                }
                var elseChain = BuildStatementChain(ifStmt.ElseBody, nextId);
                if (elseChain is not null)
                {
                    inputs["ELSE"] = new JsonObject { ["block"] = elseChain };
                }
                if (inputs.Count > 0) obj["inputs"] = inputs;
                return obj;
            }

            case ReturnStatement ret:
            {
                var obj = new JsonObject
                {
                    ["type"] = "daa_return",
                    ["id"] = nextId(ret.Id)
                };
                var valBlock = BuildExprBlock(ret.Value, nextId);
                if (valBlock is not null)
                {
                    obj["inputs"] = new JsonObject
                    {
                        ["VALUE"] = new JsonObject { ["block"] = valBlock }
                    };
                }
                return obj;
            }

            case BreakStatement brk:
                return new JsonObject
                {
                    ["type"] = "daa_break",
                    ["id"] = nextId(brk.Id)
                };

            case CallStatement call:
                return new JsonObject
                {
                    ["type"] = "daa_call",
                    ["id"] = nextId(call.Id),
                    ["fields"] = new JsonObject
                    {
                        ["FUNC"] = call.FunctionName,
                        ["ARGS"] = call.Args
                    }
                };

            case RecursiveCallStatement rec:
                return new JsonObject
                {
                    ["type"] = "daa_recursive_call",
                    ["id"] = nextId(rec.Id),
                    ["fields"] = new JsonObject
                    {
                        ["BRANCHES"] = rec.Branches,
                        ["ARG"] = rec.Subproblem
                    }
                };

            default:
                return null;
        }
    }

    private static JsonObject? BuildExprBlock(AlgorithmExpr? expr, Func<string?, string> nextId)
    {
        switch (expr)
        {
            case BinaryExpr bin:
            {
                var obj = new JsonObject
                {
                    ["type"] = "daa_binary",
                    ["id"] = nextId(null),
                    ["fields"] = new JsonObject { ["OP"] = bin.Op }
                };
                var inputs = new JsonObject();
                var left = BuildExprBlock(bin.Left, nextId);
                if (left is not null) inputs["LEFT"] = new JsonObject { ["block"] = left };
                var right = BuildExprBlock(bin.Right, nextId);
                if (right is not null) inputs["RIGHT"] = new JsonObject { ["block"] = right };
                if (inputs.Count > 0) obj["inputs"] = inputs;
                return obj;
            }

            case ArrayAccessExpr arr:
                return new JsonObject
                {
                    ["type"] = "daa_array_access",
                    ["id"] = nextId(null),
                    ["fields"] = new JsonObject
                    {
                        ["ARR"] = arr.ArrayName,
                        ["IDX"] = arr.IndexExpr
                    }
                };

            case VariableExpr v:
                return new JsonObject
                {
                    ["type"] = "daa_var",
                    ["id"] = nextId(null),
                    ["fields"] = new JsonObject { ["NAME"] = v.Name }
                };

            case LiteralExpr lit:
                return new JsonObject
                {
                    ["type"] = "daa_num",
                    ["id"] = nextId(null),
                    ["fields"] = new JsonObject { ["VAL"] = lit.Value }
                };

            default:
                return null;
        }
    }
}

internal static class JsonReadHelpers
{
    public static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (element.TryGetProperty(name, out value)) return true;
            foreach (var prop in element.EnumerateObject())
            {
                if (string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = prop.Value;
                    return true;
                }
            }
        }
        value = default;
        return false;
    }

    public static string? GetString(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var prop)) return null;
        return prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString(),
            JsonValueKind.Number => prop.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => null
        };
    }

    public static int GetInt(JsonElement element, string name, int fallback)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var prop)) return fallback;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var val)) return val;
        if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var parsed)) return parsed;
        return fallback;
    }

    public static bool GetBool(JsonElement element, string name, bool fallback)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var prop)) return fallback;
        return prop.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(prop.GetString(), out var b) => b,
            _ => fallback
        };
    }

    public static IReadOnlyList<string> GetStringList(JsonElement element, string name)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var list = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var s = item.GetString();
                if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
            }
        }
        return list;
    }

    public static IReadOnlyList<AlgorithmStatement> GetStatementList(
        JsonElement element,
        string name,
        JsonSerializerOptions options)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var prop) || prop.ValueKind != JsonValueKind.Array)
            return Array.Empty<AlgorithmStatement>();

        var list = new List<AlgorithmStatement>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Null) continue;
            var stmt = JsonSerializer.Deserialize<AlgorithmStatement>(item.GetRawText(), options);
            if (stmt is not null) list.Add(stmt);
        }
        return list;
    }

    public static AlgorithmExpr? GetExpr(JsonElement element, string name, JsonSerializerOptions options)
    {
        if (!TryGetPropertyIgnoreCase(element, name, out var prop) || prop.ValueKind != JsonValueKind.Object)
            return null;
        return JsonSerializer.Deserialize<AlgorithmExpr>(prop.GetRawText(), options);
    }
}

public sealed class AlgorithmStatementJsonConverter : JsonConverter<AlgorithmStatement>
{
    public override AlgorithmStatement? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var el = doc.RootElement;
        if (el.ValueKind == JsonValueKind.Null) return null;
        if (el.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected JSON object for AlgorithmStatement.");

        var kind = JsonReadHelpers.GetString(el, "kind")
            ?? throw new JsonException("Statement object is missing 'kind' discriminator.");
        var id = JsonReadHelpers.GetString(el, "id");

        return kind switch
        {
            "declare" => new DeclareStatement(
                JsonReadHelpers.GetStringList(el, "names"),
                id),

            "assign" => new AssignStatement(
                JsonReadHelpers.GetString(el, "target") ?? "x",
                JsonReadHelpers.GetString(el, "op") ?? "=",
                JsonReadHelpers.GetExpr(el, "value", options),
                id),

            "unary" => new UnaryStatement(
                JsonReadHelpers.GetString(el, "variable") ?? "i",
                JsonReadHelpers.GetString(el, "op") ?? "++",
                id),

            "for" => new ForStatement(
                JsonReadHelpers.GetString(el, "iterator") ?? "i",
                JsonReadHelpers.GetString(el, "startExpr") ?? "1",
                JsonReadHelpers.GetString(el, "comparator") ?? "<=",
                JsonReadHelpers.GetString(el, "boundExpr") ?? "n",
                JsonReadHelpers.GetString(el, "stepOp") ?? "++",
                JsonReadHelpers.GetString(el, "stepValue") ?? "1",
                JsonReadHelpers.GetStatementList(el, "body", options),
                id),

            "while" => new WhileStatement(
                JsonReadHelpers.GetExpr(el, "condition", options),
                JsonReadHelpers.GetString(el, "iterationsExpr") ?? "n",
                JsonReadHelpers.GetStatementList(el, "body", options),
                id),

            "if" => new IfStatement(
                JsonReadHelpers.GetExpr(el, "condition", options),
                JsonReadHelpers.GetStatementList(el, "thenBody", options),
                JsonReadHelpers.GetStatementList(el, "elseBody", options),
                id),

            "switch" => ReadSwitch(el, id, options),

            "return" => new ReturnStatement(
                JsonReadHelpers.GetExpr(el, "value", options),
                id),

            "break" => new BreakStatement(id),

            "call" => new CallStatement(
                JsonReadHelpers.GetString(el, "functionName") ?? "print",
                JsonReadHelpers.GetString(el, "args") ?? string.Empty,
                id),

            "recursiveCall" => new RecursiveCallStatement(
                JsonReadHelpers.GetInt(el, "branches", 1),
                JsonReadHelpers.GetString(el, "subproblem") ?? "n-1",
                id),

            _ => throw new JsonException($"Unknown statement kind '{kind}'.")
        };
    }

    private static SwitchStatement ReadSwitch(JsonElement el, string? id, JsonSerializerOptions options)
    {
        var variable = JsonReadHelpers.GetString(el, "variable") ?? "x";
        var cases = new List<SwitchCase>();
        if (JsonReadHelpers.TryGetPropertyIgnoreCase(el, "cases", out var casesEl) &&
            casesEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in casesEl.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Object) continue;
                var label = JsonReadHelpers.GetString(c, "label") ?? string.Empty;
                var isDefault = JsonReadHelpers.GetBool(c, "isDefault", false);
                var body = JsonReadHelpers.GetStatementList(c, "body", options);
                cases.Add(new SwitchCase(label, isDefault, body));
            }
        }
        return new SwitchStatement(variable, cases, id);
    }

    public override void Write(Utf8JsonWriter writer, AlgorithmStatement value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);
        if (value.Id is not null) writer.WriteString("id", value.Id);

        switch (value)
        {
            case DeclareStatement decl:
                writer.WritePropertyName("names");
                JsonSerializer.Serialize(writer, decl.Names, options);
                break;

            case AssignStatement assign:
                writer.WriteString("target", assign.Target);
                writer.WriteString("op", assign.Op);
                writer.WritePropertyName("value");
                JsonSerializer.Serialize(writer, assign.Value, options);
                break;

            case UnaryStatement unary:
                writer.WriteString("variable", unary.Variable);
                writer.WriteString("op", unary.Op);
                break;

            case ForStatement f:
                writer.WriteString("iterator", f.Iterator);
                writer.WriteString("startExpr", f.StartExpr);
                writer.WriteString("comparator", f.Comparator);
                writer.WriteString("boundExpr", f.BoundExpr);
                writer.WriteString("stepOp", f.StepOp);
                writer.WriteString("stepValue", f.StepValue);
                writer.WritePropertyName("body");
                JsonSerializer.Serialize(writer, f.Body, options);
                break;

            case WhileStatement w:
                writer.WritePropertyName("condition");
                JsonSerializer.Serialize(writer, w.Condition, options);
                writer.WriteString("iterationsExpr", w.IterationsExpr);
                writer.WritePropertyName("body");
                JsonSerializer.Serialize(writer, w.Body, options);
                break;

            case IfStatement i:
                writer.WritePropertyName("condition");
                JsonSerializer.Serialize(writer, i.Condition, options);
                writer.WritePropertyName("thenBody");
                JsonSerializer.Serialize(writer, i.ThenBody, options);
                writer.WritePropertyName("elseBody");
                JsonSerializer.Serialize(writer, i.ElseBody, options);
                break;

            case SwitchStatement sw:
                writer.WriteString("variable", sw.Variable);
                writer.WritePropertyName("cases");
                writer.WriteStartArray();
                foreach (var c in sw.Cases)
                {
                    writer.WriteStartObject();
                    writer.WriteString("label", c.Label);
                    writer.WriteBoolean("isDefault", c.IsDefault);
                    writer.WritePropertyName("body");
                    JsonSerializer.Serialize(writer, c.Body, options);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;

            case ReturnStatement ret:
                writer.WritePropertyName("value");
                JsonSerializer.Serialize(writer, ret.Value, options);
                break;

            case BreakStatement:
                break;

            case CallStatement call:
                writer.WriteString("functionName", call.FunctionName);
                writer.WriteString("args", call.Args);
                break;

            case RecursiveCallStatement rec:
                writer.WriteNumber("branches", rec.Branches);
                writer.WriteString("subproblem", rec.Subproblem);
                break;
        }

        writer.WriteEndObject();
    }
}

public sealed class AlgorithmExprJsonConverter : JsonConverter<AlgorithmExpr>
{
    public override AlgorithmExpr? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var el = doc.RootElement;
        if (el.ValueKind == JsonValueKind.Null) return null;
        if (el.ValueKind != JsonValueKind.Object)
            throw new JsonException("Expected JSON object for AlgorithmExpr.");

        var kind = JsonReadHelpers.GetString(el, "kind")
            ?? throw new JsonException("Expression object is missing 'kind' discriminator.");

        return kind switch
        {
            "binary" => new BinaryExpr(
                JsonReadHelpers.GetString(el, "op") ?? "+",
                JsonReadHelpers.GetExpr(el, "left", options),
                JsonReadHelpers.GetExpr(el, "right", options)),

            "arrayAccess" => new ArrayAccessExpr(
                JsonReadHelpers.GetString(el, "arrayName") ?? "array",
                JsonReadHelpers.GetString(el, "indexExpr") ?? "i"),

            "variable" => new VariableExpr(
                JsonReadHelpers.GetString(el, "name") ?? "x"),

            "literal" => new LiteralExpr(
                JsonReadHelpers.GetString(el, "value") ?? "0"),

            _ => throw new JsonException($"Unknown expression kind '{kind}'.")
        };
    }

    public override void Write(Utf8JsonWriter writer, AlgorithmExpr value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("kind", value.Kind);

        switch (value)
        {
            case BinaryExpr bin:
                writer.WriteString("op", bin.Op);
                writer.WritePropertyName("left");
                JsonSerializer.Serialize(writer, bin.Left, options);
                writer.WritePropertyName("right");
                JsonSerializer.Serialize(writer, bin.Right, options);
                break;

            case ArrayAccessExpr arr:
                writer.WriteString("arrayName", arr.ArrayName);
                writer.WriteString("indexExpr", arr.IndexExpr);
                break;

            case VariableExpr v:
                writer.WriteString("name", v.Name);
                break;

            case LiteralExpr lit:
                writer.WriteString("value", lit.Value);
                break;
        }

        writer.WriteEndObject();
    }
}
