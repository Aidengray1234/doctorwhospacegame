using UnityEngine;

namespace DoctorWho.VoxelUniverse.Core
{
    [CreateAssetMenu(menuName = "Doctor Who/Voxel Universe/Runtime Settings", fileName = "VoxelUniverseSettings")]
    public sealed class VoxelUniverseSettings : ScriptableObject
    {
        [Header("Body")]
        public string stableBodyKey = "doctorwhospacegame.primary-world";
        public int seed = 48271;
        [Min(64)] public int faceCellResolution = 512;
        [Min(32f)] public float groundRadius = 256f;
        public int minimumRadialBlock = -64;
        public int maximumRadialBlock = 64;
        public int seaLevel = 1;
        public int generatorVersion = 2;
        public int saveVersion = 1;

        [Header("Streaming")]
        [Range(1, 5)] public int nearSectionRadius = 2;
        [Range(1, 3)] public int verticalSectionRadius = 1;
        [Range(0, 4)] public int predictiveSectionLead = 2;
        [Min(0.5f)] public float unloadDelaySeconds = 12f;
        [Range(1, 8)] public int workerCount = 3;
        [Range(1, 8)] public int meshUploadsPerFrame = 2;
        [Range(1, 24)] public int mainThreadCallbacksPerFrame = 6;
        [Min(24f)] public float nearTerrainMaxAltitude = 112f;
        [Range(64, 512)] public int maximumLoadedSections = 192;

        [Header("Block Geometry")]
        [Range(0.90f, 1.04f)] public float tangentialBlockFill = 0.995f;
        [Range(0.48f, 0.54f)] public float radialBlockHalfSize = 0.5f;

        [Header("Far Planet")]
        [Range(12, 64)] public int farFaceResolution = 32;
        [Range(0.05f, 1.5f)] public float farSurfaceInset = 0.38f;

        [Header("Player")]
        [Min(0.1f)] public float walkSpeed = 5.5f;
        [Min(0.1f)] public float sprintSpeed = 9f;
        [Min(0.1f)] public float flightSpeed = 14f;
        [Min(0.1f)] public float flightSprintSpeed = 32f;
        [Min(0.1f)] public float jumpSpeed = 7.5f;
        [Min(0.1f)] public float gravity = 24f;
        [Range(0.25f, 0.6f)] public float capsuleRadius = 0.38f;
        [Range(1.2f, 2.4f)] public float capsuleHeight = 1.8f;
        [Range(0.1f, 1.1f)] public float stepHeight = 0.65f;
        [Range(0.01f, 0.3f)] public float mouseSensitivity = 0.12f;
        [Range(3f, 12f)] public float interactionReach = 7f;

        [Header("Terrain")]
        [Range(1f, 30f)] public float continentHeight = 13f;
        [Range(1f, 36f)] public float mountainHeight = 20f;
        [Range(0.1f, 8f)] public float detailHeight = 2.5f;
        [Range(0.1f, 8f)] public float caveThreshold = 1.7f;

        public void ApplyRecommendedVisualRepairDefaults()
        {
            groundRadius = Mathf.Max(groundRadius, 256f);
            int recommendedResolution = Mathf.CeilToInt((groundRadius * 2f) / 16f) * 16;
            faceCellResolution = Mathf.Max(faceCellResolution, recommendedResolution);
            nearSectionRadius = 2;
            verticalSectionRadius = 1;
            predictiveSectionLead = Mathf.Clamp(predictiveSectionLead, 1, 2);
            unloadDelaySeconds = Mathf.Max(unloadDelaySeconds, 12f);
            maximumLoadedSections = Mathf.Clamp(maximumLoadedSections, 128, 224);
            nearTerrainMaxAltitude = Mathf.Max(nearTerrainMaxAltitude, 112f);
            farFaceResolution = Mathf.Clamp(farFaceResolution, 24, 40);
            generatorVersion = Mathf.Max(generatorVersion, 2);
            minimumRadialBlock = Mathf.Min(minimumRadialBlock, -64);
            maximumRadialBlock = Mathf.Max(maximumRadialBlock, 64);
            continentHeight = Mathf.Max(continentHeight, 13f);
            mountainHeight = Mathf.Max(mountainHeight, 20f);
            ClampValues();
        }

        public void ClampValues()
        {
            groundRadius = Mathf.Max(32f, groundRadius);
            faceCellResolution = Mathf.Max(64, faceCellResolution);
            faceCellResolution = Mathf.CeilToInt(faceCellResolution / 16f) * 16;
            maximumRadialBlock = Mathf.Max(minimumRadialBlock + 16, maximumRadialBlock);
            workerCount = Mathf.Max(1, workerCount);
            meshUploadsPerFrame = Mathf.Max(1, meshUploadsPerFrame);
            mainThreadCallbacksPerFrame = Mathf.Max(1, mainThreadCallbacksPerFrame);
            maximumLoadedSections = Mathf.Max(64, maximumLoadedSections);
            nearTerrainMaxAltitude = Mathf.Max(24f, nearTerrainMaxAltitude);
            farFaceResolution = Mathf.Clamp(farFaceResolution, 12, 64);
        }

        private void OnValidate()
        {
            ClampValues();
        }
    }
}
