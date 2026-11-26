using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text;
using SuperCsStudent.Core.Math;

namespace SuperCsStudent.Algorithms.Symbolic;

/// <summary>
/// Logarithmic factor (log_Base(VarName))^Power with exact base preserved.
/// </summary>
public readonly record struct LogFactor(string VarName, int Base, int Power = 1) : IComparable<LogFactor>
{
    public double Evaluate(IReadOnlyDictionary<string, double> env, double defaultVal)
    {
        var v = env.TryGetValue(VarName, out var found) ? found : defaultVal;
        if (v <= 0) return 0;
        var l = Math.Log(v) / Math.Log(Base);
        return Power == 1 ? l : Math.Pow(l, Power);
    }

    public int CompareTo(LogFactor other)
    {
        var c = string.CompareOrdinal(VarName, other.VarName);
        if (c != 0) return c;
        c = Base.CompareTo(other.Base);
        if (c != 0) return c;
        return Power.CompareTo(other.Power);
    }

    public string ToPlainString()
    {
        var inner = $"log_{Base} {VarName}";
        return Power == 1 ? inner : $"({inner})^{Power}";
    }

    public string ToLatexString()
    {
        var basePart = Base >= 10 ? $"{{{Base}}}" : Base.ToString(CultureInfo.InvariantCulture);
        var inner = $@"\log_{basePart} {VarName}";
        return Power == 1 ? inner : $@"({inner})^{Power}";
    }
}

/// <summary>
/// Exponential factor Base^VarName (e.g. 2^n, 3^n).
/// </summary>
public readonly record struct ExpFactor(BigRational Base, string VarName) : IComparable<ExpFactor>
{
    public double Evaluate(IReadOnlyDictionary<string, double> env, double defaultVal)
    {
        var v = env.TryGetValue(VarName, out var found) ? found : defaultVal;
        return Math.Pow(Base.ToDouble(), v);
    }

    public int CompareTo(ExpFactor other)
    {
        var c = string.CompareOrdinal(VarName, other.VarName);
        if (c != 0) return c;
        return Base.CompareTo(other.Base);
    }

    public string ToPlainString()
    {
        var baseStr = Base.IsInteger ? Base.ToPlainString() : $"({Base.ToPlainString()})";
        return $"{baseStr}^{VarName}";
    }

    public string ToLatexString()
    {
        var baseStr = Base.IsInteger ? Base.ToLatexString() : $@"\left({Base.ToLatexString()}\right)";
        return $"{baseStr}^{{{VarName}}}";
    }
}

/// <summary>
/// Fractional polynomial power VarName^{log_{LogBase}(LogArg)}, e.g. n^{log_3 2}.
/// </summary>
public readonly record struct FractionalPower(string VarName, int LogBase, int LogArg) : IComparable<FractionalPower>
{
    public double ExponentValue => Math.Log(LogArg) / Math.Log(LogBase);

    public double Evaluate(IReadOnlyDictionary<string, double> env, double defaultVal)
    {
        var v = env.TryGetValue(VarName, out var found) ? found : defaultVal;
        if (v <= 0) return 0;
        return Math.Pow(v, ExponentValue);
    }

    public int CompareTo(FractionalPower other)
    {
        var c = string.CompareOrdinal(VarName, other.VarName);
        if (c != 0) return c;
        c = LogBase.CompareTo(other.LogBase);
        if (c != 0) return c;
        return LogArg.CompareTo(other.LogArg);
    }

    public string ToPlainString() => $"{VarName}^{{log_{LogBase} {LogArg}}}";

    public string ToLatexString()
    {
        var basePart = LogBase >= 10 ? $"{{{LogBase}}}" : LogBase.ToString(CultureInfo.InvariantCulture);
        return $@"{VarName}^{{\log_{basePart} {LogArg}}}";
    }
}

/// <summary>
/// Canonical product of variable integer powers, logarithmic factors, exponential factors,
/// and optional fractional exponent n^{log_b a}.
/// </summary>
public sealed class BasisTerm : IEquatable<BasisTerm>, IComparable<BasisTerm>
{
    public static readonly BasisTerm One = new(
        ImmutableSortedDictionary<string, int>.Empty,
        ImmutableArray<LogFactor>.Empty,
        ImmutableArray<ExpFactor>.Empty,
        null);

    public ImmutableSortedDictionary<string, int> VarPowers { get; }
    public ImmutableArray<LogFactor> Logs { get; }
    public ImmutableArray<ExpFactor> Exps { get; }
    public FractionalPower? FracPower { get; }

    public LogFactor? PrimaryLog => Logs.Length > 0 ? Logs[0] : null;
    public ExpFactor? PrimaryExp => Exps.Length > 0 ? Exps[0] : null;

    public bool IsConstant =>
        VarPowers.Count == 0 && Logs.Length == 0 && Exps.Length == 0 && FracPower is null;

    public int TotalPolyDegree
    {
        get
        {
            var sum = 0;
            foreach (var kv in VarPowers) sum += kv.Value;
            return sum;
        }
    }

    public double EffectivePolyDegree =>
        TotalPolyDegree + (FracPower is { } fp ? fp.ExponentValue : 0.0);

    public int TotalLogPower
    {
        get
        {
            var sum = 0;
            foreach (var l in Logs) sum += l.Power;
            return sum;
        }
    }

    public BasisTerm(
        ImmutableSortedDictionary<string, int> varPowers,
        ImmutableArray<LogFactor> logs,
        ImmutableArray<ExpFactor> exps,
        FractionalPower? fracPower)
    {
        // Strip zero powers and sort deterministically
        var cleanVars = varPowers.Where(kv => kv.Value != 0)
            .ToImmutableSortedDictionary(StringComparer.Ordinal);

        // Merge logs with identical (VarName, Base)
        var logMap = new SortedDictionary<(string Var, int Base), int>();
        foreach (var l in logs)
        {
            if (l.Power == 0) continue;
            var key = (l.VarName, l.Base);
            logMap[key] = logMap.TryGetValue(key, out var prev) ? prev + l.Power : l.Power;
        }
        var cleanLogs = logMap.Where(kv => kv.Value != 0)
            .Select(kv => new LogFactor(kv.Key.Var, kv.Key.Base, kv.Value))
            .ToImmutableArray();

        // Merge exps with identical VarName by multiplying bases
        var expMap = new SortedDictionary<string, BigRational>(StringComparer.Ordinal);
        foreach (var e in exps)
        {
            if (e.Base.IsOne) continue;
            expMap[e.VarName] = expMap.TryGetValue(e.VarName, out var prev) ? prev * e.Base : e.Base;
        }
        var cleanExps = expMap.Where(kv => !kv.Value.IsOne)
            .Select(kv => new ExpFactor(kv.Value, kv.Key))
            .ToImmutableArray();

        // Simplify exact integer fractional power if LogArg == LogBase^k
        if (fracPower is { } fp && TryExactIntLog(fp.LogArg, fp.LogBase, out var exactPow))
        {
            if (exactPow > 0)
            {
                var prev = cleanVars.TryGetValue(fp.VarName, out var existing) ? existing : 0;
                cleanVars = cleanVars.SetItem(fp.VarName, prev + exactPow);
            }
            fracPower = null;
        }

        VarPowers = cleanVars;
        Logs = cleanLogs;
        Exps = cleanExps;
        FracPower = fracPower;
    }

    private static bool TryExactIntLog(int arg, int logBase, out int exponent)
    {
        exponent = 0;
        if (logBase <= 1 || arg <= 0) return false;
        if (arg == 1) return true;
        var cur = 1L;
        while (cur < arg)
        {
            cur *= logBase;
            exponent++;
        }
        return cur == arg;
    }

    public static BasisTerm Variable(string name, int power = 1)
    {
        if (power == 0) return One;
        var dict = ImmutableSortedDictionary<string, int>.Empty.Add(name, power);
        return new BasisTerm(dict, ImmutableArray<LogFactor>.Empty, ImmutableArray<ExpFactor>.Empty, null);
    }

    public static BasisTerm Log(string varName, int logBase = 2, int power = 1) =>
        new(ImmutableSortedDictionary<string, int>.Empty,
            ImmutableArray.Create(new LogFactor(varName, logBase, power)),
            ImmutableArray<ExpFactor>.Empty,
            null);

    public static BasisTerm Exp(BigRational expBase, string varName) =>
        new(ImmutableSortedDictionary<string, int>.Empty,
            ImmutableArray<LogFactor>.Empty,
            ImmutableArray.Create(new ExpFactor(expBase, varName)),
            null);

    public static BasisTerm Fractional(string varName, int logBase, int logArg) =>
        new(ImmutableSortedDictionary<string, int>.Empty,
            ImmutableArray<LogFactor>.Empty,
            ImmutableArray<ExpFactor>.Empty,
            new FractionalPower(varName, logBase, logArg));

    public BasisTerm Multiply(BasisTerm other)
    {
        if (IsConstant) return other;
        if (other.IsConstant) return this;

        var vars = VarPowers.ToBuilder();
        foreach (var (k, v) in other.VarPowers)
        {
            vars[k] = vars.TryGetValue(k, out var existing) ? existing + v : v;
        }

        var logs = Logs.AddRange(other.Logs);
        var exps = Exps.AddRange(other.Exps);
        var frac = FracPower ?? other.FracPower;
        if (FracPower is not null && other.FracPower is not null &&
            FracPower.Value.VarName == other.FracPower.Value.VarName &&
            FracPower.Value.LogBase == other.FracPower.Value.LogBase)
        {
            frac = new FractionalPower(
                FracPower.Value.VarName,
                FracPower.Value.LogBase,
                checked(FracPower.Value.LogArg * other.FracPower.Value.LogArg));
        }

        return new BasisTerm(vars.ToImmutable(), logs, exps, frac);
    }

    public bool TryDivideBy(BasisTerm divisor, out BasisTerm result)
    {
        if (divisor.IsConstant)
        {
            result = this;
            return true;
        }

        if (divisor.Exps.Length > 0 || divisor.FracPower is not null)
        {
            if (Equals(divisor))
            {
                result = One;
                return true;
            }
            result = One;
            return false;
        }

        var vars = VarPowers.ToBuilder();
        foreach (var (k, v) in divisor.VarPowers)
        {
            if (!vars.TryGetValue(k, out var existing) || existing < v)
            {
                result = One;
                return false;
            }
            if (existing == v) vars.Remove(k);
            else vars[k] = existing - v;
        }

        var logMap = Logs.ToDictionary(l => (l.VarName, l.Base), l => l.Power);
        foreach (var l in divisor.Logs)
        {
            var key = (l.VarName, l.Base);
            if (!logMap.TryGetValue(key, out var existing) || existing < l.Power)
            {
                result = One;
                return false;
            }
            if (existing == l.Power) logMap.Remove(key);
            else logMap[key] = existing - l.Power;
        }

        var remainingLogs = logMap
            .Select(kv => new LogFactor(kv.Key.VarName, kv.Key.Base, kv.Value))
            .ToImmutableArray();

        result = new BasisTerm(vars.ToImmutable(), remainingLogs, Exps, FracPower);
        return true;
    }

    public bool ContainsVariable(string varName)
    {
        if (VarPowers.ContainsKey(varName)) return true;
        if (FracPower is { } fp && fp.VarName == varName) return true;
        foreach (var l in Logs) if (l.VarName == varName) return true;
        foreach (var e in Exps) if (e.VarName == varName) return true;
        return false;
    }

    public IEnumerable<string> GetVariables()
    {
        foreach (var k in VarPowers.Keys) yield return k;
        if (FracPower is { } fp) yield return fp.VarName;
        foreach (var l in Logs) yield return l.VarName;
        foreach (var e in Exps) yield return e.VarName;
    }

    public double Evaluate(IReadOnlyDictionary<string, double> env, double defaultVal)
    {
        var acc = 1.0;
        foreach (var (k, p) in VarPowers)
        {
            var v = env.TryGetValue(k, out var found) ? found : defaultVal;
            acc *= Math.Pow(v, p);
        }
        if (FracPower is { } fp) acc *= fp.Evaluate(env, defaultVal);
        foreach (var l in Logs) acc *= l.Evaluate(env, defaultVal);
        foreach (var e in Exps) acc *= e.Evaluate(env, defaultVal);
        return acc;
    }

    public bool StartsWithLog =>
        VarPowers.Count == 0 && FracPower is null && Exps.Length == 0 && Logs.Length > 0;

    public bool StartsWithExp =>
        VarPowers.Count == 0 && FracPower is null && Exps.Length > 0;

    /// <summary>
    /// Compares terms in canonical descending asymptotic growth order (dominant terms first).
    /// Returns negative if this term should appear BEFORE other.
    /// </summary>
    public int CompareTo(BasisTerm? other)
    {
        if (other is null) return -1;
        if (ReferenceEquals(this, other)) return 0;

        // 1. Exponential dominance (larger growing base first)
        var maxExpA = MaxExpBase();
        var maxExpB = other.MaxExpBase();
        var expCmp = maxExpB.CompareTo(maxExpA);
        if (expCmp != 0) return expCmp;

        var expCountCmp = other.Exps.Length.CompareTo(Exps.Length);
        if (expCountCmp != 0) return expCountCmp;

        // 2. Effective polynomial degree (higher degree first)
        var degA = EffectivePolyDegree;
        var degB = other.EffectivePolyDegree;
        if (Math.Abs(degA - degB) > 1e-9)
            return degB.CompareTo(degA);

        // 3. Log power (higher log power first, e.g. n log n before n, log n before 1)
        var logCmp = other.TotalLogPower.CompareTo(TotalLogPower);
        if (logCmp != 0) return logCmp;

        // 4. Deterministic variable tie-breaker ("n" preferred first, then lexicographical)
        var varsA = OrderedVars().ToList();
        var varsB = other.OrderedVars().ToList();
        var minLen = Math.Min(varsA.Count, varsB.Count);
        for (var idx = 0; idx < minLen; idx++)
        {
            var nCmp = CompareVarNames(varsA[idx].Key, varsB[idx].Key);
            if (nCmp != 0) return nCmp;
            var pCmp = varsB[idx].Value.CompareTo(varsA[idx].Value);
            if (pCmp != 0) return pCmp;
        }
        var lenCmp = varsB.Count.CompareTo(varsA.Count);
        if (lenCmp != 0) return lenCmp;

        // 5. Logs tie-breaker
        for (var idx = 0; idx < Math.Min(Logs.Length, other.Logs.Length); idx++)
        {
            var lc = Logs[idx].CompareTo(other.Logs[idx]);
            if (lc != 0) return lc;
        }
        var lLen = other.Logs.Length.CompareTo(Logs.Length);
        if (lLen != 0) return lLen;

        // 6. Exps tie-breaker
        for (var idx = 0; idx < Math.Min(Exps.Length, other.Exps.Length); idx++)
        {
            var ec = Exps[idx].CompareTo(other.Exps[idx]);
            if (ec != 0) return ec;
        }

        // 7. Fractional power tie-breaker
        if (FracPower is not null && other.FracPower is not null)
            return FracPower.Value.CompareTo(other.FracPower.Value);
        if (FracPower is not null) return -1;
        if (other.FracPower is not null) return 1;

        return 0;
    }

    private double MaxExpBase()
    {
        var max = 0.0;
        foreach (var e in Exps)
        {
            var b = e.Base.ToDouble();
            if (b > max) max = b;
        }
        return max;
    }

    private IEnumerable<KeyValuePair<string, int>> OrderedVars() =>
        VarPowers.OrderBy(kv => kv.Key == "n" ? 0 : 1).ThenBy(kv => kv.Key, StringComparer.Ordinal);

    private static int CompareVarNames(string a, string b)
    {
        if (a == b) return 0;
        if (a == "n") return -1;
        if (b == "n") return 1;
        return string.CompareOrdinal(a, b);
    }

    public string ToPlainString()
    {
        if (IsConstant) return "1";
        var parts = new List<string>();

        foreach (var (name, pow) in OrderedVars())
        {
            parts.Add(pow == 1 ? name : $"{name}^{pow}");
        }

        if (FracPower is { } fp)
        {
            parts.Add(fp.ToPlainString());
        }

        foreach (var l in Logs)
        {
            parts.Add(l.ToPlainString());
        }

        foreach (var e in Exps)
        {
            parts.Add(e.ToPlainString());
        }

        return string.Join(" ", parts);
    }

    public string ToLatexString()
    {
        if (IsConstant) return "1";
        var parts = new List<string>();

        foreach (var (name, pow) in OrderedVars())
        {
            parts.Add(pow == 1 ? name : $"{name}^{pow}");
        }

        if (FracPower is { } fp)
        {
            parts.Add(fp.ToLatexString());
        }

        foreach (var l in Logs)
        {
            parts.Add(l.ToLatexString());
        }

        foreach (var e in Exps)
        {
            parts.Add(e.ToLatexString());
        }

        return string.Join(" ", parts);
    }

    public bool Equals(BasisTerm? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (VarPowers.Count != other.VarPowers.Count ||
            Logs.Length != other.Logs.Length ||
            Exps.Length != other.Exps.Length ||
            FracPower != other.FracPower)
        {
            return false;
        }

        foreach (var (k, v) in VarPowers)
        {
            if (!other.VarPowers.TryGetValue(k, out var ov) || ov != v) return false;
        }

        for (var i = 0; i < Logs.Length; i++)
        {
            if (Logs[i] != other.Logs[i]) return false;
        }

        for (var i = 0; i < Exps.Length; i++)
        {
            if (Exps[i] != other.Exps[i]) return false;
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is BasisTerm bt && Equals(bt);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var (k, v) in VarPowers)
        {
            hc.Add(k, StringComparer.Ordinal);
            hc.Add(v);
        }
        foreach (var l in Logs) hc.Add(l);
        foreach (var e in Exps) hc.Add(e);
        hc.Add(FracPower);
        return hc.ToHashCode();
    }

    public override string ToString() => ToPlainString();
}

/// <summary>
/// Exact symbolic algebraic expression represented as a linear combination of canonical BasisTerms
/// with arbitrary-precision BigRational coefficients.
/// </summary>
public sealed class SymbolicExpr : IEquatable<SymbolicExpr>
{
    private readonly ImmutableSortedDictionary<BasisTerm, BigRational> _terms;

    public static readonly SymbolicExpr Zero = new(ImmutableSortedDictionary<BasisTerm, BigRational>.Empty);
    public static readonly SymbolicExpr One = Constant(BigRational.One);

    private SymbolicExpr(ImmutableSortedDictionary<BasisTerm, BigRational> terms)
    {
        _terms = terms;
    }

    public IReadOnlyList<KeyValuePair<BasisTerm, BigRational>> Terms => _terms.ToList();

    public bool IsZero => _terms.Count == 0;

    public bool IsConstant => _terms.Count == 0 || (_terms.Count == 1 && _terms.ContainsKey(BasisTerm.One));

    public BigRational ConstantValue =>
        _terms.TryGetValue(BasisTerm.One, out var c) ? c : BigRational.Zero;

    public KeyValuePair<BasisTerm, BigRational>? LeadingTerm =>
        _terms.Count > 0 ? _terms.First() : null;

    public static SymbolicExpr Constant(BigRational value)
    {
        if (value.IsZero) return Zero;
        return new SymbolicExpr(ImmutableSortedDictionary<BasisTerm, BigRational>.Empty.Add(BasisTerm.One, value));
    }

    public static SymbolicExpr Constant(long value) => Constant(BigRational.FromInt(value));

    public static SymbolicExpr FromTerm(BasisTerm basis, BigRational coefficient)
    {
        if (coefficient.IsZero) return Zero;
        return new SymbolicExpr(ImmutableSortedDictionary<BasisTerm, BigRational>.Empty.Add(basis, coefficient));
    }

    public static SymbolicExpr Variable(string name, int power = 1) =>
        FromTerm(BasisTerm.Variable(name, power), BigRational.One);

    public static SymbolicExpr Log(string varName, int logBase = 2, int power = 1) =>
        FromTerm(BasisTerm.Log(varName, logBase, power), BigRational.One);

    public static SymbolicExpr Exp(BigRational expBase, string varName) =>
        FromTerm(BasisTerm.Exp(expBase, varName), BigRational.One);

    public static SymbolicExpr FractionalPower(string varName, int logBase, int logArg) =>
        FromTerm(BasisTerm.Fractional(varName, logBase, logArg), BigRational.One);

    public static SymbolicExpr FromTerms(IEnumerable<KeyValuePair<BasisTerm, BigRational>> terms)
    {
        var acc = new SortedDictionary<BasisTerm, BigRational>();
        foreach (var (basis, coeff) in terms)
        {
            if (coeff.IsZero) continue;
            acc[basis] = acc.TryGetValue(basis, out var existing) ? existing + coeff : coeff;
        }
        var builder = ImmutableSortedDictionary.CreateBuilder<BasisTerm, BigRational>();
        foreach (var (basis, coeff) in acc)
        {
            if (!coeff.IsZero) builder.Add(basis, coeff);
        }
        return builder.Count == 0 ? Zero : new SymbolicExpr(builder.ToImmutable());
    }

    public static implicit operator SymbolicExpr(int value) => Constant(value);
    public static implicit operator SymbolicExpr(long value) => Constant(value);
    public static implicit operator SymbolicExpr(BigRational value) => Constant(value);

    public static SymbolicExpr operator -(SymbolicExpr expr)
    {
        if (expr.IsZero) return Zero;
        var builder = ImmutableSortedDictionary.CreateBuilder<BasisTerm, BigRational>();
        foreach (var (b, c) in expr._terms) builder.Add(b, -c);
        return new SymbolicExpr(builder.ToImmutable());
    }

    public static SymbolicExpr operator +(SymbolicExpr a, SymbolicExpr b)
    {
        if (a.IsZero) return b;
        if (b.IsZero) return a;
        var builder = a._terms.ToBuilder();
        foreach (var (basis, coeff) in b._terms)
        {
            var next = builder.TryGetValue(basis, out var existing) ? existing + coeff : coeff;
            if (next.IsZero) builder.Remove(basis);
            else builder[basis] = next;
        }
        return builder.Count == 0 ? Zero : new SymbolicExpr(builder.ToImmutable());
    }

    public static SymbolicExpr operator -(SymbolicExpr a, SymbolicExpr b) => a + (-b);

    public static SymbolicExpr operator *(SymbolicExpr a, SymbolicExpr b)
    {
        if (a.IsZero || b.IsZero) return Zero;
        var acc = new SortedDictionary<BasisTerm, BigRational>();
        foreach (var (ba, ca) in a._terms)
        {
            foreach (var (bb, cb) in b._terms)
            {
                var prodBasis = ba.Multiply(bb);
                var prodCoeff = ca * cb;
                acc[prodBasis] = acc.TryGetValue(prodBasis, out var existing) ? existing + prodCoeff : prodCoeff;
            }
        }
        var builder = ImmutableSortedDictionary.CreateBuilder<BasisTerm, BigRational>();
        foreach (var (basis, coeff) in acc)
        {
            if (!coeff.IsZero) builder.Add(basis, coeff);
        }
        return builder.Count == 0 ? Zero : new SymbolicExpr(builder.ToImmutable());
    }

    public static SymbolicExpr operator /(SymbolicExpr a, BigRational scalar)
    {
        if (scalar.IsZero) throw new DivideByZeroException("Cannot divide symbolic expression by zero.");
        if (a.IsZero || scalar.IsOne) return a;
        var builder = ImmutableSortedDictionary.CreateBuilder<BasisTerm, BigRational>();
        foreach (var (basis, coeff) in a._terms)
        {
            builder.Add(basis, coeff / scalar);
        }
        return new SymbolicExpr(builder.ToImmutable());
    }

    public static SymbolicExpr operator /(SymbolicExpr a, SymbolicExpr divisor)
    {
        if (divisor.IsZero) throw new DivideByZeroException("Cannot divide symbolic expression by zero.");
        if (divisor._terms.Count != 1)
            throw new NotSupportedException($"Symbolic division only supports scalar or monomial divisors (got '{divisor.ToPlainString()}').");

        var (divBasis, divCoeff) = divisor._terms.First();
        if (divBasis.IsConstant) return a / divCoeff;

        var builder = ImmutableSortedDictionary.CreateBuilder<BasisTerm, BigRational>();
        foreach (var (basis, coeff) in a._terms)
        {
            if (!basis.TryDivideBy(divBasis, out var quotientBasis))
            {
                throw new InvalidOperationException(
                    $"Term '{basis.ToPlainString()}' is not evenly divisible by '{divBasis.ToPlainString()}'.");
            }
            builder.Add(quotientBasis, coeff / divCoeff);
        }
        return builder.Count == 0 ? Zero : new SymbolicExpr(builder.ToImmutable());
    }

    public SymbolicExpr Pow(int exponent)
    {
        if (exponent < 0) throw new ArgumentOutOfRangeException(nameof(exponent), "Exponent must be non-negative.");
        if (exponent == 0) return One;
        if (exponent == 1) return this;
        var result = One;
        var cur = this;
        var e = exponent;
        while (e > 0)
        {
            if ((e & 1) == 1) result *= cur;
            e >>= 1;
            if (e > 0) cur *= cur;
        }
        return result;
    }

    public bool ContainsVariable(string varName) =>
        _terms.Keys.Any(t => t.ContainsVariable(varName));

    public IReadOnlyList<string> GetVariables() =>
        _terms.Keys
            .SelectMany(t => t.GetVariables())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(v => v == "n" ? 0 : 1)
            .ThenBy(v => v, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Substitutes every occurrence of <paramref name="varName"/> with <paramref name="replacement"/>.
    /// </summary>
    public SymbolicExpr Substitute(string varName, SymbolicExpr replacement)
    {
        if (IsZero || !ContainsVariable(varName)) return this;

        var total = Zero;
        foreach (var (basis, coeff) in _terms)
        {
            var termVal = Constant(coeff);

            // Variable powers
            foreach (var (v, pow) in basis.VarPowers)
            {
                if (v == varName)
                    termVal *= replacement.Pow(pow);
                else
                    termVal *= Variable(v, pow);
            }

            // Fractional power
            if (basis.FracPower is { } fp)
            {
                if (fp.VarName == varName && TryAsSingleVariable(replacement, out var singleVar))
                    termVal *= FractionalPower(singleVar, fp.LogBase, fp.LogArg);
                else
                    termVal *= FractionalPower(fp.VarName, fp.LogBase, fp.LogArg);
            }

            // Logarithmic factors
            foreach (var l in basis.Logs)
            {
                if (l.VarName != varName)
                {
                    termVal *= Log(l.VarName, l.Base, l.Power);
                }
                else
                {
                    var singleLog = SubstituteLogArg(replacement, l.Base);
                    termVal *= singleLog.Pow(l.Power);
                }
            }

            // Exponential factors
            foreach (var e in basis.Exps)
            {
                if (e.VarName != varName)
                {
                    termVal *= Exp(e.Base, e.VarName);
                }
                else
                {
                    termVal *= SubstituteExpArg(e.Base, replacement);
                }
            }

            total += termVal;
        }

        return total;
    }

    /// <summary>
    /// Computes log_base(arg) symbolically for monomials like n, n^k, c*n, or constants.
    /// </summary>
    public static SymbolicExpr LogOf(SymbolicExpr arg, int logBase)
    {
        return SubstituteLogArg(arg, logBase);
    }

    private static SymbolicExpr SubstituteLogArg(SymbolicExpr arg, int logBase)
    {
        if (arg.IsConstant && arg.ConstantValue.IsInteger && arg.ConstantValue.Numerator > 0)
        {
            var val = arg.ConstantValue.Numerator;
            if (val == BigInteger.One) return Zero;
            var pow = 0;
            var cur = BigInteger.One;
            while (cur < val)
            {
                cur *= logBase;
                pow++;
            }
            if (cur == val) return Constant(pow);
        }

        // Single term c * v^k -> log_b(c) + k * log_b(v) (ignoring small constant shift if not exact power)
        if (arg._terms.Count == 1)
        {
            var (basis, coeff) = arg._terms.First();
            if (basis.VarPowers.Count == 1 && basis.Logs.Length == 0 && basis.Exps.Length == 0 && basis.FracPower is null)
            {
                var (vName, pow) = basis.VarPowers.First();
                var res = Constant(pow) * Log(vName, logBase);
                if (coeff.IsInteger && coeff.Numerator > 1)
                {
                    var cur = BigInteger.One;
                    var cPow = 0;
                    while (cur < coeff.Numerator)
                    {
                        cur *= logBase;
                        cPow++;
                    }
                    if (cur == coeff.Numerator) res += Constant(cPow);
                }
                return res;
            }
        }

        // Fallback: use leading variable of arg
        var vars = arg.GetVariables();
        var primary = vars.Count > 0 ? vars[0] : "n";
        return Log(primary, logBase);
    }

    /// <summary>
    /// Computes expBase^(arg) symbolically for integer constants or linear expressions v + c.
    /// </summary>
    public static SymbolicExpr ExpOf(BigRational expBase, SymbolicExpr arg) =>
        SubstituteExpArg(expBase, arg);

    private static SymbolicExpr SubstituteExpArg(BigRational expBase, SymbolicExpr arg)
    {
        if (arg.IsZero) return One;

        if (arg.IsConstant && arg.ConstantValue.IsInteger)
        {
            var expInt = (int)arg.ConstantValue.Numerator;
            return Constant(expBase.Pow(expInt));
        }

        // Support linear expression: c0 + sum_i (a_i * v_i) where a_i are integers
        var constantOffset = BigRational.One;
        var expFactors = ImmutableArray.CreateBuilder<ExpFactor>();
        var allLinear = true;

        foreach (var (basis, coeff) in arg._terms)
        {
            if (basis.IsConstant && coeff.IsInteger)
            {
                constantOffset *= expBase.Pow((int)coeff.Numerator);
            }
            else if (basis.VarPowers.Count == 1 &&
                     basis.VarPowers.First().Value == 1 &&
                     basis.Logs.Length == 0 &&
                     basis.Exps.Length == 0 &&
                     basis.FracPower is null &&
                     coeff.IsInteger)
            {
                var vName = basis.VarPowers.First().Key;
                var effectiveBase = expBase.Pow((int)coeff.Numerator);
                expFactors.Add(new ExpFactor(effectiveBase, vName));
            }
            else
            {
                allLinear = false;
                break;
            }
        }

        if (allLinear)
        {
            var term = new BasisTerm(
                ImmutableSortedDictionary<string, int>.Empty,
                ImmutableArray<LogFactor>.Empty,
                expFactors.ToImmutable(),
                null);
            return FromTerm(term, constantOffset);
        }

        // Special case: b^{log_b(v)} = v and a^{log_b(v)} = v^{log_b a}
        if (arg._terms.Count == 1)
        {
            var (basis, coeff) = arg._terms.First();
            if (basis.VarPowers.Count == 0 && basis.Logs.Length == 1 && basis.Logs[0].Power == 1 &&
                coeff.IsInteger && expBase.IsInteger && expBase.Numerator > 0)
            {
                var logF = basis.Logs[0];
                var effectiveBase = (int)BigInteger.Pow(expBase.Numerator, Math.Abs((int)coeff.Numerator));
                if (effectiveBase == logF.Base) return Variable(logF.VarName);
                return FractionalPower(logF.VarName, logF.Base, effectiveBase);
            }
        }

        throw new NotSupportedException($"Cannot symbolically exponentiate {expBase}^{arg.ToPlainString()}.");
    }

    private static bool TryAsSingleVariable(SymbolicExpr expr, out string varName)
    {
        if (expr._terms.Count == 1)
        {
            var (b, c) = expr._terms.First();
            if (c.IsOne && b.VarPowers.Count == 1 && b.VarPowers.First().Value == 1 &&
                b.Logs.Length == 0 && b.Exps.Length == 0 && b.FracPower is null)
            {
                varName = b.VarPowers.First().Key;
                return true;
            }
        }
        varName = string.Empty;
        return false;
    }

    public double EvaluateAt(double n) =>
        EvaluateAt(new Dictionary<string, double>(StringComparer.Ordinal) { ["n"] = n }, n);

    public double EvaluateAt(IReadOnlyDictionary<string, double> env, double defaultVal = 1.0)
    {
        var sum = 0.0;
        foreach (var (basis, coeff) in _terms)
        {
            sum += coeff.ToDouble() * basis.Evaluate(env, defaultVal);
        }
        return sum;
    }

    /// <summary>
    /// Compares two expressions asymptotically (for large positive inputs).
    /// Positive if a &gt; b asymptotically, negative if a &lt; b, 0 if identical.
    /// </summary>
    public static int CompareAsymptotic(SymbolicExpr a, SymbolicExpr b)
    {
        var diff = a - b;
        if (diff.IsZero) return 0;
        var lead = diff.LeadingTerm!.Value;
        return lead.Value.IsPositive ? 1 : -1;
    }

    public static SymbolicExpr Max(SymbolicExpr a, SymbolicExpr b) =>
        CompareAsymptotic(a, b) >= 0 ? a : b;

    public static SymbolicExpr Min(SymbolicExpr a, SymbolicExpr b) =>
        CompareAsymptotic(a, b) <= 0 ? a : b;

    public string ToPlainString()
    {
        if (IsZero) return "0";
        var sb = new StringBuilder();
        var first = true;

        foreach (var (basis, coeff) in _terms)
        {
            var absCoeff = coeff.Abs();
            var isNeg = coeff.IsNegative;

            if (first)
            {
                if (isNeg) sb.Append('-');
                first = false;
            }
            else
            {
                sb.Append(isNeg ? " - " : " + ");
            }

            if (basis.IsConstant)
            {
                sb.Append(absCoeff.ToPlainString());
            }
            else
            {
                var basisStr = basis.ToPlainString();
                if (absCoeff.IsOne)
                {
                    sb.Append(basisStr);
                }
                else if (absCoeff.IsInteger)
                {
                    sb.Append(absCoeff.ToPlainString());
                    if (basis.StartsWithLog) sb.Append(' ');
                    else if (basis.StartsWithExp) sb.Append(" * ");
                    sb.Append(basisStr);
                }
                else
                {
                    sb.Append('(').Append(absCoeff.ToPlainString()).Append(')');
                    if (basis.StartsWithLog) sb.Append(' ');
                    else if (basis.StartsWithExp) sb.Append(" * ");
                    sb.Append(basisStr);
                }
            }
        }

        return sb.ToString();
    }

    public string ToLatexString()
    {
        if (IsZero) return "0";
        var sb = new StringBuilder();
        var first = true;

        foreach (var (basis, coeff) in _terms)
        {
            var absCoeff = coeff.Abs();
            var isNeg = coeff.IsNegative;

            if (first)
            {
                if (isNeg) sb.Append('-');
                first = false;
            }
            else
            {
                sb.Append(isNeg ? " - " : " + ");
            }

            if (basis.IsConstant)
            {
                sb.Append(absCoeff.ToLatexString());
            }
            else
            {
                var basisStr = basis.ToLatexString();
                if (absCoeff.IsOne)
                {
                    sb.Append(basisStr);
                }
                else
                {
                    sb.Append(absCoeff.ToLatexString());
                    if (basis.StartsWithLog) sb.Append(' ');
                    else if (basis.StartsWithExp) sb.Append(@" \cdot ");
                    sb.Append(basisStr);
                }
            }
        }

        return sb.ToString();
    }

    public bool Equals(SymbolicExpr? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        if (_terms.Count != other._terms.Count) return false;
        foreach (var (k, v) in _terms)
        {
            if (!other._terms.TryGetValue(k, out var ov) || ov != v) return false;
        }
        return true;
    }

    public override bool Equals(object? obj) => obj is SymbolicExpr se && Equals(se);

    public override int GetHashCode()
    {
        var hc = new HashCode();
        foreach (var (k, v) in _terms)
        {
            hc.Add(k);
            hc.Add(v);
        }
        return hc.ToHashCode();
    }

    public override string ToString() => ToPlainString();
}

/// <summary>
/// Recursive-descent parser turning loop bound strings (e.g. "n+10", "n*5", "n*n", "log_2(n)", "2^n")
/// into exact SymbolicExpr instances.
/// </summary>
public static class SymbolicParser
{
    public static SymbolicExpr Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return SymbolicExpr.Zero;
        var parser = new ParserState(text.Trim());
        var expr = parser.ParseAdditive();
        return expr;
    }

    /// <summary>
    /// Counts explicit arithmetic operators (+, -, *, /, ^) inside a loop bound expression string.
    /// </summary>
    public static int CountArithmeticOps(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 0;
        var s = text.Trim();
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is '+' or '*' or '/' or '^')
            {
                count++;
            }
            else if (ch == '-')
            {
                // Count binary minus, ignore leading unary minus on a negative literal
                var prevIdx = i - 1;
                while (prevIdx >= 0 && char.IsWhiteSpace(s[prevIdx])) prevIdx--;
                if (prevIdx >= 0 && (char.IsLetterOrDigit(s[prevIdx]) || s[prevIdx] == ')'))
                {
                    count++;
                }
            }
        }
        return count;
    }

    private sealed class ParserState
    {
        private readonly string _src;
        private int _pos;

        public ParserState(string src) => _src = src;

        private void SkipWs()
        {
            while (_pos < _src.Length && char.IsWhiteSpace(_src[_pos])) _pos++;
        }

        private char Peek()
        {
            SkipWs();
            return _pos < _src.Length ? _src[_pos] : '\0';
        }

        private bool Match(char c)
        {
            SkipWs();
            if (_pos < _src.Length && _src[_pos] == c)
            {
                _pos++;
                return true;
            }
            return false;
        }

        public SymbolicExpr ParseAdditive()
        {
            var left = ParseMultiplicative();
            while (true)
            {
                if (Match('+')) left += ParseMultiplicative();
                else if (Match('-')) left -= ParseMultiplicative();
                else break;
            }
            return left;
        }

        private SymbolicExpr ParseMultiplicative()
        {
            var left = ParsePower();
            while (true)
            {
                SkipWs();
                if (Match('*'))
                {
                    left *= ParsePower();
                }
                else if (Match('/'))
                {
                    var right = ParsePower();
                    left /= right;
                }
                else if (_pos < _src.Length && (char.IsLetter(_src[_pos]) || _src[_pos] == '('))
                {
                    // Implicit multiplication such as "2n" or "2(n+1)" or "n log_2 n"
                    left *= ParsePower();
                }
                else
                {
                    break;
                }
            }
            return left;
        }

        private SymbolicExpr ParsePower()
        {
            var baseExpr = ParseUnary();
            if (Match('^'))
            {
                var expExpr = ParseUnary();
                if (expExpr.IsConstant && expExpr.ConstantValue.IsInteger && expExpr.ConstantValue.Numerator >= 0)
                {
                    return baseExpr.Pow((int)expExpr.ConstantValue.Numerator);
                }
                if (baseExpr.IsConstant)
                {
                    return SymbolicExpr.ExpOf(baseExpr.ConstantValue, expExpr);
                }
                throw new FormatException($"Unsupported exponent expression in '{_src}'.");
            }
            return baseExpr;
        }

        private SymbolicExpr ParseUnary()
        {
            if (Match('+')) return ParseUnary();
            if (Match('-')) return -ParseUnary();
            return ParsePrimary();
        }

        private SymbolicExpr ParsePrimary()
        {
            SkipWs();
            if (Match('('))
            {
                var inner = ParseAdditive();
                Match(')');
                return inner;
            }

            if (_pos < _src.Length && char.IsDigit(_src[_pos]))
            {
                var start = _pos;
                while (_pos < _src.Length && char.IsDigit(_src[_pos])) _pos++;
                var numStr = _src[start.._pos];
                return SymbolicExpr.Constant(new BigRational(BigInteger.Parse(numStr, CultureInfo.InvariantCulture), BigInteger.One));
            }

            if (_pos < _src.Length && (char.IsLetter(_src[_pos]) || _src[_pos] == '_'))
            {
                var start = _pos;
                while (_pos < _src.Length && (char.IsLetterOrDigit(_src[_pos]) || _src[_pos] == '_')) _pos++;
                var ident = _src[start.._pos];

                // Handle log_2(n), log_10 n, log2(n), log(n)
                if (TryParseLogPrefix(ident, out var logBase))
                {
                    SkipWs();
                    SymbolicExpr arg;
                    if (Match('('))
                    {
                        arg = ParseAdditive();
                        Match(')');
                    }
                    else
                    {
                        arg = ParsePrimary();
                    }
                    return SymbolicExpr.LogOf(arg, logBase);
                }

                return SymbolicExpr.Variable(ident);
            }

            throw new FormatException($"Unexpected token at position {_pos} in '{_src}'.");
        }

        private static bool TryParseLogPrefix(string ident, out int logBase)
        {
            logBase = 2;
            if (ident.Equals("log", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            if (ident.StartsWith("log_", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(ident[4..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b1) &&
                b1 >= 2)
            {
                logBase = b1;
                return true;
            }
            if (ident.StartsWith("log", StringComparison.OrdinalIgnoreCase) &&
                ident.Length > 3 &&
                int.TryParse(ident[3..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var b2) &&
                b2 >= 2)
            {
                logBase = b2;
                return true;
            }
            return false;
        }
    }
}
