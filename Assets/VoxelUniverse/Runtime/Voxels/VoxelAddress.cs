using System;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Voxels
{
    [Serializable]
    public struct VoxelAddress : IEquatable<VoxelAddress>
    {
        public CelestialBodyId bodyId;
        public CubeSphereFace face;
        public int u;
        public int v;
        public int radial;

        public VoxelAddress(CelestialBodyId body, CubeSphereFace cubeFace, int uValue, int vValue, int radialValue)
        {
            bodyId = body;
            face = cubeFace;
            u = uValue;
            v = vValue;
            radial = radialValue;
        }

        public SectionKey SectionKey
        {
            get
            {
                return new SectionKey(bodyId, face, IntegerMath.FloorDiv(u, VoxelConstants.SectionSize), IntegerMath.FloorDiv(v, VoxelConstants.SectionSize), IntegerMath.FloorDiv(radial, VoxelConstants.SectionSize));
            }
        }

        public Int3 Local
        {
            get
            {
                return new Int3(IntegerMath.PositiveMod(u, VoxelConstants.SectionSize), IntegerMath.PositiveMod(radial, VoxelConstants.SectionSize), IntegerMath.PositiveMod(v, VoxelConstants.SectionSize));
            }
        }

        public bool Equals(VoxelAddress other)
        {
            return bodyId == other.bodyId && face == other.face && u == other.u && v == other.v && radial == other.radial;
        }

        public override bool Equals(object obj)
        {
            return obj is VoxelAddress && Equals((VoxelAddress)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = bodyId.GetHashCode();
                hash = hash * 397 ^ (int)face;
                hash = hash * 397 ^ u;
                hash = hash * 397 ^ v;
                hash = hash * 397 ^ radial;
                return hash;
            }
        }

        public static bool operator ==(VoxelAddress left, VoxelAddress right) { return left.Equals(right); }
        public static bool operator !=(VoxelAddress left, VoxelAddress right) { return !left.Equals(right); }
    }
}
