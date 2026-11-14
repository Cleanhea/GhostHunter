using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 명령 글씨 하나를 그리는 피어 전용 연출(project/ghost-inker.md §3). 복제된 <see cref="InkerWritingState"/> 를 받아
    /// 벽·문 표면에 **피로** 글씨를 쓴다(사용자 요청 2026-11-15) — 손가락이 획을 따라 지나가며 그어지고, 쓴 자리에서 피가
    /// 흘러내리고, 닦일수록 갈색으로 번지며 흐려진다(<see cref="InkerBloodMesh"/>·셰이더 GhostHunter/InkerBlood).
    /// 판정(닦기·위반)은 하지 않는다 — 서버가 상태로 정한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InkerWritingView : MonoBehaviour
    {
        public const string ShaderName = "GhostHunter/InkerBlood";

        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");
        private static readonly int SmearId = Shader.PropertyToID("_Smear");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");

        private readonly List<Vector3> _vertices = new(2048);
        private readonly List<Color32> _colors = new(2048);
        private readonly List<Vector2> _uv0 = new(2048);
        private readonly List<Vector2> _uv1 = new(2048);
        private readonly List<int> _triangles = new(4096);

        private InkerWritingState _state;
        private InkerBloodMesh _blood;
        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;
        private bool _settled;
        private float _appliedWipe = -1f;
        private float _progress;

        public int Id => _state.Id;
        public InkerWritingState State => _state;

        /// <summary>그린 정점 수(테스트·디버그).</summary>
        public int VertexCount => _vertices.Count;

        /// <summary>지금 쓰고 있는 손끝의 월드 위치(인형 손 따라가기·테스트). 쓰기 전·글씨가 없으면 false.</summary>
        public bool TryGetTipWorld(out Vector3 tip)
        {
            tip = default;
            if (_blood == null || !_blood.TryGetTip(_progress, out Vector2 layoutTip))
                return false;
            float h = LetterHeightOf(_state);
            tip = transform.TransformPoint(new Vector3(-layoutTip.x * h, layoutTip.y * h, 0f));
            return true;
        }

        public static InkerWritingView Create(in InkerWritingState state, Material material = null)
        {
            var root = new GameObject($"InkerWriting_{state.Id}");
            InkerWritingView view = root.AddComponent<InkerWritingView>();
            view.Build(material);
            view.Apply(state);
            return view;
        }

        public void Apply(in InkerWritingState state)
        {
            bool shapeChanged = _blood == null || state.Command != _state.Command || state.Id != _state.Id
                || state.LetterHeight != _state.LetterHeight || state.WrapWidth != _state.WrapWidth
                || state.Height != _state.Height;
            bool completedNow = state.Complete && !_state.Complete;
            _state = state;
            if (shapeChanged)
            {
                // 같은 글씨면 모든 피어에서 같은 모양이 나오도록 글씨 Id 로 씨앗을 정한다.
                _blood = new InkerBloodMesh(state.Text, state.WrapWidth, Seed(state.Id));
                _settled = false;
                if (_block != null)
                {
                    _block.SetFloat(SeedId, (state.Id * 7.31f) % 97f);
                    _renderer.SetPropertyBlock(_block);
                }
            }

            if (completedNow)
                _settled = false;
        }

        /// <summary>앵커(문)를 따라 위치를 맞추고 쓰기·흘러내림·닦기 진행을 반영한다.</summary>
        public void Refresh(Transform anchor, double serverTime)
        {
            if (!InkerWritingGeometry.TryGetWorldPose(_state, anchor, out Vector3 position, out Quaternion rotation))
            {
                SetVisible(false);
                return;
            }

            transform.SetPositionAndRotation(position, rotation);

            float elapsed = Mathf.Max(0f, (float)(serverTime - _state.RevealStartTime));
            float revealSeconds = Mathf.Max(0.01f, _state.RevealSeconds);
            _progress = _state.Complete ? 1f : Mathf.Clamp01(elapsed / revealSeconds);

            if (!_settled && _blood != null)
            {
                _blood.Build(_progress, elapsed, revealSeconds, LetterHeightOf(_state),
                    _vertices, _colors, _uv0, _uv1, _triangles);
                _mesh.Clear();
                _mesh.SetVertices(_vertices);
                _mesh.SetColors(_colors);
                _mesh.SetUVs(0, _uv0);
                _mesh.SetUVs(1, _uv1);
                _mesh.SetTriangles(_triangles, 0, false);
                // 흘러내림이 멈추면 메시는 더 바뀌지 않는다. 바운즈는 흘러내림까지 넉넉히 잡는다.
                float h = LetterHeightOf(_state);
                _mesh.bounds = new Bounds(Vector3.down * (h * 0.6f),
                    new Vector3(_state.Width + h, _state.Height + h * 2.6f, 0.05f));
                _settled = _blood.IsSettled(_progress, elapsed, revealSeconds);
            }

            float wipe = _state.WipeRequired > 0 ? 1f - _state.WipeRemaining / (float)_state.WipeRequired : 0f;
            if (!Mathf.Approximately(wipe, _appliedWipe))
            {
                _appliedWipe = wipe;
                _block.SetFloat(AlphaId, Mathf.Lerp(1f, 0.22f, wipe));
                _block.SetFloat(SmearId, wipe);
                _renderer.SetPropertyBlock(_block);
            }

            SetVisible(_vertices.Count > 0);
        }

        private static float LetterHeightOf(in InkerWritingState state)
            => state.LetterHeight > 0f ? state.LetterHeight : Mathf.Max(0.05f, state.Height);

        private static int Seed(int id) => unchecked(id * 92821 + 17);

        private void Build(Material material)
        {
            _mesh = new Mesh { name = "InkerBloodWriting" };
            _mesh.indexFormat = IndexFormat.UInt32;
            _mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            _renderer = gameObject.AddComponent<MeshRenderer>();
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.sharedMaterial = material != null ? material : FallbackMaterial();
            _block = new MaterialPropertyBlock();
            _block.SetFloat(AlphaId, 1f);
            _block.SetFloat(SmearId, 0f);
            _renderer.SetPropertyBlock(_block);
        }

        private static Material s_fallback;

        /// <summary>설정 에셋에 머티리얼이 없을 때(테스트·직접 만든 귀신). 피 셰이더 → 정점색 셰이더 순으로 찾는다.</summary>
        private static Material FallbackMaterial()
        {
            if (s_fallback != null)
                return s_fallback;
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
                shader = Shader.Find("Sprites/Default");
            s_fallback = shader != null ? new Material(shader) { name = "InkerBlood (runtime)" } : null;
            return s_fallback;
        }

        private void OnDestroy()
        {
            if (_mesh == null)
                return;
            if (Application.isPlaying)
                Destroy(_mesh);
            else
                DestroyImmediate(_mesh);
        }

        private void SetVisible(bool visible)
        {
            if (_renderer != null && _renderer.enabled != visible)
                _renderer.enabled = visible;
        }
    }
}
