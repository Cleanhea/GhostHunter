using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace GhostHunter.UI
{
    /// <summary>
    /// 도둑질 후 정신력 회복 테두리(theft-system.md §4.3)를 그리는 URP 포스트프로세싱 패스. <c>PC_Renderer</c>·<c>Mobile_Renderer</c>
    /// 에 등록돼 있다. 세기는 전역 셰이더 값 <see cref="IntensityId"/> 로 받고, 0 이면 패스를 넣지 않는다 — 평소에는 비용이 없다.
    /// 기본 Vignette 는 곱셈이라 밝게 못 해서(<c>Common.hlsl ApplyVignette</c>) 전용 셰이더로 섞는다.
    /// </summary>
    public sealed class TheftRecoveryGlowFeature : ScriptableRendererFeature
    {
        public static readonly int IntensityId = Shader.PropertyToID("_GH_TheftGlowIntensity");
        public static readonly int ColorId = Shader.PropertyToID("_GH_TheftGlowColor");
        public static readonly int EdgeId = Shader.PropertyToID("_GH_TheftGlowEdge");

        [SerializeField] private Shader _shader;

        private Material _material;
        private GlowPass _pass;

        public override void Create()
        {
            if (_shader == null)
                _shader = Shader.Find("Hidden/GhostHunter/TheftRecoveryGlow");
            if (_shader != null && _material == null)
                _material = CoreUtils.CreateEngineMaterial(_shader);
            _pass = new GlowPass(_material) { renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_material == null || Shader.GetGlobalFloat(IntensityId) <= 0.001f)
                return;
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(_material);
            _material = null;
        }

        private sealed class GlowPass : ScriptableRenderPass
        {
            private readonly Material _material;

            public GlowPass(Material material)
            {
                _material = material;
                // 화면 백버퍼에 바로 그리는 경우에도 원본을 읽을 수 있게 중간 텍스처를 요구한다.
                requiresIntermediateTexture = true;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData camera = frameData.Get<UniversalCameraData>();
                UniversalResourceData resources = frameData.Get<UniversalResourceData>();
                // 플레이 화면(게임 카메라)에만 — Scene 뷰·프리뷰에는 그리지 않는다.
                if (_material == null || camera.cameraType != CameraType.Game || resources.isActiveTargetBackBuffer)
                    return;

                TextureHandle source = resources.activeColorTexture;
                TextureDesc desc = renderGraph.GetTextureDesc(source);
                desc.name = "_TheftRecoveryGlow";
                desc.clearBuffer = false;
                TextureHandle destination = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(new RenderGraphUtils.BlitMaterialParameters(source, destination, _material, 0),
                    "Theft Recovery Glow");
                resources.cameraColor = destination;
            }
        }
    }
}
