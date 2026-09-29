Shader "MadeInArizona/BiomeTerrain"
{
    Properties
    {
        _BumpMap("Fallback terrain normal",2D)="bump"{}
        _GroundAlbedoArray("Ground albedo (RGB) + height (A) array",2DArray)=""{}
        _GroundNormalArray("Ground normal (RG) + AO (B) array",2DArray)=""{}
        _UseGroundTextures("Use Outdoor Ground Textures",Range(0,1))=0
        _OccluderCut("See-through where it hides the car",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        float _OccluderCut;
        float4 _SceneryVehicle; // player car position, w = 1 while it exists (set by LivingWorldDetail)
        // Landforms (not the ground itself) dither open where they stand between the camera and the player's car and rise
        // above it, so a car tucked behind a mesa or cover rock stays visible from the overhead camera.
        void OccluderCut(float3 world,float4 positionCS)
        {
            if(_OccluderCut<=0||_SceneryVehicle.w<=0)return;
            float3 d=world-_SceneryVehicle.xyz,forward=-UNITY_MATRIX_V[2].xyz;float along=dot(d,forward);
            float lateral=length(d-forward*along);
            float cut=saturate((-along-1.5)*.5)*saturate((world.y-_SceneryVehicle.y-.6)/1.2)*(1-smoothstep(6,10,lateral))*.85*_OccluderCut;
            if(cut>0)clip(frac(52.9829189*frac(dot(positionCS.xy,float2(.06711056,.00583715))))-cut);
        }
        ENDHLSL
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
            #include "CloudShadows.hlsl"

            TEXTURE2D(_BumpMap); SAMPLER(sampler_BumpMap);
            // All fourteen Outdoor Ground Textures live in two arrays built at load (see WorldArt.GroundArrays).
            TEXTURE2D_ARRAY(_GroundAlbedoArray); SAMPLER(sampler_GroundAlbedoArray); TEXTURE2D_ARRAY(_GroundNormalArray);
            float _UseGroundTextures;
            float4 _ElevationRange; // min, max, enabled (set by GeneratedWorld)
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
                OccluderCut(i.world,i.p);
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
                // Elevation readability from the top-down camera: higher ground is sun-bleached, lower ground deeper,
                // and the ramps between levels carry a darker, eroded band.
                float altitude=saturate((i.world.y-_ElevationRange.x)/max(1,_ElevationRange.y-_ElevationRange.x));
                albedo*=lerp(1,lerp(.8,1.14,altitude),_ElevationRange.z);
                albedo*=1-saturate(slope*7)*.2;
                // Each biome draws on its own collection of ground textures. Candidate layers are weighted by the
                // material role (sand/earth/gravel/rock), the biome, and patch noise at ~55 m, ~16 m and ~5 m, so a biome
                // reads as a mosaic of related grounds rather than one repeating texture. Only the three strongest
                // layers are sampled, then blended by their height maps.
                float rockWeight=saturate(rock),gravelWeight=saturate(gravel*(1-rockWeight)),sandWeight=saturate(sand*(1-rockWeight-gravelWeight));
                float earthWeight=saturate(1-rockWeight-gravelWeight-sandWeight);
                // Noise-masked role borders stay organic instead of forming broad bands.
                float4 baseWeights=float4(sandWeight,earthWeight,gravelWeight,rockWeight);
                float4 maskNoise=float4(Fbm(p*.31+float2(2,17)),Fbm(p*.34+float2(41,-9)),Fbm(p*.29+float2(-27,31)),Fbm(p*.33+float2(13,53)));
                float4 scores=baseWeights*lerp(.78,1.22,maskNoise);
                float peak=max(max(scores.x,scores.y),max(scores.z,scores.w));
                float4 naturalWeights=saturate((scores-(peak-.22))/.22);naturalWeights=naturalWeights*naturalWeights*(3-2*naturalWeights);
                naturalWeights/=max(dot(naturalWeights,float4(1,1,1,1)),.0001);
                sandWeight=naturalWeights.x;earthWeight=naturalWeights.y;gravelWeight=naturalWeights.z;rockWeight=naturalWeights.w;
                float forest=saturate(green*1.6),riverGreen=saturate(i.c.g*1.6-i.c.r*1.1-.05)*forest;
                float highland=saturate(1-(max(i.c.r,max(i.c.g,i.c.b))-min(i.c.r,min(i.c.g,i.c.b)))*4.5)*(1-forest);
                float desert=saturate(1-forest-highland);
                float patchA=smoothstep(.32,.68,Fbm(p*.018+float2(41,7))),patchB=smoothstep(.30,.70,Fbm(p*.061+float2(-9,23))),patchC=smoothstep(.35,.65,Noise(p*.21+float2(5,-17)));
                float w[14];
                w[0]=earthWeight*(desert*(1-patchA)*patchC*.8+highland*.9)+sandWeight*forest*.3;   // brown soil
                w[1]=sandWeight*desert*(1-patchB*.55);                                             // pale sand
                w[2]=sandWeight*forest*.5*(1-patchA);                                             // pale ground with weeds
                w[3]=earthWeight*forest*patchB*(1-riverGreen);                                    // weeds
                w[4]=earthWeight*forest*(1-patchB)*patchA;                                        // moss and twigs
                w[5]=(earthWeight+sandWeight)*riverGreen;                                         // lush grass by water
                w[6]=gravelWeight*(1-patchB*.6)+earthWeight*desert*patchB*patchC*.35;             // gravel speckle
                w[7]=earthWeight*desert*(1-patchA)*(1-patchC*.8)+gravelWeight*desert*patchB*.4;   // red pebble dirt
                w[8]=gravelWeight*(forest+highland)*patchB+earthWeight*highland*patchA*.7;        // olive rocky ground
                w[9]=earthWeight*forest*(1-patchB)*(1-patchA);                                    // dark grass tufts
                w[10]=sandWeight*desert*patchB*.55+earthWeight*desert*patchA*(1-patchB)*.35;      // dry fibrous ground
                w[11]=(sandWeight+earthWeight)*forest*patchC*.45;                                 // leaf and grass litter
                w[12]=earthWeight*desert*patchA*(1-patchB*.35)+rockWeight*desert*.45;             // red cracked earth
                w[13]=rockWeight*(1-desert*.45);                                                  // bedrock
                // Keep the three strongest candidates.
                int i0=0,i1=1,i2=2;float w0=-1,w1=-1,w2=-1;
                [unroll]for(int k=0;k<14;k++)
                {
                    float v=w[k];
                    if(v>w0){w2=w1;i2=i1;w1=w0;i1=i0;w0=v;i0=k;}
                    else if(v>w1){w2=w1;i2=i1;w1=v;i1=k;}
                    else if(v>w2){w2=v;i2=k;}
                }
                // Per-layer UV rotation and offset hides tiling; the same transform is used for normals.
                float2 packUV=p*.115;
                float2 uv0=float2(packUV.x+i0*.37,packUV.y+i0*.61),uv1=float2(-packUV.y+i1*.29,packUV.x+i1*.53),uv2=float2(packUV.y+i2*.43,-packUV.x+i2*.17);
                float4 t0=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,uv0,i0);
                float4 t1=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,uv1,i1);
                float4 t2=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,uv2,i2);
                float3 layerScore=float3(w0,max(w1,0),max(w2,0))/max(w0,.0001)+float3(t0.a,t1.a,t2.a)*.45;
                float layerPeak=max(layerScore.x,max(layerScore.y,layerScore.z));
                float3 layerWeight=saturate((layerScore-(layerPeak-.28))/.28);
                layerWeight=layerWeight*layerWeight*(3-2*layerWeight);layerWeight/=max(dot(layerWeight,1),.0001);
                float3 packColor=t0.rgb*layerWeight.x+t1.rgb*layerWeight.y+t2.rgb*layerWeight.z;
                float packLuma=max(.05,dot(packColor,float3(.299,.587,.114)));
                // Keep the Arizona palette's brightness while letting each texture's own hue and pattern show.
                float paletteLuma=max(.05,dot(albedo,float3(.299,.587,.114)));
                float3 detailOnly=albedo*clamp(packLuma/.42,.5,1.7);
                // Partial exposure match: each texture keeps 40% of its own brightness, so darker grounds still read.
                float3 photo=packColor*clamp(pow(paletteLuma/packLuma,.6),.6,1.6);
                photo=lerp(photo,photo*albedo/paletteLuma*.5+photo*.5,.4);
                albedo=lerp(albedo,lerp(detailOnly,photo,.7),_UseGroundTextures);
                // Cliff walls: side-projected rock (no smearing down the face) with horizontal sandstone strata.
                float cliffWeight=smoothstep(.22,.42,slope)*_UseGroundTextures;
                UNITY_BRANCH if(cliffWeight>.001)
                {
                    float3 an=abs(normalize(i.n));float2 side=an.xz/max(an.x+an.z,.001);
                    int cliffSlice=desert>.5?12:13;
                    float3 rockX=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,i.world.zy*.12,cliffSlice).rgb;
                    float3 rockZ=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,i.world.xy*.12+.37,cliffSlice).rgb;
                    float3 rockTex=rockX*side.x+rockZ*side.y;
                    float band=i.world.y*.62+Fbm(p*.035+float2(3,9))*2.4;
                    float strata=Noise(float2(band,band*.13+7))*.7+Noise(float2(band*3.1,1.7))*.3;
                    float3 strataTint=lerp(lerp(float3(.30,.27,.23),float3(.50,.44,.36),strata),lerp(float3(.44,.22,.13),float3(.70,.44,.27),strata),desert);
                    float rockLuma=max(.05,dot(rockTex,float3(.299,.587,.114)));
                    float3 cliffAlbedo=strataTint*clamp(rockLuma/.38,.55,1.45)*lerp(.82,1.1,Noise(p*.9));
                    albedo=lerp(albedo,cliffAlbedo,cliffWeight);
                    // Shaded, crumbly foot and a sun-bleached rim help the wall read from the overhead camera.
                    float foot=smoothstep(.1,.3,slope)*(1-smoothstep(.3,.5,slope));
                    albedo*=1-foot*.18*saturate(dot(i.n.xz,i.n.xz)*4);
                }
                // River bed (vertex alpha: 1 dry, .5 waterline, 0 deepest): rounded wet gravel with silt drifts under the
                // water, darkening and turning olive with depth, plus a darker wet band just above the waterline.
                float wet=saturate((1-i.c.a)*2),underwater=saturate((.5-i.c.a)*2);
                UNITY_BRANCH if(wet>.001)
                {
                    float2 bedUV=p*.21;
                    float3 pebbles=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,bedUV,6).rgb;
                    float3 silt=SAMPLE_TEXTURE2D_ARRAY(_GroundAlbedoArray,sampler_GroundAlbedoArray,float2(bedUV.y,-bedUV.x)*.6+.31,7).rgb;
                    float drift=smoothstep(.38,.66,Fbm(p*.07+float2(7,-11))+underwater*.12);
                    float3 bed=lerp(pebbles,dot(silt,float3(.299,.587,.114))*float3(.86,.84,.7),drift*.7);
                    bed=lerp(albedo*float3(.7,.68,.6),bed*1.05,_UseGroundTextures);
                    bed*=lerp(.7,.46,underwater);bed*=lerp(float3(1,1,1),float3(.78,.94,.84),underwater);
                    albedo=lerp(albedo,bed,wet);
                }
                // ddx/ddy recovers the world-space slope from one shared micro-height evaluation.
                // It replaces four high-frequency procedural resamples per fragment.
                float microHeight=Noise(p*2.6)*.055+Noise(p*7.0)*.018+scatteredStone*.018-cracks*.012;
                float2 worldDx=ddx(p),worldDy=ddy(p);float heightDx=ddx(microHeight),heightDy=ddy(microHeight);
                float det=worldDx.x*worldDy.y-worldDx.y*worldDy.x;
                float safeDet=det<0?-max(abs(det),.0001):max(abs(det),.0001);
                float gx=(heightDx*worldDy.y-heightDy*worldDx.y)/safeDet;
                float gz=(worldDx.x*heightDy-worldDy.x*heightDx)/safeDet;
                float3 a=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p*.31)),b=UnpackNormal(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p*1.37+float2(7.1,3.7)));
                float4 n0=SAMPLE_TEXTURE2D_ARRAY(_GroundNormalArray,sampler_GroundAlbedoArray,uv0,i0);
                float4 n1=SAMPLE_TEXTURE2D_ARRAY(_GroundNormalArray,sampler_GroundAlbedoArray,uv1,i1);
                float4 n2=SAMPLE_TEXTURE2D_ARRAY(_GroundNormalArray,sampler_GroundAlbedoArray,uv2,i2);
                // Undo each layer's UV rotation so the tangent-space normals line up in world space.
                float2 nx0=n0.xy*2-1,nx1=n1.xy*2-1,nx2=n2.xy*2-1;
                float2 packNormal=(nx0*layerWeight.x+float2(nx1.y,-nx1.x)*layerWeight.y+float2(-nx2.y,nx2.x)*layerWeight.z)*_UseGroundTextures;
                float3 n=normalize(i.n+(float3(-gx,0,-gz)*.72+float3(a.x,0,a.y)*.18+float3(b.x,0,b.y)*.11+float3(packNormal.x,0,packNormal.y)*.48)*(1-rock*.22));
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));sun.shadowAttenuation*=CloudShadow(i.world);float ao=1,directAO=1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor occlusion=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.p));ao=occlusion.indirectAmbientOcclusion;directAO=occlusion.directAmbientOcclusion;
                #endif
                float cloud=Fbm(p*.006+_Time.y*float2(.0021,.0013));
                // Faint ground-only mottling, replaced by the real drifting cloud shadows (CloudShadows.hlsl) when they are on.
                float cloudShade=lerp(lerp(.91,1.0,smoothstep(.38,.68,cloud)),1,saturate(_CloudShadowParams.x));
                float packAO=n0.b*layerWeight.x+n1.b*layerWeight.y+n2.b*layerWeight.z;
                ao*=lerp(1,lerp(.62,1,packAO),_UseGroundTextures*.72);
                float3 lit=albedo*(SampleSH(n)*ao+sun.color*saturate(dot(n,sun.direction))*sun.shadowAttenuation*cloudShade*directAO);
                // Sun caustics dancing on the submerged bed.
                UNITY_BRANCH if(underwater>.001)
                {
                    float ce,ch,ce2,ch2;
                    Voronoi(p*.9+_Time.y*float2(.21,.13),ce,ch);Voronoi(p*1.3-_Time.y*float2(.11,.19)+7.3,ce2,ch2);
                    float caustic=(1-smoothstep(0,.14,ce))*(1-smoothstep(0,.2,ce2));
                    lit+=albedo*sun.color*sun.shadowAttenuation*caustic*1.4*saturate(underwater*3)*(1-underwater*.55);
                }
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
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex depthVert
            #pragma fragment depthFrag
            struct DA { float4 p:POSITION; };
            struct DV { float4 p:SV_POSITION; float3 world:TEXCOORD0; };
            DV depthVert(DA v){DV o;o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);return o;}
            half depthFrag(DV i):SV_Target{OccluderCut(i.world,i.p);return i.p.z;}
            ENDHLSL
        }
    }
}
