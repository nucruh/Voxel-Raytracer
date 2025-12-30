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

bool IntersectGrass(
    vec3 ro, vec3 rd, ivec3 voxel,
    float maxT, out float tHit, out vec3 nHit, out float alpha
){
    vec3 o = ro - vec3(voxel);
    float bestT = maxT;
    vec3 normal = vec3(0.0);
    alpha = 0.0;
    bool hit = false;

    const float W = 0.45; // Width from center
    
    // Wind Configuration
    float windSpeed = 1.2;
    float windStrength = 0.1; // Increased slightly for visibility
    float windPhase = hash21(vec2(voxel.x, voxel.z)) * 6.28; 

    // --- X-Facing Grass (runs along Z axis) ---
    if(abs(rd.x) > 1e-6){
        // 1. Initial hit with the FLAT plane at x = 0.5
        float t = (0.5 - o.x)/rd.x;
        
        if(t > 0.0 && t < bestT){
            // 2. Calculate initial hit position
            vec3 p = o + rd * t;
            
            // Check approximate vertical bounds to save calculations
            if(p.y > -0.2 && p.y < 1.2) { 
                
                // 3. Calculate Sway amount at this height
                //    Clamp p.y so calculations don't explode outside [0,1]
                float h = clamp(p.y, 0.0, 1.0);
                float sway = sin(iTime * windSpeed + windPhase) * windStrength * h;

                // 4. 3D REFINEMENT STEP:
                // The grass isn't at x=0.5, it's at x = 0.5 + sway.
                // We need to move the ray 't' forward/backward to reach that new X.
                // The distance to travel in X is 'sway'.
                // The distance to travel along the ray is 'sway / rd.x'.
                float t_curved = t + (sway / rd.x);
                vec3 p_curved = o + rd * t_curved;

                // 5. Check bounds on the REFINED position
                if(p_curved.y >= 0.0 && p_curved.y <= 1.0 && abs(p_curved.z - 0.5) <= W){
                    
                    // 6. Texture Mapping
                    // Since the geometry is physically bent, we just read the UVs
                    // directly from the hit point. No "inverse" math needed.
                    // The sway moves the mesh, but the texture stays pinned to the mesh.
                    vec2 texUV = vec2(p_curved.z, p_curved.y);

                    float a = texture(uBlockTextures, vec3(texUV, 7)).a;
                    if(a > 0.0){
                        bestT = t_curved;
                        // Calculate a simplified normal vector that considers the bend
                        // (Slope of sway is roughly windStrength)
                        float slope = cos(iTime * windSpeed + windPhase) * windStrength; 
                        normal = normalize(vec3(sign(-rd.x), -slope * sign(-rd.x), 0.0));
                        alpha = a;
                        hit = true;
                    }
                }
            }
        }
    }

    // --- Z-Facing Grass (runs along X axis) ---
    if(abs(rd.z) > 1e-6){
        float t = (0.5 - o.z) / rd.z;
        
        if(t > 0.0 && t < bestT){
            vec3 p = o + rd * t;
            
            if(p.y > -0.2 && p.y < 1.2){
                
                float h = clamp(p.y, 0.0, 1.0);
                float sway = sin(iTime * windSpeed + windPhase + 1.5) * windStrength * h;
                
                // Refine T
                float t_curved = t + (sway / rd.z);
                vec3 p_curved = o + rd * t_curved;

                if(p_curved.y >= 0.0 && p_curved.y <= 1.0 && abs(p_curved.x - 0.5) <= W){
                    
                    vec2 texUV = vec2(p_curved.x, p_curved.y);

                    float a = texture(uBlockTextures, vec3(texUV, 7)).a;
                    if(a > 0.0){
                        bestT = t_curved;
                        float slope = cos(iTime * windSpeed + windPhase + 1.5) * windStrength; 
                        normal = normalize(vec3(0.0, -slope * sign(-rd.z), sign(-rd.z)));
                        alpha = a;
                        hit = true;
                    }
                }
            }
        }
    }

    tHit = bestT;
    nHit = normal;
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

            if(IntersectGrass(uCamPos, rd, mapPos, 1000.0, tg, gN, ga)){
                vec3 hitPos = uCamPos + rd*tg;
                vec3 localPos = clamp(hitPos - vec3(mapPos), 0.0, 1.0);

                vec2 texUV = abs(gN.x) > 0.0 ? vec2(localPos.z, localPos.y) : vec2(localPos.x, localPos.y);
                vec2 texSize = vec2(textureSize(uBlockTextures,0).xy);
                vec2 dx = dFdx(texUV * texSize);
                vec2 dy = dFdy(texUV * texSize);
                float mip = max(0.0, 0.5*log2(max(dot(dx,dx), dot(dy,dy))));

                vec4 texSample = textureLod(uBlockTextures, vec3(texUV,float(id)), mip);
                float alpha = texSample.a;

                // blend
                hitCol = hitCol * (1.0 - alpha) + texSample.rgb * alpha;

                // only stop if fully opaque
                if(alpha >= 0.99){
                    hit = true;
                    hitDist = tg;
                    normal = gN;
                    break;
                }
                // otherwise continue marching in the voxel to hit the other quad
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
        float shadow = TraceShadow(hitPos)?0.25:0.9;
        color = hitCol * (0.6 + diff*shadow);

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
