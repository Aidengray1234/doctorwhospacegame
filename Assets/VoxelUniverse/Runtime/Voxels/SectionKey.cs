using System;
using DoctorWho.VoxelUniverse.Celestial;

namespace DoctorWho.VoxelUniverse.Voxels
{
    [Serializable]
    public struct SectionKey : IEquatable<SectionKey>, IComparable<SectionKey>
    {
        public CelestialBodyId bodyId;
        public CubeSphereFace face;
        public int sectionU;
        public int sectionV;
        public int sectionRadial;

        public SectionKey(CelestialBodyId body, CubeSphereFace cubeFace, int u, int v, int radial)
        {
            bodyId = body;
            face = cubeFace;
            sectionU = u;
            sectionV = v;
            sectionRadial = radial;
        }

        public int CompareTo(SectionKey other)
        {
            int bodyComparison = bodyId.CompareTo(other.bodyId);
            if (bodyComparison != 0) return bodyComparison;
            int faceComparison = ((byte)face).CompareTo((byte)other.face);
            if (faceComparison != 0) return faceComparison;
            int uComparison = sectionU.CompareTo(other.sectionU);
            if (uComparison != 0) return uComparison;
            int vComparison = sectionV.CompareTo(other.sectionV);
            if (vComparison != 0) return vComparison;
            return sectionRadial.CompareTo(other.sectionRadial);
        }

        public bool Equals(SectionKey other)
        {
            return bodyId == other.bodyId && face == other.face && sectionU == other.sectionU && sectionV == other.sectionV && sectionRadial == other.sectionRadial;
        }

        public override bool Equals(object obj)
        {
            return obj is SectionKey && Equals((SectionKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = bodyId.GetHashCode();
                hash = hash * 397 ^ (int)face;
                hash = hash * 397 ^ sectionU;
                hash = hash * 397 ^ sectionV;
                hash = hash * 397 ^ sectionRadial;
                return hash;
            }
        }

        public static bool operator ==(SectionKey left, SectionKey right) { return left.Equals(right); }
        public static bool operator !=(SectionKey left, SectionKey right) { return !left.Equals(right); }

        public override string ToString()
        {
            return string.Format("{0}/{1}/{2}/{3}/{4}", bodyId, face, sectionU, sectionV, sectionRadial);
        }
    }
}
