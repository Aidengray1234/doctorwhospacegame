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
        private float worstFrameMilliseconds;

        public void Configure(VoxelUniverseWorld voxelWorld, VoxelSaveSystem saveSystem)
        {
            world = voxelWorld;
            saves = saveSystem;
        }

        private void Update()
        {
            float milliseconds = Time.unscaledDeltaTime * 1000f;
            if (milliseconds > worstFrameMilliseconds) worstFrameMilliseconds = milliseconds;
            if (VoxelInput.DiagnosticsPressed) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible || world == null) return;
            GUI.Box(new Rect(12f, 12f, 310f, 178f), "Voxel Universe Diagnostics (F3)");
            GUI.Label(new Rect(24f, 38f, 290f, 20f), "Near sections: " + world.LoadedSectionCount);
            GUI.Label(new Rect(24f, 58f, 290f, 20f), "Cached sections: " + world.CachedSectionCount);
            GUI.Label(new Rect(24f, 78f, 290f, 20f),
                "Jobs queued/active: " + world.QueuedJobCount + " / " + world.ActiveWorkerCount);
            GUI.Label(new Rect(24f, 98f, 290f, 20f),
                "Mesh uploads/frame: " + world.MeshUploadsThisFrame + "  pending: " + world.PendingUploadCount);
            GUI.Label(new Rect(24f, 118f, 290f, 20f),
                "Voxel data memory: " + (world.EstimatedSectionBytes / 1024f).ToString("0.0") + " KiB");
            GUI.Label(new Rect(24f, 138f, 290f, 20f),
                "Worst frame: " + worstFrameMilliseconds.ToString("0.0") + " ms");
            GUI.Label(new Rect(24f, 158f, 290f, 20f),
                "Edits: " + (saves != null ? saves.EditCount : 0)
                + "  safe spawn: " + (world.TimeToSafeSpawn < 0f ? "loading" : world.TimeToSafeSpawn.ToString("0.00") + "s"));
        }
    }
}
