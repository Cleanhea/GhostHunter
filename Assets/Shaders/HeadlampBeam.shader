Shader "GhostHunter/HeadlampBeam"
{
    // 헤드라이트 빛줄기(가짜 볼류메트릭) — 공기 중 먼지에 산란돼 보이는 원뿔을 가산 반투명으로 그린다.
    // 메시는 꼭짓점이 원점, +Z 방향 길이 1 인 원뿔이다(PlayerHeadlamp.BuildBeamMesh). 길이·색은 렌더러별
    // MaterialPropertyBlock 으로 넣는다 — 스케일 1 규칙 때문에 트랜스폼 대신 정점을 _BeamLength 로 늘린다.
    // 구조: docs/architecture/headlamp.md §4.1
    Properties
    {
        [HDR] _BeamColor ("Beam Color (rgb × 세기, 코드가 덮어씀)", Color) = (0.12, 0.114, 0.103, 1)
        _BeamLength ("Beam Length (m, 코드가 덮어씀)", Float) = 6
        _EdgePower ("Edge Softness (윤곽 흐림 지수)", Range(0.5, 8)) = 2.5
        _StartFadeDistance ("Start Fade (m, 꼭짓점에서 차오르는 거리)", Range(0.01, 3)) = 0.6
        _FalloffPower ("Length Falloff (길이 방향 감쇠 지수)", Range(0.5, 6)) = 2
        _CameraFadeDistance ("Camera Fade (m, 카메라 바로 앞 흐림)", Range(0.01, 3)) = 0.4
        _NearApexScale ("Near Lamp Scale (자기 헤드라이트 시점 세기 배율)", Range(0, 1)) = 0.35
        _NearApexDistance ("Near Lamp Distance (m, 이 거리 안이면 위 배율)", Range(0.1, 5)) = 1.5
        [Toggle(_HEADLAMP_SOFT_INTERSECTION)] _SoftIntersection ("Soft Intersection (깊이 텍스처 필요)", Float) = 1
        _SoftDistance ("Soft Distance (m, 벽·바닥과 만나는 곳 흐림)", Range(0.01, 2)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "HeadlampBeam"
            // 원뿔 안(자기 시점)에서도 보이도록 양면을 그린다. 밖에서 보면 앞·뒷면이 겹쳐 가운데가 조금 더 짙다.
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma shader_feature_local _HEADLAMP_SOFT_INTERSECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float4 screenPos : TEXCOORD2;
                float alongBeam : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _BeamColor;
                float _BeamLength;
                float _EdgePower;
                float _StartFadeDistance;
                float _FalloffPower;
                float _CameraFadeDistance;
                float _NearApexScale;
                float _NearApexDistance;
                float _SoftDistance;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                float3 positionOS = input.positionOS.xyz * _BeamLength;
                VertexPositionInputs position = GetVertexPositionInputs(positionOS);
                output.positionCS = position.positionCS;
                output.positionWS = position.positionWS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.screenPos = ComputeScreenPos(position.positionCS);
                output.alongBeam = input.uv.y;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float3 toCamera = _WorldSpaceCameraPos - input.positionWS;
                float3 viewDir = normalize(toCamera);

                // 윤곽(표면을 비스듬히 보는 곳)일수록 흐리게 — 원뿔 테두리가 선으로 보이지 않게 한다.
                float facing = pow(saturate(abs(dot(normalize(input.normalWS), viewDir))), _EdgePower);

                // 길이 방향: 꼭짓점에서 서서히 차오르고 끝으로 갈수록 사라진다.
                float t = saturate(input.alongBeam);
                float axial = saturate(t * _BeamLength / _StartFadeDistance) * pow(1.0 - t, _FalloffPower);

                // 카메라 바로 앞 면은 흐리게 — 원뿔을 통과해 걸을 때 면이 튀지 않게 한다.
                float eyeDepth = input.screenPos.w;
                float cameraFade = saturate((eyeDepth - _ProjectionParams.y) / _CameraFadeDistance);

                // 자기 헤드라이트(또는 관전 중인 사람의 1인칭)처럼 램프 바로 뒤에서 보면 약하게.
                float3 apexWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                float apexDistance = distance(_WorldSpaceCameraPos, apexWS);
                float nearScale = lerp(_NearApexScale, 1.0, saturate(apexDistance / _NearApexDistance));

                float soft = 1.0;
            #if defined(_HEADLAMP_SOFT_INTERSECTION)
                // 벽·바닥과 만나는 경계를 부드럽게 — URP 에셋의 Depth Texture 가 켜져 있어야 한다(PC_RPAsset 켜짐).
                float2 screenUV = input.screenPos.xy / input.screenPos.w;
                float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                soft = saturate((sceneDepth - eyeDepth) / _SoftDistance);
            #endif

                float strength = facing * axial * cameraFade * nearScale * soft;
                return half4(_BeamColor.rgb * strength, 0.0);
            }
            ENDHLSL
        }
    }
}
