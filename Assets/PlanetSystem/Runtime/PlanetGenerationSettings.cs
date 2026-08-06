using UnityEngine;

namespace DoctorWho.Planets
{
    [CreateAssetMenu(menuName = "Doctor Who/Planets/Generation Settings", fileName = "PlanetGenerationSettings")]
    public sealed class PlanetGenerationSettings : ScriptableObject
    {
        [Header("Planet")]
        [Min(10f)] public float radius = 500f;
        [Min(0f)] public float maxTerrainHeight = 120f;
        public int seed = 12345;

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

        public float ChunkWorldSize => chunkResolution * voxelSize;

        private void OnValidate()
        {
            colliderChunkRadius = Mathf.Min(colliderChunkRadius, activeChunkRadius);
            maxTerrainHeight = Mathf.Max(0f, maxTerrainHeight);
        }
    }
}
