using System;
using UnityEngine;

namespace DoctorWho.BlockPlanets
{
    // IDs intentionally follow the supplied Planetcraft example mod where practical.
    public enum BlockId : byte
    {
        Air = 0,
        Stone = 1,
        Grass = 2,
        Dirt = 3,
        Cobblestone = 4,
        Bedrock = 5,
        Bricks = 6,
        Clay = 7,
        CoalOre = 8,
        CraftingTable = 9,
        DiamondOre = 10,
        Gravel = 11,
        IronOre = 12,
        OakLeaves = 13,
        OakLog = 14,
        OakPlanks = 15,
        Sand = 16,
        Snow = 17,
        TNT = 18,
        Water = 19,
        GoldOre = 20,
        Glass = 21
    }

    public static class BlockCatalog
    {
        public const int AtlasColumns = 8;
        public const int AtlasRows = 4;

        public static readonly BlockId[] InventoryBlocks =
        {
            BlockId.Stone, BlockId.Grass, BlockId.Dirt, BlockId.Cobblestone,
            BlockId.Bricks, BlockId.Clay, BlockId.CoalOre, BlockId.CraftingTable,
            BlockId.DiamondOre, BlockId.Gravel, BlockId.IronOre, BlockId.OakLeaves,
            BlockId.OakLog, BlockId.OakPlanks, BlockId.Sand, BlockId.Snow,
            BlockId.TNT, BlockId.GoldOre, BlockId.Glass, BlockId.Bedrock
        };

        public static bool IsRenderable(BlockId id) => id != BlockId.Air;
        public static bool IsTransparent(BlockId id) => id == BlockId.Water || id == BlockId.Glass;
        public static bool IsSolid(BlockId id) => id != BlockId.Air && id != BlockId.Water;
        public static bool CanBreak(BlockId id) => id != BlockId.Air && id != BlockId.Bedrock && id != BlockId.Water;

        public static string Name(BlockId id)
        {
            switch (id)
            {
                case BlockId.Stone: return "Stone";
                case BlockId.Grass: return "Grass Block";
                case BlockId.Dirt: return "Dirt";
                case BlockId.Cobblestone: return "Cobblestone";
                case BlockId.Bedrock: return "Bedrock";
                case BlockId.Bricks: return "Bricks";
                case BlockId.Clay: return "Clay";
                case BlockId.CoalOre: return "Coal Ore";
                case BlockId.CraftingTable: return "Crafting Table";
                case BlockId.DiamondOre: return "Diamond Ore";
                case BlockId.Gravel: return "Gravel";
                case BlockId.IronOre: return "Iron Ore";
                case BlockId.OakLeaves: return "Oak Leaves";
                case BlockId.OakLog: return "Oak Log";
                case BlockId.OakPlanks: return "Oak Planks";
                case BlockId.Sand: return "Sand";
                case BlockId.Snow: return "Snow";
                case BlockId.TNT: return "TNT";
                case BlockId.Water: return "Water";
                case BlockId.GoldOre: return "Gold Ore";
                case BlockId.Glass: return "Glass";
                default: return "Air";
            }
        }

        // side order: +X, -X, +Y, -Y, +Z, -Z
        public static int Tile(BlockId id, int side)
        {
            switch (id)
            {
                case BlockId.Stone: return 0;
                case BlockId.Dirt: return 1;
                case BlockId.Grass: return side == 2 ? 2 : side == 3 ? 1 : 3;
                case BlockId.Sand: return 4;
                case BlockId.Snow: return 5;
                case BlockId.Bedrock: return 6;
                case BlockId.Cobblestone: return 7;
                case BlockId.Bricks: return 8;
                case BlockId.Clay: return 9;
                case BlockId.CoalOre: return 10;
                case BlockId.IronOre: return 11;
                case BlockId.DiamondOre: return 12;
                case BlockId.Gravel: return 13;
                case BlockId.OakLog: return side == 2 || side == 3 ? 15 : 14;
                case BlockId.OakPlanks: return 16;
                case BlockId.OakLeaves: return 17;
                case BlockId.CraftingTable:
                    if (side == 2) return 18;
                    if (side == 4) return 20;
                    return 19;
                case BlockId.TNT: return side == 2 ? 21 : side == 3 ? 23 : 22;
                case BlockId.Water: return 24;
                case BlockId.Glass: return 25;
                case BlockId.GoldOre: return 26;
                default: return 27;
            }
        }

        public static Rect TileUv(int tile)
        {
            int column = tile % AtlasColumns;
            int rowFromTop = tile / AtlasColumns;
            float width = 1f / AtlasColumns;
            float height = 1f / AtlasRows;
            float insetU = 0.35f / 128f;
            float insetV = 0.35f / 64f;
            float x = column * width + insetU;
            float y = 1f - (rowFromTop + 1) * height + insetV;
            return new Rect(x, y, width - insetU * 2f, height - insetV * 2f);
        }
    }
}
