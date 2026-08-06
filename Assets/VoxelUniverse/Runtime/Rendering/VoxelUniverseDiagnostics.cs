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
            GUI.Box(new Rect(12f, 12f, 330f, 218f), "Voxel Universe Diagnostics (F3)");
            GUI.Label(new Rect(24f, 38f, 305f, 20f), "Logical sections: " + world.LoadedSectionCount);
            GUI.Label(new Rect(24f, 58f, 305f, 20f), "Logical jobs: "
                + world.QueuedJobCount + " / " + world.ActiveWorkerCount);
            GUI.Label(new Rect(24f, 78f, 305f, 20f), "Stable cube chunks: "
                + (stableGrid != null ? stableGrid.LoadedChunkCount.ToString() : "not installed"));
            GUI.Label(new Rect(24f, 98f, 305f, 20f), "Stable build queue: "
                + (stableGrid != null ? stableGrid.QueuedChunkCount.ToString() : "-")
                + "  completed/frame: " + (stableGrid != null ? stableGrid.CompletedThisFrame.ToString() : "-"));
            GUI.Label(new Rect(24f, 118f, 305f, 20f), "Planet cover: "
                + (stableCover != null && stableCover.Ready ? "COMPLETE" : "building"));
            GUI.Label(new Rect(24f, 138f, 305f, 20f), "Voxel data memory: "
                + (world.EstimatedSectionBytes / 1024f).ToString("0.0") + " KiB");
            GUI.Label(new Rect(24f, 158f, 305f, 20f), "Worst frame: "
                + worstFrameMilliseconds.ToString("0.0") + " ms");
            GUI.Label(new Rect(24f, 178f, 305f, 20f), "Spherical edits: "
                + (saves != null ? saves.EditCount : 0) + "  stable-grid edits: "
                + (stableEdits != null ? stableEdits.EditCount : 0));
            GUI.Label(new Rect(24f, 198f, 305f, 20f), "Grid model: body-centered Cartesian cubes");
        }
    }
}
