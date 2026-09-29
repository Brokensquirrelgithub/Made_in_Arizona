Shader "MadeInArizona/LivingScenery"
{
    Properties { _NeedleAtlas("Needle silhouette",2D)="white"{} }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Cull Off
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "CloudShadows.hlsl"
        TEXTURE2D(_NeedleAtlas);SAMPLER(sampler_NeedleAtlas);
        struct A {float4 p:POSITION;float3 n:NORMAL;float4 c:COLOR;float2 uv:TEXCOORD0;};
        struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float4 c:COLOR;float fog:TEXCOORD2;float2 uv:TEXCOORD3;};
        float4 _SceneryVehicle;
        float3 Wind(float3 p,float bend)
        {
            float gust=sin(p.x*.18+p.z*.13+_Time.y*1.7)*.13+sin(p.x*.73-p.z*.46+_Time.y*3.1)*.045;
            p.xz+=float2(gust,gust*.55)*bend;
            float2 away=p.xz-_SceneryVehicle.xz;
            float press=saturate(1-length(away)/2.3)*saturate(1-abs(p.y-_SceneryVehicle.y)/2.2)*_SceneryVehicle.w*bend;
            p.xz+=normalize(away+float2(.001,.001))*press*.65;
            p.y-=press*.3;return p;
        }
        V vert(A v){V o;o.world=Wind(TransformObjectToWorld(v.p.xyz),v.c.a);o.p=TransformWorldToHClip(o.world);o.n=TransformObjectToWorldNormal(v.n);o.c=v.c;o.uv=v.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
        half4 frag(V i,FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
        {
            float3 needle=1;
            if(i.uv.x>=0){float4 tex=SAMPLE_TEXTURE2D(_NeedleAtlas,sampler_NeedleAtlas,i.uv);clip(tex.a-.35);needle=tex.rgb;}
            float3 n=normalize(i.n)*IS_FRONT_VFACE(face,1,-1);
            float grain=frac(sin(dot(floor(i.world*24),float3(127.1,311.7,74.7)))*43758.5453);
            float strata=sin(i.world.y*15+sin(i.world.x*4)+sin(i.world.z*5));
            float3 albedo=i.c.rgb*needle*(.89+grain*.14+strata*.045*(1-i.c.a));
            Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));sun.shadowAttenuation*=CloudShadow(i.world);
            float ao=1,directAO=1;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor occlusion=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.p));ao=occlusion.indirectAmbientOcclusion;directAO=occlusion.directAmbientOcclusion;
            #endif
            float wrap=saturate((dot(n,sun.direction)+.22)/1.22);
            float transmission=pow(saturate(dot(normalize(_WorldSpaceCameraPos-i.world),-sun.direction)),3)*i.c.a*.3;
            float3 lit=albedo*(SampleSH(n)*ao+sun.color*(wrap+transmission)*sun.shadowAttenuation*directAO);
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
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
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
            half4 depth(V i):SV_Target{if(i.uv.x>=0)clip(SAMPLE_TEXTURE2D(_NeedleAtlas,sampler_NeedleAtlas,i.uv).a-.35);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment depth
            half4 depth(V i):SV_Target{if(i.uv.x>=0)clip(SAMPLE_TEXTURE2D(_NeedleAtlas,sampler_NeedleAtlas,i.uv).a-.35);return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment normals
            half4 normals(V i):SV_Target{if(i.uv.x>=0)clip(SAMPLE_TEXTURE2D(_NeedleAtlas,sampler_NeedleAtlas,i.uv).a-.35);return half4(normalize(i.n),0);}
            ENDHLSL
        }
    }
}
