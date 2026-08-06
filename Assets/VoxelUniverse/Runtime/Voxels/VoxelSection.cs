using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public sealed class VoxelSection
    {
        public readonly SectionKey key;
        public readonly PackedVoxelSection blocks;
        public readonly byte[] skyLight;
        public readonly byte[] blockLight;
        public readonly int generationVersion;

        public VoxelSection(SectionKey sectionKey, int version)
        {
            key = sectionKey;
            generationVersion = version;
            blocks = new PackedVoxelSection(BlockState.Air);
            skyLight = new byte[VoxelConstants.SectionVolume];
            blockLight = new byte[VoxelConstants.SectionVolume];
        }

        public BlockState GetLocal(int x, int y, int z)
        {
            return blocks.Get(x, y, z);
        }

        public void SetLocal(int x, int y, int z, BlockState state)
        {
            blocks.Set(x, y, z, state);
        }

        public VoxelAddress ToAddress(int localX, int localY, int localZ)
        {
            return new VoxelAddress(
                key.bodyId,
                key.face,
                key.sectionU * VoxelConstants.SectionSize + localX,
                key.sectionV * VoxelConstants.SectionSize + localZ,
                key.sectionRadial * VoxelConstants.SectionSize + localY);
        }

        public int EstimatedBytes
        {
            get { return blocks.EstimatedDataBytes + skyLight.Length + blockLight.Length; }
        }
    }
}
