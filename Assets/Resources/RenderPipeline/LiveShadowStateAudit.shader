Shader "Hidden/UmaViewer/LiveShadowStateAudit"
{
    SubShader { Tags {"RenderPipeline"="UniversalPipeline"} Cull Off ZWrite Off ZTest Always
        Pass { HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/CommonMaterial.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float4 Vert(uint id:SV_VertexID):SV_POSITION {return GetFullScreenTriangleVertexPosition(id);}
            float4 Frag(float4 p:SV_POSITION):SV_Target {
                uint x=(uint)p.x;
                if(x<4)return _MainLightWorldToShadow[0][x];
                if(x==4)return _MainLightShadowParams;if(x==5)return _MainLightShadowmapSize;
                if(x==6)return _ShadowBias;
                if(x==7)return _MainLightShadowOffset0;
                if(x==8)return _MainLightShadowOffset1;
                return float4(101,202,303,404);
            }
        ENDHLSL }
    }
}
