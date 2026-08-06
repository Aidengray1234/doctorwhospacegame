using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Meshing
{
    public sealed class VoxelSectionMesher
    {
        private sealed class Builder
        {
            public readonly List<Vector3> vertices=new List<Vector3>(4096);
            public readonly List<Vector3> normals=new List<Vector3>(4096);
            public readonly List<Vector2> uv=new List<Vector2>(4096);
            public readonly List<Color32> colors=new List<Color32>(4096);
            public readonly List<int> triangles=new List<int>(6144);
            public MeshPayload ToPayload()
            {
                return new MeshPayload { vertices=vertices.ToArray(),normals=normals.ToArray(),
                    uv=uv.ToArray(),colors=colors.ToArray(),triangles=triangles.ToArray() };
            }
        }

        private readonly VoxelUniverseSettings settings;
        public VoxelSectionMesher(VoxelUniverseSettings runtimeSettings) { settings=runtimeSettings; }

        public SectionMeshData Build(VoxelSection section,int requestVersion,
            Func<VoxelAddress,BlockState> sampler)
        {
            Builder opaque=new Builder(), water=new Builder();
            for(int y=0;y<VoxelConstants.SectionSize;y++)
            for(int z=0;z<VoxelConstants.SectionSize;z++)
            for(int x=0;x<VoxelConstants.SectionSize;x++)
            {
                BlockState state=section.GetLocal(x,y,z);
                BlockDefinition definition=BlockRegistry.Get(state.BlockId);
                if(definition.renderLayer==BlockRenderLayer.None) continue;
                VoxelAddress a=section.ToAddress(x,y,z);
                Builder target=definition.renderLayer==BlockRenderLayer.Water?water:opaque;
                AddIfVisible(target,a,state,definition,BlockTextureFace.Outer,
                    new VoxelAddress(a.bodyId,a.face,a.u,a.v,a.radial+1),sampler);
                AddIfVisible(target,a,state,definition,BlockTextureFace.Inner,
                    new VoxelAddress(a.bodyId,a.face,a.u,a.v,a.radial-1),sampler);
                AddIfVisible(target,a,state,definition,BlockTextureFace.West,
                    new VoxelAddress(a.bodyId,a.face,a.u-1,a.v,a.radial),sampler);
                AddIfVisible(target,a,state,definition,BlockTextureFace.East,
                    new VoxelAddress(a.bodyId,a.face,a.u+1,a.v,a.radial),sampler);
                AddIfVisible(target,a,state,definition,BlockTextureFace.South,
                    new VoxelAddress(a.bodyId,a.face,a.u,a.v-1,a.radial),sampler);
                AddIfVisible(target,a,state,definition,BlockTextureFace.North,
                    new VoxelAddress(a.bodyId,a.face,a.u,a.v+1,a.radial),sampler);
            }
            return new SectionMeshData { key=section.key,requestVersion=requestVersion,section=section,
                opaque=opaque.ToPayload(),water=water.ToPayload() };
        }

        private void AddIfVisible(Builder builder,VoxelAddress address,BlockState state,
            BlockDefinition definition,BlockTextureFace face,VoxelAddress rawNeighbor,
            Func<VoxelAddress,BlockState> sampler)
        {
            VoxelAddress neighborAddress=CubeSphereMapper.Canonicalize(
                rawNeighbor,settings.faceCellResolution);
            BlockState neighbor=sampler(neighborAddress);
            BlockDefinition neighborDefinition=BlockRegistry.Get(neighbor.BlockId);
            bool visible;
            if(definition.renderLayer==BlockRenderLayer.Water)
                visible=neighborDefinition.renderLayer!=BlockRenderLayer.Water;
            else if(definition.renderLayer==BlockRenderLayer.Transparent)
                visible=neighbor.BlockId!=state.BlockId;
            else
                visible=!neighborDefinition.solid
                    || neighborDefinition.renderLayer==BlockRenderLayer.Transparent
                    || neighborDefinition.renderLayer==BlockRenderLayer.Water;
            if(visible) AddFace(builder,address,state,definition,face);
        }

        private void AddFace(Builder builder,VoxelAddress address,BlockState state,
            BlockDefinition definition,BlockTextureFace face)
        {
            VoxelBlockFrame f=VoxelBlockGeometry.Calculate(address,settings);
            Vector3 e=f.east*f.halfEast,n=f.north*f.halfNorth,r=f.radial*f.halfRadial,c=f.center;
            Vector3 a,b,q,d,normal; float shade;
            switch(face)
            {
                case BlockTextureFace.Outer:
                    a=c-e-n+r;b=c-e+n+r;q=c+e+n+r;d=c+e-n+r;normal=f.radial;shade=1f;break;
                case BlockTextureFace.Inner:
                    a=c-e-n-r;b=c+e-n-r;q=c+e+n-r;d=c-e+n-r;normal=-f.radial;shade=.56f;break;
                case BlockTextureFace.West:
                    a=c-e-n-r;b=c-e+n-r;q=c-e+n+r;d=c-e-n+r;normal=-f.east;shade=.76f;break;
                case BlockTextureFace.East:
                    a=c+e-n-r;b=c+e-n+r;q=c+e+n+r;d=c+e+n-r;normal=f.east;shade=.86f;break;
                case BlockTextureFace.South:
                    a=c-e-n-r;b=c-e-n+r;q=c+e-n+r;d=c+e-n-r;normal=-f.north;shade=.70f;break;
                default:
                    a=c-e+n-r;b=c+e+n-r;q=c+e+n+r;d=c-e+n+r;normal=f.north;shade=.81f;break;
            }
            int tile=definition.GetTextureTile(face,state.Orientation);
            Color32 color=Shade(definition.GetFallbackColor(face),shade,definition.renderLayer);
            AddQuad(builder,a,b,q,d,normal,color,tile);
        }

        private static Color32 Shade(Color32 source,float shade,BlockRenderLayer layer)
        {
            if(layer!=BlockRenderLayer.Water && layer!=BlockRenderLayer.Transparent)
            {
                byte v=(byte)Mathf.Clamp(Mathf.RoundToInt(255f*shade),0,255);
                return new Color32(v,v,v,255);
            }
            return new Color32((byte)Mathf.Clamp(Mathf.RoundToInt(source.r*shade),0,255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.g*shade),0,255),
                (byte)Mathf.Clamp(Mathf.RoundToInt(source.b*shade),0,255),source.a);
        }

        private static void AddQuad(Builder builder,Vector3 a,Vector3 b,Vector3 c,Vector3 d,
            Vector3 desiredNormal,Color32 color,int tile)
        {
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),desiredNormal)<0f)
            { Vector3 swap=b;b=d;d=swap; }
            int start=builder.vertices.Count;
            builder.vertices.Add(a);builder.vertices.Add(b);builder.vertices.Add(c);builder.vertices.Add(d);
            for(int i=0;i<4;i++){builder.normals.Add(desiredNormal);builder.colors.Add(color);}
            Rect rect=BlockRegistry.TileUv(tile);const float inset=.0015f;
            float x0=rect.xMin+inset,x1=rect.xMax-inset,y0=rect.yMin+inset,y1=rect.yMax-inset;
            builder.uv.Add(new Vector2(x0,y0));builder.uv.Add(new Vector2(x0,y1));
            builder.uv.Add(new Vector2(x1,y1));builder.uv.Add(new Vector2(x1,y0));
            builder.triangles.Add(start);builder.triangles.Add(start+1);builder.triangles.Add(start+2);
            builder.triangles.Add(start);builder.triangles.Add(start+2);builder.triangles.Add(start+3);
        }
    }
}
