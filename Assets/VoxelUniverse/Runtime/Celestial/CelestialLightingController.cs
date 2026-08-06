using DoctorWho.VoxelUniverse.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace DoctorWho.VoxelUniverse.Celestial
{
    public sealed class CelestialLightingController : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private Light primarySun;
        [SerializeField] private Light secondarySun;
        [SerializeField] private float simulationTimeScale = 30f;
        private double simulationSeconds;

        public void Configure(VoxelUniverseWorld voxelWorld, Light mainSun, Light secondSun)
        {
            world = voxelWorld;
            primarySun = mainSun;
            secondarySun = secondSun;
        }

        private void Update()
        {
            if (world == null || world.BodyDefinition == null) return;
            simulationSeconds += Time.deltaTime * simulationTimeScale;
            double rotationPeriod = System.Math.Max(1d, world.BodyDefinition.rotationPeriodSeconds);
            float phase = (float)((simulationSeconds / rotationPeriod) * 360d);
            float tilt = world.BodyDefinition.axialTiltDegrees;

            Vector3 primaryDirection = Quaternion.Euler(tilt, phase, 0f) * Vector3.forward;
            Vector3 secondaryDirection = Quaternion.Euler(-tilt * 0.45f, phase + 118f, 0f) * Vector3.forward;

            if (primarySun != null)
            {
                primarySun.transform.rotation = Quaternion.LookRotation(-primaryDirection, Vector3.up);
                primarySun.intensity = 1.15f;
                primarySun.color = new Color(1f, 0.94f, 0.84f);
                primarySun.shadows = LightShadows.Soft;
            }

            if (secondarySun != null)
            {
                secondarySun.transform.rotation = Quaternion.LookRotation(-secondaryDirection, Vector3.up);
                secondarySun.intensity = 0.28f;
                secondarySun.color = new Color(0.58f, 0.72f, 1f);
                secondarySun.shadows = LightShadows.None;
            }

            float daylight = Mathf.Clamp01(Vector3.Dot(Vector3.up, primaryDirection) * 0.5f + 0.5f);
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.Lerp(
                new Color(0.015f, 0.02f, 0.05f),
                new Color(0.42f, 0.48f, 0.58f),
                daylight);
        }
    }
}
