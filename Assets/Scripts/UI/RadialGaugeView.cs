using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>원형 배경·진행 링·선택적 아이콘으로 구성된 HUD 표시부다.</summary>
    internal sealed class RadialGaugeView
    {
        public RadialGaugeView(Transform parent, string name, float diameter,
            Sprite background, Sprite ring, string ringName, bool clockwise)
        {
            Rect = HudUiFactory.CreateRect(name, parent);
            Rect.anchorMin = new Vector2(0.5f, 0.5f);
            Rect.anchorMax = new Vector2(0.5f, 0.5f);
            Rect.pivot = new Vector2(0.5f, 0.5f);
            Rect.anchoredPosition = Vector2.zero;
            Rect.sizeDelta = new Vector2(diameter, diameter);
            Background = Rect.gameObject.AddComponent<Image>();
            Background.sprite = background;
            Background.raycastTarget = false;
            Ring = HudUiFactory.CreateImage(ringName, Rect, ring);
            Ring.type = Image.Type.Filled;
            Ring.fillMethod = Image.FillMethod.Radial360;
            Ring.fillOrigin = (int)Image.Origin360.Top;
            Ring.fillClockwise = clockwise;
        }

        public RectTransform Rect { get; }
        public GameObject Root => Rect.gameObject;
        public Image Background { get; }
        public Image Ring { get; }
        public Image Icon { get; private set; }
        public Sprite OnIcon { get; private set; }
        public Sprite OffIcon { get; private set; }

        /// <summary>화면 우측 상단을 기준으로 배치한다.</summary>
        public void PlaceTopRight(Vector2 position)
        {
            Rect.anchorMin = Vector2.one;
            Rect.anchorMax = Vector2.one;
            Rect.pivot = Vector2.one;
            Rect.anchoredPosition = position;
        }

        /// <summary>원형 게이지 안에 원본 비율을 보존하는 아이콘을 만든다.</summary>
        public Image CreateIcon(Sprite onIcon, Sprite offIcon = null)
        {
            OnIcon = onIcon;
            OffIcon = offIcon;
            Icon = HudUiFactory.CreateImage("Icon", Rect, onIcon);
            Icon.preserveAspect = true;
            return Icon;
        }

        /// <summary>게이지 위에 중앙 정렬 문구를 만든다.</summary>
        public Text CreateLabel(string name, Font font, int size)
        {
            RectTransform rect = HudUiFactory.CreateRect(name, Rect);
            HudUiFactory.Stretch(rect);
            Text label = rect.gameObject.AddComponent<Text>();
            label.font = font;
            label.fontSize = size;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            return label;
        }
    }
}
