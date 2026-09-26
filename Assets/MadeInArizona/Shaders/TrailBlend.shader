Shader "MadeInArizona/TrailBlend"
{
    // Unpaved trail overlay that dissolves into the terrain instead of ending on a hard line.
    // UV0: x = signed metres from the centreline, y = metres along the trail, z = core half-width, w = trail length.
    // UV1: x = ribbon half-width (outer mesh edge), y = edge feather in metres.
    Properties
    {
        _Tint("Soil tint",Color)=(.55,.42,.28,1)
        _Diffuse("Soil diffuse",2D)="white"{}
        _Normal("Soil normal",2D)="bump"{}
        _Height("Soil height",2D)="gray"{}
        _AO("Soil AO",2D)="white"{}
        _HeightBlend("Height blend depth (m)",Float)=1.1
        _Ruts("Wheel rut strength",Range(0,1))=.8
        _Crown("Grassy centre crown",Range(0,1))=0
        _Worn("Worn centre line",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "Queue"="Transparent-40" "IgnoreProjector"="True" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_Diffuse); SAMPLER(sampler_Diffuse); TEXTURE2D(_Normal); TEXTURE2D(_Height); TEXTURE2D(_AO);
            CBUFFER_START(UnityPerMaterial)
            float4 _Tint; float _HeightBlend,_Ruts,_Crown,_Worn;
            CBUFFER_END
            struct A { float4 p:POSITION; float3 n:NORMAL; float4 uv:TEXCOORD0; float2 shape:TEXCOORD1; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; float4 uv:TEXCOORD2; float fog:TEXCOORD3; float2 shape:TEXCOORD4; };
            // Same value noise as BiomeTerrain so cloud shading and grain scale match the ground underneath.
            float Hash21(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453123);}
            float Noise(float2 p){float2 id=floor(p),f=frac(p);f=f*f*(3.0-2.0*f);float a=Hash21(id),b=Hash21(id+float2(1,0)),c=Hash21(id+float2(0,1)),d=Hash21(id+1);return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);}
            float Fbm(float2 p){float n=0,a=.55;[unroll]for(int k=0;k<4;k++){n+=Noise(p)*a;p=p*2.07+float2(19.1,7.7);a*=.5;}return n*.969697;}
            V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.n=TransformObjectToWorldNormal(v.n);o.uv=v.uv;o.shape=v.shape;o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 frag(V i):SV_Target
            {
                float2 p=i.world.xz;
                float across=abs(i.uv.x),along=i.uv.y,halfWidth=i.uv.z,len=i.uv.w,outer=i.shape.x,feather=max(.3,i.shape.y);
                float2 soilUV=p*.16;
                float height=SAMPLE_TEXTURE2D(_Height,sampler_Diffuse,soilUV).r;

                // 1. Edge shape: a broad wander (~12 m) plus lobes (~3 m), so the border never runs parallel to the path.
                float wander=(Fbm(p*.07+float2(31,-7))-.5)*2;
                float lobes=(Fbm(p*.33+float2(-5,13))-.5)*2;
                float edge=halfWidth+wander*feather*.8+lobes*feather*.4-across;
                // Dead ends and junction stubs taper to a rounded, worn-out tip.
                float tip=min(along,len-along)-halfWidth*.35+wander*.6;
                edge=min(edge,tip);

                // 2. Height blend: soil fills low pockets of the ground first, so the border breaks into
                // metre-scale clumps and islands instead of a smooth airbrushed gradient.
                float clumps=(Fbm(p*1.15+float2(9,-21))-.5)*2;
                float coverage=edge+clumps*feather*.45+(height-.5)*_HeightBlend*.6;
                float mask=saturate(coverage/(feather*.55)+.5);
                mask=mask*mask*(3-2*mask);
                // Sparse clods of kicked-up soil just past the border.
                float clods=smoothstep(.74,.84,Noise(p*1.7+17)*.55+height*.45)*saturate(1+edge/(feather*1.4));
                mask=max(mask,clods*.5);
                // Dust spill: a broad, patchy, low-opacity band of loose soil between the packed trail and
                // untouched ground. Together with the clumped edge it gives a layered, graded transition.
                float dustNoise=Fbm(p*.8+float2(-13,4));
                float dust=saturate(1+(edge+(dustNoise-.5)*feather*.8)/(feather*1.1))*smoothstep(.25,.65,dustNoise+height*.3)*.4*(1-mask);
                // Safety fade at the ribbon border, so extreme noise can never expose the mesh edge.
                float border=saturate((outer-across)/.6);
                mask*=border;dust*=border;
                float alpha=mask+dust;
                if(alpha<.003)discard;

                // 3. Surface wear inside the trail.
                float3 soil=SAMPLE_TEXTURE2D(_Diffuse,sampler_Diffuse,soilUV).rgb;
                float soilLuma=max(.06,dot(soil,float3(.299,.587,.114)));
                // A second, larger-scale sample and broad mottling break up tiling across wide areas such as town aprons.
                float broadLuma=dot(SAMPLE_TEXTURE2D(_Diffuse,sampler_Diffuse,p*.047+float2(.37,.61)).rgb,float3(.299,.587,.114));
                float3 albedo=_Tint.rgb*clamp(lerp(soilLuma.xxx,soil,.35)/.45,.55,1.5)*lerp(.9,1.08,Fbm(p*.21));
                albedo*=clamp(broadLuma/.42,.8,1.2)*lerp(.86,1.1,Fbm(p*.06+float2(17,-3)));
                float gauge=min(.85,halfWidth*.62);
                float rutWave=Noise(float2(along*.35,i.uv.x>0?3:9))*.14;
                float rut=exp(-pow((across-gauge-rutWave)/.3,2))*_Ruts;
                // Dry tufts surviving between the wheel lines: small random clumps, never a continuous stripe.
                float crown=(1-smoothstep(.2,.6,across))*_Crown*smoothstep(.6,.78,Noise(p*3.3+float2(7,3)))*smoothstep(.35,.6,Fbm(p*.45));
                float worn=(1-smoothstep(.1,max(.2,halfWidth*.7),across))*_Worn;
                float berm=smoothstep(-.2,.25,edge)*(1-smoothstep(.25,.9,edge));
                albedo*=1-rut*.2;
                albedo=lerp(albedo,float3(.30,.30,.17)*lerp(.8,1.15,Noise(p*2.3)),crown*.6);
                albedo*=1+worn*.12;
                albedo*=1-berm*.14*(.6+Noise(p*4.1)*.8);
                float3 dustColor=lerp(_Tint.rgb,float3(.80,.68,.50),.55)*lerp(.9,1.1,Noise(p*2.7));
                albedo=lerp(dustColor,albedo,mask/alpha);

                float3 tn=UnpackNormal(SAMPLE_TEXTURE2D(_Normal,sampler_Diffuse,soilUV));
                float3 n=normalize(i.n+float3(tn.x,0,tn.y)*(.55-rut*.25));
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));float ao=1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.p)).indirectAmbientOcclusion;
                #endif
                ao*=lerp(.66,1,SAMPLE_TEXTURE2D(_AO,sampler_Diffuse,soilUV).r);
                float cloud=Fbm(p*.006+_Time.y*float2(.0021,.0013));
                float cloudShade=lerp(.91,1.0,smoothstep(.38,.68,cloud));
                float3 lit=albedo*(SampleSH(n)*ao+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation*cloudShade);
                #if defined(_ADDITIONAL_LIGHTS)
                uint count=GetAdditionalLightsCount();
                for(uint lightIndex=0;lightIndex<count;lightIndex++)
                {
                    Light local=GetAdditionalLight(lightIndex,i.world,half4(1,1,1,1));
                    lit+=albedo*local.color*saturate(dot(n,local.direction))*local.distanceAttenuation*local.shadowAttenuation;
                }
                #endif
                return half4(MixFog(lit,i.fog),alpha);
            }
            ENDHLSL
        }
    }
}
