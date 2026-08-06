using System;
using System.Globalization;
using System.Text;

namespace DoctorWho.VoxelUniverse.Celestial
{
    [Serializable]
    public struct CelestialBodyId : IEquatable<CelestialBodyId>, IComparable<CelestialBodyId>
    {
        public ulong high;
        public ulong low;

        public static readonly CelestialBodyId Empty = new CelestialBodyId(0UL, 0UL);

        public CelestialBodyId(ulong highValue, ulong lowValue)
        {
            high = highValue;
            low = lowValue;
        }

        public bool IsEmpty { get { return high == 0UL && low == 0UL; } }

        public static CelestialBodyId FromStableString(string value)
        {
            if (string.IsNullOrEmpty(value)) throw new ArgumentException("A stable body key is required.", "value");
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            ulong first = Fnv1A(bytes, 14695981039346656037UL);
            ulong second = Fnv1A(bytes, 1099511628211UL ^ 0x9E3779B97F4A7C15UL);
            if (first == 0UL && second == 0UL) second = 1UL;
            return new CelestialBodyId(first, second);
        }

        public static bool TryParse(string value, out CelestialBodyId id)
        {
            id = Empty;
            if (string.IsNullOrEmpty(value) || value.Length != 32) return false;
            ulong parsedHigh;
            ulong parsedLow;
            if (!ulong.TryParse(value.Substring(0, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedHigh)) return false;
            if (!ulong.TryParse(value.Substring(16, 16), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsedLow)) return false;
            id = new CelestialBodyId(parsedHigh, parsedLow);
            return true;
        }

        public int CompareTo(CelestialBodyId other)
        {
            int highComparison = high.CompareTo(other.high);
            return highComparison != 0 ? highComparison : low.CompareTo(other.low);
        }

        public bool Equals(CelestialBodyId other) { return high == other.high && low == other.low; }
        public override bool Equals(object obj) { return obj is CelestialBodyId && Equals((CelestialBodyId)obj); }

        public override int GetHashCode()
        {
            unchecked { return ((int)high * 397) ^ (int)(high >> 32) ^ (int)low ^ (int)(low >> 32); }
        }

        public static bool operator ==(CelestialBodyId left, CelestialBodyId right) { return left.Equals(right); }
        public static bool operator !=(CelestialBodyId left, CelestialBodyId right) { return !left.Equals(right); }

        public override string ToString()
        {
            return high.ToString("x16", CultureInfo.InvariantCulture) + low.ToString("x16", CultureInfo.InvariantCulture);
        }

        private static ulong Fnv1A(byte[] bytes, ulong seed)
        {
            const ulong prime = 1099511628211UL;
            ulong hash = seed;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= prime;
            }
            return hash;
        }
    }
}
