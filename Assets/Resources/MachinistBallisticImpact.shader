Shader "Combat/Machinist Ballistic Pixel Impact"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        _AlphaCutoff ("Remove soft fringe", Range(0,1)) = 0.65
        _PixelStep ("Source pixels per visible pixel", Float) = 4
        [HideInInspector] _PixelPivot ("Pixel grid anchor", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "CanUseSpriteAtlas"="False" }
        Cull Off Lighting Off ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            Tags { "LightMode"="Universal2D" }
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes
            {
                float3 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_TexelSize;
                float _AlphaCutoff;
                float _PixelStep;
                float4 _PixelPivot;
            CBUFFER_END
            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                v.positionOS = UnityFlipSprite(v.positionOS, unity_SpriteProps.xy);
                o.positionCS = TransformObjectToHClip(v.positionOS);
                o.uv = v.uv;
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }
            float4 frag(Varyings i) : SV_Target
            {
                // Quantized sampling and alpha cutout keep the generated source crisp in motion.
                float step = max(1.0, _PixelStep);
                float2 pixel = i.uv * _MainTex_TexelSize.zw;
                float2 center = (floor((pixel - _PixelPivot.xy) / step) + 0.5) * step + _PixelPivot.xy;
                float2 uv = center * _MainTex_TexelSize.xy;
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                // Preserve thin sparks when reducing detail: retain the strongest covered sample
                // in each coarse cell, while the output remains one flat, hard-edged pixel.
                float2 offset = _MainTex_TexelSize.xy * step * 0.3;
                float4 a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + offset);
                float4 b = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - offset);
                float4 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(offset.x, -offset.y));
                float4 d = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-offset.x, offset.y));
                if (a.a > tex.a) tex = a;
                if (b.a > tex.a) tex = b;
                if (c.a > tex.a) tex = c;
                if (d.a > tex.a) tex = d;
                clip(tex.a - _AlphaCutoff);
                // Electrical sparks can take a cool tint; neutral gray dust stays neutral.
                float warm = saturate((tex.r - tex.b) * 4.0);
                float tintStrength = 1.0 - min(i.color.r, min(i.color.g, i.color.b));
                float value = max(tex.r, max(tex.g, tex.b));
                float gray = dot(tex.rgb, float3(0.2126, 0.7152, 0.0722));
                clip(gray - 0.004);
                float3 baseColor = lerp(gray.xxx, tex.rgb, warm);
                float3 rgb = lerp(baseColor, value * i.color.rgb, warm * tintStrength);
                // Reduce palette in perceptual space so dark dust does not collapse to black
                // or amplify tiny blue/purple noise in the original texture.
                float3 palette = floor(sqrt(max(rgb, 0.0)) * 12.0 + 0.5) / 12.0;
                rgb = palette * palette;
                return float4(rgb, i.color.a);
            }
            ENDHLSL
        }
    }
}
