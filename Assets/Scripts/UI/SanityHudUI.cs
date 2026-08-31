using GhostHunter.Core;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;
using UnityEngine.UI;

namespace GhostHunter.UI
{
    /// <summary>월드 모니터에 최대 4인의 개인 정신력과 생존 팀 평균을 숫자로 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class SanityHudUI : MonoBehaviour
    {
        private const float RefreshInterval = 0.1f;
        private const int MaxPlayerCount = 4;

        [Header("Panel")]
        [SerializeField] private GameObject _panel;

        [Header("Players")]
        [SerializeField] private Text[] _playerLabelTexts = new Text[MaxPlayerCount];
        [SerializeField] private Text[] _playerSymbolTexts = new Text[MaxPlayerCount];
        [SerializeField] private Text[] _playerValueTexts = new Text[MaxPlayerCount];

        [Header("Team")]
        [SerializeField] private Text _teamLabelText;
        [SerializeField] private Text _teamSymbolText;
        [SerializeField] private Text _teamValueText;

        private readonly SanityNetworkState[] _playerStates = new SanityNetworkState[MaxPlayerCount];
        private ISanityTeamService _teamService;
        private float _refreshRemaining;

        private void Awake()
        {
            _teamService = Services.Get<ISanityTeamService>();
            Refresh();
        }

        private void OnEnable()
        {
            _refreshRemaining = 0f;
        }

        private void Update()
        {
            _refreshRemaining -= Time.unscaledDeltaTime;
            if (_refreshRemaining > 0f)
                return;

            _refreshRemaining = RefreshInterval;
            Refresh();
        }

        private void Refresh()
        {
            if (_teamService == null
                || _teamService is UnityEngine.Object unityObject && unityObject == null)
            {
                return;
            }

            int playerCount = _teamService.CopyPlayerStates(_playerStates);
            RefreshPlayers(playerCount);

            bool hasTeamAverage = _teamService.TryGetTeamAverage(
                out _,
                out int teamAverage,
                out _);
            RefreshTeam(hasTeamAverage, teamAverage);
        }

        private void RefreshPlayers(int playerCount)
        {
            _panel.SetActive(true);

            for (int i = 0; i < MaxPlayerCount; i++)
            {
                bool isOccupied = i < playerCount && _playerStates[i] != null;
                SanityNetworkState state = isOccupied ? _playerStates[i] : null;

                _playerLabelTexts[i].color = isOccupied ? ActiveLabelColor : DisabledColor;
                _playerSymbolTexts[i].color = isOccupied ? MonitorRed : DisabledColor;
                _playerValueTexts[i].color = isOccupied ? MonitorRed : DisabledColor;
                _playerValueTexts[i].text = isOccupied && state.HasSanity
                    ? PercentTexts[Mathf.Clamp(state.Sanity, 0, 100)]
                    : "-%";
            }
        }

        private void RefreshTeam(bool hasAverage, int average)
        {
            Color statusColor = hasAverage ? MonitorRed : DisabledColor;
            _teamLabelText.color = hasAverage ? ActiveLabelColor : DisabledColor;
            _teamSymbolText.color = statusColor;
            _teamValueText.color = statusColor;
            _teamValueText.text = hasAverage
                ? PercentTexts[Mathf.Clamp(average, 0, 100)]
                : "-%";
        }

        private static readonly string[] PercentTexts =
        {
            "0%", "1%", "2%", "3%", "4%", "5%", "6%", "7%", "8%", "9%",
            "10%", "11%", "12%", "13%", "14%", "15%", "16%", "17%", "18%", "19%",
            "20%", "21%", "22%", "23%", "24%", "25%", "26%", "27%", "28%", "29%",
            "30%", "31%", "32%", "33%", "34%", "35%", "36%", "37%", "38%", "39%",
            "40%", "41%", "42%", "43%", "44%", "45%", "46%", "47%", "48%", "49%",
            "50%", "51%", "52%", "53%", "54%", "55%", "56%", "57%", "58%", "59%",
            "60%", "61%", "62%", "63%", "64%", "65%", "66%", "67%", "68%", "69%",
            "70%", "71%", "72%", "73%", "74%", "75%", "76%", "77%", "78%", "79%",
            "80%", "81%", "82%", "83%", "84%", "85%", "86%", "87%", "88%", "89%",
            "90%", "91%", "92%", "93%", "94%", "95%", "96%", "97%", "98%", "99%",
            "100%"
        };
        private static readonly Color DisabledColor = new(0.55f, 0.59f, 0.63f, 1f);
        private static readonly Color ActiveLabelColor = new(0.9f, 0.93f, 0.96f, 1f);
        private static readonly Color MonitorRed = new(1f, 0.12f, 0.2f, 1f);
    }
}
