Shader "MadeInArizona/BiomeTerrain"
{
    Properties
    {
        _BumpMap("Fallback terrain normal",2D)="bump"{}
        _GroundDiffuse0("Packed sand",2D)="white"{} _GroundNormal0("Packed sand normal",2D)="bump"{} _GroundAO0("Packed sand AO",2D)="white"{} _GroundHeight0("Packed sand height",2D)="gray"{}
        _GroundDiffuse1("Packed earth",2D)="white"{} _GroundNormal1("Packed earth normal",2D)="bump"{} _GroundAO1("Packed earth AO",2D)="white"{} _GroundHeight1("Packed earth height",2D)="gray"{}
        _GroundDiffuse2("Packed gravel",2D)="white"{} _GroundNormal2("Packed gravel normal",2D)="bump"{} _GroundAO2("Packed gravel AO",2D)="white"{} _GroundHeight2("Packed gravel height",2D)="gray"{}
        _GroundDiffuse3("Packed rock",2D)="white"{} _GroundNormal3("Packed rock normal",2D)="bump"{} _GroundAO3("Packed rock AO",2D)="white"{} _GroundHeight3("Packed rock height",2D)="gray"{}
        _UseGroundTextures("Use Outdoor Ground Textures",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
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

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            // Every pack map uses the same repeat/bilinear sampling state. Sharing one sampler keeps
            // this four-layer blend under the 16-sampler limit on Metal and Direct3D 11.
            TEXTURE2D(_GroundDiffuse0); SAMPLER(sampler_GroundDiffuse0); TEXTURE2D(_GroundNormal0); TEXTURE2D(_GroundAO0);
            TEXTURE2D(_GroundDiffuse1); TEXTURE2D(_GroundNormal1); TEXTURE2D(_GroundAO1);
            TEXTURE2D(_GroundDiffuse2); TEXTURE2D(_GroundNormal2); TEXTURE2D(_GroundAO2);
            TEXTURE2D(_GroundDiffuse3); TEXTURE2D(_GroundNormal3); TEXTURE2D(_GroundAO3);
            TEXTURE2D(_GroundHeight0); TEXTURE2D(_GroundHeight1); TEXTURE2D(_GroundHeight2); TEXTURE2D(_GroundHeight3);
            float _UseGroundTextures;
            struct A { float4 p:POSITION; float3 n:NORMAL; float2 uv:TEXCOORD0; float4 c:COLOR; };
            struct V { float4 p:SV_POSITION; float3 world:TEXCOORD0; float3 n:TEXCOORD1; float2 uv:TEXCOORD2; float4 c:COLOR; float fog:TEXCOORD3; };
            float2 Hash22(float2 p){p=float2(dot(p,float2(127.1,311.7)),dot(p,float2(269.5,183.3)));return frac(sin(p)*43758.5453123);}
            float Hash21(float2 p){return frac(sin(dot(p,float2(127.1,311.7)))*43758.5453123);}
            // True 0..1 value noise. Every material threshold below is calibrated against this range.
            float Noise(float2 p){float2 id=floor(p),f=frac(p);f=f*f*(3.0-2.0*f);float a=Hash21(id),b=Hash21(id+float2(1,0)),c=Hash21(id+float2(0,1)),d=Hash21(id+1);return lerp(lerp(a,b,f.x),lerp(c,d,f.x),f.y);}
            float Fbm(float2 p){float n=0,a=.55;[unroll]for(int k=0;k<4;k++){n+=Noise(p)*a;p=p*2.07+float2(19.1,7.7);a*=.5;}return n*.969697;}
            float Voronoi(float2 p,out float edge,out float cellHash)
            {
                float2 id=floor(p),f=frac(p);float d1=8,d2=8;cellHash=0;
                [unroll]for(int y=-1;y<=1;y++) [unroll]for(int x=-1;x<=1;x++){float2 g=float2(x,y),h=Hash22(id+g),q=g+h-f;float d=dot(q,q);if(d<d1){d2=d1;d1=d;cellHash=h.x;}else if(d<d2)d2=d;}
                edge=sqrt(d2)-sqrt(d1);return sqrt(d1);
            }
            // Fine, broken fissures: the caller limits them to pale, dry, level flats.
            float CrackMask(float2 p){float edge,h;Voronoi(p*2.5+Fbm(p*.13)*1.7,edge,h);return(1-smoothstep(.035,.07,edge))*smoothstep(.70,.88,h);}
            V vert(A v){V o;VertexPositionInputs p=GetVertexPositionInputs(v.p.xyz);o.p=p.positionCS;o.world=p.positionWS;o.n=TransformObjectToWorldNormal(v.n);o.uv=v.uv;o.c=v.c;o.fog=ComputeFogFactor(o.p.z);return o;}
            half4 frag(V i):SV_Target
            {
                float2 p=i.world.xz;float slope=1-saturate(i.n.y),macro=Fbm(p*.045+float2(13,-5)),veins=Fbm(p*.18+float2(-23,8)),grit=Fbm(p*.72),colorVariation=Fbm(p*.95);
                float e,h,stoneDistance=Voronoi(p*.58,e,h);float stoneRadius=lerp(.050,.135,frac(h*13.7));
                float stoneShape=stoneDistance+(grit-.5)*.042;
                float scatteredStone=(1-smoothstep(stoneRadius*.52,stoneRadius,stoneShape))*smoothstep(.56,.84,h);
                // Vertex colour establishes each biome; erosion, gravel and bedrock are then layered in world space.
                float dry=saturate(i.c.r*1.25-i.c.g*.30+i.c.b*.12),green=saturate(i.c.g*1.35-i.c.r*.45);
                float pale=saturate((min(i.c.r,i.c.g)-.27)*2.7),dryFlat=dry*pale*saturate(1-slope*3.2);
                float cracks=CrackMask(p)*dryFlat*.42;
                float rock=saturate(slope*1.8+(macro-.63)*1.35+(veins-.69)*.8+scatteredStone*.65)*lerp(.68,1,dry);
                float gravel=saturate((grit-.43)*2+scatteredStone*.85+slope*.32)*(1-rock*.55),sand=saturate(dry*.75+(macro-.48)*.25-slope*.55-rock*.7);
                float3 soilColor=i.c.rgb*lerp(.68,1.14,macro);soilColor=lerp(soilColor,float3(.31,.36,.20),green*.23);
                float3 sandColor=lerp(float3(.72,.50,.27),float3(.84,.67,.39),veins);
                float3 gravelColor=lerp(float3(.30,.245,.18),float3(.48,.37,.25),colorVariation);
                float3 rockColor=lerp(float3(.20,.19,.16),float3(.42,.35,.27),Fbm(p*.31))*lerp(.85,1.12,colorVariation);
                float3 albedo=lerp(soilColor,sandColor,sand*.46);albedo=lerp(albedo,gravelColor,gravel*.68);albedo=lerp(albedo,rockColor,rock);albedo*=1-cracks*.10;albedo=lerp(albedo,gravelColor*.90,scatteredStone*.27);
                // Licensed Outdoor Ground Textures provide recognizable grain and material breakup;
                // the procedural palette keeps the Arizona biome colours and hides repetition.
                float2 packUV=p*.115;
                float3 packSand=SAMPLE_TEXTURE2D(_GroundDiffuse0,sampler_GroundDiffuse0,packUV).rgb;
                float3 packEarth=SAMPLE_TEXTURE2D(_GroundDiffuse1,sampler_GroundDiffuse0,packUV.yx*float2(-1,1)+float2(3.2,7.1)).rgb;
                float3 packGravel=SAMPLE_TEXTURE2D(_GroundDiffuse2,sampler_GroundDiffuse0,packUV*1.28+float2(11.3,2.7)).rgb;
                float3 packRock=SAMPLE_TEXTURE2D(_GroundDiffuse3,sampler_GroundDiffuse0,packUV*.82+float2(5.4,13.9)).rgb;
                float rockWeight=saturate(rock),gravelWeight=saturate(gravel*(1-rockWeight)),sandWeight=saturate(sand*(1-rockWeight-gravelWeight));
                float earthWeight=saturate(1-rockWeight-gravelWeight-sandWeight);
                // Height-aware noise masking keeps material borders organic. Texture height makes raised
                // pebbles and ridges win locally, while offset noise prevents broad biome bands and repetition.
                float4 baseWeights=float4(sandWeight,earthWeight,gravelWeight,rockWeight);
                float4 packHeight=float4(
                    SAMPLE_TEXTURE2D(_GroundHeight0,sampler_GroundDiffuse0,packUV).r,
                    SAMPLE_TEXTURE2D(_GroundHeight1,sampler_GroundDiffuse0,packUV.yx*float2(-1,1)+float2(3.2,7.1)).r,
                    SAMPLE_TEXTURE2D(_GroundHeight2,sampler_GroundDiffuse0,packUV*1.28+float2(11.3,2.7)).r,
                    SAMPLE_TEXTURE2D(_GroundHeight3,sampler_GroundDiffuse0,packUV*.82+float2(5.4,13.9)).r);
                float4 maskNoise=float4(Fbm(p*.31+float2(2,17)),Fbm(p*.34+float2(41,-9)),Fbm(p*.29+float2(-27,31)),Fbm(p*.33+float2(13,53)));
                maskNoise=maskNoise*.72+float4(Noise(p*1.31+3),Noise(p*1.47+19),Noise(p*1.23+37),Noise(p*1.39+61))*.28;
                float4 scores=baseWeights*lerp(.78,1.22,maskNoise)+(packHeight-.5)*baseWeights*(_UseGroundTextures*.16);
                float peak=max(max(scores.x,scores.y),max(scores.z,scores.w));
                float4 naturalWeights=saturate((scores-(peak-.22))/.22);
                naturalWeights=naturalWeights*naturalWeights*(3-2*naturalWeights);
                naturalWeights/=max(dot(naturalWeights,float4(1,1,1,1)),.0001);
                sandWeight=naturalWeights.x;earthWeight=naturalWeights.y;gravelWeight=naturalWeights.z;rockWeight=naturalWeights.w;
                float3 packColor=packSand*sandWeight+packEarth*earthWeight+packGravel*gravelWeight+packRock*rockWeight;
                float packLuma=max(.06,dot(packColor,float3(.299,.587,.114)));
                float3 textureDetail=lerp(packLuma.xxx,packColor,.28)/.48;
                albedo*=lerp(1,clamp(textureDetail,.48,1.65),_UseGroundTextures*.72);
                // ddx/ddy recovers the world-space slope from one shared micro-height evaluation.
                // It replaces four high-frequency procedural resamples per fragment.
                float microHeight=Noise(p*2.6)*.055+Noise(p*7.0)*.018+scatteredStone*.018-cracks*.012;
                float2 worldDx=ddx(p),worldDy=ddy(p);float heightDx=ddx(microHeight),heightDy=ddy(microHeight);
                float det=worldDx.x*worldDy.y-worldDx.y*worldDy.x;
                float safeDet=det<0?-max(abs(det),.0001):max(abs(det),.0001);
                float gx=(heightDx*worldDy.y-heightDy*worldDx.y)/safeDet;
                float gz=(worldDx.x*heightDy-worldDy.x*heightDx)/safeDet;
                float3 a=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p*.31)),b=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p*1.37+float2(7.1,3.7)));
                float3 ns=UnpackNormal(SAMPLE_TEXTURE2D(_GroundNormal0,sampler_GroundDiffuse0,packUV));
                float3 ne=UnpackNormal(SAMPLE_TEXTURE2D(_GroundNormal1,sampler_GroundDiffuse0,packUV.yx*float2(-1,1)+float2(3.2,7.1)));
                float3 ng=UnpackNormal(SAMPLE_TEXTURE2D(_GroundNormal2,sampler_GroundDiffuse0,packUV*1.28+float2(11.3,2.7)));
                float3 nr=UnpackNormal(SAMPLE_TEXTURE2D(_GroundNormal3,sampler_GroundDiffuse0,packUV*.82+float2(5.4,13.9)));
                float2 packNormal=(ns.xy*sandWeight+ne.xy*earthWeight+ng.xy*gravelWeight+nr.xy*rockWeight)*_UseGroundTextures;
                float3 n=normalize(i.n+(float3(-gx,0,-gz)*.72+float3(a.x,0,a.y)*.18+float3(b.x,0,b.y)*.11+float3(packNormal.x,0,packNormal.y)*.48)*(1-rock*.22));
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));float ao=1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    ao=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.p)).indirectAmbientOcclusion;
                #endif
                float cloud=Fbm(p*.006+_Time.y*float2(.0021,.0013));
                float cloudShade=lerp(.91,1.0,smoothstep(.38,.68,cloud));
                float packAO=SAMPLE_TEXTURE2D(_GroundAO0,sampler_GroundDiffuse0,packUV).r*sandWeight+SAMPLE_TEXTURE2D(_GroundAO1,sampler_GroundDiffuse0,packUV.yx*float2(-1,1)+float2(3.2,7.1)).r*earthWeight+SAMPLE_TEXTURE2D(_GroundAO2,sampler_GroundDiffuse0,packUV*1.28+float2(11.3,2.7)).r*gravelWeight+SAMPLE_TEXTURE2D(_GroundAO3,sampler_GroundDiffuse0,packUV*.82+float2(5.4,13.9)).r*rockWeight;
                ao*=lerp(1,lerp(.62,1,packAO),_UseGroundTextures*.72);
                float3 lit=albedo*(SampleSH(n)*ao+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation*cloudShade);
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
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
