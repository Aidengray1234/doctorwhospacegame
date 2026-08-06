using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Rendering;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Atmosphere
{
    public sealed class AtmosphereController : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material atmosphereMaterial;
        private GameObject shell;

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver, Material material)
        {
            world = voxelWorld;
            observer = trackingObserver;
            atmosphereMaterial = material;
            EnsureShell();
        }

        private void Update()
        {
            if (world == null || world.BodyDefinition == null || observer == null) return;
            CelestialBodyDefinition body = world.BodyDefinition;
            if (!body.hasAtmosphere)
            {
                RenderSettings.fog = false;
                if (shell != null) shell.SetActive(false);
                return;
            }

            EnsureShell();
            float altitude = (observer.position - world.Center).magnitude - world.Settings.groundRadius;
            float atmosphereHeight = Mathf.Max(1f, body.atmosphereHeight);
            float density = Mathf.Clamp01(1f - altitude / atmosphereHeight);
            RenderSettings.fog = density > 0.001f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(Color.black, body.atmosphereColor, density);
            RenderSettings.fogDensity = body.densityFalloff * density * 0.015f;
        }

        private void EnsureShell()
        {
            if (world == null || world.BodyDefinition == null || !world.BodyDefinition.hasAtmosphere) return;
            Transform found = transform.Find("Atmosphere Shell");
            shell = found != null ? found.gameObject : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "Atmosphere Shell";
            shell.transform.SetParent(transform, false);
            float radius = world.Settings.groundRadius + world.BodyDefinition.atmosphereHeight;
            shell.transform.localScale = Vector3.one * radius * 2f;
            Collider collider = shell.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            MeshRenderer renderer = shell.GetComponent<MeshRenderer>();
            if (renderer != null && atmosphereMaterial != null) renderer.sharedMaterial = atmosphereMaterial;
        }
    }
}
