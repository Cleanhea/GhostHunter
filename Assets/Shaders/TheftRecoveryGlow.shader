// 도둑질 후 정신력 회복 테두리(theft-system.md §4.3) — 화면 바깥쪽을 #c2fcf3 둥근 외곽 비네팅 빛으로 2초간 밝힌다.
// URP 기본 Vignette 는 화면색에 곱하기만 해서 밝게 못 하므로 전용 풀스크린 패스(TheftRecoveryGlowFeature)로 그린다.
// 세기·색·폭은 전역 셰이더 값으로 받는다 — 로컬 플레이어의 TheftHud 가 넣는다(0 이면 패스 자체를 건너뛴다).
Shader "Hidden/GhostHunter/TheftRecoveryGlow"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        ZWrite Off
        ZTest Always
        Cull Off
        Blend Off

        Pass
        {
            Name "TheftRecoveryGlow"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _GH_TheftGlowIntensity;
            float4 _GH_TheftGlowColor;
            float _GH_TheftGlowEdge;

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // 둥근 외곽 비네팅(레퍼런스 이미지 1) — 화면에 맞춘 타원 거리 r: 가운데 0, 변 가운데 1, 모서리 1.414.
                // 빛은 화면 네 모서리 끝에서만 가장 밝고 안쪽으로 곡선을 그리며 빠르게 옅어진다 — 꽉 차는 영역이 없어
                // 테두리 선이 생기지 않고, 눈에 띄는 빛은 모서리 쪽 둥근 영역에만 남는다(변 가운데는 은은하다).
                const float cornerRadius = 1.4142;
                float r = length((uv - 0.5) * 2.0);
                // 차오름: 빛 폭(_GH_TheftGlowEdge)이 넓어질수록 빛이 모서리에서 안쪽으로 번진다.
                float width = max(_GH_TheftGlowEdge, 0.0001);
                float t = saturate((r - (cornerRadius - width)) / width);
                float glow = pow(t, 2.2);
                float a = glow * _GH_TheftGlowIntensity;

                // 어두운 화면에서 8비트 밴딩(등고선)이 보이지 않게 아주 작은 디더를 더한다.
                float dither = frac(52.9829189 * frac(dot(input.positionCS.xy, float2(0.06711056, 0.00583715)))) - 0.5;
                a = saturate(a + dither * (1.0 / 255.0) * step(0.0001, a));

                // 스크린 블렌드 — 어두운 곳은 빛이 비치고, 밝은 곳은 하얗게 날아가지 않는다.
                color.rgb = color.rgb + _GH_TheftGlowColor.rgb * a * (1.0 - color.rgb);
                return color;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
