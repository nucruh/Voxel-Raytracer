#version 330 core

layout (location = 0) in vec2 aPosition;
layout (location = 1) in vec2 aTexCoord;

out vec2 TexCoord;

uniform vec2 uScreenSize;

void main()
{
    vec2 ndc = (aPosition / uScreenSize) * 2.0 - 1.0;
    
    gl_Position = vec4(ndc.x, -ndc.y, 0.0, 1.0);
    TexCoord = aTexCoord;
}