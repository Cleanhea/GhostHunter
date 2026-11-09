Shader "GhostHunter/Grab Tool Indicator"
{
    Properties
    {
        _BaseColor ("Idle Color", Color) = (0.04, 0.06, 0.015, 1)
        [HDR] _EmissionColor ("Grab Emission", Color) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "GrabIndicator"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EmissionColor;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                // 검은 기본 emission 때문에 임포터가 발광 키워드를 제거해도 영향받지 않는다.
                return half4(_BaseColor.rgb + _EmissionColor.rgb, 1);
            }
            ENDHLSL
        }
    }
}
