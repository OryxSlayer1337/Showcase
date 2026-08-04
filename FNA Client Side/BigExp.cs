// BigExp — 16-byte zero-allocation exponential scalar.
//
// Storage is exactly two doubles (16 bytes, no boxing, no GC):
//   mantissa:   magnitude in [1,10); sign carried by the double; 0 = zero
//   exponent10: decimal exponent, itself a double (nested-log staging)
// Value  =  mantissa × 10^exponent10
//
// Because the exponent is a double, the exponent itself can reach ~1.8e308,
// so the representable range spans 10^(-10^308) .. 10^(10^308) — far past a
// googolplex (10^10^100) and past the double's own ~1.8e308 ceiling.
//
// This is the compact "logarithmic tier" of the number system:
//   • Tier 1 (exact):  BigIntChunked / BigDouble string math — pixel-perfect,
//                      zero-alloc, for stats ≤ ~10^462 (RAM-bounded digits).
//   • Tier 2 (approx): this type — 16-byte log math for fill-ratio / gauge /
//                      display work at googolplex scale and beyond.
//
// Approximate by design: mantissa carries ~15-16 significant digits. It is NOT
// a replacement for exact math — it is the fast path for magnitudes where exact
// digit strings cannot exist in RAM anyway.
//
// AS3: none (log-tier concept is FNA-native).

using System;
using System.Globalization;
using System.Numerics;

namespace VortexClient.Core.Numbers
{
    /// <summary>
    /// 16-byte exponential scalar: mantissa × 10^exponent10, where exponent10 is
    /// a double so the range reaches 10^(±10^308) (googolplex and beyond).
    /// </summary>
    public readonly struct BigExp : IEquatable<BigExp>, IComparable<BigExp>
    {
        private readonly double mantissa;   // magnitude in [1,10), sign-bit; 0 = zero
        private readonly double exponent10; // decimal exponent (double, nested-log)

        /// <summary>Never expand an exact decimal string longer than this.</summary>
        private const int MaxExpansion = 1 << 20;

        /// <summary>Exponent gap ≤ this → align mantissas for Add/Sub; larger gaps
        /// make the smaller operand negligible (10^15 is the largest exact power of 10).</summary>
        private const double AlignLimit = 15.0;

        public static readonly BigExp Zero = new BigExp(0.0, 0.0);
        public static readonly BigExp One = new BigExp(1.0, 0.0);

        public bool IsZero => mantissa == 0;
        public bool IsNegative => mantissa < 0;
        public double ApproxMantissa => Math.Abs(mantissa);
        public double ApproxExponent10 => exponent10;

        private BigExp(double mantissa, double exponent10)
        {
            this.mantissa = mantissa;
            this.exponent10 = exponent10;
        }

        // ── Parsing ─────────────────────────────────────────────────────

        /// <summary>
        /// Accepts "1234.5", "2.5e300", "1e-1000" and power-tower notation like
        /// "1e1e100" (mantissa "1", exponent "1e100" = 10^100 → 10^10^100).
        /// The exponent text is parsed as a double, so it may itself contain 'e'.
        /// </summary>
        public static bool TryParse(string s, out BigExp result)
        {
            result = Zero;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            string t = s.Trim().Replace(",", "").Replace("_", "");
            if (t.Length == 0)
                return false;

            int ePos = t.IndexOfAny(new[] { 'e', 'E' });
            string mantissaText;
            string exponentText;
            if (t.EndsWith("gp", StringComparison.OrdinalIgnoreCase))
            {
                mantissaText = t.Substring(0, t.Length - 2);
                exponentText = "1e100";
            }
            else if (ePos < 0)
            {
                mantissaText = t;
                exponentText = null;
            }
            else
            {
                mantissaText = t.Substring(0, ePos);
                exponentText = t.Substring(ePos + 1).Trim();
                if (exponentText.Length >= 2 && exponentText[0] == '(' && exponentText[exponentText.Length - 1] == ')')
                    exponentText = exponentText.Substring(1, exponentText.Length - 2);
                if (mantissaText.Length == 0 || exponentText.Length == 0)
                    return false;
            }

            if (!double.TryParse(mantissaText, NumberStyles.Float,
                                 CultureInfo.InvariantCulture, out double m) ||
                !double.IsFinite(m))
                return false;

            double e = 0;
            if (exponentText != null)
            {
                if (!double.TryParse(exponentText, NumberStyles.Float,
                                     CultureInfo.InvariantCulture, out e) ||
                    !double.IsFinite(e))
                    return false;
            }

            if (m == 0)
            {
                result = Zero;
                return true;
            }

            result = Normalize(m, e);
            return true;
        }

        public static BigExp Parse(string s)
        {
            if (!TryParse(s, out var result))
                throw new FormatException("BigExp: invalid number '" + s + "'");
            return result;
        }

        /// <summary>Bring mantissa into [1,10) by shifting the exponent. Saturated
        /// when the exponent would overflow the double range.</summary>
        private static BigExp Normalize(double m, double e)
        {
            if (m == 0 || double.IsNaN(m))
                return Zero;

            double abs = Math.Abs(m);
            int guard = 0;
            while (abs >= 10 && guard++ < 1024)
            {
                abs /= 10;
                e++;
            }
            while (abs > 0 && abs < 1 && guard++ < 1024)
            {
                abs *= 10;
                e--;
            }

            if (double.IsInfinity(e) || double.IsNaN(e))
                e = e > 0 ? double.MaxValue : -double.MaxValue;

            return new BigExp(m < 0 ? -abs : abs, e);
        }

        // ── Comparison ──────────────────────────────────────────────────

        public int CompareTo(BigExp other)
        {
            if (IsZero && other.IsZero)
                return 0;
            if (IsNegative != other.IsNegative)
                return IsNegative ? -1 : 1;

            int cmp = CompareMagnitude(other);
            return IsNegative ? -cmp : cmp;
        }

        private int CompareMagnitude(BigExp other)
        {
            if (IsZero) return other.IsZero ? 0 : -1;
            if (other.IsZero) return 1;

            if (exponent10 != other.exponent10)
                return exponent10 > other.exponent10 ? 1 : -1;
            if (mantissa != other.mantissa)
                return mantissa > other.mantissa ? 1 : -1;
            return 0;
        }

        public bool Equals(BigExp other)
        {
            if (IsZero && other.IsZero)
                return true;
            return mantissa == other.mantissa && exponent10 == other.exponent10;
        }

        public override bool Equals(object? obj)
            => obj is BigExp be && Equals(be);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = mantissa.GetHashCode();
                h = (h * 397) ^ exponent10.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(BigExp a, BigExp b) => a.Equals(b);
        public static bool operator !=(BigExp a, BigExp b) => !a.Equals(b);
        public static bool operator <(BigExp a, BigExp b) => a.CompareTo(b) < 0;
        public static bool operator >(BigExp a, BigExp b) => a.CompareTo(b) > 0;
        public static bool operator <=(BigExp a, BigExp b) => a.CompareTo(b) <= 0;
        public static bool operator >=(BigExp a, BigExp b) => a.CompareTo(b) >= 0;

        // ── Arithmetic (pure double math, zero allocation) ──────────────

        public static BigExp Add(BigExp a, BigExp b)
        {
            if (a.IsZero) return b;
            if (b.IsZero) return a;

            double e1 = a.exponent10, e2 = b.exponent10;
            double diff = e1 - e2;
            if (diff > AlignLimit || diff < -AlignLimit)
            {
                // The smaller operand is < 10^-15 relative — negligible.
                // Larger exponent ⇒ larger magnitude (mantissas are normalized ≥1).
                return e1 > e2 ? a : b;
            }

            double m = a.mantissa + b.mantissa * Math.Pow(10, -diff);
            return Normalize(m, e1);
        }

        public static BigExp Subtract(BigExp a, BigExp b)
        {
            if (b.IsZero) return a;
            if (a.IsZero) return new BigExp(-b.mantissa, b.exponent10);
            return Add(a, new BigExp(-b.mantissa, b.exponent10));
        }

        public static BigExp Multiply(BigExp a, BigExp b)
        {
            if (a.IsZero || b.IsZero) return Zero;
            return Normalize(a.mantissa * b.mantissa, a.exponent10 + b.exponent10);
        }

        public static BigExp DivideByInt(BigExp a, int n)
        {
            if (n == 0) throw new DivideByZeroException();
            if (a.IsZero) return Zero;
            if (n == 1) return a;
            return Normalize(a.mantissa / n, a.exponent10);
        }

        public static BigExp MultiplyByInt(BigExp a, int n)
        {
            if (n == 0) return Zero;
            if (a.IsZero) return Zero;
            if (n == 1) return a;
            return Normalize(a.mantissa * n, a.exponent10);
        }

        public static BigExp Negate(BigExp a)
            => new BigExp(a.IsZero ? 0 : -a.mantissa, a.exponent10);

        public static BigExp operator +(BigExp a, BigExp b) => Add(a, b);
        public static BigExp operator -(BigExp a, BigExp b) => Subtract(a, b);
        public static BigExp operator *(BigExp a, BigExp b) => Multiply(a, b);
        public static BigExp operator /(BigExp a, int n) => DivideByInt(a, n);
        public static BigExp operator *(BigExp a, int n) => MultiplyByInt(a, n);
        public static BigExp operator -(BigExp a) => Negate(a);

        // ── Conversions ─────────────────────────────────────────────────

        /// <summary>Approx double for gauges/fill ratios. Saturated to
        /// ±double.MaxValue instead of overflowing to Infinity.</summary>
        public double ToDouble()
        {
            if (IsZero) return 0;

            if (exponent10 > 308)
                return IsNegative ? -double.MaxValue : double.MaxValue;
            if (exponent10 < -324)
                return 0;

            double value = mantissa * Math.Pow(10, exponent10);
            if (double.IsInfinity(value))
                return IsNegative ? -double.MaxValue : double.MaxValue;
            return value;
        }

        /// <summary>Saturated integer view (never throws).</summary>
        public int ToInt32()
        {
            if (IsZero) return 0;
            if (IsNegative) return ToDouble() <= int.MinValue ? int.MinValue : (int)ToDouble();
            return ToDouble() >= int.MaxValue ? int.MaxValue : (int)ToDouble();
        }

        public int ToInt() => ToInt32();

        /// <summary>Saturated integer view (never throws).</summary>
        public long ToInt64()
        {
            if (IsZero) return 0;
            double v = ToDouble();
            if (v >= long.MaxValue) return long.MaxValue;
            if (v <= long.MinValue) return long.MinValue;
            return (long)v;
        }

        /// <summary>Lossless bridge to the exact BigDouble tier: mantissa digits +
        /// BigInteger exponent. Only valid when the exponent is integral (all normal
        /// BigExp values); non-integral exponents are snap-rounded.</summary>
        public BigDouble ToBigDouble()
        {
            if (IsZero)
                return BigDouble.Zero;
            if (double.IsInfinity(exponent10))
                return BigDouble.FromApprox(ApproxMantissa,
                    new BigInteger(double.MaxValue), IsNegative);
            if (double.IsNaN(exponent10))
                return BigDouble.Zero;
            BigInteger e = new BigInteger(Math.Round(exponent10, MidpointRounding.AwayFromZero));
            return BigDouble.FromApprox(ApproxMantissa, e, IsNegative);
        }

        /// <summary>Exact tier → this compact tier (approx by design).</summary>
        public static BigExp FromBigDouble(BigDouble v)
        {
            if (v.IsZero)
                return Zero;
            double m = v.ApproxMantissa;
            double e;
            try { e = (double)v.ApproxExponent10; }
            catch (OverflowException) { e = v.IsNegative ? -double.MaxValue : double.MaxValue; }
            if (double.IsInfinity(e) || double.IsNaN(e))
                e = e > 0 ? double.MaxValue : -double.MaxValue;
            return Normalize(v.IsNegative ? -m : m, e);
        }

        // ── Display ─────────────────────────────────────────────────────

        /// <summary>Expands to a plain decimal when the exponent is small enough,
        /// otherwise emits simple scientific notation; nested exponents render
        /// as-is: "1e1e100" = 10^10^100 (abbreviated "gp").</summary>
        public override string ToString()
        {
            if (IsZero)
                return "0";
            if (exponent10 >= -MaxExpansion && exponent10 <= MaxExpansion)
                return ExpandDecimal(ApproxMantissa, (int)exponent10, IsNegative);
            return ToScientific(12);
        }

        private static string ExpandDecimal(double m, int e, bool neg)
        {
            string digits = MantissaDigits(m);
            string body;
            int intLen = 1 + e; // integer digits (m has one digit before its point)
            if (intLen <= 0)
                body = "0." + new string('0', -intLen) + digits;
            else if (intLen >= digits.Length)
                body = digits + new string('0', intLen - digits.Length);
            else
                body = digits.Substring(0, intLen) + "." + digits.Substring(intLen);
            return (neg ? "-" : "") + body;
        }

        /// <summary>Significant digits of the mantissa, rounded to 15 places so
        /// binary float noise never leaks into the rendered string.</summary>
        private static string MantissaDigits(double m)
        {
            return m.ToString("G15", CultureInfo.InvariantCulture).Replace(".", "");
        }

        /// <summary>Scientific form: "1.23e308"; nested exponents stack as-is:
        /// "1e1e100" (10^10^100 = googolplex). decimals &lt; 0 means keep every digit.</summary>
        public string ToScientific(int decimals = 0)
        {
            if (IsZero)
                return "0";

            string m = FormatMantissa(ApproxMantissa, decimals);
            if (decimals != 0 && m.Length > 1)
            {
                m = m.TrimEnd('0');
                if (m.EndsWith("."))
                    m = m.Substring(0, m.Length - 1);
            }
            string e = FormatExponent(exponent10);
            return (IsNegative ? "-" : "") + m + "e" + e;
        }

        /// <summary>Exponent at which a value is one or more googolplexes (10^10^100).</summary>
        private const double GoogolplexExponent = 1e100;

        /// <summary>Display abbreviation: everything below the googolplex tier stays
        /// simple scientific ("1.5e308"); 10^10^100 and beyond render as "mantissa gp"
        /// (10^10^100 = "1gp"). Never alters the wire/round-trip forms.</summary>
        public string ToAbbreviated(int decimals = 2)
        {
            if (IsZero)
                return "0";
            if (exponent10 < GoogolplexExponent)
                return ToString();
            string m = FormatMantissa(ApproxMantissa, decimals);
            if (decimals != 0 && m.Length > 1)
            {
                m = m.TrimEnd('0');
                if (m.EndsWith("."))
                    m = m.Substring(0, m.Length - 1);
            }
            return (IsNegative ? "-" : "") + m + "gp";
        }

        private static string FormatMantissa(double m, int decimals)
        {
            if (decimals < 0)
                return m.ToString("R", CultureInfo.InvariantCulture);
            if (decimals == 0)
                return m.ToString("0", CultureInfo.InvariantCulture);
            return m.ToString("0." + new string('#', decimals), CultureInfo.InvariantCulture);
        }

        private static string FormatExponent(double e)
        {
            string s = e.ToString("R", CultureInfo.InvariantCulture);
            return s.Replace("E", "e").Replace("+", "");
        }

        /// <summary>Same as ToString but with thousands separators (when expanded).</summary>
        public string ToGroupedString()
        {
            if (IsZero)
                return "0";
            if (exponent10 < -MaxExpansion || exponent10 > MaxExpansion)
                return ToString();
            string s = ExpandDecimal(ApproxMantissa, (int)exponent10, IsNegative);
            int dot = s.IndexOf('.');
            int intLen = dot < 0 ? s.Length - (IsNegative ? 1 : 0) : dot - (IsNegative ? 1 : 0);
            var builder = new System.Text.StringBuilder();
            int start = IsNegative ? 1 : 0;
            for (int i = 0; i < intLen; i++)
            {
                if (i > 0 && (intLen - i) % 3 == 0)
                    builder.Append(',');
                builder.Append(s[start + i]);
            }
            if (dot >= 0)
                builder.Append(s.Substring(dot + 1 - (IsNegative ? 1 : 0)));
            return (IsNegative ? "-" : "") + builder.ToString();
        }
    }
}
