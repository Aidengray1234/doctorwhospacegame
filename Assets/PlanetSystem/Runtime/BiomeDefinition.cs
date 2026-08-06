using UnityEngine;

namespace DoctorWho.Planets
{
    [CreateAssetMenu(menuName = "Doctor Who/Planets/Biome Definition", fileName = "BiomeDefinition")]
    public sealed class BiomeDefinition : ScriptableObject
    {
        public string biomeId = "temperate";
        public Color surfaceColor = new Color(0.25f, 0.55f, 0.22f);
        [Range(-1f, 1f)] public float minTemperature = -0.25f;
        [Range(-1f, 1f)] public float maxTemperature = 0.65f;
        [Range(0f, 1f)] public float minHumidity = 0.25f;
        [Range(0f, 1f)] public float maxHumidity = 1f;
        [Min(0f)] public float heightMultiplier = 1f;

        public bool Matches(float temperature, float humidity)
        {
            return temperature >= minTemperature && temperature <= maxTemperature &&
                   humidity >= minHumidity && humidity <= maxHumidity;
        }
    }
}
