namespace SuperCsStudent.Automata.Synthesis;

/// <summary>
/// Comparison operators supported in user-authored parametric formal language specifications.
/// </summary>
public enum CompareOp
{
    Eq,
    Neq,
    Gt,
    Geq,
    Lt,
    Leq
}

/// <summary>
/// A labeled test case automatically generated from a language specification's exact semantics.
/// </summary>
public sealed record LanguageTestCase(
    string Input,
    bool ExpectedAccept,
    string Reason);

/// <summary>
/// Inclusive integer domain bounds for a single exponent variable (default: Min = 0, Max = null).
/// </summary>
public sealed record VariableBounds(int Min = 0, int? Max = null)
{
    public bool IsUnsatisfiable => Max.HasValue && Min > Max.Value;

    public VariableBounds WithMin(int newMin) =>
        new(System.Math.Max(Min, newMin), Max);

    public VariableBounds WithMax(int newMax) =>
        new(Min, Max.HasValue ? System.Math.Min(Max.Value, newMax) : newMax);

    public bool Contains(int value) =>
        value >= Min && (!Max.HasValue || value <= Max.Value);
}

/// <summary>
/// Immutable linear affine expression over integer variables: sum_i (coeff_i * var_i) + Constant.
/// </summary>
public sealed class AffineExpr
{
    public IReadOnlyDictionary<string, int> Terms { get; }
    public int Constant { get; }

    public AffineExpr(IEnumerable<KeyValuePair<string, int>> terms, int constant = 0)
    {
        var dict = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (varName, coeff) in terms)
        {
            if (coeff == 0) continue;
            dict[varName] = dict.TryGetValue(varName, out var existing) ? existing + coeff : coeff;
            if (dict[varName] == 0)
            {
                dict.Remove(varName);
            }
        }
        Terms = dict;
        Constant = constant;
    }

    public static AffineExpr FromConstant(int value) =>
        new(Array.Empty<KeyValuePair<string, int>>(), value);

    public static AffineExpr FromVariable(string name, int coeff = 1) =>
        new(new[] { new KeyValuePair<string, int>(name, coeff) }, 0);

    public bool IsConstant => Terms.Count == 0;

    public int Evaluate(IReadOnlyDictionary<string, int> env)
    {
        var total = Constant;
        foreach (var (varName, coeff) in Terms)
        {
            if (env.TryGetValue(varName, out var v))
            {
                total += coeff * v;
            }
        }
        return total;
    }

    public AffineExpr Add(AffineExpr other)
    {
        var merged = new List<KeyValuePair<string, int>>(Terms.Count + other.Terms.Count);
        merged.AddRange(Terms);
        merged.AddRange(other.Terms);
        return new AffineExpr(merged, Constant + other.Constant);
    }

    public AffineExpr Subtract(AffineExpr other)
    {
        var merged = new List<KeyValuePair<string, int>>(Terms.Count + other.Terms.Count);
        merged.AddRange(Terms);
        foreach (var (k, v) in other.Terms)
        {
            merged.Add(new KeyValuePair<string, int>(k, -v));
        }
        return new AffineExpr(merged, Constant - other.Constant);
    }

    public AffineExpr Scale(int factor)
    {
        if (factor == 0) return FromConstant(0);
        return new AffineExpr(
            Terms.Select(kv => new KeyValuePair<string, int>(kv.Key, kv.Value * factor)),
            Constant * factor);
    }

    public AffineExpr Substitute(string variable, AffineExpr replacement)
    {
        if (!Terms.TryGetValue(variable, out var coeff))
        {
            return this;
        }
        var rest = new AffineExpr(
            Terms.Where(kv => !string.Equals(kv.Key, variable, StringComparison.Ordinal)),
            Constant);
        return rest.Add(replacement.Scale(coeff));
    }

    public string Format()
    {
        if (Terms.Count == 0) return Constant.ToString();
        var parts = new List<string>();
        foreach (var (v, c) in Terms)
        {
            if (parts.Count == 0)
            {
                parts.Add(c switch
                {
                    1 => v,
                    -1 => $"-{v}",
                    _ => $"{c}{v}"
                });
            }
            else
            {
                var abs = System.Math.Abs(c);
                var sign = c > 0 ? " + " : " - ";
                parts.Add(abs == 1 ? $"{sign}{v}" : $"{sign}{abs}{v}");
            }
        }
        if (Constant != 0)
        {
            parts.Add(Constant > 0 ? $" + {Constant}" : $" - {System.Math.Abs(Constant)}");
        }
        return string.Concat(parts);
    }
}

/// <summary>
/// A single atomic linear comparison: Left (Op) Right.
/// Normalized form: (Left - Right).Terms (Op) (Right.Constant - Left.Constant).
/// </summary>
public sealed record LinearConstraint(AffineExpr Left, CompareOp Op, AffineExpr Right)
{
    /// <summary>
    /// Returns (terms, op, rhs) representing sum(coeff_i * var_i) (op) rhs.
    /// </summary>
    public (IReadOnlyDictionary<string, int> Terms, CompareOp Op, int Rhs) Normalize()
    {
        var diff = Left.Subtract(Right);
        return (diff.Terms, Op, -diff.Constant);
    }

    public bool Evaluate(IReadOnlyDictionary<string, int> env)
    {
        var l = Left.Evaluate(env);
        var r = Right.Evaluate(env);
        return EvaluateOp(l, Op, r);
    }

    public LinearConstraint Substitute(string variable, AffineExpr replacement) =>
        new(Left.Substitute(variable, replacement), Op, Right.Substitute(variable, replacement));

    public static bool EvaluateOp(int left, CompareOp op, int right) => op switch
    {
        CompareOp.Eq => left == right,
        CompareOp.Neq => left != right,
        CompareOp.Gt => left > right,
        CompareOp.Geq => left >= right,
        CompareOp.Lt => left < right,
        CompareOp.Leq => left <= right,
        _ => false
    };

    public static CompareOp Flip(CompareOp op) => op switch
    {
        CompareOp.Eq => CompareOp.Eq,
        CompareOp.Neq => CompareOp.Neq,
        CompareOp.Gt => CompareOp.Lt,
        CompareOp.Geq => CompareOp.Leq,
        CompareOp.Lt => CompareOp.Gt,
        CompareOp.Leq => CompareOp.Geq,
        _ => op
    };

    public static string FormatOp(CompareOp op) => op switch
    {
        CompareOp.Eq => "=",
        CompareOp.Neq => "≠",
        CompareOp.Gt => ">",
        CompareOp.Geq => "≥",
        CompareOp.Lt => "<",
        CompareOp.Leq => "≤",
        _ => "?"
    };

    public string Format() => $"{Left.Format()} {FormatOp(Op)} {Right.Format()}";
}

/// <summary>
/// A single block in an ordered block-exponent pattern, e.g. a^(2n+1).
/// </summary>
public sealed record BlockTerm(char Symbol, AffineExpr Exponent);

/// <summary>
/// A conjunctive clause (AND) of linear constraints.
/// </summary>
public sealed record ConstraintBranch(IReadOnlyList<LinearConstraint> Constraints);

/// <summary>
/// Parsed parametric formal language specification.
/// </summary>
public abstract record ParametricSpec(string RawSpecification);

/// <summary>
/// Ordered block-exponent language: { s_1^{e_1} ... s_r^{e_r} | OR_j (AND_i C_{j,i}) }.
/// </summary>
public sealed record BlockExponentSpec(
    string RawSpecification,
    IReadOnlyList<BlockTerm> Blocks,
    IReadOnlyList<ConstraintBranch> Branches,
    IReadOnlyList<char>? ExplicitAlphabet) : ParametricSpec(RawSpecification);

/// <summary>
/// Unordered symbol-count language: { w in Σ* | Coeff1 * #s1(w) (Op) Coeff2 * #s2(w) + Offset }.
/// </summary>
public sealed record SymbolCountSpec(
    string RawSpecification,
    char Symbol1,
    char Symbol2,
    int Coeff1,
    int Coeff2,
    CompareOp Op,
    int Offset,
    IReadOnlyList<char> Alphabet) : ParametricSpec(RawSpecification);
