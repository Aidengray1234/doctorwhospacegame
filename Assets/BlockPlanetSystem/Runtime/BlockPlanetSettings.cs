using UnityEngine;

namespace DoctorWho.BlockPlanets
{
    [CreateAssetMenu(menuName = "Doctor Who/Block Planet/Settings", fileName = "BlockPlanetSettings")]
    public sealed class BlockPlanetSettings : ScriptableObject
    {
        [Header("Planet scale - one Unity unit is one metre")]
        [Min(48f)] public float radius = 128f;
        [Min(64)] public int faceResolution = 256;
        [Range(8, 32)] public int chunkSize = 16;
        [Range(-160, -8)] public int minimumRadialBlock = -48;
        [Range(16, 160)] public int maximumRadialBlock = 64;
        [Range(-16, 32)] public int seaLevel = 2;

        [Header("Fast streaming")]
        [Range(1, 5)] public int horizontalChunkRadius = 2;
        [Range(0, 3)] public int verticalChunkRadius = 1;
        [Range(1, 24)] public int chunkBuildsPerFrame = 6;
        [Range(1, 48)] public int initialChunkBuildsPerFrame = 18;
        [Min(0.03f)] public float streamingRefreshSeconds = 0.10f;
        [Range(0, 3)] public int unloadPadding = 1;

        [Header("Terrain")]
        public int seed = 84271;
        [Range(-12f, 20f)] public float baseHeight = 3f;
        [Range(1f, 48f)] public float continentHeight = 17f;
        [Range(0f, 28f)] public float mountainHeight = 13f;
        [Range(0f, 8f)] public float detailHeight = 2.5f;
        [Range(8, 80)] public int crustDepth = 34;
        [Range(0f, 1f)] public float caveAmount = 0.18f;

        [Header("Player - ported from the supplied Standard Assets controller")]
        [Min(1f)] public float gravity = 22f;
        [Min(0.1f)] public float walkSpeed = 6f;
        [Min(0.1f)] public float sprintSpeed = 10f;
        [Min(0.1f)] public float jumpSpeed = 6.8f;
        [Min(0.1f)] public float groundAcceleration = 22f;
        [Min(0.1f)] public float airAcceleration = 15f;
        [Range(30f, 75f)] public float maxSlopeAngle = 58f;
        [Range(40f, 100f)] public float cameraFieldOfView = 75f;
        [Range(0.01f, 0.15f)] public float cameraNearClip = 0.03f;

        [Header("Inventory")]
        public bool creativeInventory = true;
        [Range(1, 999)] public int startingStackSize = 64;

        public int MinimumChunkY => BlockPlanetMath.FloorDiv(minimumRadialBlock, chunkSize);
        public int MaximumChunkY => BlockPlanetMath.FloorDiv(maximumRadialBlock - 1, chunkSize);
        public int ChunksAcrossFace => Mathf.CeilToInt(faceResolution / (float)chunkSize);
        public float SafetyRadius => radius + minimumRadialBlock + 3f;

        private void OnValidate()
        {
            chunkSize = Mathf.Max(8, chunkSize);
            faceResolution = Mathf.Max(chunkSize * 4, faceResolution);
            faceResolution = Mathf.CeilToInt(faceResolution / (float)chunkSize) * chunkSize;
            maximumRadialBlock = Mathf.Max(maximumRadialBlock, minimumRadialBlock + chunkSize * 3);
            horizontalChunkRadius = Mathf.Max(1, horizontalChunkRadius);
        }
    }
}
