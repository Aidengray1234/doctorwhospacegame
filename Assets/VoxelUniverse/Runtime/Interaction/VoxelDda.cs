using System;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Rendering;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Interaction
{
    public enum VoxelHitFace
    {
        Outer,
        Inner,
        East,
        West,
        North,
        South
    }

    public struct VoxelRayHit
    {
        public bool valid;
        public float distance;
        public Vector3 point;
        public VoxelAddress address;
        public VoxelAddress adjacent;
        public BlockState block;
        public VoxelHitFace face;
    }

    public static class VoxelDda
    {
        private const double Epsilon = 1e-5d;

        public static bool Cast(VoxelUniverseWorld world, Ray ray, float maxDistance, out VoxelRayHit hit)
        {
            hit = new VoxelRayHit();
            if (world == null || world.Settings == null) return false;

            Double3 origin = Double3.FromVector3(ray.origin - world.Center);
            Double3 direction = Double3.FromVector3(ray.direction).Normalized;
            double t = 0d;
            bool hasPrevious = false;
            VoxelAddress previous = default(VoxelAddress);
            VoxelHitFace enteredFace = VoxelHitFace.Outer;

            for (int iteration = 0; iteration < 512 && t <= maxDistance; iteration++)
            {
                Double3 samplePoint = origin + direction * (t + Epsilon);
                if (samplePoint.Magnitude <= 1e-8d) return false;

                VoxelAddress address = CubeSphereMapper.PositionToAddress(
                    world.BodyId,
                    samplePoint,
                    world.Settings.groundRadius,
                    world.Settings.faceCellResolution);
                BlockState block = world.GetBlock(address);
                BlockDefinition definition = BlockRegistry.Get(block.BlockId);

                if (!block.IsAir && !definition.liquid)
                {
                    hit.valid = true;
                    hit.distance = (float)t;
                    hit.point = ray.origin + ray.direction * (float)t;
                    hit.address = address;
                    hit.adjacent = hasPrevious ? previous : address;
                    hit.block = block;
                    hit.face = enteredFace;
                    return true;
                }

                double nextT;
                VoxelHitFace crossedFace;
                if (!TryFindNextBoundary(world, origin, direction, address, t, out nextT, out crossedFace))
                    return false;

                previous = address;
                hasPrevious = true;
                enteredFace = Opposite(crossedFace);
                t = nextT + Epsilon;
            }

            return false;
        }

        private static bool TryFindNextBoundary(
            VoxelUniverseWorld world,
            Double3 origin,
            Double3 direction,
            VoxelAddress address,
            double currentT,
            out double nextT,
            out VoxelHitFace crossedFace)
        {
            nextT = double.PositiveInfinity;
            crossedFace = VoxelHitFace.Outer;
            int resolution = world.Settings.faceCellResolution;
            FaceBasis basis = CubeSphereMapper.GetFaceBasis(address.face);

            double u0 = ((double)address.u / resolution) * 2d - 1d;
            double u1 = ((double)(address.u + 1) / resolution) * 2d - 1d;
            double v0 = ((double)address.v / resolution) * 2d - 1d;
            double v1 = ((double)(address.v + 1) / resolution) * 2d - 1d;
            TryPlaneBoundary(origin, direction, basis.east - basis.normal * u0, currentT,
                VoxelHitFace.West, ref nextT, ref crossedFace);
            TryPlaneBoundary(origin, direction, basis.east - basis.normal * u1, currentT,
                VoxelHitFace.East, ref nextT, ref crossedFace);
            TryPlaneBoundary(origin, direction, basis.north - basis.normal * v0, currentT,
                VoxelHitFace.South, ref nextT, ref crossedFace);
            TryPlaneBoundary(origin, direction, basis.north - basis.normal * v1, currentT,
                VoxelHitFace.North, ref nextT, ref crossedFace);

            double innerRadius = world.Settings.groundRadius + address.radial;
            double outerRadius = innerRadius + 1d;
            TrySphereBoundary(origin, direction, innerRadius, currentT,
                VoxelHitFace.Inner, ref nextT, ref crossedFace);
            TrySphereBoundary(origin, direction, outerRadius, currentT,
                VoxelHitFace.Outer, ref nextT, ref crossedFace);

            return !double.IsInfinity(nextT);
        }

        private static void TryPlaneBoundary(
            Double3 origin,
            Double3 direction,
            Double3 planeNormal,
            double currentT,
            VoxelHitFace face,
            ref double bestT,
            ref VoxelHitFace bestFace)
        {
            double denominator = Double3.Dot(direction, planeNormal);
            if (Math.Abs(denominator) < 1e-12d) return;
            double candidate = -Double3.Dot(origin, planeNormal) / denominator;
            if (candidate <= currentT + Epsilon || candidate >= bestT) return;
            bestT = candidate;
            bestFace = face;
        }

        private static void TrySphereBoundary(
            Double3 origin,
            Double3 direction,
            double radius,
            double currentT,
            VoxelHitFace face,
            ref double bestT,
            ref VoxelHitFace bestFace)
        {
            if (radius <= 0d) return;
            double b = 2d * Double3.Dot(origin, direction);
            double c = origin.SqrMagnitude - radius * radius;
            double discriminant = b * b - 4d * c;
            if (discriminant < 0d) return;
            double root = Math.Sqrt(discriminant);
            double t0 = (-b - root) * 0.5d;
            double t1 = (-b + root) * 0.5d;
            if (t0 > currentT + Epsilon && t0 < bestT)
            {
                bestT = t0;
                bestFace = face;
            }
            if (t1 > currentT + Epsilon && t1 < bestT)
            {
                bestT = t1;
                bestFace = face;
            }
        }

        private static VoxelHitFace Opposite(VoxelHitFace face)
        {
            switch (face)
            {
                case VoxelHitFace.Outer: return VoxelHitFace.Inner;
                case VoxelHitFace.Inner: return VoxelHitFace.Outer;
                case VoxelHitFace.East: return VoxelHitFace.West;
                case VoxelHitFace.West: return VoxelHitFace.East;
                case VoxelHitFace.North: return VoxelHitFace.South;
                default: return VoxelHitFace.North;
            }
        }
    }
}
