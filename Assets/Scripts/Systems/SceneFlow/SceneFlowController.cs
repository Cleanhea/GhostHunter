using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Data.Scenes;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace GhostHunter.Systems.SceneFlow
{
    /// <summary>
    /// 씬 전환을 수행한다. Bootstrap 은 그대로 두고 게임플레이 씬만 additive 로 갈아 끼운다.
    /// 네트워크 세션 중에는 NGO 씬 매니저로, 그 외에는 로컬 로드로 처리한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneFlowController : MonoBehaviour, ISceneFlow
    {
        [SerializeField] private SceneNameSO _scenes;

        [Tooltip("Bootstrap 이 뜬 직후 자동으로 올릴 씬.")]
        [SerializeField] private SceneId _firstScene = SceneId.Title;

        private Scene _currentScene;
        private bool _currentLoadedByNgo;
        private readonly StageSettlementHistory _settlements = new();
        private SceneTransitionOperations _operations;
        private ISteamLobbyService _lobby;
        private Func<StageSettlementRecord, bool> _publishSettlement;
        private bool _resultReturnPressed;

        public SceneId Current { get; private set; } = SceneId.Bootstrap;
        public bool IsLoading { get; private set; }
        public int StageFailureDeadCount => _settlements.FailureDeadCount;
        public IReadOnlyList<StageSettlementRecord> SettlementHistory => _settlements.Records;
        public event Action<SceneId> SceneChanged;

        public void RecordStageSettlement(StageSettlementRecord record)
        {
            if (!_settlements.TryRecord(record))
                return;
            NetworkManager settlementNetwork = NetworkManager.Singleton;
            // 한 판이 끝날 때마다(전멸 포함) 공동 잔액 보상 — 서버(Steam 방장·로컬 호스트)만, 판마다 한 번.
            if (settlementNetwork != null && settlementNetwork.IsServer
                && Services.TryGet(out IStageShopService shop))
                shop.ServerGrantStageReward();
            if (_lobby != null && _lobby.IsLobbyOwner
                && settlementNetwork != null && settlementNetwork.IsServer)
            {
                _settlements.QueuePublication(record);
                TryPublishPendingSettlements();
            }
            if (record.TeamWiped)
                RecordStageFailure(record.Dead);
        }

        public void RecordStageFailure(int deadCount)
        {
            _settlements.RecordFailure(deadCount);
        }

        private void Awake()
        {
            _operations = new SceneTransitionOperations(this, destroyCancellationToken);
        }

        private void Start()
        {
            if (Services.TryGet(out ISteamLobbyService lobby))
            {
                _lobby = lobby;
                _publishSettlement = lobby.TryPublishStageSettlement;
                _lobby.LobbyLeft += HandleLobbyLeft;
                _lobby.LobbyUpdated += HandleLobbyUpdated;
                SyncSettlementHistoryFromLobby();
            }
            if (_scenes == null)
            {
                Debug.LogError($"{nameof(SceneFlowController)}: SceneNameSO 미할당", this);
                enabled = false;
                return;
            }

            // 게스트의 세션 씬(인게임 로비·스테이지)은 NGO 클라이언트 동기화가 올린다 — 이 컨트롤러를 거치지 않으므로
            // 그대로 두면 Current 가 Lobby 로 남고, 나중에 그 씬을 내리지 못한다.
            SceneManager.sceneLoaded += HandleSceneLoadedExternally;

            // 에디터에서 Bootstrap 과 작업 중인 씬을 함께 열어둔 경우, 그 씬을 Title 로 덮지 않는다.
            if (SceneManager.sceneCount > 1)
            {
                AdoptAlreadyLoadedScene();
                return;
            }

            Load(_firstScene);
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= HandleSceneLoadedExternally;
            if (_lobby != null)
            {
                _lobby.LobbyLeft -= HandleLobbyLeft;
                _lobby.LobbyUpdated -= HandleLobbyUpdated;
            }
        }

        private void HandleLobbyLeft()
        {
            _settlements.ClearLobby();
        }

        private void TryPublishPendingSettlements()
        {
            if (!_settlements.HasPendingPublications || _lobby == null
                || !_lobby.IsInLobby || !_lobby.IsLobbyOwner)
                return;
            if (!_settlements.PublishPending(_publishSettlement))
                Debug.LogWarning("정산 이력을 Steam 로비에 게시하지 못해 재시도합니다.", this);
        }

        private void HandleLobbyUpdated()
        {
            SyncSettlementHistoryFromLobby();
            if (Current == SceneId.Result && !IsLoading && _lobby != null
                && _lobby.IsInLobby && !_lobby.IsGameStarted && !_lobby.IsGameLoading)
            {
                NetworkManager network = NetworkManager.Singleton;
                if (network == null || !network.IsListening)
                    Load(SceneId.Lobby);
            }
        }

        private void SyncSettlementHistoryFromLobby()
        {
            if (_lobby == null || !_lobby.IsInLobby)
                return;
            int count = _lobby.PublishedSettlementCount;
            for (int i = SettlementHistory.Count; i < count; i++)
            {
                if (!_lobby.TryGetPublishedSettlement(i, out StageSettlementRecord record))
                    break;
                _settlements.ImportPublished(record, Current.IsStage());
            }
        }

        private void Update()
        {
            if (_settlements.ShouldRetry(Time.unscaledTime))
                TryPublishPendingSettlements();
            if (Current == SceneId.Result)
            {
                ReleaseCursorForResult();
                HandleLobbyUpdated();
                HandleResultReturnInput();
            }
            else
                _resultReturnPressed = false;
        }

        private static Rect ResultReturnButtonRect()
        {
            float width = Mathf.Min(400f, Screen.width - 32f);
            return new Rect((Screen.width - width) * 0.5f + 12f,
                (Screen.height + 280f) * 0.5f - 48f, width - 24f, 36f);
        }

        private void HandleResultReturnInput()
        {
            Mouse mouse = Mouse.current;
            if (IsLoading || mouse == null)
            {
                _resultReturnPressed = false;
                return;
            }
            Vector2 position = mouse.position.ReadValue();
            position.y = Screen.height - position.y;
            bool inside = ResultReturnButtonRect().Contains(position);
            if (mouse.leftButton.wasPressedThisFrame)
                _resultReturnPressed = inside;
            if (!mouse.leftButton.wasReleasedThisFrame)
                return;
            bool clicked = _resultReturnPressed && inside;
            _resultReturnPressed = false;
            if (clicked)
                ReturnFromResult();
        }

        private void ReturnFromResult()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
            {
                if (network.IsServer && Services.TryGet(out IStageSessionFlow stageFlow))
                    stageFlow.ReturnToInGameLobby();
            }
            else if (_lobby == null || !_lobby.IsInLobby || _lobby.IsLobbyOwner)
                Load(SceneId.Lobby);
        }

        /// <summary>
        /// 스테이지 → Result 는 플레이어를 디스폰하지 않으므로(정산 음성) <c>PlayerLook</c> 이 잠근 커서가 그대로 남는다.
        /// 정산 버튼을 누를 수 있게 Result 동안 매 프레임 푼다 — 스테이지 종료 확인 창 등이 닫히며 이전 잠금을
        /// 되돌려도 다시 풀린다. 인게임 로비에서 플레이어가 재스폰되면 <c>PlayerLook</c> 이 다시 잠근다.
        /// </summary>
        private static void ReleaseCursorForResult()
        {
            if (Cursor.lockState != CursorLockMode.None)
                Cursor.lockState = CursorLockMode.None;
            if (!Cursor.visible)
                Cursor.visible = true;
        }

        /// <summary>
        /// 이 컨트롤러가 올리지 않은 게임플레이 씬을 현재 씬으로 받아들인다.
        /// 실제로 이 경로를 타는 것은 <b>게스트가 NGO 씬 동기화로 들어가는 세션 씬</b>(인게임 로비·스테이지)이다.
        /// 받아들이지 않으면 세션을 떠날 때 그 씬이 화면에 남는다.
        /// </summary>
        private void HandleSceneLoadedExternally(Scene scene, LoadSceneMode mode)
        {
            // 우리가 시작한 전환은 LoadAsync 가 직접 처리한다.
            if (IsLoading || _scenes == null || !scene.IsValid())
                return;

            if (!_scenes.TryResolve(scene.name, out SceneId id) || id == SceneId.Bootstrap)
                return;

            if (id == Current && _currentScene == scene)
                return;

            SceneId previousId = Current;
            bool returnFromStage = (previousId.IsStage() || previousId == SceneId.Result)
                && id == SceneId.Lobby;
            Scene previousScene = _currentScene;
            bool previousLoadedByNgo = _currentLoadedByNgo;

            _currentScene = scene;
            _currentLoadedByNgo = true;
            Current = id;
            if (id.IsStage())
            {
                _settlements.BeginStage();
            }
            SceneManager.SetActiveScene(scene);

            Debug.Log(
                $"{nameof(SceneFlowController)}: 외부(NGO 동기화)에서 올라온 {id} 씬을 현재 씬으로 받아들였다.",
                this);

            // 게스트는 Lobby(또는 Title)에 서 있는 채로 세션 씬을 additive 로 받는다. 이전 씬을 내리지
            // 않으면 EventSystem·AudioListener 가 겹치고 그 씬의 UI 가 게임 위에 그대로 남는다.
            // NGO 가 올린 씬은 서버가 내리므로, 여기서는 우리가 올린 씬만 내린다.
            if (!previousLoadedByNgo && previousScene.IsValid() && previousScene.isLoaded
                && previousScene != scene)
            {
                _operations.UnloadAdoptedPreviousSceneAsync(previousScene).Forget();
            }

            SceneChanged?.Invoke(id);
            if (returnFromStage)
                EndStageSessionInLobbyAsync(previousId).Forget();
        }

        public void Load(SceneId scene)
        {
            if (!enabled)
                return;

            if (IsLoading)
            {
                Debug.LogWarning($"{nameof(SceneFlowController)}: 전환 중이라 {scene} 요청을 무시한다.", this);
                return;
            }

            if (scene == SceneId.Bootstrap)
            {
                Debug.LogError($"{nameof(SceneFlowController)}: Bootstrap 은 전환 대상이 아니다.", this);
                return;
            }

            if (scene == Current)
            {
                Debug.LogWarning($"{nameof(SceneFlowController)}: 이미 {scene} 이다.", this);
                return;
            }

            LoadAsync(scene).Forget();
        }

        private void OnGUI()
        {
            if (IsLoading)
            {
                if (Current is SceneId.Lobby or SceneId.InGameLobby)
                    GUI.Box(new Rect((Screen.width - 280f) * 0.5f,
                        (Screen.height - 70f) * 0.5f, 280f, 70f), "스테이지 로딩 중...");
                return;
            }

            // 방의 정산 이력은 스테이지 사이에 머무는 인게임 로비에 보인다(ADR-0018).
            if (Current == SceneId.InGameLobby && SettlementHistory.Count > 0)
            {
                GUILayout.BeginArea(new Rect(16f, 16f, 350f,
                    40f + SettlementHistory.Count * 24f), GUI.skin.box);
                GUILayout.Label("이번 방의 정산 이력");
                for (int i = 0; i < SettlementHistory.Count; i++)
                {
                    StageSettlementRecord history = SettlementHistory[i];
                    GUILayout.Label($"{i + 1}판: 가구 {history.DeliveredFurniture}/{history.TargetFurniture}, " +
                        $"청소 {history.CleaningPercent}%, 생존 {history.Survivors}명, 훔친 금액 ${history.StolenMoney}");
                }
                GUILayout.EndArea();
                return;
            }

            if (Current != SceneId.Result)
                return;

            bool hasCurrentResult = _settlements.HasCurrentResult;
            StageSettlementRecord record = hasCurrentResult
                ? SettlementHistory[SettlementHistory.Count - 1] : default;

            float width = Mathf.Min(400f, Screen.width - 32f);
            var area = new Rect((Screen.width - width) * 0.5f,
                (Screen.height - 280f) * 0.5f, width, 280f);
            GUILayout.BeginArea(area, GUI.skin.box);
            GUILayout.Space(24f);
            GUILayout.Label(hasCurrentResult
                ? (record.TeamWiped ? "스테이지 실패: 전원 사망" : "스테이지 정산")
                : "정산 결과를 수신하지 못했습니다", GUI.skin.label);
            GUILayout.Space(12f);
            if (hasCurrentResult)
            {
                GUILayout.Label($"목표 가구 반출 완료 {record.DeliveredFurniture}/{record.TargetFurniture}", GUI.skin.label);
                GUILayout.Label($"청소 완료 {record.CleaningPercent}%", GUI.skin.label);
                GUILayout.Label($"생존 {record.Survivors}명 / 실종 {record.Missing}명 / 사망 {record.Dead}명", GUI.skin.label);
                // 훔친 금액은 훔친 순간 공동 잔액에 이미 더했다 — 여기서는 따로 보여 주기만 한다(theft-system.md TH-Q2).
                GUILayout.Label($"훔친 금액 ${record.StolenMoney} (공동 잔액에 반영됨)", GUI.skin.label);
                GUILayout.Label("추가 보상과 치료비는 단가 확정 후 적용됩니다.", GUI.skin.label);
            }
            else
                GUILayout.Label("방장이 인게임 로비로 이동할 수 있습니다.", GUI.skin.label);
            GUILayout.FlexibleSpace();

            // 정산 뒤에는 세션을 유지한 채 인게임 로비로 간다(ADR-0018). 세션이 이미 끝났으면 예전처럼 일반 로비로.
            NetworkManager network = NetworkManager.Singleton;
            bool sessionRunning = network != null && network.IsListening;
            bool canReturn = sessionRunning ? network.IsServer
                : _lobby == null || !_lobby.IsInLobby || _lobby.IsLobbyOwner;
            if (canReturn)
                GUILayout.Space(48f);
            else
                GUILayout.Label("방장이 인게임 로비로 이동하기를 기다리는 중", GUI.skin.label);

            GUILayout.EndArea();
            if (canReturn)
            {
                if (GUI.Button(ResultReturnButtonRect(), sessionRunning ? "인게임 로비로 이동" : "로비로 이동"))
                    ReturnFromResult();
            }
        }

        private async UniTaskVoid LoadAsync(SceneId target)
        {
            string targetName = _scenes.GetSceneName(target);

            if (string.IsNullOrEmpty(targetName))
            {
                Debug.LogError(
                    $"{nameof(SceneFlowController)}: {target} 의 씬이 SceneNameSO 에 지정되지 않았다.", this);
                return;
            }

            NetworkManager networkManager = NetworkManager.Singleton;

            // ShutdownInProgress 를 보지 않으면, 세션을 끊은 직후(아직 IsListening 이 true 인 프레임)의
            // 전환이 NGO 경로로 빠진다. NGO 는 씬 로드는 시작해 놓고 셧다운으로 이벤트를 버리므로
            // OnLoadEventCompleted 가 영원히 오지 않고, 이 전환은 IsLoading 에 박힌 채 끝나지 않는다.
            // → docs/architecture/pause-menu.md §5.3
            bool useNgo = SceneTransitionOperations.UsesNgo(networkManager);

            if (useNgo && !networkManager.IsServer)
            {
                // 클라이언트는 서버의 씬 이벤트를 따라갈 뿐 스스로 전환을 시작할 수 없다.
                Debug.LogWarning(
                    $"{nameof(SceneFlowController)}: 세션 중 클라이언트는 씬 전환을 시작할 수 없다. ({target})",
                    this);
                return;
            }

            IsLoading = true;

            Scene previousScene = _currentScene;
            List<Behaviour> suspended = SceneTransitionOperations.SuspendInput(previousScene);
            bool unloadFirst = UnloadsBeforeLoad(Current, target);

            try
            {
                if (unloadFirst && previousScene.IsValid() && previousScene.isLoaded)
                {
                    // 인게임 로비 ⇄ 스테이지: 두 씬의 설치 컴포넌트가 같은 서비스를 등록하므로 겹치면 충돌한다.
                    // 이전 씬을 먼저 내린다. 그동안은 Bootstrap 만 남는다(StageSessionFlow 가 플레이어를 이미 뺐다).
                    Scene bootstrap = SceneManager.GetSceneByName(_scenes.GetSceneName(SceneId.Bootstrap));
                    if (bootstrap.IsValid() && bootstrap.isLoaded)
                        SceneManager.SetActiveScene(bootstrap);
                    await _operations.UnloadAsync(networkManager, previousScene);
                }

                Scene loaded = useNgo
                    ? await _operations.LoadThroughNgoAsync(networkManager, targetName)
                    : await _operations.LoadLocallyAsync(targetName);

                if (!loaded.IsValid())
                {
                    SceneTransitionOperations.RestoreInput(suspended);
                    return;
                }

                SceneId previousId = Current;
                bool returnFromStage = (previousId.IsStage() || previousId == SceneId.Result)
                    && target == SceneId.Lobby;
                SceneManager.SetActiveScene(loaded);

                _currentScene = loaded;
                _currentLoadedByNgo = useNgo;
                Current = target;

                if (target.IsStage())
                {
                    _settlements.BeginStage();
                }

                if (!unloadFirst && previousScene.IsValid() && previousScene.isLoaded)
                    await _operations.UnloadAsync(networkManager, previousScene);

                SceneChanged?.Invoke(target);
                if (returnFromStage)
                    EndStageSessionInLobbyAsync(previousId).Forget();
            }
            catch (OperationCanceledException)
            {
                // 전환 도중 앱이 내려갔다. 복구할 것이 없다.
            }
            catch (Exception e)
            {
                Debug.LogError($"{nameof(SceneFlowController)}: {target} 전환 실패 — {e}", this);
                SceneTransitionOperations.RestoreInput(suspended);
            }
            finally
            {
                IsLoading = false;
            }
        }

        /// <summary>
        /// 새 씬을 올리기 전에 이전 씬을 내려야 하는가 — 둘 다 플레이어 서비스(스폰 위치·로컬 플레이어·정신력 팀·음성)를
        /// 등록하는 세션 씬(인게임 로비·스테이지)이면 그렇다. 겹쳐 올리면 두 번째 등록이 충돌한다(ADR-0018).
        /// </summary>
        public static bool UnloadsBeforeLoad(SceneId from, SceneId to) =>
            IsSessionScene(from) && IsSessionScene(to) && from != to;

        private static bool IsSessionScene(SceneId id) => id == SceneId.InGameLobby || id.IsStage();

        private async UniTaskVoid EndStageSessionInLobbyAsync(SceneId previousId)
        {
            const int maxFrames = 300;
            string previousName = _scenes.GetSceneName(previousId);
            try
            {
                for (int frame = 0; frame < maxFrames; frame++)
                {
                    Scene previous = SceneManager.GetSceneByName(previousName);
                    if (!IsLoading && (!previous.IsValid() || !previous.isLoaded))
                        break;
                    await UniTask.NextFrame(destroyCancellationToken);
                }

                if (Services.TryGet(out ISteamLobbyService lobby))
                    lobby.MarkGameEnded();

                if (!Services.TryGet(out IConnectionService connection) || !connection.IsRunning)
                    return;

                NetworkManager network = NetworkManager.Singleton;
                if (network != null && network.IsServer)
                {
                    // 클라이언트가 요청한 종료를 먼저 마치게 해 Steam 로비 멤버십을 보존한다.
                    for (int frame = 0; frame < maxFrames && network.ConnectedClients.Count > 1; frame++)
                        await UniTask.NextFrame(destroyCancellationToken);
                }

                if (connection.IsRunning)
                    connection.Disconnect(leaveLobby: false);
            }
            catch (OperationCanceledException)
            {
                // 종료 중에는 세션을 다시 조작하지 않는다.
            }
        }

        /// <summary>
        /// Bootstrap 외에 이미 올라와 있는 씬이 있으면 그것을 현재 씬으로 받아들인다.
        /// 에디터에서 Play From Bootstrap 을 끄고 특정 씬을 반복 수정할 때의 경로다.
        /// </summary>
        private void AdoptAlreadyLoadedScene()
        {
            string bootstrapName = _scenes.GetSceneName(SceneId.Bootstrap);

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);

                if (!scene.isLoaded || scene.name == bootstrapName)
                    continue;

                if (!_scenes.TryResolve(scene.name, out SceneId id))
                    continue;

                _currentScene = scene;
                _currentLoadedByNgo = false;
                Current = id;
                SceneManager.SetActiveScene(scene);

                Debug.Log(
                    $"{nameof(SceneFlowController)}: 이미 열려 있는 {id} 씬을 현재 씬으로 받아들였다. " +
                    "부팅 전환을 건너뛴다.", this);
                return;
            }

            Debug.LogWarning(
                $"{nameof(SceneFlowController)}: 씬이 2개 이상 열려 있지만 SceneNameSO 에서 " +
                "식별되는 게임플레이 씬이 없다. 부팅 전환을 건너뛴다.", this);
        }
    }
}
