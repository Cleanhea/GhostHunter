using System;
using System.Collections.Generic;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GhostHunter.EditorTools
{
    /// <summary>B안에 씬 가구 풀·배치 후보·서버 생성기를 추가하고 저장 전에 배치를 검증한다.</summary>
    public static class PlanBFurnitureSpawnSetup
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string PlanRoot = "House_Prototype_PlanB";
        private const string SystemRoot = "RandomFurniture";
        private const string SettingsPath = "Assets/Settings/Gameplay/FurnitureSpawnSettings_PlanB.asset";
        private const string FurnitureFolder = "Assets/Prefabs/Furniture/";
        private static readonly Dictionary<string, string> PrototypePrefabs = new(StringComparer.Ordinal)
        {
            { "Sofa", "Sofa_2.2x0.9.prefab" },
            { "Drawer", "Dresser_0.8x0.45.prefab" },
            { "Chair", "Chair_0.5x0.5.prefab" },
            { "Box", "Crate_0.6.prefab" },
        };

        [MenuItem("GhostHunter/맵 B안 가구 랜덤 배치 설치", priority = 5)]
        public static void Install()
        {
            Scene scene = RequireScene();
            Transform house = FindRoot(scene, PlanRoot);
            if (house == null)
                throw new MissingReferenceException("B안 실내 프로토타입을 먼저 만들어야 합니다.");
            Transform existing = house.Find(SystemRoot);
            if (existing != null)
            {
                ValidateController(existing.GetComponentInChildren<FurnitureSpawnController>(true));
                ValidatePlayerSpawns(scene, house);
                PrototypeSceneSetup.RefreshScenePlacedNetworkObjectsInCurrentScene(scene);
                Selection.activeTransform = existing;
                Debug.Log("[PlanBFurnitureSpawnSetup] 기존 배선을 검증하고 NGO 식별자를 갱신·저장했습니다.", existing);
                return;
            }
            FurnitureSpawnSettings settings = LoadSettings();
            if (settings.CandidateSpacing < 0.1f || !IsFinite(settings.CandidateSpacing)
                || settings.WallInset <= 0f || !IsFinite(settings.WallInset)
                || settings.ClearPathWidth <= 0f || !IsFinite(settings.ClearPathWidth)
                || settings.FloorCandidatesPerRoom < 1 || settings.FloorCandidatesPerRoom > 100)
                throw new InvalidOperationException("후보 간격·벽 여유·후보 수 설정을 확인하세요.");

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install Plan B random furniture");
            bool saved = false;
            try
            {
                Transform root = CreateGroup(SystemRoot, house);
                Transform poolRoot = CreateGroup("FurniturePool", root);
                poolRoot.localPosition = Vector3.down * 50f;
                Transform pointRoot = CreateGroup("SpawnPoints", root);
                Transform controllerRoot = CreateGroup("Controller", root);

                var items = new List<RandomFurnitureItem>();
                var placementSizes = new List<Vector3>();
                foreach (FurnitureSpawnSettings.PoolRule rule in settings.Pools)
                {
                    if (rule == null || rule.PerRoom || !PrototypePrefabs.TryGetValue(rule.PoolId, out string filename))
                        throw new InvalidOperationException("초기 설치는 Sofa/Drawer/Chair/Box의 House 수량을 사용합니다. 추가 풀은 인스펙터에서 배선하세요.");
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(FurnitureFolder + filename);
                    if (prefab == null || prefab.GetComponent<NetworkObject>() == null
                        || prefab.GetComponent<NetworkTransform>() == null || prefab.GetComponent<Rigidbody>() == null)
                        throw new MissingReferenceException($"필수 가구 프리팹 배선이 없습니다: {filename}");
                    if (rule.MaximumCount < 0 || rule.MaximumCount > 256)
                        throw new InvalidOperationException("초기 풀 수량 범위는 종류별 0~256개입니다.");
                    for (int i = 0; i < rule.MaximumCount; i++)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        Undo.RegisterCreatedObjectUndo(instance, "Add pooled furniture");
                        instance.transform.SetParent(poolRoot, false);
                        instance.transform.localPosition = Vector3.right * items.Count * 3f;
                        instance.name = $"{rule.PoolId}_{i + 1:00}";
                        Bounds bounds = CalculateLocalBounds(instance.transform);
                        Vector3 placementSize = ToCardinalPlacementSize(bounds.size);
                        if (!placementSizes.Exists(size => Approximately(size, placementSize)))
                            placementSizes.Add(placementSize);
                        DetectionTargetMarker marker = Undo.AddComponent<DetectionTargetMarker>(instance);
                        marker.SetTargetActive(false);
                        RandomFurnitureItem item = Undo.AddComponent<RandomFurnitureItem>(instance);
                        item.Configure(rule.PoolId, bounds, marker);
                        items.Add(item);
                        EditorUtility.SetDirty(item);
                        EditorUtility.SetDirty(marker);
                    }
                }
                if (items.Count == 0)
                    throw new InvalidOperationException("설치할 씬 가구 풀이 비었습니다.");

                FurnitureSpawnPoint[] points = CreateCandidates(house, pointRoot, settings, placementSizes);
                Undo.AddComponent<NetworkObject>(controllerRoot.gameObject);
                FurnitureSpawnController controller = Undo.AddComponent<FurnitureSpawnController>(controllerRoot.gameObject);
                FurnitureResetter resetter = Undo.AddComponent<FurnitureResetter>(controllerRoot.gameObject);
                var bodies = new UnityEngine.Object[items.Count];
                for (int i = 0; i < items.Count; i++)
                    bodies[i] = items[i].GetComponent<Rigidbody>();
                var serializedResetter = new SerializedObject(resetter);
                SerializedProperty furniture = serializedResetter.FindProperty("_furniture");
                furniture.arraySize = bodies.Length;
                for (int i = 0; i < bodies.Length; i++)
                    furniture.GetArrayElementAtIndex(i).objectReferenceValue = bodies[i];
                serializedResetter.ApplyModifiedProperties();
                controller.Configure(settings, items.ToArray(), points, resetter);
                EditorUtility.SetDirty(controller);
                RepositionPlayerSpawns(scene, house);
                ValidatePlayerSpawns(scene, house);
                ValidateController(controller);
                Undo.CollapseUndoOperations(undoGroup);
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException("Game 씬 저장에 실패했습니다.");
                saved = true;
                PrototypeSceneSetup.RefreshScenePlacedNetworkObjectsInCurrentScene(scene);
                Selection.activeTransform = root;
                Debug.Log($"[PlanBFurnitureSpawnSetup] B안 랜덤 가구 풀 {items.Count}개, 후보 {points.Length}개를 저장했습니다. "
                    + "플레이어 시작 위치는 B안 앞마당입니다. Host 시작 시 서버가 가구를 배치합니다.", root);
            }
            catch
            {
                if (!saved)
                    Undo.RevertAllDownToGroup(undoGroup);
                else
                    Debug.LogError("[PlanBFurnitureSpawnSetup] 씬은 저장됐지만 식별자 검증이 완료되지 않았습니다. 콘솔 오류를 확인하세요.", house);
                throw;
            }
        }

        [MenuItem("GhostHunter/맵 B안 가구 랜덤 배치 검증", priority = 6)]
        public static void Validate()
        {
            Scene scene = RequireScene();
            Transform house = FindRoot(scene, PlanRoot);
            Transform root = house == null ? null : house.Find(SystemRoot);
            FurnitureSpawnController controller = root == null ? null : root.GetComponentInChildren<FurnitureSpawnController>(true);
            ValidateController(controller);
            ValidatePlayerSpawns(scene, house);
            PrototypeSceneSetup.ValidateScenePlacedNetworkObjects(scene);
            Debug.Log("[PlanBFurnitureSpawnSetup] 배선·B안 시작 위치·NGO 식별자·전체 배치 검증 통과 (seed 0~15).", controller);
        }

        /// <summary>여러 시드에서 수량·중복·정적 충돌·방 분포를 충족하는지 검사한다.</summary>
        public static void ValidateController(FurnitureSpawnController controller)
        {
            if (controller == null)
                throw new MissingReferenceException("B안 가구 랜덤 배치 설치 메뉴를 먼저 실행하세요.");
            foreach (RandomFurnitureItem item in controller.Items)
            {
                if (item == null)
                    throw new MissingReferenceException("가구 풀 배열에 비어 있는 항목이 있습니다.");

                // RandomFurnitureItem과 DetectionTargetMarker는 씬 인스턴스에 추가한 오버라이드다.
                // 추가 컴포넌트 자체는 PrefabUtility에서 프리팹 일부로 판정되지 않으므로,
                // 원본 연결 여부는 컴포넌트가 붙은 GameObject를 기준으로 검사한다.
                if (!PrefabUtility.IsPartOfPrefabInstance(item.gameObject))
                    throw new InvalidOperationException($"{item.name}: 가구 GameObject가 프리팹 인스턴스가 아닙니다.");
                if (item.GetComponent<FurnitureGrabTarget>() == null)
                    throw new MissingComponentException($"{item.name}: FurnitureGrabTarget이 없습니다.");
                if (item.GetComponent<DetectionTargetMarker>() == null)
                    throw new MissingComponentException($"{item.name}: DetectionTargetMarker가 없습니다.");
                if (item.transform.parent != null && item.transform.parent.GetComponentInParent<NetworkObject>() != null)
                    throw new InvalidOperationException($"{item.name}: 가구의 NetworkObject가 중첩되었습니다.");
            }
            for (int seed = 0; seed < 16; seed++)
            {
                if (!controller.TryBuildPlan(seed, out _, out _, out string error))
                    throw new InvalidOperationException($"B안 seed {seed} 배치 실패: {error}");
            }
        }

        private static FurnitureSpawnPoint[] CreateCandidates(Transform house, Transform parent,
            FurnitureSpawnSettings settings, List<Vector3> placementSizes)
        {
            if (placementSizes == null || placementSizes.Count == 0)
                throw new InvalidOperationException("가구 배치 치수가 없습니다.");
            var points = new List<FurnitureSpawnPoint>();
            int roomId = 0;
            float minimumFootprint = float.MaxValue;
            foreach (Vector3 size in placementSizes)
                minimumFootprint = Mathf.Min(minimumFootprint, Mathf.Min(size.x, size.z));
            Physics.SyncTransforms();
            foreach (Transform floor in house)
            {
                Transform rooms = floor.Find("Rooms_Prototype");
                if (rooms == null)
                    continue;
                foreach (Transform room in rooms)
                {
                    Transform marker = room.Find("RoomDimensions");
                    BoxCollider dimensions = marker == null ? null : marker.GetComponent<BoxCollider>();
                    if (dimensions == null)
                        throw new MissingReferenceException($"{room.name}의 RoomDimensions가 없습니다.");
                    var positions = new List<Vector3>();
                    var capacities = new List<Vector3>();
                    float halfWidth = dimensions.size.x * 0.5f;
                    float halfDepth = dimensions.size.z * 0.5f;
                    float inset = Mathf.Max(settings.WallInset, minimumFootprint * 0.5f);
                    for (float x = -halfWidth + inset; x <= halfWidth - inset; x += settings.CandidateSpacing)
                    {
                        for (float z = -halfDepth + inset; z <= halfDepth - inset; z += settings.CandidateSpacing)
                        {
                            Vector3 position = room.TransformPoint(new Vector3(x, 0f, z));
                            Vector3 capacity = Vector3.zero;
                            foreach (Vector3 size in placementSizes)
                            {
                                // 실제 가구 크기마다 중앙 십자 통로를 침범하지 않는지 검사한다.
                                float clearX = settings.ClearPathWidth * 0.5f + size.x * 0.5f;
                                float clearZ = settings.ClearPathWidth * 0.5f + size.z * 0.5f;
                                if (Mathf.Abs(x) < clearX || Mathf.Abs(z) < clearZ)
                                    continue;
                                var bounds = new Bounds(position + Vector3.up * size.y * 0.5f, size);
                                if (!FurnitureSpawnController.HasObstacle(bounds, null,
                                    settings.ObstacleMask, position.y))
                                    capacity = Vector3.Max(capacity, size);
                            }
                            if (capacity.x > 0f)
                            {
                                positions.Add(position);
                                capacities.Add(capacity);
                            }
                        }
                    }
                    var random = new System.Random(roomId);
                    var order = new List<int>(positions.Count);
                    for (int i = 0; i < positions.Count; i++)
                        order.Add(i);
                    for (int i = order.Count - 1; i > 0; i--)
                    {
                        int other = random.Next(i + 1);
                        (order[i], order[other]) = (order[other], order[i]);
                    }
                    // 같은 방에서는 큰 가구를 받을 수 있는 자리를 먼저 남기고, 같은 크기 안에서만 시드를 섞는다.
                    var shuffledRank = new int[order.Count];
                    for (int i = 0; i < order.Count; i++)
                        shuffledRank[order[i]] = i;
                    order.Sort((left, right) =>
                    {
                        int comparison = Capacity(right).CompareTo(Capacity(left));
                        return comparison != 0 ? comparison : shuffledRank[left].CompareTo(shuffledRank[right]);
                    });
                    int count = Mathf.Min(settings.FloorCandidatesPerRoom, order.Count);
                    if (count == 0)
                        Debug.Log($"[PlanBFurnitureSpawnSetup] {floor.name}/{room.name}: 안전 후보가 없어 "
                            + "이 방은 랜덤 가구 배치 후보에서 제외했습니다.", room);
                    Transform group = CreateGroup($"{floor.name}_{room.name}", parent);
                    for (int i = 0; i < count; i++)
                    {
                        int candidateIndex = order[i];
                        Transform pointTransform = CreateGroup($"Floor_{i + 1:00}", group);
                        pointTransform.position = positions[candidateIndex];
                        pointTransform.rotation = Quaternion.Euler(0f, random.Next(4) * 90f, 0f);
                        FurnitureSpawnPoint point = Undo.AddComponent<FurnitureSpawnPoint>(pointTransform.gameObject);
                        point.Configure(roomId, FurnitureSpawnType.Floor, capacities[candidateIndex]);
                        EditorUtility.SetDirty(point);
                        points.Add(point);
                    }
                    roomId++;

                    float Capacity(int index)
                    {
                        Vector3 size = capacities[index];
                        return size.x * size.z * 1000f + size.y;
                    }
                }
            }
            return points.ToArray();
        }

        private static Bounds CalculateLocalBounds(Transform root)
        {
            bool hasBounds = false;
            Bounds local = default;
            Physics.SyncTransforms();
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!collider.enabled || collider.isTrigger)
                    continue;
                Bounds bounds = collider.bounds;
                for (int mask = 0; mask < 8; mask++)
                {
                    Vector3 worldCorner = bounds.center + Vector3.Scale(bounds.extents,
                        new Vector3((mask & 1) == 0 ? -1f : 1f, (mask & 2) == 0 ? -1f : 1f,
                            (mask & 4) == 0 ? -1f : 1f));
                    // 프리팹 루트 Scale을 보존하면서도 실제 월드 미터 단위의 배치 경계를 저장한다.
                    // InverseTransformPoint는 Crate_0.6의 0.6 Scale을 역으로 나눠 1m로 부풀리므로 쓰지 않는다.
                    Vector3 corner = Quaternion.Inverse(root.rotation) * (worldCorner - root.position);
                    if (!hasBounds)
                    {
                        local = new Bounds(corner, Vector3.zero);
                        hasBounds = true;
                    }
                    else
                        local.Encapsulate(corner);
                }
            }
            if (!hasBounds || local.size.x <= 0f || local.size.y <= 0f || local.size.z <= 0f)
                throw new InvalidOperationException($"{root.name}의 물리 경계를 읽을 수 없습니다.");
            return local;
        }

        private static Vector3 ToCardinalPlacementSize(Vector3 size)
        {
            float footprint = Mathf.Max(size.x, size.z);
            return new Vector3(footprint, size.y, footprint);
        }

        private static bool Approximately(Vector3 left, Vector3 right)
        {
            return Mathf.Abs(left.x - right.x) < 0.001f
                && Mathf.Abs(left.y - right.y) < 0.001f
                && Mathf.Abs(left.z - right.z) < 0.001f;
        }

        private static void RepositionPlayerSpawns(Scene scene, Transform house)
        {
            Transform[] points = GetPlayerSpawns(scene, out _);
            BoxCollider yard = GetFrontYard(house);
            Vector3[] formation = HousePrototypeBuilder.PlayerSpawnPositions();
            if (points.Length != formation.Length)
                throw new InvalidOperationException("기존 플레이어 시작 위치 수가 프로토타입 구성과 다릅니다.");
            Vector3 center = Vector3.zero;
            foreach (Vector3 position in formation)
                center += position;
            center /= formation.Length;
            Vector3 destination = yard.bounds.center;
            destination.y = yard.bounds.max.y;
            for (int i = 0; i < points.Length; i++)
            {
                // 기존 네 명의 간격·바닥 여유를 유지하고 B안 앞마당 중심으로 옮긴다.
                Vector3 offset = formation[i] - new Vector3(center.x, 0f, center.z);
                Undo.RecordObject(points[i], "Move player start to Plan B");
                points[i].SetPositionAndRotation(destination + house.rotation * offset, house.rotation);
            }
        }

        private static void ValidatePlayerSpawns(Scene scene, Transform house)
        {
            Transform[] points = GetPlayerSpawns(scene, out PlayerSpawnRegistry registry);
            BoxCollider yard = GetFrontYard(house);
            var serialized = new SerializedObject(registry);
            float radius = serialized.FindProperty("_clearanceRadius").floatValue;
            float height = serialized.FindProperty("_clearanceHeight").floatValue;
            if (!IsFinite(radius) || !IsFinite(height) || radius <= 0f || height < radius * 2f)
                throw new InvalidOperationException("플레이어 스폰 캡슐 설정을 확인하세요.");
            Physics.SyncTransforms();
            Bounds bounds = yard.bounds;
            foreach (Transform point in points)
            {
                Vector3 foot = point.position;
                if (!IsFinite(foot.x) || !IsFinite(foot.y) || !IsFinite(foot.z)
                    || foot.x - radius < bounds.min.x || foot.x + radius > bounds.max.x
                    || foot.z - radius < bounds.min.z || foot.z + radius > bounds.max.z
                    || foot.y < bounds.max.y || foot.y > bounds.max.y + height)
                    throw new InvalidOperationException($"{point.name}이 B안 앞마당 범위 밖에 있습니다.");
                if (Physics.CheckCapsule(foot + Vector3.up * radius,
                    foot + Vector3.up * (height - radius), radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    throw new InvalidOperationException($"{point.name}의 플레이어 시작 위치가 막혔습니다.");
            }
        }

        private static Transform[] GetPlayerSpawns(Scene scene, out PlayerSpawnRegistry registry)
        {
            Transform root = FindRoot(scene, "PlayerSpawnPoints");
            registry = root == null ? null : root.GetComponent<PlayerSpawnRegistry>();
            if (registry == null)
                throw new MissingReferenceException("Game 씬의 PlayerSpawnPoints/PlayerSpawnRegistry가 필요합니다.");
            SerializedProperty references = new SerializedObject(registry).FindProperty("_spawnPoints");
            if (references.arraySize == 0)
                throw new MissingReferenceException("플레이어 시작 위치가 연결되지 않았습니다.");
            var points = new Transform[references.arraySize];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = references.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                if (points[i] == null || points[i].gameObject.scene != scene)
                    throw new MissingReferenceException("플레이어 시작 위치는 Game 씬의 Transform이어야 합니다.");
            }
            return points;
        }

        private static BoxCollider GetFrontYard(Transform house)
        {
            Transform frontYard = house == null ? null : house.Find("FrontYard_TEMP");
            BoxCollider yard = frontYard == null ? null : frontYard.GetComponent<BoxCollider>();
            if (yard == null || !yard.enabled || !yard.gameObject.activeInHierarchy
                || house.rotation != Quaternion.identity || house.lossyScale != Vector3.one)
                throw new InvalidOperationException("B안 앞마당 또는 집 루트의 회전·배율을 확인하세요.");
            return yard;
        }

        private static FurnitureSpawnSettings LoadSettings()
        {
            FurnitureSpawnSettings settings = AssetDatabase.LoadAssetAtPath<FurnitureSpawnSettings>(SettingsPath);
            if (settings != null)
                return settings;
            if (AssetDatabase.LoadMainAssetAtPath(SettingsPath) != null)
                throw new InvalidOperationException("기존 설정 에셋을 읽을 수 없습니다. 컴파일·임포트를 확인하세요.");
            settings = ScriptableObject.CreateInstance<FurnitureSpawnSettings>();
            settings.ConfigurePrototypeTargets();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            return settings;
        }

        private static Scene RequireScene()
        {
            if (Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("일반 에디터에서 Play를 종료하고 실행하세요.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                throw new InvalidOperationException("Game 씬을 열고 활성 씬으로 선택하세요.");
            return scene;
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name)
                    return root.transform;
            return null;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(group, "Add random furniture group");
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
