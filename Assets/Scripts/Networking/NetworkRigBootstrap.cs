using GhostHunter.DebugTools;
using UnityEngine;

namespace GhostHunter.Networking
{
    /// <summary>
    /// 씬에 영속 네트워크 리그(NetworkManager + 트랜스포트 + 로비/접속 매니저)가 없으면
    /// 프리팹에서 생성한다. NGO 의 NetworkManager 는 중복 인스턴스를 스스로 정리하지
    /// 않으므로, 씬마다 리그를 직접 배치하는 대신 이 부트스트랩으로 "한 개만" 보장한다.
    /// 리그는 NetworkManager 가 스스로 DontDestroyOnLoad 처리하므로 씬을 넘어 살아남는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkRigBootstrap : MonoBehaviour
    {
        [SerializeField] private GameObject _rigPrefab;

        [Tooltip("이 씬에서 리그를 새로 만들 때: 로비 이벤트로 세션을 자동 시작할지. " +
                 "Prototype 단독 플레이는 켜고, 메인메뉴/로비 씬은 끈다.")]
        [SerializeField] private bool _autoStartFromLobbyEvents = true;

        [Tooltip("이 씬에서 리그를 새로 만들 때: 개발용 접속 HUD 를 처음부터 보일지.")]
        [SerializeField] private bool _connectionHudVisible = true;

        private void Awake()
        {
            if (ConnectionManager.Instance != null)
                return; // 앞선 씬에서 만든 영속 리그가 이미 있다.

            if (_rigPrefab == null)
            {
                Debug.LogError("[NetworkRigBootstrap] 리그 프리팹이 비어 있습니다. " +
                               "GhostHunter > 프로토타입 게임 생성 메뉴로 씬을 재생성하세요.");
                return;
            }

            GameObject rig = Instantiate(_rigPrefab);
            rig.name = _rigPrefab.name;

            if (rig.TryGetComponent(out ConnectionManager connection))
                connection.SetAutoStartFromLobbyEvents(_autoStartFromLobbyEvents);

            if (rig.TryGetComponent(out ConnectionHud hud))
                hud.SetVisible(_connectionHudVisible);
        }
    }
}
