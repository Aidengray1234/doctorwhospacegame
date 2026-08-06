using UnityEngine;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public enum BlockRenderLayer : byte
    {
        None,
        Opaque,
        Cutout,
        Transparent,
        Water,
        Emissive
    }

    public enum BlockCollisionShape : byte
    {
        None,
        FullCube,
        SlabBottom,
        SlabTop
    }

    public enum BlockOrientationMode : byte
    {
        None,
        RadialEastNorthAxis,
        HorizontalFacing,
        SixDirectionFacing
    }

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
        public readonly Color32 topColor;
        public readonly Color32 sideColor;
        public readonly Color32 bottomColor;

        public BlockDefinition(
            ushort blockId,
            string blockName,
            bool isSolid,
            bool isReplaceable,
            bool isLiquid,
            byte light,
            BlockRenderLayer layer,
            BlockCollisionShape shape,
            BlockOrientationMode orientation,
            Color32 top,
            Color32 side,
            Color32 bottom)
        {
            id = blockId;
            name = blockName;
            solid = isSolid;
            replaceable = isReplaceable;
            liquid = isLiquid;
            emittedLight = light;
            renderLayer = layer;
            collisionShape = shape;
            orientationMode = orientation;
            topColor = top;
            sideColor = side;
            bottomColor = bottom;
        }
    }
}
