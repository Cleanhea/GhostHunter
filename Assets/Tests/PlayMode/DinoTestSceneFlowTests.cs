using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 박치기 공룡 시험장(DinoTest, 개발 전용 — 2026-11-09)을 실제 흐름(Bootstrap → Title → F1 HUD 와 같은
    /// <c>StartHostInGameScene(DinoTest)</c>)으로 열고, 시험장 구성과 어택 중 박치기 한 사이클이 도는지 본다.
    /// </summary>
    public sealed class DinoTestSceneFlowTests
    {
        private const ushort TestPort = 17796;
        private const float TimeoutSeconds = 120f;

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

            Scene cleanup = SceneManager.CreateScene($"{nameof(DinoTestSceneFlowTests)}Cleanup");
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
        public IEnumerator 시험장에서_세션을_열면_박치기_귀신이_어택_중_돌진하고_스턴한다()
        {
            ISceneFlow flow = Services.Get<ISceneFlow>();
            foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                transport.SetConnectionData("127.0.0.1", TestPort, "127.0.0.1");
            IConnectionService connection = Services.Get<IConnectionService>();
            connection.SetTransportMode(TransportMode.Local);
            connection.StartHostInGameScene(SceneId.DinoTest);

            yield return WaitUntil(() =>
            {
                NetworkManager network = NetworkManager.Singleton;
                NetworkObject player = network != null && network.IsListening ? network.LocalClient.PlayerObject : null;
                return flow.Current == SceneId.DinoTest && !flow.IsLoading && player != null && player.IsSpawned;
            }, $"DinoTest 에 플레이어가 스폰되지 않았다 (현재 {flow.Current})");
            Assert.IsTrue(SceneId.DinoTest.IsStage(), "시험장은 스테이지로 취급한다(정신력·사망)");

            // 구성 — 가구 5 · 닫힌 문 1, Tutorial 의 드릴카·청소는 없다.
            Scene stage = SceneManager.GetSceneByName("DinoTest");
            Assert.AreEqual(5, CountIn<FurnitureNetworkPhysics>(stage), "의자·낮은 상자 2·식탁·소파");
            Assert.AreEqual(1, CountIn<DoorInteractable>(stage), "복도 끝 문");
            Assert.AreEqual(0, CountIn<DrillCarSafeZone>(stage), "드릴카 없음 — 어디서나 귀신에게 노출");

            GhostPrototypeSpawner spawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            Assert.IsNotNull(spawner);
            yield return WaitUntil(() => spawner.ActiveGhost != null, "시험장에 귀신이 스폰되지 않았다");
            GhostPrototypeController ghost = spawner.ActiveGhost;
            var settings = (GhostPrototypeSettings)typeof(GhostPrototypeController)
                .GetField("_settings", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost);
            Assert.IsInstanceOf<GhostDinoSettings>(settings, "박치기 귀신(Ghost_Tutorial, 박치기 설정)을 쓴다");

            // 플레이어는 죽지 않게 보호해 둔다 — 전멸하면 정산으로 넘어가 시험이 끝난다. 귀신은 그대로 노리고 돌진한다.
            NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            playerObject.GetComponent<SanityNetworkState>().ServerGrantProtection(TimeoutSeconds);
            Services.Get<IGhostDebug>().ForceSpecialAttack();

            IGhostDebug debug = Services.Get<IGhostDebug>();
            float nextLog = 0f;
            yield return WaitUntil(() =>
            {
                if (Time.realtimeSinceStartup >= nextLog)
                {
                    nextLog = Time.realtimeSinceStartup + 5f;
                    object target = typeof(GhostPrototypeController)
                        .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost);
                    Debug.Log($"[DinoTestScene] stage {DinoStage(ghost)} ghost {ghost.transform.position} " +
                              $"player {playerObject.transform.position} target {(target != null ? "있음" : "없음")} " +
                              $"navmesh {UnityEngine.AI.NavMesh.CalculateTriangulation().vertices.Length}\n{debug.StatusSummary}\n{debug.LastStatus}");
                }
                return DinoStage(ghost) == GhostDinoCharge.Stage.Prepare;
            }, "어택 중 추격 8초 뒤 돌진 준비로 넘어가지 않았다");

            // 추격하던 귀신은 플레이어 바로 곁에 있다 — 그대로면 돌진 첫 스텝에 맞고 끝난다. 준비의 방향 추적 구간(앞 1.3초)에
            // 플레이어를 시험장 가운데 쪽 5m 앞으로 옮겨, 귀신이 그쪽으로 방향을 잡고 실제로 달려가게 한다.
            Vector3 toCenter = -ghost.transform.position;
            toCenter.y = 0f;
            Vector3 away = toCenter.sqrMagnitude > 1f ? toCenter.normalized : Vector3.forward;
            Vector3 destination = ghost.transform.position + away * 5f;
            destination.y = playerObject.transform.position.y;
            playerObject.GetComponent<GhostHunter.Gameplay.Player.PlayerMotor>()
                .Teleport(destination, playerObject.transform.rotation);
            Vector3 chargeStart = ghost.transform.position;

            yield return WaitUntil(() => DinoStage(ghost) == GhostDinoCharge.Stage.Stun,
                "돌진이 충돌로 끝나 스턴에 들어가지 않았다");
            float distance = (float)typeof(GhostPrototypeController)
                .GetField("_dinoChargeDistance", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost);
            Debug.Log($"[DinoTestScene] 돌진 {chargeStart} → {ghost.transform.position}, {distance:F2}m");
            Assert.Greater(distance, 3f, "옮긴 플레이어(5m)까지 실제로 달려가 부딪힌다");
            Assert.AreEqual(SceneId.DinoTest, flow.Current, "시험 중 씬이 바뀌지 않는다");
            CollectionAssert.IsEmpty(_exceptions);
        }

        /// <summary>
        /// 실제 플레이처럼 — 준비 동안 귀신이 제자리에 서 있고, 방향이 고정된 뒤 플레이어가 옆으로 피하면 따라 꺾지 않고
        /// 고정된 방향으로 곧게 달리는지 매 프레임 위치로 잰다(사용자 보고 2026-11-09 "준비동작 없고 직선이 아니다").
        /// </summary>
        [UnityTest, Timeout(400000)]
        public IEnumerator 준비_중에는_멈춰_서고_고정_뒤_피해도_직선으로_돌진한다()
        {
            ISceneFlow flow = Services.Get<ISceneFlow>();
            foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                transport.SetConnectionData("127.0.0.1", TestPort, "127.0.0.1");
            IConnectionService connection = Services.Get<IConnectionService>();
            connection.SetTransportMode(TransportMode.Local);
            connection.StartHostInGameScene(SceneId.DinoTest);
            yield return WaitUntil(() =>
            {
                NetworkManager network = NetworkManager.Singleton;
                NetworkObject player = network != null && network.IsListening ? network.LocalClient.PlayerObject : null;
                return flow.Current == SceneId.DinoTest && !flow.IsLoading && player != null && player.IsSpawned;
            }, "DinoTest 에 플레이어가 스폰되지 않았다");

            GhostPrototypeSpawner spawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            yield return WaitUntil(() => spawner.ActiveGhost != null, "귀신이 스폰되지 않았다");
            GhostPrototypeController ghost = spawner.ActiveGhost;
            NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            var motor = playerObject.GetComponent<GhostHunter.Gameplay.Player.PlayerMotor>();
            playerObject.GetComponent<SanityNetworkState>().ServerGrantProtection(TimeoutSeconds);
            Services.Get<IGhostDebug>().ForceSpecialAttack();

            yield return WaitUntil(() => DinoStage(ghost) == GhostDinoCharge.Stage.Prepare, "돌진 준비로 넘어가지 않았다");

            // 준비 시작 — 플레이어를 시험장 가운데 쪽 6m 앞에 둔다(앞 1.3초 동안 귀신이 그쪽으로 방향을 잡는다).
            Vector3 prepareAt = ghost.transform.position;
            Vector3 toCenter = -prepareAt;
            toCenter.y = 0f;
            Vector3 aim = toCenter.sqrMagnitude > 1f ? toCenter.normalized : Vector3.forward;
            Vector3 front = prepareAt + aim * 6f;
            front.y = playerObject.transform.position.y;
            motor.Teleport(front, playerObject.transform.rotation);

            float prepareMoved = 0f;
            float started = Time.time;
            while (DinoStage(ghost) == GhostDinoCharge.Stage.Prepare)
            {
                prepareMoved = Mathf.Max(prepareMoved, Horizontal(ghost.transform.position - prepareAt));
                // 방향 고정 구간(1.3초 뒤)에 들어가면 플레이어가 옆으로 4m 피한다.
                if (Time.time - started > 1.6f && Horizontal(playerObject.transform.position - front) < 0.5f)
                {
                    Vector3 side = Vector3.Cross(Vector3.up, aim) * 4f;
                    motor.Teleport(front + side, playerObject.transform.rotation);
                }
                yield return null;
            }

            Assert.Less(prepareMoved, 0.15f, $"돌진 준비 2초 동안 귀신은 제자리에 서 있어야 한다 — {prepareMoved:F2}m 움직임");

            // 돌진 — 고정된 방향(aim)에서 옆으로 얼마나 벗어나는지 잰다.
            Vector3 chargeStart = ghost.transform.position;
            Vector3 locked = (Vector3)typeof(GhostPrototypeController)
                .GetField("_dinoDirection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost);
            float maxLateral = 0f;
            float minForwardStep = float.MaxValue;
            Vector3 last = chargeStart;
            while (DinoStage(ghost) == GhostDinoCharge.Stage.Charge)
            {
                Vector3 offset = ghost.transform.position - chargeStart;
                offset.y = 0f;
                maxLateral = Mathf.Max(maxLateral, Vector3.Cross(locked, offset).magnitude);
                Vector3 step = ghost.transform.position - last;
                if (Horizontal(step) > 0.001f)
                    minForwardStep = Mathf.Min(minForwardStep, Vector3.Dot(step.normalized, locked));
                last = ghost.transform.position;
                yield return null;
            }

            float travelled = Horizontal(ghost.transform.position - chargeStart);
            Debug.Log($"[DinoTestScene] 준비 중 이동 {prepareMoved:F2}m, 고정 방향 {locked}, aim {aim}, " +
                      $"돌진 {travelled:F2}m, 옆 벗어남 최대 {maxLateral:F2}m, 스텝 방향 내적 최소 {minForwardStep:F2}, " +
                      $"끝 {DinoStage(ghost)} @ {ghost.transform.position}");
            Assert.Greater(Vector3.Dot(locked, aim), 0.95f, "방향은 준비 앞 구간의 타깃(정면) 쪽으로 고정된다");
            Assert.Greater(travelled, 2f, "피한 플레이어 대신 고정 방향으로 실제로 달린다");
            Assert.Less(maxLateral, 0.3f, "돌진은 고정 방향 직선이다 — 피한 플레이어 쪽으로 꺾이지 않는다");
            Assert.AreEqual(GhostDinoCharge.Stage.Stun, DinoStage(ghost));
            CollectionAssert.IsEmpty(_exceptions);
        }

        /// <summary>
        /// 보호 없이 플레이어가 시험장을 원으로 계속 도망칠 때(실제 플레이에 가까움) 단계 흐름과 움직임을 기록한다 —
        /// 준비에서 멈춰 서고, 돌진은 직선이다. 잡혀 죽으면 그 시점까지의 기록으로 실패 원인을 남긴다.
        /// </summary>
        [UnityTest, Timeout(400000)]
        public IEnumerator 원으로_도망치는_플레이어에게도_준비하고_직선으로_돌진한다()
        {
            ISceneFlow flow = Services.Get<ISceneFlow>();
            foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                transport.SetConnectionData("127.0.0.1", TestPort, "127.0.0.1");
            IConnectionService connection = Services.Get<IConnectionService>();
            connection.SetTransportMode(TransportMode.Local);
            connection.StartHostInGameScene(SceneId.DinoTest);
            yield return WaitUntil(() =>
            {
                NetworkManager network = NetworkManager.Singleton;
                NetworkObject player = network != null && network.IsListening ? network.LocalClient.PlayerObject : null;
                return flow.Current == SceneId.DinoTest && !flow.IsLoading && player != null && player.IsSpawned;
            }, "DinoTest 에 플레이어가 스폰되지 않았다");

            GhostPrototypeSpawner spawner = Object.FindFirstObjectByType<GhostPrototypeSpawner>();
            yield return WaitUntil(() => spawner.ActiveGhost != null, "귀신이 스폰되지 않았다");
            GhostPrototypeController ghost = spawner.ActiveGhost;
            NetworkObject playerObject = NetworkManager.Singleton.LocalClient.PlayerObject;
            var motor = playerObject.GetComponent<GhostHunter.Gameplay.Player.PlayerMotor>();
            var sanity = playerObject.GetComponent<SanityNetworkState>();
            Services.Get<IGhostDebug>().ForceSpecialAttack();

            var timeline = new System.Text.StringBuilder();
            GhostDinoCharge.Stage stage = DinoStage(ghost);
            Vector3 stageStart = ghost.transform.position;
            float prepareMoved = -1f;
            float chargeLateral = -1f;
            float chargeTravel = 0f;
            Vector3 locked = Vector3.zero;
            float angle = 0f;
            float attackAt = -1f;
            bool hadTarget = false;
            timeline.Append($"{Time.time:F1}s 어택 강제\n");
            float deadline = Time.time + 45f;
            while (Time.time < deadline && sanity.HasSanity && flow.Current == SceneId.DinoTest && chargeLateral < 0f)
            {
                // 가운데를 중심으로 반지름 7m 원을 5m/s 로 돈다.
                angle += Time.deltaTime * 5f / 7f;
                motor.Teleport(new Vector3(Mathf.Cos(angle) * 7f, 0.05f, Mathf.Sin(angle) * 7f),
                    playerObject.transform.rotation);

                GhostDinoCharge.Stage now = DinoStage(ghost);
                if (ghost.Phase == GhostPhase.Attack && attackAt < 0f)
                {
                    attackAt = Time.time;
                    timeline.Append($"{Time.time:F1}s 어택 시작\n");
                }
                bool hasTarget = typeof(GhostPrototypeController)
                    .GetField("_target", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost) != null;
                if (hasTarget != hadTarget)
                {
                    timeline.Append($"{Time.time:F1}s 타깃 {(hasTarget ? "잡음" : "놓침")} ({ghost.PursuitSummary})\n");
                    hadTarget = hasTarget;
                }

                if (now != stage)
                {
                    if (now == GhostDinoCharge.Stage.Prepare)
                        prepareMoved = 0f;
                    timeline.Append($"{Time.time:F1}s {stage}→{now} ghost {ghost.transform.position} player {playerObject.transform.position}\n");
                    if (stage == GhostDinoCharge.Stage.Charge)
                    {
                        Vector3 offset = ghost.transform.position - stageStart;
                        offset.y = 0f;
                        chargeTravel = offset.magnitude;
                        chargeLateral = Vector3.Cross(locked, offset).magnitude;
                    }
                    stage = now;
                    stageStart = ghost.transform.position;
                }

                if (now == GhostDinoCharge.Stage.Prepare)
                    prepareMoved = Mathf.Max(prepareMoved, Horizontal(ghost.transform.position - stageStart));
                if (now == GhostDinoCharge.Stage.Charge)
                {
                    if (locked == Vector3.zero)
                        locked = (Vector3)typeof(GhostPrototypeController)
                            .GetField("_dinoDirection", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost);
                    Vector3 offset = ghost.transform.position - stageStart;
                    offset.y = 0f;
                    chargeTravel = offset.magnitude;
                }

                yield return null;
            }

            Debug.Log($"[DinoTestScene] 도망 — 준비 중 이동 {prepareMoved:F2}m, 돌진 {chargeTravel:F2}m, 옆 벗어남 {chargeLateral:F2}m, " +
                      $"생존 {sanity.HasSanity}\n{timeline}");
            Assert.GreaterOrEqual(prepareMoved, 0f, "돌진 준비 단계에 들어가야 한다\n" + timeline);
            Assert.Less(prepareMoved, 0.15f, "준비 중에는 제자리에 서 있다\n" + timeline);
            Assert.GreaterOrEqual(chargeLateral, 0f, "돌진이 끝나 스턴까지 가야 한다\n" + timeline);
            Assert.Less(chargeLateral, 0.3f, "돌진은 직선이다\n" + timeline);
            CollectionAssert.IsEmpty(_exceptions);
        }

        private static float Horizontal(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        private static GhostDinoCharge.Stage DinoStage(GhostPrototypeController ghost)
        {
            var variable = (NetworkVariable<byte>)typeof(GhostPrototypeController)
                .GetField("_dinoStage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ghost);
            return (GhostDinoCharge.Stage)variable.Value;
        }

        private static int CountIn<T>(Scene scene) where T : Component
        {
            int count = 0;
            foreach (T component in Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (component.gameObject.scene == scene)
                    count++;
            return count;
        }

        private IEnumerator WaitUntil(Func<bool> condition, string failure)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
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
