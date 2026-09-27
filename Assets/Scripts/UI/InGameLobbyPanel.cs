using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 인게임 로비 화면(ADR-0018, 2026-09-28). 일반 로비에 있던 임시 공동 상점을 여기로 옮겼다.
    ///
    /// <list type="bullet">
    /// <item>오른쪽 위 요약 — 누구나 본다(잔액·보유·다음 스테이지). 방장이 아니면 관람만 한다.</item>
    /// <item>단말기 창 — <see cref="StageLobbyTerminal"/> 에서 E 로 연다. <b>방장만</b> 구매·스테이지 출발 버튼이 있고,
    /// 게스트에게는 같은 정보를 읽기 전용으로 보여 준다. 열려 있는 동안 조작을 잠그고 커서를 푼다.</item>
    /// </list>
    ///
    /// <para>임시 UI 라 다른 임시 화면(정산·스테이지 종료 확인)처럼 IMGUI 로 그린다. 방의 정산 이력은
    /// <c>SceneFlowController</c> 가 왼쪽 위에 그린다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class InGameLobbyPanel : MonoBehaviour
    {
        private const float SummaryWidth = 300f;
        private const float WindowWidth = 360f;

        private ISteamLobbyService _lobby;
        private IStageSessionFlow _stageFlow;
        private ISceneFlow _sceneFlow;
        private ILocalPlayerContext _localPlayer;
        private bool _open;
        private string _status = string.Empty;

        /// <summary>단말기 창이 열려 있는가.</summary>
        public bool IsOpen => _open;

        private void Awake()
        {
            Services.TryGet(out _lobby);
            Services.TryGet(out _stageFlow);
            Services.TryGet(out _sceneFlow);
            Services.TryGet(out _localPlayer);
        }

        private void OnEnable() => StageLobbyTerminal.OpenRequested += Open;

        private void OnDisable()
        {
            StageLobbyTerminal.OpenRequested -= Open;
            if (_open)
                Close();
        }

        private void Update()
        {
            // 일시정지 메뉴가 위에 떴다가 닫히며 커서를 다시 잠갔다 — 이 창도 닫는다.
            if (_open && Cursor.lockState == CursorLockMode.Locked)
                Close();
        }

        public void Open()
        {
            if (_open)
                return;

            _open = true;
            _status = string.Empty;
            SetGameplayLocked(true);
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public void Close()
        {
            if (!_open)
                return;

            _open = false;
            SetGameplayLocked(false);
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void SetGameplayLocked(bool locked)
        {
            PlayerInputReader input = _localPlayer != null ? _localPlayer.Input : null;
            if (input != null)
                input.SetGameplayInputLocked(locked);
        }

        private bool IsHost => _stageFlow != null && _stageFlow.CanControl
            || _lobby != null && _lobby.IsInLobby && _lobby.IsLobbyOwner;

        private void OnGUI()
        {
            DrawSummary();
            if (_open)
                DrawWindow();
        }

        private void DrawSummary()
        {
            GUILayout.BeginArea(new Rect(Screen.width - SummaryWidth - 16f, 16f, SummaryWidth, 150f), GUI.skin.box);
            GUILayout.Label("인게임 로비 — 다음 스테이지: 원룸 · 보통");
            if (_lobby != null && _lobby.IsInLobby)
            {
                GUILayout.Label($"공동 상점 잔액 {_lobby.ShopBalance}");
                GUILayout.Label($"보유  Temp1 ×{_lobby.GetPurchasedTempItemCount(1)} · " +
                    $"Temp2 ×{_lobby.GetPurchasedTempItemCount(2)} · Temp3 ×{_lobby.GetPurchasedTempItemCount(3)}");
            }
            else
            {
                GUILayout.Label("공동 상점은 Steam 방에서만 쓸 수 있습니다.");
            }

            GUILayout.Label(IsHost
                ? "단말기에서 E — 상점 · 스테이지 출발"
                : "방장이 상점과 출발을 맡습니다(관람).");
            GUILayout.EndArea();
        }

        private void DrawWindow()
        {
            var area = new Rect((Screen.width - WindowWidth) * 0.5f, (Screen.height - 330f) * 0.5f, WindowWidth, 330f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("인게임 로비 단말기");
            bool isHost = IsHost;
            bool inSteamRoom = _lobby != null && _lobby.IsInLobby;

            if (inSteamRoom)
            {
                GUILayout.Label($"공동 상점 잔액 {_lobby.ShopBalance}");
                for (int item = 1; item <= StageShopRules.ItemCount; item++)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"Temp{item}  {StageShopRules.PriceOf(item)}  |  보유 {_lobby.GetPurchasedTempItemCount(item)}");
                    if (isHost)
                    {
                        GUI.enabled = _sceneFlow != null && StageShopRules.CanPurchase(_lobby.IsLobbyOwner,
                            _sceneFlow.Current, _sceneFlow.IsLoading, _lobby.ShopBalance, item);
                        if (GUILayout.Button("구매", GUILayout.Width(60f)) && !_lobby.TryPurchaseTempItem(item))
                            _status = "구매하지 못했습니다(잔액 부족 또는 전환 중).";
                        GUI.enabled = true;
                    }
                    GUILayout.EndHorizontal();
                }
            }
            else
            {
                GUILayout.Label("공동 상점은 Steam 방에서만 쓸 수 있습니다.");
            }

            GUILayout.FlexibleSpace();
            if (isHost && _stageFlow != null)
            {
                GUI.enabled = _stageFlow.CanControl;
                if (GUILayout.Button("스테이지 출발", GUILayout.Height(36f)))
                {
                    if (_stageFlow.StartStage())
                        Close();
                    else
                        _status = "지금은 출발할 수 없습니다.";
                }
                GUI.enabled = true;
            }
            else
            {
                GUILayout.Label("구매와 스테이지 출발은 방장만 할 수 있습니다.");
            }

            if (!string.IsNullOrEmpty(_status))
                GUILayout.Label(_status);
            if (GUILayout.Button("닫기", GUILayout.Height(28f)))
                Close();
            GUILayout.EndArea();
        }
    }
}
