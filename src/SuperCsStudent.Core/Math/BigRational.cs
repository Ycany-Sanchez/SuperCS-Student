using System.Numerics;

namespace SuperCsStudent.Core.Math;

/// <summary>
/// Exact arbitrary-precision rational number with normalized positive denominator.
/// </summary>
public readonly record struct BigRational : IComparable<BigRational>
{
    public BigInteger Numerator { get; }
    public BigInteger Denominator { get; }

    public static readonly BigRational Zero = new(0, 1);
    public static readonly BigRational One = new(1, 1);
    public static readonly BigRational MinusOne = new(-1, 1);

    public BigRational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
        {
            throw new DivideByZeroException("Rational denominator cannot be zero.");
        }

        if (numerator.IsZero)
        {
            Numerator = BigInteger.Zero;
            Denominator = BigInteger.One;
            return;
        }

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = BigInteger.GreatestCommonDivisor(BigInteger.Abs(numerator), denominator);
        Numerator = numerator / gcd;
        Denominator = denominator / gcd;
    }

    public static BigRational FromInt(long value) => new(value, 1);

    public bool IsZero => Numerator.IsZero;
    public bool IsOne => Numerator == BigInteger.One && Denominator == BigInteger.One;
    public bool IsInteger => Denominator == BigInteger.One;
    public bool IsNegative => Numerator.Sign < 0;
    public bool IsPositive => Numerator.Sign > 0;

    public BigRational Abs() => IsNegative ? -this : this;

    public double ToDouble() => (double)Numerator / (double)Denominator;

    public BigRational Pow(int exponent)
    {
        if (exponent == 0) return One;
        if (exponent < 0)
        {
            return new BigRational(
                BigInteger.Pow(Denominator, -exponent),
                BigInteger.Pow(Numerator, -exponent));
        }

        return new BigRational(
            BigInteger.Pow(Numerator, exponent),
            BigInteger.Pow(Denominator, exponent));
    }

    public static implicit operator BigRational(int value) => new(value, 1);
    public static implicit operator BigRational(long value) => new(value, 1);

    public static BigRational operator -(BigRational r) => new(-r.Numerator, r.Denominator);

    public static BigRational operator +(BigRational a, BigRational b) =>
        new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static BigRational operator -(BigRational a, BigRational b) =>
        new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static BigRational operator *(BigRational a, BigRational b) =>
        new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static BigRational operator /(BigRational a, BigRational b)
    {
        if (b.IsZero) throw new DivideByZeroException();
        return new BigRational(a.Numerator * b.Denominator, a.Denominator * b.Numerator);
    }

    public int CompareTo(BigRational other) =>
        (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public static bool operator <(BigRational a, BigRational b) => a.CompareTo(b) < 0;
    public static bool operator >(BigRational a, BigRational b) => a.CompareTo(b) > 0;
    public static bool operator <=(BigRational a, BigRational b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BigRational a, BigRational b) => a.CompareTo(b) >= 0;

    public string ToPlainString() =>
        IsInteger ? Numerator.ToString() : $"{Numerator}/{Denominator}";

    public string ToLatexString()
    {
        if (IsInteger) return Numerator.ToString();
        if (IsNegative) return $@"-\frac{{{BigInteger.Abs(Numerator)}}}{{{Denominator}}}";
        return $@"\frac{{{Numerator}}}{{{Denominator}}}";
    }

    public override string ToString() => ToPlainString();
}
