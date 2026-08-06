using DoctorWho.VoxelUniverse.Collision;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Input;
using DoctorWho.VoxelUniverse.Inventory;
using DoctorWho.VoxelUniverse.Player;
using DoctorWho.VoxelUniverse.Rendering;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Interaction
{
    public sealed class VoxelInteractor : MonoBehaviour
    {
        [SerializeField] private VoxelUniverseWorld world;
        [SerializeField] private VoxelPlayerController player;
        [SerializeField] private VoxelInventory inventory;
        [SerializeField] private Camera playerCamera;
        [SerializeField] private Material outlineMaterial;
        [SerializeField] private StableCartesianVoxelGrid stableGrid;
        [SerializeField] private VoxelCollisionWorld collisionWorld;

        private LineRenderer outline;
        private VoxelRayHit currentLegacyHit;
        private StableGridRayHit currentStableHit;
        private bool stableHit;

        public void Configure(VoxelUniverseWorld voxelWorld, VoxelPlayerController playerController,
            VoxelInventory playerInventory, Camera camera, Material selectionMaterial)
        {
            world = voxelWorld;
            player = playerController;
            inventory = playerInventory;
            playerCamera = camera;
            outlineMaterial = selectionMaterial;
            if (stableGrid == null) stableGrid = FindObjectOfType<StableCartesianVoxelGrid>();
            if (collisionWorld == null) collisionWorld = FindObjectOfType<VoxelCollisionWorld>();
            EnsureOutline();
            outline.sharedMaterial = outlineMaterial;
        }

        public void ConfigureStable(StableCartesianVoxelGrid grid, VoxelCollisionWorld collision)
        {
            stableGrid = grid;
            collisionWorld = collision;
        }

        private void Awake()
        {
            EnsureOutline();
            if (stableGrid == null) stableGrid = FindObjectOfType<StableCartesianVoxelGrid>();
            if (collisionWorld == null) collisionWorld = FindObjectOfType<VoxelCollisionWorld>();
        }

        private void Update()
        {
            if (world == null || playerCamera == null || inventory == null) return;
            if (Cursor.lockState != CursorLockMode.Locked || inventory.InventoryOpen)
            {
                SetOutlineVisible(false);
                return;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            stableHit = stableGrid != null && stableGrid.TryRaycast(ray,
                world.Settings.interactionReach, out currentStableHit);
            bool hit = stableHit;
            if (!hit) hit = VoxelDda.Cast(world, ray, world.Settings.interactionReach,
                out currentLegacyHit);
            SetOutlineVisible(hit);
            if (!hit) return;
            if (stableHit) UpdateStableOutline(currentStableHit.cell);
            else UpdateLegacyOutline(currentLegacyHit.address);

            if (VoxelInput.PrimaryPressed) Mine();
            if (VoxelInput.SecondaryPressed) Place();
        }

        private void Mine()
        {
            BlockState block = stableHit ? currentStableHit.block : currentLegacyHit.block;
            BlockDefinition definition = BlockRegistry.Get(block.BlockId);
            if (block.BlockId == BlockRegistry.Bedrock || definition.liquid) return;
            if (stableHit) stableGrid.SetBlock(currentStableHit.cell, BlockState.Air);
            else world.SetBlock(currentLegacyHit.address, BlockState.Air);
            inventory.Add(block.BlockId, 1);
        }

        private void Place()
        {
            BlockState selected = inventory.SelectedBlock;
            if (selected.IsAir) return;
            if (stableHit)
            {
                Int3 targetCell = currentStableHit.adjacentCell;
                BlockState existing = stableGrid.GetBlock(targetCell);
                if (!BlockRegistry.IsReplaceable(existing)) return;
                if (player != null && collisionWorld != null
                    && collisionWorld.CapsuleOverlapsCell(player.transform.position,
                        player.CapsuleRadius, player.CapsuleHeight, targetCell)) return;
                byte orientation = DetermineStableOrientation(currentStableHit.adjacentAddress,
                    currentStableHit.normal);
                if (!inventory.ConsumeSelected(1)) return;
                stableGrid.SetBlock(targetCell, selected.WithOrientation(orientation));
                return;
            }

            VoxelAddress target = currentLegacyHit.adjacent;
            BlockState old = world.GetBlock(target);
            if (!BlockRegistry.IsReplaceable(old)) return;
            if (player != null && player.WouldOverlap(target)) return;
            if (!inventory.ConsumeSelected(1)) return;
            world.SetBlock(target, selected.WithOrientation(
                DetermineLegacyOrientation(currentLegacyHit.face)));
        }

        private byte DetermineStableOrientation(VoxelAddress address, Vector3 normal)
        {
            Vector3 radial = (stableGrid.CellCenterWorld(currentStableHit.adjacentCell)
                - world.Center).normalized;
            FaceBasis basis = world.GetBlockBasis(address);
            Vector3 east = basis.east.ToVector3();
            Vector3 north = basis.north.ToVector3();
            float radialDot = Vector3.Dot(normal, radial);
            float eastDot = Vector3.Dot(normal, east);
            float northDot = Vector3.Dot(normal, north);
            float ar = Mathf.Abs(radialDot);
            float ae = Mathf.Abs(eastDot);
            float an = Mathf.Abs(northDot);
            if (ar >= ae && ar >= an) return radialDot >= 0f ? (byte)0 : (byte)1;
            if (ae >= an) return eastDot >= 0f ? (byte)2 : (byte)3;
            return northDot >= 0f ? (byte)4 : (byte)5;
        }

        private static byte DetermineLegacyOrientation(VoxelHitFace face)
        {
            switch (face)
            {
                case VoxelHitFace.Outer: return 0;
                case VoxelHitFace.Inner: return 1;
                case VoxelHitFace.East: return 2;
                case VoxelHitFace.West: return 3;
                case VoxelHitFace.North: return 4;
                default: return 5;
            }
        }

        private void EnsureOutline()
        {
            if (outline != null) return;
            Transform found = transform.Find("Voxel Selection Outline");
            GameObject outlineObject = found != null ? found.gameObject
                : new GameObject("Voxel Selection Outline");
            outlineObject.transform.SetParent(transform, false);
            outline = outlineObject.GetComponent<LineRenderer>();
            if (outline == null) outline = outlineObject.AddComponent<LineRenderer>();
            outline.useWorldSpace = true;
            outline.loop = false;
            outline.positionCount = 24;
            outline.startWidth = 0.018f;
            outline.endWidth = 0.018f;
            outline.numCornerVertices = 0;
            outline.numCapVertices = 0;
            if (outlineMaterial != null) outline.sharedMaterial = outlineMaterial;
            outline.enabled = false;
        }

        private void SetOutlineVisible(bool visible)
        {
            EnsureOutline();
            outline.enabled = visible;
        }

        private void UpdateStableOutline(Int3 cell)
        {
            Vector3 min = world.Center + new Vector3(cell.x, cell.y, cell.z)
                - Vector3.one * 0.01f;
            Vector3 max = min + Vector3.one * 1.02f;
            SetOutlinePoints(min, max);
        }

        private void UpdateLegacyOutline(VoxelAddress address)
        {
            VoxelBlockFrame frame = world.GetBlockFrame(address);
            Vector3 east = frame.east * (frame.halfEast + 0.025f);
            Vector3 north = frame.north * (frame.halfNorth + 0.025f);
            Vector3 up = frame.radial * (frame.halfRadial + 0.025f);
            Vector3 center = frame.center;
            Vector3 min = center - east - north - up;
            Vector3 max = center + east + north + up;
            SetOutlinePoints(min, max);
        }

        private void SetOutlinePoints(Vector3 min, Vector3 max)
        {
            Vector3[] p =
            {
                new Vector3(min.x,min.y,min.z), new Vector3(max.x,min.y,min.z),
                new Vector3(max.x,min.y,max.z), new Vector3(min.x,min.y,max.z),
                new Vector3(min.x,max.y,min.z), new Vector3(max.x,max.y,min.z),
                new Vector3(max.x,max.y,max.z), new Vector3(min.x,max.y,max.z)
            };
            int[] order =
            {
                0,1, 1,2, 2,3, 3,0,
                4,5, 5,6, 6,7, 7,4,
                0,4, 1,5, 2,6, 3,7
            };
            for (int i = 0; i < order.Length; i++) outline.SetPosition(i, p[order[i]]);
        }
    }
}
