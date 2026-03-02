using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace Voxel_Raytracer
{



    public class Config
    {

        public static Dictionary<int, List<string>> blocks;

        public static readonly Config Instance = new Config();

        public int width = 2560;
        public int height = 1440;

        public float mouseSensitivity = 0.0015f;
        public float moveSpeed = 30f;

        public int worldHeight = 128;
        public float resolution = 2.0f;
        public double scale = 0.04;
        public double squishFactor = 42.0;
        public int chunkSize = 128;

        public int worldHeightChunks = 3;
        public int worldSize = 32;

        static Config()
        {
            string json = File.ReadAllText("JSONConfigs/blockTags.json");
            blocks = JsonSerializer.Deserialize<Dictionary<int, List<string>>>(json)!;
        }
        



        private Config() { }



    }
}
