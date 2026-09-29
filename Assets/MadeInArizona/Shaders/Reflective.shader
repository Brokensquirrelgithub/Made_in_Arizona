Shader "MadeInArizona/Reflective"
{
    // Sunlit reflective surfaces other than car paint: chrome and polished metal, window glass, road signs and
    // reflectors, metal debris and glossy plastic. Physically based GGX sun specular with HDR peaks, a wear map that
    // roughens the gloss in patches, micro-normal detail that fragments highlights, a procedural desert sky for
    // environment reflection, and the stylised sun glint (see SunGlint.hlsl).
    Properties
    {
        _BaseColor("Albedo",Color)=(1,1,1,1)
        _Metallic("Metallic",Range(0,1))=1
        _Smoothness("Smoothness",Range(0,1))=.85
        _GlintType("Glint material type (1 chrome, 2 glass, 3 sign, 5 metal, 6 plastic)",Float)=1
        _Wear("Dust and wear response",Range(0,2))=1
        _Micro("Micro-normal strength",Range(0,2))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "CloudShadows.hlsl"
        #include "SunGlint.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;half _Metallic;half _Smoothness;half _GlintType;half _Wear;half _Micro;
        // Per-car dust (VehicleDust.cs sets these on a car's own copy of the material; zero means clean).
        float4 _DustRow0,_DustRow1,_DustRow2,_DustState,_DustAxles,_DustTint;
        CBUFFER_END
        #include "CarDust.hlsl"
        struct A {float4 p:POSITION;float3 n:NORMAL;};
        struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float3 local:TEXCOORD2;float3 objN:TEXCOORD3;float fog:TEXCOORD4;};
        V vert(A v)
        {
            V o;o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);
            o.n=TransformObjectToWorldNormal(v.n);o.objN=v.n;
            // Metric object-space position keeps wear and micro detail fixed to the part while it moves.
            float3 scale=float3(length(UNITY_MATRIX_M._m00_m10_m20),length(UNITY_MATRIX_M._m01_m11_m21),length(UNITY_MATRIX_M._m02_m12_m22));
            o.local=v.p.xyz*scale;o.fog=ComputeFogFactor(o.p.z);return o;
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
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n);
                float3 v=GetWorldSpaceNormalizeViewDir(i.world);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));sun.shadowAttenuation*=CloudShadow(i.world);
                float atten=sun.shadowAttenuation*sun.distanceAttenuation;
                float ao=1,directAO=1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor occlusion=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.p));ao=occlusion.indirectAmbientOcclusion;directAO=occlusion.directAmbientOcclusion;
                #endif
                float4 wear=SampleWear(i.local,i.objN);
                float clean;
                float roughness=WornRoughness(max(.02,1-_Smoothness),wear,_Wear,clean);
                float3 micro=TransformObjectToWorldNormal(MicroNormalOffset(i.local,i.objN,_Micro),false);
                float3 ns=normalize(n+micro);
                float metal=_Metallic;
                float3 albedo=_BaseColor.rgb;
                // Oxidation shows as a dull, warm film on metals.
                albedo=lerp(albedo,albedo*float3(.82,.7,.58)+float3(.05,.03,.015),saturate(wear.a*_Wear*_GlintSurface.y)*metal*.6);
                // Road dust on car parts (bumpers, tyres, trim): matte and tan, it takes the shine where it sits.
                float dust=CarDust(i.world,n);
                albedo=lerp(albedo,CarDustColor(i.world),dust);
                metal*=1-dust;roughness=lerp(roughness,.95,dust);clean*=1-dust;
                float3 f0=lerp(float3(.04,.04,.04),albedo,metal);
                float nl=saturate(dot(n,sun.direction));
                float3 lit=albedo*(1-metal)*(SampleSH(n)*ao+sun.color*nl*atten*directAO);
                // Environment: a procedural desert sky, blurred and dimmed with roughness.
                float nv=saturate(dot(ns,v));
                float3 fresnel=f0+(max(1-roughness,f0)-f0)*pow(1-nv,5);
                float3 r=reflect(-v,ns);
                float envScale=lerp(.55,1.15,saturate(dot(sun.color,float3(.2126,.7152,.0722))/2.5));
                float3 env=GlintSky(normalize(lerp(r,ns,roughness*roughness)))*envScale;
                lit+=env*fresnel*lerp(1,.2,roughness)*ao;
                // Sun: HDR GGX highlight and the stylised glint, both from the virtual eye so they move with the camera.
                float3 eye=GlintView(i.world,v),radiance=sun.color*atten;
                lit+=SunSpecular(ns,eye,sun.direction,roughness,f0,radiance);
                lit+=SunGlint(ns,eye,sun.direction,radiance,roughness,clean,_GlintType);
                #if defined(_ADDITIONAL_LIGHTS)
                    uint count=GetAdditionalLightsCount();
                    for(uint lightIndex=0;lightIndex<count;lightIndex++)
                    {
                        Light local=GetAdditionalLight(lightIndex,i.world,half4(1,1,1,1));
                        float ln=saturate(dot(n,local.direction));
                        float3 glow=local.color*local.distanceAttenuation*local.shadowAttenuation;
                        lit+=albedo*(1-metal*.7)*glow*ln+f0*glow*ln*pow(saturate(dot(ns,normalize(local.direction+v))),lerp(8,90,1-roughness));
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
    FallBack "Universal Render Pipeline/Lit"
}
