using System.Collections.Generic;
using System.IO;
using GhostHunter.Core;
using GhostHunter.Core.Scenes;
using GhostHunter.Data.Scenes;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Sanity;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

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
        private const string PlayerInputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string PlayerMoveSettingsPath =
            "Assets/Settings/Gameplay/PlayerMoveSettings_Default.asset";
        private const string SanitySettingsPath =
            "Assets/Settings/Gameplay/SanitySystemSettings_Default.asset";
        private const string GhostPrefabPath = "Assets/Prefabs/Ghost/Ghost_Prototype.prefab";
        private const string GhostSettingsPath =
            "Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset";
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

        /// <summary>
        /// 프리팹에 붙은 우리 컴포넌트를 가져온다. 컴포넌트가 안 잡히면 건너뛴다 —
        /// batchmode 에서는 프로젝트 스크립트가 클래스에 바인딩되지 않아 우리 MonoBehaviour 를
        /// 찾을 수 없다 → workflow/testing.md §5.3
        /// 패키지 어셈블리(NGO 등)나 파일 존재에 기대는 검사는 <b>이 호출보다 앞에</b> 두어야
        /// batchmode 에서도 계속 돈다.
        /// </summary>
        private static T GetProjectComponentOrIgnore<T>(GameObject prefab, string prefabPath)
            where T : Component
        {
            var component = prefab.GetComponent<T>();
            if (component == null)
            {
                Assert.Ignore(
                    $"batchmode 에서는 프로젝트 스크립트가 바인딩되지 않아 {prefabPath} 의 " +
                    $"{typeof(T).Name} 을 찾을 수 없습니다. 에디터 Test Runner 로 확인하세요.");
            }

            return component;
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
        public void GameLayers_GhostPrototype_레이어가_프로젝트에_있다()
        {
            Assert.GreaterOrEqual(GameLayers.GhostPrototype, 0, "Project Settings > Tags and Layers 확인");
        }

        [Test]
        public void GameLayers_FurnitureMask_는_Furniture_레이어만_가리킨다()
        {
            Assert.AreEqual(1 << GameLayers.Furniture, GameLayers.FurnitureMask.value);
        }

        [Test]
        public void GameLayers_NonGhostPrototypeRaycastMask_는_귀신을_제외한다()
        {
            int ghostMask = 1 << GameLayers.GhostPrototype;
            Assert.AreEqual(0, GameLayers.NonGhostPrototypeRaycastMask.value & ghostMask);
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

        [Test]
        public void Player_프리팹에_정신력_상태와_설정이_배선되어_있다()
        {
            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, $"{PlayerPrefabPath} 를 찾지 못했습니다.");
            Assert.IsTrue(
                File.Exists(SanitySettingsPath),
                $"{SanitySettingsPath} 파일이 없습니다.");

            SanityNetworkState state =
                GetProjectComponentOrIgnore<SanityNetworkState>(player, PlayerPrefabPath);

            var settings = AssetDatabase.LoadAssetAtPath<SanitySystemSettings>(SanitySettingsPath);
            Assert.IsNotNull(settings, $"{SanitySettingsPath} 를 찾지 못했습니다.");

            var serialized = new SerializedObject(state);
            Assert.AreEqual(
                settings,
                serialized.FindProperty("_settings").objectReferenceValue,
                "Player 정신력 설정 배선이 잘못됐습니다.");
        }

        [Test]
        public void Player_Crouch_액션은_C키에_바인딩되어_있다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(PlayerInputActionsPath);
            Assert.IsNotNull(actions, $"{PlayerInputActionsPath} 를 찾지 못했습니다.");

            InputAction crouch = actions.FindAction("Player/Crouch", false);
            Assert.IsNotNull(crouch, "Player/Crouch 액션이 없습니다.");

            bool hasKeyboardC = false;
            foreach (InputBinding binding in crouch.bindings)
            {
                if (binding.path == "<Keyboard>/c")
                {
                    hasKeyboardC = true;
                    break;
                }
            }

            Assert.IsTrue(hasKeyboardC, "Player/Crouch 액션에 C키가 바인딩되지 않았습니다.");
        }

        [Test]
        public void Player_굴착_액션은_R키에_바인딩되어_있다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(PlayerInputActionsPath);
            Assert.IsNotNull(actions, $"{PlayerInputActionsPath} 를 찾지 못했습니다.");

            InputAction burrow = actions.FindAction("Player/Burrow", false);
            Assert.IsNotNull(burrow, "Player/Burrow 액션이 없습니다.");

            bool hasKeyboardR = false;
            bool hasKeyboardE = false;
            foreach (InputBinding binding in burrow.bindings)
            {
                hasKeyboardR |= binding.path == "<Keyboard>/r";
                hasKeyboardE |= binding.path == "<Keyboard>/e";
            }

            Assert.IsTrue(hasKeyboardR, "Player/Burrow 액션에 R키가 바인딩되지 않았습니다.");
            Assert.IsFalse(hasKeyboardE, "Player/Burrow 액션에 이전 E키 바인딩이 남아 있습니다.");
        }

        [Test]
        public void Player_웅크리기_설정과_프리팹_참조가_배선되어_있다()
        {
            Assert.IsTrue(File.Exists(PlayerMoveSettingsPath), $"{PlayerMoveSettingsPath} 파일이 없습니다.");

            var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            Assert.IsNotNull(player, $"{PlayerPrefabPath} 를 찾지 못했습니다.");

            PlayerMotor motor = GetProjectComponentOrIgnore<PlayerMotor>(player, PlayerPrefabPath);
            var settings = AssetDatabase.LoadAssetAtPath<PlayerMoveSettings>(PlayerMoveSettingsPath);
            Assert.IsNotNull(settings, $"{PlayerMoveSettingsPath} 를 찾지 못했습니다.");

            Assert.AreEqual(3.5f, settings.CrouchMoveSpeed, 0.001f);
            Assert.AreEqual(1.8f, settings.StandingHeight, 0.001f);
            Assert.AreEqual(1.2f, settings.CrouchHeight, 0.001f);
            Assert.AreEqual(1.65f, settings.StandingCameraHeight, 0.001f);
            Assert.AreEqual(1.05f, settings.CrouchCameraHeight, 0.001f);

            var serialized = new SerializedObject(motor);
            Assert.AreEqual(
                player.transform.Find("CameraPivot"),
                serialized.FindProperty("_cameraPivot").objectReferenceValue,
                "PlayerMotor 의 CameraPivot 배선이 잘못됐습니다.");
            Assert.AreEqual(
                player.transform.Find("RemoteBody"),
                serialized.FindProperty("_visualBody").objectReferenceValue,
                "PlayerMotor 의 원격 몸통 배선이 잘못됐습니다.");
        }

        /// <summary>
        /// 귀신은 서버가 F1 로 동적 스폰하므로 프리팹 해시가 확정돼 있고 NetworkPrefabsList 에
        /// 있어야 한다. 빠지면 에러 없이 스폰이 실패한다 → architecture/networking.md
        /// </summary>
        [Test]
        public void Ghost_Prototype_프리팹의_네트워크_식별자가_확정되어_있다()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            Assert.IsNotNull(prefab, $"{GhostPrefabPath} 를 찾지 못했습니다. 'GhostHunter > 귀신 프로토타입 설치'를 실행하세요.");

            var networkObject = prefab.GetComponent<NetworkObject>();
            Assert.IsNotNull(networkObject, "Ghost_Prototype 루트에 NetworkObject 가 없습니다.");

            var serialized = new SerializedObject(networkObject);
            uint hash = (uint)serialized.FindProperty("GlobalObjectIdHash").longValue;
            bool inScenePlaced = serialized.FindProperty("m_InScenePlaced").boolValue;

            Assert.AreNotEqual(0u, hash, "GlobalObjectIdHash 가 0 입니다. 프리팹을 재임포트하세요.");
            Assert.IsFalse(inScenePlaced, "프리팹 에셋은 in-scene placed 가 아니어야 합니다.");
        }

        [Test]
        public void Ghost_Prototype_프리팹_전체가_GhostPrototype_레이어다()
        {
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            Assert.IsNotNull(ghost, $"{GhostPrefabPath} 를 찾지 못했습니다.");

            foreach (Transform child in ghost.GetComponentsInChildren<Transform>(true))
            {
                Assert.AreEqual(
                    GameLayers.GhostPrototype,
                    child.gameObject.layer,
                    $"{child.name} 이(가) {GameLayers.GhostPrototypeName} 레이어가 아닙니다.");
            }
        }

        [Test]
        public void Ghost_Prototype_프리팹이_NetworkPrefabsList_에_등록되어_있다()
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            Assert.IsNotNull(list, $"{NetworkPrefabsPath} 를 찾지 못했습니다.");

            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            Assert.IsNotNull(ghost, $"{GhostPrefabPath} 를 찾지 못했습니다.");

            bool registered = false;
            foreach (NetworkPrefab entry in list.PrefabList)
            {
                if (entry != null && entry.Prefab == ghost)
                {
                    registered = true;
                    break;
                }
            }

            Assert.IsTrue(registered, "Ghost_Prototype 프리팹이 NetworkPrefabsList 에 없습니다.");
        }

        [Test]
        public void Ghost_Prototype_프리팹에_컨트롤러와_설정이_배선되어_있다()
        {
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            Assert.IsNotNull(ghost, $"{GhostPrefabPath} 를 찾지 못했습니다.");
            Assert.IsTrue(File.Exists(GhostSettingsPath), $"{GhostSettingsPath} 파일이 없습니다.");

            GhostPrototypeController controller =
                GetProjectComponentOrIgnore<GhostPrototypeController>(ghost, GhostPrefabPath);
            GhostPrototypeSceneMarker sceneMarker =
                GetProjectComponentOrIgnore<GhostPrototypeSceneMarker>(ghost, GhostPrefabPath);
            Assert.IsNotNull(sceneMarker, "Ghost_Prototype 에 Scene 뷰 표식이 없습니다.");

            var settings = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(GhostSettingsPath);
            Assert.IsNotNull(settings, $"{GhostSettingsPath} 를 찾지 못했습니다.");

            var serialized = new SerializedObject(controller);
            Assert.AreEqual(
                settings,
                serialized.FindProperty("_settings").objectReferenceValue,
                "Ghost_Prototype 설정 배선이 잘못됐습니다.");
        }

        [Test]
        public void Ghost_소리탐지는_걷기6m와_달리기12m를_구분한다()
        {
            var ghostSettings = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(GhostSettingsPath);
            var moveSettings = AssetDatabase.LoadAssetAtPath<PlayerMoveSettings>(PlayerMoveSettingsPath);
            Assert.IsNotNull(ghostSettings, $"{GhostSettingsPath} 를 찾지 못했습니다.");
            Assert.IsNotNull(moveSettings, $"{PlayerMoveSettingsPath} 를 찾지 못했습니다.");

            Assert.AreEqual(6f, ghostSettings.WalkHearingRadius, 0.001f);
            Assert.AreEqual(12f, ghostSettings.RunHearingRadius, 0.001f);
            Assert.Less(moveSettings.MoveSpeed, ghostSettings.RunSpeedThreshold);
            Assert.GreaterOrEqual(
                moveSettings.MoveSpeed * moveSettings.SprintMultiplier,
                ghostSettings.RunSpeedThreshold);
        }
    }
}
