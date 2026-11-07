Shader "Hidden/UmaViewer/LiveLegacyMatrixAudit"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            float4x4 unity_MatrixVP;
            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            {
                return float4(id==2?3:-1,id==1?3:-1,0,1);
            }
            float4 Frag(float4 p : SV_POSITION) : SV_Target
            {
                return unity_MatrixVP[(uint)p.x%4];
            }
            ENDHLSL
        }
    }
}
