Shader "MadeInArizona/Grit"
{
    // Pebbles, grit and dirt clods thrown by tyres (mesh particles from TireMarks.cs). Opaque, lit by the sun, sky and
    // nearby lights; the particle colour is the albedo, so one particle system throws gravel, sand, red rock and mud.
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "CloudShadows.hlsl"
        // Particle vertex streams: Position, Normal, Color (set by TireMarks.cs). Positions arrive in world space.
        struct A { float4 p:POSITION; float3 n:NORMAL; half4 c:COLOR; };
        struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; half4 c:COLOR; float fog:TEXCOORD2; };
        V vert(A v)
        {
            V o; o.world=TransformObjectToWorld(v.p.xyz); o.p=TransformWorldToHClip(o.world);
            o.n=TransformObjectToWorldNormal(v.n); o.c=v.c; o.fog=ComputeFogFactor(o.p.z); return o;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));sun.shadowAttenuation*=CloudShadow(i.world);
                float3 albedo=i.c.rgb;
                float3 lit=albedo*(SampleSH(n)+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation*sun.distanceAttenuation);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint count=GetAdditionalLightsCount();
                    for(uint lightIndex=0;lightIndex<count;lightIndex++)
                    {
                        Light local=GetAdditionalLight(lightIndex,i.world,half4(1,1,1,1));
                        lit+=albedo*local.color*saturate(dot(n,local.direction))*local.distanceAttenuation*local.shadowAttenuation;
                    }
                #endif
                return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadow
            #pragma fragment depth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            V shadow(A v)
            {
                V o=vert(v);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection=normalize(_LightPosition-o.world);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.n),lightDirection));
                #if UNITY_REVERSED_Z
                    o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
                #else
                    o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
                #endif
                return o;
            }
            half4 depth(V i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment depth
            half4 depth(V i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment normals
            half4 normals(V i):SV_Target{return half4(normalize(i.n),0);}
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Particles/Simple Lit"
}
