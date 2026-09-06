Shader "Combat/Machinist Pixel Impact"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        [HideInInspector] _Color ("Tint", Color) = (1,1,1,1)
        [HideInInspector] _PixelStep ("Source pixels per visible pixel", Float) = 0
        [HideInInspector] _PixelPivot ("Pixel grid anchor", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" "CanUseSpriteAtlas"="True" }
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
            struct appdata
            {
                float3 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _MainTex_TexelSize;
                float _PixelStep;
                float4 _PixelPivot;
            CBUFFER_END
            v2f vert(appdata v)
            {
                v2f o = (v2f)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                SetUpSpriteInstanceProperties();
                v.vertex = UnityFlipSprite(v.vertex, unity_SpriteProps.xy);
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                o.color = v.color * _Color * unity_SpriteColor;
                return o;
            }
            float4 frag(v2f i) : SV_Target
            {
                float2 uv = i.uv;
                if (_PixelStep > 0.0)
                {
                    float2 pixel = uv * _MainTex_TexelSize.zw;
                    pixel = (floor((pixel - _PixelPivot.xy) / _PixelStep) + 0.5)
                        * _PixelStep + _PixelPivot.xy;
                    uv = pixel * _MainTex_TexelSize.xy;
                }
                float4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                // White-hot centres remain white; cyan edges take the weapon palette.
                float white = smoothstep(0.55, 0.95, min(tex.r, min(tex.g, tex.b)));
                float value = max(tex.r, max(tex.g, tex.b));
                return float4(lerp(i.color.rgb * value, float3(1, 0.98, 0.9), white), tex.a * i.color.a);
            }
            ENDHLSL
        }
    }
}
