using System;
using System.Collections;
using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 실제 씬(Bootstrap → Title → 인게임 로비 ⇄ Stage1)으로 세션 유지 흐름(ADR-0018·0019)을 끝까지 돌린다.
    /// Build Settings·SceneNameSO 에 두 씬이 등록되어 있어야 한다.
    ///
    /// <para>Steam 이 없는 환경이라 Steam·음성 초기화 로그가 나온다 — 로그 실패는 끄고 예외만 모아 단언한다.</para>
    /// </summary>
    public sealed class InGameLobbyFlowTests
    {
        // 개발자 에디터가 7777 을 쥐고 있을 수 있다(플레이 중 재컴파일로 소켓이 샌 경우).
        private const ushort TestPort = 17795;
        private const float SceneTimeoutSeconds = 120f;

        private readonly List<string> _exceptions = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            LogAssert.ignoreFailingMessages = true;
            Application.logMessageReceived += CollectException;
            SceneManager.LoadScene(0, LoadSceneMode.Single);
            yield return WaitUntil(() => Services.TryGet(out ISceneFlow flow) && flow.Current == SceneId.Title,
                "Bootstrap 이 Title 을 올리지 못했다");
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Application.logMessageReceived -= CollectException;
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
                network.Shutdown();
            for (int i = 0; i < 10; i++)
                yield return null;

            Scene cleanup = SceneManager.CreateScene($"{nameof(InGameLobbyFlowTests)}Cleanup");
            SceneManager.SetActiveScene(cleanup);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene != cleanup && scene.isLoaded)
                    yield return SceneManager.UnloadSceneAsync(scene);
            }

            if (network != null)
                Object.Destroy(network.gameObject);
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator 인게임_로비에서_세션을_열고_스테이지를_오가며_플레이어를_다시_스폰한다()
        {
            LogAssert.ignoreFailingMessages = true;
            ISceneFlow sceneFlow = Services.Get<ISceneFlow>();
            IStageSessionFlow stageFlow = Services.Get<IStageSessionFlow>();
            IConnectionService connection = Services.Get<IConnectionService>();
            foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                transport.SetConnectionData("127.0.0.1", TestPort, "127.0.0.1");

            connection.SetTransportMode(TransportMode.Local);
            connection.StartHost();
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            NetworkObject lobbyPlayer = LocalPlayer();
            Assert.AreEqual("InGameLobby", lobbyPlayer.gameObject.scene.name, "세션은 인게임 로비에서 열린다");
            for (int i = 0; i < 10; i++)
                yield return null;
            Vector3 lobbyPosition = lobbyPlayer.transform.position;
            Assert.That(Mathf.Abs(lobbyPosition.x) < 5f && Mathf.Abs(lobbyPosition.z) < 4f,
                $"인게임 로비 방 안의 스폰 지점에 서야 한다 — {lobbyPosition}");
            Assert.IsTrue(stageFlow.CanControl, "호스트는 스테이지를 출발시킬 수 있다");
            ulong lobbyPlayerId = lobbyPlayer.NetworkObjectId;

            Assert.IsTrue(stageFlow.StartStage());
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Stage1);
            NetworkObject stagePlayer = LocalPlayer();
            Assert.AreNotEqual(lobbyPlayerId, stagePlayer.NetworkObjectId, "스테이지 씬에서 플레이어를 새로 스폰한다");
            Assert.AreEqual("Stage1", stagePlayer.gameObject.scene.name, "스테이지 출발은 Stage1 을 올린다");
            AssertStage1Contents(stagePlayer);
            yield return AssertStage1GhostAndCleaning(stagePlayer.gameObject.scene);
            Assert.IsTrue(connection.IsRunning, "스테이지로 넘어가도 세션은 유지된다");
            Assert.IsFalse(SceneManager.GetSceneByName("InGameLobby").isLoaded, "인게임 로비는 내려간다");

            // 정상 종료처럼 정산(Result)으로 간다 — 플레이어는 정산 화면으로 옮겨진다(전원 음성).
            sceneFlow.Load(SceneId.Result);
            yield return WaitUntil(() => sceneFlow.Current == SceneId.Result && !sceneFlow.IsLoading,
                "정산 화면으로 가지 못했다");
            Assert.IsTrue(connection.IsRunning, "정산 화면에서도 세션은 유지된다");

            Assert.IsTrue(stageFlow.ReturnToInGameLobby(), "정산 뒤 — 인게임 로비로 돌아간다");
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            NetworkObject returnedPlayer = LocalPlayer();
            Assert.AreNotEqual(stagePlayer.NetworkObjectId, returnedPlayer.NetworkObjectId);
            Assert.AreEqual("InGameLobby", returnedPlayer.gameObject.scene.name);
            Assert.IsTrue(connection.IsRunning, "인게임 로비로 돌아와도 세션은 유지된다");
            Assert.IsFalse(SceneManager.GetSceneByName("Result").isLoaded, "정산 씬은 내려간다");

            // 두 번째 스테이지 — 인게임 로비 ⇄ 스테이지가 반복된다. 이번에는 ESC '스테이지 나가기' 경로로 돌아온다.
            Assert.IsTrue(stageFlow.StartStage(), "다음 스테이지 출발");
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Stage1);
            Assert.IsTrue(stageFlow.ReturnToInGameLobby(), "스테이지 나가기 — 인게임 로비로 돌아간다");
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            Assert.IsFalse(SceneManager.GetSceneByName("Stage1").isLoaded, "스테이지 씬은 내려간다");
            Assert.IsTrue(connection.IsRunning);

            Assert.IsEmpty(_exceptions, "예외:\n" + string.Join("\n---\n", _exceptions));
        }

        private static NetworkObject LocalPlayer() => NetworkManager.Singleton.LocalClient.PlayerObject;

        /// <summary>Stage1 에 옮긴 B안·드릴카·조립 영역·정신력 UI 가 살아 있고 서로 이어졌는가.</summary>
        private static void AssertStage1Contents(NetworkObject stagePlayer)
        {
            Scene stage = stagePlayer.gameObject.scene;
            Assert.IsNotNull(FindRoot(stage, "House_Prototype_PlanB"), "B안 집");
            Assert.IsNotNull(FindRoot(stage, "SanityWorldMonitor"), "정신력 UI(월드 모니터)");
            Assert.IsTrue(Services.TryGet(out ISanityTeamService _), "Stage1 이 정신력 팀 서비스를 등록한다");
            Assert.IsTrue(stagePlayer.TryGetComponent(out SanityNetworkState sanity) && sanity.HasSanity,
                "스테이지 플레이어는 정신력을 가진 채 스폰된다");

            DrillCarSafeZone drillCar = Object.FindFirstObjectByType<DrillCarSafeZone>();
            Assert.IsNotNull(drillCar, "드릴카 안전 구역");
            Assert.AreEqual(stage, drillCar.gameObject.scene);
            FurnitureAssemblyZone assembly = Object.FindFirstObjectByType<FurnitureAssemblyZone>();
            Assert.IsNotNull(assembly, "조립 영역");
            Assert.AreEqual(stage, assembly.gameObject.scene);

            // 플레이어는 B안 앞마당(x 64~71, z -14 부근)에 서고, 드릴카는 그 뒤 땅으로 옮겨진다(GameInstaller).
            Vector3 position = stagePlayer.transform.position;
            Assert.That(position.x > 55f && position.x < 80f && position.z > -20f && position.z < -8f,
                $"B안 앞마당 스폰 지점에 서야 한다 — {position}");
            Assert.Less(drillCar.transform.position.z, position.z, "드릴카는 스폰 줄 뒤(남쪽)에 선다");
            Assert.IsNotNull(Object.FindFirstObjectByType<StageExitInteractable>(), "드릴카 종료 단말기가 생긴다");
        }

        /// <summary>Stage1 의 기본 귀신이 B안 집 NavMesh 위에 스폰되고, 청소 얼룩이 배치되는가.</summary>
        private IEnumerator AssertStage1GhostAndCleaning(Scene stage)
        {
            Assert.IsTrue(Services.TryGet(out ICleaningService cleaning), "Stage1 이 청소 서비스를 등록한다");
            Assert.IsTrue(Services.TryGet(out IGhostDebug _), "Stage1 이 귀신 서비스를 등록한다");
            GhostPrototypeSpawner spawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            Assert.IsNotNull(spawner, "귀신 스포너");
            Assert.AreEqual(stage, spawner.gameObject.scene);

            yield return WaitUntil(() => spawner.ActiveGhost != null, "Stage1 에 기본 귀신이 스폰되지 않았다");
            GhostPrototypeController ghost = spawner.ActiveGhost;
            Assert.AreEqual(stage, ghost.gameObject.scene, "귀신은 스테이지 씬과 함께 내려가야 한다");
            Vector3 ghostPosition = ghost.transform.position;
            Assert.That(ghostPosition.x > 52.67f && ghostPosition.x < 82.67f
                    && ghostPosition.z > -12f && ghostPosition.z < 12f,
                $"귀신은 B안 집 안에 선다 — {ghostPosition}");
            Assert.IsTrue(NavMesh.SamplePosition(ghostPosition, out _, 1f, NavMesh.AllAreas),
                "B안 집 NavMesh 가 구워져 귀신 발밑에 있다");

            yield return WaitUntil(() => cleaning.DirtyCount > 0, "Stage1 에 청소 얼룩이 배치되지 않았다");
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root;
            return null;
        }

        private IEnumerator WaitForPlayerIn(ISceneFlow sceneFlow, IStageSessionFlow stageFlow, SceneId scene)
        {
            yield return WaitUntil(() =>
            {
                NetworkManager network = NetworkManager.Singleton;
                NetworkObject player = network != null && network.IsListening ? network.LocalClient.PlayerObject : null;
                return sceneFlow.Current == scene && !sceneFlow.IsLoading && !stageFlow.IsTransitioning
                    && player != null && player.IsSpawned;
            }, $"{scene} 에 플레이어가 스폰되지 않았다 (현재 {sceneFlow.Current})");
        }

        private IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            float deadline = Time.realtimeSinceStartup + SceneTimeoutSeconds;
            while (!condition())
            {
                if (Time.realtimeSinceStartup > deadline)
                    Assert.Fail(failure + (_exceptions.Count > 0 ? "\n예외:\n" + string.Join("\n---\n", _exceptions) : string.Empty));
                yield return null;
            }
        }

        private void CollectException(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception)
                _exceptions.Add(condition + "\n" + stackTrace);
        }
    }
}
