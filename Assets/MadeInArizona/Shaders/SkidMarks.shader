Shader "MadeInArizona/SkidMarks"
{
    // Tyre marks laid by TireMarks.cs. Multiplied onto whatever is underneath, so marks keep the ground's own lighting,
    // shadows and cloud shadows, and every pass over the same patch darkens it further: rubber builds up where cars
    // burn out or slide repeatedly. Vertex colour rgb is the tint at full strength, alpha the strength; uv.x runs
    // across the mark (0-1), uv.y along it in metres; uv2.x is the time the segment was laid, uv2.y 0 for rubber on
    // pavement or 1 for ruts in loose ground.
    Properties
    {
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-150" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Marks" Tags { "LightMode"="UniversalForward" }
            Blend DstColor Zero
            ZWrite Off ZTest LEqual Cull Off
            Offset -2, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 _MarkParams; // x lifetime (s), y fade-out time (s), z strength multiplier
            TEXTURE2D(_GlintWearMap); SAMPLER(sampler_GlintWearMap);
            struct A { float4 p:POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; float2 life:TEXCOORD1; };
            struct V { float4 p:SV_POSITION; half4 c:COLOR; float2 uv:TEXCOORD0; float2 life:TEXCOORD1; float3 world:TEXCOORD2; float fog:TEXCOORD3; };
            V vert(A v)
            {
                V o; o.world=TransformObjectToWorld(v.p.xyz); o.p=TransformWorldToHClip(o.world);
                o.c=v.c; o.uv=v.uv; o.life=v.life; o.fog=ComputeFogFactor(o.p.z); return o;
            }
            half4 frag(V i):SV_Target
            {
                float age=_Time.y-i.life.x;
                float fade=1-saturate((age-(_MarkParams.x-_MarkParams.y))/max(_MarkParams.y,1));
                float across=abs(i.uv.x*2-1);
                // Streaky, uneven rubber: the wear map's scratch and smudge channels stretched along the mark.
                float4 wear=SAMPLE_TEXTURE2D(_GlintWearMap,sampler_GlintWearMap,float2(i.uv.x*.35+i.world.x*.03,i.uv.y*.09+i.world.z*.03));
                float soil=i.life.y;
                float strength;
                float3 tint=i.c.rgb;
                if(soil<.5)
                {
                    // Rubber: soft edges, faint tread grooves running along the mark, streaks and gaps.
                    float edge=1-smoothstep(.62,1,across);
                    float grooves=lerp(.72,1,smoothstep(.2,.45,abs(frac(i.uv.x*4)-.5)));
                    strength=i.c.a*edge*grooves*lerp(.55,1.2,wear.g+wear.b*.5);
                }
                else
                {
                    // Loose ground: a darker, compressed rut with lighter churned-up berms at its edges.
                    float rut=1-smoothstep(.55,.9,across);
                    float berm=smoothstep(.6,.85,across)*(1-smoothstep(.9,1,across));
                    strength=i.c.a*rut*lerp(.7,1.15,wear.r);
                    tint=lerp(tint,float3(1.12,1.1,1.07),berm);
                    strength=max(strength,i.c.a*berm*.8);
                }
                strength=saturate(strength*fade*_MarkParams.z);
                float3 color=lerp(float3(1,1,1),tint,strength);
                // Distant haze washes marks out along with the ground under them.
                color=MixFogColor(color,float3(1,1,1),i.fog);
                return half4(color,1);
            }
            ENDHLSL
        }
    }
}
