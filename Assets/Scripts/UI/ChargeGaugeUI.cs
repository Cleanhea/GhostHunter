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
            if (grab == null)
                return;

            if (!grab.TryGetHeldTarget(out FurnitureGrabTarget target))
            {
                if (grab.IsChargingShove)
                    DrawGauge(grab.ShovePower, new Color(1f, 0.55f, 0.2f), "플레이어 밀치기");
                return;
            }

            string holders = target.State != FurnitureState.Held
                ? "1인 투척 준비"
                : grab.RotateMode == FurnitureRotateMode.Tilt
                    ? "2인 협력 잡기 · 휠 기울이기 (휠 클릭: 회전)"
                    : "2인 협력 잡기 · 휠 회전 (휠 클릭: 기울이기)";

            // 누른 시간이 아니라 지금 놓으면 나갈 힘을 채운다 — 살짝 누르면 짧게 보여야 약하게 나가는 걸 안다.
            DrawGauge(
                target.LaunchPower,
                target.HolderCount >= 2 ? new Color(1f, 0.85f, 0.1f) : new Color(0.1f, 0.95f, 0.8f),
                holders);
        }

        private void DrawGauge(float power, Color fill, string caption)
        {
            _labelStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                fontStyle = FontStyle.Bold,
            };

            _whiteTexture ??= CreateWhiteTexture();

            float width = Mathf.Min(360f, Screen.width * 0.45f);
            Rect outer = new(Screen.width * 0.5f - width * 0.5f, Screen.height - 130f, width, 24f);
            Rect inner = new(outer.x + 3f, outer.y + 3f, (outer.width - 6f) * power, outer.height - 6f);

            Color previous = GUI.color;
            GUI.color = new Color(0.05f, 0.07f, 0.1f, 0.9f);
            GUI.DrawTexture(outer, _whiteTexture);

            GUI.color = fill;
            GUI.DrawTexture(inner, _whiteTexture);

            GUI.color = Color.white;
            float labelWidth = Mathf.Min(560f, Screen.width - 20f);
            GUI.Label(
                new Rect(Screen.width * 0.5f - labelWidth * 0.5f, outer.y - 23f, labelWidth, 22f),
                $"{caption}  힘 {power * 100f:0}%",
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
