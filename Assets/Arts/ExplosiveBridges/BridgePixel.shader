Shader "Game/BridgePixel"
{
 Properties
 {
  [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
  _Color ("Tint", Color) = (1,1,1,1)
  _PixelStep ("Source pixel cluster", Float) = 6
  _BlackKey ("Remove black background", Float) = 1
 }
 SubShader
 {
  Tags { "Queue"="Transparent" "RenderType"="Transparent" "CanUseSpriteAtlas"="False" }
  Cull Off ZWrite Off Lighting Off
  Blend SrcAlpha OneMinusSrcAlpha
  Pass
  {
   CGPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   struct appdata { float4 vertex:POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   struct v2f { float4 vertex:SV_POSITION; float2 uv:TEXCOORD0; fixed4 color:COLOR; };
   sampler2D _MainTex; float4 _MainTex_TexelSize; fixed4 _Color; float _PixelStep; float _BlackKey;
   v2f vert(appdata v) { v2f o; o.vertex=UnityObjectToClipPos(v.vertex);o.uv=v.uv;o.color=v.color*_Color;return o; }
   fixed4 frag(v2f i):SV_Target
   {
    float step=max(1,_PixelStep);
    float2 uv=(floor(i.uv*_MainTex_TexelSize.zw/step)+.5)*step*_MainTex_TexelSize.xy;
    fixed4 c=tex2D(_MainTex,uv);
    c.a*=lerp(1,smoothstep(.025,.085,max(c.r,max(c.g,c.b))),_BlackKey);
    return c*i.color;
   }
   ENDCG
  }
 }
}
