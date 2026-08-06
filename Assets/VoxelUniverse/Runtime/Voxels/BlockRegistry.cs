using System.Collections.Generic;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public static class BlockRegistry
    {
        public const ushort Air = 0;
        public const ushort Stone = 1;
        public const ushort Dirt = 2;
        public const ushort Grass = 3;
        public const ushort Sand = 4;
        public const ushort Water = 5;
        public const ushort Snow = 6;
        public const ushort Bedrock = 7;
        public const ushort CoalOre = 8;
        public const ushort IronOre = 9;
        public const ushort Log = 10;
        public const ushort Torch = 11;
        public const ushort Glass = 12;

        private static readonly Dictionary<ushort, BlockDefinition> Definitions =
            new Dictionary<ushort, BlockDefinition>();

        static BlockRegistry()
        {
            Register(new BlockDefinition(Air, "Air", false, true, false, 0, BlockRenderLayer.None, BlockCollisionShape.None, BlockOrientationMode.None,
                Clear(), Clear(), Clear()));
            Register(new BlockDefinition(Stone, "Stone", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(122, 126, 132), C(112, 116, 122), C(105, 108, 114)));
            Register(new BlockDefinition(Dirt, "Dirt", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(126, 88, 57), C(116, 77, 48), C(105, 68, 42)));
            Register(new BlockDefinition(Grass, "Grass", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(76, 148, 61), C(99, 119, 55), C(116, 77, 48)));
            Register(new BlockDefinition(Sand, "Sand", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(219, 207, 151), C(204, 190, 132), C(194, 179, 121)));
            Register(new BlockDefinition(Water, "Water", false, true, true, 0, BlockRenderLayer.Water, BlockCollisionShape.None, BlockOrientationMode.None,
                C(55, 119, 220, 155), C(45, 103, 205, 135), C(35, 86, 180, 120)));
            Register(new BlockDefinition(Snow, "Snow", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(239, 246, 250), C(219, 231, 239), C(195, 212, 225)));
            Register(new BlockDefinition(Bedrock, "Bedrock", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(48, 49, 54), C(40, 41, 45), C(34, 35, 39)));
            Register(new BlockDefinition(CoalOre, "Coal Ore", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(72, 72, 75), C(65, 65, 68), C(58, 58, 61)));
            Register(new BlockDefinition(IronOre, "Iron Ore", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(170, 129, 103), C(150, 111, 89), C(130, 96, 79)));
            Register(new BlockDefinition(Log, "Log", true, false, false, 0, BlockRenderLayer.Opaque, BlockCollisionShape.FullCube, BlockOrientationMode.RadialEastNorthAxis,
                C(177, 142, 85), C(104, 76, 42), C(177, 142, 85)));
            Register(new BlockDefinition(Torch, "Torch", false, false, false, 14, BlockRenderLayer.Emissive, BlockCollisionShape.None, BlockOrientationMode.SixDirectionFacing,
                C(255, 211, 94), C(255, 176, 52), C(198, 112, 35)));
            Register(new BlockDefinition(Glass, "Glass", true, false, false, 0, BlockRenderLayer.Transparent, BlockCollisionShape.FullCube, BlockOrientationMode.None,
                C(190, 230, 244, 95), C(170, 215, 235, 80), C(150, 200, 225, 75)));
        }

        public static BlockDefinition Get(ushort id)
        {
            BlockDefinition definition;
            return Definitions.TryGetValue(id, out definition) ? definition : Definitions[Air];
        }

        public static bool IsSolid(BlockState state)
        {
            return Get(state.BlockId).solid;
        }

        public static bool IsOpaque(BlockState state)
        {
            BlockRenderLayer layer = Get(state.BlockId).renderLayer;
            return layer == BlockRenderLayer.Opaque || layer == BlockRenderLayer.Cutout;
        }

        public static bool IsReplaceable(BlockState state)
        {
            return Get(state.BlockId).replaceable;
        }

        private static void Register(BlockDefinition definition)
        {
            Definitions[definition.id] = definition;
        }

        private static Color32 C(byte r, byte g, byte b, byte a = 255)
        {
            return new Color32(r, g, b, a);
        }

        private static Color32 Clear()
        {
            return new Color32(0, 0, 0, 0);
        }
    }
}
