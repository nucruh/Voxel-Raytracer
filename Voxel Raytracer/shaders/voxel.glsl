#version 330 core
out vec4 FragColor;

uniform vec2 iResolution;
uniform float iTime;

uniform vec3 uCamPos;
uniform vec3 uCamForward;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

uniform usampler3D uVoxelTex[64];
uniform ivec3 uVoxelDim;

uniform sampler2DArray uBlockTextures;

const int CHUNK_SIZE = 128;
const int CHUNK_PWR  = 7;
const int CHUNK_MASK = CHUNK_SIZE - 1;

const int WORLD_SIZE_X = 4;
const int WORLD_SIZE_Y = 4;
const int WORLD_SIZE_Z = 4;

const int WORLD_VOX_X = WORLD_SIZE_X * CHUNK_SIZE;
const int WORLD_VOX_Y = WORLD_SIZE_Y * CHUNK_SIZE;
const int WORLD_VOX_Z = WORLD_SIZE_Z * CHUNK_SIZE;

const int GODRAY_STEPS = 16;
const float GODRAY_DENSITY = 0.025;
const float GODRAY_DECAY = 0.99;
const float GODRAY_INTENSITY = 1.2;

const int MAX_STEPS = 768;
const vec3 SUN_DIR = normalize(vec3(1.0, -1.0, 0.5));
const int SHADOW_STEPS = 48;
const float SHADOW_BIAS = 0.01;

// ---------- utilities ----------

float hash21(vec2 p) {
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

float hash31(ivec3 p) {
    uint x = uint(p.x);
    uint y = uint(p.y);
    uint z = uint(p.z);

    uint h = x*374761393u + y*668265263u + z*2147483647u; // large primes
    h = (h ^ (h >> 13u)) * 1274126177u;
    return float(h & 0x00FFFFFFu) / float(0x01000000u);
}

vec3 blockColor(uint id) {
    if (id==0u) return vec3(200,200,200)/255.0;
    if (id==1u) return vec3(124,94,74)/255.0;
    if (id==2u) return vec3(142,167,47)/255.0;
    if (id==3u) return vec3(238,238,238)/255.0;
    if (id==4u) return vec3(102,51,0)/255.0;
    if (id==5u) return vec3(141,180,105)/255.0;
    if (id==6u) return vec3(125,150,49)/255.0;
    return vec3(1,0,1);
}

// safe reciprocal to avoid INF / zero-step
vec3 safeDeltaDist(vec3 rd) {
    const float EPS = 1e-6;
    return abs(vec3(
        1.0 / (abs(rd.x) > EPS ? rd.x : (rd.x < 0.0 ? -EPS : EPS)),
        1.0 / (abs(rd.y) > EPS ? rd.y : (rd.y < 0.0 ? -EPS : EPS)),
        1.0 / (abs(rd.z) > EPS ? rd.z : (rd.z < 0.0 ? -EPS : EPS))
    ));
}

float ComputeGodRaysFast(vec3 rayOrigin, vec3 rayDir, float maxDist)
{
    float stepSize = maxDist / float(GODRAY_STEPS);
    float illumination = 0.0;
    float transmittance = 1.0;

    for(int i = 0; i < GODRAY_STEPS; i++)
    {
        float t = stepSize * float(i);
        illumination += transmittance * GODRAY_DENSITY;
        transmittance *= GODRAY_DECAY;
    }

    return illumination;
}

// ---------- FIXED chunk skip ----------
int SkipEmptyChunk(
    inout ivec3 pos,
    inout vec3 sideDist,
    vec3 deltaDist,
    ivec3 step
){
    int lx = pos.x & CHUNK_MASK;
    int ly = pos.y & CHUNK_MASK;
    int lz = pos.z & CHUNK_MASK;

    int nx = (step.x > 0) ? (CHUNK_SIZE - lx) : (lx + 1);
    int ny = (step.y > 0) ? (CHUNK_SIZE - ly) : (ly + 1);
    int nz = (step.z > 0) ? (CHUNK_SIZE - lz) : (lz + 1);

    float tx = sideDist.x + float(nx - 1) * deltaDist.x;
    float ty = sideDist.y + float(ny - 1) * deltaDist.y;
    float tz = sideDist.z + float(nz - 1) * deltaDist.z;

    if (tx <= ty && tx <= tz) {
        pos.x += step.x * nx;
        sideDist.x = tx + deltaDist.x;
        return 0;
    } else if (ty <= tz) {
        pos.y += step.y * ny;
        sideDist.y = ty + deltaDist.y;
        return 1;
    } else {
        pos.z += step.z * nz;
        sideDist.z = tz + deltaDist.z;
        return 2;
    }
}

// ---------- shadow trace (same logic) ----------
bool TraceShadow(vec3 startPos) {
    vec3 rd = -SUN_DIR;
    vec3 posf = startPos + rd * SHADOW_BIAS;

    ivec3 mapPos = ivec3(floor(posf));
    vec3 deltaDist = safeDeltaDist(rd);
    ivec3 step = ivec3(rd.x>0?1:-1, rd.y>0?1:-1, rd.z>0?1:-1);

    vec3 sideDist = vec3(
        ((rd.x<0)?(posf.x-float(mapPos.x)):(float(mapPos.x+1)-posf.x))*deltaDist.x,
        ((rd.y<0)?(posf.y-float(mapPos.y)):(float(mapPos.y+1)-posf.y))*deltaDist.y,
        ((rd.z<0)?(posf.z-float(mapPos.z)):(float(mapPos.z+1)-posf.z))*deltaDist.z
    );

    for(int i=0;i<SHADOW_STEPS;i++) {
        if(any(lessThan(mapPos, ivec3(0))) ||
           mapPos.x>=WORLD_VOX_X || mapPos.y>=WORLD_VOX_Y || mapPos.z>=WORLD_VOX_Z)
            return false;

        int cx = mapPos.x>>CHUNK_PWR;
        int cy = mapPos.y>>CHUNK_PWR;
        int cz = mapPos.z>>CHUNK_PWR;

        int chunkIndex = cx*(WORLD_SIZE_Y*WORLD_SIZE_Z)+cz*WORLD_SIZE_Y+cy;
        uint header = texelFetch(uVoxelTex[chunkIndex], ivec3(0,0,0),0).r;

        if(header==255u) {
            SkipEmptyChunk(mapPos, sideDist, deltaDist, step);
            continue;
        }

        uint id = texelFetch(uVoxelTex[chunkIndex],
                             ivec3(mapPos.z&CHUNK_MASK,
                                   mapPos.y&CHUNK_MASK,
                                   mapPos.x&CHUNK_MASK),0).r;
        if(id!=254u) return true;

        if(sideDist.x<sideDist.y){
            if(sideDist.x<sideDist.z){ sideDist.x+=deltaDist.x; mapPos.x+=step.x; }
            else { sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
        } else {
            if(sideDist.y<sideDist.z){ sideDist.y+=deltaDist.y; mapPos.y+=step.y; }
            else { sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
        }
    }
    return false;
}

// ---------- main ----------
void main() {
    vec2 uv = (gl_FragCoord.xy/iResolution)*2.0-1.0;
    uv.x *= iResolution.x/iResolution.y;

    vec3 rd = normalize(uCamForward + uv.x*uCamRight + uv.y*uCamUp);

    ivec3 mapPos = ivec3(floor(uCamPos));
    vec3 deltaDist = safeDeltaDist(rd);
    ivec3 step = ivec3(rd.x>0?1:-1, rd.y>0?1:-1, rd.z>0?1:-1);

    vec3 sideDist = vec3(
        ((rd.x<0)?(uCamPos.x-float(mapPos.x)):(float(mapPos.x+1)-uCamPos.x))*deltaDist.x,
        ((rd.y<0)?(uCamPos.y-float(mapPos.y)):(float(mapPos.y+1)-uCamPos.y))*deltaDist.y,
        ((rd.z<0)?(uCamPos.z-float(mapPos.z)):(float(mapPos.z+1)-uCamPos.z))*deltaDist.z
    );

    vec3 hitCol = vec3(0.0);
    bool hit = false;
    int mask = -1;
    float t = 0.0;
    float hitDist = 0.0;

    for(int i=0;i<MAX_STEPS;i++) {
        if(any(lessThan(mapPos, ivec3(0))) ||
           mapPos.x>=WORLD_VOX_X || mapPos.y>=WORLD_VOX_Y || mapPos.z>=WORLD_VOX_Z)
            break;

        int cx = mapPos.x>>CHUNK_PWR;
        int cy = mapPos.y>>CHUNK_PWR;
        int cz = mapPos.z>>CHUNK_PWR;

        int chunkIndex = cx*(WORLD_SIZE_Y*WORLD_SIZE_Z)+cz*WORLD_SIZE_Y+cy;
        uint header = texelFetch(uVoxelTex[chunkIndex], ivec3(0,0,0),0).r;

        if(header==255u) {
            mask = SkipEmptyChunk(mapPos, sideDist, deltaDist, step);
            continue;
        }

        uint id = texelFetch(uVoxelTex[chunkIndex],
                             ivec3(mapPos.z&CHUNK_MASK,
                                   mapPos.y&CHUNK_MASK,
                                   mapPos.x&CHUNK_MASK),0).r;

        if(id != 254u)
        {
            hit = true;
            hitDist = t;

            // --- compute hit position ---
            vec3 hitPos = uCamPos + rd * hitDist;
            vec3 local = fract(hitPos); // [0..1] inside voxel

            // --- face-based UVs ---
            vec2 texUV;
            if(mask == 0)      texUV = local.zy;
            else if(mask == 1) texUV = local.xz;
            else               texUV = local.xy;

            // --- compute mip level based on screen-space derivatives ---
            vec2 texSize = vec2(textureSize(uBlockTextures, 0).xy);
            vec2 dx = dFdx(texUV * texSize);
            vec2 dy = dFdy(texUV * texSize);
            float mipLevel = 0.5 * log2(max(dot(dx, dx), dot(dy, dy)));
            mipLevel = max(mipLevel, 0.0);

            // --- sample texture array using mipmaps ---
            vec3 texCol = textureLod(uBlockTextures, vec3(texUV, float(id)), mipLevel).rgb;

            // --- subtle per-voxel variation (optional) ---
            texCol *= 1.0 + (hash31(mapPos) - 0.5) * 0.05;

            hitCol = texCol;
            break;
        }


        if(sideDist.x<sideDist.y){
            if(sideDist.x<sideDist.z){ t=sideDist.x; sideDist.x+=deltaDist.x; mapPos.x+=step.x; mask=0; }
            else { t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; mask=2; }
        } else {
            if(sideDist.y<sideDist.z){ t=sideDist.y; sideDist.y+=deltaDist.y; mapPos.y+=step.y; mask=1; }
            else { t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; mask=2; }
        }
    }

    vec3 skyColor = mix(vec3(0.72,0.89,1), vec3(0.67,0.84,1.0), rd.y*0.5+0.5);
    vec3 color = skyColor;


    if(hit) {
        vec3 normal = vec3(0.0);
        if(mask==0) normal.x = -float(step.x);
        else if(mask==1) normal.y = -float(step.y);
        else normal.z = -float(step.z);

        vec3 hitPos = uCamPos + rd*hitDist;
        float diff = max(dot(normal,-SUN_DIR),0.0);
        float shadow = TraceShadow(hitPos) ? 0.25 : 1.0;
        color = hitCol * (0.6 + diff*shadow);
        color = mix(skyColor, color, 1.0 - hitDist/float(MAX_STEPS));
    }

    float maxRayDist = hit ? hitDist : float(MAX_STEPS);
    // cheap volumetric godrays
    float godrays = ComputeGodRaysFast(uCamPos, rd, maxRayDist);
    // only visible when looking toward sun
    float sunView = max(dot(rd, -SUN_DIR), 0.0); godrays *= pow(sunView, 3.0);
    // depth fade so rays fade out for distant pixels
    godrays *= smoothstep(0.0, 1.0, maxRayDist / 80.0);
    vec3 sunColor = vec3(1.0, 0.95, 0.85);
    color += sunColor * godrays * GODRAY_INTENSITY;
    float grain = hash21(gl_FragCoord.xy);
    color += (grain-0.5)*0.03;
    color = mix(color, smoothstep(0.0,1.0,color),0.4);
    color = pow(color, vec3(0.9));

    FragColor = vec4(color,1.0);
}