using GhostHunter.Core;
using GhostHunter.Furniture;
using GhostHunter.Interaction;
using GhostHunter.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 씬 배선 의존성을 줄이기 위해 프로토타입 HUD는 IMGUI로 그린다. 실제 아트 UI로
    /// 교체하더라도 타겟팅/홀드 로직에는 영향이 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CrosshairUI : MonoBehaviour
    {
        private GUIStyle _centerStyle;
        private GUIStyle _hintStyle;
        private ILocalPlayerContext _localPlayer;

        private void Awake()
        {
            _localPlayer = Services.Get<ILocalPlayerContext>();
        }

        private void OnGUI()
        {
            _centerStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 30,
                fontStyle = FontStyle.Bold,
            };

            _hintStyle ??= new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 14,
                richText = true,
                padding = new RectOffset(12, 12, 8, 8),
            };

            DrawCrosshair();
            DrawInteractPrompt();
            DrawTestHoldStatus();
            DrawInstructions();
        }

        private void DrawInteractPrompt()
        {
            PlayerInteractor interactor = _localPlayer.Interactor;
            DoorInteractable door = interactor != null ? interactor.CurrentDoor : null;
            if (door == null)
                return;

            GUI.Box(
                new Rect(Screen.width * 0.5f - 80f, Screen.height * 0.5f + 30f, 160f, 36f),
                door.IsOpen ? "<b>E</b> 문 닫기" : "<b>E</b> 문 열기",
                _hintStyle);
        }

        private void DrawCrosshair()
        {
            Color previous = GUI.color;
            GUI.color = ResolveCrosshairColor();

            Rect rect = new(Screen.width * 0.5f - 20f, Screen.height * 0.5f - 24f, 40f, 48f);
            GUI.Label(rect, "+", _centerStyle);
            GUI.color = previous;
        }

        private Color ResolveCrosshairColor()
        {
            FurnitureTargeter targeter = _localPlayer.Targeter;
            if (targeter == null || targeter.CurrentTarget == null)
                return Color.white;

            ulong localClientId = targeter.NetworkManager != null
                ? targeter.NetworkManager.LocalClientId
                : ulong.MaxValue;

            return targeter.CurrentTarget.CanGrab(localClientId)
                ? new Color(0.2f, 1f, 0.8f)
                : new Color(1f, 0.35f, 0.35f);
        }

        private void DrawTestHoldStatus()
        {
            GrabController grab = _localPlayer.GrabController;
            if (grab == null)
                return;

            string message = grab.IsTestHoldLatched
                ? "<color=#55ffbb><b>F12 입력 고정: ON</b></color>"
                : "<b>F12</b> 입력 고정: OFF";

            GUI.Box(
                new Rect(Screen.width - 230f, 10f, 220f, 38f),
                message,
                _hintStyle);
        }

        private void DrawInstructions()
        {
            string message = _localPlayer.Targeter == null
                ? "<b>GhostHunter Prototype</b>\n왼쪽 HUD에서 Local 모드 → Host를 눌러 시작"
                : "<b>WASD</b> 이동  ·  <b>Space</b> 점프  ·  <b>마우스</b> 시점  ·  <b>E</b> 문 여닫기\n" +
                  "<b>좌클릭 누름</b> 투척 준비  ·  <b>떼기</b> 밀기/던지기  ·  " +
                  "<b>2인 동시 누름</b> 잡아당기기  ·  <b>R</b> 가구 리셋(호스트)  ·  <b>Esc</b> 커서";

            float width = Mathf.Min(620f, Screen.width - 20f);
            GUI.Box(
                new Rect((Screen.width - width) * 0.5f, Screen.height - 78f, width, 58f),
                message,
                _hintStyle);
        }
    }
}
