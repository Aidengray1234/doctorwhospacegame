using System;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Core
{
    [Serializable]
    public struct Double3 : IEquatable<Double3>
    {
        public double x;
        public double y;
        public double z;

        public static readonly Double3 Zero = new Double3(0d, 0d, 0d);
        public static readonly Double3 One = new Double3(1d, 1d, 1d);
        public static readonly Double3 Right = new Double3(1d, 0d, 0d);
        public static readonly Double3 Up = new Double3(0d, 1d, 0d);
        public static readonly Double3 Forward = new Double3(0d, 0d, 1d);

        public Double3(double xValue, double yValue, double zValue)
        {
            x = xValue;
            y = yValue;
            z = zValue;
        }

        public double SqrMagnitude { get { return x * x + y * y + z * z; } }
        public double Magnitude { get { return Math.Sqrt(SqrMagnitude); } }

        public Double3 Normalized
        {
            get
            {
                double magnitude = Magnitude;
                return magnitude > 1e-15d ? this / magnitude : Zero;
            }
        }

        public static Double3 FromVector3(Vector3 value)
        {
            return new Double3(value.x, value.y, value.z);
        }

        public Vector3 ToVector3()
        {
            return new Vector3((float)x, (float)y, (float)z);
        }

        public Vector3 ToVector3Relative(Double3 origin)
        {
            return (this - origin).ToVector3();
        }

        public static double Dot(Double3 a, Double3 b)
        {
            return a.x * b.x + a.y * b.y + a.z * b.z;
        }

        public static Double3 Cross(Double3 a, Double3 b)
        {
            return new Double3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        }

        public static Double3 Lerp(Double3 a, Double3 b, double t)
        {
            return a + (b - a) * t;
        }

        public static Double3 operator +(Double3 a, Double3 b) { return new Double3(a.x + b.x, a.y + b.y, a.z + b.z); }
        public static Double3 operator -(Double3 a, Double3 b) { return new Double3(a.x - b.x, a.y - b.y, a.z - b.z); }
        public static Double3 operator -(Double3 value) { return new Double3(-value.x, -value.y, -value.z); }
        public static Double3 operator *(Double3 value, double scalar) { return new Double3(value.x * scalar, value.y * scalar, value.z * scalar); }
        public static Double3 operator *(double scalar, Double3 value) { return value * scalar; }

        public static Double3 operator /(Double3 value, double scalar)
        {
            if (Math.Abs(scalar) <= double.Epsilon) throw new DivideByZeroException();
            return new Double3(value.x / scalar, value.y / scalar, value.z / scalar);
        }

        public bool Equals(Double3 other) { return x.Equals(other.x) && y.Equals(other.y) && z.Equals(other.z); }
        public override bool Equals(object obj) { return obj is Double3 && Equals((Double3)obj); }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + x.GetHashCode();
                hash = hash * 31 + y.GetHashCode();
                hash = hash * 31 + z.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(Double3 left, Double3 right) { return left.Equals(right); }
        public static bool operator !=(Double3 left, Double3 right) { return !left.Equals(right); }
        public override string ToString() { return string.Format("({0:R}, {1:R}, {2:R})", x, y, z); }
    }
}
