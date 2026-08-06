using UnityEngine;

namespace DoctorWho.VoxelUniverse.Core
{
    [CreateAssetMenu(menuName = "Doctor Who/Voxel Universe/Runtime Settings", fileName = "VoxelUniverseSettings")]
    public sealed class VoxelUniverseSettings : ScriptableObject
    {
        [Header("Body")]
        public string stableBodyKey = "doctorwhospacegame.primary-world";
        public int seed = 48271;
        [Min(32)] public int faceCellResolution = 256;
        [Min(16f)] public float groundRadius = 96f;
        public int minimumRadialBlock = -48;
        public int maximumRadialBlock = 48;
        public int seaLevel = 1;
        public int generatorVersion = 1;
        public int saveVersion = 1;

        [Header("Streaming")]
        [Range(1, 8)] public int nearSectionRadius = 3;
        [Range(1, 4)] public int verticalSectionRadius = 2;
        [Range(0, 4)] public int predictiveSectionLead = 2;
        [Min(0.5f)] public float unloadDelaySeconds = 8f;
        [Range(1, 16)] public int workerCount = 3;
        [Range(1, 16)] public int meshUploadsPerFrame = 2;
        [Range(1, 32)] public int mainThreadCallbacksPerFrame = 8;

        [Header("Player")]
        [Min(0.1f)] public float walkSpeed = 5.5f;
        [Min(0.1f)] public float sprintSpeed = 9f;
        [Min(0.1f)] public float flightSpeed = 11f;
        [Min(0.1f)] public float flightSprintSpeed = 22f;
        [Min(0.1f)] public float jumpSpeed = 7.5f;
        [Min(0.1f)] public float gravity = 24f;
        [Range(0.25f, 0.6f)] public float capsuleRadius = 0.38f;
        [Range(1.2f, 2.4f)] public float capsuleHeight = 1.8f;
        [Range(0.1f, 1.1f)] public float stepHeight = 0.65f;
        [Range(0.01f, 0.3f)] public float mouseSensitivity = 0.12f;
        [Range(3f, 12f)] public float interactionReach = 7f;

        [Header("Terrain")]
        [Range(1f, 20f)] public float continentHeight = 9f;
        [Range(1f, 24f)] public float mountainHeight = 13f;
        [Range(0.1f, 8f)] public float detailHeight = 2f;
        [Range(0.1f, 8f)] public float caveThreshold = 1.7f;

        public void ClampValues()
        {
            faceCellResolution = Mathf.Max(32, faceCellResolution);
            faceCellResolution = Mathf.CeilToInt(faceCellResolution / 16f) * 16;
            maximumRadialBlock = Mathf.Max(minimumRadialBlock + 16, maximumRadialBlock);
            workerCount = Mathf.Max(1, workerCount);
            meshUploadsPerFrame = Mathf.Max(1, meshUploadsPerFrame);
        }

        private void OnValidate()
        {
            ClampValues();
        }
    }
}
