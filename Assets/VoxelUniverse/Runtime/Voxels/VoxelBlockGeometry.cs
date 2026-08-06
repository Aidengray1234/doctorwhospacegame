using DoctorWho.VoxelUniverse.Core;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Voxels
{
    /// <summary>
    /// Exact radial cell geometry. Adjacent cells use the same cube-sphere grid points,
    /// so their borders remain watertight even when the local tangent basis changes or
    /// a cell touches one of the six cube-face seams.
    /// </summary>
    public struct VoxelBlockFrame
    {
        public VoxelAddress address;
        public Vector3 center;
        public Vector3 east;
        public Vector3 north;
        public Vector3 radial;
        public float halfEast;
        public float halfNorth;
        public float halfRadial;

        // Corner order is 00, 01, 11, 10 around the cell.
        public Vector3 inner00;
        public Vector3 inner01;
        public Vector3 inner11;
        public Vector3 inner10;
        public Vector3 outer00;
        public Vector3 outer01;
        public Vector3 outer11;
        public Vector3 outer10;

        public Vector3 GetCorner(int index)
        {
            switch (index)
            {
                case 0: return inner00;
                case 1: return inner10;
                case 2: return inner11;
                case 3: return inner01;
                case 4: return outer00;
                case 5: return outer10;
                case 6: return outer11;
                case 7: return outer01;
                default: return center;
            }
        }
    }

    public static class VoxelBlockGeometry
    {
        public static VoxelBlockFrame Calculate(VoxelAddress raw, VoxelUniverseSettings settings)
        {
            VoxelAddress address = CubeSphereMapper.Canonicalize(raw, settings.faceCellResolution);
            int resolution = settings.faceCellResolution;

            float innerRadius = Mathf.Max(0.01f, settings.groundRadius + address.radial);
            float outerRadius = Mathf.Max(innerRadius + 0.001f, innerRadius + 1f);

            Vector3 d00 = CubeSphereMapper.GridPointDirection(
                address.face, address.u, address.v, resolution).ToVector3();
            Vector3 d01 = CubeSphereMapper.GridPointDirection(
                address.face, address.u, address.v + 1, resolution).ToVector3();
            Vector3 d11 = CubeSphereMapper.GridPointDirection(
                address.face, address.u + 1, address.v + 1, resolution).ToVector3();
            Vector3 d10 = CubeSphereMapper.GridPointDirection(
                address.face, address.u + 1, address.v, resolution).ToVector3();

            Vector3 i00 = d00 * innerRadius;
            Vector3 i01 = d01 * innerRadius;
            Vector3 i11 = d11 * innerRadius;
            Vector3 i10 = d10 * innerRadius;
            Vector3 o00 = d00 * outerRadius;
            Vector3 o01 = d01 * outerRadius;
            Vector3 o11 = d11 * outerRadius;
            Vector3 o10 = d10 * outerRadius;

            Vector3 center = (i00 + i01 + i11 + i10 + o00 + o01 + o11 + o10) * 0.125f;
            Vector3 radial = center.sqrMagnitude > 0.000001f ? center.normalized : d00.normalized;

            FaceBasis tangent = CubeSphereMapper.GetCellTangentBasis(
                address.face, address.u, address.v, resolution);
            Vector3 east = tangent.east.ToVector3().normalized;
            Vector3 north = tangent.north.ToVector3().normalized;

            // Keep an orthonormal, right-handed collision/interaction frame.
            east = Vector3.ProjectOnPlane(east, radial).normalized;
            north = Vector3.Cross(radial, east).normalized;
            if (Vector3.Dot(north, tangent.north.ToVector3()) < 0f)
                north = -north;

            float halfEast = 0f;
            float halfNorth = 0f;
            float halfRadial = 0f;
            AccumulateExtents(i00, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(i01, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(i11, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(i10, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(o00, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(o01, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(o11, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);
            AccumulateExtents(o10, center, east, north, radial, ref halfEast, ref halfNorth, ref halfRadial);

            // A tiny collision expansion prevents falling through numerical cracks while
            // rendering still uses the exact shared corners below.
            const float collisionPadding = 0.006f;
            halfEast += collisionPadding;
            halfNorth += collisionPadding;
            halfRadial += collisionPadding;

            return new VoxelBlockFrame
            {
                address = address,
                center = center,
                east = east,
                north = north,
                radial = radial,
                halfEast = halfEast,
                halfNorth = halfNorth,
                halfRadial = halfRadial,
                inner00 = i00,
                inner01 = i01,
                inner11 = i11,
                inner10 = i10,
                outer00 = o00,
                outer01 = o01,
                outer11 = o11,
                outer10 = o10
            };
        }

        private static void AccumulateExtents(
            Vector3 point,
            Vector3 center,
            Vector3 east,
            Vector3 north,
            Vector3 radial,
            ref float halfEast,
            ref float halfNorth,
            ref float halfRadial)
        {
            Vector3 offset = point - center;
            halfEast = Mathf.Max(halfEast, Mathf.Abs(Vector3.Dot(offset, east)));
            halfNorth = Mathf.Max(halfNorth, Mathf.Abs(Vector3.Dot(offset, north)));
            halfRadial = Mathf.Max(halfRadial, Mathf.Abs(Vector3.Dot(offset, radial)));
        }
    }
}
