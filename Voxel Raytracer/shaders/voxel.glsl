#version 330 core

out vec4 FragColor;

uniform vec2 iResolution;
uniform float iTime;

uniform vec3 uCamPos;
uniform vec3 uCamForward;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

uniform sampler3D uVoxelTex[4];
uniform ivec3 uVoxelDim;      // voxel chunk dimensions

const int CHUNK_SIZE = 128;

// Per-chunk offsets
const vec3 chunkOffsets[4] = vec3[4](vec3(0,0,0), vec3(CHUNK_SIZE,0,0), vec3(0,0,CHUNK_SIZE), vec3(CHUNK_SIZE,0,CHUNK_SIZE));

// Get per-chunk max bounds
vec3 chunkGridMax(int chunkIndex)
{
    return chunkOffsets[chunkIndex] + vec3(CHUNK_SIZE);
}

// Convert world position to voxel index in chunk
ivec3 worldToVoxel(vec3 p, int chunkIndex)
{
    // Adjust for the chunk's position offset in all three axes
    vec3 localPos = p - chunkOffsets[chunkIndex];

    // Return the voxel indices after clamping them to the chunk bounds
    return ivec3(
        clamp(floor(localPos.x), 0.0, CHUNK_SIZE - 1.0),  // X-axis
        clamp(floor(localPos.y), 0.0, CHUNK_SIZE - 1.0),  // Y-axis
        clamp(floor(localPos.z), 0.0, CHUNK_SIZE - 1.0)   // Z-axis
    );
}

const int CHUNK_COUNT = uVoxelTex.length;
const int CHUNK_SIDE = int(floor(sqrt(uVoxelTex.length)));

// Ray-AABB intersection for a single chunk
bool intersectAABBChunk(vec3 ro, vec3 rd, vec3 bMin, vec3 bMax, out float tmin, out float tmax)
{
    vec3 invD = 1.0 / rd;
    vec3 t0s = (bMin - ro) * invD;
    vec3 t1s = (bMax - ro) * invD;

    vec3 tsmaller = min(t0s, t1s);
    vec3 tbigger = max(t0s, t1s);

    tmin = max(max(tsmaller.x, tsmaller.y), tsmaller.z);
    tmax = min(min(tbigger.x, tbigger.y), tbigger.z);

    return tmax >= max(tmin, 0.0);
}

// Ray-AABB intersection over all chunks
bool intersectChunks(vec3 ro, vec3 rd, out float tmin, out float tmax)
{
    tmin = 1e30;
    tmax = -1e30;
    bool hit = false;
    for(int i = 0; i < CHUNK_COUNT; i++)
    {
        float t0, t1;
        if(intersectAABBChunk(ro, rd, chunkOffsets[i], chunkGridMax(i), t0, t1))
        {
            if(t0 < tmin) tmin = t0;
            if(t1 > tmax) tmax = t1;
            hit = true;
        }
    }
    return hit;
}

void updateChunkProperties(int index, out vec3 gridMin, out vec3 gridMax, out vec3 cellSiz) {
    gridMin = chunkOffsets[index];
    gridMax = chunkGridMax(index);
    // Note: Assuming uVoxelDim is the *size* of the voxel texture, which should equal CHUNK_SIZE
    // if the voxel texture perfectly maps to the chunk.
    cellSiz = (gridMax - gridMin) / vec3(uVoxelDim); 
    // If uVoxelDim is supposed to be ivec3(CHUNK_SIZE) this simplifies to:
    // cellSiz = vec3(CHUNK_SIZE) / vec3(uVoxelDim);
}

void main()
{
    vec2 uv = (gl_FragCoord.xy / iResolution) * 2.0 - 1.0;
    uv.x *= iResolution.x / iResolution.y;

    vec3 ro = uCamPos;
    vec3 rd = normalize(uCamForward + uv.x * uCamRight + uv.y * uCamUp);

    vec3 skyColor = mix(vec3(0.6, 0.7, 0.9), vec3(0.85, 0.9, 1.0), rd.y * 0.5 + 0.5);

    float tmin, tmax;
    if(!intersectChunks(ro, rd, tmin, tmax))
    {
        FragColor = vec4(skyColor, 1.0);
        return;
    }

    float t = max(tmin, 0.0);
    vec3 pos = ro + rd * t;

    // Determine which chunk we start in
    int chunkIndex = int(floor(pos.x / CHUNK_SIZE)) + int(floor(pos.z / CHUNK_SIZE)) * CHUNK_SIDE;
    chunkIndex = clamp(chunkIndex, 0, uVoxelTex.length - 1);

    vec3 curGridMin = chunkOffsets[chunkIndex];
    vec3 curGridMax = chunkGridMax(chunkIndex);
    vec3 cellSize = (curGridMax - curGridMin) / vec3(uVoxelDim);

    #define UPDATE_CHUNK_PROPERTIES() \
        curGridMin = chunkOffsets[chunkIndex]; \
        curGridMax = chunkGridMax(chunkIndex); \
        cellSize = (curGridMax - curGridMin) / vec3(uVoxelDim);

    UPDATE_CHUNK_PROPERTIES(); // Initial update

    ivec3 ijk = worldToVoxel(pos, chunkIndex);

    ivec3 step;
    vec3 tMax;
    vec3 tDelta;

    for(int i = 0; i < uVoxelTex.length -1; i++) // maybe should just be 4??
    {
        if(rd[i] > 0.0)
        {
            step[i] = 1;
            float voxelBorder = curGridMin[i] + (float(ijk[i]) + 1.0) * cellSize[i];
            tMax[i] = (voxelBorder - ro[i]) / rd[i];
            tDelta[i] = cellSize[i] / rd[i];
        }
        else if(rd[i] < 0.0)
        {
            step[i] = -1;
            float voxelBorder = curGridMin[i] + float(ijk[i]) * cellSize[i];
            tMax[i] = (voxelBorder - ro[i]) / rd[i];
            tDelta[i] = -cellSize[i] / rd[i];
        }
        else
        {
            step[i] = 0;
            tMax[i] = 1e30;
            tDelta[i] = 1e30;
        }
    }

    const int MAX_STEPS = 512;

    bool hit = false;
    ivec3 hitIJK = ivec3(0);
    vec3 hitColor = vec3(1.0);
    int axisStepped = -1;

    for(int s = 0; s < MAX_STEPS; s++)
    {
        int oldChunkIndex = chunkIndex;

        // If voxel index goes outside current chunk, switch chunk
        if (ijk.x < 0) {
            chunkIndex--;
            ijk.x = CHUNK_SIZE - 1;
        }

        if (ijk.x >= CHUNK_SIZE) {
            chunkIndex++;
            ijk.x = 0;
        }

        if (ijk.z < 0) {
            chunkIndex -= CHUNK_SIDE;
            ijk.z = CHUNK_SIZE - 1;
        }

        if (ijk.z >= CHUNK_SIZE) {
            chunkIndex += CHUNK_SIDE;
            ijk.z = 0;
        }



        //chunkIndex = clamp(chunkIndex, 0, CHUNK_COUNT - 1);


        if (chunkIndex != oldChunkIndex)
        {
            UPDATE_CHUNK_PROPERTIES(); // IMPORTANT: Update coordinates for the new chunk
        }

        if (chunkIndex < 0 || chunkIndex > CHUNK_COUNT - 1)
        {
            hit = false;
            break;
        }

        int currentChunk = clamp(chunkIndex, 0, CHUNK_COUNT - 1); // Safety clamp

        // Sample texture directly using ijk and the correct texture index
        // vec3 uvw = (vec3(ijk) + 0.5) / vec3(uVoxelDim); // This is in the original shader's voxelAtWorld, but uVoxelDim is an ivec3, which is fine.
        vec4 tex = texture(uVoxelTex[currentChunk], (vec3(ijk) + 0.5) / vec3(uVoxelDim));

        if (tex.a > 0.5)
        {
            hit = true;
            hitColor = tex.rgb;
            hitIJK = ijk;
            break;
        }

        // DDA step
        if(tMax.x < tMax.y)
        {
            if(tMax.x < tMax.z)
            {
                ijk.x += step.x; 
                t = tMax.x; 
                tMax.x += tDelta.x; 
                axisStepped = 0;
            }
            else
            {
                ijk.z += step.z; 
                t = tMax.z; 
                tMax.z += tDelta.z; 
                axisStepped = 2;
            }
        }
        else
        {
            if(tMax.y < tMax.z)
            {
                ijk.y += step.y; 
                t = tMax.y; 
                tMax.y += tDelta.y; 
                axisStepped = 1;
            }
            else
            {
                ijk.z += step.z; 
                t = tMax.z; 
                tMax.z += tDelta.z; 
                axisStepped = 2;
            }
        }

        if(t > tmax) break;
    }

    vec3 color = skyColor;

    if(hit)
    {
        vec3 N = vec3(0.0);
        if(axisStepped == 0) N.x = -step.x;
        else if(axisStepped == 1) N.y = -step.y;
        else if(axisStepped == 2) N.z = -step.z;

        vec3 lightDir = normalize(vec3(-1.0, 1.0, 0));
        float diff = max(dot(N, lightDir), 0.0);
        float ambient = 0.8;

        color = hitColor * (ambient + (1.0 - ambient) * diff);
    }

    FragColor = vec4(color, 1.0);
}
