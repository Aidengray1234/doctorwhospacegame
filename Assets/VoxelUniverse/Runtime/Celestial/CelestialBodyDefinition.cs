using DoctorWho.VoxelUniverse.Core;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Celestial
{
    public enum CelestialBodyType
    {
        Star,
        RockyPlanet,
        Moon,
        GasGiant,
        Asteroid
    }

    [CreateAssetMenu(menuName = "Doctor Who/Voxel Universe/Celestial Body", fileName = "CelestialBody")]
    public sealed class CelestialBodyDefinition : ScriptableObject
    {
        public string stableKey = "doctorwhospacegame.primary-world";
        public string displayName = "Primary World";
        public CelestialBodyType bodyType = CelestialBodyType.RockyPlanet;
        [Min(1f)] public double radius = 96d;
        [Min(0.0001f)] public double gravityParameter = 24d;
        [Min(1f)] public double rotationPeriodSeconds = 1200d;
        [Range(-90f, 90f)] public float axialTiltDegrees = 23.4f;
        public string parentStableKey = "";
        public OrbitalElements orbit;
        public int seed = 48271;
        [Min(1f)] public double sphereOfInfluence = 25000d;
        public bool voxelWorldEnabled = true;

        [Header("Atmosphere")]
        public bool hasAtmosphere = true;
        [Min(0f)] public float atmosphereHeight = 18f;
        [Min(0f)] public float densityFalloff = 0.18f;
        public Color atmosphereColor = new Color(0.32f, 0.58f, 0.92f, 1f);
        public Color sunsetColor = new Color(1f, 0.32f, 0.1f, 1f);

        [Header("Ocean")]
        public bool hasOcean = true;
        public int seaLevel = 1;

        public CelestialBodyId BodyId
        {
            get { return CelestialBodyId.FromStableString(stableKey); }
        }
    }
}
