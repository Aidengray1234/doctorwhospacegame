using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public static class VoxelConstants
    {
        public const int SectionSize = 16;
        public const int SectionArea = SectionSize * SectionSize;
        public const int SectionVolume = SectionArea * SectionSize;

        public static int ToIndex(int localX, int localY, int localZ)
        {
            ValidateLocal(localX, "localX");
            ValidateLocal(localY, "localY");
            ValidateLocal(localZ, "localZ");
            return localX + localZ * SectionSize + localY * SectionArea;
        }

        public static Int3 FromIndex(int index)
        {
            if (index < 0 || index >= SectionVolume)
                throw new System.ArgumentOutOfRangeException("index");

            int localY = index / SectionArea;
            int remainder = index - localY * SectionArea;
            int localZ = remainder / SectionSize;
            int localX = remainder - localZ * SectionSize;
            return new Int3(localX, localY, localZ);
        }

        private static void ValidateLocal(int value, string parameterName)
        {
            if (value < 0 || value >= SectionSize)
                throw new System.ArgumentOutOfRangeException(parameterName);
        }
    }
}
