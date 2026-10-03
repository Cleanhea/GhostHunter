using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>
    /// 설정 창의 색과 UGUI 위젯을 코드로 만든다. 타이틀과 스테이지 네 씬이 같은 창을 쓰므로
    /// 씬마다 배치하지 않고 여는 쪽이 런타임에 짓는다 → docs/architecture/settings-menu.md §3
    /// </summary>
    internal static class SettingsUiKit
    {
        // 일시정지 메뉴의 남색 패널 위에 헤드램프의 따뜻한 빛을 강조색으로 쓴다.
        public static readonly Color Backdrop = new(0.01f, 0.01f, 0.02f, 0.82f);
        public static readonly Color PanelEdge = new(1f, 0.79f, 0.6f, 0.28f);
        public static readonly Color Panel = new(0.05f, 0.06f, 0.09f, 0.98f);
        public static readonly Color RowShade = new(1f, 1f, 1f, 0.035f);
        public static readonly Color Accent = new(1f, 0.79f, 0.6f, 1f);
        public static readonly Color AccentFaint = new(1f, 0.79f, 0.6f, 0.18f);
        public static readonly Color TextBright = new(0.92f, 0.95f, 1f, 1f);
        public static readonly Color TextDim = new(0.55f, 0.59f, 0.63f, 1f);
        public static readonly Color Danger = new(0.92f, 0.36f, 0.3f, 1f);
        public static readonly Color ButtonNormal = new(0.16f, 0.2f, 0.3f, 1f);
        public static readonly Color ButtonHover = new(0.25f, 0.3f, 0.42f, 1f);
        public static readonly Color ButtonPressed = new(0.1f, 0.12f, 0.18f, 1f);
        public static readonly Color Track = new(0.13f, 0.15f, 0.2f, 1f);

        public const int LabelSize = 22;
        public const int NoteSize = 17;
        public const float RowHeight = 54f;

        private static Font _font;

        /// <summary>씬의 다른 UI 와 같은 내장 폰트. 한글은 OS 폰트로 대체 렌더링된다.</summary>
        public static Font Font => _font != null ? _font : _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        public static void Stretch(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 offsetMin = default, Vector2 offsetMax = default)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        public static void Fill(RectTransform rect) => Stretch(rect, Vector2.zero, Vector2.one);

        public static Image CreateImage(string name, Transform parent, Color color, bool raycast = false)
        {
            RectTransform rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        public static Text CreateText(string name, Transform parent, string content, int size, Color color,
            TextAnchor alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = Font;
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        public static Button CreateButton(string name, Transform parent, string label, int size, out Text labelText)
        {
            Image background = CreateImage(name, parent, Color.white, raycast: true);
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.colors = new ColorBlock
            {
                normalColor = ButtonNormal,
                highlightedColor = ButtonHover,
                pressedColor = ButtonPressed,
                // 클릭 뒤 선택 상태로 남아 강조색이 붙어 있지 않게 한다.
                selectedColor = ButtonNormal,
                disabledColor = new Color(ButtonNormal.r, ButtonNormal.g, ButtonNormal.b, 0.4f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            DisableNavigation(button);

            labelText = CreateText("Label", background.transform, label, size, TextBright, TextAnchor.MiddleCenter);
            Fill(labelText.rectTransform);
            return button;
        }

        /// <summary>트랙·채움·손잡이로 된 가로 슬라이더. 행 높이 전체가 클릭을 받는다.</summary>
        public static Slider CreateSlider(Transform parent)
        {
            Image hitArea = CreateImage("Slider", parent, Color.clear, raycast: true);
            RectTransform root = hitArea.rectTransform;

            Image track = CreateImage("Track", root, Track);
            Stretch(track.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(0f, 4f));

            RectTransform fillArea = CreateRect("FillArea", root);
            Stretch(fillArea, new Vector2(0f, 0.5f), new Vector2(1f, 0.5f), new Vector2(0f, -4f), new Vector2(0f, 4f));
            Image fill = CreateImage("Fill", fillArea, Accent);
            Fill(fill.rectTransform);

            RectTransform handleArea = CreateRect("HandleArea", root);
            Stretch(handleArea, Vector2.zero, Vector2.one, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            Image handle = CreateImage("Handle", handleArea, TextBright, raycast: true);
            handle.rectTransform.sizeDelta = new Vector2(16f, -14f);

            var slider = root.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.colors = new ColorBlock
            {
                normalColor = TextBright,
                highlightedColor = Accent,
                pressedColor = Accent,
                selectedColor = TextBright,
                disabledColor = TextDim,
                colorMultiplier = 1f,
                fadeDuration = 0.08f,
            };
            DisableNavigation(slider);
            return slider;
        }

        /// <summary>
        /// 왼쪽에 이름, 오른쪽에 조작부가 있는 한 줄. 반환값은 조작부를 놓을 영역이다.
        /// 부모의 VerticalLayoutGroup 이 높이를 정한다.
        /// </summary>
        public static RectTransform CreateRow(Transform parent, string label, out Text labelText, float height = RowHeight)
        {
            Image row = CreateImage("Row " + label, parent, RowShade);
            var layout = row.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;

            labelText = CreateText("Label", row.transform, label, LabelSize, TextBright, TextAnchor.MiddleLeft);
            Stretch(labelText.rectTransform, Vector2.zero, new Vector2(0.4f, 1f), new Vector2(20f, 0f), Vector2.zero);

            RectTransform control = CreateRect("Control", row.transform);
            Stretch(control, new Vector2(0.4f, 0f), Vector2.one, new Vector2(0f, 9f), new Vector2(-16f, -9f));
            return control;
        }

        /// <summary>탭 안의 작은 소제목.</summary>
        public static Text CreateSectionHeader(Transform parent, string title)
        {
            Text text = CreateText("Section " + title, parent, title, 19, Accent, TextAnchor.LowerLeft);
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 40f;
            layout.preferredHeight = 40f;
            return text;
        }

        /// <summary>회색 설명문. 줄 수에 맞춰 높이가 늘어난다.</summary>
        public static Text CreateNote(Transform parent, string content)
        {
            Text text = CreateText("Note", parent, content, NoteSize, TextDim, TextAnchor.UpperLeft);
            var layout = text.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = 26f;
            return text;
        }

        private static void DisableNavigation(Selectable selectable)
        {
            // 메뉴 중 WASD·방향키가 UI 선택을 옮기지 않게 한다. 마우스로만 조작한다.
            selectable.navigation = new Navigation { mode = Navigation.Mode.None };
        }
    }
}
