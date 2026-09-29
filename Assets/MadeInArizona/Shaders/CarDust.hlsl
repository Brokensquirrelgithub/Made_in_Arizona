#ifndef MIA_CAR_DUST_INCLUDED
#define MIA_CAR_DUST_INCLUDED
// Desert dust building up on a car (VehicleDust.cs), shared by CarPaint and Reflective. Each car has its own copies of
// its materials, which carry the car's world-to-body transform, so the dust follows the body wherever it drives:
// - the dust line climbs the lower body from the sills toward the belt line as dust builds up;
// - wheel wells, tyres, rims and the sides around them cake first;
// - bumpers get dust below the belt line;
// - everything facing backwards collects it from the low-pressure wake behind the car;
// - a thin film settles on horizontal panels.
// Streaks and patches keep it from looking painted on. Include after the UnityPerMaterial CBUFFER, which must declare
//   float4 _DustRow0,_DustRow1,_DustRow2; // rows of the world-to-body matrix (body origin on the ground, +z forward)
//   float4 _DustState; // x amount 0-1, y wheel centre height, z wheel radius, w roof height
//   float4 _DustAxles; // x front axle z, y rear axle z, z front bumper z, w rear bumper z
//   float4 _DustTint;  // rgb dust colour (linear)
TEXTURE2D(_GrimeNoise); SAMPLER(sampler_GrimeNoise); // r large patches, g mid detail, b horizontal streaks, a speckle

float3 DustBodyPosition(float3 positionWS)
{
    float4 p = float4(positionWS, 1);
    return float3(dot(_DustRow0, p), dot(_DustRow1, p), dot(_DustRow2, p));
}

// 0-1 dust cover at a world position with world normal n.
float CarDust(float3 positionWS, float3 n)
{
    // Every term scales with the amount, so a clean car (or a material that is not a car's) gets exactly zero.
    float amount = saturate(_DustState.x);
    float3 p = DustBodyPosition(positionWS);
    float3 bn = normalize(float3(dot(_DustRow0.xyz, n), dot(_DustRow1.xyz, n), dot(_DustRow2.xyz, n)) + float3(0, 1e-4, 0));
    float h = saturate(p.y / max(_DustState.w, .8));
    float patches = SAMPLE_TEXTURE2D(_GrimeNoise, sampler_GrimeNoise, float2(p.z + p.x * .6, p.y) * .22).r;
    float detail = SAMPLE_TEXTURE2D(_GrimeNoise, sampler_GrimeNoise, float2(p.z - p.x * .4, p.y + p.x * .3) * .9).g;
    float streaks = SAMPLE_TEXTURE2D(_GrimeNoise, sampler_GrimeNoise, float2(p.z * .12, p.y * .5 + p.x * .05)).b;
    // Lower body: the dust line climbs from the sills toward the belt line as dust builds.
    float dustLine = lerp(.16, .58, amount) + (patches - .5) * .2 + (streaks - .5) * .16;
    float lower = 1 - smoothstep(dustLine - .14, dustLine + .05, h);
    // Wheel wells, tyres and rims: sprayed by the tyres. Sides and undersides near a wheel, and the wheel itself.
    float axle = abs(p.z - _DustAxles.x) < abs(p.z - _DustAxles.y) ? _DustAxles.x : _DustAxles.y;
    float arch = length(float2(p.z - axle, p.y - _DustState.y)) / max(_DustState.z, .2);
    float well = (1 - smoothstep(1.05, 1.9, arch + (detail - .5) * .35)) * max(saturate(abs(bn.x) * 1.4 + saturate(-bn.y)), step(arch, 1.02));
    // Bumpers: the ends of the car below the belt line.
    float span = max(_DustAxles.z - _DustAxles.w, 1);
    float ends = abs(p.z - (_DustAxles.z + _DustAxles.w) * .5) / (span * .5);
    float bumper = smoothstep(.8, .97, ends) * (1 - smoothstep(.45, .7, h));
    // Rear surfaces: the wake behind a moving car coats everything facing backwards.
    float rear = saturate(-bn.z * 1.4 - .15) * lerp(1, .6, h);
    // A thin film on horizontal panels, and a faint haze over everything.
    float top = saturate(bn.y);
    float dust = max(max(lower * saturate(amount * 1.3), well * saturate(amount * 2.2)),
                     max(bumper * saturate(amount * 1.5), max(rear * saturate(amount * 1.25), top * amount * .35)));
    dust = max(dust, amount * .1);
    return saturate(dust * lerp(.6, 1.2, detail) * lerp(.8, 1.1, patches));
}

// Dust colour with a little grain.
float3 CarDustColor(float3 positionWS)
{
    float3 p = DustBodyPosition(positionWS);
    float speck = SAMPLE_TEXTURE2D(_GrimeNoise, sampler_GrimeNoise, p.zy * 2.3 + p.x).a;
    return _DustTint.rgb * lerp(.85, 1.12, speck);
}
#endif
