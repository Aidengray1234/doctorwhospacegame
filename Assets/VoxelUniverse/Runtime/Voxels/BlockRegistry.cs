using System.Collections.Generic;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public static class BlockRegistry
    {
        public const ushort Air=0, Stone=1, Dirt=2, Grass=3, Sand=4, Water=5, Snow=6,
            Bedrock=7, CoalOre=8, IronOre=9, Log=10, Torch=11, Glass=12;
        public const int AtlasColumns = 8;
        public const int AtlasRows = 4;
        public static readonly ushort[] CreativeBlocks =
        { Stone, Dirt, Grass, Sand, Snow, Bedrock, CoalOre, IronOre, Log, Glass, Torch, Water };

        private static readonly Dictionary<ushort, BlockDefinition> Definitions =
            new Dictionary<ushort, BlockDefinition>();

        static BlockRegistry()
        {
            Register(D(Air,"Air",false,true,false,0,BlockRenderLayer.None,BlockCollisionShape.None,
                BlockOrientationMode.None,27,27,27,C(0,0,0,0),C(0,0,0,0),C(0,0,0,0)));
            Register(D(Stone,"Stone",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,0,0,0,C(122,126,132),C(112,116,122),C(105,108,114)));
            Register(D(Dirt,"Dirt",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,1,1,1,C(126,88,57),C(116,77,48),C(105,68,42)));
            Register(D(Grass,"Grass",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,2,3,1,C(76,148,61),C(99,119,55),C(116,77,48)));
            Register(D(Sand,"Sand",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,4,4,4,C(219,207,151),C(204,190,132),C(194,179,121)));
            Register(D(Water,"Water",false,true,true,0,BlockRenderLayer.Water,BlockCollisionShape.None,
                BlockOrientationMode.None,24,24,24,C(55,119,220,185),C(45,103,205,160),C(35,86,180,145)));
            Register(D(Snow,"Snow",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,5,5,5,C(239,246,250),C(219,231,239),C(195,212,225)));
            Register(D(Bedrock,"Bedrock",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,6,6,6,C(48,49,54),C(40,41,45),C(34,35,39)));
            Register(D(CoalOre,"Coal Ore",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,10,10,10,C(72,72,75),C(65,65,68),C(58,58,61)));
            Register(D(IronOre,"Iron Ore",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,11,11,11,C(170,129,103),C(150,111,89),C(130,96,79)));
            Register(D(Log,"Oak Log",true,false,false,0,BlockRenderLayer.Opaque,BlockCollisionShape.FullCube,
                BlockOrientationMode.RadialEastNorthAxis,15,14,15,C(177,142,85),C(104,76,42),C(177,142,85)));
            Register(D(Torch,"Torch",false,false,false,14,BlockRenderLayer.Emissive,BlockCollisionShape.None,
                BlockOrientationMode.SixDirectionFacing,27,27,27,C(255,211,94),C(255,176,52),C(198,112,35)));
            Register(D(Glass,"Glass",true,false,false,0,BlockRenderLayer.Transparent,BlockCollisionShape.FullCube,
                BlockOrientationMode.None,25,25,25,C(220,242,250,150),C(195,226,240,125),C(180,215,232,110)));
        }

        public static BlockDefinition Get(ushort id)
        { BlockDefinition d; return Definitions.TryGetValue(id,out d) ? d : Definitions[Air]; }
        public static bool IsSolid(BlockState state) { return Get(state.BlockId).solid; }
        public static bool IsOpaque(BlockState state)
        {
            BlockRenderLayer layer=Get(state.BlockId).renderLayer;
            return layer==BlockRenderLayer.Opaque || layer==BlockRenderLayer.Cutout;
        }
        public static bool IsReplaceable(BlockState state) { return Get(state.BlockId).replaceable; }

        public static Rect TileUv(int tile)
        {
            int t=Mathf.Clamp(tile,0,AtlasColumns*AtlasRows-1);
            int column=t%AtlasColumns, row=t/AtlasColumns;
            float w=1f/AtlasColumns, h=1f/AtlasRows;
            return new Rect(column*w,1f-(row+1)*h,w,h);
        }

        private static BlockDefinition D(ushort id,string name,bool solid,bool replaceable,bool liquid,
            byte light,BlockRenderLayer layer,BlockCollisionShape shape,BlockOrientationMode orient,
            int top,int side,int bottom,Color32 topColor,Color32 sideColor,Color32 bottomColor)
        {
            return new BlockDefinition(id,name,solid,replaceable,liquid,light,layer,shape,orient,
                top,side,bottom,topColor,sideColor,bottomColor);
        }
        private static void Register(BlockDefinition d) { Definitions[d.id]=d; }
        private static Color32 C(byte r,byte g,byte b,byte a=255) { return new Color32(r,g,b,a); }
    }
}
