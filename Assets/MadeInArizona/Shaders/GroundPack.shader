Shader "Hidden/MadeInArizona/GroundPack"
{
    // Packs the Outdoor Ground Textures into terrain texture-array slices at runtime.
    // Pass 0: albedo RGB + height A. Pass 1: tangent normal XY (0..1) + ambient occlusion B.
    Properties { _MainTex("Diffuse or normal",2D)="white"{} _Second("Height or AO",2D)="gray"{} }
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex); TEXTURE2D(_Second); SAMPLER(sampler_Second);
        struct A { float4 p:POSITION; float2 uv:TEXCOORD0; };
        struct V { float4 p:SV_POSITION; float2 uv:TEXCOORD0; };
        V vert(A v){V o;o.p=TransformObjectToHClip(v.p.xyz);o.uv=v.uv;return o;}
        ENDHLSL
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i):SV_Target{return float4(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv).rgb,SAMPLE_TEXTURE2D(_Second,sampler_Second,i.uv).r);}
            ENDHLSL
        }
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            float4 frag(V i):SV_Target{float3 n=UnpackNormal(SAMPLE_TEXTURE2D(_MainTex,sampler_MainTex,i.uv));return float4(n.xy*.5+.5,SAMPLE_TEXTURE2D(_Second,sampler_Second,i.uv).r,1);}
            ENDHLSL
        }
    }
}
