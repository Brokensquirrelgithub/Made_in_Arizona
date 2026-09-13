Shader "MadeInArizona/SixWaySmoke"
{
 Properties {
  _Positive("Right / Top / Back / Opacity",2D)="white"{}
  _Negative("Left / Bottom / Front / Emission",2D)="black"{}
  _Emission("Fire intensity",Float)=0
  _Density("Optical density",Float)=1.5
  _Softness("Depth intersection fade",Float)=.65
 }
 SubShader {
 Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
 Pass {
 Tags { "LightMode"="UniversalForward" }
 Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off
 HLSLPROGRAM
 #pragma target 3.5
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
 #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
 #pragma multi_compile_fragment _ _SHADOWS_SOFT
 #pragma multi_compile_fog
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
 TEXTURE2D(_Positive); SAMPLER(sampler_Positive);
 TEXTURE2D(_Negative); SAMPLER(sampler_Negative);
 CBUFFER_START(UnityPerMaterial)
 float _Emission,_Density,_Softness;
 CBUFFER_END
 struct A { float4 positionOS:POSITION;float3 normalOS:NORMAL;float4 tangentOS:TANGENT;half4 color:COLOR;float4 uv:TEXCOORD0;float blend:TEXCOORD1; };
 struct V { float4 positionCS:SV_POSITION;float4 uv:TEXCOORD0;float3 world:TEXCOORD1;half4 color:COLOR;float3 tangent:TEXCOORD2;float3 bitangent:TEXCOORD3;float3 normal:TEXCOORD4;float blend:TEXCOORD5;float fog:TEXCOORD6; };
 V vert(A i) {
  V o;o.world=TransformObjectToWorld(i.positionOS.xyz);o.positionCS=TransformWorldToHClip(o.world);
  o.normal=TransformObjectToWorldNormal(i.normalOS);o.tangent=TransformObjectToWorldDir(i.tangentOS.xyz);
  o.bitangent=cross(o.normal,o.tangent)*i.tangentOS.w;
  o.uv=i.uv;o.blend=i.blend;o.color=i.color;o.fog=ComputeFogFactor(o.positionCS.z);return o;
 }
 float Response(float3 direction,float3 t,float3 b,float3 n,float3 p,float3 m) {
  float3 d=float3(dot(direction,t),dot(direction,b),-dot(direction,n));
  float3 weights=d*d;
  return dot(weights,pow(max(lerp(m,p,step(0,d)),.001),1.45));
 }
 half4 frag(V i):SV_Target {
  half4 p=lerp(SAMPLE_TEXTURE2D(_Positive,sampler_Positive,i.uv.xy),SAMPLE_TEXTURE2D(_Positive,sampler_Positive,i.uv.zw),i.blend);
  half4 m=lerp(SAMPLE_TEXTURE2D(_Negative,sampler_Negative,i.uv.xy),SAMPLE_TEXTURE2D(_Negative,sampler_Negative,i.uv.zw),i.blend);
  float alpha=(1-exp(-p.a*_Density))*i.color.a;
  clip(alpha-.002);
  float2 screen=GetNormalizedScreenSpaceUV(i.positionCS);
  float rawDepth=SampleSceneDepth(screen);
  float3 scene=ComputeWorldSpacePosition(screen,rawDepth,UNITY_MATRIX_I_VP);
  float sceneEye=-TransformWorldToView(scene).z,particleEye=-TransformWorldToView(i.world).z;
  alpha*=saturate((sceneEye-particleEye)/max(.05,_Softness));
  float3 n=normalize(i.normal),t=normalize(i.tangent),b=normalize(i.bitangent);
  Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));
  float3 light=sun.color*Response(sun.direction,t,b,n,p.rgb,m.rgb)*lerp(.8,1,sun.shadowAttenuation);
  uint count=GetAdditionalLightsCount();
  for(uint k=0;k<min(count,8u);k++) {
   Light lamp=GetAdditionalLight(k,i.world);
   light+=lamp.color*lamp.distanceAttenuation*Response(lamp.direction,t,b,n,p.rgb,m.rgb);
  }
  float ambient=(p.r+p.g+p.b+m.r+m.g+m.b)/6;
  light+=max(SampleSH(float3(0,1,0)),.04)*(.08+ambient*.4);
  // Emission is independent of the six directional scattering channels.
  float temperature=saturate(m.a*1.3);
  float3 ember=lerp(float3(1,.035,.002),float3(1,.38,.025),saturate(temperature*2));
  ember=lerp(ember,float3(1,.86,.45),saturate((temperature-.48)*2.4));
  float3 emission=ember*pow(temperature,1.6)*_Emission;
  float tint=saturate(dot(i.color.rgb,float3(.333,.333,.333)));
  float3 albedo=_Emission>0?float3(.045,.041,.035):lerp(float3(.035,.033,.03),float3(.17,.155,.13),tint);
  float3 color=albedo*light*1.8+emission;
  return half4(MixFog(color,i.fog),alpha);
 }
 ENDHLSL
 }
 }
}
