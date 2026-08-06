using System;
using UnityEngine;

namespace DoctorWho.BlockPlanets
{
    public enum BlockPlanetFace : byte
    {
        PositiveX = 0,
        NegativeX = 1,
        PositiveY = 2,
        NegativeY = 3,
        PositiveZ = 4,
        NegativeZ = 5
    }

    [Serializable]
    public struct BlockChunkCoord : IEquatable<BlockChunkCoord>
    {
        public BlockPlanetFace face;
        public int x;
        public int y;
        public int z;

        public BlockChunkCoord(BlockPlanetFace face, int x, int y, int z)
        {
            this.face = face;
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(BlockChunkCoord other) => face == other.face && x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is BlockChunkCoord other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)face;
                hash = hash * 397 ^ x;
                hash = hash * 397 ^ y;
                hash = hash * 397 ^ z;
                return hash;
            }
        }

        public override string ToString() => $"{face}_Chunk_{x}_{y}_{z}";
    }

    [Serializable]
    public struct BlockAddress : IEquatable<BlockAddress>
    {
        public BlockPlanetFace face;
        public int x;
        public int y;
        public int z;

        public BlockAddress(BlockPlanetFace face, int x, int y, int z)
        {
            this.face = face;
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public bool Equals(BlockAddress other) => face == other.face && x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is BlockAddress other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)face;
                hash = hash * 397 ^ x;
                hash = hash * 397 ^ y;
                hash = hash * 397 ^ z;
                return hash;
            }
        }
    }

    public static class BlockPlanetMath
    {
        public static int FloorDiv(int value, int divisor)
        {
            int quotient = value / divisor;
            int remainder = value % divisor;
            if (remainder != 0 && ((remainder < 0) != (divisor < 0))) quotient--;
            return quotient;
        }

        public static int PositiveMod(int value, int divisor)
        {
            int result = value % divisor;
            return result < 0 ? result + Mathf.Abs(divisor) : result;
        }

        public static Vector3 FaceUvToCube(BlockPlanetFace face, float u, float v)
        {
            switch (face)
            {
                case BlockPlanetFace.PositiveX: return new Vector3(1f, v, -u);
                case BlockPlanetFace.NegativeX: return new Vector3(-1f, v, u);
                case BlockPlanetFace.PositiveY: return new Vector3(u, 1f, -v);
                case BlockPlanetFace.NegativeY: return new Vector3(u, -1f, v);
                case BlockPlanetFace.PositiveZ: return new Vector3(u, v, 1f);
                default: return new Vector3(-u, v, -1f);
            }
        }

        // The equal-area style cube-to-sphere mapping used by the supplied Planetcraft base.
        public static Vector3 CubeToSphere(Vector3 p)
        {
            float x2 = p.x * p.x;
            float y2 = p.y * p.y;
            float z2 = p.z * p.z;
            Vector3 result = new Vector3(
                p.x * Mathf.Sqrt(Mathf.Max(0f, 1f - y2 * 0.5f - z2 * 0.5f + y2 * z2 / 3f)),
                p.y * Mathf.Sqrt(Mathf.Max(0f, 1f - z2 * 0.5f - x2 * 0.5f + z2 * x2 / 3f)),
                p.z * Mathf.Sqrt(Mathf.Max(0f, 1f - x2 * 0.5f - y2 * 0.5f + x2 * y2 / 3f)));
            return result.normalized;
        }

        public static Vector3 FaceUvToDirection(BlockPlanetFace face, float u, float v)
            => CubeToSphere(FaceUvToCube(face, u, v));

        public static Vector3 GridPoint(BlockPlanetFace face, float x, float radialY, float z, int faceResolution, float radius)
        {
            float u = x / faceResolution * 2f - 1f;
            float v = z / faceResolution * 2f - 1f;
            return FaceUvToDirection(face, u, v) * (radius + radialY);
        }

        public static void DirectionToFaceUv(Vector3 direction, out BlockPlanetFace face, out float u, out float v)
        {
            direction.Normalize();
            float ax = Mathf.Abs(direction.x);
            float ay = Mathf.Abs(direction.y);
            float az = Mathf.Abs(direction.z);

            if (ax >= ay && ax >= az)
            {
                if (direction.x >= 0f)
                {
                    face = BlockPlanetFace.PositiveX;
                    u = -direction.z / ax;
                    v = direction.y / ax;
                }
                else
                {
                    face = BlockPlanetFace.NegativeX;
                    u = direction.z / ax;
                    v = direction.y / ax;
                }
            }
            else if (ay >= ax && ay >= az)
            {
                if (direction.y >= 0f)
                {
                    face = BlockPlanetFace.PositiveY;
                    u = direction.x / ay;
                    v = -direction.z / ay;
                }
                else
                {
                    face = BlockPlanetFace.NegativeY;
                    u = direction.x / ay;
                    v = direction.z / ay;
                }
            }
            else
            {
                if (direction.z >= 0f)
                {
                    face = BlockPlanetFace.PositiveZ;
                    u = direction.x / az;
                    v = direction.y / az;
                }
                else
                {
                    face = BlockPlanetFace.NegativeZ;
                    u = -direction.x / az;
                    v = direction.y / az;
                }
            }
        }

        public static void DirectionToCell(Vector3 direction, int faceResolution, out BlockPlanetFace face, out int x, out int z)
        {
            DirectionToFaceUv(direction, out face, out float u, out float v);
            x = Mathf.Clamp(Mathf.FloorToInt((u * 0.5f + 0.5f) * faceResolution), 0, faceResolution - 1);
            z = Mathf.Clamp(Mathf.FloorToInt((v * 0.5f + 0.5f) * faceResolution), 0, faceResolution - 1);
        }

        public static void NormalizeCell(ref BlockPlanetFace face, ref int x, ref int z, int faceResolution)
        {
            if (x >= 0 && x < faceResolution && z >= 0 && z < faceResolution) return;
            float u = (x + 0.5f) / faceResolution * 2f - 1f;
            float v = (z + 0.5f) / faceResolution * 2f - 1f;
            Vector3 direction = FaceUvToDirection(face, u, v);
            DirectionToCell(direction, faceResolution, out face, out x, out z);
        }

        public static BlockAddress WorldToBlock(Vector3 worldPosition, Vector3 planetCenter, BlockPlanetSettings settings)
        {
            Vector3 local = worldPosition - planetCenter;
            float distance = local.magnitude;
            DirectionToCell(local, settings.faceResolution, out BlockPlanetFace face, out int x, out int z);
            int y = Mathf.FloorToInt(distance - settings.radius);
            return new BlockAddress(face, x, y, z);
        }

        public static BlockChunkCoord BlockToChunk(BlockAddress address, int chunkSize)
            => new BlockChunkCoord(address.face, FloorDiv(address.x, chunkSize), FloorDiv(address.y, chunkSize), FloorDiv(address.z, chunkSize));
    }
}
