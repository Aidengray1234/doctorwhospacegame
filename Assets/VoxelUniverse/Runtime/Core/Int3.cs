using System;

namespace DoctorWho.VoxelUniverse.Core
{
    [Serializable]
    public struct Int3 : IEquatable<Int3>
    {
        public int x;
        public int y;
        public int z;

        public static readonly Int3 Zero = new Int3(0, 0, 0);

        public Int3(int xValue, int yValue, int zValue)
        {
            x = xValue;
            y = yValue;
            z = zValue;
        }

        public static Int3 operator +(Int3 a, Int3 b)
        {
            return new Int3(a.x + b.x, a.y + b.y, a.z + b.z);
        }

        public static Int3 operator -(Int3 a, Int3 b)
        {
            return new Int3(a.x - b.x, a.y - b.y, a.z - b.z);
        }

        public bool Equals(Int3 other)
        {
            return x == other.x && y == other.y && z == other.z;
        }

        public override bool Equals(object obj)
        {
            return obj is Int3 && Equals((Int3)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x;
                hash = hash * 31 + y;
                hash = hash * 31 + z;
                return hash;
            }
        }

        public static bool operator ==(Int3 left, Int3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Int3 left, Int3 right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return string.Format("({0}, {1}, {2})", x, y, z);
        }
    }
}
