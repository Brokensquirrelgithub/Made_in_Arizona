Shader "MadeInArizona/FlowRiver"
{
 Properties { _BumpMap("Ripples",2D)="bump"{} }
 SubShader
 {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque"}
  Pass
  {
   Tags {"LightMode"="UniversalForward"}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #pragma multi_compile_fog
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "CloudShadows.hlsl"
   #include "SunGlint.hlsl"
   TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
   struct A {float4 p:POSITION;float2 uv:TEXCOORD0;};
   struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float2 uv:TEXCOORD1;float fog:TEXCOORD2;};
   V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.uv=v.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
   half4 frag(V i):SV_Target
   {
    float2 uv=i.world.xz*.22+float2(.03,-.17)*_Time.y;
    float3 r=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,uv));
    float3 r2=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,i.world.xz*.53+float2(-.04,-.11)*_Time.y));
    float3 n=normalize(float3(r.x*.85+r2.x*.35,1,r.y*.85+r2.y*.35));Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));sun.shadowAttenuation*=CloudShadow(i.world);
    float3 view=GetWorldSpaceNormalizeViewDir(i.world);float fresnel=pow(1-saturate(dot(n,view)),4);
    float edge=smoothstep(.86,1,abs(i.uv.x-.5)*2);
    float pulse=.5+.5*sin(i.world.z*2+i.world.x*.8-_Time.y*2.6);
    float3 water=lerp(float3(.025,.19,.22),float3(.22,.43,.46),fresnel);
    float shoal=smoothstep(.4,.98,abs(i.uv.x-.5)*2);
    water=lerp(water,float3(.18,.31,.24),shoal*.62);
    float ribbons=sin(i.world.x*2.1+sin(i.world.z*.77-_Time.y*.6)*1.8+_Time.y*.8)*sin(i.world.z*4-_Time.y*2.1);
    water+=float3(.11,.19,.16)*pow(saturate(ribbons),12)*(.3+shoal*.5);
    water=lerp(water,float3(.53,.62,.50),edge*pow(pulse,3)*.5);
    water*=lerp(.55,1,sun.shadowAttenuation);
    // Sun on moving ripples: sharp HDR glitter where facets mirror the sun toward the (virtual) eye, and the odd
    // stylised glint as a ripple lines up exactly. Shoals and banks are rougher and duller.
    float3 eye=GlintView(i.world,view),radiance=sun.color*sun.shadowAttenuation;
    float roughness=lerp(.07,.3,shoal);
    water+=SunSpecular(n,eye,sun.direction,roughness,float3(.02,.02,.02),radiance);
    water+=SunGlint(n,eye,sun.direction,radiance,roughness,1-shoal*.8,GLINT_WATER);
    return half4(MixFog(water,i.fog),1);
   }
   ENDHLSL
  }
 }
}
