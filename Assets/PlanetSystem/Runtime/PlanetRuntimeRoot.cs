using UnityEngine;

namespace DoctorWho.Planets
{
    public sealed class PlanetRuntimeRoot : MonoBehaviour
    {
        [SerializeField] private PlanetGenerationSettings settings;
        [SerializeField] private Transform trackingTarget;
        [SerializeField] private PlanetStreamingController streamingController;

        public PlanetGenerationSettings Settings => settings;
        public Transform TrackingTarget => trackingTarget;

        public void Configure(PlanetGenerationSettings generationSettings, Transform target)
        {
            settings = generationSettings;
            trackingTarget = target;
            if (streamingController == null)
            {
                streamingController = GetComponent<PlanetStreamingController>();
            }
            streamingController?.Configure(settings, trackingTarget);
        }

        private void Awake()
        {
            if (streamingController == null)
            {
                streamingController = GetComponent<PlanetStreamingController>();
            }
            streamingController?.Configure(settings, trackingTarget);
        }
    }
}
