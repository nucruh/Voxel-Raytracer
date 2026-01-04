using OpenTK.Graphics.OpenGL;
using OpenTK.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
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

        int lowResFBO;
        int lowResTex;
        int lowW, lowH;
        int renderScale = 2; // 2 = half res, 4 = quarter res

        static int chunkSize => config.chunkSize;

        static double generationTime = 0;

        int blockTextureArray;
        const int BLOCK_TEX_SIZE = 16;
        const int BLOCK_TEX_LAYERS = 256;

        private Chunk[] _chunks;

        private GdiTextRenderer _textRenderer;


        float yaw = 45.0f; // y
        float pitch = 0.0f; // x

        Vector3 camPos = new Vector3(3f, 120f, 3f);
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
            Vsync = OpenTK.Windowing.Common.VSyncMode.Off,
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

            GL.TexImage3D(
                TextureTarget.Texture3D,
                0,
                PixelInternalFormat.R8ui,     // 1 byte per voxel
                size, size, size,
                0,
                OpenTK.Graphics.OpenGL.PixelFormat.RedInteger,      // integer data
                PixelType.UnsignedByte,
                data
            );

            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture3D, TextureParameterName.TextureWrapR, (int)TextureWrapMode.ClampToEdge);

            return tex;
        }

        byte[] LoadBlockTexture(int id)
        {
            string path = $"textures16x/{id}.png";

            if (!File.Exists(path))
            {
                Console.WriteLine(id);
                    path = "textures16x/1.png"; // dirt texture as default if nto found
            }
            using var bmp = new System.Drawing.Bitmap(path);
            var data = new byte[BLOCK_TEX_SIZE * BLOCK_TEX_SIZE * 4];

            int i = 0;
            for (int y = BLOCK_TEX_SIZE - 1; y >= 0; y--)
            {
                for (int x = 0; x < BLOCK_TEX_SIZE; x++)
                {
                    var c = bmp.GetPixel(x, y);
                    data[i++] = c.R;
                    data[i++] = c.G;
                    data[i++] = c.B;
                    data[i++] = c.A;
                }
            }

            return data;
        }



        protected override void OnLoad()
        {
            base.OnLoad();
            CursorState = CursorState.Grabbed;

            _textRenderer = new GdiTextRenderer(width, height, "Monocraft", 20f);

            var terrain = new Terrain();
            var watch = Stopwatch.StartNew();

            // Use ConcurrentDictionary for thread safety
            ConcurrentDictionary<(int x, int y, int z), List<int>> outOfBounds = new ConcurrentDictionary<(int x, int y, int z), List<int>>();
            ConcurrentDictionary<(int x, int y, int z), long> associatedTuples = new ConcurrentDictionary<(int x, int y, int z), long>();

            int worldHeightChunks = config.worldHeightChunks;
            Chunk[] chunks = new Chunk[worldSize * worldHeightChunks * worldSize];

            // Parallel generation
            Parallel.For(0, chunks.Length, idx =>
            {
                int x = idx / (worldSize * worldHeightChunks);
                int z = (idx / worldHeightChunks) % worldSize;
                int y = idx % worldHeightChunks;

                (byte[] resultVD, List<int> resultOOB) = terrain.GenerateChunk(new System.Numerics.Vector3(x, y, z));

                // Use thread-safe operations to update dictionaries
                outOfBounds[(x, y, z)] = resultOOB;
                associatedTuples[(x, y, z)] = idx;

                chunks[idx] = new Chunk
                {
                    coords = new Vector3i(x, y, z),
                    voxelData = resultVD,
                    size = chunkSize
                };
            });

            // After parallel generation, process the outOfBounds data
            foreach (var kv in outOfBounds)
            {
                int chunkX = kv.Key.x;
                int chunkY = kv.Key.y;
                int chunkZ = kv.Key.z;

                for (int i = 0; i < kv.Value.Count; i += 4)
                {
                    int x = kv.Value[i];
                    int y = kv.Value[i + 1];
                    int z = kv.Value[i + 2];
                    int id = kv.Value[i + 3];

                    // Compute target chunk coordinates and local voxel coordinates
                    int targetChunkX = chunkX + Math.DivRem(x, chunkSize, out int localX);
                    int targetChunkY = chunkY + Math.DivRem(y, chunkSize, out int localY);
                    int targetChunkZ = chunkZ + Math.DivRem(z, chunkSize, out int localZ);

                    // Adjust local coordinates and chunk boundaries
                    if (localX < 0) { localX += chunkSize; targetChunkX--; }
                    if (localY < 0) { localY += chunkSize; targetChunkY--; }
                    if (localZ < 0) { localZ += chunkSize; targetChunkZ--; }

                    localX = (localX + chunkSize) % chunkSize;
                    localY = (localY + chunkSize) % chunkSize;
                    localZ = (localZ + chunkSize) % chunkSize;
                    int voxelId = localZ + chunkSize * (localY + chunkSize * localX);

                    // Early exit checks
                    if (targetChunkX < 0 || targetChunkY < 0 || targetChunkZ < 0) continue;
                    if (!associatedTuples.TryGetValue((targetChunkX, targetChunkY, targetChunkZ), out long chunkId)) continue;
                    if (voxelId < 0 || voxelId >= chunkSize * chunkSize * chunkSize) continue;

                    // Update the voxel data for the chunk
                    chunks[chunkId].voxelData[voxelId] = (byte)id;
                }
            }

            Parallel.For(0, chunks.Length, idx =>
            {
                var chunk = chunks[idx];

                // Call GenerateSVO on the voxel data
                // Assuming GenerateSVO takes byte[] and returns byte[]
                byte[] svoResult = terrain.GenerateSVO(chunk.voxelData);

                // Replace the chunk's voxel data with the SVO result
                chunk.voxelData = svoResult;
            });




            /*
            int size = chunkSize;
            int imgW = worldSize * size;
            int imgH = worldSize * size;

            byte[] pixels_ = new byte[imgW * imgH * 4];

            for (int i = 0; i < chunks.Length; i++)
            {
                Chunk chunk = chunks[i];
                if (chunk == null) continue;

                int cx = chunk.coords.X;
                int cy = chunk.coords.Y;
                int cz = chunk.coords.Z;

                byte[] voxels = chunk.voxelData;

                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        int count = 0;

                        for (int y = 0; y < size; y++)
                        {
                            // ✅ use the correct index formula from GenerateChunk
                            int idx = z + size * (y + size * x);

                            if (voxels[idx] != 254)
                                count += 1;
                        }

                        int wx = cx * size + x;
                        int wz = cz * size + z;

                        int p = (wx + wz * imgW) * 4;
                        int v = pixels_[p] + count;
                        byte b = (byte)Math.Min(v, 255);

                        pixels_[p + 0] = b;
                        pixels_[p + 1] = b;
                        pixels_[p + 2] = b;
                        pixels_[p + 3] = 255;
                    }
                }
            }

            using Bitmap bmp = new Bitmap(imgW, imgH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            BitmapData data = bmp.LockBits(
                new Rectangle(0, 0, imgW, imgH),
                ImageLockMode.WriteOnly,
                bmp.PixelFormat
            );

            System.Runtime.InteropServices.Marshal.Copy(pixels_, 0, data.Scan0, pixels_.Length);
            bmp.UnlockBits(data);

            // flip vertically so +Z is up
            bmp.RotateFlip(RotateFlipType.RotateNoneFlipY);

            bmp.Save("world_debug.png", ImageFormat.Png);
            */

            // Upload voxel chunks
            for (int c = 0; c < chunks.Length; c++)
                chunks[c].textureId = UploadVoxelChunk(chunks[c].voxelData, chunkSize);

            _chunks = chunks;

            watch.Stop();
            generationTime = watch.ElapsedMilliseconds;

            // --------------------
            // Build shader
            // --------------------
            program = BuildShader();
            GL.UseProgram(program);

            // ============================================================
            // 1️⃣ BLOCK TEXTURE ARRAY → UNIT 0
            // ============================================================
            blockTextureArray = GL.GenTexture();
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2DArray, blockTextureArray);

            GL.TexImage3D(
                TextureTarget.Texture2DArray,
                0,
                PixelInternalFormat.Rgba8,
                BLOCK_TEX_SIZE,
                BLOCK_TEX_SIZE,
                BLOCK_TEX_LAYERS,
                0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                IntPtr.Zero
            );

            for (int i = 0; i < BLOCK_TEX_LAYERS; i++)
            {
                byte[] pixels = LoadBlockTexture(i);

                GL.TexSubImage3D(
                    TextureTarget.Texture2DArray,
                    0,
                    0, 0, i,
                    BLOCK_TEX_SIZE,
                    BLOCK_TEX_SIZE,
                    1,
                    OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    pixels
                );
            }

            // Enable mipmaps
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.NearestMipmapNearest);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);

            // Wrap mode stays the same
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2DArray, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

            // Generate mipmaps for the texture array
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2DArray);

            GL.Uniform1(
                GL.GetUniformLocation(program, "uBlockTextures"),
                0
            );

            // ============================================================
            // 2️⃣ VOXEL CHUNKS → UNITS [1..N]
            // ============================================================
            for (int i = 0; i < chunks.Length; i++)
            {
                int texUnit = 1 + i;

                GL.ActiveTexture(TextureUnit.Texture0 + texUnit);
                GL.BindTexture(TextureTarget.Texture3D, chunks[i].textureId);

                GL.Uniform1(
                    GL.GetUniformLocation(program, $"uVoxelTex[{i}]"),
                    texUnit
                );
            }

            // Other uniforms
            GL.Uniform3(
                GL.GetUniformLocation(program, "uVoxelDim"),
                chunkSize, chunkSize, chunkSize
            );

            vao = FullscreenTriangle();

            Console.WriteLine("loaded shaders");
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

            if (input.IsKeyDown(Keys.LeftShift)) delta *= 0.07f;

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

        private float iTime = 0.0f;

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

            /*
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
            */

            // send uniforms 
            int resLoc = GL.GetUniformLocation(program, "iResolution");
            GL.Uniform2(resLoc, (float)width, (float)height);
            iTime += (float)args.Time;
            int timeLoc = GL.GetUniformLocation(program, "iTime");
            GL.Uniform1(timeLoc, iTime);
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
                $"world: {voxelCount / 10e5:f1}m voxels {worldSize}x{worldSize}x{worldSize} chunks",
                margin,
                margin + lineSpacing * 1,
                1.0f, 1.0f, 1.0f // White color
            );

            _textRenderer.RenderText(
                $"generated in: {generationTime:f1}ms with an average of {(generationTime) / (_chunks != null ? _chunks.Length : 1):f2}ms per chunk",
                margin,
                margin + lineSpacing * 2,
                1.0f, 1.0f, 1.0f
            );

            _textRenderer.RenderText(
                $"system: {width}x{height}, {Environment.ProcessorCount} processors, {(voxelCount * 1) / 1024 / 1024}MiB for voxels",
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