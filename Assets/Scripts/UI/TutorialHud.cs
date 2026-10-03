using GhostHunter.Core;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 원룸 튜토리얼 안내. 단계가 바뀔 때 화면 가운데에 카드를 띄우고, 플레이어가 X 로 닫는다.
    /// 상시 패널은 두지 않는다 — 진행 수치는 청소 진행도 HUD 가 보여 준다 → docs/project/tutorial-stage.md "안내와 퀘스트".
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TutorialHud : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        private const float FadeSeconds = 0.25f;
        private const float PopScale = 0.94f;
        private const int IntroCard = -1;
        private const int NoCard = -2;

        private static readonly string[] Titles =
        {
            "가구 밀치기",
            "얼룩 청소",
            "가구 반출",
            "목표 완료",
        };

        private static readonly string[] Bodies =
        {
            "[Tab] 맨손을 고르고 가구를 조준한 뒤 좌클릭을 누르고 있다가 놓으세요.\n벽이나 바닥에 세게 부딪힌 가구는 내구도가 줄어듭니다.",
            "[Tab] 대걸레를 고르고 얼룩을 조준해 좌클릭으로 닦으세요.\n[Q] 탐지로 닦아야 할 얼룩과 반출할 가구를 찾을 수 있습니다.",
            "두 사람이 같은 가구를 좌클릭으로 붙잡으면 함께 옮깁니다.\n휠로 돌리고, 휠 클릭으로 기울이기를 바꿔 드릴카까지 옮기세요.",
            "드릴카 출구에서 [E]로 정산하세요.\n정산이 끝나면 인게임 로비에서 Stage1 으로 출발합니다.",
        };

        private static readonly string[] Keys = { "W A S D", "Shift", "Space", "E", "F", "Tab", "Q", "Esc" };
        private static readonly string[] Actions = { "이동", "달리기", "점프", "상호작용", "헤드램프", "도구 선택", "탐지", "메뉴" };

        [SerializeField] private FurnitureSpawnController _furniture;

        private ICleaningService _cleaning;
        private ILocalPlayerContext _localPlayer;
        private FurnitureLauncher[] _launchers;

        private Canvas _canvas;
        private CanvasGroup _group;
        private RectTransform _card;
        private Text _eyebrow;
        private Text _title;
        private Text _body;
        private GameObject _keyGrid;

        private float _refreshRemaining;
        private int _step = NoCard;
        private int _shownCard = NoCard;
        private bool _introDismissed;
        private int _dismissedStep = NoCard;
        private float _fade;
        private bool _visible;

        private void Awake()
        {
            Services.TryGet(out _cleaning);
            Services.TryGet(out _localPlayer);
            if (_furniture == null)
            {
                Debug.LogError("[TutorialHud] 가구 배선이 없습니다.", this);
                enabled = false;
                return;
            }

            _launchers = new FurnitureLauncher[_furniture.Items.Length];
            for (int i = 0; i < _launchers.Length; i++)
                _launchers[i] = _furniture.Items[i].GetComponent<FurnitureLauncher>();
            BuildCanvas();
        }

        private void Update()
        {
            RefreshStep();
            UpdateCard();
            Animate();
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        /// <summary>지금 떠 있는 카드를 닫는다. 기본 조작 카드를 닫으면 지금 단계 카드가 뜬다.</summary>
        public void Dismiss()
        {
            if (_shownCard == IntroCard)
                _introDismissed = true;
            else if (_shownCard != NoCard)
                _dismissedStep = _shownCard;
        }

        /// <summary>팀 진행으로 지금 단계를 정한다. 단계는 앞으로만 간다(밀치기 연습 → 청소 → 반출 → 완료).</summary>
        private void RefreshStep()
        {
            _refreshRemaining -= Time.unscaledDeltaTime;
            if (_refreshRemaining > 0f)
                return;

            _refreshRemaining = RefreshInterval;
            if (_cleaning == null || !_furniture.IsReady)
                return;

            bool practiced = false;
            foreach (FurnitureLauncher launcher in _launchers)
                if (launcher != null && launcher.HasLaunched)
                    practiced = true;

            CleaningTaskProgress progress = _cleaning.TaskProgress;
            _step = !practiced ? 0 : progress.CleanedStains < progress.TotalStains ? 1
                : progress.DeliveredFurniture < progress.TotalFurniture ? 2 : 3;
        }

        /// <summary>
        /// 처음엔 기본 조작 카드, 닫으면 지금 단계 카드. 카드가 떠 있는 동안 팀이 다음 단계로 넘어가면 새 단계로 바꿔 다시 띄운다.
        /// 메뉴가 열려 있으면 숨긴다 — 메뉴 위에 겹치지 않고, X 가 메뉴 조작과 섞이지 않게 한다.
        /// </summary>
        private void UpdateCard()
        {
            int wanted = NoCard;
            if (_step != NoCard)
            {
                if (!_introDismissed)
                    wanted = IntroCard;
                else if (_dismissedStep != _step)
                    wanted = _step;
            }

            bool menuOpen = _localPlayer?.Input != null && _localPlayer.Input.IsGameplayInputLocked;
            if (wanted != _shownCard && wanted != NoCard)
                ShowCard(wanted);
            _shownCard = wanted;
            _visible = wanted != NoCard && !menuOpen;

            if (_visible && Keyboard.current != null && Keyboard.current.xKey.wasPressedThisFrame)
                Dismiss();
        }

        private void ShowCard(int card)
        {
            bool intro = card == IntroCard;
            _eyebrow.text = intro ? "원룸 · 튜토리얼" : "원룸 · 튜토리얼  " + (card + 1) + " / " + Titles.Length;
            _title.text = intro ? "기본 조작" : Titles[card];
            _body.text = intro
                ? "2~4명이 함께 원룸을 정리합니다. 얼룩을 닦고 가구를 드릴카로 옮기세요."
                : Bodies[card];
            _keyGrid.SetActive(intro);
            // 새 카드는 처음부터 다시 떠오른다.
            _fade = 0f;
        }

        private void Animate()
        {
            float target = _visible ? 1f : 0f;
            _fade = Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime / FadeSeconds);
            float eased = 1f - (1f - _fade) * (1f - _fade);
            _group.alpha = eased;
            _card.localScale = Vector3.one * Mathf.Lerp(PopScale, 1f, eased);
            _canvas.enabled = _fade > 0f;
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("TutorialCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 145;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            _group = canvasObject.GetComponent<CanvasGroup>();
            _group.alpha = 0f;
            // 커서가 잠긴 채 플레이하므로 클릭을 받지 않는다. 닫기는 X 키다.
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 설정 창과 같은 색 — 남색 패널에 헤드램프 빛 강조색.
            Image edge = SettingsUiKit.CreateImage("TutorialCard", canvasObject.transform, SettingsUiKit.PanelEdge);
            _card = edge.rectTransform;
            _card.sizeDelta = new Vector2(760f, 0f);
            var fitter = edge.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var outer = edge.gameObject.AddComponent<VerticalLayoutGroup>();
            outer.padding = new RectOffset(2, 2, 2, 2);
            outer.childControlWidth = true;
            outer.childControlHeight = true;
            outer.childForceExpandHeight = false;

            Image panel = SettingsUiKit.CreateImage("Panel", _card, SettingsUiKit.Panel);
            var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(44, 44, 32, 28);
            layout.spacing = 12f;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _eyebrow = SettingsUiKit.CreateText("Eyebrow", panel.transform, string.Empty, 17, SettingsUiKit.Accent, TextAnchor.MiddleLeft);
            _title = SettingsUiKit.CreateText("Title", panel.transform, string.Empty, 38, SettingsUiKit.TextBright, TextAnchor.MiddleLeft);
            Image rule = SettingsUiKit.CreateImage("Rule", panel.transform, SettingsUiKit.AccentFaint);
            rule.gameObject.AddComponent<LayoutElement>().preferredHeight = 1f;
            _body = SettingsUiKit.CreateText("Body", panel.transform, string.Empty, 22, SettingsUiKit.TextBright, TextAnchor.UpperLeft);
            _body.lineSpacing = 1.15f;

            _keyGrid = BuildKeyGrid(panel.transform);

            Text hint = SettingsUiKit.CreateText("Hint", panel.transform, "[X] 닫기", 17, SettingsUiKit.TextDim, TextAnchor.MiddleRight);
            hint.gameObject.AddComponent<LayoutElement>().preferredHeight = 30f;
        }

        private static GameObject BuildKeyGrid(Transform parent)
        {
            RectTransform grid = SettingsUiKit.CreateRect("Keys", parent);
            var layout = grid.gameObject.AddComponent<GridLayoutGroup>();
            layout.cellSize = new Vector2(158f, 62f);
            layout.spacing = new Vector2(10f, 8f);
            layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            layout.constraintCount = 4;

            for (int i = 0; i < Keys.Length; i++)
            {
                RectTransform cell = SettingsUiKit.CreateRect("Key " + Actions[i], grid);
                Image cap = SettingsUiKit.CreateImage("Cap", cell, SettingsUiKit.ButtonNormal);
                SettingsUiKit.Stretch(cap.rectTransform, new Vector2(0f, 0.5f), Vector2.one);
                Text key = SettingsUiKit.CreateText("Label", cap.transform, Keys[i], 18, SettingsUiKit.TextBright, TextAnchor.MiddleCenter);
                SettingsUiKit.Fill(key.rectTransform);
                Text action = SettingsUiKit.CreateText("Action", cell, Actions[i], 16, SettingsUiKit.TextDim, TextAnchor.MiddleCenter);
                SettingsUiKit.Stretch(action.rectTransform, Vector2.zero, new Vector2(1f, 0.5f));
            }

            return grid.gameObject;
        }
    }
}
