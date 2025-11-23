using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Diagnostics.Metrics;
using System.Diagnostics;

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

        static int width = 960;
        static int height = 540;


        float yaw = 0.0f; // y
        float pitch = 0.0f; // x

        Vector3 camPos = new Vector3(0f, 0f, -3f);
        float mouseSensitivity = 0.0015f;
        float moveSpeed = 30f;

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

            var TerrainGenerator = new Terrain();
            int chunkSize = TerrainGenerator.chunkSize;

            byte[] chunk0Data = TerrainGenerator.GenerateChunk(new System.Numerics.Vector3(0, 0, 0));
            int tex0 = UploadVoxelChunk(chunk0Data, chunkSize);

            byte[] chunk1Data = TerrainGenerator.GenerateChunk(new System.Numerics.Vector3(0, 0, 1));
            int tex1 = UploadVoxelChunk(chunk1Data, chunkSize);

            byte[] chunk2Data = TerrainGenerator.GenerateChunk(new System.Numerics.Vector3(1, 0, 0));
            int tex2 = UploadVoxelChunk(chunk2Data, chunkSize);

            byte[] chunk3Data = TerrainGenerator.GenerateChunk(new System.Numerics.Vector3(1, 0, 1));
            int tex3 = UploadVoxelChunk(chunk3Data, chunkSize);

            List<Chunk> chunks = new List<Chunk>()
            {
                new Chunk { coords = new Vector3i(0,0,0), textureId = tex0, voxelData = chunk0Data, size = chunkSize },
                new Chunk { coords = new Vector3i(0,0,1), textureId = tex1, voxelData = chunk1Data, size = chunkSize },
                new Chunk { coords = new Vector3i(1,0,0), textureId = tex2, voxelData = chunk2Data, size = chunkSize },
                new Chunk { coords = new Vector3i(1,0,1), textureId = tex3, voxelData = chunk3Data, size = chunkSize },
            };


            // Build shader program first
            program = BuildShader();
            GL.UseProgram(program);

            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture3D, chunks[0].textureId);
            GL.Uniform1(GL.GetUniformLocation(program, "uVoxelTex[0]"), 0);

            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture3D, chunks[1].textureId);
            GL.Uniform1(GL.GetUniformLocation(program, "uVoxelTex[1]"), 1);

            GL.ActiveTexture(TextureUnit.Texture2);
            GL.BindTexture(TextureTarget.Texture3D, chunks[2].textureId);
            GL.Uniform1(GL.GetUniformLocation(program, "uVoxelTex[2]"), 2);

            GL.ActiveTexture(TextureUnit.Texture3);
            GL.BindTexture(TextureTarget.Texture3D, chunks[3].textureId);
            GL.Uniform1(GL.GetUniformLocation(program, "uVoxelTex[4]"), 3);

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

            if (timeBuildup > 0.5)
            {
                Console.Clear();
                Console.WriteLine($"{Math.Round(frameCount / timeBuildup)} fps");

                timeBuildup = 0;
                frameCount = 0;
            }

            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.UseProgram(program);
            GL.BindVertexArray(vao);

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

            SwapBuffers();
        }

    }
}