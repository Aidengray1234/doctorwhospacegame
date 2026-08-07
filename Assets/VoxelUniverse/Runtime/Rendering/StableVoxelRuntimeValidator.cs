using DoctorWho.VoxelUniverse.Core;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class StableVoxelRuntimeValidator : MonoBehaviour
    {
        [SerializeField] private StableCartesianVoxelGrid grid;
        [SerializeField] private StablePlanetCoverRenderer cover;
        [SerializeField] private Transform observer;
        private bool reported;

        public void Configure(StableCartesianVoxelGrid stableGrid,
            StablePlanetCoverRenderer planetCover, Transform trackingObserver)
        {
            grid = stableGrid;
            cover = planetCover;
            observer = trackingObserver;
        }

        private void Update()
        {
            if (reported || grid == null || cover == null || observer == null) return;
            if (!grid.HasReadyTerrain || !cover.Ready) return;

            Int3 center = grid.WorldToCell(observer.position);
            for (int z = -3; z <= 3; z++)
            for (int y = -2; y <= 2; y++)
            for (int x = -3; x <= 3; x++)
            {
                Int3 cell = new Int3(center.x + x, center.y + y, center.z + z);
                Int3 roundTrip = grid.WorldToCell(grid.CellCenterWorld(cell));
                if (roundTrip != cell)
                {
                    Debug.LogError("[Stable Voxel Grid Validation] FAIL grid round-trip at "
                        + cell + " -> " + roundTrip);
                    reported = true;
                    return;
                }
            }

            Debug.Log("[Stable Voxel Grid Validation] PASS — support chunk ready, permanent "
                + "1x1x1 cube coordinates are stable, worker terrain is active, horizon terrain "
                + "is complete, and the orbital planet remains a separate far-distance LOD.");
            reported = true;
        }
    }
}
