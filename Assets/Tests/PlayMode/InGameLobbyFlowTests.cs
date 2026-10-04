using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
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
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 실제 씬(Bootstrap → Title → Tutorial → 정산 → 인게임 로비 ⇄ Stage1)으로 세션 유지 흐름(ADR-0018·0019)을 끝까지 돌린다.
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
            _exceptions.Clear();
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
            connection.StartHostInGameScene(SceneId.InGameLobby);
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

            // 로컬 세션(Steam 방 아님)에서도 상점을 쓴다(2026-09-30) — 시작 $25, 호스트가 산다.
            IStageShopService shop = Services.Get<IStageShopService>();
            yield return WaitUntil(() => shop.IsAvailable, "로컬 세션에서 상점이 열리지 않았다");
            Assert.IsTrue(shop.CanManage, "로컬 호스트는 상점을 쓴다");
            Assert.AreEqual(StageShopRules.StartingBalance, shop.Balance, "시작 자금");
            ulong me = shop.LocalMemberKey;
            Assert.IsFalse(shop.GetMemberGear(me).HasLighter, "라이터는 사야 생긴다");
            Assert.IsTrue(shop.TryPurchase(ShopItem.IronLighter, me), "철제 라이터 구매");
            Assert.AreEqual(StageShopRules.StartingBalance - StageShopRules.PriceOf(ShopItem.IronLighter), shop.Balance);
            Assert.IsTrue(shop.GetMemberGear(me).HasLighter);
            Assert.IsFalse(shop.TryPurchase(ShopItem.IronDriver, me), "잔액이 모자라면 못 산다");

            Assert.IsTrue(stageFlow.StartStage());
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Tutorial);
            NetworkObject stagePlayer = LocalPlayer();
            Assert.AreNotEqual(lobbyPlayerId, stagePlayer.NetworkObjectId, "스테이지 씬에서 플레이어를 새로 스폰한다");
            Assert.AreEqual("Tutorial", stagePlayer.gameObject.scene.name, "첫 출발은 Tutorial 을 올린다");
            AssertTutorialContents(stagePlayer);
            yield return WaitUntil(() => stagePlayer.GetComponent<GhostHunter.Gameplay.Player.PlayerLighter>().IsOwned,
                "산 라이터가 스테이지 플레이어에게 오지 않았다");
            Assert.IsTrue(connection.IsRunning, "스테이지로 넘어가도 세션은 유지된다");
            Assert.IsFalse(SceneManager.GetSceneByName("InGameLobby").isLoaded, "인게임 로비는 내려간다");

            // 정상 종료처럼 정산(Result)으로 간다 — 플레이어는 정산 화면으로 옮겨진다(전원 음성).
            int balanceBeforeSettlement = shop.Balance;
            sceneFlow.RecordStageSettlement(new StageSettlementRecord(0, 0, 0, 1, 0, 0, false));
            Assert.AreEqual(balanceBeforeSettlement + StageShopRules.StageReward, shop.Balance, "한 판 보상 $50");
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
            AssertStage1Contents(LocalPlayer());
            yield return AssertStage1GhostAndCleaning(LocalPlayer().gameObject.scene);
            Assert.IsTrue(stageFlow.ReturnToInGameLobby(), "스테이지 나가기 — 인게임 로비로 돌아간다");
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            Assert.IsFalse(SceneManager.GetSceneByName("Stage1").isLoaded, "스테이지 씬은 내려간다");
            Assert.IsTrue(connection.IsRunning);

            Assert.IsEmpty(_exceptions, "예외:\n" + string.Join("\n---\n", _exceptions));
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator 첫_출발은_Tutorial이고_종료_후_Stage1로_진행한다()
        {
            yield return VerifyTutorialEnd(false);
        }

        [UnityTest, Timeout(400000)]
        public IEnumerator Tutorial에서_전멸해도_정산_후_Stage1로_진행한다()
        {
            yield return VerifyTutorialEnd(true);
        }

        private IEnumerator VerifyTutorialEnd(bool teamWiped)
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
            // 세션은 인게임 로비(드릴카 상점)에서 열리고, 첫 출발이 Tutorial 이다.
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            Assert.IsTrue(stageFlow.StartStage());
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Tutorial);
            AssertTutorialContents(LocalPlayer());

            if (!teamWiped)
            {
                // 정산 없이 나가면 다음 출발에도 Tutorial 을 플레이한다.
                Assert.IsTrue(stageFlow.ReturnToInGameLobby());
                yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
                Assert.IsTrue(stageFlow.StartStage());
                yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Tutorial);
            }

            for (int i = 0; i < 10; i++)
                yield return null;
            if (teamWiped)
                Assert.IsTrue(LocalPlayer().GetComponent<SanityNetworkState>().ServerMarkDead());
            else
                yield return EndStageThroughTerminal();

            yield return WaitUntil(() => sceneFlow.Current == SceneId.Result && !sceneFlow.IsLoading,
                "Tutorial 이 정상 종료·전멸 후 정산 화면으로 가지 못했다");
            Assert.AreEqual(1, sceneFlow.SettlementHistory.Count);
            Assert.AreEqual(teamWiped, sceneFlow.SettlementHistory[0].TeamWiped);
            Assert.IsTrue(connection.IsRunning, "정산 화면에서도 세션이 유지돼야 한다");
            yield return ClickResultReturn();
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            Assert.IsTrue(stageFlow.StartStage());
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Stage1);
            AssertStage1Contents(LocalPlayer());
            Assert.IsTrue(connection.IsRunning);
            Assert.IsEmpty(_exceptions, "예외:\n" + string.Join("\n---\n", _exceptions));
        }

        private static IEnumerator EndStageThroughTerminal()
        {
            StageExitInteractable terminal = Object.FindFirstObjectByType<StageExitInteractable>();
            DrillCarSafeZone zone = terminal.GetComponentInParent<DrillCarSafeZone>();
            Vector3 probe = terminal.transform.position - zone.transform.forward * 0.8f;
            Assert.IsTrue(Physics.Raycast(probe, Vector3.down, out RaycastHit floor, 4f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "종료 단말기 앞 실내 바닥이 있어야 한다");

            NetworkObject player = LocalPlayer();
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = floor.point + Vector3.up * 0.02f;
            controller.enabled = true;
            Physics.SyncTransforms();
            Assert.IsTrue(DrillCarSafeZone.Contains(player.transform.position),
                $"실내 바닥에 선 플레이어가 드릴카 안으로 판정돼야 한다 — 플레이어 {player.transform.position}, " +
                $"세이프 존 {zone.transform.position}, 크기 {zone.Size}");
            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo confirmation = typeof(PlayerInteractor).GetField("_confirmStageExit", flags);
            typeof(PlayerInteractor).GetField("_currentStageExit", flags).SetValue(interactor, terminal);
            MethodInfo open = typeof(PlayerInteractor).GetMethod("OpenStageExitConfirmation", flags);
            InputSettings.BackgroundBehavior previousBackground = InputSystem.settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                open.Invoke(interactor, null);
                yield return null;
                Assert.IsTrue((bool)confirmation.GetValue(interactor));
                Assert.AreEqual(CursorLockMode.None, Cursor.lockState);
                Vector2 cancel = new(Screen.width * 0.5f, Screen.height * 0.5f - 34f);
                InputSystem.QueueStateEvent(mouse, new MouseState { position = Vector2.zero }.WithButton(MouseButton.Left));
                yield return null;
                InputSystem.QueueStateEvent(mouse, new MouseState { position = cancel });
                yield return null;
                Assert.IsTrue((bool)confirmation.GetValue(interactor), "버튼 밖에서 눌러 안에서 놓는 드래그는 클릭이 아니다");
                yield return Click(mouse, cancel);
                Assert.IsFalse((bool)confirmation.GetValue(interactor), "새 Input System 클릭으로 취소창이 닫혀야 한다");
                Assert.AreEqual(SceneId.Tutorial, Services.Get<ISceneFlow>().Current);

                typeof(PlayerInteractor).GetField("_currentStageExit", flags).SetValue(interactor, terminal);
                open.Invoke(interactor, null);
                yield return null;
                Vector2 confirm = new(Screen.width * 0.5f, Screen.height * 0.5f + 7f);
                yield return Click(mouse, confirm);
                Assert.IsFalse((bool)confirmation.GetValue(interactor), "새 Input System 클릭으로 종료 확인창이 닫혀야 한다");
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousBehavior;
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
        }

        private static IEnumerator Click(Mouse mouse, Vector2 position)
        {
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position }.WithButton(MouseButton.Left));
            yield return null;
            InputSystem.QueueStateEvent(mouse, new MouseState { position = position });
            yield return null;
        }

        private static IEnumerator ClickResultReturn()
        {
            InputSettings.BackgroundBehavior previousBackground = InputSystem.settings.backgroundBehavior;
            InputSettings.EditorInputBehaviorInPlayMode previousBehavior = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode =
                InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                yield return null;
                yield return Click(mouse, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f - 110f));
            }
            finally
            {
                InputSystem.RemoveDevice(mouse);
                InputSystem.settings.editorInputBehaviorInPlayMode = previousBehavior;
                InputSystem.settings.backgroundBehavior = previousBackground;
            }
        }


        [UnityTest, Timeout(400000)]
        public IEnumerator Tutorial의_배치와_청소_반출_퀘스트가_실제_세션에서_진행된다()
        {
            // 테스트 러너는 SetUp 이후 로그 범위를 초기화하므로 테스트 본문에서도 적용한다.
            LogAssert.ignoreFailingMessages = true;
            ISceneFlow sceneFlow = Services.Get<ISceneFlow>();
            IStageSessionFlow stageFlow = Services.Get<IStageSessionFlow>();
            IConnectionService connection = Services.Get<IConnectionService>();
            foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                transport.SetConnectionData("127.0.0.1", TestPort, "127.0.0.1");
            connection.SetTransportMode(TransportMode.Local);
            connection.StartHost();
            // 세션은 인게임 로비(드릴카 상점)에서 열리고, 첫 출발이 Tutorial 이다.
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            Assert.IsTrue(stageFlow.StartStage());
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Tutorial);
            var furniture = Object.FindFirstObjectByType<GhostHunter.Gameplay.Map.FurnitureSpawnController>();
            CleaningController cleaning = Object.FindFirstObjectByType<CleaningController>();
            yield return WaitUntil(() => furniture.IsReady && cleaning.DirtyCount == 7, "원룸 배치가 준비되지 않았다");
            Assert.AreEqual(10, furniture.Items.Length);
            Assert.AreEqual(6, cleaning.TaskProgress.TotalFurniture);
            GhostPrototypeSpawner spawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            yield return WaitUntil(() => spawner.HasGhost, "베이직 귀신이 없다");
            Assert.IsTrue(NavMesh.SamplePosition(new Vector3(-3.325f, 3.57f, -4.9f), out var room, .5f, NavMesh.AllAreas));
            Assert.That(room.position.y, Is.InRange(3.4f, 3.8f));
            var ghostState = spawner.ActiveGhost.CaptureStageState();
            int kinds = 0;
            for (int i = 1; i <= 8; i++) if ((ghostState.PhenomenaPoolMask & (1u << i)) != 0) kinds++;
            Assert.AreEqual(3, kinds);
            spawner.DespawnGhost();
            yield return WalkTutorialRoom();
            var books = Array.Find(furniture.Items, item => item.PoolId == "TutorialBooks");
            Assert.Less(Quaternion.Angle(books.transform.rotation, Quaternion.identity), 5f, "책 더미가 시작부터 넘어지면 안 된다");

            // 네 후보 모두에서 책 더미가 다른 책상 소품에 밀려 넘어지지 않아야 한다.
            var propStates = new GhostHunter.Gameplay.Recovery.StageRecoverySnapshot.FurnitureState[furniture.Items.Length];
            for (int i = 0; i < furniture.Items.Length; i++)
            {
                propStates[i] = furniture.Items[i].CaptureStageState();
                if (!furniture.Items[i].IsAssignedWorkTarget) furniture.Items[i].ServerPark();
            }
            var lamp = Array.Find(furniture.Items, item => item.PoolId == "TutorialLamp");
            Assert.IsFalse(lamp.GetComponentInChildren<Light>().enabled);
            foreach (var point in furniture.Points)
                if (point.name.StartsWith("BluePropPoint", StringComparison.Ordinal))
                {
                    books.GetPlacement(point, out Pose pose, out _);
                    Assert.IsTrue(books.ServerPlace(pose, false));
                    yield return new WaitForSeconds(.8f);
                    Assert.Less(Quaternion.Angle(books.transform.rotation, Quaternion.identity), 5f, point.name);
                    Vector3 delta = books.transform.position - pose.position; delta.y = 0f;
                    Assert.Less(delta.magnitude, .05f, point.name);
                    books.ServerPark();
                }
            for (int i = 0; i < furniture.Items.Length; i++)
                if (!furniture.Items[i].IsAssignedWorkTarget)
                    Assert.IsTrue(furniture.Items[i].ServerRestoreStageState(propStates[i]));
            Assert.IsTrue(lamp.GetComponentInChildren<Light>().enabled);

            var mandatory = new Vector3[4];
            for (int i = 0; i < 4; i++) mandatory[i] = cleaning.Stains[i].transform.position;
            var layouts = new HashSet<string>();
            for (int reset = 0; reset < 12; reset++)
            {
                cleaning.ResetStains();
                Assert.AreEqual(7, cleaning.DirtyCount);
                var randomPlaces = new List<string>();
                for (int i = 0; i < 7; i++)
                {
                    if (i < 4) Assert.AreEqual(mandatory[i], cleaning.Stains[i].transform.position);
                    else randomPlaces.Add(cleaning.Stains[i].transform.position.ToString("F2"));
                }
                randomPlaces.Sort(); layouts.Add(string.Join("|", randomPlaces));
            }
            Assert.Greater(layouts.Count, 1);

            var chair = Array.Find(furniture.Items, item => item.PoolId == "TutorialChair");
            var launcher = chair.GetComponent<GhostHunter.Gameplay.Furniture.FurnitureLauncher>();
            Assert.IsFalse(launcher.HasLaunched);
            launcher.ServerLaunch(Vector3.forward, .1f, 1);
            Assert.IsTrue(launcher.HasLaunched);
            var launchState = chair.CaptureStageState();
            Assert.IsTrue(launchState.HasLaunched);
            launcher.ServerRestoreLaunchRecord(false);
            Assert.IsFalse(launcher.HasLaunched);
            Assert.IsTrue(chair.ServerRestoreStageState(launchState));
            Assert.IsTrue(launcher.HasLaunched);
            yield return new WaitForSeconds(.3f);
            var hud = Object.FindFirstObjectByType<GhostHunter.UI.TutorialHud>();
            // 처음엔 가운데에 기본 조작 카드가 뜬다. 닫으면 지금 단계(청소) 카드가 뜬다.
            Assert.IsTrue(Array.Exists(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true), text => text.text.Contains("기본 조작")));
            hud.Dismiss();
            yield return null;
            Assert.IsTrue(Array.Exists(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true), text => text.text.Contains("얼룩 청소")));

            // 실제 서버 기록을 닦기/반출로 갱신하고 HUD가 같은 기준을 읽는지 확인한다.
            foreach (CleaningStain stain in cleaning.Stains)
            {
                Assert.IsTrue(stain.ServerClean(stain.Revision));
                Assert.IsFalse(stain.ServerClean(stain.Revision));
            }
            Assert.AreEqual(7, cleaning.TaskProgress.CleanedStains);
            var delivery = Object.FindFirstObjectByType<GhostHunter.Gameplay.Map.FurnitureDeliveryZone>();
            foreach (var item in furniture.Items)
                if (item.IsAssignedWorkTarget)
                {
                    Assert.IsTrue(item.ServerPlace(new Pose(delivery.transform.position, Quaternion.identity), true));
                    Assert.IsTrue(item.ServerCompleteDelivery());
                    Assert.IsFalse(item.ServerCompleteDelivery());
                }
            yield return new WaitForSeconds(.3f);
            Assert.AreEqual(6, cleaning.TaskProgress.DeliveredFurniture);
            Assert.IsTrue(Array.Exists(hud.GetComponentsInChildren<UnityEngine.UI.Text>(true), text => text.text.Contains("목표 완료")),
                "청소 카드를 닫지 않았어도 팀이 다음 단계로 가면 새 카드로 바뀐다");
            Assert.IsEmpty(_exceptions);
        }

        private static IEnumerator WalkTutorialRoom()
        {
            NetworkObject player = LocalPlayer();
            var motor = player.GetComponent<GhostHunter.Gameplay.Player.PlayerMotor>();
            CharacterController body = player.GetComponent<CharacterController>();
            motor.ServerTeleport(new Vector3(2.12f, .04f, 2.289f), Quaternion.identity);
            yield return null;
            motor.enabled = false;
            Vector3[] points = {
                new(7.2f, 1.8f, 2.289f), new(7.2f, 1.8f, .389f),
                new(3.9f, 3.57f, .389f), new(3.9f, 3.57f, .85f),
                new(-1.625f, 3.57f, .85f), new(-1.625f, 3.57f, -1.15f),
            };
            foreach (Vector3 point in points)
            {
                for (int step = 0; step < 400; step++)
                {
                    Vector3 delta = point - player.transform.position; delta.y = 0f;
                    if (delta.magnitude < .08f) break;
                    body.Move(delta.normalized * Mathf.Min(.05f, delta.magnitude) + Vector3.down * .02f);
                    yield return null;
                }
                Vector3 remaining = point - player.transform.position; remaining.y = 0;
                Assert.Less(remaining.magnitude, .13f, $"통로 막힘 {point} → {player.transform.position}");
                Assert.That(player.transform.position.y, Is.InRange(point.y - .15f, point.y + .15f),
                    $"계단/발코니 지지 높이 {point} → {player.transform.position}");
            }
            motor.enabled = true;
        }


        private static void AssertTutorialContents(NetworkObject player)
        {
            Scene stage = player.gameObject.scene;
            Assert.AreEqual("Tutorial", stage.name);
            Assert.IsNotNull(FindRoot(stage, "TutorialMap"));
            Assert.IsNotNull(Object.FindFirstObjectByType<StageExitInteractable>());
            Assert.IsTrue(player.GetComponent<SanityNetworkState>().HasSanity);
            Assert.That(player.transform.position.x, Is.InRange(-4f, 4f));
            Assert.That(player.transform.position.z, Is.InRange(4f, 6f));
            DrillCarSafeZone car = Object.FindFirstObjectByType<DrillCarSafeZone>();
            Assert.IsNotNull(car);
            Assert.AreEqual(stage, car.gameObject.scene);
            Assert.Greater(car.transform.position.z, player.transform.position.z);
            Assert.IsTrue(Services.TryGet(out GhostHunter.Gameplay.Lighting.IStageLightingDebug _));
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
            Assert.IsTrue(NavMesh.SamplePosition(ghostPosition, out NavMeshHit ghostOnMesh, 1f, NavMesh.AllAreas),
                "B안 집 NavMesh 가 구워져 귀신 발밑에 있다");
            // MAP-11: 계단으로 2층(바닥 3m)·다락(6m)까지 이어져 있어야 귀신이 층을 오간다.
            Assert.IsTrue(HasCompletePathToFloor(ghostOnMesh.position, 3f), "귀신 NavMesh 가 2층까지 이어진다");
            Assert.IsTrue(HasCompletePathToFloor(ghostOnMesh.position, 6f), "귀신 NavMesh 가 다락까지 이어진다");

            yield return WaitUntil(() => cleaning.DirtyCount > 0, "Stage1 에 청소 얼룩이 배치되지 않았다");

            // 청소 진행도 HUD 가 읽는 두 작업 — 시작 직후라 닦은 얼룩·반출한 가구는 0이다.
            yield return WaitUntil(() => cleaning.TaskProgress.TotalFurniture > 0,
                "Stage1 에 반출 목표 가구가 정해지지 않았다");
            CleaningTaskProgress progress = cleaning.TaskProgress;
            Assert.AreEqual(cleaning.DirtyCount, progress.TotalStains - progress.CleanedStains);
            Assert.AreEqual(0, progress.CleanedStains, "시작 직후 닦은 얼룩은 없다");
            Assert.AreEqual(0, progress.DeliveredFurniture, "시작 직후 반출한 가구는 없다");
            Assert.IsNotNull(Object.FindFirstObjectByType<GhostHunter.UI.CleaningProgressHud>(),
                "Stage1 에 청소 진행도 HUD 가 있다");

            // 부활 의식 — 문이 있는 방 하나를 의식 방으로 비우고 가운데에 소환진을 놓는다. 촛불은 상점 재고.
            GhostHunter.Gameplay.Revival.RevivalRitual ritual =
                Object.FindFirstObjectByType<GhostHunter.Gameplay.Revival.RevivalRitual>();
            Assert.IsNotNull(ritual, "Stage1 에 부활 의식이 있다");
            yield return WaitUntil(() => ritual.IsPlaced, "소환진이 놓이지 않았다");
            Assert.IsNotEmpty(ritual.RoomName, "소환진이 놓인 방");
            Vector3 center = ritual.Center;
            Assert.That(center.x > 52.67f && center.x < 82.67f && center.z > -12f && center.z < 12f,
                $"소환진은 B안 집 안에 선다 — {center}");
            Assert.AreEqual(Services.Get<IStageShopService>().CandleCount, ritual.CandleStock,
                "로컬 세션도 상점의 촛불 재고를 쓴다");
            for (int i = 0; i < GhostHunter.Gameplay.Revival.RevivalRules.CandleCount; i++)
                Assert.AreEqual(GhostHunter.Gameplay.Revival.CandleSlotState.Empty, ritual.GetSlot(i));

            // 의식 방은 비어 있다 — 가구(고정·랜덤)도 얼룩도 없고, 마법진 둘레에 촛대 5개만 선다(2026-09-30).
            Assert.IsTrue(ritual.ContainsInRoom(center), "소환진은 의식 방 가운데에 있다");
            foreach (GhostHunter.Gameplay.Furniture.FurnitureNetworkPhysics item in
                     Object.FindObjectsByType<GhostHunter.Gameplay.Furniture.FurnitureNetworkPhysics>(
                         FindObjectsSortMode.None))
                Assert.IsFalse(item.IsAvailable && ritual.ContainsInRoom(item.transform.position),
                    $"의식 방({ritual.RoomName})에 가구가 남았다 — {item.name}");
            foreach (GhostHunter.Gameplay.Map.RandomFurnitureItem item in
                     Object.FindObjectsByType<GhostHunter.Gameplay.Map.RandomFurnitureItem>(FindObjectsSortMode.None))
                Assert.IsFalse(item.IsPresent && ritual.ContainsInRoom(item.transform.position),
                    $"의식 방({ritual.RoomName})에 랜덤 가구가 놓였다 — {item.name}");
            foreach (CleaningStain stain in Object.FindObjectsByType<CleaningStain>(FindObjectsSortMode.None))
                Assert.IsFalse(stain.IsPlaced && ritual.ContainsInRoom(stain.transform.position),
                    $"의식 방({ritual.RoomName})에 얼룩이 놓였다");
            Assert.AreEqual(GhostHunter.Gameplay.Revival.RevivalRules.CandleCount,
                Object.FindObjectsByType<GhostHunter.Gameplay.Revival.RevivalCandleSlot>(FindObjectsSortMode.None).Length,
                "촛대 5개");
        }

        /// <summary>B안 집 X/Z 안, 높이 floorY±0.4 의 NavMesh 정점 중 하나라도 완전한 경로로 닿는가(지붕 같은 끊긴 섬은 건너뛴다).</summary>
        private static bool HasCompletePathToFloor(Vector3 from, float floorY)
        {
            var path = new NavMeshPath();
            foreach (Vector3 vertex in NavMesh.CalculateTriangulation().vertices)
            {
                if (Mathf.Abs(vertex.y - floorY) > 0.4f
                    || vertex.x < 53.67f || vertex.x > 81.67f || vertex.z < -11f || vertex.z > 11f)
                {
                    continue;
                }

                if (NavMesh.CalculatePath(from, vertex, NavMesh.AllAreas, path)
                    && path.status == NavMeshPathStatus.PathComplete)
                {
                    return true;
                }
            }

            return false;
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
