using System;
using DoctorWho.VoxelUniverse.Input;
using DoctorWho.VoxelUniverse.Saves;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Inventory
{
    public sealed class VoxelInventory : MonoBehaviour
    {
        [Serializable]
        public struct Slot
        {
            public ushort blockId;
            public int count;
        }

        [SerializeField] private bool creativeMode = true;
        [SerializeField] private int selectedSlot;
        [SerializeField] private bool inventoryOpen;
        [SerializeField] private VoxelSaveSystem saveSystem;
        [SerializeField] private Texture2D atlas;
        private readonly Slot[] slots = new Slot[36];
        private GUIStyle centered;
        private GUIStyle small;
        private GUIStyle title;
        private GUIStyle countStyle;

        public bool CreativeMode { get { return creativeMode; } }
        public int SelectedSlot { get { return selectedSlot; } }
        public bool InventoryOpen { get { return inventoryOpen; } }

        public BlockState SelectedBlock
        {
            get
            {
                Slot slot = slots[Mathf.Clamp(selectedSlot, 0, 8)];
                return slot.blockId == BlockRegistry.Air
                    ? BlockState.Air
                    : new BlockState(slot.blockId, 0, 0);
            }
        }

        public void Configure(VoxelSaveSystem saves, bool creative)
        {
            Configure(saves, creative, atlas);
        }

        public void Configure(VoxelSaveSystem saves, bool creative, Texture2D textureAtlas)
        {
            saveSystem = saves;
            creativeMode = creative;
            atlas = textureAtlas;
            DisableLegacyInventoryUi();
            LoadFromSave();
            if (creativeMode && IsEmpty()) FillCreativeDefaults();
        }

        private void Awake()
        {
            DisableLegacyInventoryUi();
        }

        private void Start()
        {
            if (saveSystem == null) saveSystem = FindObjectOfType<VoxelSaveSystem>();
            LoadFromSave();
            if (creativeMode && IsEmpty()) FillCreativeDefaults();
            SetOpen(false);
        }

        private void Update()
        {
            if (VoxelInput.InventoryPressed)
            {
                SetOpen(!inventoryOpen);
                return;
            }
            if (inventoryOpen && VoxelInput.EscapePressed)
            {
                SetOpen(false);
                return;
            }

            if (!inventoryOpen)
            {
                if (VoxelInput.PreviousHotbarPressed) Select((selectedSlot + 8) % 9);
                if (VoxelInput.NextHotbarPressed) Select((selectedSlot + 1) % 9);
            }
            for (int i = 0; i < 9; i++)
                if (VoxelInput.HotbarSlotPressed(i)) Select(i);
        }

        public bool Add(ushort blockId, int amount)
        {
            if (blockId == BlockRegistry.Air || blockId == BlockRegistry.Water || amount <= 0)
                return false;
            for (int i = 0; i < slots.Length && amount > 0; i++)
            {
                if (slots[i].blockId != blockId || slots[i].count >= 999) continue;
                Slot slot = slots[i];
                int accepted = Mathf.Min(amount, 999 - slot.count);
                slot.count += accepted;
                slots[i] = slot;
                amount -= accepted;
            }
            for (int i = 0; i < slots.Length && amount > 0; i++)
            {
                if (slots[i].blockId != BlockRegistry.Air && slots[i].count > 0) continue;
                int accepted = Mathf.Min(999, amount);
                slots[i] = new Slot { blockId = blockId, count = accepted };
                amount -= accepted;
            }
            Persist();
            return amount <= 0;
        }

        public bool ConsumeSelected(int amount)
        {
            if (creativeMode) return !SelectedBlock.IsAir;
            Slot slot = slots[selectedSlot];
            if (slot.count < amount || slot.blockId == BlockRegistry.Air) return false;
            slot.count -= amount;
            if (slot.count <= 0) slot = new Slot();
            slots[selectedSlot] = slot;
            Persist();
            return true;
        }

        private void SetOpen(bool value)
        {
            inventoryOpen = value;
            Cursor.lockState = value ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = value;
        }

        private void Select(int index)
        {
            selectedSlot = Mathf.Clamp(index, 0, 8);
            Persist();
        }

        private void AssignCreativeBlock(ushort blockId)
        {
            slots[selectedSlot] = new Slot { blockId = blockId, count = 999 };
            Persist();
        }

        private void FillCreativeDefaults()
        {
            ushort[] defaults =
            {
                BlockRegistry.Stone, BlockRegistry.Dirt, BlockRegistry.Grass,
                BlockRegistry.Sand, BlockRegistry.Log, BlockRegistry.Glass,
                BlockRegistry.Torch, BlockRegistry.Snow, BlockRegistry.Water
            };
            for (int i = 0; i < defaults.Length; i++)
                slots[i] = new Slot { blockId = defaults[i], count = 999 };
            Persist();
        }

        private bool IsEmpty()
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].count > 0 && slots[i].blockId != BlockRegistry.Air) return false;
            return true;
        }

        private void LoadFromSave()
        {
            if (saveSystem == null) return;
            VoxelSaveSystem.InventoryRecord record = saveSystem.GetInventoryCopy();
            selectedSlot = Mathf.Clamp(record.selectedSlot, 0, 8);
            for (int i = 0; i < slots.Length; i++) slots[i] = new Slot();
            for (int i = 0; i < record.slots.Count; i++)
            {
                VoxelSaveSystem.ItemStackRecord saved = record.slots[i];
                if (saved.slot < 0 || saved.slot >= slots.Length) continue;
                slots[saved.slot] = new Slot
                {
                    blockId = (ushort)Mathf.Clamp(saved.blockId, 0, ushort.MaxValue),
                    count = Mathf.Max(0, saved.count)
                };
            }
        }

        private void Persist()
        {
            if (saveSystem == null) return;
            VoxelSaveSystem.InventoryRecord record = new VoxelSaveSystem.InventoryRecord();
            record.selectedSlot = selectedSlot;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].blockId == BlockRegistry.Air || slots[i].count <= 0) continue;
                record.slots.Add(new VoxelSaveSystem.ItemStackRecord
                {
                    slot = i,
                    blockId = slots[i].blockId,
                    count = slots[i].count
                });
            }
            saveSystem.SetInventory(record);
        }

        private void EnsureStyles(float scale)
        {
            if (centered == null)
            {
                centered = new GUIStyle(GUI.skin.label);
                centered.alignment = TextAnchor.MiddleCenter;
                centered.normal.textColor = Color.white;
                centered.wordWrap = true;
                small = new GUIStyle(centered);
                title = new GUIStyle(centered);
                title.fontStyle = FontStyle.Bold;
                countStyle = new GUIStyle(centered);
                countStyle.alignment = TextAnchor.LowerRight;
                countStyle.fontStyle = FontStyle.Bold;
            }
            centered.fontSize = Mathf.RoundToInt(13f * scale);
            small.fontSize = Mathf.RoundToInt(10f * scale);
            title.fontSize = Mathf.RoundToInt(24f * scale);
            countStyle.fontSize = Mathf.RoundToInt(13f * scale);
        }

        private void OnGUI()
        {
            float scale = Mathf.Clamp(Mathf.Min(Screen.width / 1280f, Screen.height / 720f), 0.72f, 1.35f);
            EnsureStyles(scale);
            DrawCrosshair(scale);
            DrawHotbar(scale);
            if (inventoryOpen) DrawInventory(scale);
        }

        private void DrawCrosshair(float scale)
        {
            if (inventoryOpen) return;
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            float length = 7f * scale;
            float thickness = Mathf.Max(1f, 2f * scale);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.92f);
            GUI.DrawTexture(new Rect(x - thickness * 0.5f, y - length, thickness, length * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(x - length, y - thickness * 0.5f, length * 2f, thickness), Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DrawHotbar(float scale)
        {
            float cell = 58f * scale;
            float total = cell * 9f;
            float startX = (Screen.width - total) * 0.5f;
            float y = Screen.height - cell - 18f * scale;
            for (int i = 0; i < 9; i++)
            {
                Rect rect = new Rect(startX + i * cell, y, cell - 2f, cell - 2f);
                Color old = GUI.color;
                GUI.color = i == selectedSlot
                    ? new Color(1f, 0.82f, 0.22f, 0.98f)
                    : new Color(0.08f, 0.10f, 0.09f, 0.86f);
                GUI.Box(rect, GUIContent.none);
                GUI.color = old;
                Slot slot = slots[i];
                if (slot.blockId != BlockRegistry.Air && slot.count > 0)
                {
                    DrawBlockIcon(new Rect(rect.x + 7f * scale, rect.y + 7f * scale,
                        rect.width - 14f * scale, rect.height - 14f * scale), slot.blockId);
                    GUI.Label(new Rect(rect.x + rect.width - 25f * scale, rect.y + rect.height - 20f * scale,
                        22f * scale, 17f * scale), creativeMode ? "∞" : slot.count.ToString(), countStyle);
                }
                GUI.Label(new Rect(rect.x + 2f, rect.y + 1f, 18f * scale, 16f * scale),
                    (i + 1).ToString(), small);
            }
        }

        private void DrawInventory(float scale)
        {
            int columns = 6;
            float cell = 72f * scale;
            float width = columns * cell + 42f * scale;
            float rows = Mathf.Ceil(BlockRegistry.CreativeBlocks.Length / (float)columns);
            float height = 92f * scale + rows * cell;
            Rect panel = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - height) * 0.5f, width, height);
            Color old = GUI.color;
            GUI.color = new Color(0.04f, 0.06f, 0.05f, 0.94f);
            GUI.Box(panel, GUIContent.none);
            GUI.color = old;
            GUI.Label(new Rect(panel.x, panel.y + 8f * scale, panel.width, 32f * scale),
                creativeMode ? "CREATIVE BLOCK INVENTORY" : "BLOCK INVENTORY", title);
            GUI.Label(new Rect(panel.x + 14f * scale, panel.y + 42f * scale,
                panel.width - 28f * scale, 24f * scale),
                "Choose a block for the selected hotbar slot. Press E or Esc to close.", centered);

            float gridX = panel.x + 21f * scale;
            float gridY = panel.y + 76f * scale;
            for (int i = 0; i < BlockRegistry.CreativeBlocks.Length; i++)
            {
                ushort blockId = BlockRegistry.CreativeBlocks[i];
                int column = i % columns;
                int row = i / columns;
                Rect button = new Rect(gridX + column * cell, gridY + row * cell,
                    cell - 8f * scale, cell - 8f * scale);
                if (GUI.Button(button, GUIContent.none)) AssignCreativeBlock(blockId);
                DrawBlockIcon(new Rect(button.x + 8f * scale, button.y + 5f * scale,
                    button.width - 16f * scale, button.height - 24f * scale), blockId);
                GUI.Label(new Rect(button.x + 2f, button.y + button.height - 20f * scale,
                    button.width - 4f, 18f * scale), BlockRegistry.Get(blockId).name, small);
            }
        }

        private void DrawBlockIcon(Rect rect, ushort blockId)
        {
            BlockDefinition definition = BlockRegistry.Get(blockId);
            if (atlas != null)
            {
                GUI.DrawTextureWithTexCoords(rect, atlas,
                    BlockRegistry.TileUv(definition.topTile), true);
                return;
            }
            Color old = GUI.color;
            GUI.color = definition.topColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = old;
        }

        private void DisableLegacyInventoryUi()
        {
            MonoBehaviour[] behaviours = FindObjectsOfType<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null || behaviour == this) continue;
                if (behaviour.GetType().FullName == "DoctorWho.BlockPlanets.BlockInventory")
                    behaviour.enabled = false;
            }
        }
    }
}
