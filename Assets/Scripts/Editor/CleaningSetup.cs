using System;
using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Cleaning;
using GhostHunter.Gameplay.Map;
using GhostHunter.Gameplay.Player;
using GhostHunter.Systems.Installers;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>대걸레 장착과 B안 얼룩 풀을 설치하고 참조·네트워크 식별자를 검증한다.</summary>
    public static class CleaningSetup
    {
        public const string SettingsPath = "Assets/Settings/Gameplay/CleaningSettings_Default.asset";
        public const string StainPrefabPath = "Assets/Prefabs/CleaningStain.prefab";
        private const string MopItemPath = "Assets/Settings/Gameplay/QuickSlotItem_Mop.asset";
        private const string PlayerPath = "Assets/Prefabs/Player.prefab";
        private const string RootName = "CleaningPrototype";

        [MenuItem("GhostHunter/청소 시스템 설치", priority = 11)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play 모드를 종료한 뒤 설치하세요.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != QuickSlotSetup.ScenePath)
                throw new InvalidOperationException("Game 씬을 연 뒤 실행하세요.");
            GameInstaller installer = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<GameInstaller>(true)).Single();
            FurnitureSpawnController furniture = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<FurnitureSpawnController>(true)).Single();

            CleaningSettings settings = LoadOrCreate<CleaningSettings>(SettingsPath);
            QuickSlotSetup.InstallIntoActiveGameScene(false);
            QuickSlotLoadout loadout = InstallLoadout();
            GameObject stainPrefab = CreateStainPrefab(settings);
            InstallPlayer(settings, loadout);
            Transform root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == RootName)?.transform;
            if (root == null)
            {
                root = new GameObject(RootName).transform;
                Undo.RegisterCreatedObjectUndo(root.gameObject, "Install cleaning");
                SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            }
            Transform pool = Child(root, "Stains");
            pool.localPosition = Vector3.down * 60f;
            var stains = pool.GetComponentsInChildren<CleaningStain>(true).ToList();
            while (stains.Count < settings.StainCount)
            {
                var item = (GameObject)PrefabUtility.InstantiatePrefab(stainPrefab, scene);
                Undo.RegisterCreatedObjectUndo(item, "Add cleaning stain");
                item.transform.SetParent(pool, false);
                item.transform.localPosition = Vector3.right * stains.Count * 2f;
                item.name = $"Stain_{stains.Count + 1:00}";
                stains.Add(item.GetComponent<CleaningStain>());
            }
            Transform controllerRoot = Child(root, "Controller");
            if (controllerRoot.GetComponent<NetworkObject>() == null)
                Undo.AddComponent<NetworkObject>(controllerRoot.gameObject);
            CleaningController controller = controllerRoot.GetComponent<CleaningController>();
            if (controller == null)
                controller = Undo.AddComponent<CleaningController>(controllerRoot.gameObject);
            Set(controller, "_settings", settings);
            Set(controller, "_furniture", furniture);
            SetArray(controller, "_stains", stains.Cast<Object>().ToArray());
            SetArray(controller, "_points", furniture.Points
                .Where(point => point.SpawnType == FurnitureSpawnType.Floor)
                .Select(point => (Object)point.transform).ToArray());
            Set(installer, "_cleaning", controller);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Game 씬을 저장하지 못했습니다.");
            PrototypeSceneSetup.RefreshScenePlacedNetworkObjectsInCurrentScene(scene);
            Validate();
            Debug.Log("[CleaningSetup] 대걸레·얼룩 풀·F1 초기화를 설치하고 Player 프리팹과 Game 씬을 저장했습니다.", controller);
        }

        [MenuItem("GhostHunter/청소 시스템 검증", priority = 12)]
        public static void Validate()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var cleaning = player.GetComponent<PlayerCleaningController>();
            if (cleaning == null)
                throw new MissingComponentException("PlayerCleaningController 배선 누락");
            foreach (string field in new[] { "_input", "_camera", "_loadout", "_settings", "_mopView" })
                RequireReference(cleaning, field);
            QuickSlotLoadout loadout = AssetDatabase.LoadAssetAtPath<QuickSlotLoadout>(QuickSlotSetup.LoadoutPath);
            if (loadout.GetSlot(0) == null || !loadout.GetSlot(0).IsMop)
                throw new InvalidOperationException("0번 슬롯에 대걸레가 없습니다.");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StainPrefabPath);
            CleaningStain stain = prefab.GetComponent<CleaningStain>();
            foreach (string field in new[] { "_settings", "_visual", "_hitCollider", "_marker" })
                RequireReference(stain, field);
            var identity = new SerializedObject(prefab.GetComponent<NetworkObject>());
            if (identity.FindProperty("GlobalObjectIdHash").longValue == 0
                || identity.FindProperty("m_InScenePlaced").boolValue)
                throw new InvalidOperationException("얼룩 프리팹 NGO 식별자가 유효하지 않습니다.");
            GameObject root = SceneManager.GetActiveScene().GetRootGameObjects()
                .Single(item => item.name == RootName);
            CleaningController controller = root.GetComponentInChildren<CleaningController>(true);
            RequireReference(controller, "_settings");
            RequireReference(controller, "_furniture");
            SerializedObject serialized = new(controller);
            if (serialized.FindProperty("_stains").arraySize == 0
                || serialized.FindProperty("_points").arraySize == 0)
                throw new InvalidOperationException("얼룩 풀 또는 배치 후보 누락");
            var seen = new HashSet<long>();
            foreach (NetworkObject item in root.GetComponentsInChildren<NetworkObject>(true))
            {
                var state = new SerializedObject(item);
                long hash = state.FindProperty("GlobalObjectIdHash").longValue;
                if (hash == 0 || !seen.Add(hash) || !state.FindProperty("m_InScenePlaced").boolValue)
                    throw new InvalidOperationException("씬 얼룩 NGO 식별자 누락/중복");
            }
            Debug.Log("[CleaningSetup] 배선과 NGO 식별자 검증 통과", root);
        }

        private static QuickSlotLoadout InstallLoadout()
        {
            QuickSlotItemDefinition mop = LoadOrCreate<QuickSlotItemDefinition>(MopItemPath);
            var item = new SerializedObject(mop);
            item.FindProperty("_id").stringValue = "mop";
            item.FindProperty("_displayName").stringValue = "대걸레";
            item.FindProperty("_description").stringValue = "장착한 뒤 얼룩을 조준하고 좌클릭으로 닦습니다.";
            item.FindProperty("_isMop").boolValue = true;
            item.FindProperty("_placeholderColor").colorValue = new Color(0.25f, 0.8f, 0.7f);
            item.ApplyModifiedPropertiesWithoutUndo();
            QuickSlotLoadout loadout = AssetDatabase.LoadAssetAtPath<QuickSlotLoadout>(QuickSlotSetup.LoadoutPath);
            var slots = new SerializedObject(loadout);
            slots.FindProperty("_slots").GetArrayElementAtIndex(0).objectReferenceValue = mop;
            slots.ApplyModifiedPropertiesWithoutUndo();
            // 기존 더미 항목을 대걸레 해제 경로로 표시한다. 별도 인벤토리는 만들지 않는다.
            QuickSlotItemDefinition hands = loadout.GetSlot(2);
            if (hands != null && hands.Id == "id2")
            {
                var handsData = new SerializedObject(hands);
                handsData.FindProperty("_displayName").stringValue = "맨손";
                handsData.FindProperty("_description").stringValue = "대걸레를 내려놓고 가구를 잡습니다.";
                handsData.ApplyModifiedPropertiesWithoutUndo();
            }
            return loadout;
        }

        private static GameObject CreateStainPrefab(CleaningSettings settings)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StainPrefabPath);
            if (prefab == null)
            {
                Material material = LoadMaterial("Assets/Materials/M_CleaningStain.mat",
                    "GhostHunter/CleaningStain", new Color(0.22f, 0.075f, 0.025f, 0.9f));
                var root = new GameObject("CleaningStain");
                try
                {
                    root.AddComponent<NetworkObject>();
                    Transform visual = Child(root.transform, "Visual");
                    var mesh = new Mesh { name = "CleaningStainQuad" };
                    mesh.vertices = new[] { new Vector3(-0.6f, 0f, -0.6f), new Vector3(-0.6f, 0f, 0.6f),
                        new Vector3(0.6f, 0f, 0.6f), new Vector3(0.6f, 0f, -0.6f) };
                    mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
                    mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                    mesh.RecalculateNormals();
                    mesh.RecalculateBounds();
                    AssetDatabase.CreateAsset(mesh, "Assets/Settings/Gameplay/CleaningStainMesh.asset");
                    visual.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
                    MeshRenderer renderer = visual.gameObject.AddComponent<MeshRenderer>();
                    renderer.sharedMaterial = material;
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    var hit = root.AddComponent<BoxCollider>();
                    hit.isTrigger = true;
                    hit.size = new Vector3(0.9f, 0.025f, 0.9f);
                    DetectionTargetMarker marker = root.AddComponent<DetectionTargetMarker>();
                    var markerData = new SerializedObject(marker);
                    markerData.FindProperty("_kind").enumValueIndex = (int)DetectionTargetKind.Stain;
                    markerData.FindProperty("_targetActive").boolValue = false;
                    markerData.ApplyModifiedPropertiesWithoutUndo();
                    CleaningStain stain = root.AddComponent<CleaningStain>();
                    Set(stain, "_settings", settings);
                    Set(stain, "_visual", renderer);
                    Set(stain, "_hitCollider", hit);
                    Set(stain, "_marker", marker);
                    PrefabUtility.SaveAsPrefabAsset(root, StainPrefabPath);
                }
                finally { Object.DestroyImmediate(root); }
                AssetDatabase.ImportAsset(StainPrefabPath, ImportAssetOptions.ForceUpdate);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StainPrefabPath);
                EditorUtility.SetDirty(prefab.GetComponent<NetworkObject>());
                EditorUtility.SetDirty(prefab);
            }
            NetworkPrefabsList prefabs = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if (!prefabs.PrefabList.Any(entry => entry.Prefab == prefab))
            {
                prefabs.Add(new NetworkPrefab { Prefab = prefab });
                EditorUtility.SetDirty(prefabs);
            }
            return prefab;
        }

        private static void InstallPlayer(CleaningSettings settings, QuickSlotLoadout loadout)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                Transform camera = root.transform.Find("CameraPivot/PlayerCamera");
                if (camera == null)
                    throw new MissingReferenceException("플레이어 시점 카메라가 없습니다.");
                PlayerCleaningController cleaning = root.GetComponent<PlayerCleaningController>();
                if (cleaning == null)
                    cleaning = root.AddComponent<PlayerCleaningController>();
                Transform mop = camera.Find("MopView");
                if (mop == null)
                {
                    mop = Child(camera, "MopView");
                    Material handle = LoadMaterial("Assets/Materials/M_MopHandle.mat",
                        "Universal Render Pipeline/Lit", new Color(0.26f, 0.64f, 0.62f));
                    Material head = LoadMaterial("Assets/Materials/M_MopHead.mat",
                        "Universal Render Pipeline/Lit", new Color(0.72f, 0.70f, 0.56f));
                    CreateMopPart(mop, "Handle", new Vector3(0.30f, -0.27f, 0.55f),
                        new Vector3(0.025f, 0.025f, 0.85f), handle, Quaternion.Euler(30f, -9f, 0f));
                    CreateMopPart(mop, "Head", new Vector3(0.24f, -0.49f, 0.92f),
                        new Vector3(0.30f, 0.05f, 0.14f), head, Quaternion.identity);
                    mop.gameObject.SetActive(false);
                }
                Set(cleaning, "_settings", settings);
                Set(cleaning, "_loadout", loadout);
                Set(cleaning, "_input", root.GetComponent<PlayerInputReader>());
                Set(cleaning, "_camera", camera.GetComponent<Camera>());
                Set(cleaning, "_mopView", mop);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.ImportAsset(PlayerPath, ImportAssetOptions.ForceUpdate);
        }

        private static void CreateMopPart(Transform parent, string name, Vector3 position,
            Vector3 size, Material material, Quaternion rotation)
        {
            GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = Object.Instantiate(temporary.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(temporary);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = Vector3.Scale(vertices[i], size);
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            mesh.name = "Mop" + name;
            AssetDatabase.CreateAsset(mesh, $"Assets/Settings/Gameplay/Mop{name}Mesh.asset");
            Transform part = Child(parent, name);
            part.SetLocalPositionAndRotation(position, rotation);
            part.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static Material LoadMaterial(string path, string shaderName, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
                return material;
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
                throw new MissingReferenceException(shaderName);
            material = new Material(shader);
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Transform Child(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
                return child;
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
            return child;
        }

        private static void Set(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(field).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetArray(Object target, string field, Object[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                array.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RequireReference(Object target, string field)
        {
            if (target == null || new SerializedObject(target).FindProperty(field).objectReferenceValue == null)
                throw new MissingReferenceException(field + " 배선 누락");
        }
    }
}
