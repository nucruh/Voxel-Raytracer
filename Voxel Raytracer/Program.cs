using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Printing;

namespace Voxel_Raytracer
{
    // --- Step 3: Program Class with Main Entry Point ---
    // This static class runs the Renderer when the application starts.

    class Chunk
    {
        public Vector3i coords;   // chunk coordinates
        public int textureId;     // GPU texture
        public required byte[] voxelData;  // voxel array
        public int size;          // chunk size (assume cube)
    }

    public static class Program
    {
        public static void Main()
        {
            // Create a new instance of the window and run the game loop
            using Renderer win = new Renderer();
            win.Run();
        }
    }

    // --- Step 1: Inherit from GameWindow ---
    public class Renderer : GameWindow
    {

        private static Config config => Config.Instance;

        static int width => config.width;
        static int height => config.height;
        static int worldSize => config.worldSize;

        static int chunkSize => config.chunkSize;

        static double generationTime = 0;

        private Chunk[] _chunks;

        private GdiTextRenderer _textRenderer;


        float yaw = 0.0f; // y
        float pitch = 0.0f; // x

        Vector3 camPos = new Vector3(0f, 80f, -3f);
        float mouseSensitivity => config.mouseSensitivity;
        float moveSpeed => config.moveSpeed;

        Vector3 cameraForward => new Vector3(
            MathF.Cos(pitch) * MathF.Sin(yaw),
            MathF.Sin(pitch),
            MathF.Cos(pitch) * MathF.Cos(yaw)
        );

        Vector3 camRight => Vector3.Normalize(Vector3.Cross(cameraForward, Vector3.UnitY));
        Vector3 camUp => Vector3.Cross(camRight, cameraForward);

        int program, vao;

        static NativeWindowSettings nativeSetting = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(width, height),
            Title = "Voxel Engine",
            APIVersion = new Version(3, 3),
            Vsync = OpenTK.Windowing.Common.VSyncMode.On,
            NumberOfSamples = 4,
        };

        public Renderer() : base(GameWindowSettings.Default, nativeSetting)
        {
        }


        private int LoadShader(string path, ShaderType type)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, File.ReadAllText(path));
            GL.CompileShader(shader);

            GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
            if (ok == 0)
            {
                Console.WriteLine(GL.GetShaderInfoLog(shader));
                throw new Exception("Shader compilation failed.");
            }

            return shader;
        }
        private int BuildShader()
        {
            
            // load frag shader
            int frag = LoadShader("shaders/voxel.glsl", ShaderType.FragmentShader);

            // passtrough .vert shader
            int vert = LoadShader("shaders/voxel.vert", ShaderType.VertexShader);
            // Link program
            int program = GL.CreateProgram();
            GL.AttachShader(program, vert);
            GL.AttachShader(program, frag);
            GL.LinkProgram(program);

            return program;
        }

        private int FullscreenTriangle()
        {
            // triangle data
            float[] data =
            {
                -1, -1,
                3, -1,
                -1, 3
            };

            int vao = GL.GenVertexArray();
            int vbo = GL.GenBuffer();

            GL.BindVertexArray(vao);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, data.Length * sizeof(float), data, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), 0);

            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.BindVertexArray(0);

            return vao;
        }

        int UploadVoxelChunk(byte[] data, int size)
        {
            int tex = GL.GenTexture();

            GL.BindTexture(TextureTarget.Texture3D, tex);
            GL.TexImage3D(TextureTarget.Texture3D, 0, PixelInternalFormat.Rgba8,
                          size, size, size, 0,
                          PixelFormat.Rgba, PixelType.UnsignedByte, data);

            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);


            return tex;
        }

        protected override void OnLoad()
        {
            base.OnLoad();
            this.CursorState = CursorState.Grabbed;

            _textRenderer = new GdiTextRenderer(width, height, "Monocraft", 20f);

            var TerrainGenerator = new Terrain();

            var watch = new Stopwatch();
            watch.Start();

            ConcurrentBag<(Vector3i coords, byte[] data)> generatedChunks = new ConcurrentBag<(Vector3i, byte[])>();

            ParallelOptions options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount // or any number you want
            };

            //Console.WriteLine($"{Environment.ProcessorCount} processors");

            int worldHeightChunks = config.worldHeightChunks; // define in Config
            Chunk[] chunks = new Chunk[worldSize * worldHeightChunks * worldSize];

            Parallel.For(0, worldSize * worldHeightChunks * worldSize, i =>
            {
                int x = i / (worldSize * worldHeightChunks);
                int z = (i / worldHeightChunks) % worldSize;
                int y = i % worldHeightChunks;

                byte[] chunkData = TerrainGenerator.GenerateChunk(new System.Numerics.Vector3(x, y, z));
                chunks[i] = new Chunk
                {
                    coords = new Vector3i(x, y, z),
                    voxelData = chunkData,
                    size = chunkSize
                };
            });


            // Main thread: upload textures
            for (int i_ = 0; i_ < chunks.Length; i_++)
            {
                chunks[i_].textureId = UploadVoxelChunk(chunks[i_].voxelData, chunkSize);
            }

            // If you need a List<Chunk> afterward
            List<Chunk> chunkList = chunks.ToList();


            // Build shader program first
            program = BuildShader();
            GL.UseProgram(program);

            int i = 0;
            foreach (var item in chunks)
            {
                GL.ActiveTexture(TextureUnit.Texture0 + i);
                GL.BindTexture(TextureTarget.Texture3D, item.textureId);
                GL.Uniform1(GL.GetUniformLocation(program, $"uVoxelTex[{i}]"), i);

                i++;
            }

            _chunks = chunks;

            watch.Stop();

            generationTime = watch.ElapsedMilliseconds;

            int locDim = GL.GetUniformLocation(program, "uVoxelDim");
            GL.Uniform3(locDim, chunkSize, chunkSize, chunkSize);

            vao = FullscreenTriangle();
        }


        int frameCount = 0;
        double timeBuildup = 0;

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);


            double deltaTime = args.Time;
            var input = KeyboardState;
            var mouse = MouseState;

            yaw -= mouse.Delta.X * mouseSensitivity;
            pitch -= mouse.Delta.Y * mouseSensitivity;

            pitch = MathHelper.Clamp(pitch, -MathF.PI / 2 + 0.01f, MathF.PI / 2 - 0.01f);

            float delta = moveSpeed * (float)deltaTime;

            if (input.IsKeyDown(Keys.W)) camPos += cameraForward * delta;
            if (input.IsKeyDown(Keys.S)) camPos -= cameraForward * delta;
            if (input.IsKeyDown(Keys.A)) camPos -= camRight * delta;
            if (input.IsKeyDown(Keys.D)) camPos += camRight * delta;
            if (input.IsKeyDown(Keys.Space)) camPos += Vector3.UnitY * delta;

            if (input.IsKeyDown(Keys.Escape))
            {
                CursorState = CursorState.Normal;
            }
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {

            frameCount++;
            timeBuildup += args.Time;

            string fpsString = $"{Math.Round(frameCount / (timeBuildup > 0 ? timeBuildup : 1))} fps";
            if (timeBuildup > 0.5)
            {
                timeBuildup = 0;
                frameCount = 0;
            }

            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);

            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.UseProgram(program);
            GL.BindVertexArray(vao);

            if (_chunks != null)
            {
                for (int i = 0; i < _chunks.Length; i++)
                {
                    // Activate the corresponding Texture Unit
                    GL.ActiveTexture(TextureUnit.Texture0 + i);
                    // Bind the 3D texture handle
                    GL.BindTexture(TextureTarget.Texture3D, _chunks[i].textureId);
                }
            }

            // send uniforms 
            int resLoc = GL.GetUniformLocation(program, "iResolution");
            GL.Uniform2(resLoc, (float)width, (float)height);
            int timeLoc = GL.GetUniformLocation(program, "iTime");
            GL.Uniform1(timeLoc, (float)args.Time);
            int camPosLoc = GL.GetUniformLocation(program, "uCamPos");
            GL.Uniform3(camPosLoc, camPos);
            int forwardLoc = GL.GetUniformLocation(program, "uCamForward");
            GL.Uniform3(forwardLoc, cameraForward);
            int rightLoc = GL.GetUniformLocation(program, "uCamRight");
            GL.Uniform3(rightLoc, camRight);
            int upLoc = GL.GetUniformLocation(program, "uCamUp");
            GL.Uniform3(upLoc, camUp);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 3);

            float margin = 10f;
            float lineSpacing = 30f; // Adjusted for font size 24

            int voxelCount = (int)((_chunks != null ? _chunks.Length : 1) * Math.Pow(chunkSize, 3));

            _textRenderer.RenderText(
                $"fps: {fpsString} {args.Time * 1000:f1}ms",
                margin,
                margin,
                1.0f, 1.0f, 1.0f // White color
            );

            _textRenderer.RenderText(
                $"world: {voxelCount / 10e5:f1}m voxels {worldSize}x{worldSize} chunks",
                margin,
                margin + lineSpacing * 1,
                1.0f, 1.0f, 1.0f // White color
            );

            _textRenderer.RenderText(
                $"generated in: {generationTime / 1000:f2} seconds with an average of {(generationTime / 1000) / (_chunks != null ? _chunks.Length : 1):f2}s per chunk",
                margin,
                margin + lineSpacing * 2,
                1.0f, 1.0f, 1.0f
            );

            _textRenderer.RenderText(
                $"system: {width}x{height}, {Environment.ProcessorCount} processors, {(voxelCount * 4) / 1024 / 1024}MiB for voxels",
                margin,
                margin + lineSpacing * 3,
                1.0f, 1.0f, 1.0f
            );

            _textRenderer.RenderText(
                $"pos: {Math.Round(camPos.X, 2)}, {Math.Round(camPos.Y, 2)}, {Math.Round(camPos.Z, 2)}",
                margin,
                margin + lineSpacing * 5,
                1.0f, 0.7f, 0.2f // Orange color
            );

            SwapBuffers();
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            _textRenderer?.Dispose();
        }
    }
}