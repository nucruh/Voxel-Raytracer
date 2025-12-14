#version 330 core

out vec4 FragColor;

uniform vec2 iResolution;
uniform float iTime;

uniform vec3 uCamPos;
uniform vec3 uCamForward;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

uniform sampler3D uVoxelTex[64];
uniform ivec3 uVoxelDim; // chunkSize, chunkSize, chunkSize

const int WORLD_SIZE_X = int(pow(float(uVoxelTex.length), 1.0/3.0));  // worldSize
const int WORLD_SIZE_Z = int(pow(float(uVoxelTex.length), 1.0/3.0));  // worldSize
const int WORLD_SIZE_Y = int(pow(float(uVoxelTex.length), 1.0/3.0));  // worldHeightChunks

const int CHUNK_SIZE = 128;
const int CHUNK_PWR  = 7;
const int CHUNK_MASK = CHUNK_SIZE - 1;

const int WORLD_VOX_X = WORLD_SIZE_X * CHUNK_SIZE;
const int WORLD_VOX_Y = WORLD_SIZE_Y * CHUNK_SIZE;
const int WORLD_VOX_Z = WORLD_SIZE_Z * CHUNK_SIZE;

const int MAX_STEPS = (WORLD_SIZE_X + WORLD_SIZE_Y + WORLD_SIZE_Z) * CHUNK_SIZE;

void main()
{
    // Ray setup
    vec2 uv = (gl_FragCoord.xy / iResolution) * 2.0 - 1.0;
    uv.x *= iResolution.x / iResolution.y;
    vec3 rd = normalize(uCamForward + uv.x * uCamRight + uv.y * uCamUp);

    // DDA init
    ivec3 mapPos = ivec3(floor(uCamPos));
    vec3 deltaDist = abs(1.0 / rd);
    ivec3 stepDir = ivec3(sign(rd));

    vec3 sideDist;
    sideDist.x = (rd.x < 0.0) ? (uCamPos.x - float(mapPos.x)) * deltaDist.x
                              : (float(mapPos.x + 1) - uCamPos.x) * deltaDist.x;
    sideDist.y = (rd.y < 0.0) ? (uCamPos.y - float(mapPos.y)) * deltaDist.y
                              : (float(mapPos.y + 1) - uCamPos.y) * deltaDist.y;
    sideDist.z = (rd.z < 0.0) ? (uCamPos.z - float(mapPos.z)) * deltaDist.z
                              : (float(mapPos.z + 1) - uCamPos.z) * deltaDist.z;

    bool hit = false;
    vec3 hitColor = vec3(0.0);
    int mask = 0;
    int iter = 0;

    for (int i = 0; i < MAX_STEPS; i++)
    {
        // ---- WORLD BOUNDS (CRITICAL) ----
        if (mapPos.x < 0 || mapPos.y < 0 || mapPos.z < 0 ||
            mapPos.x >= WORLD_VOX_X ||
            mapPos.y >= WORLD_VOX_Y ||
            mapPos.z >= WORLD_VOX_Z)
        {
            // outside world → skip sampling
        }
        else
        {
            // ---- CHUNK COORDS ----
            int cx = mapPos.x >> CHUNK_PWR;
            int cy = mapPos.y >> CHUNK_PWR;
            int cz = mapPos.z >> CHUNK_PWR;

            // ---- LOCAL VOXEL COORDS ----
            int lx = mapPos.x & CHUNK_MASK;
            int ly = mapPos.y & CHUNK_MASK;
            int lz = mapPos.z & CHUNK_MASK;

            // ---- MATCHES C# INDEX EXACTLY ----
            int chunkIndex =
                cx * (WORLD_SIZE_Y * WORLD_SIZE_Z) +
                cz * (WORLD_SIZE_Y) +
                cy;

            vec3 uvw = (vec3(lz, ly, lx) + 0.5) / float(CHUNK_SIZE);
            vec4 tex = texture(uVoxelTex[chunkIndex], uvw);

            if (tex.a > 0.5)
            {
                hit = true;
                hitColor = tex.rgb;
                iter = i;
                break;
            }
        }

        // ---- DDA STEP ----
        if (sideDist.x < sideDist.y)
        {
            if (sideDist.x < sideDist.z)
            {
                sideDist.x += deltaDist.x;
                mapPos.x += stepDir.x;
                mask = 0;
            }
            else
            {
                sideDist.z += deltaDist.z;
                mapPos.z += stepDir.z;
                mask = 2;
            }
        }
        else
        {
            if (sideDist.y < sideDist.z)
            {
                sideDist.y += deltaDist.y;
                mapPos.y += stepDir.y;
                mask = 1;
            }
            else
            {
                sideDist.z += deltaDist.z;
                mapPos.z += stepDir.z;
                mask = 2;
            }
        }
    }

    // ---- SHADING ----
    vec3 skyColor = mix(vec3(0.62, 0.79, 0.93),
                        vec3(0.57, 0.74, 0.90),
                        rd.y * 0.5 + 0.5);

    vec3 color = skyColor;

    if (hit)
    {
        vec3 normal = vec3(0.0);
        if (mask == 0) normal.x = -float(stepDir.x);
        else if (mask == 1) normal.y = -float(stepDir.y);
        else normal.z = -float(stepDir.z);

        float diff = max(dot(normal, normalize(vec3(-1.0, 1.0, 0.0))), 0.0);
        float ambient = 0.8;

        color = hitColor * (ambient + (1.0 - ambient) * diff);
        color = mix(skyColor, color, 1.0 - float(iter) / float(MAX_STEPS));
    }

    FragColor = vec4(color, 1.0);
}
