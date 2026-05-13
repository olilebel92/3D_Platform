Shader "HackNSLASH/TargetOutline"
{
    Properties
    {
        _OutlineColor     ("Outline Color",     Color) = (1, 0, 0, 1)
        _OutlineThickness ("Outline Thickness", Float) = 0.03
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry+1"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "TargetOutline"
            Cull  Front
            ZWrite On
            ZTest  LEqual

            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float  _OutlineThickness;
                float4 _OutlineColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS   : POSITION;
                // Averaged (smooth) normals baked into UV2 by TargetSelector at runtime.
                // NORMAL is NOT used for extrusion so hard-edge meshes have no corner gaps.
                float3 smoothNormal : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS      = TransformObjectToWorld(IN.positionOS.xyz);
                float3 normalWS   = TransformObjectToWorldNormal(IN.smoothNormal);
                float3 extrudedWS = posWS + normalWS * _OutlineThickness;

                // Prevent the outline going underground: never push a vertex
                // below its original world-space height.
                extrudedWS.y = max(posWS.y, extrudedWS.y);

                OUT.positionCS = TransformWorldToHClip(extrudedWS);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                return _OutlineColor;
            }
            ENDHLSL
        }
    }
}
