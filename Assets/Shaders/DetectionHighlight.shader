Shader "GhostHunter/DetectionHighlight"
{
    // 탐지(Q) 형광 발광 — mole-skill-system.md §4.5. 대상 메시를 같은 지오메트리로 한 번 더 그려
    // 원래 머티리얼(텍스처·음영) 위에 빛을 **더한다**(가산 혼합). 원래 모습과 메시 모양은 그대로이고,
    // 윤곽(시선과 비스듬한 면)일수록 강하게 빛나 형광 물질처럼 보인다. 두 번째 패스는 메시를 법선 방향으로 살짝
    // 부풀린 뒷면만 그려 실루엣 둘레에 형광 테두리를 두른다(평평한 면만 있는 가구도 윤곽이 빛난다). 부풀리는 방향은
    // 법선이 아니라 메시 중심에서 바깥 — 모서리가 각진 메시는 면마다 법선이 갈라져 테두리가 끊긴다. 맥동은 셰이더 안에서 한다.
    Properties
    {
        _BaseColor ("Highlight Color", Color) = (0.976, 0.973, 0.443, 1)
        _FillStrength ("Fill Strength", Range(0, 2)) = 0.05
        _RimStrength ("Rim Strength", Range(0, 6)) = 1.2
        _RimPower ("Rim Power", Range(0.5, 8)) = 4
        _PulseSpeed ("Pulse Speed", Range(0, 10)) = 3.5
        _PulseDepth ("Pulse Depth", Range(0, 1)) = 0.25
        _OutlineWidth ("Outline Width (m)", Range(0, 0.1)) = 0.035
        _OutlineStrength ("Outline Strength", Range(0, 4)) = 2.4
        // 껍질을 부풀리는 중심(오브젝트 공간). DetectionTargetMarker 가 메시 경계 중심을 렌더러마다 넣는다.
        _ShellCenter ("Shell Center (OS)", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half _FillStrength;
            half _RimStrength;
            half _RimPower;
            half _PulseSpeed;
            half _PulseDepth;
            float _OutlineWidth;
            half _OutlineStrength;
            float4 _ShellCenter;
        CBUFFER_END

        half Pulse()
        {
            return 1.0h - _PulseDepth * (0.5h + 0.5h * sin(_Time.y * _PulseSpeed));
        }
        ENDHLSL

        Pass
        {
            Name "DetectionHighlight"
            // URP 는 같은 LightMode 의 패스를 하나만 그린다 — 두 패스의 태그를 나눠야 둘 다 그려진다.
            Tags { "LightMode" = "UniversalForward" }
            // 앞면만 — 뒷면까지 더하면 얇은 판이 두 배로 밝아진다.
            Cull Back
            ZWrite Off
            // 기획서 §4.5 — **벽 투시 없음**(사용자 확정 2026-09-05). 시전자 시야에서
            // 실제로 보이는 표면에만 그린다. ZTest Always 로 두면 벽 뒤 대상까지 보인다.
            ZTest LEqual
            // 하이라이트는 대상과 같은 지오메트리라 깊이가 동일하다. 부동소수 오차로 인한
            // z-fighting 을 피하려고 카메라 쪽으로 미세하게 당긴다(ZWrite 가 꺼져 있어
            // 깊이 버퍼에는 영향이 없다).
            Offset -1, -1
            // 가산 — 원래 표면 색을 덮지 않고 빛만 더한다.
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs position = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 view = normalize(GetWorldSpaceViewDir(input.positionWS));
                half facing = saturate(abs(dot(normal, view)));
                half rim = pow(1.0h - facing, _RimPower);
                half pulse = Pulse();
                half3 glow = _BaseColor.rgb * (_FillStrength + rim * _RimStrength) * pulse * _BaseColor.a;
                return half4(glow, 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DetectionOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            // 부풀린 껍질의 뒷면만 — 대상 앞에 있는 부분은 대상 깊이에 가려져 실루엣 둘레만 남는다.
            Cull Front
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 radialOS = input.positionOS.xyz - _ShellCenter.xyz;
                // 중심과 겹친 정점은 법선 방향으로 미는 것으로 대신한다.
                float3 directionOS = dot(radialOS, radialOS) > 1e-8 ? radialOS : input.normalOS;
                float3 directionWS = normalize(TransformObjectToWorldDir(directionOS));
                output.positionCS = TransformWorldToHClip(positionWS + directionWS * _OutlineWidth);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_BaseColor.rgb * _OutlineStrength * Pulse() * _BaseColor.a, 1.0h);
            }
            ENDHLSL
        }
    }
}
