#version 330 core
out vec4 FragColor;

uniform vec2 iResolution;
uniform float iTime;

uniform vec3 uCamPos;
uniform vec3 uCamForward;
uniform vec3 uCamRight;
uniform vec3 uCamUp;

uniform usampler3D uVoxelTex[64];
uniform usamplerBuffer uChunkHeaders;
uniform ivec3 uVoxelDim;

uniform sampler2DArray uBlockTextures;

float fogEnd = 200.0;
float fogStart = 50.0;
float shadowDistance = 150.0;

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

const int MAX_STEPS = 300;
const vec3 SUN_DIR = normalize(vec3(1.0, -1.0, 0.5));
const int SHADOW_STEPS = 48;
const float SHADOW_BIAS = 0.01;

const vec3 GRASS_LIGHT_NORMAL = -SUN_DIR;
const vec3 FOLIAGE_TINT = vec3(110.0 / 255.0, 166.0 / 255.0, 94.0 / 255.0);

vec3 GetBlockNormal(int mask, ivec3 step){
    if(mask == 0)      return vec3(-step.x, 0, 0);
    else if(mask == 1) return vec3(0, -step.y, 0);
    else               return vec3(0, 0, -step.z);
}

// ---------- utilities ----------
float hash21(vec2 p){
    p = fract(p * vec2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y);
}

vec3 safeDeltaDist(vec3 rd){
    const float EPS = 1e-6;
    return abs(vec3(
        1.0 / (abs(rd.x) > EPS ? rd.x : (rd.x < 0.0 ? -EPS : EPS)),
        1.0 / (abs(rd.y) > EPS ? rd.y : (rd.y < 0.0 ? -EPS : EPS)),
        1.0 / (abs(rd.z) > EPS ? rd.z : (rd.z < 0.0 ? -EPS : EPS))
    ));
}

// ---------- godrays ----------
float ComputeGodRaysFast(vec3 ro, vec3 rd, float maxDist){
    float stepSize = maxDist / float(GODRAY_STEPS);
    float illum = 0.0;
    float trans = 1.0;

    for(int i=0;i<GODRAY_STEPS;i++){
        illum += trans * GODRAY_DENSITY;
        trans *= GODRAY_DECAY;
    }
    return illum;
}

// ---------- chunk skip ----------
int SkipEmptyChunk(
    inout ivec3 pos,
    inout vec3 sideDist,
    vec3 deltaDist,
    ivec3 step,
    inout float t
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

    if(tx <= ty && tx <= tz){
        t = tx;
        pos.x += step.x * nx;
        sideDist.x = tx + deltaDist.x;
        return 0;
    }else if(ty <= tz){
        t = ty;
        pos.y += step.y * ny;
        sideDist.y = ty + deltaDist.y;
        return 1;
    }else{
        t = tz;
        pos.z += step.z * nz;
        sideDist.z = tz + deltaDist.z;
        return 2;
    }
}

// Sine wave function for animating the grass
float Wave(float time, float frequency, float amplitude, float position) {
    return amplitude * sin(frequency * position + time);  // Adjusted for animation speed
}


const float ROT = 0.70710678;

bool IntersectGrass(
    vec3 ro, vec3 rd, ivec3 voxel,
    float maxT, out float tHit, out vec3 nHit, out float alpha
){
    vec3 o = ro - vec3(voxel);

    float bestT = maxT;
    vec3 bestN = vec3(0.0);
    alpha = 0.0;
    bool hit = false;

    const float W = 0.5;
    const float ROT = 0.70710678; // cos/sin 45°

    // wind
    float windSpeed = 1.2;
    float windStrength = 0.05;
    float windPhase = hash21(vec2(voxel.x, voxel.z)) * 6.28318;

    // two crossed planes rotated 45°
    vec3 planes[2] = vec3[2](
        normalize(vec3( ROT, 0.0,  ROT)),
        normalize(vec3(-ROT, 0.0,  ROT))
    );

    for(int i = 0; i < 2; i++){
        vec3 n = planes[i];

        float denom = dot(rd, n);
        if(abs(denom) < 1e-6) continue;

        float t = dot(vec3(0.5) - o, n) / denom;
        if(t <= 0.0 || t >= bestT) continue;

        vec3 p = o + rd * t;

        if(p.y < 0.0 || p.y > 1.0) continue;

        // wind sway
        float h = clamp(p.y, 0.0, 1.0);
        float sway = sin(iTime * windSpeed + windPhase + float(i) * 1.57)
                     * windStrength * h;

        p += n * sway;

        // width test
        vec3 d = p - vec3(0.5);
        float side = dot(d, vec3(-n.z, 0.0, n.x));
        if(abs(side) > W) continue;

        // UVs
        vec2 texUV = vec2(
            side * 0.5 + 0.5,
            p.y
        );

        float a = texture(uBlockTextures, vec3(texUV, 7)).a;
        if(a > 0.0){
            bestT = t;
            bestN = n;
            alpha = a;
            hit = true;
        }
    }

    tHit = bestT;
    nHit = bestN;
    return hit;
}


// ---------- shadow trace ----------
bool TraceShadow(vec3 startPos){
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

    float t = 0.0;

    for(int i=0;i<SHADOW_STEPS;i++){
        if(any(lessThan(mapPos, ivec3(0))) ||
           mapPos.x>=WORLD_VOX_X || mapPos.y>=WORLD_VOX_Y || mapPos.z>=WORLD_VOX_Z)
            return false;

        int cx = mapPos.x>>CHUNK_PWR;
        int cy = mapPos.y>>CHUNK_PWR;
        int cz = mapPos.z>>CHUNK_PWR;

        int chunkIndex = cx*(WORLD_SIZE_Y*WORLD_SIZE_Z)+cz*WORLD_SIZE_Y+cy;
        uint header = texelFetch(uChunkHeaders, chunkIndex).r;

        if(header == 255u){
            SkipEmptyChunk(mapPos, sideDist, deltaDist, step, t);
            continue;
        }

        uint id = texelFetch(
            uVoxelTex[chunkIndex],
            ivec3(mapPos.z&CHUNK_MASK,mapPos.y&CHUNK_MASK,mapPos.x&CHUNK_MASK),
            0).r;

        if(id != 254u && (id < 128u || id > 130u)) return true;

        if(sideDist.x<sideDist.y){
            if(sideDist.x<sideDist.z){ sideDist.x+=deltaDist.x; mapPos.x+=step.x; }
            else{ sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
        }else{
            if(sideDist.y<sideDist.z){ sideDist.y+=deltaDist.y; mapPos.y+=step.y; }
            else{ sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
        }
    }
    return false;
}

// ---------- main ----------
void main(){
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
    vec3 normal = vec3(0.0);
    bool hit = false;
    int mask = -1;
    float t = 0.0;
    float hitDist = 0.0;

    for(int i=0;i<MAX_STEPS;i++){
        if(any(lessThan(mapPos, ivec3(0))) ||
           mapPos.x>=WORLD_VOX_X || mapPos.y>=WORLD_VOX_Y || mapPos.z>=WORLD_VOX_Z)
            break;

        int cx = mapPos.x>>CHUNK_PWR;
        int cy = mapPos.y>>CHUNK_PWR;
        int cz = mapPos.z>>CHUNK_PWR;
        int chunkIndex = cx*(WORLD_SIZE_Y*WORLD_SIZE_Z)+cz*WORLD_SIZE_Y+cy;

        uint header = texelFetch(uChunkHeaders, chunkIndex).r;
        if(header == 255u){
            mask = SkipEmptyChunk(mapPos, sideDist, deltaDist, step, t);
            continue;
        }

        uint id = texelFetch(
            uVoxelTex[chunkIndex],
            ivec3(mapPos.z&CHUNK_MASK,mapPos.y&CHUNK_MASK,mapPos.x&CHUNK_MASK),
            0).r;

        if(id < 131u && id > 127u){
float tg;
vec3 gN;
float ga;

vec3 grassCol = vec3(0.0);
float accumAlpha = 0.0;
float firstHitT = 0.0;

// -------- PASS 1 --------
if(IntersectGrass(uCamPos, rd, mapPos, 1000.0, tg, gN, ga)){
    vec3 hitPos = uCamPos + rd * tg;
    vec3 localPos = clamp(hitPos - vec3(mapPos), 0.0, 1.0);

    vec2 texUV = abs(gN.x) > 0.0
        ? vec2(localPos.z, localPos.y)
        : vec2(localPos.x, localPos.y);

    vec4 texSample = texture(uBlockTextures, vec3(texUV, float(id)));

    grassCol += texSample.rgb * texSample.a;
    grassCol *= FOLIAGE_TINT;
    accumAlpha += texSample.a;
    firstHitT = tg;

    // -------- PASS 2 (only if not opaque) --------
    if(texSample.a < 0.99){
        float tg2;
        vec3 gN2;
        float ga2;

        vec3 ro2 = uCamPos + rd * (tg + 0.002);

        if(IntersectGrass(ro2, rd, mapPos, 1000.0, tg2, gN2, ga2)){
            vec3 hitPos2 = ro2 + rd * tg2;
            vec3 localPos2 = clamp(hitPos2 - vec3(mapPos), 0.0, 1.0);

            vec2 texUV2 = abs(gN2.x) > 0.0
                ? vec2(localPos2.z, localPos2.y)
                : vec2(localPos2.x, localPos2.y);

            vec4 texSample2 = texture(uBlockTextures, vec3(texUV2, float(id)));

            float a2 = texSample2.a * (1.0 - accumAlpha);
            grassCol += texSample2.rgb * a2;
            grassCol *= FOLIAGE_TINT;
            accumAlpha += a2;
        }
    }

    hitCol = mix(hitCol, grassCol, accumAlpha);

if(accumAlpha >= 0.99){
    hit = true;
    hitDist = firstHitT;
    normal = GRASS_LIGHT_NORMAL;
    break;
}
}
        }
        else if(id != 254u && id != 255u){
            hit = true;
            hitDist = t - 0.0005;

            vec3 hitPos = uCamPos + rd * hitDist;
            vec3 local = fract(hitPos);

            vec2 texUV;
            if(mask==0)      texUV = local.zy;
            else if(mask==1) texUV = local.xz;
            else             texUV = local.xy;

            vec2 texSize = vec2(textureSize(uBlockTextures,0).xy);
            vec2 dx = dFdx(texUV * texSize);
            vec2 dy = dFdy(texUV * texSize);
            float mip = max(0.0, 0.5*log2(max(dot(dx,dx), dot(dy,dy))));

            hitCol = textureLod(uBlockTextures, vec3(texUV,float(id)), mip).rgb;
            if (id == 2u)
            {
                hitCol *= FOLIAGE_TINT;
            }

            if(mask==0) normal = vec3(-step.x,0,0);
            else if(mask==1) normal = vec3(0,-step.y,0);
            else normal = vec3(0,0,-step.z);
            break;
        }

        if(sideDist.x<sideDist.y){
            if(sideDist.x<sideDist.z){ t=sideDist.x; sideDist.x+=deltaDist.x; mapPos.x+=step.x; mask=0; }
            else{ t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; mask=2; }
        }else{
            if(sideDist.y<sideDist.z){ t=sideDist.y; sideDist.y+=deltaDist.y; mapPos.y+=step.y; mask=1; }
            else{ t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; mask=2; }
        }
    }

    vec3 sky = mix(vec3(0.72,0.89,1), vec3(0.67,0.84,1), rd.y*0.5+0.5);
    vec3 color = sky;

    if(hit){
        vec3 hitPos = uCamPos + rd*hitDist;
        float diff = max(dot(normal,-SUN_DIR),0.0);

        float shadow;
        if (hitDist < shadowDistance)
        {
            shadow = TraceShadow(hitPos)?0.25:0.9;
        }
        else
        {
            shadow = 0.9;
        }

        color = hitCol * (0.6 + diff*shadow); //* FOLIAGE_TINT;

        float fog = clamp(exp(-(hitDist - fogEnd) * 0.05), 0.0, 1.0);

        fog *= smoothstep(0.0, 1.0, (hitDist - fogEnd) / (fogStart - fogEnd));
        color = mix(sky,color,fog);
    }

    float maxRay = hit?hitDist:float(MAX_STEPS);
    float god = ComputeGodRaysFast(uCamPos,rd,maxRay);
    god *= pow(max(dot(rd,-SUN_DIR),0.0),3.0);
    god *= smoothstep(0.0,1.0,maxRay/80.0);

    color += vec3(1.0,0.95,0.85) * god * GODRAY_INTENSITY;
    color += (hash21(gl_FragCoord.xy)-0.5)*0.03;
    color = mix(color,smoothstep(0.0,1.0,color),0.4);
    color = pow(color,vec3(0.9));

    FragColor = vec4(color,1.0);
}
