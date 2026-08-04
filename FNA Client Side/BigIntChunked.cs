// Pure-string vectorized chunked bigint arithmetic — zero allocation, zero padding.
// No PadLeft. No offsets. No virtual padding.
// Right-to-left operations consume 4 digits per iteration from each input's right end.
// Uses stackalloc for small buffers, ArrayPool for large ones.

using System;
using System.Buffers;

namespace VortexClient.Core
{
    internal static class BigIntChunked
    {
        private const int CHUNK = 4;
        private const int CHUNK_BASE = 10000;
        private const int STACK_MAX = 256;

        // ─── Addition ───────────────────────────────────────────────────
        // Consume 4 digits from each input's right end per iteration.

        internal static string Add(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int maxLen = a.Length > b.Length ? a.Length : b.Length;
            int bufLen = ((maxLen + CHUNK - 1) / CHUNK) * CHUNK + CHUNK;

            char[]? poolBuf = null;
            Span<char> buf = bufLen <= STACK_MAX
                ? stackalloc char[bufLen]
                : (poolBuf = ArrayPool<char>.Shared.Rent(bufLen));
            buf = buf[..bufLen];

            int carry = 0;
            int aRead = 0;
            int bRead = 0;

            for (int wi = bufLen; wi > 0; wi -= CHUNK)
            {
                int sum = ReadChunkRtl(a, ref aRead) + ReadChunkRtl(b, ref bRead) + carry;
                carry = sum / CHUNK_BASE;
                Write4(buf, wi - CHUNK, sum % CHUNK_BASE);
            }

            string result = TrimLeading(buf);
            if (poolBuf != null) ArrayPool<char>.Shared.Return(poolBuf);
            return result;
        }

        // ─── Subtraction ────────────────────────────────────────────────

        internal static string Subtract(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int maxLen = a.Length > b.Length ? a.Length : b.Length;
            int bufLen = ((maxLen + CHUNK - 1) / CHUNK) * CHUNK;

            char[]? poolBuf = null;
            Span<char> buf = bufLen <= STACK_MAX
                ? stackalloc char[bufLen]
                : (poolBuf = ArrayPool<char>.Shared.Rent(bufLen));
            buf = buf[..bufLen];

            int borrow = 0;
            int aRead = 0;
            int bRead = 0;

            for (int wi = bufLen; wi > 0; wi -= CHUNK)
            {
                int diff = ReadChunkRtl(a, ref aRead) - ReadChunkRtl(b, ref bRead) - borrow;
                if (diff < 0) { diff += CHUNK_BASE; borrow = 1; }
                else borrow = 0;
                Write4(buf, wi - CHUNK, diff);
            }

            string result = TrimLeading(buf);
            if (poolBuf != null) ArrayPool<char>.Shared.Return(poolBuf);
            return result;
        }

        // ─── Multiply By Small Int ───────────────────────────────────────

        internal static string MultiplyByInt(ReadOnlySpan<char> a, int n)
        {
            int bufLen = ((a.Length + CHUNK - 1) / CHUNK) * CHUNK + CHUNK;

            char[]? poolBuf = null;
            Span<char> buf = bufLen <= STACK_MAX
                ? stackalloc char[bufLen]
                : (poolBuf = ArrayPool<char>.Shared.Rent(bufLen));
            buf = buf[..bufLen];

            int carry = 0;
            int aRead = 0;

            for (int wi = bufLen; wi > 0; wi -= CHUNK)
            {
                int prod = ReadChunkRtl(a, ref aRead) * n + carry;
                carry = prod / CHUNK_BASE;
                Write4(buf, wi - CHUNK, prod % CHUNK_BASE);
            }

            string result = TrimLeading(buf);
            if (poolBuf != null) ArrayPool<char>.Shared.Return(poolBuf);
            return result;
        }

        // ─── Divide By Small Int ─────────────────────────────────────────
        // Digit-by-digit left-to-right (no chunking needed for division).

        internal static string DivideByInt(ReadOnlySpan<char> a, int n, out int remainder)
        {
            int bufLen = a.Length;

            char[]? poolBuf = null;
            Span<char> buf = bufLen <= STACK_MAX
                ? stackalloc char[bufLen]
                : (poolBuf = ArrayPool<char>.Shared.Rent(bufLen));
            buf = buf[..bufLen];

            int rem = 0;
            for (int i = 0; i < a.Length; i++)
            {
                rem = rem * 10 + (a[i] - '0');
                buf[i] = (char)('0' + (rem / n));
                rem %= n;
            }

            remainder = rem;
            string result = TrimLeading(buf);
            if (poolBuf != null) ArrayPool<char>.Shared.Return(poolBuf);
            return result;
        }

        // ─── Mod By Small Int ────────────────────────────────────────────
        // Digit-by-digit left-to-right.

        internal static int ModByInt(ReadOnlySpan<char> a, int n)
        {
            int rem = 0;
            for (int i = 0; i < a.Length; i++)
                rem = (rem * 10 + (a[i] - '0')) % n;
            return rem;
        }

        // ─── Long Multiplication ─────────────────────────────────────────

        internal static string Multiply(ReadOnlySpan<char> a, ReadOnlySpan<char> b)
        {
            int la = a.Length, lb = b.Length;
            int size = la + lb;

            int[]? poolDigits = null;
            Span<int> digits = size <= 128
                ? stackalloc int[size]
                : (poolDigits = ArrayPool<int>.Shared.Rent(size));
            digits = digits[..size];
            digits.Clear();

            for (int ai = la - 1; ai >= 0; ai--)
            {
                int da = a[ai] - '0';
                for (int bi = lb - 1; bi >= 0; bi--)
                {
                    int mul = da * (b[bi] - '0');
                    int p = ai + bi + 1;
                    int sum = mul + digits[p];
                    digits[p] = sum % 10;
                    digits[p - 1] += sum / 10;
                }
            }

            // Trim leading zero digits
            int start = 0;
            while (start < size && digits[start] == 0) start++;

            if (start >= size)
            {
                if (poolDigits != null) ArrayPool<int>.Shared.Return(poolDigits);
                return "0";
            }

            int resultLen = size - start;
            string result = new string('\0', resultLen);
            unsafe
            {
                fixed (char* p = result)
                {
                    var span = new Span<char>(p, resultLen);
                    for (int i = 0; i < resultLen; i++)
                        span[i] = (char)('0' + digits[start + i]);
                }
            }

            if (poolDigits != null) ArrayPool<int>.Shared.Return(poolDigits);
            return result;
        }

        // ─── Chunk Primitives ────────────────────────────────────────────

        /// <summary>Read up to 4 digits from the right end of s, advancing readPos.</summary>
        private static int ReadChunkRtl(ReadOnlySpan<char> s, ref int readPos)
        {
            int val = 0;
            int mult = 1;
            int end = readPos + CHUNK;
            for (int j = readPos; j < end; j++)
            {
                int idx = s.Length - 1 - j;
                if ((uint)idx < (uint)s.Length)
                    val += (s[idx] - '0') * mult;
                mult *= 10;
            }
            readPos = end;
            return val;
        }

        /// <summary>Write int 0-9999 as 4 chars into buf at position pos.</summary>
        private static void Write4(Span<char> buf, int pos, int val)
        {
            buf[pos + 3] = (char)('0' + val % 10); val /= 10;
            buf[pos + 2] = (char)('0' + val % 10); val /= 10;
            buf[pos + 1] = (char)('0' + val % 10); val /= 10;
            buf[pos + 0] = (char)('0' + val);
        }

        // ─── Helpers ─────────────────────────────────────────────────────

        private static string TrimLeading(ReadOnlySpan<char> s)
        {
            int i = 0;
            while (i < s.Length && s[i] == '0') i++;
            if (i >= s.Length) return "0";
            return new string(s[i..]);
        }
    }
}
