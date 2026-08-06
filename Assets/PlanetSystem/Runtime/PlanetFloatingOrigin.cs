using UnityEngine;

namespace DoctorWho.Planets
{
    public sealed class PlanetFloatingOrigin : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private PlanetGenerationSettings settings;
        public Vector3 TotalOffset { get; private set; }

        public void Configure(Transform followTarget, PlanetGenerationSettings generationSettings)
        {
            target = followTarget;
            settings = generationSettings;
        }

        private void LateUpdate()
        {
            if (target == null || settings == null) return;
            if (target.position.sqrMagnitude < settings.floatingOriginThreshold * settings.floatingOriginThreshold) return;

            Vector3 shift = target.position;
            GameObject[] roots = gameObject.scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++) roots[i].transform.position -= shift;
            TotalOffset += shift;
            Physics.SyncTransforms();
        }
    }
}
