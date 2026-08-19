using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GhostHunter.Core;
using GhostHunter.DebugTools;
using GhostHunter.Furniture;
using GhostHunter.Interaction;
using GhostHunter.Map;
using GhostHunter.Networking;
using GhostHunter.Player;
using GhostHunter.UI;
using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 문서의 M0~M7 프로토타입을 에디터 API로 재현한다. 씬/프리팹 YAML을 손으로 고치지 않고,
    /// 같은 메뉴를 다시 실행해도 동일한 생성물 경로와 GUID를 유지하도록 만든다.
    /// </summary>
    public static class PrototypeSceneSetup
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";
        internal const string TitleScenePath = "Assets/Scenes/Title.unity";
        internal const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        internal const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";
        internal const string ResultScenePath = "Assets/Scenes/Result.unity";
        internal const string NetworkRigPrefabPath = "Assets/Prefabs/NetworkRig.prefab";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string MoveSettingsPath = "Assets/Settings/Gameplay/PlayerMoveSettings_Default.asset";
        private const string ThrowSettingsPath = "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset";
        private const string LightDefinitionPath = "Assets/Settings/Gameplay/FurnitureDefinition_Light.asset";
        private const string HeavyDefinitionPath = "Assets/Settings/Gameplay/FurnitureDefinition_Heavy.asset";

        /// <summary>
        /// 맵 가구가 그 역할을 대신하면서 사라진 개발용 더미들. 재생성할 때 같이 지운다.
        /// </summary>
        private static readonly string[] ObsoleteAssetPaths =
        {
            "Assets/Prefabs/Furniture_Light_Cube.prefab",
            "Assets/Prefabs/Furniture_Heavy_Cube.prefab",
            "Assets/Settings/Gameplay/FurnitureDefinition_LightCube.asset",
            "Assets/Settings/Gameplay/FurnitureDefinition_HeavyCube.asset",
            "Assets/Materials/Furniture_Light.mat",
            "Assets/Materials/Furniture_Heavy.mat",
        };
        private const string OutlineMaterialPath = "Assets/Materials/Furniture_Outline.mat";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string MapWallMaterialPath = "Assets/Materials/Map_Wall.mat";

        [InitializeOnLoadMethod]
        private static void SetupOpenEditorWhenMissing()
        {
            if (Application.isBatchMode
                || (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null
                    && AssetDatabase.LoadAssetAtPath<Material>(MapWallMaterialPath) != null))
            {
                return;
            }

            EditorApplication.delayCall += RunDeferredSetup;
        }

        private static void RunDeferredSetup()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunDeferredSetup;
                return;
            }

            try
            {
                SetupPrototype();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("GhostHunter/프로토타입 게임 생성", priority = 1)]
        public static void SetupPrototype()
        {
            EnsureFolders();
            EnsureGameplayLayers();

            PlayerMoveSettings moveSettings = LoadOrCreateAsset<PlayerMoveSettings>(MoveSettingsPath);
            FurnitureThrowSettings throwSettings = LoadOrCreateAsset<FurnitureThrowSettings>(ThrowSettingsPath);
            FurnitureDefinition lightDefinition = CreateDefinition(
                LightDefinitionPath,
                8f,
                FurnitureWeightClass.Light);
            FurnitureDefinition heavyDefinition = CreateDefinition(
                HeavyDefinitionPath,
                25f,
                FurnitureWeightClass.Heavy);

            Material playerMaterial = CreateLitMaterial(
                "Assets/Materials/Player_Remote.mat",
                new Color(0.1f, 0.55f, 1f));
            Material mapWoodFloorMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Floor_Wood.mat",
                new Color(0.24f, 0.19f, 0.16f));
            Material mapTileFloorMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Floor_Tile.mat",
                new Color(0.3f, 0.37f, 0.4f));
            Material mapWallMaterial = CreateLitMaterial(
                MapWallMaterialPath,
                new Color(0.58f, 0.6f, 0.62f));
            Material mapTrimMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Trim.mat",
                new Color(0.075f, 0.085f, 0.095f));
            Material mapWoodMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Furniture_Wood.mat",
                new Color(0.3f, 0.19f, 0.11f));
            Material mapFabricMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Furniture_Fabric.mat",
                new Color(0.16f, 0.32f, 0.34f));
            Material mapBeddingMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Bedding.mat",
                new Color(0.72f, 0.7f, 0.64f));
            Material mapCeramicMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Ceramic.mat",
                new Color(0.78f, 0.82f, 0.82f));
            Material mapMetalMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Metal.mat",
                new Color(0.16f, 0.18f, 0.2f));
            Material mapWindowMaterial = CreateLitMaterial(
                "Assets/Materials/Map_Window.mat",
                new Color(0.22f, 0.5f, 0.62f));
            Material outlineMaterial = CreateOutlineMaterial();

            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
                throw new MissingReferenceException($"InputActionAsset을 찾지 못했습니다: {InputActionsPath}");

            GameObject playerPrefab = CreatePlayerPrefab(
                moveSettings,
                throwSettings,
                inputActions,
                playerMaterial);

            NetworkPrefabsList networkPrefabs = ConfigureNetworkPrefabs(playerPrefab);
            GameObject rigPrefab = CreateOrUpdateNetworkRigPrefab(playerPrefab, networkPrefabs);

            var palette = new HousePrototypeBuilder.Palette(
                mapWoodFloorMaterial,
                mapTileFloorMaterial,
                mapWallMaterial,
                mapTrimMaterial,
                mapWoodMaterial,
                mapFabricMaterial,
                mapBeddingMaterial,
                mapCeramicMaterial,
                mapMetalMaterial,
                mapWindowMaterial);
            var housePhysics = new HousePrototypeBuilder.PhysicsAssets(
                lightDefinition,
                heavyDefinition,
                throwSettings,
                outlineMaterial);

            CreatePrototypeScene(rigPrefab, palette, housePhysics);

            if (AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/Temp.cs") != null)
                AssetDatabase.DeleteAsset("Assets/Scripts/Temp.cs");

            DeleteObsoleteAssets();

            PlayerSettings.companyName = "GhostHunter";
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            FlushNetworkPrefabIdentity();
            ValidateGeneratedAssets();
            Debug.Log(
                "[PrototypeSceneSetup] Prototype 씬과 게임플레이 프리팹 생성 완료.\n" +
                "Prototype 씬을 열고 Play → 왼쪽 HUD에서 Local / Host를 누르면 즉시 플레이할 수 있습니다.");
        }

        /// <summary>-batchmode -executeMethod 진입점.</summary>
        public static void SetupBatch()
        {
            SetupPrototype();
        }

        [MenuItem("GhostHunter/Place Original Scale House Right", priority = 2)]
        public static void PlaceOriginalScaleHouseRight()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before placing the comparison house. " +
                    $"The active scene is '{scene.path}'.");
            }

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "House_01_OriginalScale_Right")
                {
                    Object.DestroyImmediate(root);
                    break;
                }
            }

            Material LoadMaterial(string path)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                    throw new MissingReferenceException($"Missing map material: {path}");
                return material;
            }

            var palette = new HousePrototypeBuilder.Palette(
                LoadMaterial("Assets/Materials/Map_Floor_Wood.mat"),
                LoadMaterial("Assets/Materials/Map_Floor_Tile.mat"),
                LoadMaterial(MapWallMaterialPath),
                LoadMaterial("Assets/Materials/Map_Trim.mat"),
                LoadMaterial("Assets/Materials/Map_Furniture_Wood.mat"),
                LoadMaterial("Assets/Materials/Map_Furniture_Fabric.mat"),
                LoadMaterial("Assets/Materials/Map_Bedding.mat"),
                LoadMaterial("Assets/Materials/Map_Ceramic.mat"),
                LoadMaterial("Assets/Materials/Map_Metal.mat"),
                LoadMaterial("Assets/Materials/Map_Window.mat"));

            Transform original = HousePrototypeBuilder.CreateOriginalScaleHouseRight(
                palette,
                LoadHousePhysicsAssets());
            HousePrototypeBuilder.ValidateFurnishedHouse(
                original,
                HousePrototypeBuilder.OriginalMapScale);

            // 새로 만든 가구는 R(리셋) 목록에도 넣어 준다. 지운 집의 가구는 참조가 끊겨 있다.
            RebindFurnitureResetter(scene, original);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            // 씬에 놓인 NetworkObject 는 저장·빌드목록 등록 뒤에야 해시가 정해진다.
            RefreshScenePlacedNetworkObjects();

            Debug.Log(
                "[PrototypeSceneSetup] Kept House_01 at x2 scale and placed furnished " +
                "House_01_OriginalScale_Right at x1 scale with a 3m gap.");
        }

        /// <summary>
        /// 이미 만들어져 있는 가구 공용 에셋을 읽어 온다. 도면 배율 비교용 집만 다시 놓을 때는
        /// 에셋을 새로 만들 이유가 없으므로, 없으면 전체 생성을 먼저 돌리라고 알려 준다.
        /// </summary>
        private static HousePrototypeBuilder.PhysicsAssets LoadHousePhysicsAssets()
        {
            T Require<T>(string path) where T : Object
            {
                T asset = AssetDatabase.LoadAssetAtPath<T>(path);
                if (asset == null)
                {
                    throw new MissingReferenceException(
                        $"{path} 이(가) 없습니다. 'GhostHunter > 프로토타입 게임 생성'을 먼저 실행하세요.");
                }

                return asset;
            }

            return new HousePrototypeBuilder.PhysicsAssets(
                Require<FurnitureDefinition>(LightDefinitionPath),
                Require<FurnitureDefinition>(HeavyDefinitionPath),
                Require<FurnitureThrowSettings>(ThrowSettingsPath),
                Require<Material>(OutlineMaterialPath));
        }

        /// <summary>
        /// 씬에 있는 <see cref="FurnitureResetter"/>의 목록을 지금 씬 상태로 다시 채운다.
        /// 라이브러리·방 프리셋·도면 배율 비교용 집의 가구가 모두 R 로 되돌아오게 한다.
        /// </summary>
        private static void RebindFurnitureResetter(Scene scene, Transform originalScaleHouse)
        {
            FurnitureResetter resetter = null;
            var roots = new List<Transform> { originalScaleHouse };

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (resetter == null)
                    resetter = root.GetComponentInChildren<FurnitureResetter>(true);

                if (root.name == "Furniture_Library" || root.name == "Room_Presets")
                    roots.Add(root.transform);
            }

            if (resetter == null)
                return;

            var bodies = new List<Object>();
            foreach (Transform root in roots)
            {
                foreach (FurnitureNetworkPhysics furniture in
                         root.GetComponentsInChildren<FurnitureNetworkPhysics>(true))
                {
                    bodies.Add(furniture.GetComponent<Rigidbody>());
                }
            }

            SetObjectArray(resetter, "_furniture", bodies.ToArray());
        }

        private static void DeleteObsoleteAssets()
        {
            foreach (string path in ObsoleteAssetPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<Object>(path) != null)
                    AssetDatabase.DeleteAsset(path);
            }
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets", "Shaders");
            EnsureFolder("Assets/Settings", "Gameplay");
        }

        private static void EnsureFolder(string parent, string child)
        {
            string path = $"{parent}/{child}";
            if (!AssetDatabase.IsValidFolder(path))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static void EnsureGameplayLayers()
        {
            Object tagManagerAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];
            var serialized = new SerializedObject(tagManagerAsset);
            SerializedProperty layers = serialized.FindProperty("layers");

            SetLayerIfMissing(layers, GameLayers.PlayerName, 8);
            SetLayerIfMissing(layers, GameLayers.FurnitureName, 9);
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetLayerIfMissing(SerializedProperty layers, string layerName, int preferredIndex)
        {
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName)
                    return;
            }

            int targetIndex = preferredIndex;
            if (!string.IsNullOrEmpty(layers.GetArrayElementAtIndex(targetIndex).stringValue))
            {
                targetIndex = -1;
                for (int i = 8; i < layers.arraySize; i++)
                {
                    if (string.IsNullOrEmpty(layers.GetArrayElementAtIndex(i).stringValue))
                    {
                        targetIndex = i;
                        break;
                    }
                }
            }

            if (targetIndex < 0)
                throw new System.InvalidOperationException($"'{layerName}' 레이어를 추가할 빈 슬롯이 없습니다.");

            layers.GetArrayElementAtIndex(targetIndex).stringValue = layerName;
        }

        private static T LoadOrCreateAsset<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static FurnitureDefinition CreateDefinition(
            string path,
            float mass,
            FurnitureWeightClass weightClass)
        {
            FurnitureDefinition definition = LoadOrCreateAsset<FurnitureDefinition>(path);
            var serialized = new SerializedObject(definition);
            serialized.FindProperty("_mass").floatValue = mass;
            serialized.FindProperty("_weightClass").enumValueIndex = (int)weightClass;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            return definition;
        }

        private static Material CreateLitMaterial(string path, Color color)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new MissingReferenceException("URP Lit 셰이더를 찾지 못했습니다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateOutlineMaterial()
        {
            const string path = OutlineMaterialPath;
            Shader shader = Shader.Find("GhostHunter/FurnitureOutline");
            if (shader == null)
                throw new MissingReferenceException(
                    "GhostHunter/FurnitureOutline 셰이더를 찾지 못했습니다. 임포트가 끝난 뒤 다시 실행하세요.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetFloat("_OutlineWidth", 0.045f);
            material.SetColor("_OutlineColor", Color.white);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject CreatePlayerPrefab(
            PlayerMoveSettings moveSettings,
            FurnitureThrowSettings throwSettings,
            InputActionAsset inputActions,
            Material playerMaterial)
        {
            var root = new GameObject("Player")
            {
                layer = LayerMask.NameToLayer(GameLayers.PlayerName),
            };

            root.AddComponent<NetworkObject>();
            root.AddComponent<ClientNetworkTransform>();

            CharacterController controller = root.AddComponent<CharacterController>();
            controller.height = 1.8f;
            controller.radius = 0.35f;
            controller.center = new Vector3(0f, 0.9f, 0f);
            controller.stepOffset = 0.3f;
            controller.skinWidth = 0.05f;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "RemoteBody";
            body.layer = root.layer;
            body.transform.SetParent(root.transform, false);
            body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            Renderer bodyRenderer = body.GetComponent<Renderer>();
            bodyRenderer.sharedMaterial = playerMaterial;

            var cameraPivot = new GameObject("CameraPivot");
            cameraPivot.transform.SetParent(root.transform, false);
            cameraPivot.transform.localPosition = new Vector3(0f, 1.65f, 0f);

            var cameraObject = new GameObject("PlayerCamera");
            cameraObject.tag = "MainCamera";
            cameraObject.transform.SetParent(cameraPivot.transform, false);
            Camera playerCamera = cameraObject.AddComponent<Camera>();
            playerCamera.nearClipPlane = 0.05f;
            playerCamera.fieldOfView = 75f;
            playerCamera.enabled = false;
            AudioListener listener = cameraObject.AddComponent<AudioListener>();
            listener.enabled = false;

            PlayerInputReader input = root.AddComponent<PlayerInputReader>();
            PlayerMotor motor = root.AddComponent<PlayerMotor>();
            PlayerLook look = root.AddComponent<PlayerLook>();
            PlayerVisuals visuals = root.AddComponent<PlayerVisuals>();
            FurnitureTargeter targeter = root.AddComponent<FurnitureTargeter>();
            GrabController grab = root.AddComponent<GrabController>();
            PlayerInteractor interactor = root.AddComponent<PlayerInteractor>();
            PlayerNetworkSpawn spawn = root.AddComponent<PlayerNetworkSpawn>();

            SetObjectReference(input, "_inputActions", inputActions);

            SetObjectReference(motor, "_settings", moveSettings);
            SetObjectReference(motor, "_input", input);

            SetObjectReference(look, "_settings", moveSettings);
            SetObjectReference(look, "_input", input);
            SetObjectReference(look, "_cameraPivot", cameraPivot.transform);
            SetObjectReference(look, "_playerCamera", playerCamera);
            SetObjectReference(look, "_audioListener", listener);

            SetObjectArray(visuals, "_bodyRenderers", new Object[] { bodyRenderer });

            SetObjectReference(targeter, "_camera", playerCamera);
            SetObjectReference(targeter, "_settings", throwSettings);

            SetObjectReference(grab, "_input", input);
            SetObjectReference(grab, "_targeter", targeter);
            SetObjectReference(grab, "_camera", playerCamera);
            SetObjectReference(grab, "_settings", throwSettings);

            SetObjectReference(interactor, "_input", input);
            SetObjectReference(interactor, "_camera", playerCamera);

            SetObjectReference(spawn, "_motor", motor);

            return SavePrefab(root, PlayerPrefabPath);
        }

        /// <summary>
        /// 임시 씬 오브젝트를 프리팹으로 저장하고, 저장된 에셋을 강제 재임포트한다.
        ///
        /// 재임포트가 필요한 이유: <see cref="NetworkObject"/>는 OnValidate 에서
        /// GlobalObjectId 로 GlobalObjectIdHash 를 계산하는데, SaveAsPrefabAsset 시점에는
        /// 원본이 아직 "씬 오브젝트"라 씬 기준 ID로 계산된다. 그 결과 프리팹마다 값이
        /// 겹치고 m_InScenePlaced 가 true 로 박혀, NGO 가 프리팹을 구분하지 못한다.
        /// ForceUpdate 임포트를 걸면 에셋 기준으로 OnValidate 가 다시 돌아 고유 해시가 잡힌다.
        /// </summary>
        private static GameObject SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        private static NetworkPrefabsList ConfigureNetworkPrefabs(params GameObject[] prefabs)
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
                AssetDatabase.CreateAsset(list, NetworkPrefabsPath);
            }

            while (list.PrefabList.Count > 0)
                list.Remove(list.PrefabList[list.PrefabList.Count - 1]);

            foreach (GameObject prefab in prefabs)
            {
                list.Add(new NetworkPrefab
                {
                    Override = NetworkPrefabOverride.None,
                    Prefab = prefab,
                });
            }

            EditorUtility.SetDirty(list);
            return list;
        }

        private static void CreatePrototypeScene(
            GameObject rigPrefab,
            HousePrototypeBuilder.Palette palette,
            HousePrototypeBuilder.PhysicsAssets housePhysics)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            Transform house = HousePrototypeBuilder.Create(palette);
            Transform originalScaleHouse =
                HousePrototypeBuilder.CreateOriginalScaleHouseRight(palette, housePhysics);
            Transform[] bedroomSlots = HousePrototypeBuilder.CreateBedroomSlots(house);
            Transform furnitureLibrary = HousePrototypeBuilder.CreateFurnitureLibrary(palette, housePhysics);
            Transform roomPresets = HousePrototypeBuilder.CreateBedroomPresets(palette, housePhysics);
            CreateOverviewCamera();
            Transform[] playerSpawns = CreatePlayerSpawns();
            HousePrototypeBuilder.ValidateLayout(
                house,
                furnitureLibrary,
                roomPresets,
                bedroomSlots,
                playerSpawns);
            HousePrototypeBuilder.ValidateFurnishedHouse(
                originalScaleHouse,
                HousePrototypeBuilder.OriginalMapScale);
            FurnitureResetter resetter = CreateFurnitureResetter(
                furnitureLibrary,
                roomPresets,
                originalScaleHouse);
            CreateRoomSlotAssigner(bedroomSlots, roomPresets, resetter);

            // 단독 플레이 진입점: 리그가 없으면 프리팹에서 만들고, HUD 로 즉시 Host/Join 한다.
            // 메뉴 흐름으로 들어온 경우에는 앞선 씬의 영속 리그가 있어 아무것도 하지 않는다.
            CreateNetworkBootstrap(
                rigPrefab,
                autoStartFromLobbyEvents: true,
                connectionHudVisible: true);

            var ui = new GameObject("PrototypeUI");
            ui.AddComponent<CrosshairUI>();
            ui.AddComponent<ChargeGaugeUI>();

            EditorSceneManager.SaveScene(scene, ScenePath);
            SyncBuildScenes();
            RefreshScenePlacedNetworkObjects();
        }

        /// <summary>
        /// 씬에 놓인 NetworkObject 의 GlobalObjectIdHash 를 확정한다.
        ///
        /// <see cref="NetworkObject"/>.OnValidate 는 (1) 씬이 저장되어 오브젝트에 영구 ID가 있고
        /// (2) 그 씬이 Build Settings 목록에 들어 있어야만(buildIndex >= 0) 해시를 계산하고
        /// in-scene placed 표시를 남긴다. 생성 중인 새 씬은 둘 다 아니라서 해시가 0으로 남는데,
        /// 그러면 클라이언트가 씬 오브젝트를 해시로 찾지 못하고, 0이 여럿이면 서로 충돌한다.
        /// 저장과 빌드 목록 등록이 끝난 다음 씬을 다시 열어 OnValidate 를 돌리고 한 번 더 저장한다.
        /// </summary>
        private static void RefreshScenePlacedNetworkObjects()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            MethodInfo onValidate = typeof(NetworkObject).GetMethod(
                "OnValidate",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (onValidate == null)
            {
                throw new MissingMethodException(
                    nameof(NetworkObject),
                    "OnValidate — NGO 버전이 바뀌었습니다. 씬 NetworkObject 해시 갱신 방법을 다시 확인하세요.");
            }

            foreach (NetworkObject networkObject in FindSceneNetworkObjects(scene))
            {
                onValidate.Invoke(networkObject, null);
                EditorUtility.SetDirty(networkObject);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScenePlacedNetworkObjects(scene);
        }

        private static List<NetworkObject> FindSceneNetworkObjects(Scene scene)
        {
            var found = new List<NetworkObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<NetworkObject>(true));
            return found;
        }

        /// <summary>
        /// 씬 오브젝트도 프리팹과 같은 이유로 검사한다: 해시가 0이거나 겹치면 NGO 는
        /// 에러 없이 "클라이언트에만 오브젝트가 없는" 식으로 조용히 깨진다.
        /// </summary>
        private static void ValidateScenePlacedNetworkObjects(Scene scene)
        {
            var seen = new Dictionary<uint, string>();

            foreach (NetworkObject networkObject in FindSceneNetworkObjects(scene))
            {
                var serialized = new SerializedObject(networkObject);
                uint hash = (uint)serialized.FindProperty("GlobalObjectIdHash").longValue;
                bool inScenePlaced = serialized.FindProperty("m_InScenePlaced").boolValue;
                string name = networkObject.name;

                if (hash == 0 || !inScenePlaced)
                {
                    throw new InvalidOperationException(
                        $"씬 오브젝트 '{name}' 의 NetworkObject 식별자가 확정되지 않았습니다 " +
                        $"(hash={hash}, inScenePlaced={inScenePlaced}). " +
                        $"{ScenePath} 가 Build Settings 에 등록되어 있는지 확인하세요.");
                }

                if (seen.TryGetValue(hash, out string other))
                {
                    throw new InvalidOperationException(
                        $"씬 오브젝트 '{name}' 와 '{other}' 의 GlobalObjectIdHash 가 {hash} 로 같습니다.");
                }

                seen.Add(hash, name);
            }
        }

        /// <summary>
        /// Build Settings 씬 목록을 실제 존재하는 씬으로 맞춘다. MainMenu 가 있으면
        /// 빌드의 시작 씬이 되도록 항상 맨 앞에 둔다.
        /// </summary>
        internal static void SyncBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>();

            foreach (string path in new[]
                     { BootstrapScenePath, TitleScenePath, LobbyScenePath, ScenePath, ResultScenePath })
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null)
                    scenes.Add(new EditorBuildSettingsScene(path, true));
            }

            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void CreateLighting()
        {
            var lightObject = new GameObject("Directional Light");
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.4f;
            light.color = new Color(1f, 0.94f, 0.86f);
            lightObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.34f, 0.45f);
            RenderSettings.ambientEquatorColor = new Color(0.12f, 0.15f, 0.2f);
            RenderSettings.ambientGroundColor = new Color(0.04f, 0.05f, 0.07f);
        }

        private static void CreateOverviewCamera()
        {
            var cameraObject = new GameObject("OverviewCamera")
            {
                tag = "MainCamera",
            };
            (Vector3 position, Vector3 lookAt) = HousePrototypeBuilder.OverviewCameraPose();
            cameraObject.transform.position = position;
            cameraObject.transform.LookAt(lookAt);

            Camera overviewCamera = cameraObject.AddComponent<Camera>();
            overviewCamera.fieldOfView = 55f;
            overviewCamera.clearFlags = CameraClearFlags.SolidColor;
            overviewCamera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
            AudioListener listener = cameraObject.AddComponent<AudioListener>();

            PrototypeSceneContext context = cameraObject.AddComponent<PrototypeSceneContext>();
            SetObjectReference(context, "_overviewCamera", overviewCamera);
            SetObjectReference(context, "_overviewAudioListener", listener);
        }

        /// <summary>거실 남쪽 스폰 지점. 자리는 집 외곽에서 역산한다.</summary>
        private static Transform[] CreatePlayerSpawns()
        {
            Vector3[] positions = HousePrototypeBuilder.PlayerSpawnPositions();
            var registryObject = new GameObject("PlayerSpawnPoints");
            PlayerSpawnRegistry registry = registryObject.AddComponent<PlayerSpawnRegistry>();

            var points = new Transform[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                points[i] = CreatePoint(
                    $"PlayerSpawn_{i}",
                    positions[i],
                    Quaternion.identity,
                    registryObject.transform);
            }

            SetObjectArray(registry, "_spawnPoints", points);
            return points;
        }

        /// <summary>
        /// 던져서 어질러진 가구를 R 로 되돌리는 개발용 도구. 참조는 여기서 직접 꽂아
        /// 런타임 탐색(FindObjectsByType)을 피한다.
        ///
        /// 씬을 다시 만들지 않고 손으로 가구를 붙여 넣었다면, 그 가구는 이 배열에 없으므로
        /// 인스펙터에서 직접 넣어야 R 로 되돌아온다.
        /// </summary>
        private static FurnitureResetter CreateFurnitureResetter(params Transform[] furnitureRoots)
        {
            var bodies = new List<Object>();
            foreach (Transform root in furnitureRoots)
            {
                foreach (FurnitureNetworkPhysics furniture in
                         root.GetComponentsInChildren<FurnitureNetworkPhysics>(true))
                {
                    bodies.Add(furniture.GetComponent<Rigidbody>());
                }
            }

            var resetterObject = new GameObject("FurnitureReset");
            FurnitureResetter resetter = resetterObject.AddComponent<FurnitureResetter>();
            SetObjectArray(resetter, "_furniture", bodies.ToArray());
            return resetter;
        }

        /// <summary>
        /// 세션이 시작될 때 침실 슬롯에 프리셋을 뽑아 넣는 오브젝트.
        /// 서버에서만 뽑으므로 씬에 놓인 <see cref="NetworkObject"/> 하나면 된다.
        /// </summary>
        private static void CreateRoomSlotAssigner(
            Transform[] slots,
            Transform presetsRoot,
            FurnitureResetter resetter)
        {
            var assignerObject = new GameObject("RoomSlotAssigner");
            assignerObject.AddComponent<NetworkObject>();
            RoomSlotAssigner assigner = assignerObject.AddComponent<RoomSlotAssigner>();

            SetObjectArray(assigner, "_slots", slots);
            SetObjectArray(assigner, "_pool", presetsRoot.GetComponentsInChildren<RoomPreset>(true));
            SetObjectReference(assigner, "_resetter", resetter);
        }

        private static Transform CreatePoint(
            string name,
            Vector3 position,
            Quaternion rotation,
            Transform parent)
        {
            var point = new GameObject(name);
            point.transform.SetParent(parent);
            point.transform.SetPositionAndRotation(position, rotation);
            return point.transform;
        }

        /// <summary>
        /// 영속 네트워크 리그를 프리팹으로 만든다. 씬에 직접 배치하지 않고
        /// <see cref="NetworkRigBootstrap"/> 이 "없을 때만" 생성한다 — 메뉴 흐름과
        /// 단독 플레이가 같은 리그를 공유하면서 NetworkManager 중복을 막기 위해서다.
        /// </summary>
        internal static GameObject CreateOrUpdateNetworkRigPrefab(
            GameObject playerPrefab,
            NetworkPrefabsList networkPrefabs)
        {
            var rig = new GameObject("NetworkRig");
            NetworkManager networkManager = rig.AddComponent<NetworkManager>();
            FacepunchTransport steamTransport = rig.AddComponent<FacepunchTransport>();
            UnityTransport localTransport = rig.AddComponent<UnityTransport>();
            rig.AddComponent<SteamLobbyManager>();
            ConnectionManager connection = rig.AddComponent<ConnectionManager>();
            rig.AddComponent<ConnectionHud>();
            rig.AddComponent<PrototypeRuntimeSmoke>();

            networkManager.NetworkConfig ??= new NetworkConfig();
            networkManager.NetworkConfig.NetworkTransport = localTransport;
            networkManager.NetworkConfig.EnableSceneManagement = true;
            networkManager.NetworkConfig.PlayerPrefab = playerPrefab;
            networkManager.NetworkConfig.Prefabs.NetworkPrefabsLists =
                new List<NetworkPrefabsList> { networkPrefabs };
            networkManager.LogLevel = LogLevel.Developer;

            SetObjectReference(connection, "_networkManager", networkManager);
            SetObjectReference(connection, "_steamTransport", steamTransport);
            SetObjectReference(connection, "_localTransport", localTransport);
            SetEnum(connection, "_transportMode", (int)TransportMode.Local);

            return SavePrefab(rig, NetworkRigPrefabPath);
        }

        /// <summary>메뉴 씬 생성 도구가 리그 프리팹을 요구할 때. 없으면 프로토타입 생성을 먼저 돌린다.</summary>
        internal static GameObject EnsureNetworkRigPrefab()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkRigPrefabPath);
            if (prefab != null)
                return prefab;

            SetupPrototype();

            prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkRigPrefabPath);
            if (prefab == null)
                throw new MissingReferenceException($"리그 프리팹 생성 실패: {NetworkRigPrefabPath}");

            return prefab;
        }

        /// <summary>현재 열린 씬에 리그 부트스트랩 오브젝트를 만든다.</summary>
        internal static void CreateNetworkBootstrap(
            GameObject rigPrefab,
            bool autoStartFromLobbyEvents,
            bool connectionHudVisible)
        {
            var bootstrapObject = new GameObject("NetworkBootstrap");
            NetworkRigBootstrap bootstrap = bootstrapObject.AddComponent<NetworkRigBootstrap>();

            SetObjectReference(bootstrap, "_rigPrefab", rigPrefab);
            SetBoolean(bootstrap, "_autoStartFromLobbyEvents", autoStartFromLobbyEvents);
            SetBoolean(bootstrap, "_connectionHudVisible", connectionHudVisible);
        }

        internal static void SetObjectReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        internal static void SetObjectArray(Object target, string propertyName, Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetEnum(Object target, string propertyName, int enumIndex)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.enumValueIndex = enumIndex;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        internal static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        internal static void SetFloat(Object target, string propertyName, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        internal static void SetBoolean(Object target, string propertyName, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                return;

            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void ValidateGeneratedAssets()
        {
            RequireAsset<GameObject>(PlayerPrefabPath);
            RequireAsset<GameObject>(NetworkRigPrefabPath);
            RequireAsset<SceneAsset>(ScenePath);

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (player.GetComponent<NetworkObject>() == null
                || player.GetComponent<PlayerMotor>() == null
                || player.GetComponent<GrabController>() == null
                || player.GetComponent<PlayerInteractor>() == null)
            {
                throw new MissingComponentException("Player 프리팹의 필수 컴포넌트가 빠졌습니다.");
            }

            ValidateNetworkPrefabIdentity();
        }

        /// <summary>
        /// 동적으로 스폰하는 네트워크 프리팹. 맵 가구는 씬에 놓이므로 여기 없고,
        /// <see cref="ValidateScenePlacedNetworkObjects"/> 가 대신 검사한다.
        /// </summary>
        private static readonly string[] NetworkPrefabPaths =
        {
            PlayerPrefabPath,
        };

        /// <summary>
        /// 재임포트로 고쳐진 GlobalObjectIdHash 를 디스크까지 내려보낸다.
        ///
        /// <see cref="SavePrefab"/> 의 ForceUpdate 임포트가 OnValidate 를 다시 돌려 값을
        /// 바로잡지만, 그 SetDirty 는 앞선 SaveAssets 보다 늦게 일어나서 메모리만 맞고
        /// 파일은 잘못된 값(세 프리팹이 같은 해시)이 남는다. 여기서 명시적으로 한 번 더
        /// 저장해야 커밋되는 .prefab 이 실제로 올바른 상태가 된다.
        /// </summary>
        private static void FlushNetworkPrefabIdentity()
        {
            foreach (string path in NetworkPrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                EditorUtility.SetDirty(prefab.GetComponent<NetworkObject>());
                EditorUtility.SetDirty(prefab);
            }

            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 네트워크 프리팹마다 GlobalObjectIdHash 가 0이 아니고 서로 겹치지 않는지 본다.
        /// 겹치면 NGO 가 스폰 시 프리팹을 구분하지 못하는데, 에러 없이 엉뚱한 것이
        /// 스폰되는 식으로 조용히 깨져서 여기서 잡지 않으면 알아채기 어렵다.
        /// </summary>
        private static void ValidateNetworkPrefabIdentity()
        {
            var seen = new Dictionary<uint, string>();

            foreach (string path in NetworkPrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var networkObject = prefab.GetComponent<NetworkObject>();

                // GlobalObjectIdHash 는 internal 이라 SerializedObject 로 읽는다.
                var serialized = new SerializedObject(networkObject);
                uint hash = (uint)serialized.FindProperty("GlobalObjectIdHash").longValue;
                bool inScenePlaced = serialized.FindProperty("m_InScenePlaced").boolValue;

                if (hash == 0)
                {
                    throw new InvalidOperationException(
                        $"{path} 의 GlobalObjectIdHash 가 0입니다. 프리팹을 재임포트해야 합니다.");
                }

                if (inScenePlaced)
                {
                    throw new InvalidOperationException(
                        $"{path} 이 in-scene placed 로 표시돼 있습니다. 프리팹 에셋은 false 여야 합니다.");
                }

                if (seen.TryGetValue(hash, out string other))
                {
                    throw new InvalidOperationException(
                        $"{path} 와 {other} 의 GlobalObjectIdHash 가 {hash} 로 같습니다. " +
                        "NGO 가 두 프리팹을 구분할 수 없습니다.");
                }

                seen.Add(hash, path);
            }
        }

        public static void BuildSmokeBatch()
        {
            const string buildFolder = "Build/Smoke";
            const string executablePath = buildFolder + "/GhostHunter.exe";

            Directory.CreateDirectory(buildFolder);

            BuildReport report = BuildPipeline.BuildPlayer(
                new[] { BootstrapScenePath, ScenePath },
                executablePath,
                BuildTarget.StandaloneWindows64,
                BuildOptions.Development);

            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"스모크 빌드 실패: {report.summary.result}, errors={report.summary.totalErrors}");
            }

            const string steamAppIdSource = "steam_appid.txt";
            string steamAppIdTarget = Path.Combine(buildFolder, "steam_appid.txt");
            if (File.Exists(steamAppIdSource))
                File.Copy(steamAppIdSource, steamAppIdTarget, true);

            Debug.Log(
                $"[PrototypeSceneSetup] Windows 스모크 빌드 완료: {executablePath} " +
                $"({report.summary.totalSize} bytes)");
        }

        private static void RequireAsset<T>(string path) where T : Object
        {
            if (AssetDatabase.LoadAssetAtPath<T>(path) == null)
                throw new MissingReferenceException($"생성 검증 실패: {path}");
        }
    }
}
