using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public struct FaceBasis
    {
        public readonly Double3 normal;
        public readonly Double3 east;
        public readonly Double3 north;

        public FaceBasis(Double3 normalValue, Double3 eastValue, Double3 northValue)
        {
            normal = normalValue;
            east = eastValue;
            north = northValue;
        }
    }
}
