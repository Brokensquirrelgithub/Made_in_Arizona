Shader "MadeInArizona/OrthographicDepthBlur"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
        Pass
        {
            Name "Orthographic depth blur"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragBlurH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // x/y: far-blur range in eye-depth metres, z: maximum radius in screen pixels.
            float4 _ArizonaOrthoDofParams;

            float EyeDepth(float rawDepth)
            {
                // LinearEyeDepth is perspective-only. Orthographic raw depth is already linear,
                // but must be expanded from 0..1 to the camera's near..far eye-depth range.
                return unity_OrthoParams.w > .5 ? LinearDepthToEyeDepth(rawDepth) : LinearEyeDepth(rawDepth, _ZBufferParams);
            }
            half3 ColorAt(float2 uv) { return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb; }
            half DepthWeight(float center, float sample)
            {
                // Do not blur a distant landscape across the near player silhouette.
                return 1.0-smoothstep(2.5,7.0,abs(center-sample));
            }
            half3 Gaussian(float2 uv, float2 axis)
            {
                float eyeDepth=EyeDepth(SampleSceneDepth(uv));
                float blur=saturate((eyeDepth-_ArizonaOrthoDofParams.x)/max(.01,_ArizonaOrthoDofParams.y-_ArizonaOrthoDofParams.x));
                if(blur<.001)return ColorAt(uv);
                float2 offset=axis*_BlitTexture_TexelSize.xy*(_ArizonaOrthoDofParams.z*blur*1.3333);
                float2 a=uv-offset,b=uv+offset;
                half wa=DepthWeight(eyeDepth,EyeDepth(SampleSceneDepth(a)))*.35294;
                half wb=DepthWeight(eyeDepth,EyeDepth(SampleSceneDepth(b)))*.35294;
                half wc=.29412;
                return (ColorAt(a)*wa+ColorAt(uv)*wc+ColorAt(b)*wb)/max(.001,wa+wc+wb);
            }
            half4 FragBlurH(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return half4(Gaussian(UnityStereoTransformScreenSpaceTex(input.texcoord),float2(1,0)),1);
            }
            half4 FragBlurV(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv=UnityStereoTransformScreenSpaceTex(input.texcoord);
                return half4(Gaussian(uv,float2(0,1)),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "Orthographic depth blur vertical"
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment FragBlurV
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            float4 _ArizonaOrthoDofParams;
            float EyeDepth(float rawDepth){return unity_OrthoParams.w>.5?LinearDepthToEyeDepth(rawDepth):LinearEyeDepth(rawDepth,_ZBufferParams);}
            half3 ColorAt(float2 uv){return SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv).rgb;}
            half DepthWeight(float center,float sample){return 1-smoothstep(2.5,7,abs(center-sample));}
            half3 Gaussian(float2 uv,float2 axis){float eyeDepth=EyeDepth(SampleSceneDepth(uv));float blur=saturate((eyeDepth-_ArizonaOrthoDofParams.x)/max(.01,_ArizonaOrthoDofParams.y-_ArizonaOrthoDofParams.x));if(blur<.001)return ColorAt(uv);float2 offset=axis*_BlitTexture_TexelSize.xy*(_ArizonaOrthoDofParams.z*blur*1.3333);float2 a=uv-offset,b=uv+offset;half wa=DepthWeight(eyeDepth,EyeDepth(SampleSceneDepth(a)))*.35294,wb=DepthWeight(eyeDepth,EyeDepth(SampleSceneDepth(b)))*.35294,wc=.29412;return(ColorAt(a)*wa+ColorAt(uv)*wc+ColorAt(b)*wb)/max(.001,wa+wc+wb);}
            half4 FragBlurV(Varyings input):SV_Target{UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);float2 uv=UnityStereoTransformScreenSpaceTex(input.texcoord);return half4(Gaussian(uv,float2(0,1)),1);}
            ENDHLSL
        }
    }
}
