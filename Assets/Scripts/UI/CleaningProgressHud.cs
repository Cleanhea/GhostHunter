using GhostHunter.Core;
using GhostHunter.Gameplay.Cleaning;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 화면 좌측 상단에 스테이지 청소 진행도를 두 줄로 띄운다 — 대걸레로 닦은 얼룩, 드릴카로 반출한 목표 가구.
    /// 두 작업을 합친 전체 진행도는 미정(D-14)이라 합산하지 않는다. 값은 <see cref="ICleaningService.TaskProgress"/>
    /// (복제 상태)에서 읽으므로 Host·Client 모두 같은 수치를 본다. 청소 서비스가 없는 씬에서는 숨는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CleaningProgressHud : MonoBehaviour
    {
        private const int CanvasSortingOrder = 150;
        private const float RefreshInterval = 0.2f;
        private static readonly Vector2 ReferenceResolution = new(1920f, 1080f);
        private const float Margin = 24f;
        private const float PanelWidth = 320f;
        private const float Padding = 14f;
        private const float TitleHeight = 28f;
        private const float RowHeight = 26f;
        private const float BarHeight = 8f;
        private const float RowSpacing = 10f;

        private static readonly Color PanelColor = new(0f, 0f, 0f, 0.55f);
        private static readonly Color TextColor = new(0.92f, 0.92f, 0.92f, 1f);
        private static readonly Color BarBackColor = new(1f, 1f, 1f, 0.15f);
        private static readonly Color BarColor = new(0.35f, 0.75f, 1f, 1f);
        private static readonly Color DoneColor = new(0.45f, 0.95f, 0.45f, 1f);

        private ICleaningService _cleaning;
        private Canvas _canvas;
        private GameObject _panel;
        private Row _stainRow;
        private Row _furnitureRow;
        private float _refreshRemaining;

        private sealed class Row
        {
            public Text Value;
            public RectTransform Fill;
            public Image FillImage;
            public int ShownDone = -1;
            public int ShownTotal = -1;
        }

        private void OnDestroy()
        {
            if (_canvas != null)
                Destroy(_canvas.gameObject);
        }

        private void Update()
        {
            _refreshRemaining -= Time.unscaledDeltaTime;
            if (_refreshRemaining > 0f)
                return;

            _refreshRemaining = RefreshInterval;
            if (_cleaning == null || _cleaning is Object unityObject && unityObject == null)
                Services.TryGet(out _cleaning);

            bool visible = _cleaning != null && !(_cleaning is Object alive && alive == null);
            if (visible && _canvas == null)
                BuildCanvas();
            if (_panel != null && _panel.activeSelf != visible)
                _panel.SetActive(visible);
            if (!visible)
                return;

            CleaningTaskProgress progress = _cleaning.TaskProgress;
            Apply(_stainRow, progress.CleanedStains, progress.TotalStains);
            Apply(_furnitureRow, progress.DeliveredFurniture, progress.TotalFurniture);
        }

        private static void Apply(Row row, int done, int total)
        {
            if (row.ShownDone == done && row.ShownTotal == total)
                return;

            row.ShownDone = done;
            row.ShownTotal = total;
            bool complete = total > 0 && done >= total;
            row.Value.text = total > 0 ? $"{done} / {total}" : "-";
            row.Value.color = complete ? DoneColor : TextColor;
            row.Fill.anchorMax = new Vector2(CleaningTaskProgress.Ratio(done, total), 1f);
            row.FillImage.color = complete ? DoneColor : BarColor;
        }

        private void BuildCanvas()
        {
            GameObject canvasObject = new("CleaningProgressCanvas");
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.overrideSorting = true;
            _canvas.sortingOrder = CanvasSortingOrder;

            CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = ReferenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            float rowBlock = RowHeight + BarHeight;
            float height = Padding * 2f + TitleHeight + RowSpacing + rowBlock * 2f + RowSpacing;

            _panel = new GameObject("CleaningProgressPanel");
            _panel.transform.SetParent(_canvas.transform, false);
            RectTransform panelRect = _panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 1f);
            panelRect.anchorMax = new Vector2(0f, 1f);
            panelRect.pivot = new Vector2(0f, 1f);
            panelRect.sizeDelta = new Vector2(PanelWidth, height);
            panelRect.anchoredPosition = new Vector2(Margin, -Margin);
            Image background = _panel.AddComponent<Image>();
            background.color = PanelColor;
            background.raycastTarget = false;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            float y = -Padding;
            CreateText("Title", "청소 진행도", font, 20, FontStyle.Bold, TextAnchor.MiddleLeft, y, TitleHeight);
            y -= TitleHeight + RowSpacing;
            _stainRow = CreateRow("Stains", "얼룩 닦기 (대걸레)", font, y);
            y -= rowBlock + RowSpacing;
            _furnitureRow = CreateRow("Delivery", "물품 반출 (드릴카)", font, y);
        }

        private Row CreateRow(string name, string label, Font font, float y)
        {
            CreateText(name + "Label", label, font, 17, FontStyle.Normal, TextAnchor.MiddleLeft, y, RowHeight);
            Text value = CreateText(name + "Value", "-", font, 17, FontStyle.Bold, TextAnchor.MiddleRight, y,
                RowHeight);

            RectTransform back = CreateRect(name + "Bar", _panel.transform);
            back.anchorMin = new Vector2(0f, 1f);
            back.anchorMax = new Vector2(1f, 1f);
            back.pivot = new Vector2(0.5f, 1f);
            back.offsetMin = new Vector2(Padding, y - RowHeight - BarHeight);
            back.offsetMax = new Vector2(-Padding, y - RowHeight);
            Image backImage = back.gameObject.AddComponent<Image>();
            backImage.color = BarBackColor;
            backImage.raycastTarget = false;

            // 채움은 가로 앵커 비율로 늘린다 — 스프라이트 없이 Filled 이미지를 쓸 수 없어서다.
            RectTransform fill = CreateRect("Fill", back);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            Image fillImage = fill.gameObject.AddComponent<Image>();
            fillImage.color = BarColor;
            fillImage.raycastTarget = false;

            return new Row { Value = value, Fill = fill, FillImage = fillImage };
        }

        private Text CreateText(string name, string content, Font font, int size, FontStyle style,
            TextAnchor alignment, float y, float height)
        {
            RectTransform rect = CreateRect(name, _panel.transform);
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(Padding, y - height);
            rect.offsetMax = new Vector2(-Padding, y);
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.fontStyle = style;
            text.alignment = alignment;
            text.color = TextColor;
            text.text = content;
            text.raycastTarget = false;
            return text;
        }

        private static RectTransform CreateRect(string name, Transform parent)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child.AddComponent<RectTransform>();
        }
    }
}
