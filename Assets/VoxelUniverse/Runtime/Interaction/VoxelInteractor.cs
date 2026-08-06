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

        private LineRenderer outline;
        private VoxelRayHit currentHit;

        public void Configure(VoxelUniverseWorld voxelWorld, VoxelPlayerController playerController,
            VoxelInventory playerInventory, Camera camera, Material selectionMaterial)
        {
            world = voxelWorld;
            player = playerController;
            inventory = playerInventory;
            playerCamera = camera;
            outlineMaterial = selectionMaterial;
            EnsureOutline();
            outline.sharedMaterial = outlineMaterial;
        }

        private void Awake() { EnsureOutline(); }

        private void Update()
        {
            if (world == null || playerCamera == null || inventory == null) return;
            if (Cursor.lockState != CursorLockMode.Locked || inventory.InventoryOpen)
            {
                SetOutlineVisible(false);
                return;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            bool hit = VoxelDda.Cast(world, ray, world.Settings.interactionReach, out currentHit);
            SetOutlineVisible(hit);
            if (!hit) return;
            UpdateOutline(currentHit.address);

            if (VoxelInput.PrimaryPressed) Mine();
            if (VoxelInput.SecondaryPressed) Place();
        }

        private void Mine()
        {
            BlockDefinition definition = BlockRegistry.Get(currentHit.block.BlockId);
            if (currentHit.block.BlockId == BlockRegistry.Bedrock || definition.liquid) return;
            world.SetBlock(currentHit.address, BlockState.Air);
            inventory.Add(currentHit.block.BlockId, 1);
        }

        private void Place()
        {
            BlockState selected = inventory.SelectedBlock;
            if (selected.IsAir) return;
            VoxelAddress target = currentHit.adjacent;
            BlockState existing = world.GetBlock(target);
            if (!BlockRegistry.IsReplaceable(existing)) return;
            if (player != null && player.WouldOverlap(target)) return;

            BlockState placed = selected.WithOrientation(DetermineOrientation(currentHit.face));
            if (!inventory.ConsumeSelected(1)) return;
            world.SetBlock(target, placed);
        }

        private static byte DetermineOrientation(VoxelHitFace face)
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
            GameObject outlineObject = found != null
                ? found.gameObject
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

        private void UpdateOutline(VoxelAddress address)
        {
            VoxelBlockFrame frame = world.GetBlockFrame(address);
            Vector3 east = frame.east * (frame.halfEast + 0.025f);
            Vector3 north = frame.north * (frame.halfNorth + 0.025f);
            Vector3 up = frame.radial * (frame.halfRadial + 0.025f);
            Vector3 center = frame.center;
            Vector3[] p = new Vector3[8];
            p[0] = center - east - north - up;
            p[1] = center + east - north - up;
            p[2] = center + east + north - up;
            p[3] = center - east + north - up;
            p[4] = center - east - north + up;
            p[5] = center + east - north + up;
            p[6] = center + east + north + up;
            p[7] = center - east + north + up;
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
