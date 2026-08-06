using System.Collections.Generic;
using UnityEngine;

namespace DoctorWho.Planets
{
    /// <summary>
    /// Compatibility tracker retained for systems that still inspect nearby voxel-style cells.
    /// Planet V2 terrain rendering and collision are handled by PlanetPrototypeGenerator's
    /// quadtree patches; this component no longer owns terrain generation.
    /// </summary>
    public sealed class PlanetStreamingController : MonoBehaviour
    {
        [SerializeField] private PlanetGenerationSettings settings;
        [SerializeField] private Transform trackingTarget;
        [SerializeField] private Transform chunkRoot;

        private readonly HashSet<VoxelChunkCoord> desiredChunks = new HashSet<VoxelChunkCoord>();
        private VoxelChunkCoord lastCenter;
        private bool hasCenter;

        public IReadOnlyCollection<VoxelChunkCoord> DesiredChunks => desiredChunks;

        public void Configure(PlanetGenerationSettings generationSettings, Transform target)
        {
            settings = generationSettings;
            trackingTarget = target;
            EnsureChunkRoot();
            RebuildDesiredSet(true);
        }

        private void Awake()
        {
            EnsureChunkRoot();
        }

        private void Update()
        {
            if (settings == null || trackingTarget == null)
            {
                return;
            }

            RebuildDesiredSet(false);
        }

        private void EnsureChunkRoot()
        {
            if (chunkRoot != null)
            {
                return;
            }

            Transform existing = transform.Find("Chunks");
            if (existing != null)
            {
                chunkRoot = existing;
                return;
            }

            var root = new GameObject("Chunks");
            root.transform.SetParent(transform, false);
            chunkRoot = root.transform;
        }

        private void RebuildDesiredSet(bool force)
        {
            if (settings == null || trackingTarget == null)
            {
                return;
            }

            float cellSize = GetCompatibilityCellSize();
            VoxelChunkCoord center = VoxelChunkCoord.FromWorld(
                trackingTarget.position - transform.position,
                cellSize);

            if (!force && hasCenter && center.Equals(lastCenter))
            {
                return;
            }

            hasCenter = true;
            lastCenter = center;
            desiredChunks.Clear();

            int radius = GetCompatibilityRadius(cellSize);
            for (int z = -radius; z <= radius; z++)
            {
                for (int y = -radius; y <= radius; y++)
                {
                    for (int x = -radius; x <= radius; x++)
                    {
                        if (x * x + y * y + z * z > radius * radius)
                        {
                            continue;
                        }

                        desiredChunks.Add(new VoxelChunkCoord(center.x + x, center.y + y, center.z + z));
                    }
                }
            }
        }

        private float GetCompatibilityCellSize()
        {
            int lodDivisions = 1 << Mathf.Clamp(settings.maxLod, 1, 12);
            return Mathf.Max(1f, settings.radius * 2f / lodDivisions);
        }

        private int GetCompatibilityRadius(float cellSize)
        {
            return Mathf.Clamp(Mathf.CeilToInt(settings.colliderDistance / cellSize), 1, 8);
        }
    }
}
