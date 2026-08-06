using DoctorWho.VoxelUniverse.Meshing;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class VoxelSectionRenderer : MonoBehaviour
    {
        private MeshFilter opaqueFilter;
        private MeshRenderer opaqueRenderer;
        private MeshFilter waterFilter;
        private MeshRenderer waterRenderer;
        private Mesh opaqueMesh;
        private Mesh waterMesh;

        public SectionKey Key { get; private set; }

        public void Configure(SectionKey key, Material opaqueMaterial, Material waterMaterial)
        {
            Key = key;
            name = "Section " + key;
            opaqueFilter = GetComponent<MeshFilter>();
            if (opaqueFilter == null) opaqueFilter = gameObject.AddComponent<MeshFilter>();
            opaqueRenderer = GetComponent<MeshRenderer>();
            if (opaqueRenderer == null) opaqueRenderer = gameObject.AddComponent<MeshRenderer>();
            opaqueRenderer.sharedMaterial = opaqueMaterial;
            opaqueRenderer.shadowCastingMode = ShadowCastingMode.On;
            opaqueRenderer.receiveShadows = true;

            Transform waterTransform = transform.Find("Water");
            GameObject waterObject = waterTransform != null ? waterTransform.gameObject : new GameObject("Water");
            waterObject.transform.SetParent(transform, false);
            waterFilter = waterObject.GetComponent<MeshFilter>();
            if (waterFilter == null) waterFilter = waterObject.AddComponent<MeshFilter>();
            waterRenderer = waterObject.GetComponent<MeshRenderer>();
            if (waterRenderer == null) waterRenderer = waterObject.AddComponent<MeshRenderer>();
            waterRenderer.sharedMaterial = waterMaterial;
            waterRenderer.shadowCastingMode = ShadowCastingMode.Off;
            waterRenderer.receiveShadows = false;
        }

        public void Apply(SectionMeshData data)
        {
            if (data == null) return;
            ReplaceMesh(ref opaqueMesh, opaqueFilter, data.opaque, "Opaque " + data.key);
            ReplaceMesh(ref waterMesh, waterFilter, data.water, "Water " + data.key);
            opaqueRenderer.enabled = !data.opaque.IsEmpty;
            waterRenderer.enabled = !data.water.IsEmpty;
        }

        private static void ReplaceMesh(ref Mesh mesh, MeshFilter filter, MeshPayload payload, string meshName)
        {
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.name = meshName;
                mesh.indexFormat = IndexFormat.UInt32;
            }
            else
            {
                mesh.Clear(false);
            }

            if (payload == null || payload.IsEmpty)
            {
                filter.sharedMesh = mesh;
                return;
            }

            mesh.vertices = payload.vertices;
            mesh.normals = payload.normals;
            mesh.uv = payload.uv;
            mesh.colors32 = payload.colors;
            mesh.triangles = payload.triangles;
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
        }

        private void OnDestroy()
        {
            if (opaqueMesh != null) { if (Application.isPlaying) Destroy(opaqueMesh); else DestroyImmediate(opaqueMesh); }
            if (waterMesh != null) { if (Application.isPlaying) Destroy(waterMesh); else DestroyImmediate(waterMesh); }
        }
    }
}
