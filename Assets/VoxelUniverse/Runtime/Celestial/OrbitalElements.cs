using System;

namespace DoctorWho.VoxelUniverse.Celestial
{
    [Serializable]
    public struct OrbitalElements
    {
        public double semiMajorAxis;
        public double eccentricity;
        public double inclinationRadians;
        public double longitudeAscendingNodeRadians;
        public double argumentOfPeriapsisRadians;
        public double meanAnomalyAtEpochRadians;
        public double epochSeconds;
        public double periodSeconds;

        public void Validate()
        {
            if (semiMajorAxis <= 0d)
                throw new InvalidOperationException("Semi-major axis must be positive.");
            if (eccentricity < 0d || eccentricity >= 1d)
                throw new InvalidOperationException("Only stable elliptic orbits with 0 <= eccentricity < 1 are supported.");
            if (periodSeconds <= 0d)
                throw new InvalidOperationException("Orbital period must be positive.");
        }
    }
}
