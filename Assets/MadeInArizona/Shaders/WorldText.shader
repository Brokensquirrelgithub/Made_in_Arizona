Shader "MadeInArizona/WorldText"
{
    // Lettering on signs, floors and screens (TextMesh). Unity's GUI text shader ignores depth, so shop signs showed
    // through the walls they hang on (mirrored, from outside) and a screen's text showed through the back of the
    // monitor. This one is depth tested, with a small offset so floor lettering never fights the floor it lies on.
    Properties { _MainTex("Font atlas",2D)="white"{} }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }
        Pass
        {
            Name "WorldText"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            struct A { float4 p:POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; };
            struct V { float4 p:SV_POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; float fog:TEXCOORD1; };
            V vert(A v){V o;o.p=TransformObjectToHClip(v.p.xyz);o.c=v.c;o.uv=v.uv;o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 frag(V i):SV_Target{half4 c=i.c;c.a*=SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).a;c.rgb=MixFog(c.rgb,i.fog);return c;}
            ENDHLSL
        }
    }
}
