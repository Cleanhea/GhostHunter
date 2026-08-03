using System;
using System.Collections.Generic;
using System.IO;
using GhostHunter.Core;
using GhostHunter.DebugTools;
using GhostHunter.Furniture;
using GhostHunter.Interaction;
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
        private const string ScenePath = "Assets/Scenes/Prototype.unity";
        internal const string MainMenuScenePath = "Assets/Scenes/MainMenu.unity";
        internal const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        internal const string NetworkRigPrefabPath = "Assets/Prefabs/NetworkRig.prefab";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const string LightFurniturePrefabPath = "Assets/Prefabs/Furniture_Light_Cube.prefab";
        private const string HeavyFurniturePrefabPath = "Assets/Prefabs/Furniture_Heavy_Cube.prefab";
        private const string MoveSettingsPath = "Assets/Settings/Gameplay/PlayerMoveSettings_Default.asset";
        private const string ThrowSettingsPath = "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset";
        private const string LightDefinitionPath = "Assets/Settings/Gameplay/FurnitureDefinition_LightCube.asset";
        private const string HeavyDefinitionPath = "Assets/Settings/Gameplay/FurnitureDefinition_HeavyCube.asset";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";

        [InitializeOnLoadMethod]
        private static void SetupOpenEditorWhenMissing()
        {
            if (Application.isBatchMode
                || AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
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
            Material lightFurnitureMaterial = CreateLitMaterial(
                "Assets/Materials/Furniture_Light.mat",
                new Color(0.12f, 0.72f, 0.78f));
            Material heavyFurnitureMaterial = CreateLitMaterial(
                "Assets/Materials/Furniture_Heavy.mat",
                new Color(0.85f, 0.32f, 0.18f));
            Material floorMaterial = CreateLitMaterial(
                "Assets/Materials/Arena_Floor.mat",
                new Color(0.12f, 0.14f, 0.18f));
            Material wallMaterial = CreateLitMaterial(
                "Assets/Materials/Arena_Wall.mat",
                new Color(0.35f, 0.39f, 0.48f));
            Material outlineMaterial = CreateOutlineMaterial();

            InputActionAsset inputActions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            if (inputActions == null)
                throw new MissingReferenceException($"InputActionAsset을 찾지 못했습니다: {InputActionsPath}");

            GameObject playerPrefab = CreatePlayerPrefab(
                moveSettings,
                throwSettings,
                inputActions,
                playerMaterial);
            GameObject lightFurniturePrefab = CreateFurniturePrefab(
                LightFurniturePrefabPath,
                "Furniture_Light_Cube",
                Vector3.one,
                lightDefinition,
                throwSettings,
                lightFurnitureMaterial,
                outlineMaterial);
            GameObject heavyFurniturePrefab = CreateFurniturePrefab(
                HeavyFurniturePrefabPath,
                "Furniture_Heavy_Cube",
                Vector3.one * 1.5f,
                heavyDefinition,
                throwSettings,
                heavyFurnitureMaterial,
                outlineMaterial);

            NetworkPrefabsList networkPrefabs = ConfigureNetworkPrefabs(
                playerPrefab,
                lightFurniturePrefab,
                heavyFurniturePrefab);

            GameObject rigPrefab = CreateOrUpdateNetworkRigPrefab(playerPrefab, networkPrefabs);

            CreatePrototypeScene(
                rigPrefab,
                lightFurniturePrefab,
                heavyFurniturePrefab,
                floorMaterial,
                wallMaterial);

            if (AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/Scripts/Temp.cs") != null)
                AssetDatabase.DeleteAsset("Assets/Scripts/Temp.cs");

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
            const string path = "Assets/Materials/Furniture_Outline.mat";
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

        private static GameObject CreateFurniturePrefab(
            string path,
            string objectName,
            Vector3 scale,
            FurnitureDefinition definition,
            FurnitureThrowSettings throwSettings,
            Material bodyMaterial,
            Material outlineMaterial)
        {
            GameObject root = GameObject.CreatePrimitive(PrimitiveType.Cube);
            root.name = objectName;
            root.layer = LayerMask.NameToLayer(GameLayers.FurnitureName);
            root.transform.localScale = scale;
            root.GetComponent<Renderer>().sharedMaterial = bodyMaterial;

            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.mass = definition.Mass;
            rigidbody.linearDamping = 0.05f;
            rigidbody.angularDamping = 0.5f;
            rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            root.AddComponent<NetworkObject>();
            NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
            SetBoolean(networkTransform, "Interpolate", true);

            FurnitureNetworkPhysics networkPhysics = root.AddComponent<FurnitureNetworkPhysics>();
            FurnitureGrabTarget target = root.AddComponent<FurnitureGrabTarget>();
            FurnitureHoverMotor hover = root.AddComponent<FurnitureHoverMotor>();
            FurnitureLauncher launcher = root.AddComponent<FurnitureLauncher>();
            FurnitureOutline outline = root.AddComponent<FurnitureOutline>();

            var outlineObject = new GameObject("OutlineShell")
            {
                layer = root.layer,
            };
            outlineObject.transform.SetParent(root.transform, false);
            MeshFilter outlineFilter = outlineObject.AddComponent<MeshFilter>();
            outlineFilter.sharedMesh = root.GetComponent<MeshFilter>().sharedMesh;
            MeshRenderer outlineRenderer = outlineObject.AddComponent<MeshRenderer>();
            outlineRenderer.sharedMaterial = outlineMaterial;
            outlineRenderer.enabled = false;

            SetObjectReference(networkPhysics, "_definition", definition);
            SetObjectReference(target, "_settings", throwSettings);
            SetObjectReference(hover, "_settings", throwSettings);
            SetObjectReference(launcher, "_settings", throwSettings);
            SetObjectReference(outline, "_outlineRenderer", outlineRenderer);

            return SavePrefab(root, path);
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
            GameObject lightFurniturePrefab,
            GameObject heavyFurniturePrefab,
            Material floorMaterial,
            Material wallMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateLighting();
            CreateArena(floorMaterial, wallMaterial);
            CreateOverviewCamera();
            CreatePlayerSpawns();
            CreateFurnitureSpawner(lightFurniturePrefab, heavyFurniturePrefab);

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
        }

        /// <summary>
        /// Build Settings 씬 목록을 실제 존재하는 씬으로 맞춘다. MainMenu 가 있으면
        /// 빌드의 시작 씬이 되도록 항상 맨 앞에 둔다.
        /// </summary>
        internal static void SyncBuildScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>();

            foreach (string path in new[] { MainMenuScenePath, LobbyScenePath, ScenePath })
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

        private static void CreateArena(Material floorMaterial, Material wallMaterial)
        {
            var arena = new GameObject("Arena");

            GameObject floor = CreateStaticCube(
                "Floor",
                new Vector3(0f, -0.5f, 0f),
                new Vector3(20f, 1f, 20f),
                floorMaterial,
                arena.transform);
            GameObjectUtility.SetStaticEditorFlags(floor, StaticEditorFlags.BatchingStatic);

            GameObject wall = CreateStaticCube(
                "ImpactWall",
                new Vector3(0f, 2f, 7.5f),
                new Vector3(8f, 4f, 0.5f),
                wallMaterial,
                arena.transform);
            GameObjectUtility.SetStaticEditorFlags(wall, StaticEditorFlags.BatchingStatic);

            CreateStaticCube(
                "LeftMarker",
                new Vector3(-7.5f, 0.25f, 0f),
                new Vector3(0.25f, 0.5f, 15f),
                wallMaterial,
                arena.transform);
            CreateStaticCube(
                "RightMarker",
                new Vector3(7.5f, 0.25f, 0f),
                new Vector3(0.25f, 0.5f, 15f),
                wallMaterial,
                arena.transform);
        }

        private static GameObject CreateStaticCube(
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            Transform parent)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube;
        }

        private static void CreateOverviewCamera()
        {
            var cameraObject = new GameObject("OverviewCamera")
            {
                tag = "MainCamera",
            };
            cameraObject.transform.position = new Vector3(0f, 12f, -14f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.2f, 1f));

            Camera overviewCamera = cameraObject.AddComponent<Camera>();
            overviewCamera.fieldOfView = 55f;
            overviewCamera.clearFlags = CameraClearFlags.SolidColor;
            overviewCamera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
            AudioListener listener = cameraObject.AddComponent<AudioListener>();

            PrototypeSceneContext context = cameraObject.AddComponent<PrototypeSceneContext>();
            SetObjectReference(context, "_overviewCamera", overviewCamera);
            SetObjectReference(context, "_overviewAudioListener", listener);
        }

        private static void CreatePlayerSpawns()
        {
            var registryObject = new GameObject("PlayerSpawnPoints");
            PlayerSpawnRegistry registry = registryObject.AddComponent<PlayerSpawnRegistry>();

            Transform first = CreatePoint(
                "PlayerSpawn_0",
                new Vector3(-2f, 0.05f, -6f),
                Quaternion.identity,
                registryObject.transform);
            Transform second = CreatePoint(
                "PlayerSpawn_1",
                new Vector3(2f, 0.05f, -6f),
                Quaternion.identity,
                registryObject.transform);

            SetObjectArray(registry, "_spawnPoints", new Object[] { first, second });
        }

        private static void CreateFurnitureSpawner(
            GameObject lightFurniturePrefab,
            GameObject heavyFurniturePrefab)
        {
            var spawnerObject = new GameObject("FurnitureSpawnPoints");
            spawnerObject.AddComponent<NetworkObject>();
            DevFurnitureSpawner spawner = spawnerObject.AddComponent<DevFurnitureSpawner>();

            Vector3[] positions =
            {
                new(-5f, 0.7f, -1f),
                new(-2.5f, 0.7f, 0f),
                new(0f, 0.9f, 0f),
                new(2.5f, 0.7f, 0f),
                new(5f, 0.7f, -1f),
                new(-3f, 0.9f, 3f),
                new(0f, 0.7f, 3f),
                new(3f, 0.7f, 3f),
            };

            var points = new Object[positions.Length];
            for (int i = 0; i < positions.Length; i++)
            {
                points[i] = CreatePoint(
                    $"FurnitureSpawn_{i}",
                    positions[i],
                    Quaternion.Euler(0f, i * 17f, 0f),
                    spawnerObject.transform);
            }

            SetObjectReference(
                spawner,
                "_lightFurniturePrefab",
                lightFurniturePrefab.GetComponent<NetworkObject>());
            SetObjectReference(
                spawner,
                "_heavyFurniturePrefab",
                heavyFurniturePrefab.GetComponent<NetworkObject>());
            SetObjectArray(spawner, "_spawnPoints", points);
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
            RequireAsset<GameObject>(LightFurniturePrefabPath);
            RequireAsset<GameObject>(HeavyFurniturePrefabPath);
            RequireAsset<GameObject>(NetworkRigPrefabPath);
            RequireAsset<SceneAsset>(ScenePath);

            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (player.GetComponent<NetworkObject>() == null
                || player.GetComponent<PlayerMotor>() == null
                || player.GetComponent<GrabController>() == null)
            {
                throw new MissingComponentException("Player 프리팹의 필수 컴포넌트가 빠졌습니다.");
            }

            GameObject furniture = AssetDatabase.LoadAssetAtPath<GameObject>(LightFurniturePrefabPath);
            if (furniture.GetComponent<NetworkObject>() == null
                || furniture.GetComponent<FurnitureGrabTarget>() == null
                || furniture.GetComponent<FurnitureLauncher>() == null)
            {
                throw new MissingComponentException("Furniture 프리팹의 필수 컴포넌트가 빠졌습니다.");
            }

            ValidateNetworkPrefabIdentity();
        }

        private static readonly string[] NetworkPrefabPaths =
        {
            PlayerPrefabPath,
            LightFurniturePrefabPath,
            HeavyFurniturePrefabPath,
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
                new[] { ScenePath },
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
