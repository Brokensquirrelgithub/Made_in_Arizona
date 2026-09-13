Shader "MadeInArizona/LightShaft"
{
 Properties { _BaseColor("Scattered light",Color)=(1,.65,.3,.1) }
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-10" }
 Pass {
 Blend SrcAlpha One ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma vertex vert
 #pragma fragment frag
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 struct A { float4 positionOS:POSITION;float2 uv:TEXCOORD0; };
 struct V { float4 positionCS:SV_POSITION;float2 uv:TEXCOORD0; };
 CBUFFER_START(UnityPerMaterial)
 half4 _BaseColor;
 CBUFFER_END
 V vert(A i){V o;o.positionCS=TransformObjectToHClip(i.positionOS.xyz);o.uv=i.uv;return o;}
 half4 frag(V i):SV_Target {
 float width=lerp(.48,.08,i.uv.y);
 float beam=pow(saturate(1-abs(i.uv.x-.5)/width),2);
 float fade=sin(saturate(i.uv.y)*PI);
 float dust=.85+.15*sin(i.uv.y*55+_Time.y*.3)*sin(i.uv.x*37-_Time.y*.2);
 return half4(_BaseColor.rgb,beam*fade*dust*_BaseColor.a);
 }
 ENDHLSL
 }
 }
}
