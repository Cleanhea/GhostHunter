Shader "GhostHunter/CleaningStain"
{
    Properties
    {
        _BaseColor ("Stain Color", Color) = (0.22, 0.075, 0.025, 0.9)
        _Wipe ("Wipe", Range(0, 1)) = 0
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
                float shape = 1 - smoothstep(edge - 0.08, edge, length(p));
                float grain = frac(sin(dot(floor(input.uv * 85), float2(12.9898, 78.233))) * 43758.5453);
                float wipe = 1 - smoothstep(input.uv.x - 0.04, input.uv.x + 0.04, _Wipe * 1.1);
                half alpha = shape * wipe * _BaseColor.a * (0.65 + 0.35 * grain);
                clip(alpha - 0.01);
                return half4(_BaseColor.rgb, alpha);
            }
            ENDHLSL
        }
    }
}
