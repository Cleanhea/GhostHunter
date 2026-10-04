using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Lighting;
using GhostHunter.Systems.Installers;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>Tutorial 맵의 텍스처·축·스케일·드릴카·조명 배선을 검사한다.</summary>
    public sealed class TutorialStructureTests
    {
        private Scene _scene;
        private bool _opened;
        private GameObject _map;

        [OneTimeSetUp]
        public void OpenScene()
        {
            _scene = SceneManager.GetSceneByPath("Assets/Scenes/Tutorial.unity");
            if (!_scene.isLoaded)
            {
                _scene = EditorSceneManager.OpenScene("Assets/Scenes/Tutorial.unity", OpenSceneMode.Additive);
                _opened = true;
            }
            _map = _scene.GetRootGameObjects().FirstOrDefault(x => x.name == "TutorialMap");
            Assert.IsNotNull(_map);
        }

        [OneTimeTearDown]
        public void CloseScene()
        {
            if (_opened) EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void 모델의_크기와_축을_유지하고_스케일은_1이다()
        {
            foreach (Transform t in _map.GetComponentsInChildren<Transform>(true))
                Assert.AreEqual(Vector3.one, t.localScale, t.name);
            Renderer lot = Find("Ground_Lot").GetComponent<Renderer>();
            Assert.That(lot.bounds.size.x, Is.InRange(20f, 21f));
            Assert.That(lot.bounds.size.z, Is.InRange(19f, 21f));
            Assert.Greater(Find("Door_202").GetComponent<Renderer>().bounds.size.y, 2f, "문이 세로로 서야 한다");
            Assert.Less(Find("Balcony_Floor").GetComponent<Renderer>().bounds.size.y, 0.2f, "발코니 바닥이 수평이어야 한다");
            Assert.Greater(Find("Stairs_Flight_Lower").GetComponent<Renderer>().bounds.min.y, -0.1f, "계단이 땅 아래에 있으면 안 된다");
            foreach (MeshFilter filter in _map.GetComponentsInChildren<MeshFilter>(true))
                Assert.AreSame(filter.sharedMesh, filter.GetComponent<MeshCollider>().sharedMesh, filter.name);
        }

        [Test]
        public void 모든_텍스처가_URP_머티리얼에_연결되고_데이터_맵은_선형이다()
        {
            var used = new HashSet<string>();
            foreach (Renderer renderer in _map.GetComponentsInChildren<Renderer>(true))
                foreach (Material material in renderer.sharedMaterials)
                {
                    Assert.IsNotNull(material, renderer.name);
                    Assert.AreEqual("Universal Render Pipeline/Lit", material.shader.name, material.name);
                    foreach (string property in material.GetTexturePropertyNames())
                    {
                        Texture texture = material.GetTexture(property);
                        if (texture != null) used.Add(AssetDatabase.GetAssetPath(texture));
                    }
                }
            string[] emissiveMaterials =
            {
                "M_Neon_Motel", "M_Neon_Sunset", "M_Neon_Palm", "M_Neon_Palm_2", "M_Neon_Yellow",
                "M_Bulb_On", "M_Ceiling_Bulb", "M_Room_Bulb_Warm", "M_Room_LampShade",
                "M_Room_VanityBulb", "M_Room_MsgLight", "M_Vend_Fluoro", "M_Vend_Header",
                "M_Vend_Promo", "M_Vend_LED_Green", "M_Vend_LED_Red",
            };
            foreach (string name in emissiveMaterials)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(
                    $"Assets/Mesh/TutorialMap/Materials/{name}.mat");
                Assert.IsNotNull(material, name);
                Assert.IsTrue(material.IsKeywordEnabled("_EMISSION"), name);
                Assert.IsFalse((material.globalIlluminationFlags & MaterialGlobalIlluminationFlags.EmissiveIsBlack) != 0, name);
                Assert.Greater(material.GetColor("_EmissionColor").maxColorComponent, 0f, name);
            }
            string[] paths = Directory.GetFiles("Assets/Mesh/TutorialMap/Textures", "*.png");
            Assert.AreEqual(268, paths.Length);
            foreach (string path in paths)
            {
                string assetPath = path.Replace('\\', '/');
                Assert.Contains(assetPath, used.ToList(), "미연결 텍스처");
                var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
                if (assetPath.EndsWith("_Normal.png", StringComparison.Ordinal))
                {
                    Assert.AreEqual(TextureImporterType.NormalMap, importer.textureType, path);
                    Assert.IsFalse(importer.sRGBTexture, path);
                }
                if (assetPath.EndsWith("_MetallicSmoothness.png", StringComparison.Ordinal))
                    Assert.IsFalse(importer.sRGBTexture, path);
            }
        }

        [Test]
        public void 드릴카는_정면에_서고_램프와_시작_지점은_모텔을_향한다()
        {
            GameObject car = _scene.GetRootGameObjects().Single(x => x.name == "DrillCar");
            // 2026-10-04 드릴카 1.4배 확대 — 램프 끝이 같은 자리(z 8.36)에 오도록 13 → 14.86 으로 뒤로 뺐다(stage-system.md §2.1).
            Assert.AreEqual(new Vector3(0f, 0f, 14.86f), car.transform.position);
            Assert.Greater(Vector3.Dot(car.transform.forward, Vector3.forward), 0.99f);
            DrillCarSafeZone zone = car.GetComponentInChildren<DrillCarSafeZone>(true);
            Assert.IsNotNull(zone);
            Assert.IsFalse(zone.PlacesBehindSpawnsAtRuntime);
            GameObject spawns = _scene.GetRootGameObjects().Single(x => x.name == "PlayerSpawnPoints");
            Assert.AreEqual(4, spawns.transform.childCount);
            foreach (Transform spawn in spawns.transform)
            {
                Assert.That(spawn.position.z, Is.InRange(4f, 6f));
                Assert.Less(Vector3.Dot(spawn.forward, Vector3.forward), -0.99f);
            }
            var hashes = new HashSet<uint>();
            foreach (NetworkObject network in _scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<NetworkObject>(true)))
            {
                uint hash = new SerializedObject(network).FindProperty("GlobalObjectIdHash").uintValue;
                Assert.AreNotEqual(0u, hash, network.name);
                Assert.IsTrue(hashes.Add(hash), network.name);
            }
        }

        [Test]
        public void Stage1_설정을_공유하고_모델의_여섯_조명에_배선한다()
        {
            StageLightingController controller = _scene.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<StageLightingController>(true)).Single();
            var settings = new SerializedObject(controller);
            Assert.AreSame(AssetDatabase.LoadAssetAtPath<StageLightingSettings>(
                "Assets/Settings/Gameplay/StageLightingSettings_Default.asset"),
                settings.FindProperty("_settings").objectReferenceValue);
            Assert.IsNotNull(settings.FindProperty("_sun").objectReferenceValue);
            Assert.AreEqual(6, controller.Lights.Count);
            Assert.AreEqual(2, controller.FloorCount);
            foreach (StageRoomLight light in controller.Lights)
            {
                Assert.IsNotNull(light);
                Assert.IsNotNull(new SerializedObject(light).FindProperty("_fixture").objectReferenceValue);
                Assert.IsFalse(light.SwitchOn, "Stage1 과 같이 실내등이 꺼진 상태로 시작한다");
            }
            GameInstaller installer = _scene.GetRootGameObjects()
                .SelectMany(x => x.GetComponentsInChildren<GameInstaller>(true)).Single();
            Assert.AreSame(controller, new SerializedObject(installer).FindProperty("_lighting").objectReferenceValue);
        }

        private Transform Find(string name) => _map.GetComponentsInChildren<Transform>(true).Single(x => x.name == name);
    }
}
