using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DoctorWho.BlockPlanets
{
    public sealed class BlockInventory : MonoBehaviour
    {
        [SerializeField] private Texture2D atlas;
        [SerializeField] private BlockPlanetSettings settings;
        [SerializeField] private BlockPlanetWorld world;

        private readonly BlockId[] hotbar = new BlockId[9];
        private readonly Dictionary<BlockId, int> counts = new Dictionary<BlockId, int>();
        private int selected;
        private bool open;
        private GUIStyle labelStyle;
        private GUIStyle titleStyle;

        public bool IsOpen => open;
        public BlockId SelectedBlock => hotbar[Mathf.Clamp(selected, 0, hotbar.Length - 1)];

        public void Configure(Texture2D textureAtlas, BlockPlanetSettings value, BlockPlanetWorld owner)
        {
            atlas = textureAtlas;
            settings = value;
            world = owner;
            InitializeInventory();
        }

        private void Awake() => InitializeInventory();

        private void InitializeInventory()
        {
            BlockId[] defaults =
            {
                BlockId.Dirt, BlockId.Stone, BlockId.Cobblestone, BlockId.OakPlanks,
                BlockId.Sand, BlockId.Bricks, BlockId.Glass, BlockId.TNT, BlockId.Bedrock
            };
            for (int i = 0; i < hotbar.Length; i++) hotbar[i] = defaults[i];
            if (settings != null)
            {
                for (int i = 0; i < BlockCatalog.InventoryBlocks.Length; i++)
                    if (!counts.ContainsKey(BlockCatalog.InventoryBlocks[i])) counts[BlockCatalog.InventoryBlocks[i]] = settings.startingStackSize;
            }
        }

        private void Update()
        {
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) selected = 0;
                if (Keyboard.current.digit2Key.wasPressedThisFrame) selected = 1;
                if (Keyboard.current.digit3Key.wasPressedThisFrame) selected = 2;
                if (Keyboard.current.digit4Key.wasPressedThisFrame) selected = 3;
                if (Keyboard.current.digit5Key.wasPressedThisFrame) selected = 4;
                if (Keyboard.current.digit6Key.wasPressedThisFrame) selected = 5;
                if (Keyboard.current.digit7Key.wasPressedThisFrame) selected = 6;
                if (Keyboard.current.digit8Key.wasPressedThisFrame) selected = 7;
                if (Keyboard.current.digit9Key.wasPressedThisFrame) selected = 8;
                if (Keyboard.current.eKey.wasPressedThisFrame) SetOpen(!open);
                if (open && Keyboard.current.escapeKey.wasPressedThisFrame) SetOpen(false);
            }
            if (!open && Mouse.current != null)
            {
                float wheel = Mouse.current.scroll.ReadValue().y;
                if (wheel > 0f) selected = (selected + hotbar.Length - 1) % hotbar.Length;
                else if (wheel < 0f) selected = (selected + 1) % hotbar.Length;
            }
        }

        private void SetOpen(bool value)
        {
            open = value;
            Cursor.lockState = open ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = open;
        }

        public void Add(BlockId id, int amount)
        {
            if (id == BlockId.Air || id == BlockId.Water || amount <= 0) return;
            int count;
            counts.TryGetValue(id, out count);
            counts[id] = count + amount;
        }

        public bool CanPlaceSelected()
        {
            if (settings != null && settings.creativeInventory) return true;
            int count;
            return counts.TryGetValue(SelectedBlock, out count) && count > 0;
        }

        public void ConsumeSelected()
        {
            if (settings != null && settings.creativeInventory) return;
            BlockId id = SelectedBlock;
            int count;
            if (counts.TryGetValue(id, out count) && count > 0) counts[id] = count - 1;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawCrosshair();
            DrawHotbar();
            if (open) DrawInventoryWindow();
            if (world != null && world.QueuedChunkCount > 0)
                GUI.Label(new Rect(12f, 12f, 310f, 24f), "Loading chunks: " + world.QueuedChunkCount, labelStyle);
        }

        private void EnsureStyles()
        {
            if (labelStyle != null) return;
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, normal = { textColor = Color.white } };
            titleStyle = new GUIStyle(labelStyle) { fontSize = 20, fontStyle = FontStyle.Bold };
        }

        private void DrawCrosshair()
        {
            if (open) return;
            float x = Screen.width * 0.5f;
            float y = Screen.height * 0.5f;
            GUI.Box(new Rect(x - 1f, y - 7f, 2f, 14f), GUIContent.none);
            GUI.Box(new Rect(x - 7f, y - 1f, 14f, 2f), GUIContent.none);
        }

        private void DrawHotbar()
        {
            float size = 54f;
            float total = size * hotbar.Length;
            float startX = (Screen.width - total) * 0.5f;
            float y = Screen.height - size - 18f;
            for (int i = 0; i < hotbar.Length; i++)
            {
                Rect rect = new Rect(startX + i * size, y, size - 2f, size - 2f);
                Color previous = GUI.color;
                GUI.color = i == selected ? new Color(1f, 0.9f, 0.35f, 1f) : new Color(0.25f, 0.25f, 0.25f, 0.93f);
                GUI.Box(rect, GUIContent.none);
                GUI.color = previous;
                DrawIcon(new Rect(rect.x + 7f, rect.y + 7f, rect.width - 14f, rect.height - 14f), hotbar[i]);
                GUI.Label(new Rect(rect.x + 2f, rect.y + 1f, 16f, 16f), (i + 1).ToString(), labelStyle);
                string countText = settings != null && settings.creativeInventory ? "∞" : GetCount(hotbar[i]).ToString();
                GUI.Label(new Rect(rect.x + 22f, rect.y + 34f, 27f, 16f), countText, labelStyle);
            }
        }

        private void DrawInventoryWindow()
        {
            float width = 520f;
            float height = 330f;
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(panel, GUIContent.none);
            GUI.Label(new Rect(panel.x, panel.y + 8f, panel.width, 32f), "BLOCK INVENTORY", titleStyle);
            GUI.Label(new Rect(panel.x, panel.y + 38f, panel.width, 24f), "Click a block to put it in the selected hotbar slot. Press E to close.", labelStyle);

            const int columns = 7;
            const float cell = 62f;
            float gridX = panel.x + 42f;
            float gridY = panel.y + 72f;
            for (int i = 0; i < BlockCatalog.InventoryBlocks.Length; i++)
            {
                BlockId block = BlockCatalog.InventoryBlocks[i];
                int column = i % columns;
                int row = i / columns;
                Rect cellRect = new Rect(gridX + column * cell, gridY + row * 78f, 54f, 70f);
                if (GUI.Button(new Rect(cellRect.x, cellRect.y, 54f, 54f), GUIContent.none)) hotbar[selected] = block;
                DrawIcon(new Rect(cellRect.x + 6f, cellRect.y + 6f, 42f, 42f), block);
                GUI.Label(new Rect(cellRect.x - 10f, cellRect.y + 53f, 74f, 18f), BlockCatalog.Name(block), new GUIStyle(labelStyle) { fontSize = 10 });
            }
        }

        private int GetCount(BlockId id)
        {
            int value;
            return counts.TryGetValue(id, out value) ? value : 0;
        }

        private void DrawIcon(Rect rect, BlockId block)
        {
            if (atlas == null) return;
            Rect uv = BlockCatalog.TileUv(BlockCatalog.Tile(block, 2));
            GUI.DrawTextureWithTexCoords(rect, atlas, uv, true);
        }
    }
}
