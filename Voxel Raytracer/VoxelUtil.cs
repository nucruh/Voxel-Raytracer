using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.Security.Cryptography;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Voxel_Raytracer
{
    public static class VoxelUtil
    {

        private static Config config => Config.Instance;
        static int width => config.width;
        static int height => config.height;
        static int worldSize => config.worldSize;
        static int chunkSize => config.chunkSize;
        static int voxelsPerChunk = chunkSize * chunkSize * chunkSize;
        private const float playerWidth = 0.2f;
        private const float playerHeight = 0.2f;

        public static bool Raycast(
            Vector3 camPos,
            Vector3 camForward,
            Chunk[] chunks,
            int chunkSize,
            int worldSize,
            int maxSteps,
            float maxDistance,
            bool ignoreGrass,
            out Vector3 hitPos,
            out Vector3 hitNormal,
            out int hitId,
            out int vId,
            out int cId
        )
        {
            hitPos = Vector3.Zero;
            hitNormal = Vector3.Zero;
            hitId = -1;
            cId = -1;
            vId = -1;

            Vector3 pos = camPos;
            Vector3 rd = Vector3.Normalize(camForward);

            int mapX = (int)Math.Floor(pos.X);
            int mapY = (int)Math.Floor(pos.Y);
            int mapZ = (int)Math.Floor(pos.Z);

            int stepX = rd.X > 0 ? 1 : -1;
            int stepY = rd.Y > 0 ? 1 : -1;
            int stepZ = rd.Z > 0 ? 1 : -1;

            float tMaxX = rd.X != 0 ? ((stepX > 0 ? mapX + 1 - pos.X : pos.X - mapX) / MathF.Abs(rd.X)) : float.MaxValue;
            float tMaxY = rd.Y != 0 ? ((stepY > 0 ? mapY + 1 - pos.Y : pos.Y - mapY) / MathF.Abs(rd.Y)) : float.MaxValue;
            float tMaxZ = rd.Z != 0 ? ((stepZ > 0 ? mapZ + 1 - pos.Z : pos.Z - mapZ) / MathF.Abs(rd.Z)) : float.MaxValue;

            float tDeltaX = rd.X != 0 ? 1f / MathF.Abs(rd.X) : float.MaxValue;
            float tDeltaY = rd.Y != 0 ? 1f / MathF.Abs(rd.Y) : float.MaxValue;
            float tDeltaZ = rd.Z != 0 ? 1f / MathF.Abs(rd.Z) : float.MaxValue;

            float traveled = 0f;
            int lastAxis = -1; // 0=X, 1=Y, 2=Z

            for (int i = 0; i < maxSteps && traveled < maxDistance; i++)
            {
                int cx = mapX / chunkSize;
                int cy = mapY / chunkSize;
                int cz = mapZ / chunkSize;

                int localX = mapX % chunkSize;
                int localY = mapY % chunkSize;
                int localZ = mapZ % chunkSize;

                if (localX < 0) { localX += chunkSize; cx--; }
                if (localY < 0) { localY += chunkSize; cy--; }
                if (localZ < 0) { localZ += chunkSize; cz--; }

                if (cx < 0 || cy < 0 || cz < 0 ||
                    cx >= worldSize || cy >= worldSize || cz >= worldSize)
                    break;

                cId = (cx * worldSize + cz) * worldSize + cy;
                Chunk chunk = chunks[cId];

                vId = localZ + chunkSize * (localY + chunkSize * localX);
                int blockId = chunk.voxelData[vId];

                bool isGrass = blockId == 128 || blockId == 129 || blockId == 130;
                if (blockId < 251 && (!ignoreGrass || !isGrass))
                {
                    hitPos = camPos + rd * traveled;

                    // geometric normal (face hit)
                    Vector3 n = lastAxis switch
                    {
                        0 => new Vector3(-stepX, 0, 0),
                        1 => new Vector3(0, -stepY, 0),
                        2 => new Vector3(0, 0, -stepZ),
                        _ => Vector3.Zero
                    };

                    // ensure normal opposes ray (important for bounces)
                    hitNormal = n;
                    hitId = blockId;
                    return true;
                }

                // DDA step
                if (tMaxX < tMaxY)
                {
                    if (tMaxX < tMaxZ)
                    {
                        mapX += stepX;
                        traveled = tMaxX;
                        tMaxX += tDeltaX;
                        lastAxis = 0;
                    }
                    else
                    {
                        mapZ += stepZ;
                        traveled = tMaxZ;
                        tMaxZ += tDeltaZ;
                        lastAxis = 2;
                    }
                }
                else
                {
                    if (tMaxY < tMaxZ)
                    {
                        mapY += stepY;
                        traveled = tMaxY;
                        tMaxY += tDeltaY;
                        lastAxis = 1;
                    }
                    else
                    {
                        mapZ += stepZ;
                        traveled = tMaxZ;
                        tMaxZ += tDeltaZ;
                        lastAxis = 2;
                    }
                }
            }

            return false;
        }

        public static bool IsLocationSolid(Vector3 pos, Chunk[] chunks, bool lookForGrass)
        {

            int cx = (int)Math.Floor(pos.X / chunkSize);
            int cy = (int)Math.Floor(pos.Y / chunkSize);
            int cz = (int)Math.Floor(pos.Z / chunkSize);

            if (cx < 0 || cy < 0 || cz < 0 || cx >= worldSize || cy >= worldSize || cz >= worldSize)
                return true;

            int cId = (cx * worldSize + cz) * worldSize + cy;

            int vx = (int)Math.Floor(pos.X) % chunkSize;
            int vy = (int)Math.Floor(pos.Y) % chunkSize;
            int vz = (int)Math.Floor(pos.Z) % chunkSize;

            if (vx < 0) vx += chunkSize;
            if (vy < 0) vy += chunkSize;
            if (vz < 0) vz += chunkSize;

            int vId = vz + chunkSize * (vy + chunkSize * vx);

            int blockId = chunks[cId].voxelData[vId];
            bool isGrass = blockId == 128 || blockId == 129 || blockId == 130;
            if (!lookForGrass && isGrass)
                return false; // grass is non-solid by default

            return blockId < 251 && blockId != -1;
        }

        public static bool CollidesAt(Vector3 position, Chunk[] chunks, bool lookForGrass)
        {
            Vector3[] points = new Vector3[]
            {
                new Vector3(-playerWidth, 0, -playerWidth),
                new Vector3(-playerWidth, playerHeight, -playerWidth),
                new Vector3(playerWidth, 0, -playerWidth),
                new Vector3(playerWidth, playerHeight, -playerWidth),
                new Vector3(-playerWidth, 0, playerWidth),
                new Vector3(-playerWidth, playerHeight, playerWidth),
                new Vector3(playerWidth, 0, playerWidth),
                new Vector3(playerWidth, playerHeight, playerWidth)
            };

            foreach (var p in points)
            {
                if (IsLocationSolid(position + p, chunks, lookForGrass))
                    return true;
            }

            return false;
        }

        public static byte BlockIDAtID(int cId, int vId)
        {
            return Renderer.ActiveChunks[cId].voxelData[vId];
        }

        public static byte BlockIDAtXYZ(int cx, int cy, int cz, int x, int y, int z)
        {
            int cId = ChunkIDfromXYZ(cx, cy, cz);
            int vId = VoxelIDfromXYZ(x, y, z);
            return Renderer.ActiveChunks[cId].voxelData[vId];
        }

        public static (int, int, int) ChunkXYZfromChunkID(int cId, out int cx, out int cy, out int cz)
        {
            cx = cId / (worldSize * worldSize);
            cz = (cId / worldSize) % worldSize;
            cy = cId % worldSize;

            return (cx, cy, cz);
        }

        public static (int, int, int) VoxelXYZfromVoxelID(int vId, out int x, out int y, out int z)
        {
            x = vId / (chunkSize * chunkSize);
            y = (vId / chunkSize) % chunkSize;
            z = vId % chunkSize;
            return (x, y, z);
        }

        public static int ChunkIDfromXYZ(int cx, int cy, int cz)
        {
            return (cx * worldSize + cz) * worldSize + cy;
        }

        public static int VoxelIDfromXYZ(int x, int y, int z)
        {
            return z + chunkSize * (y + chunkSize * x);
        }



        public static void Destroy(int cId, int vId)
        {
            if (cId < 0 || cId >= Renderer.ActiveChunks.Length || vId < 0 || vId >= voxelsPerChunk)
                return; // out of bounds, ignore

            byte oldBlock = Renderer.ActiveChunks[cId].voxelData[vId];
            if (oldBlock == 251 || oldBlock == 252 || oldBlock == 253 || oldBlock == 254 || oldBlock == 255)
                return; // no need to remove air

            Renderer.ActiveChunks[cId].voxelData[vId] = 254;

            while (Renderer.updatedVoxelPositions.Count <= cId)
                Renderer.updatedVoxelPositions.Add(new List<int>());

            Renderer.updatedVoxelPositions[cId].Add(vId);

            if (!Renderer.chunksUpdated.Contains(cId))
                Renderer.chunksUpdated.Add(cId);

            Interactions.RemovedEvent(oldBlock, vId, cId);
        }

        public static void Place(int cId, int vx, int vy, int vz)
        {
            byte selectedBlock = Renderer.selectedBlock;

            int cx, cy, cz;
            VoxelUtil.ChunkXYZfromChunkID(cId, out cx, out cy, out cz);

            int cModX = vx >= chunkSize ? 1 : (vx < 0 ? -1 : 0);
            int cModY = vy >= chunkSize ? 1 : (vy < 0 ? -1 : 0);
            int cModZ = vz >= chunkSize ? 1 : (vz < 0 ? -1 : 0);

            vx = vx >= chunkSize ? vx - chunkSize : (vx < 0 ? vx + chunkSize : vx);
            vy = vy >= chunkSize ? vy - chunkSize : (vy < 0 ? vy + chunkSize : vy);
            vz = vz >= chunkSize ? vz - chunkSize : (vz < 0 ? vz + chunkSize : vz);


            int newVID = VoxelUtil.VoxelIDfromXYZ(vx, vy, vz);
            int newCID = VoxelUtil.ChunkIDfromXYZ(cx + cModX, cy + cModY, cz + cModZ);

            // expand list if needed
            while (Renderer.updatedVoxelPositions.Count <= newCID)
                Renderer.updatedVoxelPositions.Add(new List<int>());

            Renderer.ActiveChunks[newCID].voxelData[newVID] = selectedBlock;
            Renderer.updatedVoxelPositions[newCID].Add(newVID);
            Renderer.chunksUpdated.Add(newCID);

            Interactions.PlacedEvent(selectedBlock, newVID, newCID);
        }


    }
}