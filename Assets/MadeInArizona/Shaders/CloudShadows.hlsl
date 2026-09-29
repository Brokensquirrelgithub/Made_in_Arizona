#ifndef MIA_CLOUD_SHADOWS_INCLUDED
#define MIA_CLOUD_SHADOWS_INCLUDED
// Drifting cloud shadows for the custom shaders. CloudShadows.cs sets a cloud map as the sun's URP light cookie, which
// URP Lit materials apply by themselves; this samples the same cookie (URP's own function and matrix), so terrain,
// roads, scenery and cars darken under exactly the same clouds as everything else. Include after Lighting.hlsl.

float4 _CloudShadowParams; // x 1 while the cloud cookie is on the sun, y shadow strength

// Sunlight left after the clouds at a world position (1 in clear sky).
float CloudShadow(float3 positionWS)
{
    return lerp(1, SampleMainLightCookie(positionWS).r, saturate(_CloudShadowParams.x));
}
#endif
