using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Core;

namespace DoctorWho.VoxelUniverse.Voxels
{
    public sealed class PackedVoxelSection
    {
        private readonly object sync = new object();
        private readonly List<BlockState> palette = new List<BlockState>();
        private ulong[] packedIndices = new ulong[0];
        private int bitsPerIndex;
        private int version;

        public PackedVoxelSection() : this(BlockState.Air) { }

        public PackedVoxelSection(BlockState initialState)
        {
            palette.Add(initialState);
        }

        public int Version { get { lock (sync) return version; } }
        public int PaletteCount { get { lock (sync) return palette.Count; } }
        public int EstimatedDataBytes { get { lock (sync) return palette.Count * sizeof(uint) + packedIndices.Length * sizeof(ulong); } }

        public BlockState Get(int localX, int localY, int localZ)
        {
            return Get(VoxelConstants.ToIndex(localX, localY, localZ));
        }

        public BlockState Get(int index)
        {
            ValidateIndex(index);
            lock (sync)
            {
                int paletteIndex = ReadIndex(index, bitsPerIndex, packedIndices);
                return palette[paletteIndex];
            }
        }

        public bool Set(int localX, int localY, int localZ, BlockState state)
        {
            return Set(VoxelConstants.ToIndex(localX, localY, localZ), state);
        }

        public bool Set(int index, BlockState state)
        {
            ValidateIndex(index);
            lock (sync)
            {
                int currentPaletteIndex = ReadIndex(index, bitsPerIndex, packedIndices);
                if (palette[currentPaletteIndex] == state) return false;
                int paletteIndex = palette.IndexOf(state);
                if (paletteIndex < 0)
                {
                    paletteIndex = palette.Count;
                    palette.Add(state);
                    int requiredBits = Math.Max(1, IntegerMath.CeilLog2(palette.Count));
                    if (requiredBits != bitsPerIndex) Repack(requiredBits);
                }
                WriteIndex(index, paletteIndex, bitsPerIndex, packedIndices);
                version++;
                return true;
            }
        }

        public Snapshot CreateSnapshot()
        {
            lock (sync)
            {
                return new Snapshot(version, bitsPerIndex, palette.ToArray(), (ulong[])packedIndices.Clone());
            }
        }

        public sealed class Snapshot
        {
            private readonly int snapshotBitsPerIndex;
            private readonly BlockState[] snapshotPalette;
            private readonly ulong[] snapshotPackedIndices;

            internal Snapshot(int snapshotVersion, int bits, BlockState[] states, ulong[] words)
            {
                Version = snapshotVersion;
                snapshotBitsPerIndex = bits;
                snapshotPalette = states;
                snapshotPackedIndices = words;
            }

            public int Version { get; private set; }
            public int PaletteCount { get { return snapshotPalette.Length; } }

            public BlockState Get(int localX, int localY, int localZ)
            {
                int index = VoxelConstants.ToIndex(localX, localY, localZ);
                return snapshotPalette[ReadIndex(index, snapshotBitsPerIndex, snapshotPackedIndices)];
            }
        }

        private void Repack(int newBitsPerIndex)
        {
            ulong[] previous = packedIndices;
            int previousBits = bitsPerIndex;
            bitsPerIndex = newBitsPerIndex;
            packedIndices = new ulong[WordCount(newBitsPerIndex)];
            for (int i = 0; i < VoxelConstants.SectionVolume; i++)
            {
                WriteIndex(i, ReadIndex(i, previousBits, previous), bitsPerIndex, packedIndices);
            }
        }

        private static int WordCount(int bits)
        {
            if (bits == 0) return 0;
            long bitCount = (long)VoxelConstants.SectionVolume * bits;
            return (int)((bitCount + 63L) / 64L);
        }

        private static int ReadIndex(int index, int bits, ulong[] words)
        {
            if (bits == 0) return 0;
            long bitOffset = (long)index * bits;
            int wordIndex = (int)(bitOffset >> 6);
            int shift = (int)(bitOffset & 63L);
            ulong mask = (1UL << bits) - 1UL;
            ulong value = words[wordIndex] >> shift;
            int spill = shift + bits - 64;
            if (spill > 0) value |= words[wordIndex + 1] << (bits - spill);
            return (int)(value & mask);
        }

        private static void WriteIndex(int index, int value, int bits, ulong[] words)
        {
            if (bits == 0) return;
            long bitOffset = (long)index * bits;
            int wordIndex = (int)(bitOffset >> 6);
            int shift = (int)(bitOffset & 63L);
            ulong mask = (1UL << bits) - 1UL;
            ulong encoded = (ulong)value & mask;
            words[wordIndex] = (words[wordIndex] & ~(mask << shift)) | (encoded << shift);
            int spill = shift + bits - 64;
            if (spill > 0)
            {
                ulong spillMask = (1UL << spill) - 1UL;
                words[wordIndex + 1] = (words[wordIndex + 1] & ~spillMask) | (encoded >> (bits - spill));
            }
        }

        private static void ValidateIndex(int index)
        {
            if (index < 0 || index >= VoxelConstants.SectionVolume) throw new ArgumentOutOfRangeException("index");
        }
    }
}
