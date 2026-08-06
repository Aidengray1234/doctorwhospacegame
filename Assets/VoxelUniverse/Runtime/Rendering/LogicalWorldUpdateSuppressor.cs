using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    /// <summary>
    /// Lets VoxelUniverseWorld run Awake so its deterministic generator and save-backed
    /// logical APIs are initialized, then stops only the rejected warped section-streaming
    /// Update loop before the first gameplay frame.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class LogicalWorldUpdateSuppressor : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;

        public void Configure(VoxelUniverseWorld voxelWorld)
        {
            world = voxelWorld;
            if (world != null) world.enabled = true;
        }

        private void Awake()
        {
            if (world == null) world = GetComponent<VoxelUniverseWorld>();
            if (world != null) world.enabled = true;
        }

        private void Start()
        {
            if (world != null) world.enabled = false;
        }
    }
}
