using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 원격 플레이어 머리 위에 <see cref="PlayerNameTag"/> 의 닉네임을 띄운다.
    ///
    /// <para><b>보이는 조건</b> — 스폰됐고, 로컬 소유자가 아니고(1인칭이라 자기 몸도 숨긴다 —
    /// <see cref="PlayerVisuals"/>), 이름이 확정됐고, 땅속에 숨지 않았고(<see cref="MoleBurrowController"/> 가
    /// 몸을 숨길 때 같이 숨는다), 설정 거리 안일 때. 사망한 플레이어도 몸이 남아 있으므로 이름을 유지한다.</para>
    ///
    /// <para>World Space 캔버스라 벽·가구에 가려진다. 방향은 <b>플레이어가 보는 화면 카메라</b>와 같게 둔다
    /// (화면 정렬 빌보드) — 켜진 카메라 중 화면에 그리는(렌더 텍스처가 없는) 게임 카메라, 여럿이면 가장 위에
    /// 그리는(depth 최대) 것이다. 생존 중엔 PlayerCamera, 사망 후엔 SpectatorCamera 가 잡히므로 어느 쪽이든
    /// 정면으로 읽힌다. <c>Camera.main</c>(태그 검색)은 쓰지 않는다.</para>
    ///
    /// <para><b>카메라마다 따로 돌리지 않는다.</b> UGUI 캔버스는 카메라들이 그리기 전에 프레임당 한 번 배치를
    /// 굽고 그 회전을 모든 카메라가 같이 쓴다. 렌더 직전(beginCameraRendering)에 돌리면 직전 프레임에
    /// 마지막으로 그린 카메라 쪽을 보게 된다 — 에디터에서는 Game 뷰 다음에 그리는 Scene 뷰 카메라 쪽을 봐서
    /// Game 뷰에서 종이처럼 옆면이 보였다(2026-09-27 수정). 그래서 배치를 굽기 직전(willRenderCanvases)에
    /// 화면 카메라의 이번 프레임 회전으로 한 번 맞춘다. Scene 뷰에서는 이름표가 Game 카메라 쪽을 본다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerNameTagView : MonoBehaviour
    {
        private static Camera[] _cameras = new Camera[4];

        [SerializeField] private PlayerNameTagSettings _settings;
        [SerializeField] private PlayerNameTag _nameTag;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private MoleBurrowController _burrow;

        private RectTransform _label;
        private Canvas _canvas;
        private Text _text;

        private void Awake()
        {
            if (_settings == null || _nameTag == null || _motor == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerNameTagView)}: 설정·닉네임·이동 컴포넌트가 Player 프리팹에 배선되지 않았습니다.",
                    this);
                enabled = false;
                return;
            }

            BuildLabel();
        }

        private void OnEnable()
        {
            if (_canvas == null)
                return;

            _nameTag.DisplayNameChanged += HandleDisplayNameChanged;
            Canvas.willRenderCanvases += FaceViewer;
            HandleDisplayNameChanged(_nameTag.DisplayName);
        }

        private void LateUpdate()
        {
            bool visible = ShouldShow();
            if (_canvas.enabled != visible)
                _canvas.enabled = visible;

            if (visible)
                _label.localPosition = new Vector3(0f, _motor.CameraLocalHeight + _settings.HeightAboveEyes, 0f);
        }

        private void OnDisable()
        {
            if (_canvas == null)
                return;

            _nameTag.DisplayNameChanged -= HandleDisplayNameChanged;
            Canvas.willRenderCanvases -= FaceViewer;
            _canvas.enabled = false;
        }

        private bool ShouldShow()
        {
            if (!_nameTag.IsSpawned || _nameTag.IsOwner || _text.text.Length == 0)
                return false;

            if (_burrow != null && _burrow.IsBurrowed)
                return false;

            float maxDistance = _settings.MaxVisibleDistance;
            Camera viewer = FindScreenCamera();
            return maxDistance <= 0f || viewer == null ||
                   (transform.position - viewer.transform.position).sqrMagnitude <= maxDistance * maxDistance;
        }

        private void HandleDisplayNameChanged(string displayName) => _text.text = displayName ?? string.Empty;

        /// <summary>
        /// 캔버스 배치를 굽기 직전 — 이동·시점·관전 카메라가 이번 프레임 자리를 다 잡은 뒤다. 여기서 정한 방향을
        /// 이 프레임의 모든 카메라가 같이 쓰므로, 플레이어 화면 카메라에 맞춘다.
        /// </summary>
        private void FaceViewer()
        {
            if (!_canvas.enabled)
                return;

            Camera viewer = FindScreenCamera();
            if (viewer != null)
                _label.rotation = viewer.transform.rotation;
        }

        /// <summary>
        /// 플레이어가 보는 카메라 — 켜진 카메라 중 화면에 그리는 게임 카메라, 여럿이면 가장 위에 그리는 것.
        /// Scene 뷰·미리보기 카메라는 이 목록에 없고, 렌더 텍스처 카메라는 뺀다. 할당 없이 재사용 배열로 센다.
        /// </summary>
        private static Camera FindScreenCamera()
        {
            if (_cameras.Length < Camera.allCamerasCount)
                _cameras = new Camera[Camera.allCamerasCount];

            int count = Camera.GetAllCameras(_cameras);
            Camera best = null;
            for (int i = 0; i < count; i++)
            {
                Camera camera = _cameras[i];
                if (camera.cameraType != CameraType.Game || camera.targetTexture != null)
                    continue;

                if (best == null || camera.depth > best.depth)
                    best = camera;
            }

            System.Array.Clear(_cameras, 0, count);
            return best;
        }

        private void BuildLabel()
        {
            GameObject labelObject = new("NameTag", typeof(RectTransform));
            labelObject.layer = gameObject.layer;
            _label = (RectTransform)labelObject.transform;
            _label.SetParent(transform, false);

            // Canvas 는 기본값(Screen Space Overlay)이 RectTransform 크기·배율을 덮어쓰므로
            // World Space 로 바꾼 다음에 크기를 정한다.
            _canvas = labelObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.enabled = false;
            _label.localPosition = Vector3.zero;
            _label.localRotation = Quaternion.identity;
            _label.sizeDelta = new Vector2(_settings.FontSize * 12f, _settings.FontSize * 1.5f);
            // 글자는 Font Size(px)로 래스터화하고, 캔버스 배율로 월드 높이(Text Height, m)에 맞춘다.
            _label.localScale = Vector3.one * (_settings.TextHeight / _settings.FontSize);

            GameObject textObject = new("Text", typeof(RectTransform));
            textObject.layer = gameObject.layer;
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.SetParent(_label, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            _text = textObject.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _text.fontSize = _settings.FontSize;
            _text.alignment = TextAnchor.MiddleCenter;
            _text.color = _settings.TextColor;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            // Steam 이름에 <color> 같은 태그가 들어 있어도 서식이 아니라 글자로 보이게 한다.
            _text.supportRichText = false;
            _text.raycastTarget = false;

            Outline outline = textObject.AddComponent<Outline>();
            outline.effectColor = _settings.OutlineColor;
            outline.effectDistance = new Vector2(_settings.OutlineDistance, -_settings.OutlineDistance);
        }
    }
}
