using System;
using OpenTK.Mathematics;

namespace Voxel_Raytracer
{


    public static class VoxelUtil
    {

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
            out int hitId
        )
        {
            hitPos = camPos;
            hitId = -1;

            Vector3 pos = camPos;
            Vector3.Normalize( pos );
            Vector3 rd = Vector3.Normalize(camForward);

            // voxel position
            int mapX = (int)Math.Floor(pos.X);
            int mapY = (int)Math.Floor(pos.Y);
            int mapZ = (int)Math.Floor(pos.Z);

            int stepX = (rd.X > 0) ? 1 : -1;
            int stepY = (rd.Y > 0) ? 1 : -1;
            int stepZ = (rd.Z > 0) ? 1 : -1;

            float tMaxX = (rd.X != 0) ? ((stepX > 0 ? mapX + 1 - pos.X : pos.X - mapX) / MathF.Abs(rd.X)) : float.MaxValue;
            float tMaxY = (rd.Y != 0) ? ((stepY > 0 ? mapY + 1 - pos.Y : pos.Y - mapY) / MathF.Abs(rd.Y)) : float.MaxValue;
            float tMaxZ = (rd.Z != 0) ? ((stepZ > 0 ? mapZ + 1 - pos.Z : pos.Z - mapZ) / MathF.Abs(rd.Z)) : float.MaxValue;

            float tDeltaX = (rd.X != 0) ? 1 / MathF.Abs(rd.X) : float.MaxValue;
            float tDeltaY = (rd.Y != 0) ? 1 / MathF.Abs(rd.Y) : float.MaxValue;
            float tDeltaZ = (rd.Z != 0) ? 1 / MathF.Abs(rd.Z) : float.MaxValue;

            float traveled = 0f;

            for (int i = 0; i < maxSteps && traveled < maxDistance; i++)
            {
                // fetch chunk
                int cx = mapX / chunkSize;
                int cy = mapY / chunkSize;
                int cz = mapZ / chunkSize;

                int localX = mapX % chunkSize;
                int localY = mapY % chunkSize;
                int localZ = mapZ % chunkSize;

                if (localX < 0) { localX += chunkSize; cx--; }
                if (localY < 0) { localY += chunkSize; cy--; }
                if (localZ < 0) { localZ += chunkSize; cz--; }

                // world bounds check
                if (cx < 0 || cy < 0 || cz < 0 || cx >= worldSize || cy >= worldSize || cz >= worldSize)
                    break;

                int cId = (cx * worldSize + cz) * worldSize + cy;
                Chunk chunk = chunks[cId];
                int vId = localZ + chunkSize * (localY + chunkSize * localX);
                int blockId = chunk.voxelData[vId];

                bool isGrass = blockId == 128 || blockId == 129 || blockId == 130;
                if (blockId < 251 && (!ignoreGrass || !isGrass))
                {
                    hitPos = new Vector3(mapX, mapY, mapZ);
                    hitId = blockId;
                    return true;
                }

                // step along DDA
                if (tMaxX < tMaxY)
                {
                    if (tMaxX < tMaxZ) { mapX += stepX; traveled = tMaxX; tMaxX += tDeltaX; }
                    else { mapZ += stepZ; traveled = tMaxZ; tMaxZ += tDeltaZ; }
                }
                else
                {
                    if (tMaxY < tMaxZ) { mapY += stepY; traveled = tMaxY; tMaxY += tDeltaY; }
                    else { mapZ += stepZ; traveled = tMaxZ; tMaxZ += tDeltaZ; }
                }
            }

            return false;
        }
    }
}