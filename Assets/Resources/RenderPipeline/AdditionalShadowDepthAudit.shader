Shader "Hidden/UmaViewer/AdditionalShadowDepthAudit"
{
    SubShader
    {
        Pass
        {
            ZTest Always ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            Texture2D<float> _GallopShadowDepthSource;
            struct V { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            V vert(uint id : SV_VertexID)
            {
                V o; o.uv=float2((id<<1)&2,id&2); o.position=float4(o.uv*2-1,0,1); return o;
            }
            float4 frag(V i) : SV_Target
            {
                uint w,h; _GallopShadowDepthSource.GetDimensions(w,h);
                float d=_GallopShadowDepthSource.Load(int3(min(uint2(i.uv*float2(w,h)),uint2(w-1,h-1)),0));
                return float4(d,d,d,1);
            }
            ENDHLSL
        }
    }
}
