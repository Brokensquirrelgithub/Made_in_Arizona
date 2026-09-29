Shader "MadeInArizona/CarPaint"
{
    // Glossy automotive paint: base coat with metallic flake under a clear coat. The scene has no reflection
    // probe or skybox, so the clear coat reflects a procedural desert sky with a bright horizon band and sun.
    // Reflections use a gently "pillowed" normal so flat procedural panels still catch rolling highlights.
    Properties
    {
        _BaseColor("Paint",Color)=(1,1,1,1)
        _Metallic("Metallic flake",Range(0,1))=.35
        _Smoothness("Base coat smoothness",Range(0,1))=.62
        _ClearCoat("Clear coat",Range(0,1))=1
        _Flake("Flake sparkle",Range(0,1))=.5
        _Pillow("Panel curvature for reflections",Range(0,3))=1.9
        _GlintType("Glint material type (0 paint, 2 glass)",Float)=0
        _Wear("Dust and wear response",Range(0,2))=1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "CloudShadows.hlsl"
        #include "SunGlint.hlsl"
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;half _Metallic;half _Smoothness;half _ClearCoat;half _Flake;half _Pillow;half _GlintType;half _Wear;
        // Per-car dust (VehicleDust.cs sets these on the car's own copy of the material; zero means clean).
        float4 _DustRow0,_DustRow1,_DustRow2,_DustState,_DustAxles,_DustTint;
        CBUFFER_END
        #include "CarDust.hlsl"
        struct A {float4 p:POSITION;float3 n:NORMAL;};
        struct V {float4 p:SV_POSITION;float3 world:TEXCOORD0;float3 n:TEXCOORD1;float3 bent:TEXCOORD2;float3 local:TEXCOORD3;float fog:TEXCOORD4;float3 objN:TEXCOORD5;};
        V vert(A v)
        {
            V o;o.world=TransformObjectToWorld(v.p.xyz);o.p=TransformWorldToHClip(o.world);
            o.n=TransformObjectToWorldNormal(v.n);
            o.bent=TransformObjectToWorldNormal(normalize(v.n+v.p.xyz*_Pillow));
            // Metric object-space position keeps flakes fixed to the panel while the car moves.
            float3 scale=float3(length(UNITY_MATRIX_M._m00_m10_m20),length(UNITY_MATRIX_M._m01_m11_m21),length(UNITY_MATRIX_M._m02_m12_m22));
            o.local=v.p.xyz*scale;o.objN=v.n;o.fog=ComputeFogFactor(o.p.z);return o;
        }
        ENDHLSL
        Pass
        {
            Name "ForwardLit" Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile_fog
            float Hash(float3 p){return frac(sin(dot(p,float3(127.1,311.7,74.7)))*43758.5453);}
            float Ggx(float nh,float roughness){float a=roughness*roughness;float a2=a*a;float d=nh*nh*(a2-1)+1;return a2/(PI*d*d+1e-5);}
            float3 Sky(float3 r,float3 sunDir,float3 sunColor)
            {
                float up=saturate(r.y);
                float3 sky=lerp(float3(.95,.88,.76),float3(.30,.52,.86),pow(up,.5));
                float3 ground=lerp(float3(.66,.55,.42),float3(.27,.21,.16),saturate(-r.y*3));
                float3 env=r.y>=0?sky:ground;
                // A bright skyline band and two soft "studio" panels overhead give every panel a readable reflection.
                env+=float3(1,.94,.84)*.45*exp(-abs(r.y-.05)*18);
                env+=float3(1,.98,.94)*.55*smoothstep(.93,.985,dot(r,normalize(float3(.35,.8,.45))));
                env+=float3(1,.98,.94)*.35*smoothstep(.95,.99,dot(r,normalize(float3(-.55,.7,-.25))));
                // The gameplay camera looks down steeply from the south, so roofs and hoods reflect only a narrow patch
                // of sky to the north. Two softboxes sit near that patch in world space: highlights roll across the
                // curved panels as a car turns, while other views (garage, close-ups) do not wash the paint out.
                env+=float3(1,.97,.92)*1.4*smoothstep(.975,.995,dot(r,normalize(float3(.2,.74,.64))));
                env+=float3(1,.97,.92)*.6*smoothstep(.94,.975,dot(r,normalize(float3(-.32,.78,.54))));
                float s=saturate(dot(r,sunDir));
                env+=sunColor*(pow(s,700)*8+pow(s,28)*.3);
                return env;
            }
            half4 frag(V i):SV_Target
            {
                float3 n=normalize(i.n),b=normalize(i.bent);
                float3 v=GetWorldSpaceNormalizeViewDir(i.world);
                Light sun=GetMainLight(TransformWorldToShadowCoord(i.world));sun.shadowAttenuation*=CloudShadow(i.world);
                float atten=sun.shadowAttenuation*sun.distanceAttenuation;
                float ao=1,directAO=1;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor occlusion=GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.p));ao=occlusion.indirectAmbientOcclusion;directAO=occlusion.directAmbientOcclusion;
                #endif
                float3 albedo=_BaseColor.rgb;
                // Dust, scratches, fingerprints and oxidation roughen the paint and clear coat in patches.
                float4 wear=SampleWear(i.local,i.objN);
                float clean;
                float metal=_Metallic,roughness=WornRoughness(max(.12,1-_Smoothness),wear,_Wear,clean);
                bool glass=_GlintType>1.5;
                float coatRoughness=WornRoughness(glass?.03:.06,wear,_Wear*(glass?.7:1),clean);
                // Micro-surface waviness and orange peel tilt the clear coat slightly, fragmenting sun highlights.
                float3 micro=TransformObjectToWorldNormal(MicroNormalOffset(i.local,i.objN,glass?.35:1),false);
                float3 coatNormal=normalize(b+micro);
                // Desert dust caked on by driving: matte and tan, and it takes the gloss and clear coat where it sits.
                float dust=CarDust(i.world,n);
                albedo=lerp(albedo,CarDustColor(i.world),dust);
                metal*=1-dust;roughness=lerp(roughness,.92,dust);coatRoughness=lerp(coatRoughness,.9,dust);clean*=1-dust;
                float coatLeft=1-dust*.97;
                float3 f0=lerp(float3(.04,.04,.04),albedo,metal);
                float nl=saturate(dot(n,sun.direction));
                float3 h=normalize(sun.direction+v);
                // Base coat: diffuse paint plus a soft metallic lobe.
                float3 lit=albedo*(1-metal*.55)*(SampleSH(n)*ao+sun.color*nl*atten*directAO);
                float3 baseFresnel=f0+(1-f0)*pow(1-saturate(dot(h,v)),5);
                lit+=baseFresnel*min(Ggx(saturate(dot(b,h)),roughness)*.25,6)*sun.color*nl*atten;
                // Metal flake: sparse randomly tilted facets glint when they line up with the sun.
                float3 cell=floor(i.local*150);
                float3 tilt=float3(Hash(cell),Hash(cell+17.3),Hash(cell+41.9))*2-1;
                float flake=pow(saturate(dot(normalize(n+tilt*.6),h)),500)*step(.6,Hash(cell+7.1));
                lit+=(albedo+.35)*flake*_Flake*coatLeft*3*sun.color*atten*saturate(nl*4);
                // Clear coat: Fresnel-weighted mirror of the desert sky and a sharp sun highlight.
                float nv=saturate(dot(b,v));
                // Exaggerated at normal incidence (stylised gloss): from overhead, physical 4-6% read as matte plastic.
                float coat=(.12+.88*pow(1-nv,4))*_ClearCoat*lerp(.55,1,clean)*coatLeft;
                float envScale=lerp(.55,1.15,saturate(dot(sun.color,float3(.2126,.7152,.0722))/2.5));
                float3 r=reflect(-v,b);
                float3 env=Sky(r,sun.direction,sun.color)*envScale;
                float3 blurry=Sky(normalize(lerp(r,b,roughness)),sun.direction,sun.color*.25)*envScale;
                lit=lit*(1-coat)+env*coat+blurry*f0*(1-coat)*(.35+.65*metal)*ao;
                // Sun on the clear coat: an unclamped HDR GGX highlight, tiny and intense on clean gloss, broad and weak
                // where dust has roughened it, plus the stylised glint when the mirrored sun lines up with the eye.
                float3 eye=GlintView(i.world,v),radiance=sun.color*atten;
                lit+=SunSpecular(coatNormal,eye,sun.direction,coatRoughness,float3(.04,.04,.04),radiance)*_ClearCoat*coatLeft;
                lit+=SunGlint(coatNormal,eye,sun.direction,radiance,coatRoughness,clean,_GlintType)*_ClearCoat;
                #if defined(_ADDITIONAL_LIGHTS)
                    uint count=GetAdditionalLightsCount();
                    for(uint lightIndex=0;lightIndex<count;lightIndex++)
                    {
                        Light local=GetAdditionalLight(lightIndex,i.world,half4(1,1,1,1));
                        float ln=saturate(dot(n,local.direction));
                        float3 lh=normalize(local.direction+v);
                        float3 glow=local.color*local.distanceAttenuation*local.shadowAttenuation;
                        lit+=albedo*glow*ln+glow*(.04+_ClearCoat*.08)*coatLeft*min(Ggx(saturate(dot(b,lh)),.18)*.25,20)*ln;
                    }
                #endif
                return half4(MixFog(lit,i.fog),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster" Tags { "LightMode"="ShadowCaster" }
            ZWrite On ColorMask 0
            HLSLPROGRAM
            #pragma vertex shadow
            #pragma fragment depth
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            V shadow(A v)
            {
                V o=vert(v);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirection=normalize(_LightPosition-o.world);
                #else
                    float3 lightDirection=_LightDirection;
                #endif
                o.p=TransformWorldToHClip(ApplyShadowBias(o.world,normalize(o.n),lightDirection));
                #if UNITY_REVERSED_Z
                    o.p.z=min(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
                #else
                    o.p.z=max(o.p.z,UNITY_NEAR_CLIP_VALUE*o.p.w);
                #endif
                return o;
            }
            half4 depth(V i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly" Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment depth
            half4 depth(V i):SV_Target{return 0;}
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals" Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment normals
            half4 normals(V i):SV_Target{return half4(normalize(i.n),0);}
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
