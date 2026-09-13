using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    [DisallowMultipleComponent]
    public sealed class ChargeGaugeUI : MonoBehaviour
    {
        private GUIStyle _labelStyle;
        private Texture2D _whiteTexture;
        private ILocalPlayerContext _localPlayer;

        private void Awake()
        {
            _localPlayer = Services.Get<ILocalPlayerContext>();
        }

        private void OnDestroy()
        {
            if (_whiteTexture != null)
                Destroy(_whiteTexture);
        }

        private void OnGUI()
        {
            GrabController grab = _localPlayer.GrabController;
            if (grab == null || !grab.TryGetHeldTarget(out FurnitureGrabTarget target))
                return;

            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };

            _whiteTexture ??= CreateWhiteTexture();

            float width = Mathf.Min(360f, Screen.width * 0.45f);
            Rect outer = new(Screen.width * 0.5f - width * 0.5f, Screen.height - 130f, width, 24f);
            Rect inner = new(outer.x + 3f, outer.y + 3f, (outer.width - 6f) * target.Charge, outer.height - 6f);

            Color previous = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.9f);
            GUI.DrawTexture(outer, _whiteTexture);

            GUI.color = target.HolderCount >= 2
                ? new Color(1f, 0.85f, 0.1f)
                : new Color(0.1f, 0.95f, 0.8f);
            GUI.DrawTexture(inner, _whiteTexture);

            GUI.color = Color.white;
            string holders = target.State != FurnitureState.Held
                ? "1인 투척 준비"
                : grab.RotateMode == FurnitureRotateMode.Tilt
                    ? "2인 협력 잡기 · 휠 기울이기 (휠 클릭: 회전)"
                    : "2인 협력 잡기 · 휠 회전 (휠 클릭: 기울이기)";
            float labelWidth = Mathf.Min(560f, Screen.width - 20f);
            GUI.Label(
                new Rect(Screen.width * 0.5f - labelWidth * 0.5f, outer.y - 23f, labelWidth, 22f),
                $"{holders}  {target.Charge * 100f:0}%",
                _labelStyle);
            GUI.color = previous;
        }

        private static Texture2D CreateWhiteTexture()
        {
            var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                name = "ChargeGaugePixel",
            };
            texture.SetPixel(0, 0, Color.white);
            texture.Apply();
            return texture;
        }
    }
}
