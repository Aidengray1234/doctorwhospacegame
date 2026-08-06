using System;
using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Generation
{
    public sealed class DeterministicNoise
    {
        private readonly int seed;

        public DeterministicNoise(int seedValue)
        {
            seed = seedValue;
        }

        public double Value(Double3 p)
        {
            int x0 = (int)Math.Floor(p.x);
            int y0 = (int)Math.Floor(p.y);
            int z0 = (int)Math.Floor(p.z);
            double tx = Smooth(p.x - x0);
            double ty = Smooth(p.y - y0);
            double tz = Smooth(p.z - z0);

            double a = Lerp(Hash(x0, y0, z0), Hash(x0 + 1, y0, z0), tx);
            double b = Lerp(Hash(x0, y0 + 1, z0), Hash(x0 + 1, y0 + 1, z0), tx);
            double c = Lerp(Hash(x0, y0, z0 + 1), Hash(x0 + 1, y0, z0 + 1), tx);
            double d = Lerp(Hash(x0, y0 + 1, z0 + 1), Hash(x0 + 1, y0 + 1, z0 + 1), tx);
            return Lerp(Lerp(a, b, ty), Lerp(c, d, ty), tz) * 2d - 1d;
        }

        public double Fbm(Double3 p, int octaves, double lacunarity = 2d, double gain = 0.5d)
        {
            double sum = 0d;
            double amplitude = 0.5d;
            double normalization = 0d;
            for (int i = 0; i < octaves; i++)
            {
                sum += Value(p) * amplitude;
                normalization += amplitude;
                p *= lacunarity;
                amplitude *= gain;
            }
            return normalization > 0d ? sum / normalization : 0d;
        }

        public double Ridged(Double3 p, int octaves)
        {
            double value = 1d - Math.Abs(Fbm(p, octaves));
            return value * value;
        }

        private double Hash(int x, int y, int z)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + z * 2147483647 + seed * 1442695041);
                h = (h ^ (h >> 13)) * 1274126177u;
                h ^= h >> 16;
                return (h & 0x00FFFFFF) / 16777215d;
            }
        }

        private static double Smooth(double t)
        {
            return t * t * (3d - 2d * t);
        }

        private static double Lerp(double a, double b, double t)
        {
            return a + (b - a) * t;
        }
    }
}
