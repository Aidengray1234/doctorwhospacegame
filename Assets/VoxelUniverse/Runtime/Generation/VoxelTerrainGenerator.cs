using System;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;

namespace DoctorWho.VoxelUniverse.Generation
{
    public sealed class VoxelTerrainGenerator
    {
        private readonly VoxelUniverseSettings settings;
        private readonly CelestialBodyId bodyId;
        private readonly DeterministicNoise noise;

        public VoxelTerrainGenerator(VoxelUniverseSettings runtimeSettings, CelestialBodyId id)
        {
            settings = runtimeSettings;
            bodyId = id;
            noise = new DeterministicNoise(settings.seed);
        }

        public VoxelSection GenerateSection(SectionKey key)
        {
            VoxelSection section = new VoxelSection(key, settings.generatorVersion);
            for (int y = 0; y < VoxelConstants.SectionSize; y++)
            {
                for (int z = 0; z < VoxelConstants.SectionSize; z++)
                {
                    for (int x = 0; x < VoxelConstants.SectionSize; x++)
                    {
                        VoxelAddress address = section.ToAddress(x, y, z);
                        section.SetLocal(x, y, z, SampleBaseBlock(address));
                    }
                }
            }
            return section;
        }

        public BlockState SampleBaseBlock(VoxelAddress rawAddress)
        {
            VoxelAddress address = CubeSphereMapper.Canonicalize(rawAddress, settings.faceCellResolution);
            if (address.bodyId != bodyId) return BlockState.Air;
            if (address.radial < settings.minimumRadialBlock || address.radial >= settings.maximumRadialBlock)
                return BlockState.Air;

            int surface = GetSurfaceHeight(address.face, address.u, address.v);
            if (address.radial > surface)
            {
                return address.radial <= settings.seaLevel
                    ? new BlockState(BlockRegistry.Water, 0, 0)
                    : BlockState.Air;
            }

            if (address.radial <= settings.minimumRadialBlock + 2)
                return new BlockState(BlockRegistry.Bedrock, 0, 0);

            int depth = surface - address.radial;
            Double3 direction = CubeSphereMapper.CellCenterDirection(
                address.face, address.u, address.v, settings.faceCellResolution);
            Double3 cavePoint = direction * 37d + new Double3(0d, address.radial * 0.17d, 0d);

            if (depth > 6)
            {
                double cave = noise.Fbm(cavePoint * 1.7d + new Double3(13d, 71d, 29d), 3);
                if (cave > 0.68d + Math.Max(0d, 2d - settings.caveThreshold) * 0.04d)
                    return BlockState.Air;
            }

            if (depth > 5)
            {
                double ore = noise.Value(cavePoint * 3.4d + new Double3(91d, 17d, 43d));
                if (depth > 16 && ore > 0.73d) return new BlockState(BlockRegistry.IronOre, 0, 0);
                if (ore > 0.58d) return new BlockState(BlockRegistry.CoalOre, 0, 0);
            }

            double latitude = Math.Abs(direction.y);
            if (depth == 0)
            {
                if (surface <= settings.seaLevel + 1) return new BlockState(BlockRegistry.Sand, 0, 0);
                if (latitude > 0.73d || surface > settings.continentHeight + settings.mountainHeight * 0.65d)
                    return new BlockState(BlockRegistry.Snow, 0, 0);
                return new BlockState(BlockRegistry.Grass, 0, 0);
            }

            if (depth <= 3)
                return new BlockState(surface <= settings.seaLevel + 1 ? BlockRegistry.Sand : BlockRegistry.Dirt, 0, 0);

            return new BlockState(BlockRegistry.Stone, 0, 0);
        }

        public int GetSurfaceHeight(CubeSphereFace face, int u, int v)
        {
            VoxelAddress canonical = CubeSphereMapper.Canonicalize(
                new VoxelAddress(bodyId, face, u, v, 0),
                settings.faceCellResolution);

            Double3 direction = CubeSphereMapper.CellCenterDirection(
                canonical.face, canonical.u, canonical.v, settings.faceCellResolution);

            double continents = noise.Fbm(direction * 2.4d + new Double3(17d, 3d, 41d), 5);
            continents = Math.Sign(continents) * Math.Pow(Math.Abs(continents), 1.35d);
            double ridges = noise.Ridged(direction * 7.3d + new Double3(31d, 11d, 23d), 4);
            double mountainMask = Clamp01(continents * 1.4d + 0.58d);
            double detail = noise.Fbm(direction * 31d + new Double3(7d, 53d, 19d), 3);
            double height = continents * settings.continentHeight
                            + ridges * settings.mountainHeight * mountainMask
                            + detail * settings.detailHeight;
            return (int)Math.Round(height);
        }

        public VoxelAddress FindSurfaceAddress(Double3 direction)
        {
            CubeSphereFace face;
            double u;
            double v;
            CubeSphereMapper.DirectionToFaceUv(direction, out face, out u, out v);
            int cellU = Math.Max(0, Math.Min(settings.faceCellResolution - 1,
                (int)Math.Floor((u + 1d) * 0.5d * settings.faceCellResolution)));
            int cellV = Math.Max(0, Math.Min(settings.faceCellResolution - 1,
                (int)Math.Floor((v + 1d) * 0.5d * settings.faceCellResolution)));
            int surface = GetSurfaceHeight(face, cellU, cellV);
            return new VoxelAddress(bodyId, face, cellU, cellV, surface);
        }

        private static double Clamp01(double value)
        {
            if (value < 0d) return 0d;
            if (value > 1d) return 1d;
            return value;
        }
    }
}
