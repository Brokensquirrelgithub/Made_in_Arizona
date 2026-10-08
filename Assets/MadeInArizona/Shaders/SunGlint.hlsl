#ifndef MIA_SUN_GLINT_INCLUDED
#define MIA_SUN_GLINT_INCLUDED
// Sun specular and stylised glints shared by CarPaint, Reflective and FlowRiver.
//
// * Sun specular is URP-style GGX from the main directional light with no low clamp, so a smooth surface produces a
//   small, very bright HDR highlight (only a high ceiling guards against overflow) and a rough one a broad, weak lobe.
// * Roughness is broken up by a wear map (dust, scratches, fingerprints, oxidation) and reflection direction by a
//   micro-normal map, both sampled triplanar in metric object space so the pattern sticks to the surface.
// * The stylised glint adds a brief HDR spike where the mirrored sun lines up with the eye, within a small angular
//   tolerance. It is added to the pixel only; nothing else is lit by it.
// * The gameplay camera is orthographic, so its view direction is the same everywhere and never changes. Sun
//   highlights and glints use a virtual perspective eye behind the camera instead (_GlintEye), so reflections slide
//   and flash as the camera travels, the way they do through a real lens.
// All globals are written by SunGlint.cs from the Dev Tuning > Reflections sliders.

float4 _GlintParams;      // x glint intensity, y cos(angular tolerance), z bloom contribution (0-1), w fade sharpness
float4 _GlintEye;         // xyz virtual eye position, w 1 when set
float4 _GlintTypesA;      // eligibility by material type: paint, chrome, glass, sign
float4 _GlintTypesB;      // water, metal, plastic, unused
float4 _GlintSurface;     // x specular HDR ceiling, y wear strength, z micro-normal strength, w linear bloom threshold
TEXTURE2D(_GlintWearMap); SAMPLER(sampler_GlintWearMap);        // r dust, g scratches, b fingerprints/smudges, a oxidation/chips
TEXTURE2D(_GlintMicroNormal); SAMPLER(sampler_GlintMicroNormal); // rgb object-space-agnostic normal, encoded n*.5+.5

// Material types for eligibility and wear response.
#define GLINT_PAINT 0
#define GLINT_CHROME 1
#define GLINT_GLASS 2
#define GLINT_SIGN 3
#define GLINT_WATER 4
#define GLINT_METAL 5
#define GLINT_PLASTIC 6

float GlintEligibility(float type)
{
    int t = (int)round(type);
    return t == 0 ? _GlintTypesA.x : t == 1 ? _GlintTypesA.y : t == 2 ? _GlintTypesA.z : t == 3 ? _GlintTypesA.w :
           t == 4 ? _GlintTypesB.x : t == 5 ? _GlintTypesB.y : _GlintTypesB.z;
}

// View direction from the virtual eye (falls back to the camera's own view when the globals are not set).
float3 GlintView(float3 positionWS, float3 cameraView)
{
    return _GlintEye.w > .5 ? normalize(_GlintEye.xyz - positionWS) : cameraView;
}

float3 TriplanarWeights(float3 normalOS)
{
    float3 w = pow(abs(normalOS), 4);
    return w / max(w.x + w.y + w.z, 1e-4);
}

// Dust, scratches, fingerprints and oxidation at a metric object-space position.
float4 SampleWear(float3 positionOS, float3 normalOS)
{
    float3 w = TriplanarWeights(normalOS);
    float3 p = positionOS * .36;
    return SAMPLE_TEXTURE2D(_GlintWearMap, sampler_GlintWearMap, p.zy) * w.x +
           SAMPLE_TEXTURE2D(_GlintWearMap, sampler_GlintWearMap, p.xz + .31) * w.y +
           SAMPLE_TEXTURE2D(_GlintWearMap, sampler_GlintWearMap, p.xy + .67) * w.z;
}

// Object-space offset to add to a normal: subtle waviness and orange peel that fragment highlights.
float3 MicroNormalOffset(float3 positionOS, float3 normalOS, float strength)
{
    float3 w = TriplanarWeights(normalOS);
    float3 p = positionOS * .55;
    float2 x = SAMPLE_TEXTURE2D(_GlintMicroNormal, sampler_GlintMicroNormal, p.zy).xy * 2 - 1;
    float2 y = SAMPLE_TEXTURE2D(_GlintMicroNormal, sampler_GlintMicroNormal, p.xz + .19).xy * 2 - 1;
    float2 z = SAMPLE_TEXTURE2D(_GlintMicroNormal, sampler_GlintMicroNormal, p.xy + .53).xy * 2 - 1;
    return (float3(0, x.y, x.x) * w.x + float3(y.x, 0, y.y) * w.y + float3(z.x, z.y, 0) * w.z) * strength * _GlintSurface.z;
}

// Wear raises roughness in patches instead of leaving a uniform gloss. `response` scales it per material
// (weathered props more than fresh paint). Returns the roughness and a 0-1 mask of how much clean surface is left.
float WornRoughness(float roughness, float4 wear, float response, out float clean)
{
    float k = _GlintSurface.y * response;
    float dust = saturate(wear.r * k), scratch = saturate(wear.g * k), smudge = saturate(wear.b * k), oxide = saturate(wear.a * k);
    roughness = lerp(roughness, max(roughness, .62), dust);
    roughness = lerp(roughness, max(roughness, .34), scratch * .8);
    roughness = lerp(roughness, max(roughness, .28), smudge * .75);
    roughness = lerp(roughness, max(roughness, .8), oxide);
    clean = saturate(1 - dust * 1.4) * saturate(1 - oxide * 1.2) * (1 - smudge * .55) * (1 - scratch * .35);
    return saturate(roughness);
}

// URP-style GGX sun specular (URP's normalisation, no 1/pi, matching the rest of the scene's lighting). Smooth
// surfaces reach very high HDR values in a tiny spot; the only clamp is the configurable ceiling.
float3 SunSpecular(float3 n, float3 v, float3 l, float perceptualRoughness, float3 f0, float3 radiance)
{
    float3 h = normalize(l + v);
    float nh = saturate(dot(n, h)), lh = saturate(dot(l, h)), nl = saturate(dot(n, l));
    float roughness = max(perceptualRoughness * perceptualRoughness, .0012);
    float a2 = roughness * roughness;
    float d = nh * nh * (a2 - 1) + 1.00001;
    float term = a2 / (d * d * max(.1, lh * lh) * (roughness * 4 + 2));
    float3 fresnel = f0 + (1 - f0) * pow(1 - lh, 5);
    float3 specular = term * fresnel * radiance * nl;
    float peak = max(specular.r, max(specular.g, specular.b));
    return peak > _GlintSurface.x ? specular * (_GlintSurface.x / peak) : specular;
}

// Stylised glint: a brief HDR spike when the mirrored sun lines up with the eye. Rougher, dustier or ineligible
// surfaces weaken or lose it; the angular window and fade sharpness decide how briefly it flashes.
float3 SunGlint(float3 n, float3 v, float3 l, float3 radiance, float perceptualRoughness, float clean, float type)
{
    float eligible = GlintEligibility(type) * _GlintParams.x;
    if (eligible <= 0) return 0;
    float align = dot(reflect(-l, n), v);
    // Rough surfaces spread the reflection, so the tolerance widens a little while the strength collapses.
    float tolerance = 1 - (1 - _GlintParams.y) * (1 + perceptualRoughness * 3);
    float window = saturate((align - tolerance) / max(1 - tolerance, 1e-5));
    float flash = pow(window, _GlintParams.w);
    float gloss = saturate(1 - (perceptualRoughness - .04) / .3);
    float strength = flash * gloss * gloss * clean * eligible;
    if (strength <= 0) return 0;
    // Core brightness well above the bloom threshold; the bloom setting decides how much of the overshoot is kept.
    float threshold = _GlintSurface.w;
    float core = strength * 40;
    float kept = min(core, threshold) + max(core - threshold, 0) * _GlintParams.z;
    return radiance * kept;
}

float4 _SkySunset; // x 0 at midday, 1 at sunset (TimeOfDay.cs)

// Sunset sky for reflections: hot orange along the horizon, brightest under the low sun and fading to a dusty pink
// belt opposite it, through violet to deep blue overhead; the ground below the horizon is dark dusk.
float3 SunsetSky(float3 r)
{
    float3 sunDir = _MainLightPosition.xyz;
    float up = saturate(r.y);
    float toward = dot(normalize(r.xz + 1e-4), normalize(sunDir.xz + 1e-4)) * .5 + .5;
    float3 horizon = lerp(float3(.55, .32, .38), float3(1.25, .55, .2), toward);
    float3 sky = lerp(horizon, float3(.34, .26, .44), smoothstep(0, .35, up));
    sky = lerp(sky, float3(.08, .11, .28), smoothstep(.3, .9, up));
    sky += float3(1.4, .62, .22) * pow(saturate(dot(r, sunDir)), 6) * (1 - smoothstep(0, .6, up));
    float3 ground = lerp(float3(.3, .18, .15), float3(.08, .06, .07), saturate(-r.y * 3));
    return (r.y >= 0 ? sky : ground) + float3(1.1, .5, .22) * .35 * exp(-abs(r.y - .03) * 22) * (.4 + .6 * toward);
}

// A soft sky for rough reflections when a shader has no environment of its own; the desert noon sky, or the sunset.
float3 GlintSky(float3 r)
{
    float up = saturate(r.y);
    float3 sky = lerp(float3(.95, .88, .76), float3(.30, .52, .86), pow(up, .5));
    float3 ground = lerp(float3(.66, .55, .42), float3(.27, .21, .16), saturate(-r.y * 3));
    float3 noon = (r.y >= 0 ? sky : ground) + float3(1, .94, .84) * .4 * exp(-abs(r.y - .05) * 18);
    return lerp(noon, SunsetSky(r), saturate(_SkySunset.x));
}
#endif
