using UnityEngine;

namespace DoctorWho.Planets
{
    internal sealed class PlanetNoise
    {
        private readonly PlanetGenerationSettings s;
        private readonly int seed;

        public PlanetNoise(PlanetGenerationSettings settings) { s = settings; seed = settings.seed; }

        public float Height01(Vector3 unit)
        {
            Vector3 p = unit * s.radius;
            Vector3 warp = new Vector3(
                Fbm(p * s.warpFrequency + SeedOffset(11), 4),
                Fbm(p * s.warpFrequency + SeedOffset(23), 4),
                Fbm(p * s.warpFrequency + SeedOffset(37), 4)) * s.warpStrength;
            p += warp;

            float continental = Fbm(p * s.continentFrequency + SeedOffset(3), 6) * .5f + .5f;
            float land = Mathf.Clamp01((continental - s.continentThreshold) / Mathf.Max(.001f, 1f - s.continentThreshold));
            land = Mathf.Pow(land, s.continentPower);

            float ridged = 1f - Mathf.Abs(Fbm(p * s.mountainFrequency + SeedOffset(7), 5));
            ridged = Mathf.Pow(Mathf.Clamp01(ridged), 3.4f);
            float erosion = Fbm(p * s.erosionFrequency + SeedOffset(17), 4) * .5f + .5f;
            float mountain = ridged * land * Mathf.Lerp(1f, erosion, s.erosionStrength) * s.mountainStrength;

            float plate = Mathf.Floor((land + mountain * .35f) * 7f) / 7f;
            float plateauMask = Mathf.SmoothStep(.58f, .84f, Value(p * .0011f + SeedOffset(41)) * .5f + .5f);
            float macro = Mathf.Lerp(land, plate, plateauMask * .32f) + mountain;

            float dunes = Mathf.Sin((p.x + p.z) * .018f + Fbm(p * .006f, 2) * 4f) * .025f;
            float fine = Fbm(p * s.detailFrequency + SeedOffset(29), 3) * s.detailStrength;
            float basin = Mathf.Lerp(-.62f, .02f, Mathf.SmoothStep(.18f, .54f, continental));
            return Mathf.Clamp(basin + macro + fine + dunes * DesertMask(unit, p), -1f, 1f);
        }

        public float Radius(Vector3 unit) => s.radius + Height01(unit.normalized) * s.maxTerrainHeight;

        public Color Biome(Vector3 unit, float h, float slope)
        {
            Vector3 p = unit * s.radius;
            float latitude = Mathf.Abs(unit.y);
            float temperature = Mathf.Clamp01(1f - latitude + Fbm(p * s.climateFrequency + SeedOffset(53), 3) * .18f - h * .22f);
            float moisture = Mathf.Clamp01(Fbm(p * s.climateFrequency + SeedOffset(67), 4) * .5f + .5f);
            float shore = Mathf.InverseLerp(s.seaLevel - .02f, s.seaLevel + .045f, h);

            Color beach = new Color(.66f, .56f, .36f);
            Color desert = new Color(.58f, .39f, .18f);
            Color grass = new Color(.12f, .34f, .11f);
            Color forest = new Color(.035f, .19f, .07f);
            Color tundra = new Color(.34f, .39f, .30f);
            Color rock = new Color(.28f, .27f, .25f);
            Color snow = new Color(.88f, .92f, .96f);

            Color climate = temperature > .58f ? Color.Lerp(desert, grass, moisture) : Color.Lerp(tundra, forest, moisture);
            climate = Color.Lerp(beach, climate, shore);
            float rockMask = Mathf.SmoothStep(.46f, .78f, slope) + Mathf.SmoothStep(.38f, .72f, h) * .55f;
            climate = Color.Lerp(climate, rock, Mathf.Clamp01(rockMask));
            float snowMask = Mathf.Max(Mathf.SmoothStep(s.polarStart, 1f, latitude), Mathf.SmoothStep(s.snowHeight, .92f, h));
            return Color.Lerp(climate, snow, snowMask);
        }

        private float DesertMask(Vector3 u, Vector3 p)
        {
            float t = 1f - Mathf.Abs(u.y);
            float m = Fbm(p * s.climateFrequency + SeedOffset(67), 3) * .5f + .5f;
            return Mathf.SmoothStep(.5f, .85f, t) * (1f - Mathf.SmoothStep(.28f, .55f, m));
        }

        private Vector3 SeedOffset(int n)
        {
            uint x = Hash((uint)(seed + n * 1013));
            uint y = Hash(x + 0x9e3779b9u);
            uint z = Hash(y + 0x85ebca6bu);
            return new Vector3((x & 1023) * .173f, (y & 1023) * .197f, (z & 1023) * .223f);
        }

        private static float Fbm(Vector3 p, int octaves)
        {
            float sum = 0f, amp = .5f, norm = 0f;
            for (int i = 0; i < octaves; i++) { sum += Value(p) * amp; norm += amp; p = p * 2.03f + new Vector3(19.1f, 7.7f, 31.3f); amp *= .5f; }
            return sum / Mathf.Max(.0001f, norm);
        }

        private static float Value(Vector3 p)
        {
            Vector3Int i = new Vector3Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y), Mathf.FloorToInt(p.z));
            Vector3 f = new Vector3(p.x - i.x, p.y - i.y, p.z - i.z);
            f = new Vector3(f.x * f.x * (3f - 2f * f.x), f.y * f.y * (3f - 2f * f.y), f.z * f.z * (3f - 2f * f.z));
            float c000 = Rand(i.x, i.y, i.z), c100 = Rand(i.x + 1, i.y, i.z), c010 = Rand(i.x, i.y + 1, i.z), c110 = Rand(i.x + 1, i.y + 1, i.z);
            float c001 = Rand(i.x, i.y, i.z + 1), c101 = Rand(i.x + 1, i.y, i.z + 1), c011 = Rand(i.x, i.y + 1, i.z + 1), c111 = Rand(i.x + 1, i.y + 1, i.z + 1);
            float x00 = Mathf.Lerp(c000, c100, f.x), x10 = Mathf.Lerp(c010, c110, f.x), x01 = Mathf.Lerp(c001, c101, f.x), x11 = Mathf.Lerp(c011, c111, f.x);
            return Mathf.Lerp(Mathf.Lerp(x00, x10, f.y), Mathf.Lerp(x01, x11, f.y), f.z);
        }

        private static float Rand(int x, int y, int z)
        {
            uint h = Hash((uint)x * 374761393u ^ (uint)y * 668265263u ^ (uint)z * 2246822519u);
            return (h / 2147483647.5f) - 1f;
        }

        private static uint Hash(uint x) { x ^= x >> 16; x *= 0x7feb352du; x ^= x >> 15; x *= 0x846ca68bu; x ^= x >> 16; return x; }
    }
}
