// AS3: com.company.assembleegameclient.util.NumberDisplay
// Pure string-based BigInt arithmetic + abbreviation — no int/float overflow.
namespace VortexClient.Core
{
    public static class NumberDisplay
    {
        private static (string exp, string suffix)[] SCALES = NumberDisplayScales.GetStandardScales();
        private static readonly int[] SCALE_EXPS = BuildScaleExps();
        private static readonly int[] SCALE_LOOKUP = BuildScaleLookup();

        private static int[] BuildScaleExps()
        {
            var exps = new int[SCALES.Length];
            for (int i = 0; i < SCALES.Length; i++)
                exps[i] = int.Parse(SCALES[i].exp, System.Globalization.CultureInfo.InvariantCulture);
            return exps;
        }

        /// <summary>Precomputed exp → scale index map (old SuffixForExponent semantics, O(1) per call).</summary>
        private static int[] BuildScaleLookup()
        {
            int maxExp = SCALE_EXPS[0]; // largest exponent in the table
            var map = new int[maxExp + 1];
            for (int exp = 0; exp <= maxExp; exp++)
            {
                int idx = -1;
                for (int i = SCALE_EXPS.Length - 1; i >= 0; i--) // ascending exponents
                    if (SCALE_EXPS[i] <= exp) idx = i;           // last ≤ exp is the largest
                map[exp] = idx;
            }
            return map;
        }

        // ─── Formatting ─────────────────────────────────────────────────

        /// <summary>AS3: formatBigInt(val, decimals) — "1234567" → "1.23M".</summary>
        public static string FormatBigInt(string val, int decimals = 2)
        {
            if (string.IsNullOrEmpty(val))
                return "0";
            return FormatBigIntCore(val.AsSpan(), decimals);
        }

        /// <summary>Allocation-free input variant — formats directly from a span (e.g. BigIntChunked results).</summary>
        public static string FormatBigInt(ReadOnlySpan<char> val, int decimals = 2)
        {
            if (val.IsEmpty)
                return "0";
            return FormatBigIntCore(val, decimals);
        }

        private static string FormatBigIntCore(ReadOnlySpan<char> raw, int decimals)
        {
            var normalized = NormalizeSignedIntegerString(raw);
            if (!normalized.Ok)
                return FormatApproxNumber(raw, decimals);

            return AbbrevFromDecimalString(normalized.Digits, decimals, normalized.Neg);
        }

        private static string FormatApproxNumber(ReadOnlySpan<char> raw, int decimals)
        {
            var s = StripSeparators(raw.Trim());
            if (!double.TryParse(s, out double n) || double.IsInfinity(n))
                return new string(raw);

            bool neg = n < 0;
            double abs = System.Math.Abs(n);
            if (abs < 1000)
                return (neg ? "-" : "") + ((int)abs).ToString();

            int rawExp = (int)System.Math.Floor(System.Math.Log10(abs));
            int chosenExp = (rawExp / 3) * 3;
            string suffix = SuffixForExponent(chosenExp);
            double scaled = abs / System.Math.Pow(10, chosenExp);
            string text = scaled.ToString("F" + System.Math.Max(0, decimals));
            if (text.Contains('.'))
            {
                text = text.TrimEnd('0');
                if (text.EndsWith(".")) text = text[..^1];
            }
            return (neg ? "-" : "") + text + suffix;
        }

        // ─── String Normalization ───────────────────────────────────────

        private ref struct NormResult
        {
            public bool Ok;
            public bool Neg;
            public ReadOnlySpan<char> Digits;
        }

        private static NormResult NormalizeSignedIntegerString(ReadOnlySpan<char> raw)
        {
            var trimmed = raw.Trim();
            if (trimmed.Length == 0) return default;

            bool neg = false;
            int start = 0;
            if (trimmed[0] == '-') { neg = true; start = 1; }
            else if (trimmed[0] == '+') start = 1;
            if (start >= trimmed.Length) return default;

            var s = StripSeparators(trimmed[start..]);
            if (IsDigits(s))
                return new NormResult { Ok = true, Neg = neg, Digits = TrimLeadingZeros(s) };

            var sci = ParseScientificDigits(s);
            if (!sci.ok) return default;
            return new NormResult { Ok = true, Neg = neg, Digits = TrimLeadingZeros(sci.digits) };
        }

        private static ReadOnlySpan<char> TrimLeadingZeros(ReadOnlySpan<char> d)
        {
            int i = 0;
            while (i < d.Length && d[i] == '0') i++;
            if (i >= d.Length) return "0".AsSpan();
            return d[i..];
        }

        private static bool IsDigits(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
                if ((uint)(s[i] - '0') > 9) return false;
            return true;
        }

        private static (bool ok, string digits) ParseScientificDigits(ReadOnlySpan<char> s)
        {
            int ePos = -1;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == 'e' || s[i] == 'E') { ePos = i; break; }
            }
            if (ePos <= 0 || ePos >= s.Length - 1) return (false, "");

            var mantissa = s[..ePos];
            var expPart = s[(ePos + 1)..];
            if (expPart.Length > 0 && expPart[0] == '+') expPart = expPart[1..];
            if (!IsDigits(expPart)) return (false, "");

            if (!int.TryParse(expPart, out int exp)) return (false, "");

            int dot = mantissa.IndexOf('.');
            int fracDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
            int mantLen = dot < 0 ? mantissa.Length : mantissa.Length - 1;
            if (mantLen == 0) return (false, "");

            if (exp < fracDigits) return (false, "");
            int appendZeros = exp - fracDigits;

            // Single allocation: merged digits + appended zeros written straight into the result string.
            Span<char> merged = mantLen <= 256 ? stackalloc char[mantLen] : new char[mantLen];
            if (dot < 0)
            {
                mantissa.CopyTo(merged);
            }
            else
            {
                mantissa[..dot].CopyTo(merged);
                mantissa[(dot + 1)..].CopyTo(merged[dot..]);
            }
            if (!IsDigits(merged)) return (false, "");

            var digitsOnly = new string('\0', mantLen + appendZeros);
            unsafe
            {
                fixed (char* p = digitsOnly)
                {
                    var rs = new Span<char>(p, mantLen + appendZeros);
                    merged.CopyTo(rs);
                    if (appendZeros > 0)
                        rs[mantLen..].Fill('0');
                }
            }
            return (true, digitsOnly);
        }

        // ─── String Comparison ──────────────────────────────────────────

        /// <summary>AS3: compareBigIntStrings — returns >0 if a>b, 0 if equal, <0 if a<b.</summary>
        public static int CompareBigIntStrings(string a, string b)
        {
            a = TrimUnsignedDecimal(a);
            b = TrimUnsignedDecimal(b);
            if (a.Length != b.Length) return a.Length > b.Length ? 1 : -1;
            if (a == b) return 0;
            return string.CompareOrdinal(a, b) > 0 ? 1 : -1;
        }

        // ─── Abbreviation ───────────────────────────────────────────────

        private static string AbbrevFromDecimalString(ReadOnlySpan<char> d, int decimals, bool neg)
        {
            int len = d.Length;

            if (len <= 3)
                return BuildResult(d, ReadOnlySpan<char>.Empty, "", neg);

            int chosenExp = ((len - 1) / 3) * 3; // was CalculateChosenExponentString(lenStr)
            string suffix = SuffixForExponent(chosenExp);

            int intDigits = len - chosenExp;
            if (intDigits < 1) intDigits = 1;
            else if (intDigits > 3) intDigits = 3;

            var head = d[..intDigits];
            var frac = ReadOnlySpan<char>.Empty;
            if (decimals > 0 && intDigits < len)
            {
                int take = System.Math.Min(decimals, len - intDigits);
                var f = d.Slice(intDigits, take);
                while (f.Length > 0 && f[^1] == '0') f = f[..^1];
                frac = f;
            }

            return BuildResult(head, frac, suffix, neg);
        }

        /// <summary>Single-allocation result builder: sign + head + "." + frac + suffix.</summary>
        private static string BuildResult(ReadOnlySpan<char> head, ReadOnlySpan<char> frac, string suffix, bool neg)
        {
            int total = (neg ? 1 : 0) + head.Length + (frac.Length > 0 ? 1 + frac.Length : 0) + suffix.Length;
            if (total <= 64)
            {
                Span<char> buf = stackalloc char[64];
                int w = 0;
                if (neg) buf[w++] = '-';
                head.CopyTo(buf[w..]); w += head.Length;
                if (frac.Length > 0) { buf[w++] = '.'; frac.CopyTo(buf[w..]); w += frac.Length; }
                suffix.AsSpan().CopyTo(buf[w..]); w += suffix.Length;
                return new string(buf[..w]);
            }
            // Rare fallback for oversized inputs (e.g. huge decimals count)
            return (neg ? "-" : "") + new string(head) + (frac.Length > 0 ? "." + new string(frac) : "") + suffix;
        }

        private static string SuffixForExponent(int exp)
        {
            if (exp >= SCALE_LOOKUP.Length)
                return "e" + exp.ToString(System.Globalization.CultureInfo.InvariantCulture); // beyond the table: simple scientific
            int idx = SCALE_LOOKUP[exp];
            if (idx >= 0)
                return SCALES[idx].suffix;
            if (exp == 0)
                return "";
            return "e" + exp.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        // ─── Status Bar Ratio ───────────────────────────────────────────

        /// <summary>AS3: barFillRatio — fill ratio in [0,1] for HP/MP/EXP bars.</summary>
        public static double BarFillRatio(string valStr, string maxStr)
        {
            if (valStr == null || maxStr == null) return -1;
            string v = TrimUnsignedDecimal(valStr);
            string m = TrimUnsignedDecimal(maxStr);
            if (m == "0" || v == "0") return 0;
            if (CompareBigIntStrings(v, m) >= 0) return 1;

            var ov = MantissaExponent(v);
            var om = MantissaExponent(m);
            double mNum = om.n;
            if (mNum <= 0) return 0;

            double ratio = ov.n / mNum;
            int ed = ov.e - om.e;
            if (ed > 0)
                for (int i = 0; i < ed && ratio < 1e200; i++) ratio *= 10;
            else if (ed < 0)
                for (int i = 0; i < -ed && ratio > 1e-200; i++) ratio /= 10;

            if (double.IsInfinity(ratio) || ratio < 0) return 0;
            return System.Math.Min(1, System.Math.Max(0, ratio));
        }

        private static (double n, int e) MantissaExponent(string d)
        {
            int L = d.Length;
            if (L == 0 || d == "0") return (0, 0);
            if (L <= 15) return (double.Parse(d), 0);
            return (double.Parse(d[..15]), L - 15);
        }

        // ─── String Math Operations ─────────────────────────────────────

        private static string TrimUnsignedDecimal(string s)
        {
            if (string.IsNullOrEmpty(s)) return "0";
            int start = 0;
            if (s[0] == '-') start = 1;
            int i = start;
            while (i < s.Length && s[i] == '0') i++;
            if (i >= s.Length) return "0";
            string d = s[i..];
            return IsDigits(d) ? d : "0";
        }

        /// <summary>AS3: addBigIntStrings — string-based addition (vectorized chunked).</summary>
        public static string AddBigIntStrings(string a, string b)
        {
            a = TrimUnsignedDecimal(a);
            b = TrimUnsignedDecimal(b);
            return BigIntChunked.Add(a, b);
        }

        /// <summary>AS3: subtractBigIntStrings — string-based subtraction (vectorized chunked).</summary>
        public static string SubtractBigIntStrings(string a, string b)
        {
            a = TrimUnsignedDecimal(a);
            b = TrimUnsignedDecimal(b);
            if (CompareBigIntStrings(a, b) <= 0) return "0";
            return BigIntChunked.Subtract(a, b);
        }

        /// <summary>AS3: mulBigStrByInt — multiply decimal string by small int (vectorized chunked).</summary>
        public static string MulBigStrByInt(string a, int n)
        {
            a = TrimUnsignedDecimal(a);
            if (a == "0" || n == 0) return "0";
            if (n == 1) return a;
            return BigIntChunked.MultiplyByInt(a, n);
        }

        /// <summary>AS3: divBigStrByInt — floor division of decimal string by small int (vectorized chunked).</summary>
        public static string DivBigStrByInt(string a, int n)
        {
            a = TrimUnsignedDecimal(a);
            if (a == "0" || n == 0) return "0";
            if (n == 1) return a;
            return BigIntChunked.DivideByInt(a, n, out _);
        }

        /// <summary>AS3: mulBigIntStrByFrac — multiply by fraction num/den (floor).</summary>
        public static string MulBigIntStrByFrac(string val, int num, int den)
        {
            return DivBigStrByInt(MulBigStrByInt(val, num), den);
        }

        /// <summary>AS3: divBigIntStrByInt — alias matching DamageManager convention.</summary>
        public static string DivBigIntStrByInt(string a, int n) => DivBigStrByInt(a, n);

        /// <summary>AS3: modBigIntStrByInt — remainder of decimal string / small int (vectorized chunked).</summary>
        public static int ModBigIntStrByInt(string a, int n)
        {
            a = TrimUnsignedDecimal(a);
            if (a == "0" || n == 0) return 0;
            return BigIntChunked.ModByInt(a, n);
        }

        /// <summary>AS3: mulBigStrByBigStr — long multiplication of two decimal strings (vectorized chunked).</summary>
        public static string MulBigStrByBigStr(string a, string b)
        {
            a = TrimUnsignedDecimal(a);
            b = TrimUnsignedDecimal(b);
            if (a == "0" || b == "0") return "0";
            return BigIntChunked.Multiply(a, b);
        }

        // ─── Allocation-Safe Helpers ───────────────────────────────────

        /// <summary>Single-pass strip of ',' and '_' — no allocation when the input is clean.</summary>
        private static ReadOnlySpan<char> StripSeparators(ReadOnlySpan<char> s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ',' || c == '_')
                {
                    char[] result = new char[s.Length - 1];
                    int ri = 0;
                    for (int j = 0; j < s.Length; j++)
                    {
                        char d = s[j];
                        if (d != ',' && d != '_')
                            result[ri++] = d;
                    }
                    return new string(result).AsSpan();
                }
            }

            return s;
        }
    }

    /// <summary>
    /// Per-UI-component text cache: formats the value once and reuses the resulting string
    /// until the raw value or decimals actually change. Call Get() from Update() when the
    /// game value updates (or on a ~10-20Hz UI refresh timer) — never from Draw().
    /// </summary>
    public sealed class FormattedNumberCache
    {
        private string _lastRaw;
        private int _lastDecimals = int.MinValue;
        private string _cached;

        public string Get(string value, int decimals = 2)
        {
            if (_cached != null && _lastDecimals == decimals && string.Equals(_lastRaw, value, StringComparison.Ordinal))
                return _cached;
            _lastRaw = value;
            _lastDecimals = decimals;
            _cached = NumberDisplay.FormatBigInt(value, decimals);
            return _cached;
        }

        public string Get(ReadOnlySpan<char> value, int decimals = 2)
        {
            if (_cached != null && _lastDecimals == decimals && _lastRaw != null
                && value.Length == _lastRaw.Length && value.SequenceEqual(_lastRaw))
                return _cached;
            _lastRaw = new string(value);
            _lastDecimals = decimals;
            _cached = NumberDisplay.FormatBigInt(value, decimals);
            return _cached;
        }

        public void Invalidate()
        {
            _lastRaw = null;
            _lastDecimals = int.MinValue;
            _cached = null;
        }
    }
}
