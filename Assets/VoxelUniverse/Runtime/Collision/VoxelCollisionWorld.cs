using DoctorWho.VoxelUniverse.Rendering;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Collision
{
    public sealed class VoxelCollisionWorld : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        private int lastQueryBlocks;
        public int LastQueryBlocks { get { return lastQueryBlocks; } }
        public void Configure(VoxelUniverseWorld voxelWorld) { world=voxelWorld; }

        public Vector3 ResolveMotion(Vector3 position,Vector3 delta,float radius,float height,
            float stepHeight,out bool grounded)
        {
            grounded=false;if(world==null||world.Settings==null)return position+delta;
            int steps=Mathf.Max(1,Mathf.CeilToInt(delta.magnitude/.28f));
            Vector3 step=delta/steps,current=position;
            for(int i=0;i<steps;i++)
            {
                Vector3 up=(current-world.Center).normalized;
                Vector3 resolved=ResolveCapsule(current+step,radius,height,ref grounded);
                Vector3 moved=resolved-current;
                Vector3 requested=Vector3.ProjectOnPlane(step,up);
                Vector3 actual=Vector3.ProjectOnPlane(moved,up);
                bool blocked=requested.sqrMagnitude>.0001f && actual.magnitude<requested.magnitude*.45f;
                if(blocked&&stepHeight>0f)
                {
                    bool stepGrounded=false;
                    Vector3 raised=ResolveCapsule(current+up*stepHeight+step,radius,height,ref stepGrounded);
                    if((raised-current).sqrMagnitude>moved.sqrMagnitude)
                    { resolved=raised;grounded=grounded||stepGrounded; }
                }
                current=resolved;
            }
            return current;
        }

        public bool CapsuleOverlapsBlock(Vector3 position,float radius,float height,VoxelAddress address)
        {
            if(world==null||!BlockRegistry.IsSolid(world.GetBlock(address)))return false;
            Vector3 up=(position-world.Center).normalized;
            Vector3 feet=position+up*radius,head=position+up*Mathf.Max(radius,height-radius);
            return SphereOverlapsBlock(feet,radius,address)||SphereOverlapsBlock(head,radius,address);
        }

        private Vector3 ResolveCapsule(Vector3 position,float radius,float height,ref bool grounded)
        {
            Vector3 up=(position-world.Center).normalized;
            Vector3 feet=position+up*radius,middle=position+up*(height*.5f),
                head=position+up*Mathf.Max(radius,height-radius);
            VoxelAddress center=world.GetAddress(middle);lastQueryBlocks=0;Vector3 total=Vector3.zero;
            for(int dr=-2;dr<=2;dr++)for(int dv=-2;dv<=2;dv++)for(int du=-2;du<=2;du++)
            {
                VoxelAddress a=CubeSphereMapper.Canonicalize(new VoxelAddress(center.bodyId,center.face,
                    center.u+du,center.v+dv,center.radial+dr),world.Settings.faceCellResolution);
                if(!BlockRegistry.IsSolid(world.GetBlock(a)))continue;lastQueryBlocks++;
                Vector3 push;
                if(TrySphereBlockPenetration(feet+total,radius,a,out push))
                { total+=push;if(push.sqrMagnitude>.000001f&&Vector3.Dot(push.normalized,up)>.55f)grounded=true; }
                if(TrySphereBlockPenetration(middle+total,radius,a,out push))total+=push;
                if(TrySphereBlockPenetration(head+total,radius,a,out push))total+=push;
            }
            return position+total;
        }

        private bool SphereOverlapsBlock(Vector3 center,float radius,VoxelAddress a)
        { Vector3 push;return TrySphereBlockPenetration(center,radius,a,out push); }

        private bool TrySphereBlockPenetration(Vector3 sphereCenter,float sphereRadius,
            VoxelAddress address,out Vector3 push)
        {
            push=Vector3.zero;VoxelBlockFrame f=world.GetBlockFrame(address);
            Vector3 offset=sphereCenter-f.center;
            Vector3 local=new Vector3(Vector3.Dot(offset,f.east),Vector3.Dot(offset,f.radial),
                Vector3.Dot(offset,f.north));
            Vector3 half=new Vector3(f.halfEast,f.halfRadial,f.halfNorth);
            Vector3 closest=new Vector3(Mathf.Clamp(local.x,-half.x,half.x),
                Mathf.Clamp(local.y,-half.y,half.y),Mathf.Clamp(local.z,-half.z,half.z));
            Vector3 separation=local-closest;float distanceSq=separation.sqrMagnitude;
            if(distanceSq>=sphereRadius*sphereRadius)return false;
            Vector3 localNormal;float distance=Mathf.Sqrt(distanceSq);
            if(distance>.0001f)localNormal=separation/distance;
            else
            {
                Vector3 distances=half-new Vector3(Mathf.Abs(local.x),Mathf.Abs(local.y),Mathf.Abs(local.z));
                if(distances.x<=distances.y&&distances.x<=distances.z)
                    localNormal=new Vector3(Mathf.Sign(local.x==0f?1f:local.x),0,0);
                else if(distances.y<=distances.z)
                    localNormal=new Vector3(0,Mathf.Sign(local.y==0f?1f:local.y),0);
                else localNormal=new Vector3(0,0,Mathf.Sign(local.z==0f?1f:local.z));
                distance=0f;
            }
            float penetration=sphereRadius-distance+.001f;
            push=(f.east*localNormal.x+f.radial*localNormal.y+f.north*localNormal.z)*penetration;
            return true;
        }
    }
}
