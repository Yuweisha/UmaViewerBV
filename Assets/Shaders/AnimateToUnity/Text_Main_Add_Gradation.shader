Shader "AnimateToUnity/Text_Main_Add_Gradation"
{
 Properties
 {
  _MainTex("Color Texture",2D)="white"{}
  _uvColorInfo("UV Color",Vector)=(1,1,0,0)
  _uvAlphaInfo("UV Alpha",Vector)=(1,1,0,0)
  _gradStartColor("Gradation Start Color",Color)=(1,1,1,1)
  _gradEndColor("Gradation End Color",Color)=(1,1,1,1)
  _gradStartPosition("Gradation Start Position",Vector)=(0,1,0,0)
  _gradEndPosition("Gradation End Position",Vector)=(0,0,0,0)
  _multiplyColor("Multiply Color",Color)=(1,1,1,1)
  _colorOffset("Color Offset",Color)=(0,0,0,0)
  _StencilComp("Stencil Comparison",Float)=8
  _Stencil("Stencil ID",Float)=0
  _StencilOp("Stencil Operation",Float)=0
  _StencilWriteMask("Stencil Write Mask",Float)=255
  _StencilReadMask("Stencil Read Mask",Float)=255
  _BlendSecondSrc("Alpha Source",Float)=0
  _BlendSecondDst("Alpha Destination",Float)=1
  _ColorMask("Color Mask",Float)=15
 }
 SubShader
 {
  Tags{"Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True"}
  Pass
  {
   Cull Off ZWrite Off ZTest LEqual
   Blend SrcAlpha One, [_BlendSecondSrc] [_BlendSecondDst]
   ColorMask [_ColorMask]
   Stencil{Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]}
   HLSLPROGRAM
   #pragma vertex vert
   #pragma fragment frag
   #include "UnityCG.cginc"
   sampler2D _MainTex;
   float4 _uvColorInfo,_multiplyColor,_colorOffset;
   float4 _gradStartColor,_gradEndColor,_gradStartPosition,_gradEndPosition;
   struct appdata{float4 vertex:POSITION;float4 color:COLOR;float2 uv:TEXCOORD0;float2 offsetRG:TEXCOORD1;float2 offsetBA:TEXCOORD2;};
   struct v2f{float4 vertex:SV_POSITION;float4 color:COLOR0;float4 offset:COLOR1;float2 uv:TEXCOORD0;};
   v2f vert(appdata v){v2f o;o.vertex=UnityObjectToClipPos(v.vertex);float3 world=mul(unity_ObjectToWorld,v.vertex).xyz;
    float3 direction=_gradEndPosition.xyz-_gradStartPosition.xyz;
    float projection=max(dot(direction,world-_gradStartPosition.xyz),0.0)/dot(direction,direction);
    float3 projected=direction*projection;
    float length=sqrt(dot(direction,direction));
    float rate=min(length,sqrt(dot(projected,projected)))/length;
    o.color=v.color*(_gradStartColor+rate*(_gradEndColor-_gradStartColor));o.offset=float4(v.offsetRG,v.offsetBA);o.uv=v.uv*_uvColorInfo.xy+_uvColorInfo.zw;return o;}
   float4 frag(v2f i):SV_Target
   {
    float4 tex=tex2D(_MainTex,i.uv);
    return float4(i.color.rgb*_multiplyColor.rgb+_colorOffset.rgb,tex.a*(i.color.a*_multiplyColor.a+_colorOffset.a));
   }
   ENDHLSL
  }
 }
}
