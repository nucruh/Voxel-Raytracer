using System;
using System.Collections.Generic;
using System.Text;

namespace Voxel_Raytracer
{
    public class Config
    {
        public static readonly Config Instance = new Config();

        public int width = 1920;
        public int height = 1080;

        public float mouseSensitivity = 0.0015f;
        public float moveSpeed = 30f;

        public int worldHeight = 128;
        public float resolution = 2.0f;
        public double scale = 0.04;
        public double squishFactor = 42.0;
        public int chunkSize = 128;

        public int worldHeightChunks = 4;
        public int worldSize = 4;

        private Config() { } // private constructor



    }
}
