Shader "Hidden/UmaViewer/LiveMatrixAudit"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 Vert(uint id : SV_VertexID) : SV_POSITION
            { return GetFullScreenTriangleVertexPosition(id); }
            float4 Frag(float4 p : SV_POSITION) : SV_Target
            {
                uint index=(uint)p.x;
                uint row=index%4;
                if(index<4)return UNITY_MATRIX_V[row];
                if(index<8)return UNITY_MATRIX_P[row];
                if(index<12)return UNITY_MATRIX_VP[row];
                return UNITY_MATRIX_I_VP[row];
            }
            ENDHLSL
        }
    }
}
