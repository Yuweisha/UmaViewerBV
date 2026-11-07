Shader "Hidden/LiveRendererMatrixAudit"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZTest Always ZWrite Off Cull Off Blend Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float4 vert(uint vertexID : SV_VertexID) : SV_POSITION
            {
                float2 uv = float2((vertexID << 1) & 2, vertexID & 2);
                return float4(uv * 2 - 1, 0, 1);
            }
            float4 frag(float4 position : SV_POSITION) : SV_Target
            {
                uint row = min((uint)position.x, 15u);
                if (row < 4u) return UNITY_MATRIX_VP[row];
                if (row < 8u) return UNITY_MATRIX_V[row - 4u];
                if (row < 12u) return UNITY_MATRIX_P[row - 8u];
                return unity_ObjectToWorld[row - 12u];
            }
            ENDHLSL
        }
    }
}
