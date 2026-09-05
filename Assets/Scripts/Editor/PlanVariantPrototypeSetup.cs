using System;
using System.Collections.Generic;
using System.Globalization;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Map;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// HousePlanC.png(B안)와 HousePlanB.png(C안)을 Game 씬 오른쪽에 나란히 만드는 전용
    /// 비교용 생성기다. 기존 House_01, 원본 배율 집, MAP-1 그레이박스는 건드리지 않는다.
    ///
    /// 씬/YAML을 직접 고치지 않고 Unity Editor 메뉴에서만 실행한다. 구조물은 정적 프리미티브,
    /// 가구는 이미 구워 둔 Furniture 프리팹의 in-scene 인스턴스로 만든다.
    /// </summary>
    internal static class PlanVariantPrototypeSetup
    {
        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string SettingsPath =
            "Assets/Settings/Gameplay/PlanVariantPrototypeSettings_Default.asset";
        private const string FurnitureFolder = "Assets/Prefabs/Furniture";
        private const string PlanBRootName = "House_Prototype_PlanB";
        private const string PlanCRootName = "House_Prototype_PlanC";
        private const string FurnitureGroupName = "Furniture_TEMP";
        private const string RoomGroupName = "Rooms_Prototype";
        private const string StructureGroupName = "Structure";
        private const string ConnectorGroupName = "ConnectorFloor_TEMP";
        private const string TemporaryLightsGroupName = "Lights_TEMP";
        private const string PlayerPrefabPath = "Assets/Prefabs/Player.prefab";
        private const float BoundsTolerance = 0.05f;
        private static readonly Dictionary<Vector3, Mesh> CubeMeshCache = new();
        private static PlanVariantPrototypeSettings CubeMeshAssetOwner;

        private enum Variant
        {
            PlanB,
            PlanC,
        }

        private enum DoorSide
        {
            North,
            South,
            East,
            West,
        }

        private enum RoomKind
        {
            Bedroom,
            Guest,
            Living,
            Family,
            Study,
            Dining,
            Kitchen,
            Bathroom,
            Laundry,
            Storage,
            Entrance,
            Playroom,
            SecretStorage,
        }

        private sealed class RoomSpec
        {
            internal readonly string Id;
            internal readonly string Label;
            internal readonly Vector2 Center;
            internal readonly float Width;
            internal readonly float Depth;
            internal readonly RoomKind Kind;
            internal readonly DoorSide Door;

            internal RoomSpec(
                string id,
                string label,
                float centerX,
                float centerZ,
                float width,
                float depth,
                RoomKind kind,
                DoorSide door)
            {
                Id = id;
                Label = label;
                Center = new Vector2(centerX, centerZ);
                Width = width;
                Depth = depth;
                Kind = kind;
                Door = door;
            }
        }

        private sealed class FloorSpec
        {
            internal readonly int Index;
            internal readonly string Name;
            internal readonly string Label;
            internal readonly float Width;
            internal readonly float Depth;
            internal readonly float HallWidth;
            internal readonly float HallDepth;
            internal readonly RoomSpec[] Rooms;
            internal readonly RoomSpec Entry;

            internal FloorSpec(
                int index,
                string name,
                string label,
                float width,
                float depth,
                float hallWidth,
                float hallDepth,
                RoomSpec[] rooms,
                RoomSpec entry = null)
            {
                Index = index;
                Name = name;
                Label = label;
                Width = width;
                Depth = depth;
                HallWidth = hallWidth;
                HallDepth = hallDepth;
                Rooms = rooms;
                Entry = entry;
            }
        }

        private sealed class PlanSpec
        {
            internal readonly Variant Variant;
            internal readonly string RootName;
            internal readonly string Label;
            internal readonly float MainWidth;
            internal readonly float MainDepth;
            internal readonly float AtticWidth;
            internal readonly float AtticDepth;
            internal readonly FloorSpec[] Floors;

            internal PlanSpec(
                Variant variant,
                string rootName,
                string label,
                float mainWidth,
                float mainDepth,
                float atticWidth,
                float atticDepth,
                FloorSpec[] floors)
            {
                Variant = variant;
                RootName = rootName;
                Label = label;
                MainWidth = mainWidth;
                MainDepth = mainDepth;
                AtticWidth = atticWidth;
                AtticDepth = atticDepth;
                Floors = floors;
            }
        }

        private readonly struct RectSpec
        {
            internal readonly float CenterX;
            internal readonly float CenterZ;
            internal readonly float Width;
            internal readonly float Depth;

            internal RectSpec(float centerX, float centerZ, float width, float depth)
            {
                CenterX = centerX;
                CenterZ = centerZ;
                Width = width;
                Depth = depth;
            }

            internal float MinX => CenterX - Width * 0.5f;
            internal float MaxX => CenterX + Width * 0.5f;
            internal float MinZ => CenterZ - Depth * 0.5f;
            internal float MaxZ => CenterZ + Depth * 0.5f;

            internal bool Contains(float x, float z)
            {
                return x > MinX + 0.0001f
                    && x < MaxX - 0.0001f
                    && z > MinZ + 0.0001f
                    && z < MaxZ - 0.0001f;
            }
        }

        private sealed class StairSpec
        {
            internal readonly string Name;
            internal readonly int LowerFloor;
            internal readonly int UpperFloor;
            internal readonly float X;
            internal readonly float StartZ;
            internal readonly float TopZ;
            internal readonly float Run;
            internal readonly float LowerY;
            internal readonly float UpperY;

            internal StairSpec(
                string name,
                int lowerFloor,
                int upperFloor,
                float x,
                float startZ,
                float topZ,
                float run,
                float lowerY,
                float upperY)
            {
                Name = name;
                LowerFloor = lowerFloor;
                UpperFloor = upperFloor;
                X = x;
                StartZ = startZ;
                TopZ = topZ;
                Run = run;
                LowerY = lowerY;
                UpperY = upperY;
            }

            internal RectSpec Opening(float width, float margin)
            {
                return new RectSpec(
                    X,
                    (StartZ + TopZ) * 0.5f,
                    width + margin * 2f,
                    Run + margin * 2f);
            }
        }

        private sealed class Palette
        {
            internal readonly Material WoodFloor;
            internal readonly Material TileFloor;
            internal readonly Material Wall;
            internal readonly Material Trim;
            internal readonly Material Wood;
            internal readonly Material Fabric;
            internal readonly Material Window;

            internal Palette(
                Material woodFloor,
                Material tileFloor,
                Material wall,
                Material trim,
                Material wood,
                Material fabric,
                Material window)
            {
                WoodFloor = woodFloor;
                TileFloor = tileFloor;
                Wall = wall;
                Trim = trim;
                Wood = wood;
                Fabric = fabric;
                Window = window;
            }
        }

        private static readonly string[] RequiredFurnitureKeys =
        {
            "DoubleBed_1.6x2.0",
            "Nightstand_0.45x0.4",
            "Wardrobe_1.2x0.6",
            "Dresser_0.8x0.45",
            "Sofa_2.2x0.9",
            "CoffeeTable_1.25x0.65",
            "LivingConsole_1.6x0.45",
            "Desk_1.2x0.6",
            "DeskChair_0.55x0.55",
            "Bookshelf_0.9x0.3",
            "DiningTable_1.55x0.85",
            "Chair_0.5x0.5",
            "Shelving_2.6x0.55",
            "Shelving_1.65x0.45",
            "Crate_0.6",
            "ToyChest_0.9x0.45",
            "StorageChest_0.9x0.45",
            "LaundryBasket_0.5x0.4",
            "TrashBin_0.3",
            "Vanity_1.0x0.5",
            "VanityStool_0.45x0.45",
        };

        [MenuItem("GhostHunter/맵 B·C 프로토타입 추가 (실내·계단·가구)", priority = 3)]
        public static void AddPlanVariantPrototypes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play Mode를 종료한 뒤 실행하세요.");

            Scene scene = SceneManager.GetActiveScene();
            RequireGameScene(scene);

            PlanVariantPrototypeSettings settings = LoadOrCreateSettings();
            PrepareCubeMeshCache(settings);
            Palette palette = LoadPalette();
            Dictionary<string, GameObject> furniture = LoadFurniturePrefabs(settings.PlaceFurniture);
            PlanSpec planB = CreatePlan(Variant.PlanB);
            PlanSpec planC = CreatePlan(Variant.PlanC);

            Transform existingB = FindRoot(scene, planB.RootName);
            Transform existingC = FindRoot(scene, planC.RootName);
            if (existingB == null && existingC != null)
            {
                throw new InvalidOperationException(
                    $"{PlanCRootName}만 남아 있어 B→C 배치 순서를 확정할 수 없습니다. " +
                    $"{PlanCRootName}을 먼저 Undo/삭제한 뒤 메뉴를 다시 실행하세요.");
            }
            if (existingB != null)
                Validate(existingB, planB, settings);
            if (existingC != null)
                Validate(existingC, planC, settings);

            List<Transform> preserved = FindPreservedMapRoots(scene);
            Bounds occupied = CalculateColliderBounds(preserved, "기존 맵");
            if (existingB != null)
                occupied = Encapsulate(occupied, CalculateColliderBounds(
                    new[] { existingB }, planB.RootName));
            if (existingC != null)
                occupied = Encapsulate(occupied, CalculateColliderBounds(
                    new[] { existingC }, planC.RootName));
            Transform createdB = null;
            Transform createdC = null;
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add B/C mansion prototypes");

            try
            {
                if (existingB == null)
                {
                    Bounds previousBounds = occupied;
                    Vector3 position = PositionRightOf(previousBounds, planB, settings.VariantGap);
                    createdB = CreateVariant(scene, planB, settings, palette, furniture, position);
                    CreateConnectorFloor(createdB, previousBounds, planB, settings, palette);
                    Validate(createdB, planB, settings);
                    Undo.RegisterCreatedObjectUndo(createdB.gameObject, "Add B mansion prototype");
                    occupied = Encapsulate(occupied, CalculateColliderBounds(
                        new[] { createdB }, planB.RootName));
                }

                if (existingC == null)
                {
                    Bounds previousBounds = occupied;
                    Vector3 position = PositionRightOf(previousBounds, planC, settings.VariantGap);
                    createdC = CreateVariant(scene, planC, settings, palette, furniture, position);
                    CreateConnectorFloor(createdC, previousBounds, planC, settings, palette);
                    Validate(createdC, planC, settings);
                    Undo.RegisterCreatedObjectUndo(createdC.gameObject, "Add C mansion prototype");
                }
                Undo.CollapseUndoOperations(undoGroup);
            }
            catch
            {
                if (createdC != null)
                    Object.DestroyImmediate(createdC.gameObject);
                if (createdB != null)
                    Object.DestroyImmediate(createdB.gameObject);
                throw;
            }

            if (createdB == null && createdC == null)
            {
                Selection.activeTransform = existingB != null ? existingB : existingC;
                SceneView sceneView = SceneView.lastActiveSceneView;
                if (sceneView != null)
                    sceneView.FrameSelected();
                Debug.Log(
                    "[PlanVariantPrototypeSetup] B·C 프로토타입이 이미 있어 덮어쓰지 않고 검증·선택만 했습니다.");
                return;
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
                throw new InvalidOperationException($"{ScenePath} 저장에 실패했습니다.");

            // 씬에 배치한 Furniture 프리팹의 NGO 식별자를 저장·빌드 목록 기준으로 확정한다.
            PrototypeSceneSetup.RefreshScenePlacedNetworkObjectsForGeneratedPrototype();

            Scene savedScene = SceneManager.GetActiveScene();
            Transform selectedRoot = createdC != null ? createdC : createdB;
            Selection.activeTransform = selectedRoot;
            SceneView activeSceneView = SceneView.lastActiveSceneView;
            if (activeSceneView != null)
                activeSceneView.FrameSelected();
            Transform reportedB = createdB != null ? createdB : existingB;
            Transform reportedC = createdC != null ? createdC : existingC;
            Debug.Log(
                $"[PlanVariantPrototypeSetup] {PlanBRootName}/{PlanCRootName}을 {savedScene.path}에 저장했습니다. " +
                $"B안 {CountFurniture(reportedB)}개, C안 {CountFurniture(reportedC)}개 가구. " +
                "계단 경사 콜라이더와 상부 바닥 개구부를 포함한 [TEMP] 비교용 결과입니다.");
        }

        [MenuItem("GhostHunter/맵 B·C 프로토타입 검증", priority = 4)]
        public static void ValidatePlanVariantPrototypes()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play Mode를 종료한 뒤 실행하세요.");

            Scene scene = SceneManager.GetActiveScene();
            RequireGameScene(scene);
            PlanVariantPrototypeSettings settings = LoadSettings();
            Transform planB = FindRoot(scene, PlanBRootName);
            Transform planC = FindRoot(scene, PlanCRootName);
            if (planB == null || planC == null)
                throw new MissingReferenceException(
                    $"{PlanBRootName}와 {PlanCRootName}을 모두 만든 뒤 검증하세요.");

            Validate(planB, CreatePlan(Variant.PlanB), settings);
            Validate(planC, CreatePlan(Variant.PlanC), settings);
            Debug.Log(
                $"[PlanVariantPrototypeSetup] 검증 통과: B안 가구 {CountFurniture(planB)}개 / " +
                $"C안 가구 {CountFurniture(planC)}개, 계단 각 4개, 방 각 21개(7/8/6).");
        }

        private static void RequireGameScene(Scene scene)
        {
            if (scene.path != ScenePath)
                throw new InvalidOperationException(
                    $"{ScenePath}를 연 뒤 실행하세요. 현재 활성 씬은 '{scene.path}'입니다.");
        }

        private static PlanVariantPrototypeSettings LoadSettings()
        {
            PlanVariantPrototypeSettings settings =
                AssetDatabase.LoadAssetAtPath<PlanVariantPrototypeSettings>(SettingsPath);
            if (settings == null)
                throw new MissingReferenceException(
                    $"설정 에셋이 없습니다: {SettingsPath}. B·C 프로토타입 추가 메뉴를 먼저 실행하세요.");
            return settings;
        }

        private static PlanVariantPrototypeSettings LoadOrCreateSettings()
        {
            PlanVariantPrototypeSettings settings =
                AssetDatabase.LoadAssetAtPath<PlanVariantPrototypeSettings>(SettingsPath);
            if (settings != null)
                return settings;

            EnsureFolder("Assets/Settings/Gameplay");
            settings = ScriptableObject.CreateInstance<PlanVariantPrototypeSettings>();
            AssetDatabase.CreateAsset(settings, SettingsPath);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            return settings;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path);
            if (parent != null)
                parent = parent.Replace('\\', '/');
            string leaf = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(leaf))
                throw new InvalidOperationException($"폴더 경로가 잘못됐습니다: {path}");
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }

        private static Palette LoadPalette()
        {
            return new Palette(
                LoadMaterial("Assets/Materials/Map_Floor_Wood.mat"),
                LoadMaterial("Assets/Materials/Map_Floor_Tile.mat"),
                LoadMaterial("Assets/Materials/Map_Wall.mat"),
                LoadMaterial("Assets/Materials/Map_Trim.mat"),
                LoadMaterial("Assets/Materials/Map_Furniture_Wood.mat"),
                LoadMaterial("Assets/Materials/Map_Furniture_Fabric.mat"),
                LoadMaterial("Assets/Materials/Map_Window.mat"));
        }

        private static Material LoadMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
                throw new MissingReferenceException($"맵 재료를 찾지 못했습니다: {path}");
            return material;
        }

        private static Dictionary<string, GameObject> LoadFurniturePrefabs(bool required)
        {
            var result = new Dictionary<string, GameObject>(StringComparer.Ordinal);
            if (!required)
                return result;

            foreach (string key in RequiredFurnitureKeys)
            {
                string path = $"{FurnitureFolder}/{key}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    throw new MissingReferenceException(
                        $"가구 프리팹을 찾지 못했습니다: {path}. '프로토타입 게임 생성'을 먼저 실행하세요.");
                if (prefab.GetComponent<NetworkObject>() == null
                    || prefab.GetComponent<Rigidbody>() == null
                    || prefab.GetComponent<FurnitureGrabTarget>() == null
                    || prefab.GetComponent<FurnitureNetworkPhysics>() == null)
                {
                    throw new MissingComponentException(
                        $"{path} 루트에 NetworkObject/Rigidbody/Furniture 컴포넌트가 모두 필요합니다.");
                }

                result.Add(key, prefab);
            }

            return result;
        }

        private static Transform CreateVariant(
            Scene scene,
            PlanSpec plan,
            PlanVariantPrototypeSettings settings,
            Palette palette,
            Dictionary<string, GameObject> furniture,
            Vector3 worldPosition)
        {
            GameObject rootObject = new GameObject(plan.RootName);
            rootObject.transform.position = worldPosition;
            Transform root = rootObject.transform;

            try
            {
                List<StairSpec> stairs = CreateStairSpecs(plan, settings);
                for (int i = 0; i < plan.Floors.Length; i++)
                {
                    FloorSpec floor = plan.Floors[i];
                    CreateFloor(
                        root,
                        floor,
                        settings,
                        palette,
                        OpeningsForFloor(stairs, floor.Index, settings),
                        furniture);
                }

                Transform stairsGroup = CreateGroup("Stairs_A_B_TEMP", root);
                foreach (StairSpec stair in stairs)
                    CreateStair(stairsGroup, stair, settings, palette);

                CreateFrontYard(root, plan, settings, palette);

                if (settings.CreateLabels)
                {
                    CreateTextLabel(
                        "PrototypeTitle",
                        $"{plan.Label} [TEMP]\n{plan.MainWidth:0.#} x {plan.MainDepth:0.#}m / attic {plan.AtticWidth:0.#} x {plan.AtticDepth:0.#}m",
                        new Vector3(0f, 0.06f, -plan.MainDepth * 0.5f - 0.8f),
                        root,
                        0.12f);
                }

                return root;
            }
            catch
            {
                Object.DestroyImmediate(rootObject);
                throw;
            }
        }

        private static void CreateFrontYard(
            Transform root,
            PlanSpec plan,
            PlanVariantPrototypeSettings settings,
            Palette palette)
        {
            CreateCube(
                "FrontYard_TEMP",
                new Vector3(
                    0f,
                    -settings.FloorThickness * 0.5f,
                    -plan.MainDepth * 0.5f - settings.FrontYardDepth * 0.5f),
                new Vector3(plan.MainWidth, settings.FloorThickness, settings.FrontYardDepth),
                palette.WoodFloor,
                root,
                true);
        }

        private static void CreateConnectorFloor(
            Transform root,
            Bounds previousBounds,
            PlanSpec plan,
            PlanVariantPrototypeSettings settings,
            Palette palette)
        {
            Transform connectorRoot = CreateGroup(ConnectorGroupName, root);
            float halfWidth = plan.MainWidth * 0.5f;
            float pathHalfWidth = settings.ConnectorWidth * 0.5f;
            float westPathX = root.position.x - halfWidth - pathHalfWidth;
            float frontYardZ = root.position.z
                - plan.MainDepth * 0.5f
                - settings.FrontYardDepth * 0.5f;
            float routeZ = previousBounds.center.z;
            float fromX = previousBounds.max.x;
            float horizontalWidth = westPathX - fromX;

            if (horizontalWidth > 0.05f)
            {
                CreateCube(
                    "Connector_Horizontal_TEMP",
                    new Vector3(
                        (fromX + westPathX) * 0.5f - root.position.x,
                        -settings.FloorThickness * 0.5f,
                        routeZ - root.position.z),
                    new Vector3(horizontalWidth, settings.FloorThickness, settings.ConnectorWidth),
                    palette.WoodFloor,
                    connectorRoot,
                    true);
            }

            float verticalDepth = Mathf.Abs(routeZ - frontYardZ);
            if (verticalDepth > 0.05f)
            {
                CreateCube(
                    "Connector_Vertical_TEMP",
                    new Vector3(
                        westPathX - root.position.x,
                        -settings.FloorThickness * 0.5f,
                        (routeZ + frontYardZ) * 0.5f - root.position.z),
                    new Vector3(settings.ConnectorWidth, settings.FloorThickness, verticalDepth),
                    palette.WoodFloor,
                    connectorRoot,
                    true);
            }

            if (settings.CreateLabels)
            {
                CreateTextLabel(
                    "ConnectorLabel",
                    "WALKABLE CONNECTOR [TEMP]",
                    new Vector3(
                        westPathX - root.position.x,
                        0.06f,
                        (routeZ + frontYardZ) * 0.5f - root.position.z),
                    connectorRoot,
                    0.06f);
            }
        }

        private static void CreateFloor(
            Transform root,
            FloorSpec floor,
            PlanVariantPrototypeSettings settings,
            Palette palette,
            List<RectSpec> openings,
            Dictionary<string, GameObject> furniture)
        {
            Transform floorRoot = CreateGroup(floor.Name, root);
            floorRoot.localPosition = new Vector3(0f, floor.Index * settings.FloorPitch, 0f);
            Transform structure = CreateGroup(StructureGroupName, floorRoot);
            Transform rooms = CreateGroup(RoomGroupName, floorRoot);
            Transform circulation = CreateGroup("Circulation_TEMP", floorRoot);
            Transform furnitureRoot = CreateGroup(FurnitureGroupName, floorRoot);

            CreateDimensionsMarker("FloorDimensions", floor.Width, floor.Depth, structure);
            CreateSurfaceWithHoles(
                structure,
                "FloorSurface",
                0f,
                0f,
                floor.Width,
                floor.Depth,
                -settings.FloorThickness * 0.5f,
                settings.FloorThickness,
                palette.WoodFloor,
                openings,
                true);
            CreateExteriorWalls(
                structure,
                floor.Width,
                floor.Depth,
                floor.Entry != null,
                settings,
                palette.Wall);
            CreateExteriorWindows(structure, floor.Width, floor.Depth, floor.Entry != null, settings, palette);

            if (settings.PlaceTemporaryLights)
                CreateTemporaryLight(floorRoot, floor, settings);

            foreach (RoomSpec room in floor.Rooms)
            {
                CreateRoom(rooms, room, settings, palette);
                if (settings.PlaceFurniture)
                {
                    Transform roomFurniture = CreateGroup($"Furniture_{room.Id}", furnitureRoot);
                    roomFurniture.localPosition = new Vector3(room.Center.x, 0f, room.Center.y);
                    FurnishRoom(roomFurniture, room, settings, furniture);
                }
            }

            // 도면의 현관은 이동 동선에 포함되는 진입 공간으로 두고, 기획서의 방 수
            // 7/8/6 집계에서는 제외한다. 그래도 실제 실내와 가구 배치는 생략하지 않는다.
            if (floor.Entry != null)
            {
                CreateRoom(circulation, floor.Entry, settings, palette);
                if (settings.PlaceFurniture)
                {
                    Transform entryFurniture = CreateGroup(
                        $"EntryFurniture_{floor.Entry.Id}",
                        furnitureRoot);
                    entryFurniture.localPosition = new Vector3(
                        floor.Entry.Center.x,
                        0f,
                        floor.Entry.Center.y);
                    FurnishRoom(entryFurniture, floor.Entry, settings, furniture);
                }
            }

            CreateSurfaceWithHoles(
                circulation,
                floor.Index == 1 ? "GalleryFloor_SOLID_TEMP" : "HallFloor_TEMP",
                0f,
                0f,
                floor.HallWidth,
                floor.HallDepth,
                0.018f,
                0.035f,
                palette.WoodFloor,
                openings,
                false);

            float landingDepth = floor.Depth * 0.5f - floor.HallDepth * 0.5f;
            if (landingDepth > 0.25f)
            {
                CreateSurfaceWithHoles(
                    circulation,
                    "NorthStairLanding_TEMP",
                    0f,
                    floor.HallDepth * 0.5f + landingDepth * 0.5f,
                    floor.HallWidth,
                    landingDepth,
                    0.02f,
                    0.03f,
                    palette.WoodFloor,
                    openings,
                    false);
            }

            if (settings.CreateLabels)
            {
                CreateTextLabel(
                    "FloorLabel",
                    $"{floor.Label} [TEMP]",
                    new Vector3(0f, 0.055f, -floor.Depth * 0.5f + 0.55f),
                    floorRoot,
                    0.1f);
                if (floor.Index == 1)
                {
                    CreateTextLabel(
                        "GalleryDecision",
                        "GALLERY FLOOR\nSOLID [TEMP] — OPEN VOID TBD",
                        new Vector3(0f, 0.06f, 0f),
                        circulation,
                        0.075f);
                }
            }
        }

        private static void CreateExteriorWalls(
            Transform parent,
            float width,
            float depth,
            bool hasFrontDoor,
            PlanVariantPrototypeSettings settings,
            Material material)
        {
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            CreateCube(
                "Outer_North",
                new Vector3(0f, settings.WallHeight * 0.5f, halfDepth),
                new Vector3(width + settings.WallThickness, settings.WallHeight, settings.WallThickness),
                material,
                parent);
            float frontDoorHalf = Mathf.Min(
                settings.FrontDoorWidth * 0.5f,
                halfWidth - settings.WallThickness - 0.15f);
            if (!hasFrontDoor || frontDoorHalf <= 0.05f)
            {
                CreateCube(
                    "Outer_South",
                    new Vector3(0f, settings.WallHeight * 0.5f, -halfDepth),
                    new Vector3(width + settings.WallThickness, settings.WallHeight, settings.WallThickness),
                    material,
                    parent);
            }
            else
            {
                CreateWall(
                    "Outer_South_A",
                    true,
                    -halfDepth,
                    -halfWidth,
                    -frontDoorHalf,
                    settings,
                    material,
                    parent);
                CreateWall(
                    "Outer_South_B",
                    true,
                    -halfDepth,
                    frontDoorHalf,
                    halfWidth,
                    settings,
                    material,
                    parent);
                CreateDoorFrame(
                    parent,
                    "Front",
                    true,
                    -halfDepth,
                    frontDoorHalf,
                    settings,
                    material);
            }
            CreateCube(
                "Outer_West",
                new Vector3(-halfWidth, settings.WallHeight * 0.5f, 0f),
                new Vector3(settings.WallThickness, settings.WallHeight, depth),
                material,
                parent);
            CreateCube(
                "Outer_East",
                new Vector3(halfWidth, settings.WallHeight * 0.5f, 0f),
                new Vector3(settings.WallThickness, settings.WallHeight, depth),
                material,
                parent);
        }

        private static void CreateExteriorWindows(
            Transform parent,
            float width,
            float depth,
            bool hasFrontDoor,
            PlanVariantPrototypeSettings settings,
            Palette palette)
        {
            Transform windows = CreateGroup("Windows_TEMP", parent);
            float windowWidth = Mathf.Min(settings.WindowWidth, width * 0.25f);
            float windowDepth = Mathf.Min(settings.WindowWidth, depth * 0.25f);
            float y = settings.WindowSillHeight + settings.WindowHeight * 0.5f;
            float northZ = depth * 0.5f - settings.WallThickness * 0.55f;
            float southZ = -depth * 0.5f + settings.WallThickness * 0.55f;
            float westX = -width * 0.5f + settings.WallThickness * 0.55f;
            float eastX = width * 0.5f - settings.WallThickness * 0.55f;

            CreateWindowX(
                "Window_North_Left",
                -width * 0.27f,
                northZ,
                y,
                windowWidth,
                settings,
                palette,
                windows);
            CreateWindowX(
                "Window_North_Right",
                width * 0.27f,
                northZ,
                y,
                windowWidth,
                settings,
                palette,
                windows);
            CreateWindowX(
                "Window_South_Left",
                hasFrontDoor ? -width * 0.27f : -width * 0.2f,
                southZ,
                y,
                windowWidth,
                settings,
                palette,
                windows);
            CreateWindowX(
                "Window_South_Right",
                hasFrontDoor ? width * 0.27f : width * 0.2f,
                southZ,
                y,
                windowWidth,
                settings,
                palette,
                windows);
            CreateWindowZ(
                "Window_West",
                westX,
                0f,
                y,
                windowDepth,
                settings,
                palette,
                windows);
            CreateWindowZ(
                "Window_East",
                eastX,
                0f,
                y,
                windowDepth,
                settings,
                palette,
                windows);
        }

        private static void CreateWindowX(
            string name,
            float x,
            float z,
            float y,
            float width,
            PlanVariantPrototypeSettings settings,
            Palette palette,
            Transform parent)
        {
            float frame = settings.WallThickness * 0.4f;
            CreateCube(
                $"{name}_Glass",
                new Vector3(x, y, z),
                new Vector3(width, settings.WindowHeight, 0.04f),
                palette.Window,
                parent,
                false);
            CreateCube(
                $"{name}_Top",
                new Vector3(x, y + settings.WindowHeight * 0.5f, z - 0.01f),
                new Vector3(width + frame, frame, frame),
                palette.Trim,
                parent,
                false);
            CreateCube(
                $"{name}_Bottom",
                new Vector3(x, y - settings.WindowHeight * 0.5f, z - 0.01f),
                new Vector3(width + frame, frame, frame),
                palette.Trim,
                parent,
                false);
            CreateCube(
                $"{name}_Left",
                new Vector3(x - width * 0.5f, y, z - 0.01f),
                new Vector3(frame, settings.WindowHeight, frame),
                palette.Trim,
                parent,
                false);
            CreateCube(
                $"{name}_Right",
                new Vector3(x + width * 0.5f, y, z - 0.01f),
                new Vector3(frame, settings.WindowHeight, frame),
                palette.Trim,
                parent,
                false);
        }

        private static void CreateWindowZ(
            string name,
            float x,
            float z,
            float y,
            float depth,
            PlanVariantPrototypeSettings settings,
            Palette palette,
            Transform parent)
        {
            float frame = settings.WallThickness * 0.4f;
            CreateCube(
                $"{name}_Glass",
                new Vector3(x, y, z),
                new Vector3(0.04f, settings.WindowHeight, depth),
                palette.Window,
                parent,
                false);
            CreateCube(
                $"{name}_Top",
                new Vector3(x - 0.01f, y + settings.WindowHeight * 0.5f, z),
                new Vector3(frame, frame, depth + frame),
                palette.Trim,
                parent,
                false);
            CreateCube(
                $"{name}_Bottom",
                new Vector3(x - 0.01f, y - settings.WindowHeight * 0.5f, z),
                new Vector3(frame, frame, depth + frame),
                palette.Trim,
                parent,
                false);
            CreateCube(
                $"{name}_Near",
                new Vector3(x - 0.01f, y, z - depth * 0.5f),
                new Vector3(frame, settings.WindowHeight, frame),
                palette.Trim,
                parent,
                false);
            CreateCube(
                $"{name}_Far",
                new Vector3(x - 0.01f, y, z + depth * 0.5f),
                new Vector3(frame, settings.WindowHeight, frame),
                palette.Trim,
                parent,
                false);
        }

        private static void CreateTemporaryLight(
            Transform floorRoot,
            FloorSpec floor,
            PlanVariantPrototypeSettings settings)
        {
            Transform lights = CreateGroup(TemporaryLightsGroupName, floorRoot);
            var lightObject = new GameObject("PointLight_TEMP");
            lightObject.transform.SetParent(lights, false);
            lightObject.transform.localPosition = new Vector3(
                0f,
                settings.WallHeight * 0.85f,
                0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = settings.TemporaryLightRange;
            light.intensity = settings.TemporaryLightIntensity;
            light.color = new Color(1f, 0.82f, 0.67f);
            light.shadows = LightShadows.None;
            lightObject.name = $"PointLight_{floor.Name}_TEMP";
        }

        private static void CreateRoom(
            Transform parent,
            RoomSpec room,
            PlanVariantPrototypeSettings settings,
            Palette palette)
        {
            Transform roomRoot = CreateGroup(room.Id, parent);
            roomRoot.localPosition = new Vector3(room.Center.x, 0f, room.Center.y);
            CreateDimensionsMarker("RoomDimensions", room.Width, room.Depth, roomRoot);

            Material floorMaterial = IsTile(room.Kind) ? palette.TileFloor : palette.WoodFloor;
            CreateCube(
                "RoomFloor",
                new Vector3(0f, 0.022f, 0f),
                new Vector3(
                    Mathf.Max(0.2f, room.Width - settings.WallThickness * 1.5f),
                    0.035f,
                    Mathf.Max(0.2f, room.Depth - settings.WallThickness * 1.5f)),
                floorMaterial,
                roomRoot,
                false);

            float halfWidth = room.Width * 0.5f;
            float halfDepth = room.Depth * 0.5f;
            CreateWallWithDoor(
                roomRoot,
                "North",
                true,
                halfDepth,
                halfWidth,
                settings,
                room.Door == DoorSide.North || room.Kind == RoomKind.Entrance,
                palette.Trim);
            CreateWallWithDoor(
                roomRoot,
                "South",
                true,
                -halfDepth,
                halfWidth,
                settings,
                room.Door == DoorSide.South || room.Kind == RoomKind.Entrance,
                palette.Trim);
            CreateWallWithDoor(
                roomRoot,
                "East",
                false,
                halfWidth,
                halfDepth,
                settings,
                room.Door == DoorSide.East,
                palette.Trim);
            CreateWallWithDoor(
                roomRoot,
                "West",
                false,
                -halfWidth,
                halfDepth,
                settings,
                room.Door == DoorSide.West,
                palette.Trim);

            if (settings.CreateLabels)
            {
                CreateTextLabel(
                    "Label",
                    $"{room.Label}\n{room.Width:0.#} x {room.Depth:0.#}m",
                    new Vector3(0f, 0.065f, 0f),
                    roomRoot,
                    0.06f);
            }
        }

        private static void CreateWallWithDoor(
            Transform parent,
            string sideName,
            bool alongX,
            float sidePosition,
            float halfLength,
            PlanVariantPrototypeSettings settings,
            bool hasDoor,
            Material material)
        {
            float doorHalf = Mathf.Min(settings.DoorWidth * 0.5f, halfLength - 0.15f);
            if (!hasDoor || doorHalf <= 0.05f)
            {
                CreateWall(
                    $"Wall_{sideName}",
                    alongX,
                    sidePosition,
                    -halfLength,
                    halfLength,
                    settings,
                    material,
                    parent);
                return;
            }

            CreateWall(
                $"Wall_{sideName}_A",
                alongX,
                sidePosition,
                -halfLength,
                -doorHalf,
                settings,
                material,
                parent);
            CreateWall(
                $"Wall_{sideName}_B",
                alongX,
                sidePosition,
                doorHalf,
                halfLength,
                settings,
                material,
                parent);
            CreateDoorFrame(parent, sideName, alongX, sidePosition, doorHalf, settings, material);
        }

        private static void CreateWall(
            string name,
            bool alongX,
            float sidePosition,
            float from,
            float to,
            PlanVariantPrototypeSettings settings,
            Material material,
            Transform parent)
        {
            if (to - from <= 0.05f)
                return;

            Vector3 position;
            Vector3 size;
            if (alongX)
            {
                position = new Vector3(
                    (from + to) * 0.5f,
                    settings.WallHeight * 0.5f,
                    sidePosition);
                size = new Vector3(
                    to - from,
                    settings.WallHeight,
                    settings.WallThickness);
            }
            else
            {
                position = new Vector3(
                    sidePosition,
                    settings.WallHeight * 0.5f,
                    (from + to) * 0.5f);
                size = new Vector3(
                    settings.WallThickness,
                    settings.WallHeight,
                    to - from);
            }

            CreateCube(name, position, size, material, parent);
        }

        private static void CreateDoorFrame(
            Transform parent,
            string sideName,
            bool alongX,
            float sidePosition,
            float doorHalf,
            PlanVariantPrototypeSettings settings,
            Material material)
        {
            const float frameWidth = 0.1f;
            const float frameDepth = 0.25f;
            float openingWidth = doorHalf * 2f;
            if (alongX)
            {
                CreateCube(
                    $"DoorFrame_{sideName}_Left",
                    new Vector3(-doorHalf, settings.WallHeight * 0.5f, sidePosition),
                    new Vector3(frameWidth, settings.WallHeight, frameDepth),
                    material,
                    parent);
                CreateCube(
                    $"DoorFrame_{sideName}_Right",
                    new Vector3(doorHalf, settings.WallHeight * 0.5f, sidePosition),
                    new Vector3(frameWidth, settings.WallHeight, frameDepth),
                    material,
                    parent);
                CreateCube(
                    $"DoorFrame_{sideName}_Header",
                    new Vector3(0f, settings.WallHeight - 0.12f, sidePosition),
                    new Vector3(openingWidth + frameWidth, 0.12f, frameDepth),
                    material,
                    parent);
            }
            else
            {
                CreateCube(
                    $"DoorFrame_{sideName}_Left",
                    new Vector3(sidePosition, settings.WallHeight * 0.5f, -doorHalf),
                    new Vector3(frameDepth, settings.WallHeight, frameWidth),
                    material,
                    parent);
                CreateCube(
                    $"DoorFrame_{sideName}_Right",
                    new Vector3(sidePosition, settings.WallHeight * 0.5f, doorHalf),
                    new Vector3(frameDepth, settings.WallHeight, frameWidth),
                    material,
                    parent);
                CreateCube(
                    $"DoorFrame_{sideName}_Header",
                    new Vector3(sidePosition, settings.WallHeight - 0.12f, 0f),
                    new Vector3(frameDepth, 0.12f, openingWidth + frameWidth),
                    material,
                    parent);
            }
        }

        private static void CreateStair(
            Transform parent,
            StairSpec stair,
            PlanVariantPrototypeSettings settings,
            Palette palette)
        {
            Transform stairRoot = CreateGroup(stair.Name, parent);
            float rise = stair.UpperY - stair.LowerY;
            float angle = Mathf.Atan2(rise, stair.Run) * Mathf.Rad2Deg;
            float length = Mathf.Sqrt(stair.Run * stair.Run + rise * rise);

            GameObject ramp = CreateCube(
                "Stair_Ramp_Collider",
                new Vector3(stair.X, stair.LowerY + rise * 0.5f, stair.StartZ + stair.Run * 0.5f),
                new Vector3(settings.StairWidth, settings.StairColliderThickness, length),
                palette.Trim,
                stairRoot,
                true,
                Quaternion.Euler(-angle, 0f, 0f));
            ramp.GetComponent<Collider>().isTrigger = false;

            float stepDepth = stair.Run / settings.StairStepCount;
            float stepRise = rise / settings.StairStepCount;
            for (int i = 0; i < settings.StairStepCount; i++)
            {
                float top = stair.LowerY + stepRise * (i + 1);
                CreateCube(
                    $"Step_{i + 1:00}",
                    new Vector3(
                        stair.X,
                        stair.LowerY + (top - stair.LowerY) * 0.5f,
                        stair.StartZ + stepDepth * (i + 0.5f)),
                    new Vector3(settings.StairWidth, top - stair.LowerY, stepDepth + 0.02f),
                    palette.Wood,
                    stairRoot,
                    false);
            }

            CreateStairRailings(stairRoot, stair, settings, palette, angle, length);

            Transform bottom = CreateGroup("BottomConnection", stairRoot);
            bottom.localPosition = new Vector3(stair.X, stair.LowerY, stair.StartZ);
            Transform topConnection = CreateGroup("TopConnection", stairRoot);
            topConnection.localPosition = new Vector3(stair.X, stair.UpperY, stair.TopZ);

            if (settings.CreateLabels)
            {
                CreateTextLabel(
                    "Label",
                    $"{stair.Name}\n{angle:0.0}° ramp [TEMP]",
                    new Vector3(stair.X, stair.LowerY + 0.08f, stair.StartZ + stair.Run * 0.5f),
                    parent,
                    0.055f);
            }
        }

        private static void CreateStairRailings(
            Transform stairRoot,
            StairSpec stair,
            PlanVariantPrototypeSettings settings,
            Palette palette,
            float angle,
            float length)
        {
            float railOffset = settings.StairWidth * 0.5f
                - settings.StairRailThickness * 0.5f
                - 0.05f;
            if (railOffset <= 0.05f)
                return;

            float railY = stair.LowerY + (stair.UpperY - stair.LowerY) * 0.5f
                + settings.StairRailHeight;
            for (int side = -1; side <= 1; side += 2)
            {
                float x = stair.X + railOffset * side;
                CreateCube(
                    $"Rail_{(side < 0 ? "Left" : "Right")}_Handrail",
                    new Vector3(x, railY, stair.StartZ + stair.Run * 0.5f),
                    new Vector3(
                        settings.StairRailThickness,
                        settings.StairRailThickness,
                        length),
                    palette.Trim,
                    stairRoot,
                    false,
                    Quaternion.Euler(-angle, 0f, 0f));
                CreateCube(
                    $"Rail_{(side < 0 ? "Left" : "Right")}_BottomPost",
                    new Vector3(x, stair.LowerY + settings.StairRailHeight * 0.5f, stair.StartZ),
                    new Vector3(
                        settings.StairRailThickness,
                        settings.StairRailHeight,
                        settings.StairRailThickness),
                    palette.Trim,
                    stairRoot,
                    false);
                CreateCube(
                    $"Rail_{(side < 0 ? "Left" : "Right")}_TopPost",
                    new Vector3(x, stair.UpperY + settings.StairRailHeight * 0.5f, stair.TopZ),
                    new Vector3(
                        settings.StairRailThickness,
                        settings.StairRailHeight,
                        settings.StairRailThickness),
                    palette.Trim,
                    stairRoot,
                    false);
            }
        }

        private static List<StairSpec> CreateStairSpecs(
            PlanSpec plan,
            PlanVariantPrototypeSettings settings)
        {
            float offset = plan.AtticWidth * settings.StairOffsetRatio;
            var result = new List<StairSpec>(4);
            AddPair(0, 1, plan.MainDepth, "01_02");
            AddPair(1, 2, plan.AtticDepth, "02_Attic");
            return result;

            void AddPair(int lower, int upper, float upperDepth, string suffix)
            {
                float topZ = upperDepth * 0.5f - settings.StairLandingDepth;
                float startZ = topZ - settings.StairRun;
                if (startZ <= -upperDepth * 0.5f + settings.StairLandingDepth)
                {
                    throw new InvalidOperationException(
                        $"{plan.Label} 계단 진행 길이가 상부 층 깊이에 맞지 않습니다. " +
                        "PlanVariantPrototypeSettings의 StairRun/StairLandingDepth를 확인하세요.");
                }

                float lowerY = lower * settings.FloorPitch;
                float upperY = upper * settings.FloorPitch;
                result.Add(new StairSpec(
                    $"Stair_A_{suffix}",
                    lower,
                    upper,
                    -offset,
                    startZ,
                    topZ,
                    settings.StairRun,
                    lowerY,
                    upperY));
                result.Add(new StairSpec(
                    $"Stair_B_{suffix}",
                    lower,
                    upper,
                    offset,
                    startZ,
                    topZ,
                    settings.StairRun,
                    lowerY,
                    upperY));
            }
        }

        private static List<RectSpec> OpeningsForFloor(
            List<StairSpec> stairs,
            int upperFloor,
            PlanVariantPrototypeSettings settings)
        {
            var result = new List<RectSpec>();
            foreach (StairSpec stair in stairs)
            {
                if (stair.UpperFloor == upperFloor)
                    result.Add(stair.Opening(settings.StairWidth, settings.StairOpeningMargin));
            }

            return result;
        }

        private static void FurnishRoom(
            Transform parent,
            RoomSpec room,
            PlanVariantPrototypeSettings settings,
            Dictionary<string, GameObject> furniture)
        {
            int serial = 0;
            void Add(string key, Vector2 local, float yaw = 0f)
            {
                if (!furniture.TryGetValue(key, out GameObject prefab))
                    throw new KeyNotFoundException($"가구 프리팹 키가 없습니다: {key}");

                GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(
                    prefab,
                    parent.gameObject.scene);
                instance.transform.SetParent(parent, false);
                instance.transform.localPosition = new Vector3(local.x, 0f, local.y);
                instance.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                instance.name = $"{key}_TEMP_{serial++:00}";
                HousePrototypeBuilder.RestOnSupport(
                    instance.transform,
                    parent.root.position.y + parent.parent.parent.localPosition.y);
                EditorUtility.SetDirty(instance);
            }

            float halfWidth = room.Width * 0.5f;
            float halfDepth = room.Depth * 0.5f;
            switch (room.Kind)
            {
                case RoomKind.Bedroom:
                case RoomKind.Guest:
                    Add("DoubleBed_1.6x2.0", new Vector2(0f, -halfDepth * 0.18f));
                    Add("Nightstand_0.45x0.4", new Vector2(-1.15f, -halfDepth * 0.18f));
                    Add("Nightstand_0.45x0.4", new Vector2(1.15f, -halfDepth * 0.18f));
                    Add("Wardrobe_1.2x0.6", new Vector2(halfWidth - 0.9f, halfDepth - 0.65f));
                    Add("Dresser_0.8x0.45", new Vector2(-halfWidth + 0.9f, halfDepth - 0.65f));
                    break;

                case RoomKind.Living:
                case RoomKind.Family:
                    Add("Sofa_2.2x0.9", new Vector2(0f, halfDepth * 0.22f), 180f);
                    // Keep collider clearance on both sides in the shallowest 5 m Family room.
                    Add("CoffeeTable_1.25x0.65", new Vector2(0f, -halfDepth * 0.11f));
                    Add("LivingConsole_1.6x0.45", new Vector2(0f, -halfDepth * 0.35f));
                    break;

                case RoomKind.Study:
                    Add("Desk_1.2x0.6", new Vector2(0f, -halfDepth * 0.25f));
                    Add("DeskChair_0.55x0.55", new Vector2(0f, halfDepth * 0.12f), 180f);
                    float shelfX = room.Door == DoorSide.East
                        ? -halfWidth * 0.55f
                        : halfWidth * 0.55f;
                    Add("Bookshelf_0.9x0.3", new Vector2(shelfX, 0f), 90f);
                    break;

                case RoomKind.Dining:
                    Add("DiningTable_1.55x0.85", new Vector2(0.25f, 0f));
                    Add("Chair_0.5x0.5", new Vector2(-0.85f, 0.8f));
                    Add("Chair_0.5x0.5", new Vector2(1.35f, 0.8f), 180f);
                    Add("Chair_0.5x0.5", new Vector2(-0.85f, -0.8f));
                    Add("Chair_0.5x0.5", new Vector2(1.35f, -0.8f), 180f);
                    break;

                case RoomKind.Kitchen:
                    Add("Shelving_2.6x0.55", new Vector2(-halfWidth * 0.35f, halfDepth * 0.32f));
                    Add("DiningTable_1.55x0.85", new Vector2(halfWidth * 0.18f, -0.1f));
                    Add("Chair_0.5x0.5", new Vector2(halfWidth * 0.18f - 1.15f, -0.1f));
                    Add("Chair_0.5x0.5", new Vector2(halfWidth * 0.18f + 1.15f, -0.1f), 180f);
                    break;

                case RoomKind.Bathroom:
                    // The 2.5 m attic bathroom needs both pieces shifted inward together.
                    float vanityZ = room.Door == DoorSide.North
                        ? -halfDepth * 0.16f
                        : halfDepth * 0.3f;
                    float stoolZ = room.Door == DoorSide.North
                        ? -halfDepth * 0.56f
                        : -halfDepth * 0.05f;
                    float trashX = room.Door == DoorSide.East
                        ? -halfWidth * 0.55f
                        : halfWidth * 0.55f;
                    Add("Vanity_1.0x0.5", new Vector2(0f, vanityZ));
                    Add("VanityStool_0.45x0.45", new Vector2(0f, stoolZ));
                    Add("TrashBin_0.3", new Vector2(trashX, -halfDepth * 0.25f));
                    break;

                case RoomKind.Laundry:
                    float laundryShelfZ = room.Door == DoorSide.West
                        ? halfDepth - 0.65f
                        : room.Door == DoorSide.East ? -halfDepth + 0.65f : -halfDepth * 0.28f;
                    Add("Shelving_1.65x0.45", new Vector2(0f, laundryShelfZ));
                    Add("LaundryBasket_0.5x0.4", new Vector2(-halfWidth * 0.45f, -halfDepth * 0.2f));
                    Add("TrashBin_0.3", new Vector2(halfWidth * 0.45f, -halfDepth * 0.2f));
                    break;

                case RoomKind.Storage:
                    bool sideDoor = room.Door == DoorSide.West || room.Door == DoorSide.East;
                    float storageShelfZ = sideDoor ? 0f : halfDepth * 0.28f;
                    float storageShelfX = room.Door == DoorSide.East
                        ? -halfWidth * 0.45f
                        : room.Door == DoorSide.West ? halfWidth * 0.45f : 0f;
                    string storageShelfKey = room.Width < 4f && room.Depth < 3.5f
                        ? "Shelving_1.65x0.45"
                        : "Shelving_2.6x0.55";
                    Add(
                        storageShelfKey,
                        new Vector2(storageShelfX, storageShelfZ),
                        sideDoor ? 90f : 0f);
                    Vector2 cratePosition = sideDoor
                        ? new Vector2(
                            room.Door == DoorSide.East ? -halfWidth * 0.14f : halfWidth * 0.14f,
                            halfDepth * 0.6f)
                        : new Vector2(-halfWidth * 0.42f, -halfDepth * 0.25f);
                    Vector2 chestPosition = sideDoor
                        ? new Vector2(
                            room.Door == DoorSide.East ? halfWidth * 0.23f : -halfWidth * 0.23f,
                            -halfDepth * 0.62f)
                        : new Vector2(halfWidth * 0.35f, -halfDepth * 0.25f);
                    Add("Crate_0.6", cratePosition);
                    Add("StorageChest_0.9x0.45", chestPosition);
                    break;

                case RoomKind.Entrance:
                    Add("LivingConsole_1.6x0.45", new Vector2(0f, halfDepth * 0.28f));
                    Add("StorageChest_0.9x0.45", new Vector2(0f, -halfDepth * 0.25f));
                    break;

                case RoomKind.Playroom:
                    Add("ToyChest_0.9x0.45", new Vector2(0f, halfDepth * 0.28f));
                    Add("Chair_0.5x0.5", new Vector2(0f, -halfDepth * 0.2f));
                    Add("Crate_0.6", new Vector2(-halfWidth * 0.4f, -halfDepth * 0.2f));
                    break;

                case RoomKind.SecretStorage:
                    Add("StorageChest_0.9x0.45", new Vector2(0f, 0f));
                    Add("Crate_0.6", new Vector2(Mathf.Max(0.4f, halfWidth - 0.65f), 0f));
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static bool IsTile(RoomKind kind)
        {
            return kind == RoomKind.Kitchen
                || kind == RoomKind.Bathroom
                || kind == RoomKind.Laundry
                || kind == RoomKind.Storage;
        }

        private static void PrepareCubeMeshCache(PlanVariantPrototypeSettings settings)
        {
            CubeMeshCache.Clear();
            CubeMeshAssetOwner = settings;
            foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SettingsPath))
            {
                if (asset is Mesh mesh && mesh.name.StartsWith("PlanVariantCube_", StringComparison.Ordinal))
                {
                    Vector3 key = ParseCubeMeshKey(mesh.name);
                    if (!float.IsNaN(key.x))
                        CubeMeshCache[key] = mesh;
                }
            }
        }

        private static Mesh GetSizedCubeMesh(Mesh unitCube, Vector3 size)
        {
            if (CubeMeshCache.TryGetValue(size, out Mesh cached))
                return cached;

            Mesh mesh = Object.Instantiate(unitCube);
            mesh.name = CubeMeshName(size);
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = Vector3.Scale(vertices[i], size);
            mesh.vertices = vertices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (CubeMeshAssetOwner != null && AssetDatabase.Contains(CubeMeshAssetOwner))
            {
                AssetDatabase.AddObjectToAsset(mesh, CubeMeshAssetOwner);
                mesh.hideFlags = HideFlags.HideInHierarchy;
                EditorUtility.SetDirty(mesh);
                EditorUtility.SetDirty(CubeMeshAssetOwner);
            }

            CubeMeshCache.Add(size, mesh);
            return mesh;
        }

        private static string CubeMeshName(Vector3 size)
        {
            return "PlanVariantCube_"
                + size.x.ToString("R", CultureInfo.InvariantCulture)
                + "_"
                + size.y.ToString("R", CultureInfo.InvariantCulture)
                + "_"
                + size.z.ToString("R", CultureInfo.InvariantCulture);
        }

        private static Vector3 ParseCubeMeshKey(string name)
        {
            string suffix = name.Substring("PlanVariantCube_".Length);
            string[] parts = suffix.Split('_');
            if (parts.Length != 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z))
            {
                return new Vector3(float.NaN, float.NaN, float.NaN);
            }

            return new Vector3(x, y, z);
        }

        private static GameObject CreateCube(
            string name,
            Vector3 localPosition,
            Vector3 size,
            Material material,
            Transform parent,
            bool collider = true,
            Quaternion? localRotation = null)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = localRotation.HasValue
                ? localRotation.Value
                : Quaternion.identity;
            MeshFilter meshFilter = cube.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
                throw new MissingComponentException($"{name} primitive mesh is missing.");
            meshFilter.sharedMesh = GetSizedCubeMesh(meshFilter.sharedMesh, size);
            cube.transform.localScale = Vector3.one;
            BoxCollider boxCollider = cube.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.center = Vector3.zero;
                boxCollider.size = size;
            }
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
                Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static Transform CreateDimensionsMarker(
            string name,
            float width,
            float depth,
            Transform parent)
        {
            var marker = new GameObject(name);
            marker.transform.SetParent(parent, false);
            BoxCollider dimensions = marker.AddComponent<BoxCollider>();
            dimensions.size = new Vector3(width, 1f, depth);
            dimensions.enabled = false;
            return marker.transform;
        }

        private static void CreateSurfaceWithHoles(
            Transform parent,
            string name,
            float centerX,
            float centerZ,
            float width,
            float depth,
            float y,
            float thickness,
            Material material,
            List<RectSpec> holes,
            bool collider)
        {
            float minX = centerX - width * 0.5f;
            float maxX = centerX + width * 0.5f;
            float minZ = centerZ - depth * 0.5f;
            float maxZ = centerZ + depth * 0.5f;
            var xCuts = new List<float> { minX, maxX };
            var zCuts = new List<float> { minZ, maxZ };
            foreach (RectSpec hole in holes)
            {
                AddCut(xCuts, Mathf.Clamp(hole.MinX, minX, maxX));
                AddCut(xCuts, Mathf.Clamp(hole.MaxX, minX, maxX));
                AddCut(zCuts, Mathf.Clamp(hole.MinZ, minZ, maxZ));
                AddCut(zCuts, Mathf.Clamp(hole.MaxZ, minZ, maxZ));
            }

            xCuts.Sort();
            zCuts.Sort();
            for (int x = 0; x < xCuts.Count - 1; x++)
            {
                for (int z = 0; z < zCuts.Count - 1; z++)
                {
                    float cellWidth = xCuts[x + 1] - xCuts[x];
                    float cellDepth = zCuts[z + 1] - zCuts[z];
                    if (cellWidth <= 0.01f || cellDepth <= 0.01f)
                        continue;

                    float cellCenterX = (xCuts[x + 1] + xCuts[x]) * 0.5f;
                    float cellCenterZ = (zCuts[z + 1] + zCuts[z]) * 0.5f;
                    bool isHole = false;
                    foreach (RectSpec hole in holes)
                    {
                        if (hole.Contains(cellCenterX, cellCenterZ))
                        {
                            isHole = true;
                            break;
                        }
                    }

                    if (!isHole)
                    {
                        CreateCube(
                            $"{name}_{x:00}_{z:00}",
                            new Vector3(cellCenterX, y, cellCenterZ),
                            new Vector3(cellWidth, thickness, cellDepth),
                            material,
                            parent,
                            collider);
                    }
                }
            }
        }

        private static void AddCut(List<float> cuts, float value)
        {
            foreach (float existing in cuts)
            {
                if (Mathf.Abs(existing - value) < 0.001f)
                    return;
            }

            cuts.Add(value);
        }

        private static void CreateTextLabel(
            string name,
            string text,
            Vector3 localPosition,
            Transform parent,
            float characterSize)
        {
            var labelObject = new GameObject(name);
            labelObject.transform.SetParent(parent, false);
            labelObject.transform.localPosition = localPosition;
            labelObject.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextMesh label = labelObject.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = characterSize;
            label.color = Color.white;
        }

        private static void Validate(
            Transform root,
            PlanSpec plan,
            PlanVariantPrototypeSettings settings)
        {
            if (root == null || root.name != plan.RootName)
                throw new InvalidOperationException($"{plan.Label} 루트 이름이 잘못됐습니다.");
            RequireUnitScale(root, $"{plan.RootName} scale");
            RequireGeneratedUnitScales(root);
            Transform frontYard = root.Find("FrontYard_TEMP");
            if (frontYard == null)
                throw new MissingReferenceException($"{plan.Label} front yard is missing.");
            CalculateColliderBounds(new[] { frontYard }, $"{plan.Label} front yard");
            Transform connector = root.Find(ConnectorGroupName);
            if (connector == null)
                throw new MissingReferenceException($"{plan.Label} connector floor is missing.");
            CalculateColliderBounds(new[] { connector }, $"{plan.Label} connector floor");

            int roomCount = 0;
            foreach (FloorSpec floor in plan.Floors)
            {
                Transform floorRoot = root.Find(floor.Name);
                if (floorRoot == null)
                    throw new MissingReferenceException($"{plan.Label}에 {floor.Name}이 없습니다.");
                RequireApproximately(
                    floorRoot.localPosition.y,
                    floor.Index * settings.FloorPitch,
                    $"{plan.Label}/{floor.Name} 높이");

                Transform dimensions = floorRoot.Find($"{StructureGroupName}/FloorDimensions");
                if (dimensions == null)
                    throw new MissingReferenceException($"{plan.Label}/{floor.Name} 치수 표식이 없습니다.");
                BoxCollider floorDimensions = dimensions.GetComponent<BoxCollider>();
                if (floorDimensions == null || floorDimensions.enabled)
                    throw new InvalidOperationException(
                        $"{plan.Label}/{floor.Name} 치수 표식은 비활성 BoxCollider여야 합니다.");
                RequireApproximately(floorDimensions.size.x, floor.Width, $"{plan.Label}/{floor.Name} 폭");
                RequireApproximately(floorDimensions.size.z, floor.Depth, $"{plan.Label}/{floor.Name} 깊이");

                if (settings.PlaceTemporaryLights
                    && floorRoot.Find(TemporaryLightsGroupName) == null)
                {
                    throw new MissingReferenceException(
                        $"{plan.Label}/{floor.Name} temporary light is missing.");
                }
                if (floorRoot.Find($"{StructureGroupName}/Windows_TEMP") == null)
                    throw new MissingReferenceException(
                        $"{plan.Label}/{floor.Name} temporary windows are missing.");

                Transform rooms = floorRoot.Find(RoomGroupName);
                int roomChildCount = rooms == null ? 0 : rooms.childCount;
                if (rooms == null || roomChildCount != floor.Rooms.Length)
                    throw new InvalidOperationException(
                        $"{plan.Label}/{floor.Name} 방 수가 {roomChildCount}개입니다. " +
                        $"기획값 {floor.Rooms.Length}개.");
                roomCount += rooms.childCount;

                for (int i = 0; i < floor.Rooms.Length; i++)
                {
                    RoomSpec room = floor.Rooms[i];
                    Transform roomRoot = rooms.Find(room.Id);
                    if (roomRoot == null)
                        throw new MissingReferenceException($"{plan.Label}/{floor.Name}/{room.Id}가 없습니다.");
                    Transform roomDimensions = roomRoot.Find("RoomDimensions");
                    if (roomDimensions == null)
                        throw new MissingReferenceException($"{room.Id} 치수 표식이 없습니다.");
                    BoxCollider roomDimensionsCollider = roomDimensions.GetComponent<BoxCollider>();
                    if (roomDimensionsCollider == null || roomDimensionsCollider.enabled)
                        throw new InvalidOperationException(
                            $"{room.Id} 치수 표식은 비활성 BoxCollider여야 합니다.");
                    RequireApproximately(roomDimensionsCollider.size.x, room.Width, $"{room.Id} 폭");
                    RequireApproximately(roomDimensionsCollider.size.z, room.Depth, $"{room.Id} 깊이");
                }

                if (floor.Entry != null)
                    ValidateEntryOpenings(floorRoot, settings);

                if (settings.PlaceFurniture)
                    ValidateFurnitureFloor(root, floorRoot, floor, settings);
            }

            if (roomCount != 21)
                throw new InvalidOperationException($"{plan.Label} 방 총 수가 {roomCount}개입니다. 기획값 21개.");

            ValidateStairs(root, plan, settings);
            ValidateRigidbodyOwnership(root);
            ValidateRoomBounds(plan, settings);
        }

        private static void ValidateEntryOpenings(
            Transform floorRoot,
            PlanVariantPrototypeSettings settings)
        {
            Transform structure = floorRoot.Find(StructureGroupName);
            Transform entry = floorRoot.Find("Circulation_TEMP/Entrance");
            if (structure == null
                || structure.Find("Outer_South_A") == null
                || structure.Find("Outer_South_B") == null
                || entry == null
                || entry.Find("Wall_North_A") == null
                || entry.Find("Wall_North_B") == null
                || entry.Find("Wall_South_A") == null
                || entry.Find("Wall_South_B") == null)
            {
                throw new InvalidOperationException(
                    "Entrance must have an exterior opening and north/south interior openings.");
            }

            Transform dimensions = entry.Find("RoomDimensions");
            BoxCollider dimensionsCollider = dimensions == null
                ? null
                : dimensions.GetComponent<BoxCollider>();
            if (dimensionsCollider == null)
                throw new MissingReferenceException("Entrance dimensions marker is missing.");

            float frontDoorHalf = Mathf.Min(
                settings.FrontDoorWidth * 0.5f,
                dimensionsCollider.size.x * 0.5f - settings.WallThickness - 0.15f);
            if (frontDoorHalf <= 0.05f)
                throw new InvalidOperationException("Front door opening is too narrow for the entrance.");
        }

        private static void ValidateFurnitureFloor(
            Transform root,
            Transform floorRoot,
            FloorSpec floor,
            PlanVariantPrototypeSettings settings)
        {
            Transform furnitureRoot = floorRoot.Find(FurnitureGroupName);
            int expectedGroups = floor.Rooms.Length + (floor.Entry == null ? 0 : 1);
            int furnitureGroupCount = furnitureRoot == null ? 0 : furnitureRoot.childCount;
            if (furnitureRoot == null || furnitureGroupCount != expectedGroups)
                throw new InvalidOperationException(
                    $"{floor.Name} 가구 그룹 수가 {furnitureGroupCount}개입니다. " +
                    $"방마다 하나의 Furniture_TEMP 그룹이 필요합니다.");

            Transform stairs = root.Find("Stairs_A_B_TEMP");
            var stairColliders = stairs == null
                ? Array.Empty<Collider>()
                : stairs.GetComponentsInChildren<Collider>(true);

            foreach (Transform roomFurniture in furnitureRoot)
            {
                if (roomFurniture.childCount == 0)
                    throw new InvalidOperationException($"{roomFurniture.name}에 가구가 없습니다.");
                bool isEntry = roomFurniture.name.StartsWith("EntryFurniture_", StringComparison.Ordinal);
                string roomId = isEntry
                    ? roomFurniture.name.Substring("EntryFurniture_".Length)
                    : roomFurniture.name.Substring("Furniture_".Length);
                Transform roomRoot = isEntry
                    ? floorRoot.Find($"Circulation_TEMP/{roomId}")
                    : floorRoot.Find($"{RoomGroupName}/{roomId}");
                if (roomRoot == null)
                    throw new MissingReferenceException($"{roomFurniture.name}에 대응하는 방이 없습니다.");

                RoomSpec room = isEntry ? floor.Entry : FindRoom(floor, roomRoot.name);
                Bounds roomInterior = new Bounds(
                    roomRoot.position + new Vector3(0f, settings.WallHeight * 0.5f, 0f),
                    new Vector3(
                        room.Width - settings.WallThickness * 2f - 0.2f,
                        settings.WallHeight - 0.15f,
                        room.Depth - settings.WallThickness * 2f - 0.2f));
                var roomPlaced = new List<Bounds>();

                foreach (Transform furniture in roomFurniture)
                {
                    if (furniture.GetComponent<NetworkObject>() == null
                        || furniture.GetComponent<Rigidbody>() == null
                        || furniture.GetComponent<FurnitureGrabTarget>() == null
                        || furniture.GetComponent<FurnitureNetworkPhysics>() == null)
                    {
                        throw new MissingComponentException(
                            $"{furniture.name}가 실제 Furniture 프리팹 인스턴스가 아닙니다.");
                    }

                    Bounds bounds = CalculateColliderBounds(new[] { furniture }, furniture.name);
                    if (bounds.min.x < roomInterior.min.x
                        || bounds.max.x > roomInterior.max.x
                        || bounds.min.z < roomInterior.min.z
                        || bounds.max.z > roomInterior.max.z)
                        throw new InvalidOperationException(
                            $"{furniture.name}가 {room.Id} 방 벽 안쪽을 벗어났습니다.");
                    foreach (Bounds other in roomPlaced)
                    {
                        if (bounds.Intersects(other))
                            throw new InvalidOperationException(
                                $"{furniture.name}가 같은 방의 다른 가구와 겹칩니다.");
                    }
                    foreach (Collider stairCollider in stairColliders)
                    {
                        if (stairCollider != null && bounds.Intersects(stairCollider.bounds))
                            throw new InvalidOperationException(
                                $"{furniture.name}가 계단 통로와 겹칩니다.");
                    }

                    ValidateDoorClearance(bounds, roomRoot, room, room.Door, settings);
                    if (room.Kind == RoomKind.Entrance && room.Door != DoorSide.South)
                        ValidateDoorClearance(bounds, roomRoot, room, DoorSide.South, settings);
                    roomPlaced.Add(bounds);
                }
            }
        }

        private static void ValidateDoorClearance(
            Bounds furniture,
            Transform roomRoot,
            RoomSpec room,
            DoorSide door,
            PlanVariantPrototypeSettings settings)
        {
            float depth = 0.85f;
            float openingWidth = room.Kind == RoomKind.Entrance && door == DoorSide.South
                ? settings.FrontDoorWidth
                : settings.DoorWidth;
            RectSpec keepOut;
            switch (door)
            {
                case DoorSide.North:
                    keepOut = new RectSpec(
                        roomRoot.position.x,
                        roomRoot.position.z + room.Depth * 0.5f - depth * 0.5f,
                        openingWidth + 0.25f,
                        depth);
                    break;
                case DoorSide.South:
                    keepOut = new RectSpec(
                        roomRoot.position.x,
                        roomRoot.position.z - room.Depth * 0.5f + depth * 0.5f,
                        openingWidth + 0.25f,
                        depth);
                    break;
                case DoorSide.East:
                    keepOut = new RectSpec(
                        roomRoot.position.x + room.Width * 0.5f - depth * 0.5f,
                        roomRoot.position.z,
                        depth,
                        openingWidth + 0.25f);
                    break;
                default:
                    keepOut = new RectSpec(
                        roomRoot.position.x - room.Width * 0.5f + depth * 0.5f,
                        roomRoot.position.z,
                        depth,
                        openingWidth + 0.25f);
                    break;
            }

            if (furniture.min.x < keepOut.MaxX
                && furniture.max.x > keepOut.MinX
                && furniture.min.z < keepOut.MaxZ
                && furniture.max.z > keepOut.MinZ)
            {
                throw new InvalidOperationException(
                    $"{roomRoot.name} 문 개구부 앞에 가구가 놓였습니다.");
            }
        }

        private static void ValidateStairs(
            Transform root,
            PlanSpec plan,
            PlanVariantPrototypeSettings settings)
        {
            Transform stairsRoot = root.Find("Stairs_A_B_TEMP");
            if (stairsRoot == null)
                throw new MissingReferenceException($"{plan.Label} 계단 그룹이 없습니다.");

            List<StairSpec> expected = CreateStairSpecs(plan, settings);
            foreach (StairSpec stair in expected)
            {
                Transform stairRoot = stairsRoot.Find(stair.Name);
                if (stairRoot == null)
                    throw new MissingReferenceException($"{stair.Name}이 없습니다.");
                Transform ramp = stairRoot.Find("Stair_Ramp_Collider");
                BoxCollider collider = ramp == null ? null : ramp.GetComponent<BoxCollider>();
                if (collider == null || collider.isTrigger)
                    throw new InvalidOperationException($"{stair.Name}에 실제 경사 콜라이더가 없습니다.");

                int stepCount = 0;
                foreach (Transform child in stairRoot)
                {
                    if (child.name.StartsWith("Step_", StringComparison.Ordinal))
                        stepCount++;
                }
                if (stepCount != settings.StairStepCount)
                    throw new InvalidOperationException(
                        $"{stair.Name} 시각 계단 단 수가 {stepCount}개입니다. " +
                        $"설정값 {settings.StairStepCount}개.");
                int railPartCount = 0;
                foreach (Transform child in stairRoot)
                {
                    if (child.name.StartsWith("Rail_", StringComparison.Ordinal))
                        railPartCount++;
                }
                if (railPartCount != 6)
                    throw new InvalidOperationException(
                        $"{stair.Name} ?⑤???뺤긽 ?섍? {railPartCount}媛쒖엯?덈떎. (湲곕낯 6媛?)");

                FloorSpec upperFloor = plan.Floors[stair.UpperFloor];
                RectSpec opening = stair.Opening(settings.StairWidth, settings.StairOpeningMargin);
                if (opening.MinX < -upperFloor.Width * 0.5f
                    || opening.MaxX > upperFloor.Width * 0.5f
                    || opening.MinZ < -upperFloor.Depth * 0.5f
                    || opening.MaxZ > upperFloor.Depth * 0.5f)
                {
                    throw new InvalidOperationException(
                        $"{stair.Name} ?곷? 諛붾떏 媛쒓뎄遺媛 {upperFloor.Name} 踰붿쐞瑜?踰쀬뼱?ъ뒿?덈떎.");
                }
            }

            float actualSlope = Mathf.Atan2(
                settings.FloorPitch,
                settings.StairRun) * Mathf.Rad2Deg;
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            CharacterController playerController = playerPrefab == null
                ? null
                : playerPrefab.GetComponent<CharacterController>();
            if (playerController == null)
                throw new MissingReferenceException(
                    $"{PlayerPrefabPath}에 CharacterController가 없습니다.");
            float maxSlope = playerController.slopeLimit;
            if (settings.WallHeight <= playerController.height)
                throw new InvalidOperationException(
                    $"벽 높이 {settings.WallHeight:0.00}m가 Player 높이 {playerController.height:0.00}m보다 " +
                    "높아야 계단 머리 공간을 확보할 수 있습니다.");
            float clearWidth = settings.StairWidth - settings.StairRailThickness * 2f;
            if (clearWidth <= playerController.radius * 2f + 0.1f)
                throw new InvalidOperationException(
                    $"계단 유효 폭 {clearWidth:0.00}m가 Player 반경 {playerController.radius:0.00}m보다 " +
                    "충분히 넓지 않습니다.");
            if (actualSlope >= maxSlope - 0.01f)
                throw new InvalidOperationException(
                    $"계단 경사 {actualSlope:0.0}°가 Player CharacterController slopeLimit {maxSlope:0.0}° 이상입니다.");
        }

        private static void ValidateRigidbodyOwnership(Transform root)
        {
            if (root.Find("Floor_01/Furniture_TEMP") == null)
                return;

            foreach (Rigidbody body in root.GetComponentsInChildren<Rigidbody>(true))
            {
                Transform current = body.transform;
                bool ownedByFurniture = false;
                while (current != null && current != root)
                {
                    if (current.name == FurnitureGroupName
                        || current.name.StartsWith("EntryFurniture_", StringComparison.Ordinal))
                    {
                        ownedByFurniture = true;
                        break;
                    }

                    current = current.parent;
                }

                if (!ownedByFurniture)
                {
                    throw new InvalidOperationException(
                        $"{body.name} Rigidbody가 Furniture_TEMP 밖에 있습니다.");
                }
            }
        }

        private static void ValidateRoomBounds(PlanSpec plan, PlanVariantPrototypeSettings settings)
        {
            foreach (FloorSpec floor in plan.Floors)
            {
                float halfWidth = floor.Width * 0.5f;
                float halfDepth = floor.Depth * 0.5f;
                for (int i = 0; i < floor.Rooms.Length; i++)
                {
                    RoomSpec room = floor.Rooms[i];
                    if (room.Center.x - room.Width * 0.5f < -halfWidth + settings.WallThickness
                        || room.Center.x + room.Width * 0.5f > halfWidth - settings.WallThickness
                        || room.Center.y - room.Depth * 0.5f < -halfDepth + settings.WallThickness
                        || room.Center.y + room.Depth * 0.5f > halfDepth - settings.WallThickness)
                    {
                        throw new InvalidOperationException(
                            $"{plan.Label}/{floor.Name}/{room.Id}가 외곽 벽 밖에 있습니다.");
                    }

                    for (int j = i + 1; j < floor.Rooms.Length; j++)
                    {
                        RoomSpec other = floor.Rooms[j];
                        if (RectanglesOverlap(room, other, settings.WallThickness * 0.25f))
                            throw new InvalidOperationException(
                                $"{plan.Label}/{floor.Name}의 {room.Id}와 {other.Id}가 겹칩니다.");
                    }
                }

                if (floor.Entry != null
                    && (floor.Entry.Center.x - floor.Entry.Width * 0.5f < -halfWidth + settings.WallThickness
                        || floor.Entry.Center.x + floor.Entry.Width * 0.5f > halfWidth - settings.WallThickness
                        || floor.Entry.Center.y - floor.Entry.Depth * 0.5f < -halfDepth + settings.WallThickness
                        || floor.Entry.Center.y + floor.Entry.Depth * 0.5f > halfDepth - settings.WallThickness))
                {
                    throw new InvalidOperationException(
                        $"{plan.Label}/{floor.Name}/{floor.Entry.Id}가 외곽 벽 밖에 있습니다.");
                }
            }
        }

        private static bool RectanglesOverlap(RoomSpec a, RoomSpec b, float margin)
        {
            return a.Center.x - a.Width * 0.5f < b.Center.x + b.Width * 0.5f - margin
                && a.Center.x + a.Width * 0.5f > b.Center.x - b.Width * 0.5f + margin
                && a.Center.y - a.Depth * 0.5f < b.Center.y + b.Depth * 0.5f - margin
                && a.Center.y + a.Depth * 0.5f > b.Center.y - b.Depth * 0.5f + margin;
        }

        private static RoomSpec FindRoom(FloorSpec floor, string id)
        {
            foreach (RoomSpec room in floor.Rooms)
            {
                if (room.Id == id)
                    return room;
            }

            throw new KeyNotFoundException($"{floor.Name}에 방 {id}가 없습니다.");
        }

        private static void RequireUnitScale(Transform transform, string label)
        {
            if ((transform.localScale - Vector3.one).sqrMagnitude > 0.000001f)
                throw new InvalidOperationException($"{label}은(는) 1이어야 합니다.");
        }

        private static void RequireGeneratedUnitScales(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                Transform current = child;
                bool insideFurniture = false;
                while (current != null && current != root)
                {
                    if (current.name == FurnitureGroupName)
                    {
                        insideFurniture = true;
                        break;
                    }

                    current = current.parent;
                }

                if (!insideFurniture)
                    RequireUnitScale(child, $"{root.name}/{child.name} scale");
            }
        }

        private static void RequireApproximately(float actual, float expected, string label)
        {
            if (Mathf.Abs(actual - expected) > BoundsTolerance)
                throw new InvalidOperationException(
                    $"{label}: {actual:0.###}, 기대값 {expected:0.###}.");
        }

        private static Transform FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == name)
                    return root.transform;
            }

            return null;
        }

        private static List<Transform> FindPreservedMapRoots(Scene scene)
        {
            var roots = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "House_01"
                    || root.name == "House_01_OriginalScale_Right"
                    || root.name == MansionGrayboxBuilder.RootName)
                {
                    roots.Add(root.transform);
                }
            }

            if (FindRoot(scene, "House_01") == null
                || FindRoot(scene, "House_01_OriginalScale_Right") == null)
            {
                throw new MissingReferenceException(
                    "기존 House_01과 House_01_OriginalScale_Right를 보존한 상태에서 실행하세요.");
            }

            return roots;
        }

        private static Vector3 PositionRightOf(
            Bounds occupied,
            PlanSpec plan,
            float gap)
        {
            float halfWidth = plan.MainWidth * 0.5f;
            return new Vector3(
                occupied.max.x + gap + halfWidth,
                0f,
                0f);
        }

        private static Bounds CalculateColliderBounds(
            IEnumerable<Transform> roots,
            string label)
        {
            Physics.SyncTransforms();
            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Transform root in roots)
            {
                if (root == null)
                    continue;
                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
                    if (!collider.enabled)
                        continue;
                    if (!hasBounds)
                    {
                        bounds = collider.bounds;
                        hasBounds = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            if (!hasBounds)
                throw new InvalidOperationException($"{label}에 Collider가 없어 배치 외곽을 계산할 수 없습니다.");
            return bounds;
        }

        private static Bounds Encapsulate(Bounds first, Bounds second)
        {
            first.Encapsulate(second);
            return first;
        }

        private static int CountFurniture(Transform root)
        {
            return root == null
                ? 0
                : root.GetComponentsInChildren<FurnitureGrabTarget>(true).Length;
        }

        private static PlanSpec CreatePlan(Variant variant)
        {
            return variant == Variant.PlanB ? CreatePlanB() : CreatePlanC();
        }

        private static PlanSpec CreatePlanB()
        {
            return new PlanSpec(
                Variant.PlanB,
                PlanBRootName,
                "B안 — HousePlanC",
                30f,
                24f,
                20f,
                14f,
                new[]
                {
                    new FloorSpec(
                        0,
                        "Floor_01",
                        "1F",
                        30f,
                        24f,
                        10f,
                        9f,
                        new[]
                        {
                            Room("Study", "Study", -10.5f, 8.5f, 6f, 5f, RoomKind.Study, DoorSide.East),
                            Room("Bathroom_SW", "Bathroom", -12f, 4f, 3.5f, 3f, RoomKind.Bathroom, DoorSide.East),
                            Room("Storage", "Storage", -12f, 0f, 3.5f, 3f, RoomKind.Storage, DoorSide.East),
                            Room("LivingRoom", "Living Room", -10f, -5.5f, 7f, 6f, RoomKind.Living, DoorSide.East),
                            Room("Kitchen", "Kitchen", 10f, 4f, 6.5f, 6f, RoomKind.Kitchen, DoorSide.West),
                            Room("DiningRoom", "Dining Room", 10f, -5.5f, 6f, 5f, RoomKind.Dining, DoorSide.West),
                            Room("Laundry", "Laundry", 12f, 9.5f, 4.5f, 4.5f, RoomKind.Laundry, DoorSide.West),
                        },
                        Room("Entrance", "Main Entrance", 0f, -9.5f, 4f, 3.5f, RoomKind.Entrance, DoorSide.North)),
                    new FloorSpec(
                        1,
                        "Floor_02",
                        "2F — Gallery",
                        30f,
                        24f,
                        12f,
                        9f,
                        new[]
                        {
                            Room("Bedroom_01", "Bedroom 1", -10.5f, 7.5f, 6f, 5.5f, RoomKind.Bedroom, DoorSide.East),
                            Room("Bedroom_02", "Bedroom 2", 10.5f, 7.5f, 6f, 5.5f, RoomKind.Bedroom, DoorSide.West),
                            Room("Bedroom_03", "Bedroom 3", -10.5f, -1.8f, 6f, 5.5f, RoomKind.Bedroom, DoorSide.East),
                            Room("Bedroom_04", "Bedroom 4", 10.5f, -1.8f, 6f, 5.5f, RoomKind.Bedroom, DoorSide.West),
                            Room("Bathroom_01", "Bathroom 1", -11f, -8.5f, 3.5f, 3f, RoomKind.Bathroom, DoorSide.North),
                            Room("Bathroom_02", "Bathroom 2", 11f, -8.5f, 3.5f, 3f, RoomKind.Bathroom, DoorSide.North),
                            Room("FamilyRoom", "Family Room", -4f, -8.5f, 7f, 5f, RoomKind.Family, DoorSide.North),
                            Room("StudyWorkroom", "Study / Workroom", 4f, -8.5f, 7f, 5f, RoomKind.Study, DoorSide.North),
                        }),
                    new FloorSpec(
                        2,
                        "Floor_03_Attic",
                        "Attic",
                        20f,
                        14f,
                        7f,
                        6f,
                        new[]
                        {
                            Room("GuestRoom_01", "Guest Room 1", -6.25f, 4.5f, 5f, 4.5f, RoomKind.Guest, DoorSide.East),
                            Room("GuestRoom_02", "Guest Room 2", 6.25f, 4.5f, 5f, 4.5f, RoomKind.Guest, DoorSide.West),
                            Room("Playroom", "Playroom", -6.25f, -1.8f, 5f, 4.5f, RoomKind.Playroom, DoorSide.East),
                            Room("StorageRoom", "Storage Room", 6.25f, -1.8f, 5f, 4.5f, RoomKind.Storage, DoorSide.West),
                            Room("SmallBathroom", "Small Bathroom", -1.8f, -5.2f, 3f, 2.5f, RoomKind.Bathroom, DoorSide.North),
                            Room("SecretStorage", "Secret Storage", 1.8f, -5.2f, 3.5f, 2.5f, RoomKind.SecretStorage, DoorSide.North),
                        }),
                });
        }

        private static PlanSpec CreatePlanC()
        {
            return new PlanSpec(
                Variant.PlanC,
                PlanCRootName,
                "C안 — HousePlanB",
                40f,
                32f,
                28f,
                20f,
                new[]
                {
                    new FloorSpec(
                        0,
                        "Floor_01",
                        "1F",
                        40f,
                        32f,
                        14f,
                        12f,
                        new[]
                        {
                            Room("LivingRoom", "Living Room", -14.5f, 10f, 9f, 7.5f, RoomKind.Living, DoorSide.East),
                            Room("DiningRoom", "Dining Room", 14f, 10f, 8f, 6f, RoomKind.Dining, DoorSide.West),
                            Room("Study", "Study", -15f, -0.5f, 7.5f, 6f, RoomKind.Study, DoorSide.East),
                            Room("Kitchen", "Kitchen", 14f, -0.5f, 8f, 7f, RoomKind.Kitchen, DoorSide.West),
                            Room("Bathroom_SW", "Bathroom", -16f, -7.5f, 4f, 3.5f, RoomKind.Bathroom, DoorSide.East),
                            Room("Storage", "Storage", 9f, -10.5f, 4f, 3.5f, RoomKind.Storage, DoorSide.West),
                            Room("Laundry", "Laundry", 16f, -8.5f, 5.5f, 5f, RoomKind.Laundry, DoorSide.West),
                        },
                        Room("Entrance", "Main Entrance", 0f, -13.5f, 5f, 4f, RoomKind.Entrance, DoorSide.North)),
                    new FloorSpec(
                        1,
                        "Floor_02",
                        "2F — Gallery",
                        40f,
                        32f,
                        16f,
                        12f,
                        new[]
                        {
                            Room("Bedroom_01", "Bedroom 1", -14f, 9f, 7.5f, 7f, RoomKind.Bedroom, DoorSide.East),
                            Room("Bedroom_02", "Bedroom 2", -14f, -1.5f, 7.5f, 7f, RoomKind.Bedroom, DoorSide.East),
                            Room("Bedroom_03", "Bedroom 3", 14f, 9f, 7.5f, 7f, RoomKind.Bedroom, DoorSide.West),
                            Room("Bedroom_04", "Bedroom 4", 14f, -1.5f, 7.5f, 7f, RoomKind.Bedroom, DoorSide.West),
                            Room("Bathroom_01", "Bathroom 1", -13.5f, -12f, 4f, 3.5f, RoomKind.Bathroom, DoorSide.North),
                            Room("Bathroom_02", "Bathroom 2", 13.5f, -12f, 4f, 3.5f, RoomKind.Bathroom, DoorSide.North),
                            Room("FamilyRoom", "Family Room", -5f, -11.5f, 9f, 6.5f, RoomKind.Family, DoorSide.North),
                            Room("StudyWorkroom", "Study / Workroom", 5f, -11.5f, 9f, 6.5f, RoomKind.Study, DoorSide.North),
                        }),
                    new FloorSpec(
                        2,
                        "Floor_03_Attic",
                        "Attic",
                        28f,
                        20f,
                        10f,
                        8f,
                        new[]
                        {
                            Room("GuestRoom_01", "Guest Room 1", -9f, 6.2f, 6.5f, 5.5f, RoomKind.Guest, DoorSide.East),
                            Room("GuestRoom_02", "Guest Room 2", 9f, 6.2f, 6.5f, 5.5f, RoomKind.Guest, DoorSide.West),
                            Room("Playroom", "Playroom", -9f, -2.5f, 6.5f, 5.5f, RoomKind.Playroom, DoorSide.East),
                            Room("StorageRoom", "Storage Room", 9f, -2.5f, 6.5f, 5.5f, RoomKind.Storage, DoorSide.West),
                            Room("SmallBathroom", "Small Bathroom", -2f, -7.8f, 3.5f, 3f, RoomKind.Bathroom, DoorSide.North),
                            Room("SecretStorage", "Secret Storage", 2f, -7.8f, 4f, 3f, RoomKind.SecretStorage, DoorSide.North),
                        }),
                });
        }

        private static RoomSpec Room(
            string id,
            string label,
            float centerX,
            float centerZ,
            float width,
            float depth,
            RoomKind kind,
            DoorSide door)
        {
            return new RoomSpec(id, label, centerX, centerZ, width, depth, kind, door);
        }
    }
}
