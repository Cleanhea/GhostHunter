using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Sanity;
using Unity.Netcode;
using UnityEngine;

namespace GhostHunter.Systems.SceneFlow
{
    /// <summary>
    /// 세션을 유지한 채 인게임 로비 ⇄ 스테이지를 전환한다(ADR-0018). 서버(호스트)만 시작한다.
    /// 첫 출발은 Tutorial, 정상 종료·전멸 정산 이후 출발은 Stage1 이다.
    ///
    /// <para>순서: 플레이어 디스폰(이전 씬 서비스가 살아 있을 때 정리) → <see cref="ISceneFlow.Load"/>(세션 중이라
    /// NGO 씬 로드·이전 씬 언로드) → 새 씬 서비스가 등록된 뒤 접속자마다 플레이어 재스폰. 재스폰한 플레이어는
    /// <c>OnNetworkSpawn</c>에서 새 씬의 스폰 위치·서비스를 잡는다.</para>
    ///
    /// <para>스테이지 → Result 는 이 경로를 타지 않는다 — 정산 화면의 전원 음성이 스테이지의 플레이어를 그대로 쓴다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StageSessionFlow : MonoBehaviour, IStageSessionFlow
    {
        private ISceneFlow _sceneFlow;
        private bool _transitioning;
        private bool _tutorialCompleted;
        private SceneId _previousScene;

        public bool IsTransitioning => _transitioning;

        public bool CanControl
        {
            get
            {
                NetworkManager network = NetworkManager.Singleton;
                return !_transitioning && _sceneFlow != null && !_sceneFlow.IsLoading
                    && network != null && network.IsListening && network.IsServer
                    && !network.ShutdownInProgress && network.NetworkConfig.EnableSceneManagement;
            }
        }

        private void OnEnable()
        {
            if (_sceneFlow != null)
                _sceneFlow.SceneChanged += HandleSceneChanged;
        }

        private void OnDisable()
        {
            if (_sceneFlow != null)
                _sceneFlow.SceneChanged -= HandleSceneChanged;
        }

        public void Initialize(ISceneFlow sceneFlow)
        {
            if (_sceneFlow != null)
                _sceneFlow.SceneChanged -= HandleSceneChanged;
            _sceneFlow = sceneFlow;
            _previousScene = sceneFlow.Current;
            if (isActiveAndEnabled)
                _sceneFlow.SceneChanged += HandleSceneChanged;
        }

        private void HandleSceneChanged(SceneId scene)
        {
            if (scene is SceneId.Title or SceneId.Lobby)
                _tutorialCompleted = false;
            else if (_previousScene == SceneId.Tutorial && scene == SceneId.Result)
                _tutorialCompleted = true;
            _previousScene = scene;
        }

        public bool StartStage() =>
            CanControl && _sceneFlow.Current == SceneId.InGameLobby
            && Begin(_tutorialCompleted ? SceneId.Stage1 : SceneId.Tutorial);

        public bool ReturnToInGameLobby() =>
            CanControl && (_sceneFlow.Current.IsStage() || _sceneFlow.Current == SceneId.Result)
            && Begin(SceneId.InGameLobby);

        private bool Begin(SceneId target)
        {
            TransitionAsync(target).Forget();
            return true;
        }

        private async UniTaskVoid TransitionAsync(SceneId target)
        {
            _transitioning = true;
            NetworkManager network = NetworkManager.Singleton;
            try
            {
                DespawnPlayers(network);
                _sceneFlow.Load(target);

                // Load 는 요청일 뿐이다. 거부되면(전환 중·미배선) 곧바로 IsLoading 이 꺼진 채 남는다 —
                // 그때는 방금 디스폰한 플레이어를 지금 씬에 되살린다.
                if (!_sceneFlow.IsLoading && _sceneFlow.Current != target)
                {
                    Debug.LogWarning($"{nameof(StageSessionFlow)}: {target} 전환이 거부됐다. 플레이어를 되살린다.", this);
                    SpawnMissingPlayers(network);
                    return;
                }

                // 자체 시간 제한을 두지 않는다. 전환은 NGO LoadSceneTimeOut(늦은 게스트는 timedOut 으로 넘김)이나
                // 세션 종료로 반드시 끝난다. 그보다 짧게 끊으면 뒤늦게 끝난 전환에서 아무도 플레이어를 되살리지 않는다.
                await UniTask.WaitUntil(() => !_sceneFlow.IsLoading, cancellationToken: destroyCancellationToken);

                if (_sceneFlow.Current != target)
                    Debug.LogWarning($"{nameof(StageSessionFlow)}: {target} 전환이 실패했다. 플레이어를 지금 씬에 되살린다.", this);
                SpawnMissingPlayers(network);
            }
            catch (OperationCanceledException)
            {
                // 앱 종료 중이다.
            }
            finally
            {
                _transitioning = false;
            }
        }

        /// <summary>
        /// 서버 전용 — 접속자 전원의 플레이어 오브젝트를 디스폰·파괴한다.
        ///
        /// <para><b>죽은 플레이어부터</b> 뺀다. 정신력 팀은 등록 해제마다 "남은 인원이 전원 사망이면 전멸"을 판정하므로,
        /// 살아 있는 사람을 먼저 빼면 스테이지 나가기 도중 가짜 전멸 정산이 일어난다.</para>
        /// </summary>
        public static void DespawnPlayers(NetworkManager network)
        {
            if (network == null || !network.IsServer)
                return;

            DespawnPlayers(network, deadOnly: true);
            DespawnPlayers(network, deadOnly: false);
        }

        private static void DespawnPlayers(NetworkManager network, bool deadOnly)
        {
            var players = new List<NetworkObject>();
            foreach (NetworkClient client in network.ConnectedClientsList)
            {
                NetworkObject player = client.PlayerObject;
                if (player == null || !player.IsSpawned)
                    continue;
                if (deadOnly && (!player.TryGetComponent(out SanityNetworkState sanity) || sanity.HasSanity))
                    continue;
                players.Add(player);
            }

            foreach (NetworkObject player in players)
                player.Despawn(true);
        }

        /// <summary>
        /// 서버 전용 — 플레이어 오브젝트가 없는 접속자에게 활성 씬에 새 플레이어를 스폰한다.
        /// 위치는 각 플레이어의 <c>PlayerNetworkSpawn</c>이 새 씬의 스폰 레지스트리에서 정한다.
        /// </summary>
        public static int SpawnMissingPlayers(NetworkManager network)
        {
            if (network == null || !network.IsServer || !network.IsListening)
                return 0;

            GameObject prefab = network.NetworkConfig.PlayerPrefab;
            if (prefab == null || !prefab.TryGetComponent(out NetworkObject prefabObject))
            {
                Debug.LogError($"{nameof(StageSessionFlow)}: NetworkConfig.PlayerPrefab 이 없어 플레이어를 스폰할 수 없다.");
                return 0;
            }

            int spawned = 0;
            var clientIds = new List<ulong>(network.ConnectedClientsIds);
            foreach (ulong clientId in clientIds)
            {
                if (network.ConnectedClients.TryGetValue(clientId, out NetworkClient client)
                    && client.PlayerObject != null && client.PlayerObject.IsSpawned)
                    continue;

                prefabObject.InstantiateAndSpawn(network, clientId, destroyWithScene: false, isPlayerObject: true);
                spawned++;
            }

            return spawned;
        }
    }
}
