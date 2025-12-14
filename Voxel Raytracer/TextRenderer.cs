using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System.Drawing; // GDI+ Namespace
using System.Drawing.Imaging;
using System;
using System.IO;

// This replaces the complex SharpFont TextRenderer
public class GdiTextRenderer : IDisposable
{
    private int _fontTexture;
    private int _program, _vao, _vbo;
    private readonly int _windowWidth;
    private readonly int _windowHeight;
    private string _lastText = "";
    private Size _lastSize;
    private float _textWidth;
    private float _textHeight;
    private System.Drawing.Font _font;


    public GdiTextRenderer(int width, int height, string fontName, float fontSize)
    {
        _windowWidth = width;
        _windowHeight = height;

        // Use a standard font file.
        // NOTE: This relies on the font being installed on the system OR finding the .ttf file
        // For simplicity, we use the font family name.
        _font = new System.Drawing.Font(fontName, fontSize, System.Drawing.FontStyle.Regular);

        // 1. Setup Quad Shaders (same as the previous text.vert/text.frag)
        _program = LoadTextShader("shaders/text.vert", "shaders/text.frag");

        // 2. Setup VAO/VBO for a single, full-screen text quad
        // We render a quad that dynamically resizes to fit the text content.
        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int stride = sizeof(float) * 4; // Position(2) + UV(2)

        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, 0);

        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(1, 2, VertexAttribPointerType.Float, false, stride, sizeof(float) * 2);

        GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        GL.BindVertexArray(0);

        // 3. Setup Shaders Uniforms
        GL.UseProgram(_program);
        GL.Uniform1(GL.GetUniformLocation(_program, "uTexture"), 0);
        GL.Uniform2(GL.GetUniformLocation(_program, "uScreenSize"), (float)_windowWidth, (float)_windowHeight);
    }

    private int LoadTextShader(string vertPath, string fragPath)
    {
        // Reusing the robust shader loading logic from your Renderer class
        int vert = LoadShader(vertPath, ShaderType.VertexShader);
        int frag = LoadShader(fragPath, ShaderType.FragmentShader);

        int program = GL.CreateProgram();
        GL.AttachShader(program, vert);
        GL.AttachShader(program, frag);
        GL.LinkProgram(program);

        return program;
    }

    private int LoadShader(string path, ShaderType type)
    {
        int shader = GL.CreateShader(type);
        GL.ShaderSource(shader, File.ReadAllText(path));
        GL.CompileShader(shader);

        GL.GetShader(shader, ShaderParameter.CompileStatus, out int ok);
        if (ok == 0)
        {
            Console.WriteLine($"Shader compilation failed for {path}: {GL.GetShaderInfoLog(shader)}");
            throw new Exception("Shader compilation failed.");
        }
        return shader;
    }

    private void GenerateTextTexture(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
            _fontTexture = 0;
            _lastText = "";
            return;
        }

        // Use Graphics.MeasureString to determine the size of the required bitmap
        // Use a high-quality Bitmap (32bpp) for smooth text and transparency
        using var tempBitmap = new Bitmap(1, 1);
        using var tempGraphics = Graphics.FromImage(tempBitmap);

        // TextFormatFlags.NoPadding helps get a tight bound.
#pragma warning disable CA1416 // Validate platform compatibility
        SizeF measuredSize = tempGraphics.MeasureString(text, _font, new System.Drawing.PointF(0, 0), StringFormat.GenericTypographic);
#pragma warning restore CA1416 // Validate platform compatibility

        // Add a small padding for safety
        int textureWidth = (int)Math.Ceiling(measuredSize.Width) + 20;
        int textureHeight = (int)Math.Ceiling(measuredSize.Height) + 4;

        // Only regenerate texture if size or text content changes
        if (text == _lastText && _lastSize.Width == textureWidth && _lastSize.Height == textureHeight)
        {
            return;
        }
        _lastText = text;
        _lastSize = new Size(textureWidth, textureHeight);

        // 1. Draw text onto the Bitmap
        using var bitmap = new Bitmap(textureWidth, textureHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
        graphics.Clear(System.Drawing.Color.Transparent); // Make sure the background is transparent

        // Draw the text
        graphics.DrawString(text, _font, System.Drawing.Brushes.White, new PointF(2, 2)); // Draw with white color onto the bitmap

        // 2. Upload to OpenGL
        if (_fontTexture == 0)
        {
            _fontTexture = GL.GenTexture();
        }

        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);

        // Flip the image vertically for OpenGL compatibility
        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);

        BitmapData data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            bitmap.Width, bitmap.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
            PixelType.UnsignedByte, data.Scan0);

        bitmap.UnlockBits(data);

        // Set texture parameters
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        _textWidth = textureWidth;
        _textHeight = textureHeight;
    }

    public void RenderText(string text, float x, float y, float colorR, float colorG, float colorB)
    {
        GenerateTextTexture(text);

        if (_fontTexture == 0) return;

        // Vertices for a single quad dynamically sized to the text texture
        // Position: (x, y) coordinates for top-left corner
        // Size: (_textWidth, _textHeight)

        float x0 = x;
        float y0 = y;
        float x1 = x + _textWidth;
        float y1 = y + _textHeight;

        // Quad vertices (Position and UVs)
        float[] vertices = new float[]
        {
            // Position (2f), UV (2f)
            x0, y0, 0f, 1f, // Top-Left
            x0, y1, 0f, 0f, // Bottom-Left
            x1, y1, 1f, 0f, // Bottom-Right

            x1, y1, 1f, 0f, // Bottom-Right
            x1, y0, 1f, 1f, // Top-Right
            x0, y0, 0f, 1f  // Top-Left
        };

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.DepthTest);

        // Upload and Draw
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.DepthTest);

        GL.UseProgram(_program);
        GL.BindVertexArray(_vao);

        // Set uniform color (uses white brush in GDI+ but we tint it here)
        int colorLoc = GL.GetUniformLocation(_program, "uColor");
        GL.Uniform4(colorLoc, colorR, colorG, colorB, 1.0f);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

        // Cleanup / Reset state
        GL.Disable(EnableCap.Blend);
        GL.Enable(EnableCap.DepthTest);
        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        // --- Cleanup / Reset State ---
        GL.UseProgram(0);
        GL.BindVertexArray(0);

        // Unbind the text texture from Unit 0 (CRITICAL)
        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, 0);

        GL.Disable(EnableCap.Blend);
    }

    public void Dispose()
    {
        if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
        if (_vbo != 0) GL.DeleteBuffer(_vbo);
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_program != 0) GL.DeleteProgram(_program);
        _font?.Dispose();
    }
}