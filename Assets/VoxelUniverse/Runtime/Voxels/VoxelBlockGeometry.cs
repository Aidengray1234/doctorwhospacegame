using DoctorWho.VoxelUniverse.Core;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public struct VoxelBlockFrame
    {
        public VoxelAddress address;
        public Vector3 center, east, north, radial;
        public float halfEast, halfNorth, halfRadial;
    }

    public static class VoxelBlockGeometry
    {
        public static VoxelBlockFrame Calculate(VoxelAddress raw, VoxelUniverseSettings settings)
        {
            VoxelAddress a=CubeSphereMapper.Canonicalize(raw,settings.faceCellResolution);
            Vector3 center=CenterOf(a,settings);
            FaceBasis b=CubeSphereMapper.GetCellTangentBasis(a.face,a.u,a.v,settings.faceCellResolution);
            Vector3 east=b.east.ToVector3().normalized;
            Vector3 north=b.north.ToVector3().normalized;
            Vector3 radial=b.normal.ToVector3().normalized;
            float halfEast=(Vector3.Distance(center,CenterOf(new VoxelAddress(a.bodyId,a.face,a.u+1,a.v,a.radial),settings))
                +Vector3.Distance(center,CenterOf(new VoxelAddress(a.bodyId,a.face,a.u-1,a.v,a.radial),settings)))
                *0.25f*settings.tangentialBlockFill;
            float halfNorth=(Vector3.Distance(center,CenterOf(new VoxelAddress(a.bodyId,a.face,a.u,a.v+1,a.radial),settings))
                +Vector3.Distance(center,CenterOf(new VoxelAddress(a.bodyId,a.face,a.u,a.v-1,a.radial),settings)))
                *0.25f*settings.tangentialBlockFill;
            return new VoxelBlockFrame
            {
                address=a,center=center,east=east,north=north,radial=radial,
                halfEast=Mathf.Clamp(halfEast,0.38f,0.72f),
                halfNorth=Mathf.Clamp(halfNorth,0.38f,0.72f),
                halfRadial=settings.radialBlockHalfSize
            };
        }

        private static Vector3 CenterOf(VoxelAddress raw,VoxelUniverseSettings settings)
        {
            VoxelAddress a=CubeSphereMapper.Canonicalize(raw,settings.faceCellResolution);
            return CubeSphereMapper.AddressCenterToPosition(
                a,settings.groundRadius,settings.faceCellResolution).ToVector3();
        }
    }
}
