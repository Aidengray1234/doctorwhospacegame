using DoctorWho.VoxelUniverse.Core;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class FarPlanetRenderer : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Material material;
        private GameObject sphere;

        public void Configure(VoxelUniverseWorld voxelWorld, Material farMaterial)
        {
            world = voxelWorld;
            material = farMaterial;
            EnsureSphere();
        }

        private void Awake()
        {
            EnsureSphere();
        }

        private void OnEnable()
        {
            EnsureSphere();
        }

        private void EnsureSphere()
        {
            if (world == null || world.Settings == null) return;
            Transform found = transform.Find("Complete Far Planet");
            sphere = found != null ? found.gameObject : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "Complete Far Planet";
            sphere.transform.SetParent(transform, false);
            float radius = world.Settings.groundRadius - 1.25f;
            sphere.transform.localPosition = Vector3.zero;
            sphere.transform.localScale = Vector3.one * radius * 2f;
            Collider collider = sphere.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            MeshRenderer renderer = sphere.GetComponent<MeshRenderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;
        }
    }
}
