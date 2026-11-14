// 인커 명령 글씨 — 피로 쓴 획(InkerBloodMesh). 사용자 요청 2026-11-15 "피로 쓰이는 느낌".
// 메시 채널: 정점색 = 피 색(sRGB), UV0 = (획 따라 거리, 획 가로 0~1 — 원판은 중심 0.5·테두리 0), UV1 = (젖음, 종류 0 획·1 흘러내림·2 방울).
// 조명을 받지 않는다(어두운 집에서도 글씨를 읽어야 판정이 성립한다) — 대신 색을 어둡게 잡아 빛나 보이지 않게 한다.
Shader "GhostHunter/InkerBlood"
{
    Properties
    {
        _Alpha ("Alpha (wipe fade)", Range(0, 1)) = 1
        _Smear ("Smear (wiped)", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        _Gloss ("Wet gloss", Range(0, 1)) = 0.55
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 posOS : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half _Alpha;
                half _Smear;
                float _Seed;
                half _Gloss;
            CBUFFER_END

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float Noise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = Hash(i);
                float b = Hash(i + float2(1, 0));
                float c = Hash(i + float2(0, 1));
                float d = Hash(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                half3 rgb = input.color.rgb;
                #ifndef UNITY_COLORSPACE_GAMMA
                rgb = pow(abs(rgb), 2.2);
                #endif
                output.color = half4(rgb, input.color.a);
                output.uv0 = input.uv0;
                output.uv1 = input.uv1;
                output.posOS = input.positionOS.xy;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float along = input.uv0.x;
                float edge = abs(input.uv0.y * 2.0 - 1.0); // 0 = 획 가운데, 1 = 가장자리
                float kind = input.uv1.y;
                float wet = input.uv1.x;

                // 손가락의 피가 줄어든 정도(획 끝으로 갈수록) — 획 가장자리만 해당(흘러내림·방울은 늘 젖어 있다).
                float stroke = 1.0 - step(0.5, kind);
                float dry = saturate(1.0 - wet) * stroke;

                // 가장자리를 손가락 자국처럼 들쭉날쭉하게 — 획을 따라 변하는 큰 물결 + 잔 물결. 마를수록 가늘어진다.
                float wave = Noise(float2(along * 9.0 + _Seed, input.uv0.y * 1.7)) * 0.6
                    + Noise(float2(along * 31.0 - _Seed, 4.1)) * 0.4;
                float threshold = 0.84 + (wave - 0.5) * 0.4 - _Smear * 0.12 - dry * 0.22;
                float soft = 0.06 + _Smear * 0.35;
                float alpha = 1.0 - smoothstep(threshold - soft, threshold, edge);

                // 마른 끝은 붓질이 갈라진 것처럼 획을 따라 줄무늬로 끊긴다.
                float bristle = Noise(float2(along * 3.0 + _Seed * 0.7, input.uv0.y * 11.0));
                alpha *= 1.0 - dry * smoothstep(0.38, 0.7, bristle);

                // 피가 얇게 묻은 자리 — 획 안쪽에 옅은 얼룩과 바늘구멍.
                float thin = Noise(input.posOS * 55.0 + _Seed * 1.37);
                alpha *= lerp(1.0, saturate(0.5 + thin * 0.9), 0.3 * stroke);

                half3 blood = input.color.rgb;
                // 가장자리만 살짝 말라 검붉다(테두리선처럼 보이지 않게 약하게).
                half3 dried = blood * 0.45;
                half3 color = lerp(blood, dried, smoothstep(0.6, 1.0, edge) * 0.5);
                color *= lerp(0.82, 1.12, thin);
                color = lerp(color, blood * 0.7, dry * 0.5);

                // 젖은 광택: 획 한쪽을 따라 끊기듯 이어지는 밝은 줄(조명 없이 흉내 낸다).
                float band = saturate(1.0 - abs(input.uv0.y - 0.34) * 5.5);
                band = band * band * smoothstep(0.35, 0.8, Noise(float2(along * 4.0 + _Seed, 7.3)));
                // 원판(획 끝 막음 0.3·흘러내림 끝 방울 1·튄 방울 2)은 UV 가 방사형이라 줄 대신 가운데만 은은히 반짝인다.
                if (kind > 0.1 && kind < 0.5)
                    band = saturate(1.0 - edge * 1.6) * 0.25;
                else if (kind > 1.5)
                    band = saturate(1.0 - edge * 2.2) * 0.6;
                half3 shine = blood * 2.4 + half3(0.05, 0.012, 0.01);
                color = lerp(color, shine, band * _Gloss * wet * (1.0 - _Smear));

                // 닦인 자국: 갈색으로 번지고 흐려진다.
                color = lerp(color, half3(0.16, 0.05, 0.035), _Smear * 0.65);

                alpha *= _Alpha * input.color.a;
                clip(alpha - 0.01);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
