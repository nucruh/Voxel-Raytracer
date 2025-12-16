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

        Vector3 lightDir = Vector3.Normalize(new Vector3(1, -1.0f, 0f));

        bool IsInShadow(int x, int y, int z, byte[] data)
        {
            const int maxSteps = 128;
            float step = 0.9f;        // Now stepping nearly 1 voxel unit
            float epsilon = 0.05f;    // Bias must be larger than before

            // 1. Start at the center of the current voxel
            float fx = x + 0.5f;
            float fy = y + 0.5f;
            float fz = z + 0.5f;

            // 2. Bias the starting point *towards* the light source, pushing the ray *away* from the current block
            fx += lightDir.X * epsilon;
            fy += lightDir.Y * epsilon;
            fz += lightDir.Z * epsilon;

            for (int i = 0; i < maxSteps; i++)
            {
                // 3. Raymarch *away* from the light source
                fx -= lightDir.X * step;
                fy -= lightDir.Y * step;
                fz -= lightDir.Z * step;

                int ix = (int)fx;
                int iy = (int)fy;
                int iz = (int)fz;

                // No need for the (ix == x) skip check here, as the bias and larger step should have pushed it out.

                if (ix < 0 || iy < 0 || iz < 0 ||
                    ix >= chunkSize || iy >= chunkSize || iz >= chunkSize)
                    return false; // ray left the chunk

                int idx = 4 * (iz + chunkSize * (iy + chunkSize * ix));
                if (data[idx + 3] == 255)
                    return true; // hit solid voxel
            }

            return false;
        }
        public byte[] GenerateChunk(Vector3 chunkCoords)
        {

            // R, G, B, filled
            var result = new byte[chunkSize * chunkSize * chunkSize * 4];
            int arraySize = result.Length;
            var perlin = new PerlinNoise(seed: 1234);

            Random rnd = new Random();

            Vector3 offset = new Vector3(chunkCoords.X * chunkSize, chunkCoords.Y * chunkSize, chunkCoords.Z * chunkSize);

            bool emptyChunk = true;

            /*
            for (int x = 0; x < chunkSize; x++)
                for (int z = 0; z < chunkSize; z++)
                {
                    double xzSquish = squishFactor * Math.Clamp(Math.Pow(perlin.Noise(x + offset.X, z + offset.Z, 1) + 1, 2), 0.5, 1.5);

                    for (int y = 0; y < chunkSize; y++)
                    {
                        double worldX = x + offset.X;
                        double worldY = y + offset.Y;
                        double worldZ = z + offset.Z;


                        double sampleX = (worldX / resolution) * scale;
                        double sampleY = (worldY / resolution) * scale;
                        double sampleZ = (worldZ / resolution) * scale;

                        double density = perlin.FBM3D(sampleX, sampleZ, sampleY, 1);
                        double densityModifier = (baseHeight - worldY) / xzSquish;
                        double finalDensity = density + densityModifier;

                        if (finalDensity > 0.9)
                        {
                            double lightDiff = 10 * rnd.NextDouble();
                            int baseIndex = 4 * (z + chunkSize * (y + chunkSize * x));
                            // solid material
                            result[baseIndex] = (byte)(200 + lightDiff); // R
                            result[baseIndex + 1] = (byte)(200 + lightDiff); // G
                            result[baseIndex + 2] = (byte)(200 + lightDiff); // B
                            result[baseIndex + 3] = 255; // filled (1.0f)
                            emptyChunk = false;
                        }
                        
                        // no else needed since default is already 0

                    }
                }
            */
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
                        y++;
                        if (y >= chunkSize) break;
                        double lightDiff = 10 * rnd.NextDouble();
                        int baseIndex = 4 * (z + chunkSize * (y + chunkSize * x));
                        // solid material
                        result[baseIndex] = (byte)(200 + lightDiff); // R
                        result[baseIndex + 1] = (byte)(200 + lightDiff); // G
                        result[baseIndex + 2] = (byte)(200 + lightDiff); // B
                        result[baseIndex + 3] = 255; // filled (1.0f)
                        emptyChunk = false;
                    }
                    
                }


                    if (emptyChunk)
            {
                result[0] = 255;
                result[3] = 0;

                return result;
            }

            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int baseIndex = 4 * (z + chunkSize * (y + chunkSize * x));

                        if (result[baseIndex + 3] == 0) continue;

                        // check above
                        int above1 = 4 * (z + chunkSize * ((y + 1) + chunkSize * x));
                        int above2 = 4 * (z + chunkSize * ((y + 2) + chunkSize * x));

                        if (above1+3 > arraySize || above2+3 > arraySize) continue;

                        if (result[above1 + 3] == 0 && result[above2 + 3] == 0)
                        {
                            // make base block grass
                            double lightDiff = 10 * rnd.NextDouble();
                            result[baseIndex] = (byte)(142 + lightDiff); // R
                            result[baseIndex + 1] = (byte)(167 + lightDiff); // G
                            result[baseIndex + 2] = (byte)(47 + lightDiff); //


                        }
                        else
                            continue;

                            // make bottom ones dirt if they do exist

                        int below1 = 4 * (z + chunkSize * ((y - 1) + chunkSize * x));
                        int below2 = 4 * (z + chunkSize * ((y - 2) + chunkSize * x));
                        int below3 = 4 * (z + chunkSize * ((y - 3) + chunkSize * x));

                        if (below1 >= 0)
                        {
                            if (result[below1 + 3] == 255)
                            {
                                // make dirt
                                double lightDiff = 10 * rnd.NextDouble();
                                result[below1] = (byte)(124 + lightDiff); // R
                                result[below1 + 1] = (byte)(94 + lightDiff); // G
                                result[below1 + 2] = (byte)(74 + lightDiff); // B
                            }
                        }


                        if (below2 >= 0)
                        {
                            if (result[below2 + 3] == 255)
                            {
                                // make dirt
                                double lightDiff = 10 * rnd.NextDouble();
                                result[below2] = (byte)(124 + lightDiff); // R
                                result[below2 + 1] = (byte)(94 + lightDiff); // G
                                result[below2 + 2] = (byte)(74 + lightDiff); // B
                            }
                        }

                        if (below3 >= 0)
                        {
                            if (result[below3 + 3] == 255)
                            {
                                // make dirt
                                double lightDiff = 10 * rnd.NextDouble();
                                result[below3] = (byte)(124 + lightDiff); // R
                                result[below3 + 1] = (byte)(94 + lightDiff); // G
                                result[below3 + 2] = (byte)(74 + lightDiff); // B
                            }
                        }

                    }

            Random treeRand = new Random((int)(chunkCoords.X * 24957 + chunkCoords.Y * 135 + chunkCoords.Z * 13581)); // deterministic per chunk

            for (int x = 2; x < chunkSize - 2; x++)
                for (int z = 2; z < chunkSize - 2; z++)
                {
                    // find grass surface
                    for (int y = chunkSize - 4; y >= 2; y--)
                    {
                        int base_id = 4 * (z + chunkSize * (y + chunkSize * x));

                        if (result[base_id + 3] == 255 &&
                            result[base_id + 1] > 150 && result[base_id + 2] < 150 &&
                            treeRand.NextDouble() < 0.0015)      // tree frequency
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
                                int _y = int.Parse(split[2]) + y;
                                int _r = int.Parse(split[3]);
                                int _g = int.Parse(split[4]);
                                int _b = int.Parse(split[5]);

                                // make sure its not out of bounds
                                if (_x < 0 || _x >= chunkSize || _y < 0 || _y >= chunkSize || _z < 0 || _z >= chunkSize)
                                    continue;


                                int voxel_id = 4 * (_z + chunkSize * (_y + chunkSize * _x));
                                result[voxel_id] = (byte)_r;
                                result[voxel_id + 1] =(byte)_g;
                                result[voxel_id + 2] = (byte)_b;
                                result[voxel_id + 3] = (byte)255;
                            }
                        }
                    }
                }





            float[,,]? shadowBuf = new float[chunkSize, chunkSize, chunkSize];

            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int baseIndex = 4 * (z + chunkSize * (y + chunkSize * x));
                        if (result[baseIndex + 3] == 0)
                            continue;

                        bool shadow = IsInShadow(x, y, z, result);
                        shadowBuf[x, y, z] = shadow ? 0f : 1f;
                    }

            // 2. Blur the shadow buffer (diffusion kernel)
            float[,,]? blurred = new float[chunkSize, chunkSize, chunkSize];

            int radius = 2; // 2–3 is enough for smoothing

            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        float sum = 0f;
                        int count = 0;

                        for (int dx = -radius; dx <= radius; dx++)
                            for (int dy = -radius; dy <= radius; dy++)
                                for (int dz = -radius; dz <= radius; dz++)
                                {
                                    int nx = x + dx;
                                    int ny = y + dy;
                                    int nz = z + dz;

                                    if (nx < 0 || ny < 0 || nz < 0 ||
                                        nx >= chunkSize || ny >= chunkSize || nz >= chunkSize)
                                        continue;

                                    sum += shadowBuf[nx, ny, nz];
                                    count++;
                                }

                        blurred[x, y, z] = sum / count; // average = local light amount
                    }


            // 3. Apply smoothed shadow factor to block colors
            for (int x = 0; x < chunkSize; x++)
                for (int y = 0; y < chunkSize; y++)
                    for (int z = 0; z < chunkSize; z++)
                    {
                        int baseIndex = 4 * (z + chunkSize * (y + chunkSize * x));
                        if (result[baseIndex + 3] == 0)
                            continue;

                        float light = 0.7f + blurred[x, y, z] * 1f;
                        // 0.6 = full shadow
                        // 1.0 = full light
                        // softens in-between

                        result[baseIndex] = (byte)(result[baseIndex] * light);
                        result[baseIndex + 1] = (byte)(result[baseIndex + 1] * light);
                        result[baseIndex + 2] = (byte)(result[baseIndex + 2] * light);
                    }

            shadowBuf = null;
            blurred = null;
            GC.Collect();
            
            return result;
        }

    }
}
