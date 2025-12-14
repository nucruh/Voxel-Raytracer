#version 330 core

in vec2 TexCoord;
out vec4 FragColor;

uniform sampler2D uTexture; // The font atlas texture
uniform vec4 uColor;        // The desired color of the text

void main()
{
    // Sample the font atlas texture
    vec4 sampled = texture(uTexture, TexCoord);
    
    // The alpha channel of the sampled texture (usually R or A is used for monochrome fonts)
    // determines the presence of the character.
    float alpha = sampled.r; 
    
    // Output color is the desired uniform color, multiplied by the sampled alpha.
    FragColor = uColor * alpha;
}