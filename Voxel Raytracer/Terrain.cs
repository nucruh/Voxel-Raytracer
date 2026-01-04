using OpenTK.Graphics.OpenGL;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace Voxel_Raytracer
{

    internal class Terrain
    {
        private Config config => Config.Instance;

        public float resolution => config.resolution;
        public double scale => config.scale;
        public double squishFactor => config.squishFactor;
        public double baseHeight => config.worldHeight * 0.2;
        public int chunkSize => config.chunkSize;


        static readonly string[] preset_birch = File.ReadAllLines("presets/birch.ply");
        static readonly string[] preset_fir = File.ReadAllLines("presets/alpine_fir.ply");
        public (byte[], List<int>) GenerateChunk(Vector3 chunkCoords)
        {

            // R, G, B, filled
            var result = new byte[chunkSize * chunkSize * chunkSize];
            List<int> outOfBounds = new List<int>();

            var surface_ids = new int[chunkSize * chunkSize];

            Array.Fill(result, (byte)254);
            int arraySize = result.Length;
            var perlin = new PerlinNoise(seed: 1234);

            Random rnd = new Random();

            Vector3 offset = new Vector3(chunkCoords.X * chunkSize, chunkCoords.Y * chunkSize, chunkCoords.Z * chunkSize);

            bool emptyChunk = true;

            for (int x = 0; x < chunkSize; x++)
                for (int z = 0; z < chunkSize; z++)
                {
                    double xzSquish = squishFactor * Math.Clamp(Math.Pow(perlin.Noise(x + offset.X, z + offset.Z, 1) + 1, 2), 0.5, 1.5);
                    double worldX = x + offset.X;
                    double worldZ = z + offset.Z;


                    double sampleX = (worldX / resolution) * scale;
                    double sampleZ = (worldZ / resolution) * scale;

                    int height = (int) Math.Round(perlin.FBM3D(sampleX, sampleZ, (1 / resolution) * scale, 1) * 52);
                    height -= (int)offset.Y;
                    height = Math.Max(height, 0);
                    int y = 0;

                    while (y < height)
                    {

                        if (y >= chunkSize) break;
                        //double lightDiff = 10 * rnd.NextDouble();
                        int baseIndex = (z + chunkSize * (y + chunkSize * x));
                        // solid materiala
                        result[baseIndex] = 0;
                        emptyChunk = false;
                        y++;
                    }
                    
                }


            if (emptyChunk)
            {
                //result[0] = 255;
                Array.Fill(result, (byte)255, 0, chunkSize * chunkSize * chunkSize);

                return (result, outOfBounds);
            }

            int grass_count = 0;

            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int baseIndex = (z + chunkSize * (y + chunkSize * x));

                        if (result[baseIndex] == 254) continue;

                        // check above
                        int above1 = (z + chunkSize * ((y + 1) + chunkSize * x));
                        int above2 = (z + chunkSize * ((y + 2) + chunkSize * x));

                        if (above1 >= arraySize || above2 >= arraySize) continue;
                        // keep blocks on high elevations stone
                        if (y + offset.Y > 150) continue;

                        // could maybe omit since not using 3d perlin anymore????? !
                        if (result[above1] == 254 && result[above2] == 254)
                        {
                            // make base block grass
                            result[baseIndex] = 2;
                            surface_ids[grass_count] = baseIndex;

                            grass_count++;
                            int below1 = (z + chunkSize * ((y - 1) + chunkSize * x));
                            int below2 = (z + chunkSize * ((y - 2) + chunkSize * x));
                            int below3 = (z + chunkSize * ((y - 3) + chunkSize * x));

                            // make bottom 3 dirt if possible (stone below)
                            if (y > 0 && result[below1] == 0) result[below1] = 1;
                            if (y > 1 && result[below2] == 0) result[below2] = 1;
                            if (y > 2 && result[below3] == 0) result[below3] = 1;
                        }
                        else
                            continue;

                        // make bottom ones dirt if they do exist

                    }

            Random treeRand = new Random((int)(chunkCoords.X * 24957 + chunkCoords.Y * 135 + chunkCoords.Z * 13581)); // deterministic per chunk


            foreach (var grassId in surface_ids)
            {
                int x = grassId / (chunkSize * chunkSize);
                int y = (grassId / chunkSize) % chunkSize;
                int z = grassId % chunkSize;

                double next = treeRand.NextDouble();

                if (next < 0.0065)
                {
                    string[] content;



                    if (y + chunkCoords.Y * chunkSize > 80)
                        content = preset_fir;
                    else
                        content = preset_birch;


                    bool end_of_header = false;

                    foreach (var item in content)
                    {
                        if (!end_of_header)
                        {
                            if (item == "end_header")
                                end_of_header = true;
                            continue;
                        }

                        string[] split = item.Split(' ');
                        int _x = int.Parse(split[0]) + x;
                        int _z = int.Parse(split[1]) + z;
                        int _y = int.Parse(split[2]) + y + 1; // spawn above grass block
                        int _r = int.Parse(split[3]);
                        int _g = int.Parse(split[4]);
                        int _b = int.Parse(split[5]);




                        int voxel_id = (_z + chunkSize * (_y + chunkSize * _x));

                        byte blockId;
                        switch (_r, _g)
                        {
                            case (141, 180): blockId = 5; break; // birch_leaves
                            case (221, 221): blockId = 3; break; // birch_log

                            case (125, 150): blockId = 6; break; // pine_leaves
                            case (102, 51): blockId = 4; break; // pine_log
                            default: blockId = 0; break;
                        }

                        // make sure its not out of bounds
                        if (_x < 0 || _x >= chunkSize || _y < 0 || _y >= chunkSize || _z < 0 || _z >= chunkSize)
                        {
                            outOfBounds.Add(_x);
                            outOfBounds.Add(_y);
                            outOfBounds.Add(_z);
                            outOfBounds.Add(blockId);
                            continue;
                        }


                        result[voxel_id] = blockId;
                    }


                }

                int above = (z + chunkSize * (y + 1 + chunkSize * x));

                if (above > arraySize -1) continue;

                if (next > 0.7)
                {
                    result[above] = 128;
                    continue;
                }

                if (next > 0.4)
                {
                    result[above] = 129;
                    continue;
                }

                if (next > 0.0065)
                {
                    result[above] = 130;
                    continue;
                }
            }


            return (result, outOfBounds);
        }

        // shouldn't need to return anything since array is a reference type
        private void FillRegionID(byte[] chunk, int size, int startX, int startY, int startZ, byte id)
        {
            for (int x = startX; x < startX + size; x++)
                for (int y = startY; y < startY + size; y++)
                    for (int z = startZ; z < startZ + size; z++)
                    {
                        int baseIndex = z + chunkSize * (y + chunkSize * x);
                        chunk[baseIndex] = id;
                    }

            //return chunk;
        }

        public byte[] GenerateSVO(byte[] result)
        {
            // -- SPARSE VOXEL OCTREES -- \\
            /* 
             * ox - octree x
             * sx - subregion x

            */
            if (result[0] == 255)
                return result;


            // 16x16x16
            int l1 = 16;

            for (int ox = 0; ox < chunkSize; ox += l1)
                for (int oy = 0; oy < chunkSize; oy += l1)
                    for (int oz = 0; oz < chunkSize; oz += l1)
                    {
                        for (int x = ox; x < ox + l1; x++)
                            for (int y = oy; y < oy + l1; y++)
                                for (int z = oz; z < oz + l1; z++)
                                {
                                    int baseIndex = z + chunkSize * (y + chunkSize * x);
                                    if (result[baseIndex] != 254)
                                        goto endOfL1;
                                }
                        // code here runs if whole region was air
                        FillRegionID(result, l1, ox, oy, oz, 253);
                        //int regionHeaderIndex = oz + chunkSize * (oy + chunkSize * ox);
                        //result[regionHeaderIndex] = 253;
                    //
                    endOfL1:;
                    }

            // 32x32x32 (combine 4 16x16x16 regions)
            int l2 = 32;
            int stepl2 = 16;
            for (int ox = 0; ox < chunkSize; ox += l2)
                for (int oy = 0; oy < chunkSize; oy += l2)
                    for (int oz = 0; oz < chunkSize; oz += l2)
                    {
                        for (int sx = 0; sx < 2; sx++)
                            for (int sy = 0; sy < 2; sy++)
                                for (int sz = 0; sz < 2; sz++)
                                {
                                    int subX = ox + (sx * stepl2);
                                    int subY = oy + (sy * stepl2);
                                    int subZ = oz + (sz * stepl2);

                                    int subRegionHeaderIndex = subZ + chunkSize * (subY + chunkSize * subX);
                                    if (result[subRegionHeaderIndex] != 253)
                                        goto endOfL2;
                                }

                        // code here runs if 8 subregions were detected empty
                        FillRegionID(result, l2, ox, oy, oz, 252);
                        //int regionHeaderIndex32 = oz + chunkSize * (oy + chunkSize * ox);
                        //result[regionHeaderIndex32] = 252;

                    endOfL2:;
                    }

            // 64x64x64 (combine 8 32x32x32 regions)
            int l3 = 64;
            int stepl3 = 32;

            for (int ox = 0; ox < chunkSize; ox += l3)
                for (int oy = 0; oy < chunkSize; oy += l3)
                    for (int oz = 0; oz < chunkSize; oz += l3)
                    {
                        for (int sx = 0; sx < 2; sx++)
                            for (int sy = 0; sy < 2; sy++)
                                for (int sz = 0; sz < 2; sz++)
                                {
                                    int subX = ox + (sx * stepl3);
                                    int subY = oy + (sy * stepl3);
                                    int subZ = oz + (sz * stepl3);

                                    int subRegionHeaderIndex = subZ + chunkSize * (subY + chunkSize * subX);

                                    // check if the 32-block was marked as empty (252)
                                    if (result[subRegionHeaderIndex] != 252)
                                        goto endOfL3;
                                }

                        // code here runs if 8 32x32x32 regions were empty
                        FillRegionID(result, l3, ox, oy, oz, 251);
                        //int regionHeaderIndex64 = oz + chunkSize * (oy + chunkSize * ox);
                        //result[regionHeaderIndex64] = 251; // marker for 64x64x64

                    endOfL3:;
                    }

            return result;
        }
    }
}
