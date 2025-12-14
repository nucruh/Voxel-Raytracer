#version 330 core

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 TexCoord;

uniform vec2 uScreenSize;

void main()
{
    // Convert input pixel coordinates (x, y) to Normalized Device Coordinates (NDC)
    // NDC ranges from -1 to 1.
    vec2 ndc = (aPosition / uScreenSize) * 2.0 - 1.0;
    
    // We flip the Y axis to match standard top-down GUI drawing convention.
    gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);
    TexCoord = aTexCoord;
}