using System;
using System.Numerics;

namespace common
{
    /// <summary>
    /// A string-backed integer type that supports arbitrary-precision values (no Int32/Int64 cap).
    /// Internally uses BigInteger for arithmetic while exposing a string interface.
    /// Can be used interchangeably with int, BigInteger, and string via implicit conversions.
    /// </summary>
    public readonly struct StringInt : IComparable<StringInt>, IEquatable<StringInt>
    {
        private readonly BigInteger _value;
        private readonly string _cachedString;

        public static readonly StringInt Zero = new StringInt(BigInteger.Zero);
        public static readonly StringInt One = new StringInt(BigInteger.One);
        public static readonly StringInt MaxValue = new StringInt(BigInteger.Parse("99999999999999999999999999999999999999999"));
        public static readonly StringInt MinValue = new StringInt(BigInteger.Parse("-99999999999999999999999999999999999999999"));

        private StringInt(BigInteger value)
        {
            _value = value;
            _cachedString = null;
        }

        public StringInt(string value)
        {
            _value = string.IsNullOrEmpty(value) ? BigInteger.Zero : BigInteger.Parse(value);
            _cachedString = value;
        }

        // Implicit conversions
        public static implicit operator StringInt(int value) => new StringInt(new BigInteger(value));
        public static implicit operator StringInt(long value) => new StringInt(new BigInteger(value));
        public static implicit operator StringInt(BigInteger value) => new StringInt(value);
        public static implicit operator StringInt(string value) => new StringInt(value);

        public static implicit operator string(StringInt value) => value.ToString();
        public static implicit operator BigInteger(StringInt value) => value._value;
        public static explicit operator int(StringInt value) => (int)value._value;
        public static explicit operator long(StringInt value) => (long)value._value;

        // Arithmetic operators
        public static StringInt operator +(StringInt a, StringInt b) => new StringInt(a._value + b._value);
        public static StringInt operator -(StringInt a, StringInt b) => new StringInt(a._value - b._value);
        public static StringInt operator *(StringInt a, StringInt b) => new StringInt(a._value * b._value);
        public static StringInt operator /(StringInt a, StringInt b) => new StringInt(a._value / b._value);
        public static StringInt operator %(StringInt a, StringInt b) => new StringInt(a._value % b._value);
        public static StringInt operator -(StringInt a) => new StringInt(-a._value);
        public static StringInt operator ++(StringInt a) => new StringInt(a._value + 1);
        public static StringInt operator --(StringInt a) => new StringInt(a._value - 1);

        // Comparison operators
        public static bool operator ==(StringInt a, StringInt b) => a._value == b._value;
        public static bool operator !=(StringInt a, StringInt b) => a._value != b._value;
        public static bool operator >(StringInt a, StringInt b) => a._value > b._value;
        public static bool operator <(StringInt a, StringInt b) => a._value < b._value;
        public static bool operator >=(StringInt a, StringInt b) => a._value >= b._value;
        public static bool operator <=(StringInt a, StringInt b) => a._value <= b._value;

        public int CompareTo(StringInt other) => _value.CompareTo(other._value);

        public bool Equals(StringInt other) => _value.Equals(other._value);
        public override bool Equals(object obj) => obj is StringInt other && Equals(other);
        public override int GetHashCode() => _value.GetHashCode();

        public override string ToString()
        {
            return _cachedString ?? _value.ToString();
        }

        // Static helpers
        public static StringInt Min(StringInt a, StringInt b) => a._value < b._value ? a : b;
        public static StringInt Max(StringInt a, StringInt b) => a._value > b._value ? a : b;
        public static StringInt Abs(StringInt value) => new StringInt(BigInteger.Abs(value._value));
        public static StringInt Parse(string s) => new StringInt(s);
        public static bool TryParse(string s, out StringInt result)
        {
            if (BigInteger.TryParse(s, out var bi))
            {
                result = new StringInt(bi);
                return true;
            }
            result = Zero;
            return false;
        }

        public static StringInt FromDecimal(decimal d) => new StringInt(new BigInteger(decimal.Truncate(d)));
    }
}
