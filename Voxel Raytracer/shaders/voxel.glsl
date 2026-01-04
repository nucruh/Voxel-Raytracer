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

float fogEnd = 200.0;
float fogStart = 50.0;
float shadowDistance = 150.0;
float grassDistance = 100.0;

const int CHUNK_SIZE = 128;
const int CHUNK_PWR  = 7;
const int CHUNK_MASK = CHUNK_SIZE - 1;


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
        if(id == 253u){ svoSize = 16; svoMask = 15; }
        else if(id == 252u){ svoSize = 32; svoMask = 31; }
        else if(id == 251u){ svoSize = 64; svoMask = 63; }

        // Skip empty SVO region
        if(id == 253u || id == 252u || id == 251u){
            int sx = (step.x > 0) ? (svoSize - (mapPos.x & svoMask)) : ((mapPos.x & svoMask) + 1);
            int sy = (step.y > 0) ? (svoSize - (mapPos.y & svoMask)) : ((mapPos.y & svoMask) + 1);
            int sz = (step.z > 0) ? (svoSize - (mapPos.z & svoMask)) : ((mapPos.z & svoMask) + 1);

            int steps = min(sx, min(sy, sz));
            if(steps <= 0) steps = 1;

            for(int k=0;k<steps;k++){
                if(sideDist.x < sideDist.y){
                    if(sideDist.x < sideDist.z){ t=sideDist.x; sideDist.x+=deltaDist.x; mapPos.x+=step.x; }
                    else{ t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
                }else{
                    if(sideDist.y < sideDist.z){ t=sideDist.y; sideDist.y+=deltaDist.y; mapPos.y+=step.y; }
                    else{ t=sideDist.z; sideDist.z+=deltaDist.z; mapPos.z+=step.z; }
                }
            }
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
        else if(id == 255u){svoSize = 128; svoMask = 127; }

        
        // ---------- Skip empty SVO region ----------
        if(id == 253u || id == 252u || id == 251u || id == 255u){
            int sx = (step.x > 0) ? (svoSize - (localMapPos.x & svoMask)) : ((localMapPos.x & svoMask) + 1);
            int sy = (step.y > 0) ? (svoSize - (localMapPos.y & svoMask)) : ((localMapPos.y & svoMask) + 1);
            int sz = (step.z > 0) ? (svoSize - (localMapPos.z & svoMask)) : ((localMapPos.z & svoMask) + 1);

            int steps = min(sx, min(sy, sz));
            if(steps <= 0) steps = 1;

            // --- Single-step calculation ---

            // compute potential new side distances
            float nx = sideDist.x + deltaDist.x * (steps - 1);
            float ny = sideDist.y + deltaDist.y * (steps - 1);
            float nz = sideDist.z + deltaDist.z * (steps - 1);

            // find minimal distance (where the loop would stop)
            float tFinal = min(nx, min(ny, nz));

            // compute how many steps along each axis
            int stepX = int((tFinal - sideDist.x) / deltaDist.x);
            int stepY = int((tFinal - sideDist.y) / deltaDist.y);
            int stepZ = int((tFinal - sideDist.z) / deltaDist.z);

            // update map position
            mapPos.x += step.x * stepX;
            mapPos.y += step.y * stepY;
            mapPos.z += step.z * stepZ;

            // update side distances
            sideDist.x += deltaDist.x * stepX;
            sideDist.y += deltaDist.y * stepY;
            sideDist.z += deltaDist.z * stepZ;

            // update mask based on final minimal side
            if(sideDist.x <= sideDist.y && sideDist.x <= sideDist.z) mask = 0;
            else if(sideDist.y <= sideDist.x && sideDist.y <= sideDist.z) mask = 1;
            else mask = 2;

            // t is the minimal distance reached
            t = tFinal;

            i += steps;

            //hit = true;
            //hitDist = t;
            //hitCol = vec3(1.0, 0 ,0);
            //break;
        }
       

        // ---------- Grass intersection ----------

        bool isGrass = id == 128u || id == 129u || id == 130u;
        if(isGrass && t < grassDistance){
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
        // ---------- Normal voxel ----------
        else if(id < 251u && !isGrass){
            hit = true;
            hitDist = t - 0.0005;

            vec3 hitPos = uCamPos + rd * hitDist;
            vec3 local = fract(hitPos);
            vec2 texUV = (mask==0) ? local.zy : (mask==1) ? local.xz : local.xy;

            vec2 texSize = vec2(textureSize(uBlockTextures,0).xy);
            vec2 dx = dFdx(texUV * texSize);
            vec2 dy = dFdy(texUV * texSize);
            float mip = max(0.0, 0.5*log2(max(dot(dx,dx), dot(dy,dy))));

            hitCol = textureLod(uBlockTextures, vec3(texUV,float(id)), mip).rgb;
            if(id == 2u) hitCol *= FOLIAGE_TINT;

            normal = (mask==0)?vec3(-step.x,0,0):(mask==1)?vec3(0,-step.y,0):vec3(0,0,-step.z);
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
