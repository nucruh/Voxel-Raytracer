#version 330 core

out vec4 FragColor;

uniform vec2 iResolution;
uniform float iTime;

uniform vec3 uCamPos;
uniform vec3 uCamForward;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

uniform sampler3D uVoxelTex; // voxel chunk texture
uniform ivec3 uVoxelDim;     // voxel chunk dimensions

const vec3 gridMin = vec3(-5.0, -5.0, -5.0);
const vec3 gridMax = vec3(5.0, 5.0, 5.0);

ivec3 worldToVoxel(vec3 p)
{
    vec3 t = (p - gridMin) / (gridMax - gridMin);
    return ivec3(clamp(floor(t * vec3(uVoxelDim)), vec3(0), vec3(uVoxelDim - 1)));
}

// sample voxel texture and return occupancy + color
bool voxelAt(ivec3 ijk, out vec3 color)
{
    vec3 uvw = (vec3(ijk) + 0.5) / vec3(uVoxelDim);
    vec4 tex = texture(uVoxelTex, uvw);
    color = tex.rgb;
    return tex.a > 0.5;
}

bool intersectAABB(vec3 ro, vec3 rd, out float tmin, out float tmax)
{
    vec3 invD = 1.0 / rd;
    vec3 t0s = (gridMin - ro) * invD;
    vec3 t1s = (gridMax - ro) * invD;

    vec3 tsmaller = min(t0s, t1s);
    vec3 tbigger = max(t0s, t1s);

    tmin = max(max(tsmaller.x, tsmaller.y), tsmaller.z);
    tmax = min(min(tbigger.x, tbigger.y), tbigger.z);

    return tmax >= max(tmin, 0.0);
}

void main()
{
    vec2 uv = (gl_FragCoord.xy / iResolution) * 2.0 - 1.0;
    uv.x *= iResolution.x / iResolution.y;

    vec3 ro = uCamPos;
    vec3 rd = normalize(uCamForward + uv.x * uCamRight + uv.y * uCamUp);

    vec3 skyColor = mix(vec3(0.6, 0.7, 0.9), vec3(0.85, 0.9, 1.0), rd.y * 0.5 + 0.5);

    float tmin, tmax;
    if(!intersectAABB(ro, rd, tmin, tmax))
    {
        FragColor = vec4(skyColor, 1.0);
        return;
    }

    float t = max(tmin, 0.0);
    vec3 pos = ro + rd * t;
    ivec3 ijk = worldToVoxel(pos);

    vec3 cellSize = (gridMax - gridMin) / vec3(uVoxelDim);

    ivec3 step;
    vec3 tMax;
    vec3 tDelta;

    for(int i = 0; i < 3; i++)
    {
        if(rd[i] > 0.0)
        {
            step[i] = 1;
            float voxelBorder = gridMin[i] + (float(ijk[i]) + 1.0) * cellSize[i];
            tMax[i] = (voxelBorder - ro[i]) / rd[i];
            tDelta[i] = cellSize[i] / rd[i];
        }
        else if(rd[i] < 0.0)
        {
            step[i] = -1;
            float voxelBorder = gridMin[i] + float(ijk[i]) * cellSize[i];
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

    const int MAX_STEPS = 256;
    bool hit = false;
    ivec3 hitIJK = ivec3(0);
    vec3 hitColor = vec3(1.0);
    int axisStepped = -1;

    for(int s = 0; s < MAX_STEPS; s++)
    {
        if(ijk.x < 0 || ijk.y < 0 || ijk.z < 0 ||
           ijk.x >= uVoxelDim.x || ijk.y >= uVoxelDim.y || ijk.z >= uVoxelDim.z) break;

        if(voxelAt(ijk, hitColor))
        {
            hit = true;
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
        // Face-aligned normal from last DDA step
        vec3 N = vec3(0.0);
        if(axisStepped == 0) N.x = -step.x;
        else if(axisStepped == 1) N.y = -step.y;
        else if(axisStepped == 2) N.z = -step.z;

        vec3 lightDir = normalize(vec3(-1.0, 1.0, 0));
        float diff = max(dot(N, lightDir), 0.0);
        float ambient = 0.2;

        color = hitColor * (ambient + (1.0 - ambient) * diff);
    }

    FragColor = vec4(color, 1.0);
}