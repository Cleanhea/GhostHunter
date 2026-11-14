using System;
using System.Collections.Generic;
using System.Linq;
using GhostHunter.Core.Scenes;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using GhostHunter.Gameplay.Theft;
using GhostHunter.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>도둑질 시스템(theft-system.md)의 규칙·배선·임시 배치를 정적으로 검사한다.</summary>
    public sealed class TheftSystemTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string SanitySettingsPath = "Assets/Settings/Gameplay/SanitySystemSettings_Default.asset";

        [Test]
        public void 정산_기록은_훔친_금액을_싣고_옛_7칸_기록도_읽는다()
        {
            var original = new StageSettlementRecord(3, 5, 75, 1, 1, 2, false, 15);
            Assert.IsTrue(StageSettlementRecord.TryFromLobbyData(original.ToLobbyData(), out StageSettlementRecord restored));
            Assert.AreEqual(15, restored.StolenMoney);
            Assert.AreEqual(3, restored.DeliveredFurniture);

            Assert.IsTrue(StageSettlementRecord.TryFromLobbyData("3,5,75,1,1,2,0", out StageSettlementRecord legacy),
                "도둑질 이전에 방 데이터에 적힌 7칸 기록도 읽어야 한다");
            Assert.AreEqual(0, legacy.StolenMoney);
            Assert.IsFalse(StageSettlementRecord.TryFromLobbyData("3,5,75,1,1,2,0,-1", out _), "음수 금액은 손상 기록이다");
            Assert.IsFalse(StageSettlementRecord.TryFromLobbyData("3,5,75,1,1,2,0,1,9", out _));
        }

        [Test]
        public void 귀중품_가격은_모두_정신력_금액표에_있다()
        {
            var settings = AssetDatabase.LoadAssetAtPath<SanitySystemSettings>(SanitySettingsPath);
            Assert.IsNotNull(settings);
            foreach (ValuablePrice price in Enum.GetValues(typeof(ValuablePrice)))
                Assert.IsTrue(settings.TryGetValuableRestore((int)price, out int restore) && restore > 0,
                    $"${(int)price} 귀중품을 훔쳐도 정신력이 오르지 않는다 — 금액표(sanity-system.md §4.1)와 맞춰야 한다");
        }

        [Test]
        public void 테두리는_선형으로_차올랐다가_2초에_빠지고_가격은_튀어나와_떠오르며_사라진다()
        {
            var settings = ScriptableObject.CreateInstance<TheftSettings>();
            try
            {
                float fill = settings.GlowFillSeconds;
                Assert.AreEqual(2f, settings.GlowSeconds, "원문 2초");
                Assert.AreEqual(0f, settings.GlowIntensityAt(0f));
                Assert.AreEqual(0.5f, settings.GlowIntensityAt(fill * 0.5f), 1e-4f, "차오름은 선형");
                Assert.AreEqual(1f, settings.GlowIntensityAt(fill), 1e-4f);
                float mid = (fill + 2f) * 0.5f;
                Assert.AreEqual(0.5f, settings.GlowIntensityAt(mid), 1e-4f, "빠짐도 선형");
                Assert.AreEqual(0f, settings.GlowIntensityAt(2f));

                Assert.AreEqual(0f, settings.GlowWidthAt(0f));
                float startWidth = settings.GlowEdgeWidth * TheftSettings.GlowStartWidthRatio;
                Assert.AreEqual(startWidth, settings.GlowWidthAt(1e-5f), 1e-3f, "얇게 시작한다");
                Assert.AreEqual((startWidth + settings.GlowEdgeWidth) * 0.5f, settings.GlowWidthAt(fill * 0.5f), 1e-4f,
                    "빛은 가장자리에서 안쪽으로 선형으로 넓어진다");
                Assert.AreEqual(settings.GlowEdgeWidth, settings.GlowWidthAt(mid), 1e-4f, "빠지는 동안 폭은 유지");

                Assert.Greater(settings.PriceScaleAt(0f), 1f, "나타날 때 살짝 크다");
                Assert.AreEqual(1f, settings.PriceScaleAt(1f));
                Assert.AreEqual(0f, settings.PriceRiseAt(0f));
                Assert.Greater(settings.PriceRiseAt(1f), settings.PriceRiseAt(0.5f), "보이는 동안 위로 떠오른다");
                Assert.AreEqual(1f, settings.PriceAlphaAt(0.1f));
                Assert.AreEqual(0f, settings.PriceAlphaAt(10f));
                Assert.AreEqual(0f, settings.PriceAlphaAt(-1f), "아직 훔친 적이 없으면 가격을 띄우지 않는다");
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void 귀중품_번호는_씬과_위치로_정해지고_mm_안쪽_오차는_같다()
        {
            int a = Valuable.ComputeId("Tutorial", new Vector3(1.0001f, 2f, 3f));
            Assert.AreEqual(a, Valuable.ComputeId("Tutorial", new Vector3(1.0002f, 2f, 3f)));
            Assert.AreNotEqual(a, Valuable.ComputeId("Stage1", new Vector3(1f, 2f, 3f)));
            Assert.AreNotEqual(a, Valuable.ComputeId("Tutorial", new Vector3(1.01f, 2f, 3f)));
        }

        [Test]
        public void Player_프리팹에_도둑질과_HUD가_배선돼_있다()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            PlayerTheft theft = player.GetComponent<PlayerTheft>();
            Assert.IsNotNull(theft, "Player 에 PlayerTheft 가 없다");
            Assert.IsNotNull(theft.Settings, "PlayerTheft 설정 에셋이 비었다");
            var serialized = new SerializedObject(theft);
            Assert.AreEqual(player.GetComponent<SanityNetworkState>(),
                serialized.FindProperty("_sanity").objectReferenceValue);

            TheftHud hud = player.GetComponent<TheftHud>();
            Assert.IsNotNull(hud, "Player 에 TheftHud 가 없다");
            var hudSerialized = new SerializedObject(hud);
            Assert.AreEqual(theft, hudSerialized.FindProperty("_theft").objectReferenceValue);
            Assert.AreEqual(player.GetComponent<PlayerInteractor>(), hudSerialized.FindProperty("_interactor").objectReferenceValue);

            TheftSettings settings = theft.Settings;
            Assert.AreEqual(new Vector2(132f, 50f), settings.PromptSize, "원문 \"E 훔치기\" 132×50");
            Assert.AreEqual(378f, settings.PromptRightMargin);
            Assert.AreEqual(161f, settings.PromptBottomMargin);
            Assert.AreEqual("54D6AD", ColorUtility.ToHtmlStringRGB(settings.PriceColor));
            Assert.AreEqual("C2FCF3", ColorUtility.ToHtmlStringRGB(settings.GlowColor));
        }

        [TestCase("Assets/Settings/PC_Renderer.asset")]
        [TestCase("Assets/Settings/Mobile_Renderer.asset")]
        public void 렌더러에_회복_테두리_패스가_셰이더와_함께_켜져_있다(string rendererPath)
        {
            Object feature = AssetDatabase.LoadAllAssetsAtPath(rendererPath)
                .FirstOrDefault(asset => asset != null && asset.GetType().FullName == "GhostHunter.UI.TheftRecoveryGlowFeature");
            Assert.IsNotNull(feature, $"{rendererPath} 에 TheftRecoveryGlowFeature 가 없다");
            var serialized = new SerializedObject(feature);
            Assert.IsTrue(serialized.FindProperty("m_Active").boolValue);
            var shader = serialized.FindProperty("_shader").objectReferenceValue as Shader;
            Assert.IsNotNull(shader);
            Assert.AreEqual("Hidden/GhostHunter/TheftRecoveryGlow", shader.name);
            Assert.IsTrue(shader.isSupported, "테두리 셰이더가 컴파일되지 않았다");

            // 렌더러 목록에도 실제로 들어 있어야 한다(하위 에셋만 있고 목록에서 빠지면 그리지 않는다).
            var renderer = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(rendererPath));
            SerializedProperty list = renderer.FindProperty("m_RendererFeatures");
            bool listed = false;
            for (int i = 0; i < list.arraySize; i++)
                listed |= list.GetArrayElementAtIndex(i).objectReferenceValue == feature;
            Assert.IsTrue(listed, $"{rendererPath} 의 m_RendererFeatures 에 패스가 없다");
        }

        [TestCase("Assets/Scenes/Tutorial.unity")]
        [TestCase("Assets/Scenes/Stage1.unity")]
        public void 임시_귀중품은_고정된_바닥에_서로_다른_번호로_놓여_있다(string scenePath)
        {
            Scene scene = SceneManager.GetSceneByPath(scenePath);
            bool openedHere = !scene.isLoaded;
            if (openedHere)
                scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                Physics.SyncTransforms();
                List<Valuable> valuables = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Valuable>(true)).ToList();
                Assert.GreaterOrEqual(valuables.Count, 4, $"{scenePath} 에 임시 귀중품이 없다");
                CollectionAssert.AllItemsAreUnique(valuables.Select(v => Valuable.ComputeId(scene.name, v.transform.position)),
                    "같은 자리에 둔 귀중품은 번호가 겹친다");
                CollectionAssert.AreEquivalent(new[] { 2, 3, 5, 10 }, valuables.Select(v => v.Price).ToArray(),
                    "임시 배치는 네 가격을 하나씩 둔다");

                foreach (Valuable valuable in valuables)
                {
                    for (Transform t = valuable.transform; t != null; t = t.parent)
                        Assert.AreEqual(Vector3.one, t.localScale, $"{t.name} 스케일이 1이 아니다");
                    Assert.IsNull(valuable.GetComponentInParent<Rigidbody>(), $"{valuable.name} 이 움직이는 물체 아래에 있다");

                    // 바로 아래가 고정된(리지드바디 없는) 바닥이어야 한다 — 가구 위면 가구가 움직일 때 공중에 남는다.
                    Vector3 origin = valuable.transform.position + Vector3.up * 0.02f;
                    Assert.IsTrue(Physics.Raycast(origin, Vector3.down, out RaycastHit floor, 0.1f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), $"{valuable.name} 아래에 바닥이 없다");
                    Assert.IsNull(floor.collider.attachedRigidbody, $"{valuable.name} 이 {floor.collider.name}(움직이는 물체) 위에 있다");

                    // 다른 물체와 겹치지 않는다(바닥 면은 1cm 띄워서 뺀다).
                    Vector3 size = valuable.Size;
                    Collider[] overlaps = Physics.OverlapBox(
                        valuable.transform.TransformPoint(new Vector3(0f, size.y * 0.5f + 0.01f, 0f)),
                        new Vector3(size.x, size.y - 0.02f, size.z) * 0.5f, valuable.transform.rotation,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                    string[] blockers = overlaps.Where(c => c.GetComponentInParent<Valuable>() != valuable)
                        .Select(c => c.name).ToArray();
                    CollectionAssert.IsEmpty(blockers, $"{valuable.name} 이 다른 물체와 겹친다");
                }
            }
            finally
            {
                if (openedHere && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
                Physics.SyncTransforms();
            }
        }
    }
}
