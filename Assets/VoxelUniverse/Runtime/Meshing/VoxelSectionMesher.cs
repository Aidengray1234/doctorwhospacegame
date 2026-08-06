using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Meshing
{
    public sealed class VoxelSectionMesher
    {
        private enum FaceDirection
        {
            Outer,
            Inner,
            West,
            East,
            South,
            North
        }

        private sealed class Builder
        {
            public readonly List<Vector3> vertices = new List<Vector3>(4096);
            public readonly List<Vector3> normals = new List<Vector3>(4096);
            public readonly List<Vector2> uv = new List<Vector2>(4096);
            public readonly List<Color32> colors = new List<Color32>(4096);
            public readonly List<int> triangles = new List<int>(6144);

            public MeshPayload ToPayload()
            {
                return new MeshPayload
                {
                    vertices = vertices.ToArray(),
                    normals = normals.ToArray(),
                    uv = uv.ToArray(),
                    colors = colors.ToArray(),
                    triangles = triangles.ToArray()
                };
            }
        }

        private readonly VoxelUniverseSettings settings;

        public VoxelSectionMesher(VoxelUniverseSettings runtimeSettings)
        {
            settings = runtimeSettings;
        }

        public SectionMeshData Build(VoxelSection section, int requestVersion, Func<VoxelAddress, BlockState> sampler)
        {
            Builder opaque = new Builder();
            Builder water = new Builder();

            for (int y = 0; y < VoxelConstants.SectionSize; y++)
            {
                for (int z = 0; z < VoxelConstants.SectionSize; z++)
                {
                    for (int x = 0; x < VoxelConstants.SectionSize; x++)
                    {
                        BlockState state = section.GetLocal(x, y, z);
                        BlockDefinition definition = BlockRegistry.Get(state.BlockId);
                        if (definition.renderLayer == BlockRenderLayer.None) continue;

                        VoxelAddress address = section.ToAddress(x, y, z);
                        Builder target = definition.renderLayer == BlockRenderLayer.Water ? water : opaque;

                        AddIfVisible(target, address, state, definition, FaceDirection.Outer,
                            new VoxelAddress(address.bodyId, address.face, address.u, address.v, address.radial + 1), sampler);
                        AddIfVisible(target, address, state, definition, FaceDirection.Inner,
                            new VoxelAddress(address.bodyId, address.face, address.u, address.v, address.radial - 1), sampler);
                        AddIfVisible(target, address, state, definition, FaceDirection.West,
                            new VoxelAddress(address.bodyId, address.face, address.u - 1, address.v, address.radial), sampler);
                        AddIfVisible(target, address, state, definition, FaceDirection.East,
                            new VoxelAddress(address.bodyId, address.face, address.u + 1, address.v, address.radial), sampler);
                        AddIfVisible(target, address, state, definition, FaceDirection.South,
                            new VoxelAddress(address.bodyId, address.face, address.u, address.v - 1, address.radial), sampler);
                        AddIfVisible(target, address, state, definition, FaceDirection.North,
                            new VoxelAddress(address.bodyId, address.face, address.u, address.v + 1, address.radial), sampler);
                    }
                }
            }

            return new SectionMeshData
            {
                key = section.key,
                requestVersion = requestVersion,
                section = section,
                opaque = opaque.ToPayload(),
                water = water.ToPayload()
            };
        }

        private void AddIfVisible(
            Builder builder,
            VoxelAddress address,
            BlockState state,
            BlockDefinition definition,
            FaceDirection faceDirection,
            VoxelAddress rawNeighbor,
            Func<VoxelAddress, BlockState> sampler)
        {
            VoxelAddress neighborAddress = CubeSphereMapper.Canonicalize(rawNeighbor, settings.faceCellResolution);
            BlockState neighbor = sampler(neighborAddress);
            BlockDefinition neighborDefinition = BlockRegistry.Get(neighbor.BlockId);

            bool visible;
            if (definition.renderLayer == BlockRenderLayer.Water)
                visible = neighborDefinition.renderLayer != BlockRenderLayer.Water;
            else if (definition.renderLayer == BlockRenderLayer.Transparent)
                visible = neighbor.BlockId != state.BlockId;
            else
                visible = !neighborDefinition.solid || neighborDefinition.renderLayer == BlockRenderLayer.Transparent
                          || neighborDefinition.renderLayer == BlockRenderLayer.Water;

            if (!visible) return;
            AddFace(builder, address, definition, faceDirection);
        }

        private void AddFace(Builder builder, VoxelAddress address, BlockDefinition definition, FaceDirection face)
        {
            int resolution = settings.faceCellResolution;
            float innerRadius = settings.groundRadius + address.radial;
            float outerRadius = innerRadius + 1f;

            Vector3 d00 = CubeSphereMapper.GridPointDirection(address.face, address.u, address.v, resolution).ToVector3();
            Vector3 d10 = CubeSphereMapper.GridPointDirection(address.face, address.u + 1, address.v, resolution).ToVector3();
            Vector3 d11 = CubeSphereMapper.GridPointDirection(address.face, address.u + 1, address.v + 1, resolution).ToVector3();
            Vector3 d01 = CubeSphereMapper.GridPointDirection(address.face, address.u, address.v + 1, resolution).ToVector3();

            Vector3 i00 = d00 * innerRadius;
            Vector3 i10 = d10 * innerRadius;
            Vector3 i11 = d11 * innerRadius;
            Vector3 i01 = d01 * innerRadius;
            Vector3 o00 = d00 * outerRadius;
            Vector3 o10 = d10 * outerRadius;
            Vector3 o11 = d11 * outerRadius;
            Vector3 o01 = d01 * outerRadius;

            FaceBasis basis = CubeSphereMapper.GetCellTangentBasis(
                address.face, address.u, address.v, resolution);
            Vector3 radial = basis.normal.ToVector3().normalized;
            Vector3 east = basis.east.ToVector3().normalized;
            Vector3 north = basis.north.ToVector3().normalized;

            switch (face)
            {
                case FaceDirection.Outer:
                    AddQuad(builder, o00, o01, o11, o10, radial, definition.topColor);
                    break;
                case FaceDirection.Inner:
                    AddQuad(builder, i00, i10, i11, i01, -radial, definition.bottomColor);
                    break;
                case FaceDirection.West:
                    AddQuad(builder, i00, i01, o01, o00, -east, definition.sideColor);
                    break;
                case FaceDirection.East:
                    AddQuad(builder, i10, o10, o11, i11, east, definition.sideColor);
                    break;
                case FaceDirection.South:
                    AddQuad(builder, i00, o00, o10, i10, -north, definition.sideColor);
                    break;
                case FaceDirection.North:
                    AddQuad(builder, i01, i11, o11, o01, north, definition.sideColor);
                    break;
            }
        }

        private static void AddQuad(
            Builder builder,
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d,
            Vector3 desiredNormal,
            Color32 color)
        {
            Vector3 calculated = Vector3.Cross(b - a, c - a);
            if (Vector3.Dot(calculated, desiredNormal) < 0f)
            {
                Vector3 swap = b;
                b = d;
                d = swap;
            }

            int start = builder.vertices.Count;
            builder.vertices.Add(a);
            builder.vertices.Add(b);
            builder.vertices.Add(c);
            builder.vertices.Add(d);
            for (int i = 0; i < 4; i++)
            {
                builder.normals.Add(desiredNormal);
                builder.colors.Add(color);
            }
            builder.uv.Add(new Vector2(0f, 0f));
            builder.uv.Add(new Vector2(0f, 1f));
            builder.uv.Add(new Vector2(1f, 1f));
            builder.uv.Add(new Vector2(1f, 0f));
            builder.triangles.Add(start);
            builder.triangles.Add(start + 1);
            builder.triangles.Add(start + 2);
            builder.triangles.Add(start);
            builder.triangles.Add(start + 2);
            builder.triangles.Add(start + 3);
        }
    }
}
