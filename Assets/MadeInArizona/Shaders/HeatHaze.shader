Shader "MadeInArizona/HeatHaze"
{
 Properties { _Strength("Refraction",Float)=0.003 _Opacity("Lifetime",Range(0,1))=1 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+20" "RenderType"="Transparent" }
 Pass {
 ZWrite Off Cull Off Blend SrcAlpha OneMinusSrcAlpha
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
 struct A { float4 positionOS:POSITION; float2 uv:TEXCOORD0; };
 struct V { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; };
 CBUFFER_START(UnityPerMaterial)
 float _Strength; float _Opacity;
 CBUFFER_END
 V vert(A i) { V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o; }
 half4 frag(V i):SV_Target {
 float2 uv=GetNormalizedScreenSpaceUV(i.positionCS);
 float mask=saturate(1-length((i.uv-.5)*2)); mask=mask*mask*_Opacity;
 float2 ripple=float2(sin(i.uv.y*36-_Time.y*7+sin(i.uv.x*22)),cos(i.uv.x*25+_Time.y*4));
 return half4(SampleSceneColor(saturate(uv+ripple*_Strength*mask)),mask*.65);
 }
 ENDHLSL
 }
 }
}
