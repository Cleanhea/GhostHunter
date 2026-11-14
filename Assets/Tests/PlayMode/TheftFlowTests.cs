using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using GhostHunter.Core;
using GhostHunter.Core.Networking;
using GhostHunter.Core.Scenes;
using GhostHunter.Core.Steam;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Theft;
using GhostHunter.UI;
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
    /// 실제 세션(Bootstrap → 인게임 로비 → Tutorial)에서 귀중품을 훔친다(theft-system.md). 진짜 Player 프리팹과 Tutorial 의
    /// 임시 귀중품으로 서버 판정 → 귀중품 숨김 → 공동 잔액 즉시 가산 → 정신력 회복 → "+ n$"·테두리 → 정산 표시까지 본다.
    /// </summary>
    public sealed class TheftFlowTests
    {
        private const ushort TestPort = 17797;
        private const float SceneTimeoutSeconds = 120f;
        private static readonly int GlowIntensityId = Shader.PropertyToID("_GH_TheftGlowIntensity");

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
            Shader.SetGlobalFloat(GlowIntensityId, 0f);
            NetworkManager network = NetworkManager.Singleton;
            if (network != null && network.IsListening)
                network.Shutdown();
            for (int i = 0; i < 10; i++)
                yield return null;

            Scene cleanup = SceneManager.CreateScene($"{nameof(TheftFlowTests)}Cleanup");
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
        public IEnumerator Tutorial에서_귀중품을_훔치면_사라지고_잔액과_정신력이_오르고_정산에_따로_남는다()
        {
            LogAssert.ignoreFailingMessages = true;
            ISceneFlow sceneFlow = Services.Get<ISceneFlow>();
            IStageSessionFlow stageFlow = Services.Get<IStageSessionFlow>();
            foreach (UnityTransport transport in Object.FindObjectsByType<UnityTransport>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                transport.SetConnectionData("127.0.0.1", TestPort, "127.0.0.1");
            IConnectionService connection = Services.Get<IConnectionService>();
            connection.SetTransportMode(TransportMode.Local);
            connection.StartHost();
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.InGameLobby);
            Assert.IsTrue(stageFlow.StartStage());
            yield return WaitForPlayerIn(sceneFlow, stageFlow, SceneId.Tutorial);

            NetworkObject player = NetworkManager.Singleton.LocalClient.PlayerObject;
            PlayerTheft theft = player.GetComponent<PlayerTheft>();
            SanityNetworkState sanity = player.GetComponent<SanityNetworkState>();
            TheftHud hud = player.GetComponent<TheftHud>();
            Assert.IsNotNull(theft);
            Assert.IsNotNull(hud);
            Assert.IsTrue(Services.TryGet(out IStageTheftService service), "Tutorial 에 도둑질 서비스가 등록되지 않았다");

            Valuable[] valuables = Object.FindObjectsByType<Valuable>(FindObjectsSortMode.None)
                .Where(v => v.gameObject.scene == player.gameObject.scene || v.gameObject.scene.name == "Tutorial").ToArray();
            Assert.AreEqual(4, valuables.Length, "Tutorial 임시 귀중품 4개");
            Valuable target = valuables.OrderByDescending(v => v.Price).First();
            Valuable far = valuables.Where(v => v != target)
                .OrderByDescending(v => Vector3.Distance(v.transform.position, target.transform.position)).First();

            IStageShopService shop = Services.Get<IStageShopService>();
            int balance = shop.Balance;
            Assert.IsTrue(sanity.ServerSetSanity(50));
            yield return null;

            // 멀리서는 서버가 거절한다.
            Teleport(player, target.transform.position + new Vector3(0f, 0f, 1f) * 6f);
            Assert.IsFalse(theft.ServerTrySteal(target.Id), "조준 거리 밖의 도둑질은 거절해야 한다");
            Assert.IsFalse(target.IsStolen);

            // 귀중품 옆에 서서 조준하면 "E 훔치기" 안내가 뜬다.
            Vector3 stand = FindStandingSpot(target);
            Teleport(player, stand);
            PlayerInteractor interactor = player.GetComponent<PlayerInteractor>();
            Camera camera = player.GetComponentInChildren<Camera>();
            bool aimed = false;
            for (int i = 0; i < 30 && !aimed; i++)
            {
                camera.transform.rotation = Quaternion.LookRotation(
                    target.Collider.bounds.center - camera.transform.position, Vector3.up);
                yield return null;
                aimed = interactor.CurrentValuable == target;
            }
            Assert.IsTrue(aimed, "귀중품을 조준하면 PlayerInteractor 가 대상으로 잡아야 한다");
            yield return null;
            Assert.IsTrue(hud.IsPromptVisible, "조준 중에는 \"E 훔치기\" 안내가 보여야 한다");

            // 소유자 요청 → 서버 확정.
            theft.RequestSteal(target);
            yield return WaitUntil(() => target.IsStolen, "서버가 도둑질을 확정하지 않았다");
            yield return null;
            yield return null;

            int price = target.Price;
            Assert.AreEqual(10, price);
            Assert.AreEqual(balance + price, shop.Balance, "훔친 금액은 공동 잔액에 즉시 더해진다");
            Assert.AreEqual(50 + 15, sanity.Sanity, "$10 → 정신력 +15 (sanity-system.md §4.1)");
            Assert.AreEqual(price, service.StolenMoney);
            Assert.IsFalse(target.Collider.enabled, "훔친 귀중품은 사라진다(콜라이더)");
            Assert.IsFalse(target.GetComponent<MeshRenderer>().enabled, "훔친 귀중품은 사라진다(렌더러)");
            Assert.AreEqual("+ 10$", hud.PriceText);
            Assert.Greater(Shader.GetGlobalFloat(GlowIntensityId), 0f, "훔친 직후 테두리 빛이 켜져야 한다");
            yield return null;
            Assert.IsFalse(hud.IsPromptVisible, "훔친 귀중품에는 더 이상 안내가 뜨지 않는다");

            // 같은 귀중품은 다시 훔칠 수 없다(동시 입력도 서버가 먼저 처리한 하나만).
            Assert.IsFalse(theft.ServerTrySteal(target.Id));
            Assert.AreEqual(balance + price, shop.Balance);

            // 테두리는 2초 뒤 꺼진다.
            yield return new WaitForSeconds(2.3f);
            Assert.AreEqual(0f, Shader.GetGlobalFloat(GlowIntensityId), "2초 뒤 테두리가 꺼져야 한다");
            Assert.IsFalse(far.IsStolen);

            // 정산(Result)에는 훔친 금액이 따로 남고, 잔액에는 다시 더하지 않는다(기본 보상만 더해진다).
            int beforeSettlement = shop.Balance;
            int history = sceneFlow.SettlementHistory.Count;
            Services.Get<ISanityTeamService>().ServerEndStageByExit();
            yield return WaitUntil(() => sceneFlow.SettlementHistory.Count > history, "정산 기록이 남지 않았다");
            Assert.AreEqual(price, sceneFlow.SettlementHistory[sceneFlow.SettlementHistory.Count - 1].StolenMoney);
            Assert.AreEqual(beforeSettlement + StageShopRules.StageReward, shop.Balance,
                "정산은 훔친 금액을 다시 더하지 않는다");
            CollectionAssert.IsEmpty(_exceptions);
        }

        private static Vector3 FindStandingSpot(Valuable target)
        {
            Vector3 center = target.transform.position;
            foreach (Vector3 dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                         new Vector3(1f, 0f, 1f).normalized, new Vector3(-1f, 0f, 1f).normalized,
                         new Vector3(1f, 0f, -1f).normalized, new Vector3(-1f, 0f, -1f).normalized })
            {
                Vector3 feet = center + dir * 1.0f;
                bool blocked = Physics.CheckCapsule(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.0f, 0.28f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool floor = Physics.Raycast(feet + Vector3.up * 0.3f, Vector3.down, 0.6f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (!blocked && floor)
                    return feet + Vector3.up * 0.02f;
            }
            Assert.Fail($"{target.name} 옆에 설 자리가 없다");
            return center;
        }

        private static void Teleport(NetworkObject player, Vector3 position)
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            controller.enabled = false;
            player.transform.position = position;
            controller.enabled = true;
            Physics.SyncTransforms();
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

    /// <summary>회복 테두리 포스트프로세싱 패스가 실제로 화면 가장자리를 #c2fcf3 쪽으로 밝히는지 렌더해서 본다.</summary>
    public sealed class TheftRecoveryGlowRenderTests
    {
        private static readonly int IntensityId = Shader.PropertyToID("_GH_TheftGlowIntensity");
        private static readonly int ColorId = Shader.PropertyToID("_GH_TheftGlowColor");
        private static readonly int EdgeId = Shader.PropertyToID("_GH_TheftGlowEdge");

        [UnityTest]
        public IEnumerator 세기가_있으면_가장자리만_밝아지고_0이면_그대로다()
        {
            var cameraObject = new GameObject("TheftGlowTestCamera");
            var texture = new RenderTexture(256, 144, 24, RenderTextureFormat.ARGB32);
            try
            {
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                camera.cullingMask = 0;
                camera.targetTexture = texture;
                cameraObject.transform.position = new Vector3(0f, -500f, 0f);

                Shader.SetGlobalFloat(IntensityId, 0f);
                Color[] off = RenderAndRead(camera, texture);
                Shader.SetGlobalColor(ColorId, new Color(0xc2 / 255f, 0xfc / 255f, 0xf3 / 255f, 1f));
                Shader.SetGlobalFloat(EdgeId, 0.35f);
                Shader.SetGlobalFloat(IntensityId, 1f);
                Color[] on = RenderAndRead(camera, texture);
                yield return null;

                Color offEdge = off[0], onEdge = on[0], onCenter = on[1];
                Assert.Less(offEdge.grayscale, 0.05f, "세기 0 이면 패스가 그리지 않아 검은 배경 그대로");
                Assert.Greater(onEdge.grayscale, 0.5f, $"세기 1 이면 가장자리가 밝아져야 한다 (실제 {onEdge})");
                Assert.Greater(onEdge.g, onEdge.r, "#c2fcf3 처럼 초록·파랑이 빨강보다 밝다");
                Assert.Less(onCenter.grayscale, 0.05f, $"화면 가운데는 그대로여야 한다 (실제 {onCenter})");
                Assert.Less(on[2].grayscale, onEdge.grayscale, "모서리에서 안쪽으로 갈수록 옅어진다");
                Assert.Greater(on[2].grayscale, onCenter.grayscale, "폭 안쪽 중간에도 빛이 조금 있다");
            }
            finally
            {
                Shader.SetGlobalFloat(IntensityId, 0f);
                Object.Destroy(cameraObject);
                texture.Release();
                Object.Destroy(texture);
            }
        }

        /// <summary>[0] = 왼쪽 위 가장자리 근처, [1] = 가운데, [2] = 모서리에서 대각선으로 조금 안쪽.</summary>
        private static Color[] RenderAndRead(Camera camera, RenderTexture texture)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var read = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            read.Apply();
            RenderTexture.active = previous;
            Color edge = read.GetPixel(2, texture.height - 3);
            Color center = read.GetPixel(texture.width / 2, texture.height / 2);
            // 모서리에서 대각선으로 조금 안쪽(타원 거리 약 1.22) — 빛 폭 0.35 안쪽이라 옅게만 밝다.
            Color inner = read.GetPixel(Mathf.RoundToInt(texture.width * 0.07f), Mathf.RoundToInt(texture.height * 0.07f));
            Object.Destroy(read);
            return new[] { edge, center, inner };
        }
    }
}
