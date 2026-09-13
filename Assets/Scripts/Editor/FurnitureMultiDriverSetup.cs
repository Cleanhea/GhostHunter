using System;
using System.Collections.Generic;
using System.Linq;
using GhostHunter.Core;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Ghost;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 가구용 멀티 드라이버 데이터·프리팹·씬 배선을 설치한다(구현 지시 §4 FM-IMPL-1~3).
    /// 청소 시스템 설치 도구(<see cref="CleaningSetup"/>)와 같은 멱등 설치 + 검증 패턴을 따른다.
    ///
    /// <para>MD-1(2026-09-12 확정) 6종만 다룬다 — 더블 침대, 옷장 2종, 식탁, 선반 2종.
    /// 부품 풀 크기는 2026-09-12 씬 스캔 기준으로, <see cref="Room_Presets"/> A/B/C 중 실제로
    /// 대상 가구를 포함하는 프리셋의 최대 동시 존재 개수 + <c>House_01/PhysicsFurniture</c>의
    /// 현재 배치 개수를 더한 값이다. 식탁·선반 2종은 현재 <c>House_01/PhysicsFurniture</c>가
    /// 비어 있어 라이브 인스턴스가 0개다 — 레벨 배치 전까지는 분해할 대상이 없다(정상. §12 참고).</para>
    /// </summary>
    public static class FurnitureMultiDriverSetup
    {
        private const string SettingsPath = "Assets/Settings/Gameplay/FurnitureDriverSettings_Default.asset";
        private const string CatalogPath = "Assets/Settings/Gameplay/FurnitureDriverCatalog_Default.asset";
        private const string RecipeFolder = "Assets/Settings/Gameplay/FurnitureDriverRecipes";
        private const string DriverItemPath = "Assets/Settings/Gameplay/QuickSlotItem_Driver.asset";
        private const string PartPrefabFolder = "Assets/Prefabs/FurnitureDriverParts";
        private const string PartMeshFolder = "Assets/Settings/Gameplay/FurnitureDriverPartMeshes";
        private const string PlayerPath = "Assets/Prefabs/Player.prefab";
        private const string RootName = "FurnitureMultiDriverPrototype";
        private const int DriverSlotIndex = 1;

        private const string ThrowSettingsPath = "Assets/Settings/Gameplay/FurnitureThrowSettings_Default.asset";
        private const string LightDefinitionPath = "Assets/Settings/Gameplay/FurnitureDefinition_Light.asset";
        private const string OutlineMaterialPath = "Assets/Materials/Furniture_Outline.mat";
        private const string PartMaterialPath = "Assets/Materials/M_FurnitureDriverPart.mat";
        private const string UiSettingsPath = "Assets/Settings/Gameplay/FurnitureDriverUiSettings_Default.asset";

        private static readonly string[] LargeFurniturePrefabPaths =
        {
            "Assets/Prefabs/Furniture/DoubleBed_1.6x2.0.prefab",
            "Assets/Prefabs/Furniture/Wardrobe_1.2x0.6.prefab",
            "Assets/Prefabs/Furniture/Wardrobe_1.5x0.6.prefab",
            "Assets/Prefabs/Furniture/DiningTable_1.55x0.85.prefab",
            "Assets/Prefabs/Furniture/Shelving_2.6x0.55.prefab",
            "Assets/Prefabs/Furniture/Shelving_1.65x0.45.prefab",
        };

        private struct PartSpec
        {
            internal string PartId;
            internal int Count;
            internal Vector3 Size;
        }

        private struct RecipeSpec
        {
            internal string LargeFurnitureId;
            internal string DisplayName;
            internal int LiveInstanceCount;
            internal PartSpec[] Parts;
        }

        // MD-1(2026-09-12) 확정 6종. 부품 치수는 원본 가구 치수에서 유도한 그레이박스 임시값이고,
        // LiveInstanceCount는 같은 날 Game.unity 스캔 결과다(Furniture_Library·비교용 집 제외).
        private static readonly RecipeSpec[] Recipes =
        {
            new RecipeSpec
            {
                LargeFurnitureId = "DoubleBed_1.6x2.0", DisplayName = "더블 침대", LiveInstanceCount = 1,
                Parts = new[]
                {
                    new PartSpec { PartId = "Mattress", Count = 1, Size = new Vector3(1.6f, 0.25f, 2.0f) },
                    new PartSpec { PartId = "BedHead", Count = 1, Size = new Vector3(1.6f, 0.9f, 0.08f) },
                    new PartSpec { PartId = "BedLeg", Count = 1, Size = new Vector3(1.6f, 0.15f, 2.0f) },
                },
            },
            new RecipeSpec
            {
                LargeFurnitureId = "Wardrobe_1.2x0.6", DisplayName = "옷장(1.2m)", LiveInstanceCount = 1,
                Parts = new[]
                {
                    new PartSpec { PartId = "DoorPanel_1.2", Count = 1, Size = new Vector3(1.2f, 1.85f, 0.05f) },
                    new PartSpec { PartId = "Hanger_1.2", Count = 1, Size = new Vector3(0.9f, 0.05f, 0.5f) },
                    new PartSpec { PartId = "Clothes_1.2", Count = 1, Size = new Vector3(0.6f, 0.6f, 0.3f) },
                },
            },
            new RecipeSpec
            {
                LargeFurnitureId = "Wardrobe_1.5x0.6", DisplayName = "옷장(1.5m)", LiveInstanceCount = 1,
                Parts = new[]
                {
                    new PartSpec { PartId = "DoorPanel_1.5", Count = 1, Size = new Vector3(1.5f, 1.85f, 0.05f) },
                    new PartSpec { PartId = "Hanger_1.5", Count = 1, Size = new Vector3(0.9f, 0.05f, 0.5f) },
                    new PartSpec { PartId = "Clothes_1.5", Count = 1, Size = new Vector3(0.6f, 0.6f, 0.3f) },
                },
            },
            new RecipeSpec
            {
                LargeFurnitureId = "DiningTable_1.55x0.85", DisplayName = "식탁", LiveInstanceCount = 0,
                Parts = new[]
                {
                    new PartSpec { PartId = "TableTop_Dining", Count = 1, Size = new Vector3(1.55f, 0.05f, 0.85f) },
                    new PartSpec { PartId = "TableLeg_Dining", Count = 4, Size = new Vector3(0.08f, 0.74f, 0.08f) },
                },
            },
            new RecipeSpec
            {
                LargeFurnitureId = "Shelving_2.6x0.55", DisplayName = "큰 선반", LiveInstanceCount = 0,
                Parts = new[]
                {
                    new PartSpec { PartId = "ShelfFrame_2.6", Count = 1, Size = new Vector3(2.6f, 1.9f, 0.1f) },
                    new PartSpec { PartId = "ShelfBoard_2.6", Count = 4, Size = new Vector3(2.6f, 0.05f, 0.5f) },
                },
            },
            new RecipeSpec
            {
                LargeFurnitureId = "Shelving_1.65x0.45", DisplayName = "작은 선반", LiveInstanceCount = 0,
                Parts = new[]
                {
                    new PartSpec { PartId = "ShelfFrame_1.65", Count = 1, Size = new Vector3(1.65f, 1.65f, 0.08f) },
                    new PartSpec { PartId = "ShelfBoard_1.65", Count = 3, Size = new Vector3(1.65f, 0.05f, 0.4f) },
                },
            },
        };

        [MenuItem("GhostHunter/가구용 멀티 드라이버 설치", priority = 13)]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play 모드를 종료한 뒤 설치하세요.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != QuickSlotSetup.ScenePath)
                throw new InvalidOperationException("Game 씬을 연 뒤 실행하세요.");

            FurnitureDriverSettings settings = LoadOrCreate<FurnitureDriverSettings>(SettingsPath);
            FurnitureThrowSettings throwSettings = LoadRequired<FurnitureThrowSettings>(ThrowSettingsPath);
            FurnitureDefinition lightDefinition = LoadRequired<FurnitureDefinition>(LightDefinitionPath);
            Material outlineMaterial = LoadRequired<Material>(OutlineMaterialPath);
            Material partMaterial = LoadMaterial(PartMaterialPath, "Universal Render Pipeline/Lit",
                new Color(0.62f, 0.55f, 0.42f));

            FurnitureDisassemblyRecipe[] recipeAssets = InstallRecipes();
            FurnitureDriverCatalog catalog = LoadOrCreate<FurnitureDriverCatalog>(CatalogPath);
            catalog.Configure(recipeAssets);
            EditorUtility.SetDirty(catalog);

            AddPoolItemToLargeFurniturePrefabs();
            MarkLiveLargeFurnitureActive(scene);
            QuickSlotLoadout loadout = InstallDriverItem();
            InstallPlayer(settings, catalog, loadout);

            Transform root = FindOrCreateRoot(scene);
            Transform partsRoot = Child(root, "Parts");
            List<FurnitureDriverPoolItem> allParts = InstallPartPool(
                partsRoot, throwSettings, lightDefinition, outlineMaterial, partMaterial);

            FurnitureAssemblyZone zone = InstallAssemblyZone(root, scene, catalog);
            InstallActionHud(scene, LoadOrCreate<FurnitureDriverUiSettings>(UiSettingsPath));

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Game 씬을 저장하지 못했습니다.");
            PrototypeSceneSetup.RefreshScenePlacedNetworkObjectsInCurrentScene(scene);

            Validate();
            Debug.Log($"[FurnitureMultiDriverSetup] 레시피 {recipeAssets.Length}종, 부품 {allParts.Count}개, " +
                "조립 영역 1개를 설치하고 Player 프리팹과 Game 씬을 저장했습니다.", zone);
        }

        /// <summary>
        /// 행동 시간 원형 게이지 HUD(기획서 §6.1)만 Game 씬 PrototypeUI에 멱등으로 붙이고 드라이버
        /// 안내 문구를 갱신한다. 부품 풀·가구 프리팹·Player 프리팹은 다시 저장하지 않는다.
        /// </summary>
        [MenuItem("GhostHunter/가구용 멀티 드라이버 행동 UI 설치", priority = 15)]
        public static void InstallActionHudOnly()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play 모드를 종료한 뒤 설치하세요.");
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != QuickSlotSetup.ScenePath)
                throw new InvalidOperationException("Game 씬을 연 뒤 실행하세요.");

            InstallDriverItem();
            InstallActionHud(scene, LoadOrCreate<FurnitureDriverUiSettings>(UiSettingsPath));

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException("Game 씬을 저장하지 못했습니다.");

            ValidateActionHud(scene);
            Debug.Log("[FurnitureMultiDriverSetup] 행동 시간 원형 게이지 HUD를 PrototypeUI에 설치하고 Game 씬을 저장했습니다.");
        }

        [MenuItem("GhostHunter/가구용 멀티 드라이버 검증", priority = 14)]
        public static void Validate()
        {
            GameObject player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
            var driver = player.GetComponent<PlayerFurnitureDriverController>();
            if (driver == null)
                throw new MissingComponentException("PlayerFurnitureDriverController 배선 누락");
            foreach (string field in new[] { "_input", "_camera", "_loadout", "_settings", "_catalog" })
                RequireReference(driver, field);

            QuickSlotLoadout loadout = AssetDatabase.LoadAssetAtPath<QuickSlotLoadout>(QuickSlotSetup.LoadoutPath);
            if (loadout.GetSlot(DriverSlotIndex) == null || !loadout.GetSlot(DriverSlotIndex).IsDriver)
                throw new InvalidOperationException($"{DriverSlotIndex}번 슬롯에 드라이버가 없습니다.");

            FurnitureDriverCatalog catalog = AssetDatabase.LoadAssetAtPath<FurnitureDriverCatalog>(CatalogPath);
            if (catalog.Recipes.Count != Recipes.Length)
                throw new InvalidOperationException("카탈로그 레시피 개수가 MD-1 확정 6종과 다릅니다.");

            foreach (string prefabPath in LargeFurniturePrefabPaths)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                var poolItem = prefab.GetComponent<FurnitureDriverPoolItem>();
                if (poolItem == null || poolItem.PoolKey != System.IO.Path.GetFileNameWithoutExtension(prefabPath))
                    throw new InvalidOperationException($"{prefabPath}에 FurnitureDriverPoolItem 배선 누락");
            }

            Scene scene = SceneManager.GetActiveScene();
            GameObject root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == RootName);
            if (root == null)
                throw new InvalidOperationException($"{RootName} 루트가 씬에 없습니다.");

            var zone = root.GetComponentInChildren<FurnitureAssemblyZone>(true);
            if (zone == null)
                throw new InvalidOperationException("FurnitureAssemblyZone이 없습니다.");
            RequireReference(zone, "_catalog");
            RequireReference(zone, "_trigger");

            var seen = new HashSet<long>();
            var networkObjects = new List<NetworkObject>(root.GetComponentsInChildren<NetworkObject>(true));
            foreach (NetworkObject item in networkObjects)
            {
                var state = new SerializedObject(item);
                long hash = state.FindProperty("GlobalObjectIdHash").longValue;
                if (hash == 0 || !seen.Add(hash) || !state.FindProperty("m_InScenePlaced").boolValue)
                    throw new InvalidOperationException($"{item.name}의 NGO 식별자 누락/중복");
            }

            int expectedParts = Recipes.Sum(recipe =>
                recipe.Parts.Sum(part => part.Count * Math.Max(1, recipe.LiveInstanceCount)));
            Transform partsRoot = root.transform.Find("Parts");
            int actualParts = partsRoot != null
                ? partsRoot.GetComponentsInChildren<FurnitureDriverPoolItem>(true).Length
                : 0;
            if (actualParts != expectedParts)
                throw new InvalidOperationException($"부품 풀 개수가 예상과 다릅니다(기대 {expectedParts}, 실제 {actualParts}).");

            foreach (RecipeSpec recipe in Recipes)
            {
                if (recipe.LiveInstanceCount <= 0)
                    continue;
                int activeCount = Object
                    .FindObjectsByType<FurnitureDriverPoolItem>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Count(item => item.PoolKey == recipe.LargeFurnitureId
                        && new SerializedObject(item).FindProperty("_startActive").boolValue);
                if (activeCount != recipe.LiveInstanceCount)
                {
                    throw new InvalidOperationException(
                        $"{recipe.LargeFurnitureId}의 시작 활성 인스턴스 수가 예상과 다릅니다" +
                        $"(기대 {recipe.LiveInstanceCount}, 실제 {activeCount}) — 이미 배치된 가구가 " +
                        "숨겨진 채로 스폰될 수 있습니다.");
                }
            }

            ValidateActionHud(scene);

            Debug.Log("[FurnitureMultiDriverSetup] 배선과 NGO 식별자 검증 통과", root);
        }

        private static FurnitureDisassemblyRecipe[] InstallRecipes()
        {
            if (!AssetDatabase.IsValidFolder(RecipeFolder))
                AssetDatabase.CreateFolder("Assets/Settings/Gameplay", "FurnitureDriverRecipes");

            var assets = new FurnitureDisassemblyRecipe[Recipes.Length];
            for (int i = 0; i < Recipes.Length; i++)
            {
                RecipeSpec spec = Recipes[i];
                string path = $"{RecipeFolder}/FurnitureDisassemblyRecipe_{spec.LargeFurnitureId}.asset";
                FurnitureDisassemblyRecipe recipe = LoadOrCreate<FurnitureDisassemblyRecipe>(path);
                recipe.Configure(spec.LargeFurnitureId, spec.DisplayName,
                    spec.Parts.Select(part => new FurniturePartRequirement(part.PartId, part.Count)).ToArray());
                EditorUtility.SetDirty(recipe);
                assets[i] = recipe;
            }
            return assets;
        }

        /// <summary>
        /// 큰 가구 프리팹 6종에 <see cref="FurnitureDriverPoolItem"/>을 제자리 편집으로 추가한다.
        /// SaveAsPrefabAsset 재굽기(전체 재생성)가 아니라 LoadPrefabContents 편집이라
        /// GlobalObjectIdHash가 바뀌지 않는다(가구 프리팹 재굽기 해시 드리프트 메모 참고).
        /// </summary>
        private static void AddPoolItemToLargeFurniturePrefabs()
        {
            foreach (string prefabPath in LargeFurniturePrefabPaths)
            {
                string poolKey = System.IO.Path.GetFileNameWithoutExtension(prefabPath);
                GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
                try
                {
                    FurnitureDriverPoolItem poolItem = root.GetComponent<FurnitureDriverPoolItem>();
                    if (poolItem == null)
                        poolItem = root.AddComponent<FurnitureDriverPoolItem>();
                    poolItem.Configure(poolKey);
                    EditorUtility.SetDirty(poolItem);
                    PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            }
        }

        /// <summary>
        /// 이미 방에 배치되어 있던 큰 가구 실 인스턴스를 처음부터 보이고 사용 가능하게 표시한다.
        /// <see cref="FurnitureDriverPoolItem"/>은 기본이 비활성(부품 풀·조립 대기 사본과 같은
        /// 취급)이라, 이 표시가 없으면 이미 방에 있던 침대·옷장까지 스폰 즉시 숨고 잡을 수 없게
        /// 된다(§4.1 §4.2 사용 불가 버그). <c>Furniture_Library</c>·<c>Furniture_TEMP</c>·
        /// 비교용 집(이름에 <c>OriginalScale</c> 포함) 아래는 진열용 사본이라 제외한다.
        /// </summary>
        private static void MarkLiveLargeFurnitureActive(Scene scene)
        {
            var largeKeys = new HashSet<string>(
                LargeFurniturePrefabPaths.Select(System.IO.Path.GetFileNameWithoutExtension));

            int marked = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (FurnitureDriverPoolItem item in root.GetComponentsInChildren<FurnitureDriverPoolItem>(true))
                {
                    if (!largeKeys.Contains(item.PoolKey) || IsUnderExcludedRoot(item.transform))
                        continue;

                    var serialized = new SerializedObject(item);
                    SerializedProperty startActive = serialized.FindProperty("_startActive");
                    if (startActive.boolValue)
                        continue;
                    startActive.boolValue = true;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(item);
                    marked++;
                }
            }
            Debug.Log($"[FurnitureMultiDriverSetup] 이미 배치된 큰 가구 {marked}개를 시작 활성 상태로 표시했습니다.");
        }

        private static bool IsUnderExcludedRoot(Transform t)
        {
            for (Transform cur = t; cur != null; cur = cur.parent)
            {
                if (cur.name == "Furniture_Library" || cur.name == "Furniture_TEMP"
                    || cur.name.Contains("OriginalScale"))
                    return true;
            }
            return false;
        }

        private static QuickSlotLoadout InstallDriverItem()
        {
            QuickSlotItemDefinition driver = LoadOrCreate<QuickSlotItemDefinition>(DriverItemPath);
            var item = new SerializedObject(driver);
            item.FindProperty("_id").stringValue = "furniture_driver";
            item.FindProperty("_displayName").stringValue = "가구용 멀티 드라이버";
            item.FindProperty("_description").stringValue =
                "장착한 뒤 분해 가능한 가구에 우클릭을 누르고 있으면 분해, " +
                "조립 영역의 초록 실루엣에 우클릭을 누르고 있으면 조립합니다. 떼면 중단됩니다.";
            item.FindProperty("_isDriver").boolValue = true;
            item.FindProperty("_placeholderColor").colorValue = new Color(0.85f, 0.65f, 0.2f);
            item.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(driver);

            QuickSlotLoadout loadout = AssetDatabase.LoadAssetAtPath<QuickSlotLoadout>(QuickSlotSetup.LoadoutPath);
            if (loadout == null)
                throw new InvalidOperationException("퀵슬롯 로드아웃이 없습니다 — 청소 시스템 설치를 먼저 실행하세요.");
            var slots = new SerializedObject(loadout);
            slots.FindProperty("_slots").GetArrayElementAtIndex(DriverSlotIndex).objectReferenceValue = driver;
            slots.ApplyModifiedPropertiesWithoutUndo();
            return loadout;
        }

        private static void InstallPlayer(FurnitureDriverSettings settings, FurnitureDriverCatalog catalog,
            QuickSlotLoadout loadout)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPath);
            try
            {
                Transform camera = root.transform.Find("CameraPivot/PlayerCamera");
                if (camera == null)
                    throw new MissingReferenceException("플레이어 시점 카메라가 없습니다.");
                PlayerFurnitureDriverController driver = root.GetComponent<PlayerFurnitureDriverController>();
                if (driver == null)
                    driver = root.AddComponent<PlayerFurnitureDriverController>();
                Set(driver, "_input", root.GetComponent<PlayerInputReader>());
                Set(driver, "_camera", camera.GetComponent<Camera>());
                Set(driver, "_loadout", loadout);
                Set(driver, "_settings", settings);
                Set(driver, "_catalog", catalog);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.ImportAsset(PlayerPath, ImportAssetOptions.ForceUpdate);
        }

        private static List<FurnitureDriverPoolItem> InstallPartPool(Transform partsRoot,
            FurnitureThrowSettings throwSettings, FurnitureDefinition lightDefinition,
            Material outlineMaterial, Material partMaterial)
        {
            if (!AssetDatabase.IsValidFolder(PartPrefabFolder))
                AssetDatabase.CreateFolder("Assets/Prefabs", "FurnitureDriverParts");
            if (!AssetDatabase.IsValidFolder(PartMeshFolder))
                AssetDatabase.CreateFolder("Assets/Settings/Gameplay", "FurnitureDriverPartMeshes");

            var placed = new List<FurnitureDriverPoolItem>();
            float spawnX = 0f;
            foreach (RecipeSpec recipe in Recipes)
            {
                int sets = Math.Max(1, recipe.LiveInstanceCount);
                foreach (PartSpec part in recipe.Parts)
                {
                    GameObject prefab = GetOrCreatePartPrefab(part, throwSettings, lightDefinition,
                        outlineMaterial, partMaterial);
                    int totalInstances = part.Count * sets;
                    var existing = new List<FurnitureDriverPoolItem>();
                    Transform group = Child(partsRoot, part.PartId);
                    foreach (Transform child in group)
                        if (child.TryGetComponent(out FurnitureDriverPoolItem poolItem))
                            existing.Add(poolItem);

                    while (existing.Count < totalInstances)
                    {
                        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, group.gameObject.scene);
                        Undo.RegisterCreatedObjectUndo(instance, "Add furniture driver part");
                        instance.transform.SetParent(group, false);
                        instance.transform.localPosition = new Vector3(spawnX, -50f, existing.Count * 1.5f);
                        instance.name = $"{part.PartId}_{existing.Count + 1:00}";
                        existing.Add(instance.GetComponent<FurnitureDriverPoolItem>());
                    }
                    spawnX += 3f;
                    placed.AddRange(existing);
                }
            }
            return placed;
        }

        private static GameObject GetOrCreatePartPrefab(PartSpec part, FurnitureThrowSettings throwSettings,
            FurnitureDefinition lightDefinition, Material outlineMaterial, Material partMaterial)
        {
            string prefabPath = $"{PartPrefabFolder}/{part.PartId}.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
                return prefab;

            var root = new GameObject(part.PartId);
            try
            {
                root.layer = LayerMask.NameToLayer(GameLayers.FurnitureName);

                GameObject temporary = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Mesh mesh = Object.Instantiate(temporary.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(temporary);
                Vector3[] vertices = mesh.vertices;
                for (int i = 0; i < vertices.Length; i++)
                    vertices[i] = Vector3.Scale(vertices[i], part.Size);
                mesh.vertices = vertices;
                mesh.RecalculateBounds();
                mesh.name = part.PartId + "Mesh";
                AssetDatabase.CreateAsset(mesh, $"{PartMeshFolder}/{part.PartId}Mesh.asset");

                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = partMaterial;
                BoxCollider collider = root.AddComponent<BoxCollider>();
                collider.size = part.Size;

                Renderer[] outlineRenderers = CreateOutlineShell(root.transform, mesh, outlineMaterial);

                Rigidbody body = root.AddComponent<Rigidbody>();
                body.mass = lightDefinition.Mass;
                body.linearDamping = 0.05f;
                body.angularDamping = 0.5f;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.isKinematic = true;

                root.AddComponent<NetworkObject>();
                NetworkTransform networkTransform = root.AddComponent<NetworkTransform>();
                PrototypeSceneSetup.SetBoolean(networkTransform, "Interpolate", true);

                FurnitureNetworkPhysics networkPhysics = root.AddComponent<FurnitureNetworkPhysics>();
                Set(networkPhysics, "_definition", lightDefinition);

                FurnitureGrabTarget grabTarget = root.AddComponent<FurnitureGrabTarget>();
                Set(grabTarget, "_settings", throwSettings);

                FurnitureHoverMotor hover = root.AddComponent<FurnitureHoverMotor>();
                Set(hover, "_settings", throwSettings);

                FurnitureLauncher launcher = root.AddComponent<FurnitureLauncher>();
                Set(launcher, "_settings", throwSettings);

                FurnitureOutline outline = root.AddComponent<FurnitureOutline>();
                SetArray(outline, "_outlineRenderers", outlineRenderers.Cast<Object>().ToArray());

                FurnitureDriverPoolItem poolItem = root.AddComponent<FurnitureDriverPoolItem>();
                poolItem.Configure(part.PartId);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            return AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        }

        private static Renderer[] CreateOutlineShell(Transform root, Mesh mesh, Material outlineMaterial)
        {
            var shell = new GameObject($"{root.name}_Outline") { layer = root.gameObject.layer };
            shell.transform.SetParent(root, false);
            shell.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer shellRenderer = shell.AddComponent<MeshRenderer>();
            shellRenderer.sharedMaterial = outlineMaterial;
            shellRenderer.enabled = false;
            return new Renderer[] { shellRenderer };
        }

        private static FurnitureAssemblyZone InstallAssemblyZone(Transform root, Scene scene,
            FurnitureDriverCatalog catalog)
        {
            // MD-2(2026-09-12 확정) — 기존 임시 드릴 카 세이프 존 자리를 그대로 재사용한다.
            GameObject safeZone = scene.GetRootGameObjects()
                .FirstOrDefault(item => item.GetComponent<DrillCarSafeZone>() != null);
            if (safeZone == null)
                throw new InvalidOperationException("DrillCarSafeZone_Temp가 씬에 없습니다 — MD-2 재사용 대상이 없습니다.");

            Transform zoneTransform = Child(root, "AssemblyZone");
            zoneTransform.position = safeZone.transform.position;
            zoneTransform.rotation = safeZone.transform.rotation;

            if (zoneTransform.GetComponent<NetworkObject>() == null)
                Undo.AddComponent<NetworkObject>(zoneTransform.gameObject);

            BoxCollider trigger = zoneTransform.GetComponent<BoxCollider>();
            if (trigger == null)
                trigger = zoneTransform.gameObject.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(3f, 2.5f, 3f);
            trigger.center = new Vector3(0f, 1.25f, 0f);

            FurnitureAssemblyZone zone = zoneTransform.GetComponent<FurnitureAssemblyZone>();
            if (zone == null)
                zone = zoneTransform.gameObject.AddComponent<FurnitureAssemblyZone>();
            zone.Configure(catalog, trigger);
            EditorUtility.SetDirty(zone);
            return zone;
        }

        private static void InstallActionHud(Scene scene, FurnitureDriverUiSettings uiSettings)
        {
            GameObject hudObject = FindSceneObject(scene, QuickSlotSetup.HudObjectName);
            if (hudObject == null)
                throw new InvalidOperationException("Game 씬에 PrototypeUI가 없습니다 — 퀵슬롯 HUD 설치를 먼저 실행하세요.");

            FurnitureDriverActionHud hud = hudObject.GetComponent<FurnitureDriverActionHud>();
            if (hud == null)
                hud = Undo.AddComponent<FurnitureDriverActionHud>(hudObject);
            Set(hud, "_uiSettings", uiSettings);
        }

        private static void ValidateActionHud(Scene scene)
        {
            GameObject hudObject = FindSceneObject(scene, QuickSlotSetup.HudObjectName);
            FurnitureDriverActionHud hud = hudObject != null
                ? hudObject.GetComponent<FurnitureDriverActionHud>()
                : null;
            if (hud == null)
                throw new MissingComponentException("Game 씬 PrototypeUI에 FurnitureDriverActionHud가 없습니다.");
            RequireReference(hud, "_uiSettings");
        }

        private static GameObject FindSceneObject(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform found = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == objectName);
                if (found != null)
                    return found.gameObject;
            }
            return null;
        }

        private static Transform FindOrCreateRoot(Scene scene)
        {
            Transform root = scene.GetRootGameObjects()
                .FirstOrDefault(item => item.name == RootName)?.transform;
            if (root != null)
                return root;
            root = new GameObject(RootName).transform;
            Undo.RegisterCreatedObjectUndo(root.gameObject, "Install furniture multidriver");
            SceneManager.MoveGameObjectToScene(root.gameObject, scene);
            return root;
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

        private static T LoadRequired<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new MissingReferenceException($"{path} 에셋이 없습니다.");
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
