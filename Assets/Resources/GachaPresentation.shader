Shader "UmaViewer/GachaPresentation"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "Queue"="Overlay" "IgnoreProjector"="True" "RenderType"="Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct Input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            struct Interpolated
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };
            sampler2D _MainTex;
            Interpolated vert(Input input)
            {
                Interpolated output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }
            fixed4 frag(Interpolated input) : SV_Target
            {
                return fixed4(tex2D(_MainTex, input.uv).rgb, 1);
            }
            ENDCG
        }
    }
}
