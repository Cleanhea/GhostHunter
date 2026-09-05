Shader "GhostHunter/DetectionHighlight"
{
    Properties
    {
        _BaseColor ("Highlight Color", Color) = (0.976, 0.973, 0.443, 0.95)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Name "DetectionHighlight"
            Cull Off
            ZWrite Off
            // 기획서 §4.5 — **벽 투시 없음**(사용자 확정 2026-09-05 변경). 시전자 시야에서
            // 실제로 보이는 표면에만 그린다. ZTest Always 로 두면 벽 뒤 대상까지 보인다.
            ZTest LEqual
            // 하이라이트는 대상과 같은 지오메트리라 깊이가 동일하다. 부동소수 오차로 인한
            // z-fighting 을 피하려고 카메라 쪽으로 미세하게 당긴다(ZWrite 가 꺼져 있어
            // 깊이 버퍼에는 영향이 없다).
            Offset -1, -1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }
}
