using UnityEngine;

namespace DoctorWho.Planets
{
    [CreateAssetMenu(menuName = "Doctor Who/Planets/Generation Settings", fileName = "PlanetGenerationSettings")]
    public sealed class PlanetGenerationSettings : ScriptableObject
    {
        [Header("Scale (1 unit = 1 metre)")]
        [Min(100f)] public float radius = 1200f;
        [Min(1f)] public float maxTerrainHeight = 260f;
        [Range(-1f, 1f)] public float seaLevel = -0.12f;
        public int seed = 48271;

        [Header("Quadtree LOD")]
        [Range(8, 64)] public int patchResolution = 24;
        [Range(1, 9)] public int maxLod = 7;
        [Range(0.5f, 12f)] public float lodScreenError = 3.2f;
        [Range(1, 8)] public int maxBuildsPerFrame = 2;
        [Min(10f)] public float colliderDistance = 180f;
        [Min(0.1f)] public float skirtDepth = 8f;

        [Header("Terrain")]
        [Min(0.00001f)] public float continentFrequency = 0.00072f;
        [Range(0f, 1f)] public float continentThreshold = 0.49f;
        [Range(0.1f, 4f)] public float continentPower = 1.65f;
        [Min(0.00001f)] public float warpFrequency = 0.0014f;
        [Range(0f, 500f)] public float warpStrength = 145f;
        [Min(0.00001f)] public float mountainFrequency = 0.0032f;
        [Range(0f, 2f)] public float mountainStrength = 0.82f;
        [Min(0.00001f)] public float erosionFrequency = 0.0075f;
        [Range(0f, 1f)] public float erosionStrength = 0.42f;
        [Min(0.00001f)] public float detailFrequency = 0.028f;
        [Range(0f, 0.4f)] public float detailStrength = 0.09f;

        [Header("Climate")]
        [Min(0.00001f)] public float climateFrequency = 0.0018f;
        [Range(0f, 1f)] public float polarStart = 0.72f;
        [Range(0f, 1f)] public float snowHeight = 0.56f;

        [Header("Player")]
        [Min(1f)] public float gravity = 28f;
        [Min(0.1f)] public float walkSpeed = 6.5f;
        [Min(0.1f)] public float sprintSpeed = 11f;
        [Min(0.1f)] public float jumpSpeed = 8f;
        [Min(0.1f)] public float groundAcceleration = 32f;
        [Min(0.1f)] public float airAcceleration = 7f;
        [Range(20f, 70f)] public float maxSlopeAngle = 52f;
        [Range(0.01f, 0.2f)] public float cameraNearClip = 0.03f;
        [Range(50f, 100f)] public float cameraFov = 75f;

        [Header("World Precision")]
        [Min(500f)] public float floatingOriginThreshold = 4000f;
    }
}
