using DoctorWho.VoxelUniverse.Voxels;
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

        [Header("Legacy Section Streaming")]
        [Range(1, 5)] public int nearSectionRadius = 2;
        [Range(1, 3)] public int verticalSectionRadius = 1;
        [Range(0, 4)] public int predictiveSectionLead = 2;
        [Min(0.5f)] public float unloadDelaySeconds = 12f;
        [Range(1, 8)] public int workerCount = 3;
        [Range(1, 8)] public int meshUploadsPerFrame = 2;
        [Range(1, 24)] public int mainThreadCallbacksPerFrame = 6;
        [Min(24f)] public float nearTerrainMaxAltitude = 112f;
        [Range(64, 512)] public int maximumLoadedSections = 192;

        [Header("No-Warp Tangent Cubes")]
        [Range(12, 48)] public int tangentPatchRadius = 24;
        [Range(8, 40)] public int tangentPatchBlocksBelow = 24;
        [Range(8, 48)] public int tangentPatchBlocksAbove = 28;
        [Range(4, 16)] public int tangentPatchTileSize = 8;
        [Range(3f, 20f)] public float tangentPatchRecenterDistance = 8f;
        [Range(1, 8)] public int tangentPatchTilesPerFrame = 1;
        [Range(24f, 160f)] public float tangentPatchMaxAltitude = 92f;

        [Header("Block Geometry Compatibility")]
        [Range(0.90f, 1.04f)] public float tangentialBlockFill = 0.995f;
        [Range(0.48f, 0.54f)] public float radialBlockHalfSize = 0.5f;

        [Header("Infinite Planet LOD")]
        [Range(16, 64)] public int middleFaceResolution = 44;
        [Range(10, 40)] public int farFaceResolution = 24;
        [Range(10f, 60f)] public float middleInnerRadiusBlocks = 18f;
        [Range(64f, 260f)] public float middleOuterRadiusBlocks = 150f;
        [Range(48f, 220f)] public float farHoleRadiusBlocks = 124f;
        [Range(0.5f, 6f)] public float middleSurfaceInset = 1.25f;
        [Range(1f, 10f)] public float farSurfaceInset = 3.25f;

        [Header("Previous Far-LOD Compatibility")]
        [Min(16f)] public float farLocalHideRadius = 88f;
        [Min(1f)] public float farLocalFadeWidth = 20f;
        [Min(24f)] public float farFullVisibilityAltitude = 176f;
        [Min(1f)] public float farFallbackInset = 5f;

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

        public void ApplyNoWarpInfiniteRenderingDefaults()
        {
            groundRadius = Mathf.Max(groundRadius, 256f);
            int recommendedResolution = Mathf.CeilToInt((groundRadius * 2f) / 16f) * 16;
            faceCellResolution = Mathf.Max(faceCellResolution, recommendedResolution);
            tangentPatchRadius = 24;
            tangentPatchBlocksBelow = Mathf.Max(tangentPatchBlocksBelow, 24);
            tangentPatchBlocksAbove = Mathf.Max(tangentPatchBlocksAbove, 28);
            tangentPatchTileSize = 8;
            tangentPatchRecenterDistance = Mathf.Clamp(tangentPatchRecenterDistance, 6f, 10f);
            tangentPatchTilesPerFrame = 1;
            tangentPatchMaxAltitude = Mathf.Max(tangentPatchMaxAltitude, 92f);
            middleFaceResolution = Mathf.Clamp(middleFaceResolution, 40, 48);
            farFaceResolution = Mathf.Clamp(farFaceResolution, 20, 28);
            middleInnerRadiusBlocks = Mathf.Min(middleInnerRadiusBlocks, tangentPatchRadius - 6f);
            middleOuterRadiusBlocks = Mathf.Max(middleOuterRadiusBlocks, 140f);
            farHoleRadiusBlocks = Mathf.Clamp(farHoleRadiusBlocks, 100f, middleOuterRadiusBlocks - 12f);
            middleSurfaceInset = Mathf.Max(middleSurfaceInset, 1.25f);
            farSurfaceInset = Mathf.Max(farSurfaceInset, 3.25f);
            farLocalHideRadius = Mathf.Max(farLocalHideRadius,
                (tangentPatchRadius + 12f));
            farLocalFadeWidth = Mathf.Max(farLocalFadeWidth, 18f);
            farFullVisibilityAltitude = Mathf.Max(farFullVisibilityAltitude,
                tangentPatchMaxAltitude + 56f);
            farFallbackInset = Mathf.Max(farFallbackInset, farSurfaceInset + 2f);
            maximumLoadedSections = 64;
            ClampValues();
        }

        public void ApplyRecommendedVisualRepairDefaults()
        {
            ApplyNoWarpInfiniteRenderingDefaults();
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
            maximumLoadedSections = Mathf.Max(32, maximumLoadedSections);
            nearTerrainMaxAltitude = Mathf.Max(24f, nearTerrainMaxAltitude);
            tangentPatchRadius = Mathf.Clamp(tangentPatchRadius, 12, 48);
            tangentPatchBlocksBelow = Mathf.Clamp(tangentPatchBlocksBelow, 8, 40);
            tangentPatchBlocksAbove = Mathf.Clamp(tangentPatchBlocksAbove, 8, 48);
            tangentPatchTileSize = Mathf.Clamp(tangentPatchTileSize, 4, 16);
            tangentPatchRecenterDistance = Mathf.Clamp(tangentPatchRecenterDistance, 3f, 20f);
            tangentPatchTilesPerFrame = Mathf.Clamp(tangentPatchTilesPerFrame, 1, 8);
            tangentPatchMaxAltitude = Mathf.Max(24f, tangentPatchMaxAltitude);
            middleFaceResolution = Mathf.Clamp(middleFaceResolution, 16, 64);
            farFaceResolution = Mathf.Clamp(farFaceResolution, 10, 40);
            middleInnerRadiusBlocks = Mathf.Clamp(middleInnerRadiusBlocks, 10f, 60f);
            middleOuterRadiusBlocks = Mathf.Max(middleInnerRadiusBlocks + 24f, middleOuterRadiusBlocks);
            farHoleRadiusBlocks = Mathf.Clamp(farHoleRadiusBlocks,
                middleInnerRadiusBlocks + 12f, middleOuterRadiusBlocks - 4f);
            middleSurfaceInset = Mathf.Clamp(middleSurfaceInset, 0.5f, 6f);
            farSurfaceInset = Mathf.Clamp(farSurfaceInset, 1f, 10f);
            farLocalHideRadius = Mathf.Max(16f, farLocalHideRadius);
            farLocalFadeWidth = Mathf.Max(1f, farLocalFadeWidth);
            farFullVisibilityAltitude = Mathf.Max(24f, farFullVisibilityAltitude);
            farFallbackInset = Mathf.Max(1f, farFallbackInset);
        }

        private void OnValidate()
        {
            ClampValues();
        }
    }
}
