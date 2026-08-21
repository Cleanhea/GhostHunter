using System.Collections.Generic;
using System.IO;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Data.Scenes;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 프로젝트 설정과 에셋 배선을 지키는 검사. 여기서 잡는 실수들은 공통점이 있다 —
    /// <b>컴파일은 통과하고 플레이해야만 드러난다.</b> 레이어 누락은 "레이캐스트가 아무것도
    /// 안 맞음", 씬 목록 누락은 "로드 실패", 프리팹 해시 중복은 "엉뚱한 게 스폰됨"으로 나타난다.
    /// </summary>
    public sealed class ProjectWiringTests
    {
        private const string SceneCatalogPath = "Assets/Settings/Scenes/SceneNameSO.asset";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";

        private static readonly SceneId[] AllSceneIds =
        {
            SceneId.Bootstrap, SceneId.Title, SceneId.Lobby, SceneId.Game, SceneId.Result,
        };

        /// <summary>
        /// SceneNameSO 를 읽는다. 에셋 파일이 아예 없으면 실패시키고, 파일은 있는데
        /// 역직렬화가 안 되면 건너뛴다 — batchmode 에서는 프로젝트 스크립트가 클래스에
        /// 바인딩되지 않아 우리 ScriptableObject 를 열 수 없다 → workflow/testing.md §5.3
        /// </summary>
        private static SceneNameSO LoadCatalogOrIgnore()
        {
            Assert.IsTrue(
                File.Exists(SceneCatalogPath),
                $"{SceneCatalogPath} 파일이 없습니다.");

            var catalog = AssetDatabase.LoadAssetAtPath<SceneNameSO>(SceneCatalogPath);
            if (catalog == null)
            {
                Assert.Ignore(
                    "batchmode 에서는 프로젝트 스크립트가 바인딩되지 않아 " +
                    "SceneNameSO 를 역직렬화할 수 없습니다. 에디터 Test Runner 로 확인하세요.");
            }

            return catalog;
        }

        private static List<string> EnabledBuildSceneNames()
        {
            var names = new List<string>();
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    names.Add(Path.GetFileNameWithoutExtension(scene.path));
            }

            return names;
        }

        [Test]
        public void GameLayers_Player_레이어가_프로젝트에_있다()
        {
            Assert.GreaterOrEqual(GameLayers.Player, 0, "Project Settings > Tags and Layers 확인");
        }

        [Test]
        public void GameLayers_Furniture_레이어가_프로젝트에_있다()
        {
            Assert.GreaterOrEqual(GameLayers.Furniture, 0, "Project Settings > Tags and Layers 확인");
        }

        [Test]
        public void GameLayers_FurnitureMask_는_Furniture_레이어만_가리킨다()
        {
            Assert.AreEqual(1 << GameLayers.Furniture, GameLayers.FurnitureMask.value);
        }

        /// <summary>
        /// SceneId 를 추가하고 씬을 만들지 않거나 빌드 목록에 넣지 않는 실수를 잡는다.
        /// 에셋 역직렬화에 기대지 않으므로 batchmode 에서도 돈다.
        /// </summary>
        [Test]
        public void 모든_SceneId_에_대응하는_씬이_빌드_목록에_있다()
        {
            List<string> buildScenes = EnabledBuildSceneNames();

            foreach (SceneId id in AllSceneIds)
            {
                Assert.Contains(
                    id.ToString(),
                    buildScenes,
                    $"SceneId.{id} 에 대응하는 씬이 Build Settings 에 없습니다.");
            }
        }

        /// <summary>
        /// Bootstrap 은 전역 서비스를 등록하는 진입점이라 빌드 인덱스 0 이어야 한다.
        /// </summary>
        [Test]
        public void Bootstrap_이_빌드_목록의_첫_씬이다()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;

            Assert.Greater(scenes.Length, 0, "Build Settings 씬 목록이 비어 있습니다.");
            Assert.AreEqual(
                SceneId.Bootstrap.ToString(),
                Path.GetFileNameWithoutExtension(scenes[0].path));
        }

        [Test]
        public void 빌드_목록의_씬_파일이_전부_존재한다()
        {
            foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                Assert.IsTrue(File.Exists(scene.path), $"{scene.path} 파일이 없습니다.");
        }

        [Test]
        public void SceneNameSO_모든_SceneId_에_씬이_지정되어_있다()
        {
            SceneNameSO catalog = LoadCatalogOrIgnore();

            foreach (SceneId id in AllSceneIds)
                Assert.IsTrue(catalog.IsAssigned(id), $"SceneId.{id} 가 비어 있습니다.");
        }

        /// <summary>
        /// SceneNameSO 에만 있고 빌드 목록에 없는 씬은 런타임 로드 실패로만 드러난다.
        /// </summary>
        [Test]
        public void SceneNameSO_에_등록된_씬이_전부_빌드_목록에_있다()
        {
            SceneNameSO catalog = LoadCatalogOrIgnore();
            List<string> buildScenes = EnabledBuildSceneNames();

            foreach (SceneId id in AllSceneIds)
            {
                Assert.Contains(
                    catalog.GetSceneName(id),
                    buildScenes,
                    $"SceneId.{id} 가 Build Settings 에 없습니다.");
            }
        }

        [Test]
        public void SceneNameSO_씬_이름으로_SceneId_를_되찾을_수_있다()
        {
            SceneNameSO catalog = LoadCatalogOrIgnore();

            foreach (SceneId id in AllSceneIds)
            {
                Assert.IsTrue(
                    catalog.TryResolve(catalog.GetSceneName(id), out SceneId resolved),
                    $"SceneId.{id} 를 이름으로 되찾지 못했습니다.");
                Assert.AreEqual(id, resolved);
            }
        }

        /// <summary>
        /// 프리팹 에셋의 GlobalObjectIdHash 가 0 이거나 in-scene placed 로 박혀 있으면
        /// NGO 는 에러 없이 조용히 엉뚱한 것을 스폰한다 → conventions/unity-assets.md §5.2
        /// </summary>
        [Test]
        public void Player_프리팹의_네트워크_식별자가_확정되어_있다()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(prefab, $"{PlayerPrefabPath} 를 찾지 못했습니다.");

            var networkObject = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(networkObject, "Player 프리팹 루트에 NetworkObject 가 없습니다.");

            var serialized = new SerializedObject(networkObject);
            uint hash = (uint)serialized.FindProperty("GlobalObjectIdHash").longValue;
            bool inScenePlaced = serialized.FindProperty("m_InScenePlaced").boolValue;

            Assert.AreNotEqual(0u, hash, "GlobalObjectIdHash 가 0 입니다. 프리팹을 재임포트하세요.");
            Assert.IsFalse(inScenePlaced, "프리팹 에셋은 in-scene placed 가 아니어야 합니다.");
        }

        /// <summary>
        /// 동적으로 스폰되는 프리팹은 NetworkPrefabsList 에 있어야 한다.
        /// 빠지면 에러 없이 스폰이 실패한다 → architecture/networking.md
        /// </summary>
        [Test]
        public void Player_프리팹이_NetworkPrefabsList_에_등록되어_있다()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsNotNull(list, $"{NetworkPrefabsPath} 를 찾지 못했습니다.");

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            bool registered = false;
            foreach (NetworkPrefab entry in list.PrefabList)
            {
                if (entry != null && entry.Prefab == player)
                {
                    registered = true;
                    break;
                }
            }

            Assert.IsTrue(registered, "Player 프리팹이 NetworkPrefabsList 에 없습니다.");
        }
    }
}
