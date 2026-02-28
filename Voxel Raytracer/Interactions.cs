using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Voxel_Raytracer
{
    public class Interactions
    {

        private static Config config => Config.Instance;
        static int chunkSize => config.chunkSize;

        public static void HandleBlockSelection(KeyboardState input)
        {

            byte selected = Renderer.selectedBlock;

            if (input.IsKeyDown(Keys.D1))
                selected = 8;
            else if (input.IsKeyDown(Keys.D2))
                selected = 7;
            else if (input.IsKeyDown(Keys.D3))
                selected = 3;
            else if (input.IsKeyDown(Keys.D4))
                selected = 4;
            else if (input.IsKeyDown(Keys.D5))
                selected = 5;
            else if (input.IsKeyDown(Keys.D6))
                selected = 6;
            else if (input.IsKeyDown(Keys.D7))
                selected = 7;
            else if (input.IsKeyDown(Keys.D8))
                selected = 8;
            else if (input.IsKeyDown(Keys.D9))
                selected = 9;
            else if (input.IsKeyDown(Keys.D0))
                selected = 0;


            Renderer.selectedBlock = selected;
        }

        public static void PlacedEvent(byte blockId, int voxelId, int chunkId)
        {

            int vx, vy, vz;
            VoxelUtil.VoxelXYZfromVoxelID(voxelId, out vx, out vy, out vz);
            int cx, cy, cz;
            VoxelUtil.ChunkXYZfromChunkID(chunkId, out cx, out cy, out cz);

            Console.WriteLine($"placed {blockId} at ({vx}, {vy}, {vz}) in chunk {chunkId}");


            switch (blockId)
            {
                case 8: DetonatorPlaced(vx, vy, vz, cx, cy, cz); break; // detonator
                default: break;
            }
        }

        public static void RemovedEvent(byte blockId, int voxelId, int chunkId)
        {

            int vx, vy, vz;
            VoxelUtil.VoxelXYZfromVoxelID(voxelId, out vx, out vy, out vz);
            int cx, cy, cz;
            VoxelUtil.ChunkXYZfromChunkID(chunkId, out cx, out cy, out cz);

            //Console.WriteLine($"removed {blockId} at ({vx}, {vy}, {vz}) in chunk {chunkId}");
        }



        public static void DetonatorPlaced(int vx, int vy, int vz, int cx, int cy, int cz)
        {
            var neighbors = new (int x, int y, int z)[]
            {
                (vx + 1, vy, vz),
                (vx - 1, vy, vz),
                (vx, vy + 1, vz),
                (vx, vy - 1, vz),
                (vx, vy, vz + 1),
                (vx, vy, vz - 1)
            };

            foreach (var neighbor in neighbors)
            {
                int cModX = neighbor.x >= chunkSize ? 1 : (neighbor.x < 0 ? -1 : 0);
                int cModY = neighbor.y >= chunkSize ? 1 : (neighbor.y < 0 ? -1 : 0);
                int cModZ = neighbor.z >= chunkSize ? 1 : (neighbor.z < 0 ? -1 : 0);

                int rx = neighbor.x >= chunkSize ? neighbor.x - chunkSize : (neighbor.x < 0 ? neighbor.x + chunkSize : neighbor.x);
                int ry = neighbor.y >= chunkSize ? neighbor.y - chunkSize : (neighbor.y < 0 ? neighbor.y + chunkSize : neighbor.y);
                int rz = neighbor.z >= chunkSize ? neighbor.z - chunkSize : (neighbor.z < 0 ? neighbor.z + chunkSize : neighbor.z);
                
                byte neighborBlockId = VoxelUtil.BlockIDAtXYZ(cx + cModX, cy + cModY, cz + cModZ, rx, ry, rz);
                
                if (neighborBlockId == 7)
                {
                    C4Activated(VoxelUtil.VoxelIDfromXYZ(rx, ry, rz), VoxelUtil.ChunkIDfromXYZ(cx+cModX, cy+cModY, cz+cModZ));
                }
            }
        }

        public static void C4Activated(int voxelId, int chunkId)
        {
            Renderer.performFullSVO = true; // force full SVO rebuild, prevent partial svo optimization from being used
            int radius = 30;

            int vx, vy, vz;
            VoxelUtil.VoxelXYZfromVoxelID(voxelId, out vx, out vy, out vz);

            int cx, cy, cz;
            VoxelUtil.ChunkXYZfromChunkID(chunkId, out cx, out cy, out cz);

            int r = radius / 2;
            int rSq = r * r;

            for (int x = vx - r; x < vx + r; x++)
                for (int y = vy - r; y < vy + r; y++)
                    for (int z = vz - r; z < vz + r; z++)
                    {
                        int dx = x - vx;
                        int dy = y - vy;
                        int dz = z - vz;

                        // sphere check
                        if (dx * dx + dy * dy + dz * dz > rSq)
                            continue;

                        int cModX = x >= chunkSize ? 1 : (x < 0 ? -1 : 0);
                        int cModY = y >= chunkSize ? 1 : (y < 0 ? -1 : 0);
                        int cModZ = z >= chunkSize ? 1 : (z < 0 ? -1 : 0);

                        int rx = x >= chunkSize ? x - chunkSize : (x < 0 ? x + chunkSize : x);
                        int ry = y >= chunkSize ? y - chunkSize : (y < 0 ? y + chunkSize : y);
                        int rz = z >= chunkSize ? z - chunkSize : (z < 0 ? z + chunkSize : z);

                        int newCId = VoxelUtil.ChunkIDfromXYZ(cx + cModX, cy + cModY, cz + cModZ);
                        int newVId = VoxelUtil.VoxelIDfromXYZ(rx, ry, rz);


                        if (newCId < 0 || newCId >= Renderer.ActiveChunks.Length || newVId < 0 || newVId >= chunkSize * chunkSize * chunkSize)
                            return; // out of bounds, ignore
                        byte oldBlock = Renderer.ActiveChunks[newCId].voxelData[newVId];

                        VoxelUtil.Destroy(newCId, newVId);

                        if (oldBlock == 7)
                            C4Activated(newVId, newCId);  // chain reaction



                    }

            Console.WriteLine($"C4 detonated at {voxelId}");
        }
    }
}
