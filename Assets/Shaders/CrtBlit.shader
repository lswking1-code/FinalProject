Shader "Hidden/CrtBlit"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
        }

        Pass
        {
            Name "CrtBlit"
            ZWrite Off
            Cull Off
            ZTest Always

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Fragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _LensValue;
            float _RGBSplitOffset;
            float _ScanlineIntensity;
            float _ScanlinePixelSize;
            float _VignetteIntensity;

            float2 WarpUv(float2 uv, float lens)
            {
                float2 centered = (uv - 0.5) * 0.97;
                float radiusSq = dot(centered, centered);
                return centered * (1.0 + lens * radiusSq) + 0.5;
            }

            half3 SampleCrt(float2 uv)
            {
                if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
                    return 0.0;
                return SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearRepeat, uv).rgb;
            }

            half4 Fragment(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 warped = WarpUv(uv, _LensValue);
                float2 split = float2(_RGBSplitOffset, 0.0);

                half red = SampleCrt(warped + split).r;
                half green = SampleCrt(warped).g;
                half blue = SampleCrt(warped - split).b;

                float2 centered = uv * 2.0 - 1.0;
                float radiusSq = dot(centered, centered);
                float lineSize = max(_ScanlinePixelSize, 1.0);
                half scan = 1.0 - _ScanlineIntensity * (frac(uv.y * _ScreenParams.y / lineSize) < 0.5);
                half vignette = 1.0 - saturate(_VignetteIntensity * radiusSq * 0.35);
                return half4(half3(red, green, blue) * scan * vignette, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
