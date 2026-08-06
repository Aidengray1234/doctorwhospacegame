using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    /// <summary>
    /// One-shot runtime acceptance check for the renderer handoff. It verifies that the
    /// near mesh uses half-integer Cartesian cube vertices and that both planet LODs exist.
    /// </summary>
    public sealed class NoWarpRuntimeValidator : MonoBehaviour
    {
        [SerializeField] private TangentVoxelClipmap tangentPatch;
        [SerializeField] private PlanetInfiniteLodRenderer planetLod;
        [SerializeField] private VoxelUniverseWorld world;
        private bool completed;
        private float started;

        private void Awake()
        {
            if (tangentPatch == null) tangentPatch = GetComponent<TangentVoxelClipmap>();
            if (planetLod == null) planetLod = GetComponent<PlanetInfiniteLodRenderer>();
            if (world == null) world = GetComponent<VoxelUniverseWorld>();
            started = Time.realtimeSinceStartup;
        }

        private void Update()
        {
            if (completed || tangentPatch == null || !tangentPatch.Ready) return;
            if (Time.realtimeSinceStartup - started < 0.25f) return;
            completed = true;

            Transform patchRoot = transform.Find("No-Warp Tangent Cube Patch");
            Transform middle = transform.Find("Middle Planet Clipmap");
            Transform far = transform.Find("Complete Far Planet Clipmap");
            bool valid = patchRoot != null && middle != null && far != null;
            int checkedVertices = 0;
            if (patchRoot != null)
            {
                MeshFilter[] filters = patchRoot.GetComponentsInChildren<MeshFilter>(true);
                for (int i = 0; i < filters.Length && checkedVertices < 2048; i++)
                {
                    Mesh mesh = filters[i].sharedMesh;
                    if (mesh == null) continue;
                    Vector3[] vertices = mesh.vertices;
                    for (int v = 0; v < vertices.Length && checkedVertices < 2048; v++)
                    {
                        Vector3 p = vertices[v];
                        valid &= IsHalfInteger(p.x) && IsHalfInteger(p.y) && IsHalfInteger(p.z);
                        checkedVertices++;
                    }
                }
            }
            valid &= checkedVertices > 0;
            if (world == null || world.Settings == null) valid = false;
            else
            {
                valid &= world.Settings.middleInnerRadiusBlocks < world.Settings.tangentPatchRadius;
                valid &= world.Settings.farHoleRadiusBlocks < world.Settings.middleOuterRadiusBlocks;
            }
            if (valid)
                Debug.Log("[Voxel Universe No-Warp Validation] PASS: true cube vertices, tangent patch, middle clipmap and complete far planet are active.");
            else
                Debug.LogError("[Voxel Universe No-Warp Validation] FAILED: a near mesh vertex was warped or a required LOD object is missing.");
        }

        private static bool IsHalfInteger(float value)
        {
            float doubled = value * 2f;
            return Mathf.Abs(doubled - Mathf.Round(doubled)) <= 0.0001f
                && Mathf.Abs(Mathf.Repeat(Mathf.Abs(doubled), 2f) - 1f) <= 0.0001f;
        }
    }
}
