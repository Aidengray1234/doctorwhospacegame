using System;

namespace DoctorWho.VoxelUniverse.Core
{
    public static class IntegerMath
    {
        public static int FloorDiv(int value, int divisor)
        {
            if (divisor <= 0)
                throw new ArgumentOutOfRangeException("divisor");

            int quotient = value / divisor;
            int remainder = value % divisor;
            return remainder < 0 ? quotient - 1 : quotient;
        }

        public static int PositiveMod(int value, int divisor)
        {
            if (divisor <= 0)
                throw new ArgumentOutOfRangeException("divisor");

            int remainder = value % divisor;
            return remainder < 0 ? remainder + divisor : remainder;
        }

        public static int CeilLog2(int value)
        {
            if (value <= 1)
                return 0;

            int bits = 0;
            int remaining = value - 1;
            while (remaining > 0)
            {
                remaining >>= 1;
                bits++;
            }

            return bits;
        }
    }
}
