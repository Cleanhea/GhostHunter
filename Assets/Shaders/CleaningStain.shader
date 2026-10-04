Shader "GhostHunter/CleaningStain"
{
    Properties
    {
        _BaseColor ("Stain Color", Color) = (0.22, 0.075, 0.025, 0.9)
        _Wipe ("Wipe", Range(0, 1)) = 0
        // 탐지(Q) 형광 발광 — 알파 0 이면 꺼짐. DetectionTargetMarker 가 MaterialPropertyBlock 으로 켠다(mole-skill-system.md §4.5).
        _HighlightColor ("Detection Highlight", Color) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _Wipe;
                half4 _HighlightColor;
            CBUFFER_END
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                float2 p = (input.uv - 0.5) * 2;
                float angle = atan2(p.y, p.x);
                float edge = 0.76 + 0.10 * sin(angle * 7) + 0.07 * cos(angle * 11);
                float dist = length(p);
                float shape = 1 - smoothstep(edge - 0.08, edge, dist);
                float grain = frac(sin(dot(floor(input.uv * 85), float2(12.9898, 78.233))) * 43758.5453);
                float wipe = 1 - smoothstep(input.uv.x - 0.04, input.uv.x + 0.04, _Wipe * 1.1);
                half alpha = shape * wipe * _BaseColor.a * (0.65 + 0.35 * grain);
                half3 color = _BaseColor.rgb;

                // 탐지 형광: 얼룩 모양은 그대로, 안쪽은 형광색으로 물들고 가장자리는 더 밝게, 바깥으로 은은한 번짐.
                if (_HighlightColor.a > 0.001)
                {
                    half pulse = 0.8 + 0.2 * sin(_Time.y * 3.5);
                    half rim = smoothstep(edge - 0.3, edge - 0.04, dist) * shape;
                    // 사각 메시 가장자리에서 잘린 직선이 보이지 않도록 경계 근처에서 번짐을 0으로 줄인다.
                    half border = 1 - smoothstep(0.88, 0.99, max(abs(p.x), abs(p.y)));
                    half halo = (1 - smoothstep(edge, edge + 0.18, dist)) * (1 - shape) * wipe * border;
                    half3 glow = _HighlightColor.rgb * pulse;
                    color = lerp(color, glow, 0.75 * _HighlightColor.a) + glow * rim * 0.8 * _HighlightColor.a;
                    half inner = shape * wipe * max(_BaseColor.a, 0.85);
                    half outer = halo * 0.55 * pulse * _HighlightColor.a;
                    alpha = max(inner, outer);
                    color = lerp(glow, color, saturate(inner / max(alpha, 0.001)));
                }

                clip(alpha - 0.01);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
