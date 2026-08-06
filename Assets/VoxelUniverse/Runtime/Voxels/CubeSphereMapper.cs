using System;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public static class CubeSphereMapper
    {
        public static FaceBasis GetFaceBasis(CubeSphereFace face)
        {
            switch (face)
            {
                case CubeSphereFace.PositiveX: return new FaceBasis(Double3.Right, new Double3(0d, 0d, -1d), Double3.Up);
                case CubeSphereFace.NegativeX: return new FaceBasis(-Double3.Right, Double3.Forward, Double3.Up);
                case CubeSphereFace.PositiveY: return new FaceBasis(Double3.Up, Double3.Right, new Double3(0d, 0d, -1d));
                case CubeSphereFace.NegativeY: return new FaceBasis(-Double3.Up, Double3.Right, Double3.Forward);
                case CubeSphereFace.PositiveZ: return new FaceBasis(Double3.Forward, Double3.Right, Double3.Up);
                case CubeSphereFace.NegativeZ: return new FaceBasis(-Double3.Forward, -Double3.Right, Double3.Up);
                default: throw new ArgumentOutOfRangeException("face");
            }
        }

        public static Double3 FaceUvToDirection(CubeSphereFace face, double u, double v)
        {
            FaceBasis basis = GetFaceBasis(face);
            return (basis.normal + basis.east * u + basis.north * v).Normalized;
        }

        public static void DirectionToFaceUv(Double3 direction, out CubeSphereFace face, out double u, out double v)
        {
            if (direction.SqrMagnitude <= 1e-24d) throw new ArgumentException("Direction cannot be zero.", "direction");
            Double3 normalized = direction.Normalized;
            double ax = Math.Abs(normalized.x);
            double ay = Math.Abs(normalized.y);
            double az = Math.Abs(normalized.z);
            if (ax >= ay && ax >= az) face = normalized.x >= 0d ? CubeSphereFace.PositiveX : CubeSphereFace.NegativeX;
            else if (ay >= az) face = normalized.y >= 0d ? CubeSphereFace.PositiveY : CubeSphereFace.NegativeY;
            else face = normalized.z >= 0d ? CubeSphereFace.PositiveZ : CubeSphereFace.NegativeZ;
            FaceBasis basis = GetFaceBasis(face);
            double denominator = Double3.Dot(normalized, basis.normal);
            u = Double3.Dot(normalized, basis.east) / denominator;
            v = Double3.Dot(normalized, basis.north) / denominator;
        }

        public static Double3 CellCenterDirection(CubeSphereFace face, int u, int v, int faceCellResolution)
        {
            ValidateResolution(faceCellResolution);
            double normalizedU = ((u + 0.5d) / faceCellResolution) * 2d - 1d;
            double normalizedV = ((v + 0.5d) / faceCellResolution) * 2d - 1d;
            return FaceUvToDirection(face, normalizedU, normalizedV);
        }

        public static Double3 GridPointDirection(CubeSphereFace face, int u, int v, int faceCellResolution)
        {
            ValidateResolution(faceCellResolution);
            double normalizedU = ((double)u / faceCellResolution) * 2d - 1d;
            double normalizedV = ((double)v / faceCellResolution) * 2d - 1d;
            return FaceUvToDirection(face, normalizedU, normalizedV);
        }

        public static VoxelAddress PositionToAddress(CelestialBodyId bodyId, Double3 bodyLocalPosition, double groundRadius, int faceCellResolution)
        {
            ValidateResolution(faceCellResolution);
            double radius = bodyLocalPosition.Magnitude;
            if (radius <= 1e-12d) throw new ArgumentException("Position cannot be at the body center.", "bodyLocalPosition");
            CubeSphereFace face;
            double u;
            double v;
            DirectionToFaceUv(bodyLocalPosition / radius, out face, out u, out v);
            int cellU = ClampCell((int)Math.Floor((u + 1d) * 0.5d * faceCellResolution), faceCellResolution);
            int cellV = ClampCell((int)Math.Floor((v + 1d) * 0.5d * faceCellResolution), faceCellResolution);
            int radial = (int)Math.Floor(radius - groundRadius);
            return new VoxelAddress(bodyId, face, cellU, cellV, radial);
        }

        public static Double3 AddressCenterToPosition(VoxelAddress address, double groundRadius, int faceCellResolution)
        {
            return CellCenterDirection(address.face, address.u, address.v, faceCellResolution) * (groundRadius + address.radial + 0.5d);
        }

        public static VoxelAddress Canonicalize(VoxelAddress address, int faceCellResolution)
        {
            ValidateResolution(faceCellResolution);
            if (address.u >= 0 && address.u < faceCellResolution && address.v >= 0 && address.v < faceCellResolution) return address;
            double normalizedU = ((address.u + 0.5d) / faceCellResolution) * 2d - 1d;
            double normalizedV = ((address.v + 0.5d) / faceCellResolution) * 2d - 1d;
            Double3 direction = FaceUvToDirection(address.face, normalizedU, normalizedV);
            CubeSphereFace canonicalFace;
            double canonicalU;
            double canonicalV;
            DirectionToFaceUv(direction, out canonicalFace, out canonicalU, out canonicalV);
            int cellU = ClampCell((int)Math.Floor((canonicalU + 1d) * 0.5d * faceCellResolution), faceCellResolution);
            int cellV = ClampCell((int)Math.Floor((canonicalV + 1d) * 0.5d * faceCellResolution), faceCellResolution);
            return new VoxelAddress(address.bodyId, canonicalFace, cellU, cellV, address.radial);
        }

        public static FaceBasis GetCellTangentBasis(CubeSphereFace face, int u, int v, int faceCellResolution)
        {
            Double3 center = CellCenterDirection(face, u, v, faceCellResolution);
            FaceBasis fixedBasis = GetFaceBasis(face);
            Double3 east = (fixedBasis.east - center * Double3.Dot(fixedBasis.east, center)).Normalized;
            Double3 north = Double3.Cross(center, east).Normalized;
            return new FaceBasis(center, east, north);
        }

        private static int ClampCell(int value, int resolution)
        {
            if (value < 0) return 0;
            if (value >= resolution) return resolution - 1;
            return value;
        }

        private static void ValidateResolution(int resolution)
        {
            if (resolution <= 0) throw new ArgumentOutOfRangeException("faceCellResolution");
        }
    }
}
