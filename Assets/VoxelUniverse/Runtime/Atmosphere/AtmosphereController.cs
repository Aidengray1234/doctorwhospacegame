using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Atmosphere
{
    public sealed class AtmosphereController : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Transform observer;
        [SerializeField] private Material atmosphereMaterial;
        private GameObject shell;
        private MeshRenderer shellRenderer;
        private MaterialPropertyBlock properties;

        public void Configure(VoxelUniverseWorld voxelWorld, Transform trackingObserver, Material material)
        {
            world = voxelWorld;
            observer = trackingObserver;
            atmosphereMaterial = material;
            RebuildNow();
        }

        private void Awake() { EnsureShell(); }
        private void OnEnable() { EnsureShell(); }

        private void LateUpdate()
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
            if (shell == null || shellRenderer == null) return;
            shell.SetActive(true);

            float groundRadius = world.Settings.groundRadius;
            float atmosphereHeight = Mathf.Max(8f, body.atmosphereHeight);
            float atmosphereRadius = groundRadius + atmosphereHeight;
            float altitude = (observer.position - world.Center).magnitude - groundRadius;
            float normalizedAltitude = Mathf.Clamp01(altitude / atmosphereHeight);
            float localDensity = Mathf.Pow(1f - normalizedAltitude,
                Mathf.Lerp(0.7f, 2.4f, Mathf.Clamp01(body.densityFalloff * 2f)));

            Color horizon = Color.Lerp(body.atmosphereColor, body.sunsetColor,
                Mathf.Clamp01(0.12f + normalizedAltitude * 0.12f));
            RenderSettings.fog = altitude < atmosphereHeight * 1.15f;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Color.Lerp(Color.black, horizon, localDensity);
            RenderSettings.fogDensity = Mathf.Max(0.00002f,
                body.densityFalloff * localDensity * 0.0065f);

            if (properties == null) properties = new MaterialPropertyBlock();
            shellRenderer.GetPropertyBlock(properties);
            properties.SetVector("_PlanetCenter", new Vector4(world.Center.x, world.Center.y, world.Center.z, 0f));
            properties.SetFloat("_GroundRadius", groundRadius);
            properties.SetFloat("_AtmosphereRadius", atmosphereRadius);
            properties.SetFloat("_Density", Mathf.Max(0.05f, body.densityFalloff * 4f));
            properties.SetColor("_AtmosphereColor", body.atmosphereColor);
            properties.SetColor("_SunsetColor", body.sunsetColor);
            Light sun = RenderSettings.sun != null ? RenderSettings.sun : FindDirectionalLight();
            Vector3 sunDirection = sun != null ? -sun.transform.forward : new Vector3(0.35f, 0.72f, 0.24f).normalized;
            properties.SetVector("_SunDirection", new Vector4(sunDirection.x, sunDirection.y, sunDirection.z, 0f));
            properties.SetFloat("_ObserverAltitude01", normalizedAltitude);
            shellRenderer.SetPropertyBlock(properties);
        }

        public void RebuildNow()
        {
            EnsureShell();
            if (world == null || world.Settings == null || world.BodyDefinition == null || shell == null)
                return;
            float height = Mathf.Max(8f, world.BodyDefinition.atmosphereHeight);
            float radius = world.Settings.groundRadius + height;
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * radius * 2f;
            if (shellRenderer != null)
            {
                shellRenderer.sharedMaterial = atmosphereMaterial;
                shellRenderer.shadowCastingMode = ShadowCastingMode.Off;
                shellRenderer.receiveShadows = false;
            }
        }

        private void EnsureShell()
        {
            if (world == null || world.BodyDefinition == null || !world.BodyDefinition.hasAtmosphere)
                return;
            Transform found = transform.Find("Layered Atmosphere Shell");
            shell = found != null ? found.gameObject : GameObject.CreatePrimitive(PrimitiveType.Sphere);
            shell.name = "Layered Atmosphere Shell";
            shell.transform.SetParent(transform, false);
            Collider collider = shell.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying) Destroy(collider);
                else DestroyImmediate(collider);
            }
            shellRenderer = shell.GetComponent<MeshRenderer>();
            if (shellRenderer != null && atmosphereMaterial != null)
                shellRenderer.sharedMaterial = atmosphereMaterial;
            RebuildScaleOnly();
        }

        private void RebuildScaleOnly()
        {
            if (shell == null || world == null || world.Settings == null || world.BodyDefinition == null)
                return;
            float radius = world.Settings.groundRadius + Mathf.Max(8f, world.BodyDefinition.atmosphereHeight);
            shell.transform.localPosition = Vector3.zero;
            shell.transform.localScale = Vector3.one * radius * 2f;
        }

        private static Light FindDirectionalLight()
        {
            Light[] lights = Object.FindObjectsOfType<Light>();
            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null && lights[i].enabled && lights[i].type == LightType.Directional)
                    return lights[i];
            return null;
        }
    }
}
