// BigDouble — hybrid scalar for the abbreviation system.
//
// Holds BOTH components of a big number:
//   1. Exact decimal string: integer digits plus fractional digit count. Capable
//      of exact values up to the RAM limit for the digit string (~10^n, n bounded
//      by MaxExpansion for on-screen expansion; storage itself is unbounded).
//   2. Approx BigInteger part: mantissa (10^1 range) + BigInteger exponent10.
//      Only derived data at parse/update time, reporting values the game commonly
//      compresses. The exponent is BigInteger, so the approximate range reaches
//      ~10^(10^462) and far beyond — there is no 32-bit (or 64-bit) exponent cap.
//
// Per AGENTS.md Critical Code Rule #1 the exponent is BigInteger (never an int) so
// no overflow can ever occur; the exact digit path remains pure string math
// (BigIntChunked / NumberDisplay). The double approximation exists for UI
// abbreviation and fill-ratio math only.
//
// Note: there was no "BigDouble" class in the AS3 client - legacy used purely
// numeric strings. This FNA-native hybrid wraps the exact decimal string and the
// derived approximate exponent pair in one immutable struct.
// AS3 variant         C# variant
//   (none)            digits: (string) exact significant digits
//   (none)            intLen: (BigInteger) decimal-point position
//   (none)            negative: (bool) sign
//   (none)            mantissa: (double) approx mantissa in [1,10)
//   (none)            exponent10: (BigInteger) approx decimal exponent

using System;
using System.Globalization;
using System.Numerics;
using VortexClient.Core;

namespace VortexClient.Core.Numbers
{
    /// <summary>
    /// Immutable hybrid number: exact decimal string + cached double/BigInteger
    /// approximation. Exponent storage is BigInteger so large magnitudes never
    /// cap out (up to 10^10^462+ for the approx path).
    /// </summary>
    public readonly struct BigDouble : IEquatable<BigDouble>, IComparable<BigDouble>
    {
        // ── Storage ─────────────────────────────────────────────────────
        // digits:      exact digits, no sign, no decimal point, leading zeros trimmed
        //              ("0" when the value is zero)
        // intLen:      number of leading digits belonging to the integer part
        //              (BigInteger: may be astronomically large)
        // negative:    sign flag (0 is never negative)
        // mantissa:    approximate mantissa in [1,10); 0 when value is zero
        // exponent10:  decimal exponent of the approximation (mantissa * 10^exp)
        //              (BigInteger: no int cap)

        private readonly string digits;
        private readonly BigInteger intLen;
        private readonly bool negative;
        private readonly double mantissa;
        private readonly BigInteger exponent10;

        /// <summary>Never expand an exact decimal string longer than this (prevents
        /// OOM bomb on ToString/abbreviation of astronomically large magnitudes).</summary>
        private const int MaxExpansion = 1 << 20;

        /// <summary>Alignment padding budget for Add/Subtract. Beyond this the gap is
        /// treated as non-contributing (the larger operand wins).</summary>
        private const int MaxAlignPad = 1 << 20;

        public static readonly BigDouble Zero = Parse("0");
        public static readonly BigDouble One = Parse("1");

        public bool IsZero => digits == "0";
        public bool IsNegative => negative && digits != "0";
        public double ApproxMantissa => mantissa;
        public BigInteger ApproxExponent10 => exponent10;
        public string Digits => digits;
        public BigInteger IntPartLength => intLen;

        private BigDouble(string digits, BigInteger intLen, bool negative,
                          double mantissa, BigInteger exponent10)
        {
            this.digits = digits;
            this.intLen = intLen;
            this.negative = negative;
            this.mantissa = mantissa;
            this.exponent10 = exponent10;
        }

        // ── Parsing ─────────────────────────────────────────────────────

        /// <summary>
        /// Builds an exact BigDouble from an approximate (mantissa, exponent) pair —
        /// the bridge from the 16-byte BigExp tier back into the exact tier.
        /// mantissa is in [1,10); its round-trip digits become the exact digit
        /// string and the exponent becomes the BigInteger decimal exponent.
        /// </summary>
        public static BigDouble FromApprox(double mantissa, BigInteger exponent10,
                                           bool negative = false)
        {
            if (mantissa == 0 || !double.IsFinite(mantissa))
                return Zero;

            string m = mantissa.ToString("R", CultureInfo.InvariantCulture);
            if (m.Length == 0)
                return Zero;

            int dot = m.IndexOf('.');
            string digits = dot < 0 ? m : m.Remove(dot, 1);
            int fracDigits = dot < 0 ? 0 : m.Length - dot - 1;

            digits = TrimLeadingZeros(digits);
            if (digits == "0")
                return Zero;

            BigInteger exp = exponent10 - fracDigits;
            BigInteger intLen = digits.Length + exp;

            double m2;
            BigInteger e2;
            ComputeApprox(digits, exp, out m2, out e2);
            return new BigDouble(digits, intLen, negative, m2, e2);
        }

        public static BigDouble Parse(string s)
        {
            if (!TryParse(s, out var result))
                throw new FormatException("BigDouble: invalid number '" + s + "'");
            return result;
        }

        public static bool TryParse(string s, out BigDouble result)
        {
            result = Zero;
            if (string.IsNullOrWhiteSpace(s))
                return false;

            string t = s.Trim().Replace(",", "").Replace("_", "");
            bool neg = false;
            if (t.Length > 0 && (t[0] == '-' || t[0] == '+'))
            {
                neg = t[0] == '-';
                t = t.Substring(1);
            }
            if (t.Length == 0)
                return false;

            int ePos = t.IndexOfAny(new[] { 'e', 'E' });
            BigInteger exp10 = BigInteger.Zero;
            if (ePos >= 0)
            {
                string expText = t.Substring(ePos + 1);
                if (expText.Length == 0)
                    return false;
                if (!BigInteger.TryParse(expText, NumberStyles.AllowLeadingSign,
                                         CultureInfo.InvariantCulture, out exp10))
                    return false;
                t = t.Substring(0, ePos);
            }

            int dot = t.IndexOf('.');
            if (dot >= 0)
            {
                exp10 -= t.Length - dot - 1;
                t = t.Remove(dot, 1);
            }

            if (t.Length == 0 || !IsAllDigits(t))
                return false;

            string digits = TrimLeadingZeros(t);
            if (digits == "0")
                neg = false;

            BigInteger intLen = digits.Length + exp10;

            // Approximation from leading significant digits (unbounded exponent).
            double m;
            BigInteger e;
            ComputeApprox(digits, exp10, out m, out e);

            result = new BigDouble(digits, intLen, neg, m, e);
            return true;
        }

        private static bool IsAllDigits(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if ((uint)(c - '0') > 9)
                    return false;
            }
            return true;
        }

        private static string TrimLeadingZeros(string s)
        {
            int i = 0;
            while (i < s.Length - 1 && s[i] == '0')
                i++;
            return s.Substring(i);
        }

        private static void ComputeApprox(string digits, BigInteger exp10,
                                          out double mantissa, out BigInteger exponent)
        {
            if (digits == "0")
            {
                mantissa = 0;
                exponent = BigInteger.Zero;
                return;
            }
            int take = Math.Min(digits.Length, 17);
            double d = double.Parse(digits.Substring(0, take),
                                     CultureInfo.InvariantCulture);
            int leftover = digits.Length - take;
            BigInteger totalExp = exp10 + leftover;
            // normalize to [1,10); the loop count is bounded by `take`.
            while (d >= 10)
            {
                d /= 10;
                totalExp++;
            }
            while (d > 0 && d < 1)
            {
                d *= 10;
                totalExp--;
            }
            mantissa = d;
            exponent = totalExp;
        }

        // ── Exact string representation ────────────────────────────────

        public override string ToString()
        {
            if (IsZero)
                return "0";

            // Never expand beyond the RAM budget; fall back to scientific when the
            // exact decimal string would be absurdly long.
            bool fitsInt = intLen <= MaxExpansion && intLen >= -MaxExpansion;
            if (fitsInt)
            {
                long il = (long)intLen;
                if (il <= 0)
                {
                    // value is a pure fraction: "0.000123"
                    int zeros = (int)(-il);
                    if (digits.Length + zeros + 2 > MaxExpansion)
                        fitsInt = false;
                    else
                    {
                        string body = "0." + new string('0', zeros) + digits;
                        return negative ? "-" + body : body;
                    }
                }
                else if (il >= digits.Length)
                {
                    string body = digits + new string('0', (int)(il - digits.Length));
                    return negative ? "-" + body : body;
                }
                else
                {
                    string body = digits.Substring(0, (int)il) + "." +
                                  digits.Substring((int)il);
                    return negative ? "-" + body : body;
                }
            }

            return ToScientific(-1);
        }

        /// <summary>Compact scientific form for magnitudes too large to expand:
        /// "1.234e308" where the exponent is an exact BigInteger. decimals &lt; 0
        /// means "keep every significant digit" (lossless); otherwise it caps to
        /// the given number of fraction digits.</summary>
        public string ToScientific(int decimals = 0)
        {
            if (IsZero)
                return "0";

            BigInteger exp = intLen - 1; // one leading digit stays with the mantissa
            string frac = "";
            if (digits.Length > 1)
            {
                int take = decimals < 0 ? digits.Length - 1
                                        : Math.Min(decimals, digits.Length - 1);
                frac = digits.Substring(1, take);
                while (frac.Length > 0 && frac[frac.Length - 1] == '0')
                    frac = frac.Substring(0, frac.Length - 1);
            }
            string head = digits.Substring(0, 1);
            string body = frac.Length > 0 ? head + "." + frac : head;
            return (negative ? "-" : "") + body + "e" +
                   exp.ToString(CultureInfo.InvariantCulture);
        }

        // ── Comparison ──────────────────────────────────────────────────

        public int CompareTo(BigDouble other)
        {
            if (IsZero && other.IsZero)
                return 0;
            if (negative != other.negative)
                return negative ? -1 : 1;

            int cmp = CompareMagnitude(other);
            return negative ? -cmp : cmp;
        }

        private int CompareMagnitude(BigDouble other)
        {
            if (IsZero) return other.IsZero ? 0 : -1;
            if (other.IsZero) return 1;

            // floor(log10) == intLen - 1 (digits carry no leading zeros)
            BigInteger magA = intLen - 1;
            BigInteger magB = other.intLen - 1;
            int magCmp = BigInteger.Compare(magA, magB);
            if (magCmp != 0)
                return magCmp > 0 ? 1 : -1;

            // Same order of magnitude: compare digit-by-digit, trailing-pad the
            // shorter operand with '0' (e.g. 1.2 vs 1.23).
            int lenA = digits.Length;
            int lenB = other.digits.Length;
            int maxLen = lenA > lenB ? lenA : lenB;
            for (int i = 0; i < maxLen; i++)
            {
                char ca = i < lenA ? digits[i] : '0';
                char cb = i < lenB ? other.digits[i] : '0';
                if (ca != cb)
                    return ca > cb ? 1 : -1;
            }
            return 0;
        }

        public bool Equals(BigDouble other)
        {
            if (IsZero && other.IsZero)
                return true;
            return negative == other.negative
                && intLen == other.intLen
                && digits == other.digits;
        }

        public override bool Equals(object? obj)
            => obj is BigDouble bd && Equals(bd);

        public override int GetHashCode()
        {
            int h = negative ? 1 : 0;
            h = (h * 397) ^ intLen.GetHashCode();
            h = (h * 397) ^ (digits != null ? digits.GetHashCode() : 0);
            return h;
        }

        public static bool operator ==(BigDouble a, BigDouble b) => a.Equals(b);
        public static bool operator !=(BigDouble a, BigDouble b) => !a.Equals(b);
        public static bool operator <(BigDouble a, BigDouble b) => a.CompareTo(b) < 0;
        public static bool operator >(BigDouble a, BigDouble b) => a.CompareTo(b) > 0;
        public static bool operator <=(BigDouble a, BigDouble b) => a.CompareTo(b) <= 0;
        public static bool operator >=(BigDouble a, BigDouble b) => a.CompareTo(b) >= 0;

        // ── String arithmetic (BigIntChunked, exact) ────────────────────

        /// <summary>
        /// Aligns both operands to a common decimal exponent so their digit
        /// strings can be added/subtracted directly. Shared exponent chosen as
        /// the minimum of the two (rightmost decimal point). Returns false when
        /// the exponent gap is physically unreasonable to materialize as digits.
        /// </summary>
        private static bool TryAlign(BigDouble a, BigDouble b,
                                     out string aDigits, out string bDigits,
                                     out BigInteger sharedExp)
        {
            aDigits = "";
            bDigits = "";
            sharedExp = BigInteger.Zero;

            BigInteger
                expA = a.intLen - a.digits.Length,
                expB = b.intLen - b.digits.Length;
            sharedExp = expA <= expB ? expA : expB;

            if (ToSmallDelta(expA - sharedExp) > MaxAlignPad ||
                ToSmallDelta(expB - sharedExp) > MaxAlignPad)
                return false;

            aDigits = a.digits + new string('0', (int)(expA - sharedExp));
            bDigits = b.digits + new string('0', (int)(expB - sharedExp));
            return true;
        }

        private static long ToSmallDelta(BigInteger d)
        {
            if (d < 0) d = -d;
            return d < long.MaxValue ? (long)d : long.MaxValue;
        }

        public static BigDouble Add(BigDouble a, BigDouble b)
        {
            if (a.IsZero) return b;
            if (b.IsZero) return a;

            if (!TryAlign(a, b, out string ad, out string bd, out BigInteger exp))
            {
                // Exponent gap beyond what memory can materialize: the smaller
                // operand is negligible against the larger.
                int cmp = a.CompareMagnitude(b);
                if (cmp == 0) return Zero;
                return cmp > 0 ? a : b;
            }

            string sumDigits;
            bool resultNeg;
            if (a.negative == b.negative)
            {
                sumDigits = BigIntChunked.Add(ad, bd);
                resultNeg = a.negative;
            }
            else
            {
                int cmpDigit = CompareDigitStrings(ad, bd);
                if (cmpDigit == 0) return Zero;
                if (cmpDigit > 0)
                {
                    sumDigits = BigIntChunked.Subtract(ad, bd);
                    resultNeg = a.negative;
                }
                else
                {
                    sumDigits = BigIntChunked.Subtract(bd, ad);
                    resultNeg = b.negative;
                }
            }
            return NormalizeResult(sumDigits, exp, resultNeg);
        }

        public static BigDouble Subtract(BigDouble a, BigDouble b)
        {
            if (b.IsZero) return a;
            if (a.IsZero) return new BigDouble(b.digits, b.intLen, !b.negative,
                b.mantissa, b.exponent10);
            BigDouble negB = Negate(b);
            return Add(a, negB);
        }

        private static BigDouble Negate(BigDouble v)
            => new BigDouble(v.digits, v.intLen, !v.negative, v.mantissa, v.exponent10);

        public static BigDouble Multiply(BigDouble a, BigDouble b)
        {
            if (a.IsZero || b.IsZero) return Zero;
            string product = BigIntChunked.Multiply(a.digits, b.digits);
            BigInteger exp = (a.intLen - a.digits.Length) + (b.intLen - b.digits.Length);
            return NormalizeResult(product, exp, a.negative != b.negative);
        }

        public static BigDouble DivideByInt(BigDouble a, int n)
        {
            if (n == 0) throw new DivideByZeroException();
            if (a.IsZero) return a;
            if (n == 1) return a;

            string quotient = BigIntChunked.DivideByInt(a.digits, n, out int _);
            BigInteger exp = a.intLen - a.digits.Length;
            return NormalizeResult(quotient, exp, a.negative);
        }

        private static BigDouble NormalizeResult(string digits, BigInteger exp, bool neg)
        {
            digits = TrimLeadingZeros(digits);
            if (digits == "0")
                return Zero;

            // Trim trailing zeros (alignment padding). Each removed digit must
            // bump the exponent so the value stays identical.
            int end = digits.Length - 1;
            while (end > 0 && digits[end] == '0')
                end--;
            if (end != digits.Length - 1)
            {
                int removed = digits.Length - (end + 1);
                digits = digits.Substring(0, end + 1);
                exp += removed;
            }

            BigInteger intLen = digits.Length + exp;
            double m;
            BigInteger e;
            ComputeApprox(digits, exp, out m, out e);
            return new BigDouble(digits, intLen, neg, m, e);
        }

        private static int CompareDigitStrings(string a, string b)
        {
            if (a.Length != b.Length) return a.Length > b.Length ? 1 : -1;
            int cmp = string.CompareOrdinal(a, b);
            return cmp > 0 ? 1 : (cmp < 0 ? -1 : 0);
        }

        // ── Operators ────────────────────────────────────────────────────

        public static BigDouble operator +(BigDouble a, BigDouble b) => Add(a, b);
        public static BigDouble operator -(BigDouble a, BigDouble b) => Subtract(a, b);
        public static BigDouble operator *(BigDouble a, BigDouble b) => Multiply(a, b);
        public static BigDouble operator /(BigDouble a, int n) => DivideByInt(a, n);
        public static BigDouble operator *(BigDouble a, int n)
        {
            if (n == 0) return Zero;
            if (a.IsZero) return Zero;
            string prod = BigIntChunked.MultiplyByInt(a.digits, Math.Abs(n));
            BigInteger exp = a.intLen - a.digits.Length;
            return NormalizeResult(prod, exp, a.negative != (n < 0));
        }
        public static BigDouble operator -(BigDouble a) => Negate(a);

        // ── Conversions ─────────────────────────────────────────────────

        /// <summary>True when the value is an integer in [int.MinValue, int.MaxValue].</summary>
        public bool FitsInInt32
        {
            get
            {
                if (IsZero) return true;
                BigInteger span = negative ? 2147483648 : 2147483647; // |MinValue|, MaxValue
                return FitsInMagnitude(span);
            }
        }

        /// <summary>True when the value is an integer in [long.MinValue (−2^63), long.MaxValue].</summary>
        public bool FitsInInt64
        {
            get
            {
                if (IsZero) return true;
                BigInteger span = negative
                    ? new BigInteger(1) << 63                        // |MinValue|
                    : (new BigInteger(1) << 63) - 1;                 // MaxValue
                return FitsInMagnitude(span);
            }
        }

        private bool FitsInMagnitude(BigInteger limit)
        {
            if (intLen <= 0) return false;      // fractional value is not an integer
            if (intLen > 25) return false;      // far beyond any 64-bit bound
            long il = (long)intLen;
            string intPart = il >= digits.Length
                ? digits + new string('0', (int)(il - digits.Length))
                : digits.Substring(0, (int)il);
            return StringInRange(intPart, limit.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Lexical range check on equal-padded digit strings (no leading zeros).</summary>
        private static bool StringInRange(string intPart, string limit)
        {
            if (intPart.Length != limit.Length)
                return intPart.Length < limit.Length;
            return string.CompareOrdinal(intPart, limit) <= 0;
        }

        /// <summary>Bounded integer view (AS3 int cast on damage/amount values).</summary>
        public int ToInt32()
        {
            if (IsZero) return 0;
            if (FitsInInt32)
            {
                string intPart = ToPartDigits(digits, intLen);
                return int.Parse(intPart, CultureInfo.InvariantCulture);
            }
            return negative ? int.MinValue : int.MaxValue;
        }

        public long ToInt64()
        {
            if (IsZero) return 0;
            if (!FitsInInt64)
                throw new OverflowException($"BigDouble value exceeds long range: {ToString()}");
            string intPart = ToPartDigits(digits, intLen);
            return long.Parse(intPart, NumberStyles.AllowLeadingSign,
                              CultureInfo.InvariantCulture);
        }

        public int ToInt() => ToInt32();

        /// <summary>Approx double for gauges/fill ratios (never used for exact math).
        /// Saturated to double.MaxValue instead of overflowing to Infinity.</summary>
        public double ToDouble()
        {
            if (IsZero) return 0;

            double sign = negative ? -1.0 : 1.0;
            if (exponent10 > 308)
                return sign * double.MaxValue;
            if (exponent10 < -324)
                return 0;

            double value = mantissa * Math.Pow(10, (int)exponent10);
            if (double.IsInfinity(value))
                return sign * double.MaxValue;
            return sign * value;
        }

        public decimal ToDecimal()
        {
            string s = ToString();
            if (s.Length > 40 || s.IndexOf('e') >= 0 || s.IndexOf('E') >= 0)
                throw new OverflowException($"value too large for decimal: {s}");
            return decimal.Parse(s, NumberStyles.Number, CultureInfo.InvariantCulture);
        }

        private string ToPartDigits(string digits_, BigInteger intLen_)
        {
            if (digits_ == "0")
                return "0";
            if (intLen_ <= 0 || intLen_ > int.MaxValue)
                return "0";
            long il = (long)intLen_;
            string body = il >= digits_.Length
                ? digits_ + new string('0', (int)(il - digits_.Length))
                : digits_.Substring(0, (int)il);
            return (negative && body != "0") ? "-" + body : body;
        }

        // ── Abbreviation / display ──────────────────────────────────────

        /// <summary>
        /// Renders the exact integer value via NumberDisplay's abbreviation
        /// pipeline ("1234567" → "1.23M"). Switches to compact scientific when
        /// the expansion would blow the RAM budget.
        /// </summary>
        public string ToAbbreviated(int decimals = 2)
        {
            if (intLen <= 0)
                return ToString();
            if (intLen > MaxExpansion)
                return ToScientific(decimals > 0 ? Math.Min(decimals, 10) : 0);
            long il = (long)intLen;
            string integer = il >= digits.Length
                ? digits + new string('0', (int)(il - digits.Length))
                : digits.Substring(0, (int)il);
            string sign = negative ? "-" : "";
            return sign + NumberDisplay.FormatBigInt(integer, decimals);
        }

        /// <summary>Same as ToString but with thousands separators.</summary>
        public string ToGroupedString()
        {
            if (IsZero)
                return "0";
            if (intLen <= 0)
                return ToString();
            if (intLen > MaxExpansion)
                return ToString();

            long il = (long)intLen;
            var builder = new System.Text.StringBuilder();

            if (il <= digits.Length)
            {
                for (int i = 0; i < il; i++)
                {
                    if (i > 0 && (il - i) % 3 == 0)
                        builder.Append(',');
                    builder.Append(digits[i]);
                }
                if (il < digits.Length)
                    builder.Append('.').Append(digits.Substring((int)il));
            }
            else
            {
                long extra = il - digits.Length;
                for (int i = 0; i < digits.Length; i++)
                {
                    if (i > 0 && (il - i) % 3 == 0)
                        builder.Append(',');
                    builder.Append(digits[i]);
                }
                for (long i = 0; i < extra; i++)
                {
                    if ((il - digits.Length - i) % 3 == 0 && (i > 0 || digits.Length > 0))
                        builder.Append(',');
                    builder.Append('0');
                }
            }

            return (negative ? "-" : "") + builder.ToString();
        }
    }
}
