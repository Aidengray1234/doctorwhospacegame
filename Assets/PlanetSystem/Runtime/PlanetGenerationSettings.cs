using UnityEngine;

namespace DoctorWho.Planets
{
    [CreateAssetMenu(menuName = "Doctor Who/Planets/Generation Settings", fileName = "PlanetGenerationSettings")]
    public sealed class PlanetGenerationSettings : ScriptableObject
    {
        [Header("Planet")]
        [Min(10f)] public float radius = 500f;
        [Min(0f)] public float maxTerrainHeight = 145f;
        [Range(16, 160)] public int faceResolution = 72;
        public int seed = 12345;
        [Range(-1f, 1f)] public float seaLevel = -0.08f;

        [Header("Terrain Shape")]
        [Min(0.0001f)] public float continentFrequency = 0.0024f;
        [Min(0.0001f)] public float mountainFrequency = 0.0105f;
        [Min(0.0001f)] public float detailFrequency = 0.038f;
        [Range(1, 8)] public int octaves = 6;
        [Range(0f, 1f)] public float persistence = 0.52f;
        [Min(1f)] public float lacunarity = 2.05f;
        [Range(0f, 2f)] public float continentStrength = 1.05f;
        [Range(0f, 2f)] public float mountainStrength = 0.78f;
        [Range(0f, 1f)] public float detailStrength = 0.16f;

        [Header("Voxel Chunks")]
        [Range(8, 64)] public int chunkResolution = 24;
        [Min(1f)] public float voxelSize = 2f;
        [Range(1, 12)] public int activeChunkRadius = 4;
        [Range(1, 8)] public int colliderChunkRadius = 2;
        [Range(1, 8)] public int maxChunkBuildsPerFrame = 1;

        [Header("Player")]
        [Min(0.1f)] public float gravity = 32f;
        [Min(0.1f)] public float walkSpeed = 9f;
        [Min(0.1f)] public float sprintSpeed = 16f;
        [Min(0.1f)] public float jumpSpeed = 11f;
        [Min(0.1f)] public float mouseSensitivity = 0.12f;
        [Range(0f, 30f)] public float groundAcceleration = 22f;
        [Range(0f, 15f)] public float airAcceleration = 5f;

        public float ChunkWorldSize => chunkResolution * voxelSize;

        private void OnValidate()
        {
            colliderChunkRadius = Mathf.Min(colliderChunkRadius, activeChunkRadius);
            maxTerrainHeight = Mathf.Max(0f, maxTerrainHeight);
            faceResolution = Mathf.Max(16, faceResolution);
        }
    }
}
