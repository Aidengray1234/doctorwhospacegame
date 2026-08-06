using System;
using UnityEngine;

namespace DoctorWho.Planets
{
    [Serializable]
    public readonly struct VoxelChunkCoord : IEquatable<VoxelChunkCoord>
    {
        public readonly int x;
        public readonly int y;
        public readonly int z;

        public VoxelChunkCoord(int x, int y, int z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public static VoxelChunkCoord FromWorld(Vector3 worldPosition, float chunkWorldSize)
        {
            return new VoxelChunkCoord(
                Mathf.FloorToInt(worldPosition.x / chunkWorldSize),
                Mathf.FloorToInt(worldPosition.y / chunkWorldSize),
                Mathf.FloorToInt(worldPosition.z / chunkWorldSize));
        }

        public Vector3 ToWorldOrigin(float chunkWorldSize) => new Vector3(x, y, z) * chunkWorldSize;
        public bool Equals(VoxelChunkCoord other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is VoxelChunkCoord other && Equals(other);
        public override int GetHashCode() => unchecked((x * 73856093) ^ (y * 19349663) ^ (z * 83492791));
        public override string ToString() => $"({x}, {y}, {z})";
    }
}
