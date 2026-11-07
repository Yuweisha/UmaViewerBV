Shader "Hidden/UmaViewer/LiveDepthAudit"
{
    SubShader { Cull Off ZWrite Off ZTest Always
        Pass { HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #include "UnityCG.cginc"
            UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            float4 Frag(v2f_img input):SV_Target
            {
                float raw=SAMPLE_DEPTH_TEXTURE(_CameraDepthTexture,input.uv);
                return float4(LinearEyeDepth(raw),Linear01Depth(raw),raw,1);
            }
        ENDHLSL }
    }
}
