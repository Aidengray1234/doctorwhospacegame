using DoctorWho.VoxelUniverse.Input;
using DoctorWho.VoxelUniverse.Saves;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class VoxelUniverseDiagnostics : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private VoxelSaveSystem saves;
        [SerializeField] private bool visible = true;
        private StableCartesianVoxelGrid stableGrid;
        private StablePlanetCoverRenderer stableCover;
        private StableGridEditStore stableEdits;
        private float worstFrameMilliseconds;

        public void Configure(VoxelUniverseWorld voxelWorld, VoxelSaveSystem saveSystem)
        {
            world = voxelWorld;
            saves = saveSystem;
        }

        private void Update()
        {
            if (stableGrid == null) stableGrid = FindObjectOfType<StableCartesianVoxelGrid>();
            if (stableCover == null) stableCover = FindObjectOfType<StablePlanetCoverRenderer>();
            if (stableEdits == null) stableEdits = FindObjectOfType<StableGridEditStore>();
            float milliseconds = Time.unscaledDeltaTime * 1000f;
            if (milliseconds > worstFrameMilliseconds) worstFrameMilliseconds = milliseconds;
            if (VoxelInput.DiagnosticsPressed) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || world == null) return;
            GUI.Box(new Rect(12f, 12f, 370f, 278f), "Voxel Universe Diagnostics (F3)");
            GUI.Label(new Rect(24f, 38f, 345f, 20f), "Worker cube terrain: "
                + (stableGrid != null ? stableGrid.ReadyChunkCount.ToString() : "not installed")
                + " ready / " + (stableGrid != null ? stableGrid.RequestedChunkCount.ToString() : "-")
                + " tracked");
            GUI.Label(new Rect(24f, 58f, 345f, 20f), "Worker jobs: "
                + (stableGrid != null ? stableGrid.QueuedChunkCount.ToString() : "-")
                + " queued / " + (stableGrid != null ? stableGrid.ActiveWorkerCount.ToString() : "-")
                + " active");
            GUI.Label(new Rect(24f, 78f, 345f, 20f), "Mesh uploads: "
                + (stableGrid != null ? stableGrid.PendingUploadCount.ToString() : "-")
                + " pending / " + (stableGrid != null ? stableGrid.UploadedThisFrame.ToString() : "-")
                + " this frame");
            GUI.Label(new Rect(24f, 98f, 345f, 20f), "Surface detail coverage: "
                + (stableGrid != null ? stableGrid.ReadyCoverageRadius.ToString("0") : "-")
                + " blocks");
            GUI.Label(new Rect(24f, 118f, 345f, 20f), "Horizon terrain: "
                + (stableCover != null ? stableCover.MiddleFacesReady.ToString() : "-")
                + "/6 faces"
                + (stableCover != null && stableCover.Ready ? " READY" : " building"));
            GUI.Label(new Rect(24f, 138f, 345f, 20f), "Orbital planet: "
                + (stableCover != null ? stableCover.FarFacesReady.ToString() : "-")
                + "/6"
                + (stableCover != null && stableCover.OrbitalVisible ? " VISIBLE" : " hidden near surface"));
            GUI.Label(new Rect(24f, 158f, 345f, 20f), "Logical legacy sections: "
                + world.LoadedSectionCount + " (not required by worker grid)");
            GUI.Label(new Rect(24f, 178f, 345f, 20f), "Last worker surface columns: "
                + (stableGrid != null ? stableGrid.LastSurfaceColumnsSampled.ToString() : "-"));
            GUI.Label(new Rect(24f, 198f, 345f, 20f), "Worst frame: "
                + worstFrameMilliseconds.ToString("0.0") + " ms");
            GUI.Label(new Rect(24f, 218f, 345f, 20f), "Spherical edits: "
                + (saves != null ? saves.EditCount : 0) + "  stable edits: "
                + (stableEdits != null ? stableEdits.EditCount : 0));
            GUI.Label(new Rect(24f, 238f, 345f, 20f),
                "Grid: permanent body-centered 1x1x1 cubes");
            GUI.Label(new Rect(24f, 258f, 345f, 20f),
                "LOD: cubes -> horizon terrain -> orbital planet");
        }
    }
}
