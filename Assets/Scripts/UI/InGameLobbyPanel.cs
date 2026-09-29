using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.UI
{
    /// <summary>
    /// 인게임 로비 화면(ADR-0018, 2026-09-28). 상점 규칙은 <see cref="StageShopRules"/>(2026-09-29 — 철제 드라이버·
    /// 라이터는 플레이어별, 촛대는 공동, 철제 드라이버 수리).
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
        private const float SummaryWidth = 320f;
        private const float WindowWidth = 560f;
        private const float WindowHeight = 460f;

        private ISteamLobbyService _lobby;
        private IStageShopService _shop;
        private IStageSessionFlow _stageFlow;
        private ISceneFlow _sceneFlow;
        private ILocalPlayerContext _localPlayer;
        private bool _open;
        private string _status = string.Empty;
        private Vector2 _scroll;

        /// <summary>단말기 창이 열려 있는가.</summary>
        public bool IsOpen => _open;

        private void Awake()
        {
            Services.TryGet(out _lobby);
            Services.TryGet(out _shop);
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
            || _lobby != null && _lobby.IsInLobby && _lobby.IsLobbyOwner
            || _shop != null && _shop.CanManage;

        private void OnGUI()
        {
            DrawSummary();
            if (_open)
                DrawWindow();
        }

        private void DrawSummary()
        {
            GUILayout.BeginArea(new Rect(Screen.width - SummaryWidth - 16f, 16f, SummaryWidth, 170f), GUI.skin.box);
            GUILayout.Label("인게임 로비 — 다음 스테이지: 원룸 · 보통");
            if (ShopAvailable)
            {
                GUILayout.Label($"공동 잔액 ${_shop.Balance}  (한 판마다 +${StageShopRules.StageReward})");
                GUILayout.Label($"촛불 {_shop.CandleCount}개  (부활 1회에 {StageShopRules.CandlesPerSet}개)");
                MemberGear mine = _shop.GetMemberGear(_shop.LocalMemberKey);
                GUILayout.Label($"내 장비  {GearSummary(mine)}");
            }
            else
            {
                GUILayout.Label("세션이 시작되면 상점을 쓸 수 있습니다.");
            }

            GUILayout.Label(IsHost
                ? "단말기에서 E — 상점 · 스테이지 출발"
                : "방장이 상점과 출발을 맡습니다(관람).");
            GUILayout.EndArea();
        }

        private void DrawWindow()
        {
            var area = new Rect((Screen.width - WindowWidth) * 0.5f, (Screen.height - WindowHeight) * 0.5f,
                WindowWidth, WindowHeight);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Label("인게임 로비 단말기");
            bool isHost = IsHost;
            if (ShopAvailable)
            {
                _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(WindowHeight - 150f));
                DrawShop(isHost);
                GUILayout.EndScrollView();
            }
            else
            {
                GUILayout.Label("세션이 시작되면 상점을 쓸 수 있습니다.");
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

        /// <summary>공동 품목 한 줄 + 플레이어마다 드라이버·라이터·수리 한 줄(stage-system.md §2.2).</summary>
        private void DrawShop(bool isHost)
        {
            GUILayout.Label($"공동 잔액 ${_shop.Balance}");

            GUILayout.Label("— 공동 —");
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{StageShopRules.NameOf(ShopItem.CandleSet)}  ${StageShopRules.PriceOf(ShopItem.CandleSet)}" +
                $"  |  촛불 {_shop.CandleCount}개");
            if (isHost)
                BuyButton(ShopItem.CandleSet, 0, MemberGear.Starting, $"구매");
            GUILayout.EndHorizontal();

            GUILayout.Space(6f);
            GUILayout.Label("— 플레이어별 —");
            foreach (ShopMember member in _shop.GetMembers())
            {
                MemberGear gear = _shop.GetMemberGear(member.Key);
                GUILayout.Label($"{member.DisplayName}  —  {GearSummary(gear)}");
                if (!isHost)
                    continue;

                GUILayout.BeginHorizontal();
                BuyButton(ShopItem.IronDriver, member.Key, gear,
                    gear.Driver == DriverTier.Iron ? "철제 드라이버 보유" : $"철제 드라이버 ${StageShopRules.PriceOf(ShopItem.IronDriver)}");
                BuyButton(ShopItem.IronLighter, member.Key, gear,
                    gear.HasLighter ? "라이터 보유" : $"철제 라이터 ${StageShopRules.PriceOf(ShopItem.IronLighter)}");

                int cost = StageShopRules.RepairCost(gear.DriverDurability);
                GUI.enabled = CanShopNow && StageShopRules.CanRepair(_shop.CanManage, _sceneFlow.Current,
                    _sceneFlow.IsLoading, _shop.Balance, gear);
                string repairLabel = !StageShopRules.IsRepairable(gear) ? "수리 (철제만)"
                    : cost == 0 ? "수리 불필요" : $"수리 ${cost}";
                if (GUILayout.Button(repairLabel, GUILayout.Width(120f)) && !_shop.TryRepairDriver(member.Key))
                    _status = "수리하지 못했습니다(잔액 부족 또는 전환 중).";
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
        }

        private bool ShopAvailable => _shop != null && _shop.IsAvailable;

        private bool CanShopNow => _sceneFlow != null && ShopAvailable;

        private void BuyButton(ShopItem item, ulong memberKey, MemberGear gear, string label)
        {
            GUI.enabled = CanShopNow && StageShopRules.CanPurchase(_shop.CanManage, _sceneFlow.Current,
                _sceneFlow.IsLoading, _shop.Balance, item, gear);
            if (GUILayout.Button(label, GUILayout.Width(150f)) && !_shop.TryPurchase(item, memberKey))
                _status = "구매하지 못했습니다(잔액 부족·이미 보유 또는 전환 중).";
            GUI.enabled = true;
        }

        private static string GearSummary(MemberGear gear)
        {
            string driver = gear.Driver == DriverTier.Iron ? "철제" : "나무";
            return $"드라이버({driver}) {gear.DriverDurability}% · 라이터 {(gear.HasLighter ? "있음" : "없음")}";
        }
    }
}
