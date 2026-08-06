using UnityEngine;
using DoctorWho.VoxelUniverse.Voxels;

namespace DoctorWho.VoxelUniverse.Meshing
{
    public sealed class MeshPayload
    {
        public Vector3[] vertices = new Vector3[0];
        public Vector3[] normals = new Vector3[0];
        public Vector2[] uv = new Vector2[0];
        public Color32[] colors = new Color32[0];
        public int[] triangles = new int[0];

        public bool IsEmpty
        {
            get { return vertices == null || vertices.Length == 0; }
        }
    }

    public sealed class SectionMeshData
    {
        public SectionKey key;
        public int requestVersion;
        public VoxelSection section;
        public MeshPayload opaque = new MeshPayload();
        public MeshPayload water = new MeshPayload();
    }
}
