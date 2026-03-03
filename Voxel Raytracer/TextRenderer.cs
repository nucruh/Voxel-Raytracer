using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using System;
using System.Drawing; // GDI+ Namespace
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using static System.Net.Mime.MediaTypeNames;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

// This replaces the complex SharpFont TextRenderer
public class GdiTextRenderer : IDisposable
{
    private int _fontTexture;
    private int _program, _vao, _vbo;
    private int _windowWidth;
    private int _windowHeight;
    private string _lastText = "";
    private Size _lastSize;
    private float _textWidth;
    private float _textHeight;
    private System.Drawing.Font _font;
    private PrivateFontCollection _fonts;


    public GdiTextRenderer(int width, int height, string fontName, float fontSize)
    {
        _windowWidth = width;
        _windowHeight = height;


        _fonts = new PrivateFontCollection();
        _fonts.AddFontFile("Monocraft.ttc");

        _font = new System.Drawing.Font(_fonts.Families[0], fontSize, System.Drawing.FontStyle.Bold);

        _program = LoadTextShader("shaders/text.vert", "shaders/text.frag");

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();

        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);

        int stride = sizeof(float) * 4;

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

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    private void GenerateTextTexture(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            if (_fontTexture != 0) GL.DeleteTexture(_fontTexture);
            _fontTexture = 0;
            _lastText = "";
            return;
        }

        // Only regenerate texture if text changed
        if (text == _lastText) return;
        _lastText = text;

        string[] lines = text.Split('\n');

        // Measure the width and height of each line
        int textureWidth = 0;
        int textureHeight = 0;

        using var measureBmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(measureBmp);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        SizeF[] lineSizes = new SizeF[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            lineSizes[i] = g.MeasureString(lines[i], _font, PointF.Empty, StringFormat.GenericTypographic);
            textureWidth = Math.Max(textureWidth, (int)Math.Ceiling(lineSizes[i].Width));
            textureHeight += (int)Math.Ceiling(lineSizes[i].Height);
        }

        // Add some padding
        textureWidth += 20;
        textureHeight += 4;

        _textWidth = textureWidth;
        _textHeight = textureHeight;

        // Draw all lines into a single bitmap
        using var bitmap = new Bitmap(textureWidth, textureHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.Clear(Color.Transparent);
        graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        float y = 0;
        foreach (string line in lines)
        {
            graphics.DrawString(line, _font, Brushes.White, new PointF(2, y));
            y += g.MeasureString(line, _font, PointF.Empty, StringFormat.GenericTypographic).Height;
        }

        // Flip vertically for OpenGL
        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY);

        // Upload to OpenGL
        if (_fontTexture == 0)
            _fontTexture = GL.GenTexture();

        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);

        BitmapData data = bitmap.LockBits(
            new Rectangle(0, 0, bitmap.Width, bitmap.Height),
            ImageLockMode.ReadOnly,
            PixelFormat.Format32bppArgb);

        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
            bitmap.Width, bitmap.Height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
            PixelType.UnsignedByte, data.Scan0);

        bitmap.UnlockBits(data);

        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);

        GL.BindTexture(TextureTarget.Texture2D, 0);
    }


    [System.Diagnostics.CodeAnalysis.SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "<Pending>")]
    public float GetHeight(float sizeScale)
    {
        string text = "0";

        if (string.IsNullOrEmpty(text))
            return 0f;

        // Temporary bitmap/graphics for measuring
        using var bmp = new Bitmap(1, 1);
        using var g = Graphics.FromImage(bmp);

        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

        // Measure string
        SizeF measuredSize = g.MeasureString(text, _font, new PointF(0, 0), StringFormat.GenericTypographic);

        // Scale by sizeScale
        return measuredSize.Height * sizeScale;
    }

    public void RenderText(string text, float xScale, float yScale, float colorR, float colorG, float colorB, float sizeScale = 1f)
    {
        colorR = Math.Clamp(colorR / 255.0f, 0f, 1f);
        colorG = Math.Clamp(colorG / 255.0f, 0f, 1f);
        colorB = Math.Clamp(colorB / 255.0f, 0f, 1f);

        int lines = text.Split('\n').Length - 1;
        lines = lines == 0 ? 1 : lines; // avoid / 0

        GenerateTextTexture(text);
        if (_fontTexture == 0) return;

        float scaledWidth = _textWidth * sizeScale;
        float scaledHeight = _textHeight * sizeScale;

        float x = xScale * _windowWidth;
        float y = yScale * _windowHeight;

        float shadowOffset = scaledHeight / lines * 0.06f; // smaller, proportional

        // Render shadow (full multi-line)
        RenderTextQuad(x + shadowOffset, y + shadowOffset, scaledWidth, scaledHeight, 0f, 0f, 0f, 0.5f);

        // Render main text
        RenderTextQuad(x, y, scaledWidth, scaledHeight, colorR, colorG, colorB, 1f);
    }
    private void RenderTextQuad(float x, float y, float width, float height, float r, float g, float b, float a)
    {
        float x0 = x;
        float y0 = y;
        float x1 = x + width;
        float y1 = y + height;

        float[] vertices = new float[]
        {
        x0, y0, 0f, 1f,
        x0, y1, 0f, 0f,
        x1, y1, 1f, 0f,

        x1, y1, 1f, 0f,
        x1, y0, 1f, 1f,
        x0, y0, 0f, 1f
        };

        GL.Enable(EnableCap.Blend);
        GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
        GL.Disable(EnableCap.DepthTest);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StreamDraw);

        GL.UseProgram(_program);
        GL.BindVertexArray(_vao);

        int colorLoc = GL.GetUniformLocation(_program, "uColor");
        GL.Uniform4(colorLoc, r, g, b, a);

        GL.ActiveTexture(TextureUnit.Texture0);
        GL.BindTexture(TextureTarget.Texture2D, _fontTexture);

        GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

        GL.BindVertexArray(0);
        GL.BindTexture(TextureTarget.Texture2D, 0);
        GL.UseProgram(0);

        GL.Enable(EnableCap.DepthTest);
        GL.Disable(EnableCap.Blend);
    }

    public void Resize(int newWidth, int newHeight)
    {
        _windowWidth = newWidth;
        _windowHeight = newHeight;

        GL.UseProgram(_program);

        int screenSizeLoc = GL.GetUniformLocation(_program, "uScreenSize");
        GL.Uniform2(screenSizeLoc, (float)_windowWidth, (float)_windowHeight);

        float newSize = Math.Clamp((float)Math.Round(newHeight / 270f) * 5f, 5f, 100f);

        _font = new System.Drawing.Font(_fonts.Families[0], newSize, System.Drawing.FontStyle.Bold);

        GL.UseProgram(0);
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