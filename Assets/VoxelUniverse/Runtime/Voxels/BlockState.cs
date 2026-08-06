using System;

namespace DoctorWho.VoxelUniverse.Voxels
{
    [Serializable]
    public struct BlockState : IEquatable<BlockState>
    {
        public const ushort AirId = 0;

        [UnityEngine.SerializeField] private uint packed;

        public BlockState(ushort blockId, byte orientation, byte variant)
        {
            packed = blockId | ((uint)orientation << 16) | ((uint)variant << 24);
        }

        private BlockState(uint packedValue)
        {
            packed = packedValue;
        }

        public ushort BlockId
        {
            get { return (ushort)(packed & 0xFFFFu); }
        }

        public byte Orientation
        {
            get { return (byte)((packed >> 16) & 0xFFu); }
        }

        public byte Variant
        {
            get { return (byte)((packed >> 24) & 0xFFu); }
        }

        public uint Packed
        {
            get { return packed; }
        }

        public bool IsAir
        {
            get { return BlockId == AirId; }
        }

        public static BlockState Air
        {
            get { return new BlockState(AirId, 0, 0); }
        }

        public static BlockState FromPacked(uint value)
        {
            return new BlockState(value);
        }

        public BlockState WithOrientation(byte orientation)
        {
            return new BlockState(BlockId, orientation, Variant);
        }

        public BlockState WithVariant(byte variant)
        {
            return new BlockState(BlockId, Orientation, variant);
        }

        public bool Equals(BlockState other)
        {
            return packed == other.packed;
        }

        public override bool Equals(object obj)
        {
            return obj is BlockState && Equals((BlockState)obj);
        }

        public override int GetHashCode()
        {
            return (int)packed;
        }

        public static bool operator ==(BlockState left, BlockState right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BlockState left, BlockState right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return string.Format("Block {0}, orientation {1}, variant {2}", BlockId, Orientation, Variant);
        }
    }
}
