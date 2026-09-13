Shader "MadeInArizona/Scattering"
{
 Properties { _BaseColor("Fur / wax color",Color)=(1,1,1,1) _Transmission("Subsurface wrap",Range(0,1))=.32 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
 Pass {
 Tags { "LightMode"="UniversalForward" }
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 struct A { float4 positionOS:POSITION;float3 normalOS:NORMAL; };
 struct V { float4 positionCS:SV_POSITION;float3 world:TEXCOORD0;float3 normal:TEXCOORD1;float fog:TEXCOORD2; };
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;half _Transmission;
 CBUFFER_END
 float _ArizonaDetail;
 V vert(A i){ V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);o.normal=TransformObjectToWorldNormal(i.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o; }
 half4 frag(V i):SV_Target {
 float3 n=normalize(i.normal),v=GetWorldSpaceNormalizeViewDir(i.world);
 Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
 float wrap=saturate((dot(n,sun.direction)+.35)/1.35);
 float forward=pow(saturate(dot(v,-sun.direction)),4);
 float transmission=(forward*.8+.2)*saturate(-dot(n,sun.direction))*_Transmission*_ArizonaDetail;
 float sheen=pow(1-saturate(dot(n,v)),4)*.12*_ArizonaDetail;
 half3 lit=_BaseColor.rgb*(SampleSH(n)+sun.color*(wrap*sun.shadowAttenuation+transmission))+sun.color*sheen;
 return half4(MixFog(lit,i.fog),1);
 }
 ENDHLSL
 }
 UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 UsePass "Universal Render Pipeline/Lit/DepthOnly"
 }
}
