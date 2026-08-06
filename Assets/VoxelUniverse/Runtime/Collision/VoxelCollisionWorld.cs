using DoctorWho.VoxelUniverse.Core;
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

        public void Configure(VoxelUniverseWorld voxelWorld)
        {
            world = voxelWorld;
        }

        public Vector3 ResolveMotion(
            Vector3 position,
            Vector3 delta,
            float radius,
            float height,
            float stepHeight,
            out bool grounded)
        {
            grounded = false;
            if (world == null || world.Settings == null) return position + delta;

            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.32f));
            Vector3 step = delta / steps;
            Vector3 current = position;

            for (int i = 0; i < steps; i++)
            {
                Vector3 up = (current - world.Center).normalized;
                Vector3 candidate = current + step;
                Vector3 resolved = ResolveCapsule(candidate, radius, height, ref grounded);
                Vector3 moved = resolved - current;

                Vector3 requestedHorizontal = Vector3.ProjectOnPlane(step, up);
                Vector3 actualHorizontal = Vector3.ProjectOnPlane(moved, up);
                bool blockedHorizontally = requestedHorizontal.sqrMagnitude > 0.0001f
                    && actualHorizontal.magnitude < requestedHorizontal.magnitude * 0.45f;

                if (blockedHorizontally && stepHeight > 0f)
                {
                    bool stepGrounded = false;
                    Vector3 raised = ResolveCapsule(current + up * stepHeight + step, radius, height, ref stepGrounded);
                    if ((raised - current).sqrMagnitude > moved.sqrMagnitude)
                    {
                        resolved = raised;
                        grounded = grounded || stepGrounded;
                    }
                }

                current = resolved;
            }

            return current;
        }

        public bool CapsuleOverlapsBlock(Vector3 position, float radius, float height, VoxelAddress address)
        {
            if (world == null) return false;
            BlockState state = world.GetBlock(address);
            if (!BlockRegistry.IsSolid(state)) return false;
            Vector3 up = (position - world.Center).normalized;
            Vector3 feet = position + up * radius;
            Vector3 head = position + up * Mathf.Max(radius, height - radius);
            return SphereOverlapsBlock(feet, radius, address) || SphereOverlapsBlock(head, radius, address);
        }

        private Vector3 ResolveCapsule(Vector3 position, float radius, float height, ref bool grounded)
        {
            Vector3 up = (position - world.Center).normalized;
            Vector3 feet = position + up * radius;
            Vector3 middle = position + up * (height * 0.5f);
            Vector3 head = position + up * Mathf.Max(radius, height - radius);

            VoxelAddress centerAddress = world.GetAddress(middle);
            lastQueryBlocks = 0;
            Vector3 totalPush = Vector3.zero;

            for (int dr = -2; dr <= 2; dr++)
            {
                for (int dv = -2; dv <= 2; dv++)
                {
                    for (int du = -2; du <= 2; du++)
                    {
                        VoxelAddress raw = new VoxelAddress(
                            centerAddress.bodyId,
                            centerAddress.face,
                            centerAddress.u + du,
                            centerAddress.v + dv,
                            centerAddress.radial + dr);
                        VoxelAddress address = CubeSphereMapper.Canonicalize(raw, world.Settings.faceCellResolution);
                        BlockState state = world.GetBlock(address);
                        if (!BlockRegistry.IsSolid(state)) continue;
                        lastQueryBlocks++;

                        Vector3 push;
                        if (TrySphereBlockPenetration(feet + totalPush, radius, address, out push))
                        {
                            totalPush += push;
                            if (Vector3.Dot(push.normalized, up) > 0.55f) grounded = true;
                        }
                        if (TrySphereBlockPenetration(middle + totalPush, radius, address, out push))
                            totalPush += push;
                        if (TrySphereBlockPenetration(head + totalPush, radius, address, out push))
                            totalPush += push;
                    }
                }
            }

            return position + totalPush;
        }

        private bool SphereOverlapsBlock(Vector3 sphereCenter, float radius, VoxelAddress address)
        {
            Vector3 push;
            return TrySphereBlockPenetration(sphereCenter, radius, address, out push);
        }

        private bool TrySphereBlockPenetration(
            Vector3 sphereCenter,
            float sphereRadius,
            VoxelAddress address,
            out Vector3 push)
        {
            push = Vector3.zero;
            Vector3 center = world.GetBlockCenter(address);
            FaceBasis basis = world.GetBlockBasis(address);
            Vector3 east = basis.east.ToVector3().normalized;
            Vector3 north = basis.north.ToVector3().normalized;
            Vector3 radial = basis.normal.ToVector3().normalized;
            Vector3 offset = sphereCenter - center;

            Vector3 local = new Vector3(
                Vector3.Dot(offset, east),
                Vector3.Dot(offset, radial),
                Vector3.Dot(offset, north));

            Vector3 half = new Vector3(0.53f, 0.53f, 0.53f);
            Vector3 closest = new Vector3(
                Mathf.Clamp(local.x, -half.x, half.x),
                Mathf.Clamp(local.y, -half.y, half.y),
                Mathf.Clamp(local.z, -half.z, half.z));
            Vector3 separation = local - closest;
            float distanceSq = separation.sqrMagnitude;
            if (distanceSq >= sphereRadius * sphereRadius) return false;

            Vector3 localNormal;
            float distance = Mathf.Sqrt(distanceSq);
            if (distance > 0.0001f)
            {
                localNormal = separation / distance;
            }
            else
            {
                Vector3 distances = half - new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                if (distances.x <= distances.y && distances.x <= distances.z)
                    localNormal = new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x), 0f, 0f);
                else if (distances.y <= distances.z)
                    localNormal = new Vector3(0f, Mathf.Sign(local.y == 0f ? 1f : local.y), 0f);
                else
                    localNormal = new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z));
                distance = 0f;
            }

            float penetration = sphereRadius - distance + 0.001f;
            push = (east * localNormal.x + radial * localNormal.y + north * localNormal.z) * penetration;
            return true;
        }
    }
}
