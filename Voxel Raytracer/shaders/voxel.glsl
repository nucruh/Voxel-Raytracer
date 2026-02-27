#version 430 core
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

float fogEnd = 300.0;
float fogStart = 50.0;
float shadowDistance = 150.0;
float grassDistance = 200.0;

const int CHUNK_SIZE = 128;
const int CHUNK_PWR  = 7;
const int CHUNK_MASK = CHUNK_SIZE - 1;

const float CLOUD_BOTTOM = 700.0;
const float CLOUD_TOP    = 900.0;
const int CLOUD_STEPS = 18;

const float CLOUD_BLOCK_SIZE = (CLOUD_TOP - CLOUD_BOTTOM) / float(CLOUD_STEPS);

const int SVO_SIZE = 16;
const int SVO_PWR  = 4;
const int SVO_MASK = SVO_SIZE - 1;


const int WORLD_SIZE_X = 4;
const int WORLD_SIZE_Y = 4;
const int WORLD_SIZE_Z = 4;

const int WORLD_VOX_X = WORLD_SIZE_X * CHUNK_SIZE;
const int WORLD_VOX_Y = WORLD_SIZE_Y * CHUNK_SIZE;
const int WORLD_VOX_Z = WORLD_SIZE_Z * CHUNK_SIZE;

const int GODRAY_STEPS = 16;
const float GODRAY_DENSITY = 0.015;
const float GODRAY_DECAY = 0.9;
const float GODRAY_INTENSITY = 5.2;

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
    const float ROT = 0.70710678; // cos(45°)

    // Wind parameters
    float windSpeed = 2.0;       // speed of sway
    float windStrength = 0.15;   // horizontal offset
    float windPhase = hash21(vec2(voxel.x, voxel.z)) * 6.28318;

    // Two crossed planes
    vec3 planes[2] = vec3[2](
        normalize(vec3( ROT, 0.0,  ROT)),
        normalize(vec3(-ROT, 0.0,  ROT))
    );

    for(int i = 0; i < 2; i++){
        vec3 n = planes[i];
        vec3 swayDir = vec3(-n.z, 0.0, n.x); // horizontal along blade

        float denom = dot(rd, n);
        if(abs(denom) < 1e-6) continue;

        // Plane intersection
        float t = dot(vec3(0.5) - o, n) / denom;
        if(t <= 0.0 || t >= bestT) continue;

        vec3 p = o + rd * t;
        if(p.y < 0.0 || p.y > 1.0) continue;

        // Width check
        vec3 d = p - vec3(0.5);
        float side = dot(d, swayDir);
        if(abs(side) > W) continue;

        // ------------------------
        // Compute swaying offset
        // ------------------------
        float swayFactor = pow(p.y, 2.0); // top moves more
        float wave = sin(iTime * windSpeed + windPhase + float(i) * 1.57) 
                     * windStrength * swayFactor;

        // Offset the intersection point for visual sway
        vec3 pSway = p + swayDir * wave;

        // UV coordinates for texture
        vec2 texUV;
        texUV.y = p.y; // vertical along blade
        texUV.x = clamp(dot(pSway - vec3(0.5), swayDir) / W * 0.5 + 0.5, 0.0, 1.0);

        // Sample texture
        vec4 texSample = texture(uBlockTextures, vec3(texUV, 7));

        if(texSample.a > 0.0){
            bestT = t;
            bestN = n;
            alpha = texSample.a;
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
        // Out of world
        if(any(lessThan(mapPos, ivec3(0))) ||
           mapPos.x>=WORLD_VOX_X || mapPos.y>=WORLD_VOX_Y || mapPos.z>=WORLD_VOX_Z)
            return false;

        int cx = mapPos.x>>CHUNK_PWR;
        int cy = mapPos.y>>CHUNK_PWR;
        int cz = mapPos.z>>CHUNK_PWR;
        int chunkIndex = cx*(WORLD_SIZE_Y*WORLD_SIZE_Z)+cz*WORLD_SIZE_Y+cy;

        uint id = texelFetch(
            uVoxelTex[chunkIndex],
            ivec3(mapPos.z&CHUNK_MASK,mapPos.y&CHUNK_MASK,mapPos.x&CHUNK_MASK),
            0
        ).r;

        // Check SVO sizes
       int svoSize = 1;
        int svoMask = 0;
        if(id == 253u) { svoSize = 16; svoMask = 15; }  // 16
        else if(id == 252u){ svoSize = 32; svoMask = 31; } // 32
        else if(id == 251u){ svoSize = 64; svoMask = 63; } // 64
        else if(id == 255u){svoSize = 128; svoMask = 127; } // 128

        
        if(svoMask != 0)
        {
            ivec3 regionBase = mapPos & ~svoMask;
            vec3 boxMin = vec3(regionBase);
            vec3 boxMax = boxMin + vec3(svoSize);

            // Ray-box intersection
            vec3 invDir = 1.0 / rd;
            vec3 t0 = (boxMin - uCamPos) * invDir;
            vec3 t1 = (boxMax - uCamPos) * invDir;

            vec3 tsmaller = min(t0, t1);
            vec3 tbigger  = max(t0, t1);

            float tEnter = max(max(tsmaller.x, tsmaller.y), tsmaller.z);
            float tExit  = min(min(tbigger.x, tbigger.y), tbigger.z);

            if(tExit <= tEnter) continue;

            // Use a very small epsilon to nudge into the next voxel
            float epsilon = 0.001; 
            t = tExit + epsilon; 

            // Sync the mapPos and sideDist to the NEW position after the jump
            vec3 jumpPos = uCamPos + rd * t;
            mapPos = ivec3(floor(jumpPos));

            // IMPORTANT: You must fully refresh sideDist here
            sideDist = (vec3(step) * (vec3(mapPos) - uCamPos + 0.5 + vec3(step) * 0.5)) * deltaDist;
    
            // Also re-calculate sideDist based on the standard DDA formula 
            // to ensure the next loop iteration doesn't use old data
            sideDist = vec3(
                ((rd.x < 0.0) ? (jumpPos.x - float(mapPos.x)) : (float(mapPos.x + 1.0) - jumpPos.x)) * deltaDist.x,
                ((rd.y < 0.0) ? (jumpPos.y - float(mapPos.y)) : (float(mapPos.y + 1.0) - jumpPos.y)) * deltaDist.y,
                ((rd.z < 0.0) ? (jumpPos.z - float(mapPos.z)) : (float(mapPos.z + 1.0) - jumpPos.z)) * deltaDist.z
            );

            continue; 
        }


        // Hit solid voxel
        if(id < 251u && (id < 128u || id > 130u)) return true;

        // Step to next voxel
        if(sideDist.x < sideDist.y){
            if(sideDist.x < sideDist.z){ sideDist.x+=deltaDist.x; mapPos.x+=step.x; }
            else{ sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
        }else{
            if(sideDist.y < sideDist.z){ sideDist.y+=deltaDist.y; mapPos.y+=step.y; }
            else{ sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
        }
    }

    return false;
}

// ---------- 3D noise & fbm ----------
float hash31(vec3 p){
    p = fract(p * vec3(123.34, 456.21, 789.56));
    p += dot(p, p + 45.32);
    return fract(p.x * p.y * p.z);
}

float noise3(vec3 p){
    vec3 i = floor(p);
    vec3 f = fract(p);
    f = f*f*(3.0 - 2.0*f);

    float n000 = hash31(i + vec3(0.0,0.0,0.0));
    float n100 = hash31(i + vec3(1.0,0.0,0.0));
    float n010 = hash31(i + vec3(0.0,1.0,0.0));
    float n110 = hash31(i + vec3(1.0,1.0,0.0));
    float n001 = hash31(i + vec3(0.0,0.0,1.0));
    float n101 = hash31(i + vec3(1.0,0.0,1.0));
    float n011 = hash31(i + vec3(0.0,1.0,1.0));
    float n111 = hash31(i + vec3(1.0,1.0,1.0));

    float nx00 = mix(n000, n100, f.x);
    float nx10 = mix(n010, n110, f.x);
    float nx01 = mix(n001, n101, f.x);
    float nx11 = mix(n011, n111, f.x);

    float nxy0 = mix(nx00, nx10, f.y);
    float nxy1 = mix(nx01, nx11, f.y);

    return mix(nxy0, nxy1, f.z);
}

float fbm3(vec3 p){
    float v = 0.0;
    float a = 0.5;
    for(int i=0;i<3;i++){
        v += noise3(p) * a;
        p *= 2.0;
        a *= 0.5;
    }
    return v;
}

// ---------- volumetric cloud density ----------
float SampleCloudDensity(vec3 p)
{
    float blockSize = CLOUD_BLOCK_SIZE;
    vec3 blockPos = floor(p / blockSize);
    vec3 localPos = fract(p / blockSize); 

    // 3D FBM for density
    vec3 noisePos = blockPos * 0.04 + iTime * 0.02;
    float density = fbm3(noisePos);

    density = step(0.5, density);

    float yFactor = smoothstep(CLOUD_BOTTOM, CLOUD_BOTTOM + blockSize, p.y) *
                    smoothstep(CLOUD_TOP, CLOUD_TOP - blockSize, p.y);

    return density * yFactor;
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

    for(int i = 0; i < MAX_STEPS; i++){
        // World bounds check
        if(any(lessThan(mapPos, ivec3(0))) ||
           mapPos.x >= WORLD_VOX_X || mapPos.y >= WORLD_VOX_Y || mapPos.z >= WORLD_VOX_Z)
            break;

        int cx = mapPos.x >> CHUNK_PWR;
        int cy = mapPos.y >> CHUNK_PWR;
        int cz = mapPos.z >> CHUNK_PWR;
        int chunkIndex = cx*(WORLD_SIZE_Y*WORLD_SIZE_Z) + cz*WORLD_SIZE_Y + cy;

        ivec3 localMapPos = mapPos & CHUNK_MASK;

        // Fetch voxel ID
        uint id = texelFetch(
            uVoxelTex[chunkIndex],
            ivec3(localMapPos.z, localMapPos.y, localMapPos.x),
            0
        ).r;

        // Determine SVO size for this voxel
        int svoSize = 1;
        int svoMask = 0;
        if(id == 253u) { svoSize = 16; svoMask = 15; }  // 16
        else if(id == 252u){ svoSize = 32; svoMask = 31; } // 32
        else if(id == 251u){ svoSize = 64; svoMask = 63; } // 64
        else if(id == 255u){svoSize = 128; svoMask = 127; } // 128

        
        if(svoMask != 0)
        {
            ivec3 regionBase = mapPos & ~svoMask;
            vec3 boxMin = vec3(regionBase);
            vec3 boxMax = boxMin + vec3(svoSize);

            // Ray-box intersection
            vec3 invDir = 1.0 / rd;
            vec3 t0 = (boxMin - uCamPos) * invDir;
            vec3 t1 = (boxMax - uCamPos) * invDir;

            vec3 tsmaller = min(t0, t1);
            vec3 tbigger  = max(t0, t1);

            float tEnter = max(max(tsmaller.x, tsmaller.y), tsmaller.z);
            float tExit  = min(min(tbigger.x, tbigger.y), tbigger.z);

            if(tExit <= tEnter) continue;

            // Use a very small epsilon to nudge into the next voxel
            float epsilon = 0.001; 
            t = tExit + epsilon; 

            // Sync the mapPos and sideDist to the NEW position after the jump
            vec3 jumpPos = uCamPos + rd * t;
            mapPos = ivec3(floor(jumpPos));

            // IMPORTANT: You must fully refresh sideDist here
            sideDist = (vec3(step) * (vec3(mapPos) - uCamPos + 0.5 + vec3(step) * 0.5)) * deltaDist;
    
            // Also re-calculate sideDist based on the standard DDA formula 
            // to ensure the next loop iteration doesn't use old data
            sideDist = vec3(
                ((rd.x < 0.0) ? (jumpPos.x - float(mapPos.x)) : (float(mapPos.x + 1.0) - jumpPos.x)) * deltaDist.x,
                ((rd.y < 0.0) ? (jumpPos.y - float(mapPos.y)) : (float(mapPos.y + 1.0) - jumpPos.y)) * deltaDist.y,
                ((rd.z < 0.0) ? (jumpPos.z - float(mapPos.z)) : (float(mapPos.z + 1.0) - jumpPos.z)) * deltaDist.z
            );

            continue; 
        }

        // ---------- Grass intersection ----------

        bool isGrass = id == 128u || id == 129u || id == 130u;
        if(isGrass && (distance(vec3(mapPos), uCamPos) < grassDistance)){
            float tg; vec3 gN; float ga;
            vec3 grassCol = vec3(0.0);
            float accumAlpha = 0.0;
            float firstHitT = 0.0;

            if(IntersectGrass(uCamPos, rd, mapPos, 1000.0, tg, gN, ga)){
                vec3 hitPos = uCamPos + rd * tg;
                vec3 localPos = clamp(hitPos - vec3(mapPos), 0.0, 1.0);
                vec2 texUV = abs(gN.x) > 0.0 ? vec2(localPos.z, localPos.y) : vec2(localPos.x, localPos.y);
                vec4 texSample = texture(uBlockTextures, vec3(texUV, float(id)));

                grassCol += texSample.rgb * texSample.a * FOLIAGE_TINT;
                accumAlpha += texSample.a;
                firstHitT = tg;

                // Second pass if not opaque
                if(texSample.a < 0.99){
                    float tg2; vec3 gN2; float ga2;
                    vec3 ro2 = uCamPos + rd * (tg + 0.002);
                    if(IntersectGrass(ro2, rd, mapPos, 1000.0, tg2, gN2, ga2)){
                        vec3 hitPos2 = ro2 + rd * tg2;
                        vec3 localPos2 = clamp(hitPos2 - vec3(mapPos), 0.0, 1.0);
                        vec2 texUV2 = abs(gN2.x) > 0.0 ? vec2(localPos2.z, localPos2.y) : vec2(localPos2.x, localPos2.y);
                        vec4 texSample2 = texture(uBlockTextures, vec3(texUV2, float(id)));
                        float a2 = texSample2.a * (1.0 - accumAlpha);
                        grassCol += texSample2.rgb * a2 * FOLIAGE_TINT;
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
        else if(id < 251u && !isGrass) {
            hit = true;
    
            // 1. Get the normal FIRST so we can use it for Mip/UV calculations
            normal = GetBlockNormal(mask, step);

            // 2. Stable hit distance calculation
            // Instead of using the 't' accumulator, calculate distance to the hit face plane.
            // This prevents "shimmering" or "stair-stepping" in the texture LOD.
            if (mask == 0)      hitDist = (float(mapPos.x + (step.x <= 0 ? 1 : 0)) - uCamPos.x) / rd.x;
            else if (mask == 1) hitDist = (float(mapPos.y + (step.y <= 0 ? 1 : 0)) - uCamPos.y) / rd.y;
            else                hitDist = (float(mapPos.z + (step.z <= 0 ? 1 : 0)) - uCamPos.z) / rd.z;

            vec3 hitPos = uCamPos + rd * (hitDist);
            vec3 local = fract(hitPos);
            vec2 texUV = (mask==0) ? local.zy : (mask==1) ? local.xz : local.xy;

            // 3. Analytically calculate Mip Level
            vec2 texSize = vec2(textureSize(uBlockTextures, 0).xy);
    
            // TUNING: Increase 'sharpness' if it's too blurry, decrease if aliasing.
            // 1.5 to 2.0 is usually the sweet spot for voxel engines.
            float sharpness = 2.0; 
            float K = (1.0 / iResolution.y) * sharpness; 

            // Calculate footprint: how many world-units a pixel covers
            // We use max() to prevent division by zero on glancing angles
            float angleCorrection = max(abs(dot(rd, normal)), 0.001);
            float footprint = (hitDist * K) / angleCorrection;

            // Scale footprint by texture resolution (e.g., 16x16) to find texel coverage
            float mip = log2(footprint * texSize.x);

            // Clamp to valid range
            float maxMip = float(textureQueryLevels(uBlockTextures) - 1);
            mip = clamp(mip, 0.0, maxMip);

            hitCol = textureLod(uBlockTextures, vec3(texUV, float(id)), mip).rgb;
            if(id == 2u) hitCol *= FOLIAGE_TINT;

            break;
        }
        // Step to next voxel
        if(sideDist.x < sideDist.y){
            if(sideDist.x < sideDist.z){ t=sideDist.x; sideDist.x+=deltaDist.x; mapPos.x+=step.x; mask=0; }
            else{ t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; mask=2; }
        }else{
            if(sideDist.y < sideDist.z){ t=sideDist.y; sideDist.y+=deltaDist.y; mapPos.y+=step.y; mask=1; }
            else{ t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; mask=2; }
        }
    }

    // 1st = bottom, 2nd = top
    vec3 sky = mix(vec3(0.5235, 0.6490, 0.7980), vec3(0.3490, 0.5098, 0.6941), rd.y*0.7+0.5);
    vec3 color = sky;

    float fog;

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

        fog = clamp(exp(-(hitDist - fogEnd) * 0.05), 0.0, 1.0);

        fog *= smoothstep(0.0, 1.0, (hitDist - fogEnd) / (fogStart - fogEnd));
        color = mix(sky,color,fog);

    }

    float cloudAccum = 0.0;
    float cloudTrans = 1.0;

    if(rd.y > 0.0){

        float t0 = (CLOUD_BOTTOM - uCamPos.y) / rd.y;
        float t1 = (CLOUD_TOP    - uCamPos.y) / rd.y;

        if(t1 > 0.0){

            float start = max(t0, 0.0);
            float end   = max(t1, 0.0);

            // ---- DEPTH CLIP AGAINST WORLD ----
            float maxCloudDist = hit ? hitDist : MAX_STEPS * 5;
            end = min(end, maxCloudDist);

            if(end > start){

                float stepSize = (end - start) / float(CLOUD_STEPS);

                for(int i=0;i<CLOUD_STEPS;i++){
                    float tCloud = start + stepSize*i;
                    vec3 pos = uCamPos + rd*tCloud;

                    float d = SampleCloudDensity(pos);

                    // ---- APPLY FOG TO CLOUD SAMPLE ----
                    float fogCloud = clamp(exp(-(tCloud - 1600) * 0.05), 0.0, 1.0);
                    fogCloud *= smoothstep(0.0, 1.0, (tCloud - 1600) / (300 - 1600));

                    d *= fogCloud;   // fade cloud density by fog

                    float light = max(dot(-SUN_DIR, vec3(0,1,0)),0.0);

                    cloudAccum += d * cloudTrans * light;
                    cloudTrans *= (1.0 - d*0.4);

                    if (cloudTrans < 0.05) break;
                }
            }
        }
    }

    vec3 cloudColor = vec3(1.0);
    color = mix(color, cloudColor, cloudAccum * 0.6);

    float maxRay = hit?hitDist:float(MAX_STEPS);
    float god = ComputeGodRaysFast(uCamPos,rd,maxRay);
    god *= pow(max(dot(rd,-SUN_DIR),0.0),3.0);
    god *= smoothstep(0.0,1.0,maxRay/80.0);

    color += vec3(1.0,0.95,0.85) * god * GODRAY_INTENSITY;
    //color += (hash21(gl_FragCoord.xy)-0.5)*0.03;
    color = mix(color,smoothstep(0.0,1.0,color),0.6);
    color = pow(color,vec3(0.75));

    float vibrance = 0.12;
    vec3 avg = vec3((color.r + color.g + color.b) / 3.0);
    vec3 delta = color - avg;
    color += delta * vibrance * (1.0 - abs(delta));

    FragColor = vec4(color,1.0);
}
