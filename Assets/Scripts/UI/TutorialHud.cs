using GhostHunter.Core;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>원룸 튜토리얼의 조작키와 팀 청소·반출 목표를 순서대로 안내한다.</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialHud : MonoBehaviour
    {
        private const float RefreshInterval = 0.2f;
        [SerializeField] private FurnitureSpawnController _furniture;
        private ICleaningService _cleaning;
        private ILocalPlayerContext _localPlayer;
        private FurnitureLauncher[] _launchers;
        private FurnitureNetworkPhysics[] _physics;
        private Canvas _canvas;
        private Text _lesson;
        private Text _tasks;
        private float _refreshRemaining;
        private int _shownStep = -1;
        private int _shownCleaned = -1;
        private int _shownDelivered = -1;
        private int _shownDurability = -1;

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
            _physics = new FurnitureNetworkPhysics[_furniture.Items.Length];
            for (int i = 0; i < _launchers.Length; i++)
            {
                _launchers[i] = _furniture.Items[i].GetComponent<FurnitureLauncher>();
                _physics[i] = _furniture.Items[i].GetComponent<FurnitureNetworkPhysics>();
            }
            BuildCanvas();
        }

        private void Update()
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
            int step = !practiced ? 0 : progress.CleanedStains < progress.TotalStains ? 1
                : progress.DeliveredFurniture < progress.TotalFurniture ? 2 : 3;
            int durability = -1;
            if (_localPlayer != null && _localPlayer.Targeter != null
                && _localPlayer.Targeter.CurrentTarget != null)
            {
                foreach (FurnitureNetworkPhysics physics in _physics)
                    if (physics != null && physics.gameObject == _localPlayer.Targeter.CurrentTarget.gameObject)
                        durability = physics.Durability;
            }
            if (_shownStep == step && _shownCleaned == progress.CleanedStains
                && _shownDelivered == progress.DeliveredFurniture && _shownDurability == durability)
                return;
            _shownStep = step;
            _shownCleaned = progress.CleanedStains;
            _shownDelivered = progress.DeliveredFurniture;
            _shownDurability = durability;
            _lesson.text = step switch
            {
                0 => "1. 루시우 · 가구 밀치기\n[Tab] 맨손 선택 → 가구 조준 → 좌클릭 홀드 후 놓기\n벽·바닥에 세게 부딪히면 내구도가 줄어듭니다.",
                1 => "2. 얼룩 청소\n[Tab] 대걸레 선택 → 얼룩 조준 → 좌클릭\n[Q] 탐지로 청소할 얼룩과 반출 가구를 확인하세요.",
                2 => "3. 가구 반출\n두 사람이 같은 가구를 좌클릭 홀드하면 운반합니다.\n휠: 회전 · 휠 클릭: 기울이기 전환 → 드릴카로 옮기기",
                _ => "원룸 목표 완료\n드릴카 출구에서 [E]로 정산하세요.\n정산 후 인게임 로비에서 Stage1으로 출발합니다.",
            };
            _tasks.text = $"팀 목표  {(practiced ? "✓" : "□")} 밀치기 연습\n"
                + $"얼룩 {progress.CleanedStains}/{progress.TotalStains}  ·  반출 {progress.DeliveredFurniture}/{progress.TotalFurniture}"
                + (durability >= 0 ? $"\n조준한 가구 내구도 {durability}/100" : "\n내구도: 강한 충돌로 감소");
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new("TutorialCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 145;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            RectTransform panel = Rect("TutorialPanel", _canvas.transform, new Vector2(1f, 1f),
                new Vector2(-24f, -24f), new Vector2(520f, 392f), new Vector2(1f, 1f));
            Image image = panel.gameObject.AddComponent<Image>();
            image.color = new Color(0.025f, 0.04f, 0.07f, 0.85f);
            image.raycastTarget = false;
            Label(panel, "원룸 · Tutorial", new Vector2(20f, -14f), new Vector2(480f, 30f), 23);
            Label(panel, "2~4명 · 쉬움 · 목표 5~8분", new Vector2(20f, -48f), new Vector2(480f, 25f), 17);
            _lesson = Label(panel, "원룸 준비 중…", new Vector2(20f, -86f), new Vector2(480f, 110f), 19);
            _tasks = Label(panel, "", new Vector2(20f, -203f), new Vector2(480f, 76f), 18);
            string[] keys = { "W A S D", "Shift", "Space", "E", "F", "Tab", "Q", "Esc" };
            string[] actions = { "이동", "달리기", "점프", "상호작용", "헤드램프", "도구 선택", "탐지", "메뉴" };
            for (int i = 0; i < keys.Length; i++)
            {
                float x = 20f + (i % 4) * 122f;
                float y = -290f - (i / 4) * 46f;
                RectTransform key = Rect("Key" + i, panel, new Vector2(0f, 1f), new Vector2(x, y),
                    new Vector2(110f, 23f), new Vector2(0f, 1f));
                Image background = key.gameObject.AddComponent<Image>();
                background.color = new Color(0.17f, 0.24f, 0.34f, 1f);
                background.raycastTarget = false;
                Text text = Label(key, keys[i], Vector2.zero, new Vector2(110f, 23f), 15);
                text.alignment = TextAnchor.MiddleCenter;
                Label(panel, actions[i], new Vector2(x, y - 23f), new Vector2(110f, 20f), 14);
            }
        }

        private static Text Label(Transform parent, string content, Vector2 position, Vector2 size, int fontSize)
        {
            RectTransform rect = Rect("Text", parent, new Vector2(0f, 1f), position, size, new Vector2(0f, 1f));
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.text = content;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform Rect(string name, Transform parent, Vector2 anchor, Vector2 position,
            Vector2 size, Vector2 pivot)
        {
            GameObject child = new(name, typeof(RectTransform));
            child.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)child.transform;
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
