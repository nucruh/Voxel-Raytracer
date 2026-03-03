#version 330 core

in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D uTexture;
uniform vec4 uColor;

void main()
{
    vec4 sampled = texture(uTexture, TexCoord);
    float alpha = sampled.r; 
    
    FragColor = uColor * alpha;
}