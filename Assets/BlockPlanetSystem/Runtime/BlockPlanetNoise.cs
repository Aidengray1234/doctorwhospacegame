using UnityEngine;

namespace DoctorWho.BlockPlanets
{
    public sealed class BlockPlanetNoise
    {
        private readonly int seed;

        public BlockPlanetNoise(int seed) => this.seed = seed;

        private float Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215f;
            }
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);

        public float Value3(Vector3 p)
        {
            int x0 = Mathf.FloorToInt(p.x);
            int y0 = Mathf.FloorToInt(p.y);
            int z0 = Mathf.FloorToInt(p.z);
            int x1 = x0 + 1;
            int y1 = y0 + 1;
            int z1 = z0 + 1;
            float tx = Smooth(p.x - x0);
            float ty = Smooth(p.y - y0);
            float tz = Smooth(p.z - z0);

            float a = Mathf.Lerp(Hash(x0, y0, z0), Hash(x1, y0, z0), tx);
            float b = Mathf.Lerp(Hash(x0, y1, z0), Hash(x1, y1, z0), tx);
            float c = Mathf.Lerp(Hash(x0, y0, z1), Hash(x1, y0, z1), tx);
            float d = Mathf.Lerp(Hash(x0, y1, z1), Hash(x1, y1, z1), tx);
            return Mathf.Lerp(Mathf.Lerp(a, b, ty), Mathf.Lerp(c, d, ty), tz) * 2f - 1f;
        }

        public float Fbm(Vector3 p, int octaves, float lacunarity = 2f, float gain = 0.5f)
        {
            float sum = 0f;
            float amplitude = 0.5f;
            float normalization = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value3(p) * amplitude;
                normalization += amplitude;
                p *= lacunarity;
                amplitude *= gain;
            }
            return normalization > 0f ? sum / normalization : 0f;
        }

        public float Ridged(Vector3 p, int octaves)
        {
            float value = 1f - Mathf.Abs(Fbm(p, octaves));
            return value * value;
        }

        public Vector3 Warp(Vector3 p, float frequency, float strength)
        {
            Vector3 q = p * frequency;
            return p + new Vector3(
                Fbm(q + new Vector3(17.1f, 3.7f, 9.2f), 3),
                Fbm(q + new Vector3(4.2f, 29.3f, 11.8f), 3),
                Fbm(q + new Vector3(13.9f, 7.4f, 31.6f), 3)) * strength;
        }
    }
}
