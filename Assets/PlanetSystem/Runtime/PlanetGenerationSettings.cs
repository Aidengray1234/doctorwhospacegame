using UnityEngine;

namespace DoctorWho.Planets
{
    [CreateAssetMenu(menuName = "Doctor Who/Planets/Generation Settings", fileName = "PlanetGenerationSettings")]
    public sealed class PlanetGenerationSettings : ScriptableObject
    {
        [Header("Planet")]
        [Min(10f)] public float radius = 500f;
        [Min(0f)] public float maxTerrainHeight = 120f;
        [Range(8, 128)] public int faceResolution = 48;
        public int seed = 12345;
        [Range(-1f, 1f)] public float seaLevel = -0.05f;

        [Header("Voxel Chunks")]
        [Range(8, 64)] public int chunkResolution = 24;
        [Min(1f)] public float voxelSize = 2f;
        [Range(1, 12)] public int activeChunkRadius = 4;
        [Range(1, 8)] public int colliderChunkRadius = 2;
        [Range(1, 8)] public int maxChunkBuildsPerFrame = 1;

        [Header("Noise")]
        [Min(0.0001f)] public float continentFrequency = 0.0035f;
        [Min(0.0001f)] public float mountainFrequency = 0.012f;
        [Range(1, 8)] public int octaves = 5;
        [Range(0f, 1f)] public float persistence = 0.5f;
        [Min(1f)] public float lacunarity = 2f;

        [Header("Player")]
        [Min(0.1f)] public float gravity = 30f;
        [Min(0.1f)] public float walkSpeed = 8f;
        [Min(0.1f)] public float sprintSpeed = 14f;
        [Min(0.1f)] public float jumpSpeed = 10f;
        [Min(0.1f)] public float mouseSensitivity = 0.12f;

        public float ChunkWorldSize => chunkResolution * voxelSize;

        private void OnValidate()
        {
            colliderChunkRadius = Mathf.Min(colliderChunkRadius, activeChunkRadius);
            maxTerrainHeight = Mathf.Max(0f, maxTerrainHeight);
            faceResolution = Mathf.Max(8, faceResolution);
        }
    }
}
