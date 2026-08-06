using System;
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
        private readonly Slot[] slots = new Slot[36];
        [SerializeField] private VoxelSaveSystem saveSystem;

        public bool CreativeMode { get { return creativeMode; } }
        public int SelectedSlot { get { return selectedSlot; } }
        public bool InventoryOpen { get { return inventoryOpen; } }

        public BlockState SelectedBlock
        {
            get
            {
                Slot slot = slots[selectedSlot];
                return slot.blockId == BlockRegistry.Air
                    ? BlockState.Air
                    : new BlockState(slot.blockId, 0, 0);
            }
        }

        public void Configure(VoxelSaveSystem saves, bool creative)
        {
            saveSystem = saves;
            creativeMode = creative;
            LoadFromSave();
            if (creativeMode && IsEmpty()) FillCreativeDefaults();
        }

        public bool Add(ushort blockId, int amount)
        {
            if (blockId == BlockRegistry.Air || amount <= 0) return false;
            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].blockId == blockId && slots[i].count < 999)
                {
                    Slot slot = slots[i];
                    int accepted = Mathf.Min(amount, 999 - slot.count);
                    slot.count += accepted;
                    slots[i] = slot;
                    amount -= accepted;
                    if (amount <= 0)
                    {
                        Persist();
                        return true;
                    }
                }
            }

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i].blockId == BlockRegistry.Air || slots[i].count <= 0)
                {
                    slots[i] = new Slot { blockId = blockId, count = Mathf.Min(999, amount) };
                    amount -= slots[i].count;
                    if (amount <= 0)
                    {
                        Persist();
                        return true;
                    }
                }
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


        private void Start()
        {
            if (saveSystem == null) saveSystem = FindObjectOfType<VoxelSaveSystem>();
            LoadFromSave();
            if (creativeMode && IsEmpty()) FillCreativeDefaults();
        }

        private void Update()
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel > 0.01f) Select((selectedSlot + 8) % 9);
            if (wheel < -0.01f) Select((selectedSlot + 1) % 9);
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) Select(i);
            }
            if (Input.GetKeyDown(KeyCode.E)) inventoryOpen = !inventoryOpen;
        }

        private void Select(int index)
        {
            selectedSlot = Mathf.Clamp(index, 0, 8);
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

        private void OnGUI()
        {
            const float size = 48f;
            float total = size * 9f;
            float startX = (Screen.width - total) * 0.5f;
            float y = Screen.height - size - 18f;
            for (int i = 0; i < 9; i++)
            {
                Rect rect = new Rect(startX + i * size, y, size - 2f, size - 2f);
                GUI.Box(rect, i == selectedSlot ? ">" : "");
                Slot slot = slots[i];
                if (slot.blockId != BlockRegistry.Air && slot.count > 0)
                {
                    BlockDefinition definition = BlockRegistry.Get(slot.blockId);
                    GUI.Label(new Rect(rect.x + 4f, rect.y + 4f, rect.width - 8f, 20f), definition.name);
                    GUI.Label(new Rect(rect.x + 4f, rect.y + 24f, rect.width - 8f, 18f),
                        creativeMode ? "∞" : slot.count.ToString());
                }
            }

            if (!inventoryOpen) return;
            Rect panel = new Rect((Screen.width - 520f) * 0.5f, (Screen.height - 300f) * 0.5f, 520f, 300f);
            GUI.Box(panel, creativeMode ? "Creative Inventory" : "Inventory");
            for (int i = 0; i < slots.Length; i++)
            {
                int column = i % 9;
                int row = i / 9;
                Rect rect = new Rect(panel.x + 18f + column * 54f, panel.y + 36f + row * 56f, 50f, 50f);
                GUI.Box(rect, "");
                Slot slot = slots[i];
                if (slot.blockId != BlockRegistry.Air && slot.count > 0)
                {
                    GUI.Label(new Rect(rect.x + 3f, rect.y + 3f, 44f, 28f), BlockRegistry.Get(slot.blockId).name);
                    GUI.Label(new Rect(rect.x + 3f, rect.y + 31f, 44f, 16f),
                        creativeMode ? "∞" : slot.count.ToString());
                }
            }
        }
    }
}
