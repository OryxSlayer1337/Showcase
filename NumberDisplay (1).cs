// AS3: com.company.assembleegameclient.util.NumberDisplay
// Pure string-based BigInt arithmetic + abbreviation — no int/float overflow.
namespace VortexClient.Core
{
    public static class NumberDisplay
    {
        private static (string exp, string suffix)[] SCALES = NumberDisplayScales.GetStandardScales();

        // ─── Formatting ─────────────────────────────────────────────────

        /// <summary>AS3: formatBigInt(val, decimals) — "1234567" → "1.23M".</summary>
        public static string FormatBigInt(string val, int decimals = 2)
        {
            System.Console.WriteLine($"[NumberDisplay.FormatBigInt] Input: '{val}', decimals={decimals}");
            
            if (string.IsNullOrEmpty(val) || val == "0") 
            {
                System.Console.WriteLine("[NumberDisplay.FormatBigInt] Output: '0' (empty or zero)");
                return "0";
            }

            var normalized = NormalizeSignedIntegerString(val);
            if (!normalized.ok)
            {
                System.Console.WriteLine($"[NumberDisplay.FormatBigInt] Normalization failed, using approx: '{val}'");
                return FormatApproxNumber(val, decimals);
            }

            bool neg = normalized.neg;
            string d = normalized.digits;
            System.Console.WriteLine($"[NumberDisplay.FormatBigInt] Normalized digits: length={d.Length}, first20='{(d.Length > 20 ? d.Substring(0, 20) : d)}...'");
            
            var result = AbbrevFromDecimalString(d, decimals, neg);
            System.Console.WriteLine($"[NumberDisplay.FormatBigInt] Output: '{result}'");
            return result;
        }

        private static string FormatApproxNumber(string raw, int decimals)
        {
            string s = StripSeparators(raw.AsSpan().Trim());
            if (!double.TryParse(s, out double n) || double.IsInfinity(n))
                return raw;

            bool neg = n < 0;
            double abs = System.Math.Abs(n);
            if (abs < 1000)
                return (neg ? "-" : "") + ((int)abs).ToString();

            int rawExp = (int)System.Math.Floor(System.Math.Log10(abs));
            int chosenExp = (rawExp / 3) * 3;
            string suffix = SuffixForExponent(chosenExp.ToString());
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

        private static (bool ok, bool neg, string digits) NormalizeSignedIntegerString(string raw)
        {
            var trimmed = raw.AsSpan().Trim();
            if (trimmed.Length == 0) return (false, false, "");

            bool neg = false;
            int start = 0;
            if (trimmed[0] == '-') { neg = true; start = 1; }
            else if (trimmed[0] == '+') start = 1;
            if (start >= trimmed.Length) return (false, false, "");

            string s = StripSeparators(trimmed[start..]);
            if (IsDigits(s))
                return (true, neg, TrimLeadingZeros(s));

            var sci = ParseScientificDigits(s);
            if (!sci.ok) return (false, false, "");
            return (true, neg, TrimLeadingZeros(sci.digits));
        }

        private static string TrimLeadingZeros(ReadOnlySpan<char> d)
        {
            int i = 0;
            while (i < d.Length && d[i] == '0') i++;
            if (i >= d.Length) return "0";
            return new string(d[i..]);
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
            int fracDigits = 0;

            // Build digits string: mantissa without dot, interning the span
            string digitsOnly;
            if (dot < 0)
            {
                digitsOnly = new string(mantissa);
            }
            else
            {
                fracDigits = mantissa.Length - dot - 1;
                int mantLen = mantissa.Length - 1;
                char[] merged = new char[mantLen];
                mantissa[..dot].CopyTo(merged);
                mantissa[(dot + 1)..].CopyTo(merged.AsSpan(dot));
                digitsOnly = new string(merged);
            }

            if (!IsDigits(digitsOnly) || digitsOnly.Length == 0) return (false, "");

            if (exp < fracDigits) return (false, "");
            int appendZeros = exp - fracDigits;
            digitsOnly += new string('0', appendZeros);
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

        private static int CompareExpStrings(string a, string b)
        {
            if (a.Length != b.Length) return a.Length > b.Length ? 1 : -1;
            if (a == b) return 0;
            return string.CompareOrdinal(a, b) > 0 ? 1 : -1;
        }

        // ─── Abbreviation ───────────────────────────────────────────────

        private static string AbbrevFromDecimalString(string d, int decimals, bool neg)
        {
            string lenStr = d.Length.ToString();
            System.Console.WriteLine($"[NumberDisplay.Abbrev] d.length={d.Length}, lenStr='{lenStr}'");
            
            if (CompareBigIntStrings(lenStr, "3") <= 0)
            {
                var simple = (neg ? "-" : "") + d;
                System.Console.WriteLine($"[NumberDisplay.Abbrev] Returning simple (len<=3): '{simple}'");
                return simple;
            }

            string chosenExpString = CalculateChosenExponentString(lenStr);
            System.Console.WriteLine($"[NumberDisplay.Abbrev] chosenExpString='{chosenExpString}'");
            
            string suffix = SuffixForExponent(chosenExpString);
            System.Console.WriteLine($"[NumberDisplay.Abbrev] suffix='{suffix}'");

            int LInt = d.Length;
            int chosenExpIntVal = int.Parse(chosenExpString);
            System.Console.WriteLine($"[NumberDisplay.Abbrev] intDigits = LInt - chosenExpIntVal = {LInt} - {chosenExpIntVal}");
            
            int intDigits = LInt - chosenExpIntVal;
            if (intDigits < 1) intDigits = 1;
            else if (intDigits > 3) intDigits = 3;
            System.Console.WriteLine($"[NumberDisplay.Abbrev] intDigits (clamped)={intDigits}");

            string head = d[..intDigits];
            string frac = "";
            if (decimals > 0 && intDigits < d.Length)
            {
                int fracLen = System.Math.Min(decimals, d.Length - intDigits);
                frac = "." + d.Substring(intDigits, fracLen);
            }
            if (frac.Length > 0)
            {
                while (frac.Length > 1 && frac[^1] == '0')
                    frac = frac[..^1];
                if (frac == ".") frac = "";
            }
            
            var result = (neg ? "-" : "") + head + frac + suffix;
            System.Console.WriteLine($"[NumberDisplay.Abbrev] head='{head}', frac='{frac}', result='{result}'");
            return result;
        }

        private static string CalculateChosenExponentString(string lenStr)
        {
            string LMinus1 = SubtractBigIntStrings(lenStr, "1");
            string div3 = DivBigStrByInt(LMinus1, 3);
            return MulBigStrByInt(div3, 3);
        }

        private static string SuffixForExponent(string exp)
        {
            System.Console.WriteLine($"[NumberDisplay.SuffixForExponent] exp='{exp}'");
            
            string bestSuffix = null;
            string bestExp = null;

            for (int i = SCALES.Length - 1; i >= 0; i--)
            {
                string raw = SCALES[i].exp;
                // Scales now use exponent values directly, no sentinel needed

                // Get group exponent (now just returns the value as-is)
                string normalized = GetGroupExponentFromString(raw);
                int cmp = CompareExpStrings(normalized, exp);
                if (cmp == 0) 
                {
                    System.Console.WriteLine($"[NumberDisplay.SuffixForExponent] exact match: suffix='{SCALES[i].suffix}' normalized='{normalized}'");
                    return SCALES[i].suffix;
                }
                if (cmp < 0)
                {
                    if (bestExp == null || CompareExpStrings(normalized, bestExp) > 0)
                    {
                        bestExp = normalized;
                        bestSuffix = SCALES[i].suffix;
                    }
                }
            }

            if (bestSuffix != null) 
            {
                System.Console.WriteLine($"[NumberDisplay.SuffixForExponent] best match: suffix='{bestSuffix}' bestExp='{bestExp}'");
                return bestSuffix;
            }
            if (exp == "0" || exp == "") 
            {
                System.Console.WriteLine("[NumberDisplay.SuffixForExponent] no suffix needed");
                return "";
            }
            var sci = "e" + exp;
            System.Console.WriteLine($"[NumberDisplay.SuffixForExponent] no match, returning scientific: '{sci}'");
            return sci;
        }

        private static string GetGroupExponentFromString(string expStr)
        {
            // Scales now store exponent values directly (e.g., "3" for 10^3, "6" for 10^6)
            // No conversion needed - return as-is
            return expStr;
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

        /// <summary>Single-pass strip of ',' and '_' — avoids chained Replace allocations.</summary>
        private static string StripSeparators(ReadOnlySpan<char> s)
        {
            int sepCount = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == ',' || c == '_') sepCount++;
            }

            if (sepCount == 0)
                return s.Length == 0 ? "" : new string(s);

            int cleanLen = s.Length - sepCount;
            char[] result = new char[cleanLen];
            int ri = 0;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != ',' && c != '_')
                    result[ri++] = c;
            }

            return new string(result);
        }
    }
}
