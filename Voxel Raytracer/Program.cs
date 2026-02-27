using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Voxel_Raytracer
{

    public class Chunk
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
        static double svoTime = 0;

        int blockTextureArray;
        const int BLOCK_TEX_SIZE = 16;
        const int BLOCK_TEX_LAYERS = 256;

        private Terrain terrain = new Terrain();
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



        // get system resolution
        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        const int SM_CXSCREEN = 0;
        const int SM_CYSCREEN = 1;

        readonly static int screenWidth = GetSystemMetrics(SM_CXSCREEN);
        readonly static int screenHeight = GetSystemMetrics(SM_CYSCREEN);



        static NativeWindowSettings nativeSetting = new NativeWindowSettings()
        {
            ClientSize = new Vector2i(width, height),
            Title = "Voxel Engine",
            APIVersion = new Version(4, 3),
            Vsync = OpenTK.Windowing.Common.VSyncMode.Off,
            Location = new Vector2i(screenWidth / 2 - width / 2, screenHeight / 2 - height / 2),
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

            var watch = Stopwatch.StartNew();

            ConcurrentDictionary<(int x, int y, int z), List<int>> outOfBounds = new ConcurrentDictionary<(int x, int y, int z), List<int>>();
            ConcurrentDictionary<(int x, int y, int z), long> associatedTuples = new ConcurrentDictionary<(int x, int y, int z), long>();

            int worldHeightChunks = config.worldHeightChunks;
            Chunk[] chunks = new Chunk[worldSize * worldHeightChunks * worldSize];

            Parallel.For(0, chunks.Length, idx =>
            {
                int x = idx / (worldSize * worldHeightChunks);
                int z = (idx / worldHeightChunks) % worldSize;
                int y = idx % worldHeightChunks;

                (byte[] resultVD, List<int> resultOOB) = terrain.GenerateChunk(new System.Numerics.Vector3(x, y, z));

                outOfBounds[(x, y, z)] = resultOOB;
                associatedTuples[(x, y, z)] = idx;

                chunks[idx] = new Chunk
                {
                    coords = new Vector3i(x, y, z),
                    voxelData = resultVD,
                    size = chunkSize,
                };
            });

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

                    int targetChunkX = chunkX + Math.DivRem(x, chunkSize, out int localX);
                    int targetChunkY = chunkY + Math.DivRem(y, chunkSize, out int localY);
                    int targetChunkZ = chunkZ + Math.DivRem(z, chunkSize, out int localZ);

                    if (localX < 0) { localX += chunkSize; targetChunkX--; }
                    if (localY < 0) { localY += chunkSize; targetChunkY--; }
                    if (localZ < 0) { localZ += chunkSize; targetChunkZ--; }

                    localX = (localX + chunkSize) % chunkSize;
                    localY = (localY + chunkSize) % chunkSize;
                    localZ = (localZ + chunkSize) % chunkSize;
                    int voxelId = localZ + chunkSize * (localY + chunkSize * localX);

                    // early exits
                    if (targetChunkX < 0 || targetChunkY < 0 || targetChunkZ < 0) continue;
                    if (!associatedTuples.TryGetValue((targetChunkX, targetChunkY, targetChunkZ), out long chunkId)) continue;
                    if (voxelId < 0 || voxelId >= chunkSize * chunkSize * chunkSize) continue;

                    chunks[chunkId].voxelData[voxelId] = (byte)id;
                }
            }

            watch.Stop();
            generationTime = watch.ElapsedMilliseconds;

            watch.Start();

            Parallel.For(0, chunks.Length, idx =>
            {
                var chunk = chunks[idx];

                terrain.GenerateSVO(chunk.voxelData, false);
            });


            // upload voxel chunks
            for (int c = 0; c < chunks.Length; c++)
                chunks[c].textureId = UploadVoxelChunk(chunks[c].voxelData, chunkSize);

            _chunks = chunks;

            watch.Stop();
            svoTime = watch.ElapsedMilliseconds - generationTime;

            // build shader
            program = BuildShader();
            GL.UseProgram(program);

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
        double interactionTickBuildup = 0;

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            base.OnUpdateFrame(args);

            double deltaTime = args.Time;
            interactionTickBuildup = Math.Clamp(interactionTickBuildup + deltaTime, 0, 0.25);
            var input = KeyboardState;
            var mouse = MouseState;

            yaw -= mouse.Delta.X * mouseSensitivity;
            pitch -= mouse.Delta.Y * mouseSensitivity;
            pitch = MathHelper.Clamp(pitch, -MathF.PI / 2 + 0.01f, MathF.PI / 2 - 0.01f);

            float delta = moveSpeed * (float)deltaTime;
            if (input.IsKeyDown(Keys.LeftShift)) delta *= 0.07f;

            Vector3 moveDir = Vector3.Zero;
            if (input.IsKeyDown(Keys.W)) moveDir += cameraForward;
            if (input.IsKeyDown(Keys.S)) moveDir -= cameraForward;
            if (input.IsKeyDown(Keys.A)) moveDir -= camRight;
            if (input.IsKeyDown(Keys.D)) moveDir += camRight;
            if (input.IsKeyDown(Keys.Space)) moveDir += Vector3.UnitY;

            Vector3 nextPos = camPos;
            nextPos.X += moveDir.X * delta;
            if (!VoxelUtil.CollidesAt(nextPos, _chunks, false))
            {
                camPos.X = nextPos.X;
            }

            nextPos = camPos;
            nextPos.Y += moveDir.Y * delta;
            if (!VoxelUtil.CollidesAt(nextPos, _chunks, false))
            {
                camPos.Y = nextPos.Y;
            }

            nextPos = camPos;
            nextPos.Z += moveDir.Z * delta;
            if (!VoxelUtil.CollidesAt(nextPos, _chunks, false))
            {
                camPos.Z = nextPos.Z;
            }

            Vector3 hitPos, hitNormal;
            int hitId, cId, vId;
            VoxelUtil.Raycast(camPos, cameraForward, _chunks, chunkSize, worldSize, 10, 5f, false, out hitPos, out hitNormal, out hitId, out vId, out cId);

            List<int> chunksUpdated = new List<int>();
            List<List<int>> updatedVoxelPositions = new List<List<int>>();
            while (updatedVoxelPositions.Count <= cId)
                updatedVoxelPositions.Add(new List<int>());

            // break
            if (hitId > -1 && mouse.IsButtonDown(MouseButton.Left) && interactionTickBuildup == 0.25)
            {
                interactionTickBuildup = 0;
                _chunks[cId].voxelData[vId] = 254;

                updatedVoxelPositions[cId].Add(vId);
                chunksUpdated.Add(cId);
            }
            
            // place
            if (hitId > -1 && hitId < 251 && mouse.IsButtonDown(MouseButton.Right) && interactionTickBuildup == 0.25)
            {
                interactionTickBuildup = 0;
                int vx = (int)(vId / (chunkSize * chunkSize) + hitNormal.X);
                int vy = (int)((vId / chunkSize) % chunkSize + hitNormal.Y);
                int vz = (int)(vId % chunkSize + hitNormal.Z);

                int newVID = vz + chunkSize * (vy + chunkSize * vx);
                //Console.WriteLine(newVID);

                _chunks[cId].voxelData[newVID] = 2;
                updatedVoxelPositions[cId].Add(newVID);
                chunksUpdated.Add(cId);
            }

            Stopwatch debug = Stopwatch.StartNew();

            foreach (int listChunk in chunksUpdated)
            {
                Console.WriteLine("updating");
                foreach (int vId1 in updatedVoxelPositions[listChunk])
                {
                    int vx = vId1 / (chunkSize * chunkSize);
                    int vy = (vId1 / chunkSize) % chunkSize;
                    int vz = vId1 % chunkSize;
                    terrain.PartialSVOUpdate(_chunks[listChunk].voxelData, vx, vy, vz);
                }
                UpdateChunkTexture(listChunk);
            }

            debug.Stop();
            if (debug.ElapsedTicks > 10)
            {
                Console.WriteLine(debug.ElapsedTicks);
            }


            if (input.IsKeyDown(Keys.Escape))
            {
                CursorState = CursorState.Normal;
            }
        }
        private float iTime = 0.0f;

        void UpdateChunkTexture(int chunkId)
        {
            GL.BindTexture(TextureTarget.Texture3D, _chunks[chunkId].textureId);
            GL.TexSubImage3D(
                TextureTarget.Texture3D,
                0,
                0, 0, 0,
                chunkSize, chunkSize, chunkSize,
                OpenTK.Graphics.OpenGL.PixelFormat.RedInteger,
                PixelType.UnsignedByte,
                _chunks[chunkId].voxelData
            );
        }

        private const int MaxFrames = 300; // last 300 frames
        private float[] frameTimes = new float[MaxFrames];
        private int frameIndex = 0;
        private int frameCountStored = 0;


        protected override void OnRenderFrame(FrameEventArgs args)
        {

            // Store frame time in circular array
            frameTimes[frameIndex] = (float)args.Time;
            frameIndex = (frameIndex + 1) % MaxFrames;
            frameCountStored = Math.Min(frameCountStored + 1, MaxFrames);

            // Copy to temp array for sorting
            float[] sortedTimes = new float[frameCountStored];
            Array.Copy(frameTimes, sortedTimes, frameCountStored);
            Array.Sort(sortedTimes);

            float avgTime = sortedTimes.Average();

            int onePctIndex = (int)Math.Ceiling(0.99f * frameCountStored) - 1;
            onePctIndex = Math.Clamp(onePctIndex, 0, frameCountStored - 1);
            float low1Pct = sortedTimes[onePctIndex];

            // 0.1% low = 0.1% slowest frames → near the very end
            int pointOnePctIndex = (int)Math.Ceiling(0.999f * frameCountStored) - 1;
            pointOnePctIndex = Math.Clamp(pointOnePctIndex, 0, frameCountStored - 1);
            float low01Pct = sortedTimes[pointOnePctIndex];

            string fpsString = string.Format(
                "FPS avg: {0:F0}, 1% low: {1:F0}, 0.1% low: {2:F0}",
                1f / avgTime,
                1f / low1Pct,
                1f / low01Pct
            );


            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.DepthTest);

            GL.Clear(ClearBufferMask.ColorBufferBit);

            GL.UseProgram(program);
            GL.BindVertexArray(vao);

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

            float marginScaleX = 10f / width;
            float marginScaleY = 10f / height;
            float lineSpacingScale = 30f / height;
            float textSize = 1.0f;

            int voxelCount = (int)((_chunks != null ? _chunks.Length : 1) * Math.Pow(chunkSize, 3));

            _textRenderer.RenderText(
                $"{fpsString}\n" +
                $"world: {voxelCount / 10e5:f1}m voxels {worldSize}x{worldSize}x{worldSize} chunks\n"+
                $"Terrain generated in: {generationTime:f1}ms with an average of {(generationTime) / (_chunks != null ? _chunks.Length : 1):f2}ms per chunk\n" +
                $"SVO and chunk upload in: {svoTime:f1}ms with an average of {(svoTime) / (_chunks != null ? _chunks.Length : 1):f2}ms per chunk\n" +
                $"system: {width}x{height}, {Environment.ProcessorCount} processors, {(voxelCount * 1) / 1024 / 1024}MiB for voxels\n",
                marginScaleX,
                marginScaleY,
                240, 240, 240,
                textSize
                );

            _textRenderer.RenderText(
                $"pos: {Math.Round(camPos.X, 2)}, {Math.Round(camPos.Y, 2)}, {Math.Round(camPos.Z, 2)}",
                marginScaleX,
                marginScaleY + lineSpacingScale * 6,
                240, 180, 50,
                textSize
            );

            _textRenderer.RenderText(
                "°",
                0.5f,
                0.5f,
                240, 240, 240,
                0.6f
            );

            // Example for bottom-right HUD element (normalized coordinates)
            _textRenderer.RenderText(
                $"[1] - Grass Block",
                10f / width,
                0.98f - _textRenderer.GetHeight(1.0f) / height,
                255, 255, 255,
                textSize
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