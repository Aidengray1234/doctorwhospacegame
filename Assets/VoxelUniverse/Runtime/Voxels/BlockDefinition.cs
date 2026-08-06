using UnityEngine;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public enum BlockRenderLayer : byte { None, Opaque, Cutout, Transparent, Water, Emissive }
    public enum BlockCollisionShape : byte { None, FullCube, SlabBottom, SlabTop }
    public enum BlockOrientationMode : byte { None, RadialEastNorthAxis, HorizontalFacing, SixDirectionFacing }
    public enum BlockTextureFace : byte { Outer, Inner, West, East, South, North }

    public sealed class BlockDefinition
    {
        public readonly ushort id;
        public readonly string name;
        public readonly bool solid;
        public readonly bool replaceable;
        public readonly bool liquid;
        public readonly byte emittedLight;
        public readonly BlockRenderLayer renderLayer;
        public readonly BlockCollisionShape collisionShape;
        public readonly BlockOrientationMode orientationMode;
        public readonly int topTile;
        public readonly int sideTile;
        public readonly int bottomTile;
        public readonly Color32 topColor;
        public readonly Color32 sideColor;
        public readonly Color32 bottomColor;

        public BlockDefinition(ushort blockId, string blockName, bool isSolid, bool isReplaceable,
            bool isLiquid, byte light, BlockRenderLayer layer, BlockCollisionShape shape,
            BlockOrientationMode orientation, int topTextureTile, int sideTextureTile,
            int bottomTextureTile, Color32 top, Color32 side, Color32 bottom)
        {
            id = blockId; name = blockName; solid = isSolid; replaceable = isReplaceable;
            liquid = isLiquid; emittedLight = light; renderLayer = layer; collisionShape = shape;
            orientationMode = orientation; topTile = topTextureTile; sideTile = sideTextureTile;
            bottomTile = bottomTextureTile; topColor = top; sideColor = side; bottomColor = bottom;
        }

        public int GetTextureTile(BlockTextureFace face, byte orientation)
        {
            if (orientationMode == BlockOrientationMode.RadialEastNorthAxis)
            {
                BlockTextureFace positive = BlockTextureFace.Outer;
                BlockTextureFace negative = BlockTextureFace.Inner;
                if (orientation == 2 || orientation == 3)
                { positive = BlockTextureFace.East; negative = BlockTextureFace.West; }
                else if (orientation == 4 || orientation == 5)
                { positive = BlockTextureFace.North; negative = BlockTextureFace.South; }
                return face == positive || face == negative ? topTile : sideTile;
            }
            if (face == BlockTextureFace.Outer) return topTile;
            if (face == BlockTextureFace.Inner) return bottomTile;
            return sideTile;
        }

        public Color32 GetFallbackColor(BlockTextureFace face)
        {
            if (face == BlockTextureFace.Outer) return topColor;
            if (face == BlockTextureFace.Inner) return bottomColor;
            return sideColor;
        }
    }
}
