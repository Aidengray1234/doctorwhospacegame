using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Rendering;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Collision
{
    public sealed class VoxelCollisionWorld : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private StableCartesianVoxelGrid stableGrid;
        private int lastQueryBlocks;
        public int LastQueryBlocks { get { return lastQueryBlocks; } }

        public void Configure(VoxelUniverseWorld voxelWorld)
        {
            world = voxelWorld;
            if (stableGrid == null) stableGrid = FindObjectOfType<StableCartesianVoxelGrid>();
        }

        public void Configure(VoxelUniverseWorld voxelWorld, StableCartesianVoxelGrid grid)
        {
            world = voxelWorld;
            stableGrid = grid;
        }

        public Vector3 ResolveMotion(Vector3 position, Vector3 delta, float radius, float height,
            float stepHeight, out bool grounded)
        {
            if (stableGrid != null)
                return ResolveStableMotion(position, delta, radius, height, stepHeight, out grounded);
            return ResolveLegacyMotion(position, delta, radius, height, stepHeight, out grounded);
        }

        public bool CapsuleOverlapsCell(Vector3 position, float radius, float height, Int3 cell)
        {
            if (stableGrid == null || !BlockRegistry.IsSolid(stableGrid.GetBlock(cell))) return false;
            Vector3 up = (position - world.Center).normalized;
            Vector3 feet = position + up * radius;
            Vector3 middle = position + up * (height * 0.5f);
            Vector3 head = position + up * Mathf.Max(radius, height - radius);
            Vector3 push;
            return TrySphereCellPenetration(feet, radius, cell, out push)
                || TrySphereCellPenetration(middle, radius, cell, out push)
                || TrySphereCellPenetration(head, radius, cell, out push);
        }

        public bool CapsuleOverlapsBlock(Vector3 position, float radius, float height,
            VoxelAddress address)
        {
            if (stableGrid != null)
                return CapsuleOverlapsCell(position, radius, height,
                    stableGrid.CellForAddress(address));
            if (world == null || !BlockRegistry.IsSolid(world.GetBlock(address))) return false;
            Vector3 up = (position - world.Center).normalized;
            Vector3 feet = position + up * radius;
            Vector3 head = position + up * Mathf.Max(radius, height - radius);
            return SphereOverlapsLegacyBlock(feet, radius, address)
                || SphereOverlapsLegacyBlock(head, radius, address);
        }

        private Vector3 ResolveStableMotion(Vector3 position, Vector3 delta, float radius,
            float height, float stepHeight, out bool grounded)
        {
            grounded = false;
            if (world == null) return position + delta;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.24f));
            Vector3 step = delta / steps;
            Vector3 current = position;
            for (int i = 0; i < steps; i++)
            {
                Vector3 up = (current - world.Center).normalized;
                Vector3 resolved = ResolveStableCapsule(current + step, radius, height, ref grounded);
                Vector3 moved = resolved - current;
                Vector3 requested = Vector3.ProjectOnPlane(step, up);
                Vector3 actual = Vector3.ProjectOnPlane(moved, up);
                bool blocked = requested.sqrMagnitude > 0.0001f
                    && actual.magnitude < requested.magnitude * 0.45f;
                if (blocked && stepHeight > 0f)
                {
                    bool stepGrounded = false;
                    Vector3 raised = ResolveStableCapsule(current + up * stepHeight + step,
                        radius, height, ref stepGrounded);
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

        private Vector3 ResolveStableCapsule(Vector3 position, float radius, float height,
            ref bool grounded)
        {
            Vector3 up = (position - world.Center).normalized;
            Vector3[] points =
            {
                position + up * radius,
                position + up * (height * 0.5f),
                position + up * Mathf.Max(radius, height - radius)
            };
            Vector3 total = Vector3.zero;
            lastQueryBlocks = 0;
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                Vector3 local = points[pointIndex] - world.Center + total;
                int minX = Mathf.FloorToInt(local.x - radius) - 1;
                int maxX = Mathf.FloorToInt(local.x + radius) + 1;
                int minY = Mathf.FloorToInt(local.y - radius) - 1;
                int maxY = Mathf.FloorToInt(local.y + radius) + 1;
                int minZ = Mathf.FloorToInt(local.z - radius) - 1;
                int maxZ = Mathf.FloorToInt(local.z + radius) + 1;
                for (int z = minZ; z <= maxZ; z++)
                for (int y = minY; y <= maxY; y++)
                for (int x = minX; x <= maxX; x++)
                {
                    Int3 cell = new Int3(x, y, z);
                    if (!BlockRegistry.IsSolid(stableGrid.GetBlock(cell))) continue;
                    lastQueryBlocks++;
                    Vector3 push;
                    if (!TrySphereCellPenetration(points[pointIndex] + total,
                        radius, cell, out push)) continue;
                    total += push;
                    if (push.sqrMagnitude > 0.000001f
                        && Vector3.Dot(push.normalized, up) > 0.55f)
                        grounded = true;
                }
            }
            return position + total;
        }

        private bool TrySphereCellPenetration(Vector3 sphereCenter, float sphereRadius,
            Int3 cell, out Vector3 push)
        {
            push = Vector3.zero;
            Vector3 center = stableGrid.CellCenterWorld(cell);
            Vector3 local = sphereCenter - center;
            Vector3 closest = new Vector3(Mathf.Clamp(local.x, -0.5f, 0.5f),
                Mathf.Clamp(local.y, -0.5f, 0.5f), Mathf.Clamp(local.z, -0.5f, 0.5f));
            Vector3 separation = local - closest;
            float distanceSq = separation.sqrMagnitude;
            if (distanceSq >= sphereRadius * sphereRadius) return false;
            float distance = Mathf.Sqrt(distanceSq);
            Vector3 normal;
            if (distance > 0.0001f) normal = separation / distance;
            else
            {
                Vector3 distances = Vector3.one * 0.5f
                    - new Vector3(Mathf.Abs(local.x), Mathf.Abs(local.y), Mathf.Abs(local.z));
                if (distances.x <= distances.y && distances.x <= distances.z)
                    normal = new Vector3(Mathf.Sign(local.x == 0f ? 1f : local.x), 0f, 0f);
                else if (distances.y <= distances.z)
                    normal = new Vector3(0f, Mathf.Sign(local.y == 0f ? 1f : local.y), 0f);
                else normal = new Vector3(0f, 0f, Mathf.Sign(local.z == 0f ? 1f : local.z));
                distance = 0f;
            }
            push = normal * (sphereRadius - distance + 0.001f);
            return true;
        }

        private Vector3 ResolveLegacyMotion(Vector3 position, Vector3 delta, float radius,
            float height, float stepHeight, out bool grounded)
        {
            grounded = false;
            if (world == null || world.Settings == null) return position + delta;
            int steps = Mathf.Max(1, Mathf.CeilToInt(delta.magnitude / 0.28f));
            Vector3 step = delta / steps;
            Vector3 current = position;
            for (int i = 0; i < steps; i++)
            {
                Vector3 up = (current - world.Center).normalized;
                Vector3 resolved = ResolveLegacyCapsule(current + step, radius, height, ref grounded);
                Vector3 moved = resolved - current;
                Vector3 requested = Vector3.ProjectOnPlane(step, up);
                Vector3 actual = Vector3.ProjectOnPlane(moved, up);
                bool blocked = requested.sqrMagnitude > 0.0001f
                    && actual.magnitude < requested.magnitude * 0.45f;
                if (blocked && stepHeight > 0f)
                {
                    bool stepGrounded = false;
                    Vector3 raised = ResolveLegacyCapsule(current + up * stepHeight + step,
                        radius, height, ref stepGrounded);
                    if ((raised - current).sqrMagnitude > moved.sqrMagnitude)
                    { resolved = raised; grounded = grounded || stepGrounded; }
                }
                current = resolved;
            }
            return current;
        }

        private Vector3 ResolveLegacyCapsule(Vector3 position, float radius, float height,
            ref bool grounded)
        {
            Vector3 up = (position - world.Center).normalized;
            Vector3 feet = position + up * radius;
            Vector3 middle = position + up * (height * 0.5f);
            Vector3 head = position + up * Mathf.Max(radius, height - radius);
            VoxelAddress center = world.GetAddress(middle);
            lastQueryBlocks = 0;
            Vector3 total = Vector3.zero;
            for (int dr = -2; dr <= 2; dr++)
            for (int dv = -2; dv <= 2; dv++)
            for (int du = -2; du <= 2; du++)
            {
                VoxelAddress address = CubeSphereMapper.Canonicalize(new VoxelAddress(center.bodyId,
                    center.face, center.u + du, center.v + dv, center.radial + dr),
                    world.Settings.faceCellResolution);
                if (!BlockRegistry.IsSolid(world.GetBlock(address))) continue;
                lastQueryBlocks++;
                Vector3 push;
                if (TrySphereLegacyBlockPenetration(feet + total, radius, address, out push))
                { total += push; if (Vector3.Dot(push.normalized, up) > 0.55f) grounded = true; }
                if (TrySphereLegacyBlockPenetration(middle + total, radius, address, out push)) total += push;
                if (TrySphereLegacyBlockPenetration(head + total, radius, address, out push)) total += push;
            }
            return position + total;
        }

        private bool SphereOverlapsLegacyBlock(Vector3 center, float radius, VoxelAddress address)
        {
            Vector3 push;
            return TrySphereLegacyBlockPenetration(center, radius, address, out push);
        }

        private bool TrySphereLegacyBlockPenetration(Vector3 sphereCenter, float sphereRadius,
            VoxelAddress address, out Vector3 push)
        {
            push = Vector3.zero;
            VoxelBlockFrame frame = world.GetBlockFrame(address);
            Vector3 offset = sphereCenter - frame.center;
            Vector3 local = new Vector3(Vector3.Dot(offset, frame.east),
                Vector3.Dot(offset, frame.radial), Vector3.Dot(offset, frame.north));
            Vector3 half = new Vector3(frame.halfEast, frame.halfRadial, frame.halfNorth);
            Vector3 closest = new Vector3(Mathf.Clamp(local.x, -half.x, half.x),
                Mathf.Clamp(local.y, -half.y, half.y), Mathf.Clamp(local.z, -half.z, half.z));
            Vector3 separation = local - closest;
            float distanceSq = separation.sqrMagnitude;
            if (distanceSq >= sphereRadius * sphereRadius) return false;
            float distance = Mathf.Sqrt(distanceSq);
            Vector3 localNormal = distance > 0.0001f ? separation / distance : Vector3.up;
            push = (frame.east * localNormal.x + frame.radial * localNormal.y
                + frame.north * localNormal.z) * (sphereRadius - distance + 0.001f);
            return true;
        }
    }
}
