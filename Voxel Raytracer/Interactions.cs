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
                selected = 1;
            else if (input.IsKeyDown(Keys.D2))
                selected = 2;
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
                case 8: CustomBlockEvents.DetonatorPlaced(vx, vy, vz, cx, cy, cz); break; // detonator
                default: break;
            }
        }

        public static void RemovedEvent(byte blockId, int voxelId, int chunkId)
        {

            int vx, vy, vz;
            VoxelUtil.VoxelXYZfromVoxelID(voxelId, out vx, out vy, out vz);
            int cx, cy, cz;
            VoxelUtil.ChunkXYZfromChunkID(chunkId, out cx, out cy, out cz);

            switch (blockId)
            {
                case 2: CustomBlockEvents.GrassBlockDestroyed(vx, vy, vz, cx, cy, cz); break; // grass
                default: break;
            }

            //Console.WriteLine($"removed {blockId} at ({vx}, {vy}, {vz}) in chunk {chunkId}");
        }




    }
}
