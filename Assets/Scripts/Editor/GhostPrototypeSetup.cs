using System;
using GhostHunter.Core;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Interaction;
using GhostHunter.Systems.Installers;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 귀신 프로토타입 설정·프리팹·Game 씬 스포너를 반복 가능하게 설치한다.
    /// <see cref="PrototypeSceneSetup"/> 의 전체 생성 마지막에도 호출되므로 씬을 다시 구워도
    /// 귀신 시스템 배선이 사라지지 않는다.
    /// </summary>
    public static class GhostPrototypeSetup
    {
        internal const string SettingsPath =
            "Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset";

        private const string GhostPrefabPath = "Assets/Prefabs/Ghost/Ghost_Prototype.prefab";
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string NetworkPrefabsPath = "Assets/DefaultNetworkPrefabs.asset";
        private const string BodyMaterialPath = "Assets/Materials/M_GhostBody.mat";
        private const string ConeMaterialPath = "Assets/Materials/M_GhostVisionCone.mat";
        private const string SystemObjectName = "GhostPrototypeSystem";
        private const string SpawnPointName = "GhostSpawnPoint";

        private static readonly Vector3 SpawnPointPosition = new(0f, 0.1f, 2f);
        private static readonly Vector3 RoamCenter = new(0f, 0.1f, -1f);
        private static readonly Vector3 RoamSize = new(16f, 3f, 9f);

        [MenuItem("GhostHunter/귀신 프로토타입 설치", priority = 5)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the ghost prototype.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the ghost prototype. " +
                    $"The active scene is '{scene.path}'.");
            }

            EnsureFolders();
            PrototypeSceneSetup.EnsureGameplayLayers();
            GhostPrototypeSettings settings = LoadOrCreateSettings();
            GameObject ghostPrefab = BuildGhostPrefab(settings);
            RegisterNetworkPrefab(ghostPrefab);
            InstallSceneSystem(scene, ghostPrefab);

            AssetDatabase.SaveAssets();
            ValidateInstallation();

            if (logCompletion)
            {
                Debug.Log(
                    "[GhostPrototypeSetup] 귀신 프로토타입 설정·프리팹·Game 씬 스포너 설치 완료. " +
                    "Bootstrap 씬에서 Play → F1 HUD의 귀신 섹션에서 스폰합니다.");
            }
        }

        internal static void ValidateInstallation()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(SettingsPath);
            if (settings == null)
                throw new MissingReferenceException($"귀신 설정 에셋이 없습니다: {SettingsPath}");

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            if (prefab == null)
                throw new MissingReferenceException($"귀신 프리팹이 없습니다: {GhostPrefabPath}");

            var networkObject = prefab.GetComponent<NetworkObject>();
            if (networkObject == null)
                throw new MissingComponentException("Ghost_Prototype 루트에 NetworkObject 가 없습니다.");

            var serializedNetworkObject = new SerializedObject(networkObject);
            uint hash = (uint)serializedNetworkObject.FindProperty("GlobalObjectIdHash").longValue;
            bool inScenePlaced = serializedNetworkObject.FindProperty("m_InScenePlaced").boolValue;
            if (hash == 0)
                throw new InvalidOperationException("Ghost_Prototype 의 GlobalObjectIdHash 가 0 입니다. 재임포트가 필요합니다.");
            if (inScenePlaced)
                throw new InvalidOperationException("Ghost_Prototype 이 in-scene placed 로 박혀 있습니다. 프리팹 에셋은 false 여야 합니다.");

            var controller = prefab.GetComponent<GhostPrototypeController>();
            if (controller == null)
                throw new MissingComponentException("Ghost_Prototype 에 GhostPrototypeController 가 없습니다.");
            if (prefab.GetComponent<CharacterController>() == null)
                throw new MissingComponentException("Ghost_Prototype 에 CharacterController 가 없습니다.");
            if (prefab.GetComponent<NetworkTransform>() == null)
                throw new MissingComponentException("Ghost_Prototype 에 NetworkTransform 이 없습니다.");
            if (prefab.GetComponent<GhostPrototypeSceneMarker>() == null)
                throw new MissingComponentException("Ghost_Prototype 에 Scene 뷰 표식이 없습니다.");

            var phenomenaPlayer = prefab.GetComponent<GhostPhenomenaPlayer>();
            if (phenomenaPlayer == null)
                throw new MissingComponentException("Ghost_Prototype 에 GhostPhenomenaPlayer 가 없습니다.");

            ValidateGhostPrototypeLayer(prefab);

            var serializedController = new SerializedObject(controller);
            if (serializedController.FindProperty("_settings").objectReferenceValue != settings)
                throw new MissingReferenceException("Ghost_Prototype 의 설정 배선이 잘못됐습니다.");
            if (serializedController.FindProperty("_phenomenaPlayer").objectReferenceValue != phenomenaPlayer)
                throw new MissingReferenceException("Ghost_Prototype 의 현상 재생기 배선이 잘못됐습니다.");

            var serializedPhenomena = new SerializedObject(phenomenaPlayer);
            if (serializedPhenomena.FindProperty("_settings").objectReferenceValue != settings)
                throw new MissingReferenceException("GhostPhenomenaPlayer 의 설정 배선이 잘못됐습니다.");
            if (serializedPhenomena.FindProperty("_apparitionMaterial").objectReferenceValue == null)
                throw new MissingReferenceException("GhostPhenomenaPlayer 의 일시 출현 머티리얼이 없습니다.");

            if (!IsRegisteredNetworkPrefab(prefab))
                throw new MissingReferenceException("Ghost_Prototype 이 NetworkPrefabsList 에 없습니다.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                return;

            FindSceneBindings(scene, out GameInstaller installer, out GhostPrototypeSpawner spawner);
            if (installer == null || spawner == null)
                throw new MissingReferenceException("Game 씬의 귀신 스포너 배선이 없습니다.");

            var serializedSpawner = new SerializedObject(spawner);
            if (serializedSpawner.FindProperty("_ghostPrefab").objectReferenceValue != prefab)
                throw new MissingReferenceException("GhostPrototypeSpawner 의 프리팹 배선이 잘못됐습니다.");
            if (serializedSpawner.FindProperty("_spawnPoint").objectReferenceValue == null)
                throw new MissingReferenceException("GhostPrototypeSpawner 의 스폰 지점 배선이 없습니다.");

            var serializedInstaller = new SerializedObject(installer);
            if (serializedInstaller.FindProperty("_ghostSpawner").objectReferenceValue != spawner)
                throw new MissingReferenceException("GameInstaller 의 귀신 스포너 배선이 잘못됐습니다.");

            if (FindSceneObjectDeep(scene, "DrillCarSafeZone_Temp") == null)
                throw new MissingReferenceException("Game 씬에 임시 드릴 카 세이프 존이 없습니다.");

            if (FindSceneObjectDeep(scene, HidingSpotsRootName) == null)
                throw new MissingReferenceException("Game 씬에 임시 은신처가 없습니다.");
        }

        private static GhostPrototypeSettings LoadOrCreateSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(SettingsPath);
            if (settings != null)
                return settings;

            settings = ScriptableObject.CreateInstance<GhostPrototypeSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static GameObject BuildGhostPrefab(GhostPrototypeSettings settings)
        {
            var root = new GameObject("Ghost_Prototype")
            {
                layer = GetGhostPrototypeLayer(),
            };

            try
            {
                root.AddComponent<NetworkObject>();

                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                networkTransform.SyncScaleX = false;
                networkTransform.SyncScaleY = false;
                networkTransform.SyncScaleZ = false;
                networkTransform.SyncRotAngleX = false;
                networkTransform.SyncRotAngleZ = false;
                networkTransform.Interpolate = true;

                CharacterController controller = root.AddComponent<CharacterController>();
                controller.height = 1.8f;
                controller.radius = 0.35f;
                controller.center = new Vector3(0f, 0.9f, 0f);
                controller.stepOffset = 0.35f;
                controller.skinWidth = 0.03f;

                GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                body.name = "Body";
                body.layer = root.layer;
                Object.DestroyImmediate(body.GetComponent<Collider>());
                body.transform.SetParent(root.transform, false);
                body.transform.localPosition = new Vector3(0f, 0.9f, 0f);
                body.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);
                Renderer bodyRenderer = body.GetComponent<Renderer>();
                Material bodyMaterial = CreateUnlitMaterial(
                    BodyMaterialPath,
                    new Color(0.76f, 0.79f, 0.92f, 0.6f));
                bodyRenderer.sharedMaterial = bodyMaterial;
                bodyRenderer.enabled = false;

                var lightObject = new GameObject("StateLight")
                {
                    layer = root.layer,
                };
                lightObject.transform.SetParent(root.transform, false);
                lightObject.transform.localPosition = new Vector3(0f, 1.4f, 0f);
                Light stateLight = lightObject.AddComponent<Light>();
                stateLight.type = LightType.Point;
                stateLight.range = 9f;
                stateLight.intensity = 3f;
                stateLight.color = new Color(1f, 0.2f, 0.15f);
                stateLight.enabled = false;

                var coneObject = new GameObject("VisionCone")
                {
                    layer = root.layer,
                };
                coneObject.transform.SetParent(root.transform, false);
                coneObject.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                MeshFilter coneFilter = coneObject.AddComponent<MeshFilter>();
                coneFilter.sharedMesh = GhostVision.BuildConeMesh(
                    settings.VisionDistance,
                    settings.VisionAngle);
                MeshRenderer coneRenderer = coneObject.AddComponent<MeshRenderer>();
                coneRenderer.sharedMaterial = CreateUnlitMaterial(
                    ConeMaterialPath,
                    new Color(1f, 0.12f, 0.1f, 0.22f),
                    doubleSided: true);
                coneRenderer.shadowCastingMode = ShadowCastingMode.Off;
                coneRenderer.receiveShadows = false;
                coneObject.SetActive(false);

                var phenomenaPlayer = root.AddComponent<GhostPhenomenaPlayer>();
                PrototypeSceneSetup.SetObjectReference(phenomenaPlayer, "_settings", settings);
                PrototypeSceneSetup.SetObjectReference(
                    phenomenaPlayer,
                    "_apparitionMaterial",
                    bodyMaterial);

                var controllerComponent = root.AddComponent<GhostPrototypeController>();
                root.AddComponent<GhostPrototypeSceneMarker>();
                PrototypeSceneSetup.SetObjectReference(controllerComponent, "_settings", settings);
                PrototypeSceneSetup.SetObjectReference(controllerComponent, "_body", body);
                PrototypeSceneSetup.SetObjectArray(
                    controllerComponent,
                    "_bodyRenderers",
                    new Object[] { bodyRenderer });
                PrototypeSceneSetup.SetObjectReference(controllerComponent, "_stateLight", stateLight);
                PrototypeSceneSetup.SetObjectReference(controllerComponent, "_visionConeRoot", coneObject);
                PrototypeSceneSetup.SetObjectReference(controllerComponent, "_visionConeFilter", coneFilter);
                PrototypeSceneSetup.SetObjectReference(
                    controllerComponent,
                    "_phenomenaPlayer",
                    phenomenaPlayer);

                PrefabUtility.SaveAsPrefabAsset(root, GhostPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }

            // SaveAsPrefabAsset 시점에는 원본이 씬 오브젝트라 NetworkObject 해시가 씬 기준으로
            // 잡힌다. ForceUpdate 재임포트로 에셋 기준 OnValidate 를 다시 돌려 고유 해시를 만든다
            // → conventions/unity-assets.md §5.2
            AssetDatabase.ImportAsset(GhostPrefabPath, ImportAssetOptions.ForceUpdate);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);

            EditorUtility.SetDirty(prefab.GetComponent<NetworkObject>());
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            return prefab;
        }

        private static void RegisterNetworkPrefab(GameObject ghostPrefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
            {
                throw new MissingReferenceException(
                    $"{NetworkPrefabsPath} 가 없습니다. 'GhostHunter > 프로토타입 게임 생성'을 먼저 실행하세요.");
            }

            if (IsRegisteredNetworkPrefab(ghostPrefab))
                return;

            list.Add(new NetworkPrefab
            {
                Override = NetworkPrefabOverride.None,
                Prefab = ghostPrefab,
            });
            EditorUtility.SetDirty(list);
        }

        private static bool IsRegisteredNetworkPrefab(GameObject ghostPrefab)
        {
            var list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabsPath);
            if (list == null)
                return false;

            foreach (NetworkPrefab entry in list.PrefabList)
            {
                if (entry != null && entry.Prefab == ghostPrefab)
                    return true;
            }

            return false;
        }

        private static void InstallSceneSystem(Scene scene, GameObject ghostPrefab)
        {
            FindSceneBindings(scene, out GameInstaller installer, out GhostPrototypeSpawner spawner);
            if (installer == null)
                throw new MissingReferenceException("Game 씬에서 GameInstaller 를 찾지 못했습니다.");

            if (spawner == null)
            {
                GameObject systemObject = FindSceneRoot(scene, SystemObjectName);
                if (systemObject == null)
                {
                    systemObject = new GameObject(SystemObjectName);
                    SceneManager.MoveGameObjectToScene(systemObject, scene);
                }

                spawner = systemObject.GetComponent<GhostPrototypeSpawner>();
                if (spawner == null)
                    spawner = systemObject.AddComponent<GhostPrototypeSpawner>();
            }

            Transform spawnPoint = spawner.transform.Find(SpawnPointName);
            if (spawnPoint == null)
            {
                var spawnPointObject = new GameObject(SpawnPointName);
                spawnPointObject.transform.SetParent(spawner.transform, false);
                spawnPointObject.transform.position = SpawnPointPosition;
                spawnPoint = spawnPointObject.transform;
            }

            PrototypeSceneSetup.SetObjectReference(spawner, "_ghostPrefab", ghostPrefab);
            PrototypeSceneSetup.SetObjectReference(spawner, "_spawnPoint", spawnPoint);
            SetVector3(spawner, "_roamCenter", RoamCenter);
            SetVector3(spawner, "_roamSize", RoamSize);
            PrototypeSceneSetup.SetObjectReference(installer, "_ghostSpawner", spawner);

            InstallAmbientLights(scene);
            InstallGhostDrawers(scene);
            InstallDrillCarSafeZone(scene);
            InstallHidingSpots(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }

        /// <summary>
        /// 드릴 카 세이프 존 임시 버전(기획서 §11.1). 정식 드릴 카 없이 현관 앞에 상자만 둔다 —
        /// 이 안의 플레이어는 귀신 탐지·잡힘에서 제외된다. 현관문을 기준으로 남쪽에 배치한다.
        /// </summary>
        private static void InstallDrillCarSafeZone(Scene scene)
        {
            const string zoneName = "DrillCarSafeZone_Temp";
            var boxSize = new Vector3(4.5f, 3f, 5f);

            // 비교용 집(House_01_OriginalScale_Right)에도 같은 이름의 현관문이 있으므로 House_01 안으로 좁힌다.
            Transform house = FindSceneObjectDeep(scene, "House_01");
            Transform anchor = house != null
                ? (FindChildDeep(house, "FrontDoor_1.5m") ?? FindChildDeep(house, "FrontEntrance"))
                : null;

            // 현관문 기준 남쪽으로. 상자 북쪽 면이 계단 바로 밖에 오도록 오프셋을 잡는다.
            Vector3 center = anchor != null
                ? new Vector3(anchor.position.x - 0.75f, boxSize.y * 0.5f, anchor.position.z - (0.3f + boxSize.z * 0.5f))
                : new Vector3(1f, boxSize.y * 0.5f, -7f);

            GameObject zoneObject = FindSceneRoot(scene, zoneName);
            if (zoneObject == null)
            {
                zoneObject = new GameObject(zoneName);
                SceneManager.MoveGameObjectToScene(zoneObject, scene);
            }

            zoneObject.transform.SetPositionAndRotation(center, Quaternion.identity);
            zoneObject.transform.localScale = Vector3.one; // 하드 룰 §3.5

            var safeZone = zoneObject.GetComponent<DrillCarSafeZone>()
                ?? zoneObject.AddComponent<DrillCarSafeZone>();
            SetVector3(safeZone, "_size", boxSize);
            EditorUtility.SetDirty(zoneObject);

            if (anchor == null)
                Debug.LogWarning("[GhostPrototypeSetup] 현관문을 찾지 못해 세이프 존을 기본 좌표에 뒀습니다. 위치를 확인하세요.");
        }

        private const string HidingSpotsRootName = "HidingSpots_Temp";
        private static readonly Vector3 HidingSpotSize = new(1.2f, 1.4f, 1.2f);
        private const float HidingSpotWallInset = 1f;

        /// <summary>방 이름표(로그용) → 그 방의 바닥 앵커 오브젝트 이름. 앵커의 위치·크기에서
        /// 은신처 상자 좌표를 역산한다 — 정식 가구(옷장·침대 밑·책상 밑)가 아직 없어서다.</summary>
        private static readonly (string Label, string FloorAnchor)[] HidingSpotRooms =
        {
            ("침실1", "Bedroom_01_A_Floor"),
            ("침실2", "Bedroom_02_B_Floor"),
            ("거실", "LivingRoom_Floor"),
            ("창고", "Storage_Floor"),
        };

        /// <summary>
        /// 일반 은신처 임시 버전(기획서 §9.5 — 옷장·침대 밑·책상 밑). 정식 가구가 아직
        /// 프리팹화되지 않아(docs/todo/TODO-미정.md) 특정 가구에 붙이지 않고, 방 바닥 앵커에서
        /// 역산한 상자를 방마다 하나씩 둔다. 정식 가구가 생기면 이 오브젝트는 통째로 삭제한다
        /// (DrillCarSafeZone과 동일 방침).
        /// </summary>
        private static void InstallHidingSpots(Scene scene)
        {
            Transform house = FindSceneObjectDeep(scene, "House_01");
            if (house == null)
            {
                Debug.LogWarning("[GhostPrototypeSetup] House_01 을 찾지 못해 은신처를 설치하지 못했습니다.");
                return;
            }

            GameObject spotsRoot = FindSceneRoot(scene, HidingSpotsRootName);
            if (spotsRoot == null)
            {
                spotsRoot = new GameObject(HidingSpotsRootName);
                SceneManager.MoveGameObjectToScene(spotsRoot, scene);
            }

            foreach ((string label, string floorAnchor) in HidingSpotRooms)
            {
                // 비교용 집에도 같은 이름의 바닥이 있으므로 House_01 안으로 좁혀서 찾는다.
                Transform floor = FindChildDeep(house, floorAnchor);
                if (floor == null)
                {
                    Debug.LogWarning(
                        $"[GhostPrototypeSetup] '{floorAnchor}' 을 찾지 못해 {label} 은신처를 건너뜁니다.");
                    continue;
                }

                // 바닥 크기(localScale = 방 실제 폭·깊이)에서 벽 안쪽으로 한 발 들어간 구석을 잡는다.
                Vector3 half = floor.localScale * 0.5f;
                float insetX = Mathf.Max(0f, half.x - HidingSpotWallInset);
                float insetZ = Mathf.Max(0f, half.z - HidingSpotWallInset);
                Vector3 center = floor.position
                    + new Vector3(-insetX, HidingSpotSize.y * 0.5f, -insetZ);

                string zoneName = $"HidingSpot_{label}";
                Transform existing = FindChildDeep(spotsRoot.transform, zoneName);
                GameObject zoneObject = existing != null ? existing.gameObject : null;
                if (zoneObject == null)
                {
                    zoneObject = new GameObject(zoneName);
                    zoneObject.transform.SetParent(spotsRoot.transform);
                }

                zoneObject.transform.SetPositionAndRotation(center, Quaternion.identity);
                zoneObject.transform.localScale = Vector3.one; // 하드 룰 §3.5

                var hidingSpot = zoneObject.GetComponent<HidingSpot>()
                    ?? zoneObject.AddComponent<HidingSpot>();
                SetVector3(hidingSpot, "_size", HidingSpotSize);
                EditorUtility.SetDirty(zoneObject);
            }
        }

        private static Transform FindSceneObjectDeep(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = FindChildDeep(root.transform, objectName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform FindChildDeep(Transform root, string objectName)
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == objectName)
                    return t;
            }

            return null;
        }

        /// <summary>
        /// 초자연현상 '조명 깜빡임/끄기'(§6.5 #5)가 건드릴 방 조명에 <see cref="GhostAmbientLight"/>
        /// 표식을 붙인다. <c>House_01/RoomLights</c> 그룹의 조명만 대상으로 한다.
        /// </summary>
        private static void InstallAmbientLights(Scene scene)
        {
            int tagged = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Light light in root.GetComponentsInChildren<Light>(true))
                {
                    if (light.type != LightType.Point || !IsUnderGroup(light.transform, "RoomLights"))
                        continue;

                    if (light.GetComponent<GhostAmbientLight>() == null)
                    {
                        light.gameObject.AddComponent<GhostAmbientLight>();
                        EditorUtility.SetDirty(light.gameObject);
                    }

                    tagged++;
                }
            }

            if (tagged == 0)
                Debug.LogWarning("[GhostPrototypeSetup] RoomLights 그룹의 조명을 찾지 못했습니다. 조명 현상이 비활성입니다.");
        }

        /// <summary>
        /// 초자연현상 '서랍 열기'(§6.5 #4)용으로 서랍장류 가구 인스턴스에 얇은 서랍 면과
        /// <see cref="GhostDrawer"/> 를 붙인다. 가구 프리팹은 아직 서랍 파츠가 없으므로 씬에서만
        /// 처리한다(정신력 테스트베드와 같은 범위). 이미 붙어 있으면 건너뛴다.
        /// </summary>
        private static void InstallGhostDrawers(Scene scene)
        {
            string[] drawerKinds = { "Dresser", "Nightstand", "BedsideTable" };
            int installed = 0;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
                {
                    if (transform.GetComponent<NetworkObject>() == null)
                        continue;

                    if (!StartsWithAny(transform.name, drawerKinds))
                        continue;

                    if (transform.Find("GhostDrawer_Face") != null)
                    {
                        installed++;
                        continue;
                    }

                    AddDrawerFace(transform);
                    installed++;
                }
            }

            if (installed == 0)
                Debug.LogWarning("[GhostPrototypeSetup] 서랍장류 가구를 찾지 못했습니다. 서랍 현상은 다른 서랍이 있어야 동작합니다.");
        }

        private static void AddDrawerFace(Transform furniture)
        {
            Bounds local = ComputeLocalBounds(furniture);
            var size = new Vector3(Mathf.Abs(local.size.x), Mathf.Abs(local.size.y), Mathf.Abs(local.size.z));

            var face = GameObject.CreatePrimitive(PrimitiveType.Cube);
            face.name = "GhostDrawer_Face";
            face.layer = furniture.gameObject.layer;
            Object.DestroyImmediate(face.GetComponent<Collider>());

            face.transform.SetParent(furniture, false);
            face.transform.localRotation = Quaternion.identity;
            face.transform.localScale = new Vector3(
                Mathf.Max(0.1f, size.x * 0.6f),
                Mathf.Max(0.06f, size.y * 0.32f),
                0.05f);
            face.transform.localPosition = new Vector3(
                local.center.x,
                local.center.y,
                local.center.z + size.z * 0.5f + 0.02f);

            Renderer renderer = face.GetComponent<Renderer>();
            renderer.sharedMaterial = CreateUnlitMaterial(
                "Assets/Materials/M_GhostDrawer.mat",
                new Color(0.18f, 0.16f, 0.14f, 1f));
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            var drawer = face.AddComponent<GhostDrawer>();
            var serialized = new SerializedObject(drawer);
            serialized.FindProperty("_openLocalOffset").vector3Value =
                new Vector3(0f, 0f, Mathf.Max(0.12f, size.z * 0.55f));
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(furniture.gameObject);
        }

        private static Bounds ComputeLocalBounds(Transform furniture)
        {
            Renderer[] renderers = furniture.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(Vector3.zero, new Vector3(0.5f, 0.5f, 0.4f));

            Bounds world = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
                world.Encapsulate(renderers[i].bounds);

            Vector3 localCenter = furniture.InverseTransformPoint(world.center);
            Vector3 localSize = furniture.InverseTransformVector(world.size);
            return new Bounds(localCenter, localSize);
        }

        private static bool IsUnderGroup(Transform transform, string groupName)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (current.name == groupName)
                    return true;
            }

            return false;
        }

        private static bool StartsWithAny(string value, string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static void FindSceneBindings(
            Scene scene,
            out GameInstaller installer,
            out GhostPrototypeSpawner spawner)
        {
            installer = null;
            spawner = null;

            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (installer == null)
                    installer = root.GetComponentInChildren<GameInstaller>(true);
                if (spawner == null)
                    spawner = root.GetComponentInChildren<GhostPrototypeSpawner>(true);
            }
        }

        private static GameObject FindSceneRoot(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == objectName)
                    return root;
            }

            return null;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets", "Prefabs");
            EnsureFolder("Assets/Prefabs", "Ghost");
            EnsureFolder("Assets", "Materials");
            EnsureFolder("Assets/Settings", "Gameplay");
        }

        private static int GetGhostPrototypeLayer()
        {
            int layer = GameLayers.GhostPrototype;
            if (layer < 0)
                throw new InvalidOperationException($"'{GameLayers.GhostPrototypeName}' 레이어가 없습니다.");

            return layer;
        }

        private static void ValidateGhostPrototypeLayer(GameObject prefab)
        {
            int expectedLayer = GetGhostPrototypeLayer();
            foreach (Transform child in prefab.GetComponentsInChildren<Transform>(true))
            {
                if (child.gameObject.layer != expectedLayer)
                {
                    throw new InvalidOperationException(
                        $"'{child.name}' 이(가) {GameLayers.GhostPrototypeName} 레이어가 아닙니다.");
                }
            }
        }

        private static void EnsureFolder(string parent, string child)
        {
            if (!AssetDatabase.IsValidFolder($"{parent}/{child}"))
                AssetDatabase.CreateFolder(parent, child);
        }

        private static Material CreateUnlitMaterial(string path, Color color, bool doubleSided = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                throw new MissingReferenceException("URP Unlit 셰이더를 찾지 못했습니다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);

            // URP Unlit 반투명 설정. 색의 알파를 그대로 쓰게 블렌드를 켠다.
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", doubleSided ? (float)CullMode.Off : (float)CullMode.Back);
            material.SetOverrideTag("RenderType", "Transparent");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.DisableKeyword("_ALPHATEST_ON");
            material.renderQueue = (int)RenderQueue.Transparent;

            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetVector3(Object target, string propertyName, Vector3 value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, propertyName);

            property.vector3Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
