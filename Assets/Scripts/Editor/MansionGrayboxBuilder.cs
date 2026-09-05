using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 맵 생성 v0.3의 MAP-1 검증용 대저택 그레이박스.
    /// 기존 <see cref="HousePrototypeBuilder"/> 생성물과 게임플레이 배선은 건드리지 않고,
    /// 확정된 외곽·방 치수와 고정 동선만 별도 루트에 시각화한다.
    /// </summary>
    internal static class MansionGrayboxBuilder
    {
        internal const string RootName = "House_01_V03_Graybox_Right";
        internal const float MapScale = 1f;
        internal const float MainFloorWidth = 20f;
        internal const float MainFloorDepth = 16f;
        internal const float AtticWidth = 14f;
        internal const float AtticDepth = 10f;
        internal const float RightSideGap = 3f;

        /// <summary>
        /// MAP-1 장면 검토만을 위한 임시 층간 간격. MG-7의 층고·계단 경사·계단참·단높이는
        /// 여전히 미결이며, 이 값은 해당 결정을 닫지 않는다.
        /// </summary>
        internal const float PreviewFloorPitch = 3f;

        private const float WallHeight = 2.5f;
        private const float WallThickness = 0.18f;
        private const float FloorThickness = 0.2f;
        private const float MarkerThickness = 0.03f;
        private const float OutlineHeight = 0.08f;
        private const float OutlineThickness = 0.06f;
        private const float EntranceWidth = 1.5f;
        private const float StairWidth = 2.2f;

        private const string RoomsGroupName = "Rooms_21_FixedFootprints";
        private const string CirculationGroupName = "FixedCirculation";
        private const string PendingGroupName = "TBD_Placeholders";

        internal static Transform Create(
            HousePrototypeBuilder.Palette palette,
            Vector3 worldPosition)
        {
            if (palette == null)
                throw new ArgumentNullException(nameof(palette));

            var rootObject = new GameObject(RootName);
            rootObject.transform.position = worldPosition;

            try
            {
                CreateFloor01(rootObject.transform, palette);
                CreateFloor02(rootObject.transform, palette);
                CreateFloor03(rootObject.transform, palette);
                return rootObject.transform;
            }
            catch
            {
                Object.DestroyImmediate(rootObject);
                throw;
            }
        }

        /// <summary>
        /// 보존할 기존 맵의 실제 Collider 외곽에서 3m를 띄운 뒤 새 20m 외곽을 놓는다.
        /// 기존 집이 이동해도 하드코딩 좌표 때문에 겹치지 않도록 bounds를 직접 사용한다.
        /// </summary>
        internal static Vector3 RightSidePosition(IEnumerable<Transform> preservedMapRoots)
        {
            Bounds preservedBounds = CalculateColliderBounds(preservedMapRoots, "기존 맵");
            float exteriorHalfWidth = MainFloorWidth * 0.5f + WallThickness * 0.5f;
            return new Vector3(
                preservedBounds.max.x + RightSideGap + exteriorHalfWidth,
                0f,
                0f);
        }

        internal static void Validate(Transform root)
        {
            if (root == null)
                throw new ArgumentNullException(nameof(root));
            if (root.name != RootName)
                throw new InvalidOperationException($"그레이박스 루트 이름이 '{RootName}'이 아닙니다.");
            RequireOne(root.localScale, "그레이박스 루트 scale");

            ValidateFloor(root, "Floor_01", 0f, MainFloorWidth, MainFloorDepth, 7);
            ValidateFloor(root, "Floor_02", PreviewFloorPitch, MainFloorWidth, MainFloorDepth, 8);
            ValidateFloor(root, "Floor_03_Attic", PreviewFloorPitch * 2f, AtticWidth, AtticDepth, 6);

            int roomCount = 0;
            foreach (Transform floor in DirectChildren(root))
                roomCount += RequireChild(floor, RoomsGroupName).childCount;
            if (roomCount != 21)
                throw new InvalidOperationException($"그레이박스 방 개수 오류: {roomCount}, 기획값 21");

            if (root.GetComponentInChildren<NetworkObject>(true) != null)
            {
                throw new InvalidOperationException(
                    "MAP-1 그레이박스에 NetworkObject가 연결됐습니다. 검증용 맵은 런타임 배선 대상이 아닙니다.");
            }

            if (root.GetComponentInChildren<Rigidbody>(true) != null)
                throw new InvalidOperationException("MAP-1 그레이박스에 Rigidbody가 연결됐습니다.");
        }

        internal static void ValidateRightSidePlacement(
            Transform graybox,
            IEnumerable<Transform> preservedMapRoots)
        {
            Bounds grayboxBounds = CalculateColliderBounds(new[] { graybox }, "v0.3 그레이박스");
            Bounds preservedBounds = CalculateColliderBounds(preservedMapRoots, "기존 맵");
            float gap = grayboxBounds.min.x - preservedBounds.max.x;
            if (gap + 0.01f < RightSideGap)
            {
                throw new InvalidOperationException(
                    $"기존 맵과 v0.3 그레이박스 사이가 {gap:0.00}m입니다. 최소 {RightSideGap:0.00}m가 필요합니다.");
            }
        }

        private static void CreateFloor01(Transform house, HousePrototypeBuilder.Palette palette)
        {
            FloorGroups floor = CreateFloorShell(
                house,
                "Floor_01",
                0f,
                MainFloorWidth,
                MainFloorDepth,
                palette,
                true);

            CreateRoom(floor.Rooms, "Study_4.2x3.8", "Study\n4.2 x 3.8m",
                -7.8f, 6f, 4.2f, 3.8f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Bathroom_2.4x2.0", "Bathroom\n2.4 x 2.0m",
                0f, 6.9f, 2.4f, 2f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "LaundryUtility_3.2x3.8", "Laundry / Utility\n3.2 x 3.8m",
                8.3f, 6f, 3.2f, 3.8f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "LivingRoom_5.0x4.4", "Living Room\n5.0 x 4.4m",
                -7.4f, 0.4f, 5f, 4.4f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Storage_2.6x2.0", "Storage\n2.6 x 2.0m",
                8.6f, 1.7f, 2.6f, 2f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "DiningRoom_4.4x3.6", "Dining Room\n4.4 x 3.6m",
                -7.7f, -6f, 4.4f, 3.6f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Kitchen_4.2x4.4", "Kitchen\n4.2 x 4.4m",
                7.8f, -5.7f, 4.2f, 4.4f, palette.TileFloor, palette.Trim);

            CreateZone(floor.Circulation, "CentralHall_6.8x6.8", "Central Hall\n6.8 x 6.8m",
                0f, 0f, 6.8f, 6.8f, palette.WoodFloor, palette.Trim);
            CreateZone(floor.Circulation, "MainEntrance_2.8x2.4", "Main Entrance\n2.8 x 2.4m",
                0f, -6.7f, 2.8f, 2.4f, palette.TileFloor, palette.Trim);

            CreateStairWidthMarker(floor.Pending, "Stair_A", -2.3f, 6.9f, palette);
            CreateStairWidthMarker(floor.Pending, "Stair_B", 2.3f, 6.9f, palette);
        }

        private static void CreateFloor02(Transform house, HousePrototypeBuilder.Palette palette)
        {
            FloorGroups floor = CreateFloorShell(
                house,
                "Floor_02",
                PreviewFloorPitch,
                MainFloorWidth,
                MainFloorDepth,
                palette,
                false);

            CreateRoom(floor.Rooms, "Bedroom_01_4.2x4.0", "Bedroom 1\n4.2 x 4.0m",
                -7.8f, 5.9f, 4.2f, 4f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Bathroom_01_2.6x2.2", "Bathroom 1\n2.6 x 2.2m",
                0f, 6.8f, 2.6f, 2.2f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Bedroom_03_4.2x4.0", "Bedroom 3\n4.2 x 4.0m",
                7.8f, 5.9f, 4.2f, 4f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Bedroom_02_4.2x4.0", "Bedroom 2\n4.2 x 4.0m",
                -7.8f, 0.7f, 4.2f, 4f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Bedroom_04_4.2x4.0", "Bedroom 4\n4.2 x 4.0m",
                7.8f, 0.7f, 4.2f, 4f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "FamilyRoom_4.8x3.6", "Family Room\n4.8 x 3.6m",
                -7.5f, -6f, 4.8f, 3.6f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Bathroom_02_2.6x2.2", "Bathroom 2\n2.6 x 2.2m",
                0f, -6.8f, 2.6f, 2.2f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "StudyWorkroom_4.8x3.6", "Study / Workroom\n4.8 x 3.6m",
                7.5f, -6f, 4.8f, 3.6f, palette.WoodFloor, palette.Trim);

            CreateZone(floor.Circulation, "GalleryHall_8.4x6.4", "Gallery Hall\n8.4 x 6.4m",
                0f, 0f, 8.4f, 6.4f, palette.WoodFloor, palette.Trim);
            CreateTextLabel(
                "OpenVoid_Extent_TBD",
                "OPEN VOID\nextent / railing TBD",
                new Vector3(0f, 0.15f, 0f),
                floor.Pending);
            CreateCrossMarker("OpenVoid_Position_Marker", Vector3.zero, 1.2f, palette.Window, floor.Pending);

            CreateStairWidthMarker(floor.Pending, "Stair_A", -2.45f, 6.8f, palette);
            CreateStairWidthMarker(floor.Pending, "Stair_B", 2.45f, 6.8f, palette);
        }

        private static void CreateFloor03(Transform house, HousePrototypeBuilder.Palette palette)
        {
            FloorGroups floor = CreateFloorShell(
                house,
                "Floor_03_Attic",
                PreviewFloorPitch * 2f,
                AtticWidth,
                AtticDepth,
                palette,
                false);

            CreateRoom(floor.Rooms, "GuestRoom_01_3.4x3.6", "Guest Room 1\n3.4 x 3.6m",
                -5.2f, 3.1f, 3.4f, 3.6f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "SmallBathroom_2.2x2.0", "Small Bathroom\n2.2 x 2.0m",
                0f, 3.9f, 2.2f, 2f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "Playroom_3.4x3.6", "Playroom\n3.4 x 3.6m",
                5.2f, 3.1f, 3.4f, 3.6f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "GuestRoom_02_3.4x3.6", "Guest Room 2\n3.4 x 3.6m",
                -5.2f, -2.7f, 3.4f, 3.6f, palette.WoodFloor, palette.Trim);
            CreateRoom(floor.Rooms, "StorageRoom_3.4x3.6", "Storage Room\n3.4 x 3.6m",
                5.2f, -2.7f, 3.4f, 3.6f, palette.TileFloor, palette.Trim);
            CreateRoom(floor.Rooms, "SecretStorage_2.4x1.8", "Secret Storage\n2.4 x 1.8m",
                0f, -4f, 2.4f, 1.8f, palette.TileFloor, palette.Trim);

            CreateZone(floor.Circulation, "AtticHall_4.8x4.8", "Attic Hall\n4.8 x 4.8m",
                0f, 0f, 4.8f, 4.8f, palette.WoodFloor, palette.Trim);
            CreateStairWidthMarker(floor.Pending, "Stair_A", -2.25f, 4.2f, palette);
            CreateStairWidthMarker(floor.Pending, "Stair_B", 2.25f, 4.2f, palette);
        }

        private static FloorGroups CreateFloorShell(
            Transform house,
            string name,
            float elevation,
            float width,
            float depth,
            HousePrototypeBuilder.Palette palette,
            bool leaveSouthEntrance)
        {
            Transform floor = CreateGroup(name, house);
            floor.localPosition = new Vector3(0f, elevation, 0f);

            Transform structure = CreateGroup("Structure", floor);
            Transform rooms = CreateGroup(RoomsGroupName, floor);
            Transform circulation = CreateGroup(CirculationGroupName, floor);
            Transform pending = CreateGroup(PendingGroupName, floor);

            CreateLocalCube(
                "FloorSlab",
                new Vector3(0f, -FloorThickness * 0.5f, 0f),
                new Vector3(width, FloorThickness, depth),
                palette.Trim,
                structure);
            CreateOuterWalls(structure, width, depth, palette.Wall, leaveSouthEntrance);

            CreateTextLabel(
                "FloorSizeLabel",
                $"{name}\n{width:0.#} x {depth:0.#}m | MapScale 1.0",
                new Vector3(0f, 0.13f, depth * 0.5f - 0.55f),
                circulation);

            return new FloorGroups(floor, rooms, circulation, pending);
        }

        private static void CreateOuterWalls(
            Transform parent,
            float width,
            float depth,
            Material material,
            bool leaveSouthEntrance)
        {
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            CreateLocalCube("North_Outer", new Vector3(0f, WallHeight * 0.5f, halfDepth),
                new Vector3(width, WallHeight, WallThickness), material, parent);
            CreateLocalCube("West_Outer", new Vector3(-halfWidth, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, depth), material, parent);
            CreateLocalCube("East_Outer", new Vector3(halfWidth, WallHeight * 0.5f, 0f),
                new Vector3(WallThickness, WallHeight, depth), material, parent);

            if (!leaveSouthEntrance)
            {
                CreateLocalCube("South_Outer", new Vector3(0f, WallHeight * 0.5f, -halfDepth),
                    new Vector3(width, WallHeight, WallThickness), material, parent);
                return;
            }

            float segmentWidth = (width - EntranceWidth) * 0.5f;
            float segmentCenter = EntranceWidth * 0.5f + segmentWidth * 0.5f;
            CreateLocalCube("South_Outer_West", new Vector3(-segmentCenter, WallHeight * 0.5f, -halfDepth),
                new Vector3(segmentWidth, WallHeight, WallThickness), material, parent);
            CreateLocalCube("South_Outer_East", new Vector3(segmentCenter, WallHeight * 0.5f, -halfDepth),
                new Vector3(segmentWidth, WallHeight, WallThickness), material, parent);
        }

        private static void CreateRoom(
            Transform parent,
            string name,
            string label,
            float centerX,
            float centerZ,
            float width,
            float depth,
            Material floorMaterial,
            Material outlineMaterial)
        {
            CreateZone(parent, name, label, centerX, centerZ, width, depth, floorMaterial, outlineMaterial);
        }

        private static void CreateZone(
            Transform parent,
            string name,
            string label,
            float centerX,
            float centerZ,
            float width,
            float depth,
            Material floorMaterial,
            Material outlineMaterial)
        {
            Transform zone = CreateGroup(name, parent);
            zone.localPosition = new Vector3(centerX, 0f, centerZ);
            CreateLocalCube(
                "Footprint",
                new Vector3(0f, MarkerThickness * 0.5f, 0f),
                new Vector3(width, MarkerThickness, depth),
                floorMaterial,
                zone,
                false);
            CreateOutline(zone, width, depth, outlineMaterial);
            CreateTextLabel("Label", label, new Vector3(0f, 0.14f, 0f), zone);
        }

        private static void CreateOutline(Transform parent, float width, float depth, Material material)
        {
            float y = MarkerThickness + OutlineHeight * 0.5f;
            float halfWidth = width * 0.5f;
            float halfDepth = depth * 0.5f;
            CreateLocalCube("Outline_North", new Vector3(0f, y, halfDepth),
                new Vector3(width, OutlineHeight, OutlineThickness), material, parent, false);
            CreateLocalCube("Outline_South", new Vector3(0f, y, -halfDepth),
                new Vector3(width, OutlineHeight, OutlineThickness), material, parent, false);
            CreateLocalCube("Outline_West", new Vector3(-halfWidth, y, 0f),
                new Vector3(OutlineThickness, OutlineHeight, depth), material, parent, false);
            CreateLocalCube("Outline_East", new Vector3(halfWidth, y, 0f),
                new Vector3(OutlineThickness, OutlineHeight, depth), material, parent, false);
        }

        private static void CreateStairWidthMarker(
            Transform parent,
            string name,
            float centerX,
            float centerZ,
            HousePrototypeBuilder.Palette palette)
        {
            Transform marker = CreateGroup(name + "_Width_2.2m", parent);
            marker.localPosition = new Vector3(centerX, 0f, centerZ);
            CreateLocalCube(
                "ConfirmedWidth",
                new Vector3(0f, MarkerThickness * 0.5f, 0f),
                new Vector3(StairWidth, MarkerThickness, 0.18f),
                palette.Metal,
                marker,
                false);
            CreateTextLabel(
                "Label",
                $"{name} width 2.2m\nslope / landing / height TBD",
                new Vector3(0f, 0.14f, -0.22f),
                marker);
        }

        private static void CreateCrossMarker(
            string name,
            Vector3 localPosition,
            float size,
            Material material,
            Transform parent)
        {
            Transform marker = CreateGroup(name, parent);
            marker.localPosition = localPosition;
            float y = MarkerThickness * 0.5f;
            CreateLocalCube("X", new Vector3(0f, y, 0f),
                new Vector3(size, MarkerThickness, 0.08f), material, marker, false, 45f);
            CreateLocalCube("Z", new Vector3(0f, y, 0f),
                new Vector3(size, MarkerThickness, 0.08f), material, marker, false, -45f);
        }

        private static void CreateTextLabel(
            string name,
            string text,
            Vector3 localPosition,
            Transform parent)
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
            label.characterSize = 0.075f;
            label.color = Color.white;
        }

        private static void ValidateFloor(
            Transform root,
            string floorName,
            float expectedElevation,
            float expectedWidth,
            float expectedDepth,
            int expectedRoomCount)
        {
            Transform floor = RequireChild(root, floorName);
            RequireOne(floor.localScale, $"{floorName} scale");
            RequireApproximately(floor.localPosition.y, expectedElevation, $"{floorName} 높이");

            Transform slab = RequireChild(RequireChild(floor, "Structure"), "FloorSlab");
            RequireApproximately(slab.localScale.x, expectedWidth, $"{floorName} 폭");
            RequireApproximately(slab.localScale.z, expectedDepth, $"{floorName} 깊이");

            int actualRoomCount = RequireChild(floor, RoomsGroupName).childCount;
            if (actualRoomCount != expectedRoomCount)
            {
                throw new InvalidOperationException(
                    $"{floorName} 방 개수 오류: {actualRoomCount}, 기획값 {expectedRoomCount}");
            }

            RequireChild(floor, CirculationGroupName);
            RequireChild(floor, PendingGroupName);
        }

        private static Bounds CalculateColliderBounds(
            IEnumerable<Transform> roots,
            string label)
        {
            if (roots == null)
                throw new ArgumentNullException(nameof(roots));

            // 생성 직후 부모 Transform을 옮긴 프레임에도 world-space Collider.bounds가
            // 이전 위치를 반환하지 않도록 명시적으로 물리 Transform을 동기화한다.
            Physics.SyncTransforms();

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Transform root in roots)
            {
                if (root == null)
                    continue;

                foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                {
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
                throw new InvalidOperationException($"{label}에서 Collider bounds를 계산할 수 없습니다.");
            return bounds;
        }

        private static IEnumerable<Transform> DirectChildren(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
                yield return parent.GetChild(i);
        }

        private static Transform RequireChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child == null)
                throw new MissingReferenceException($"'{parent.name}/{name}'을(를) 찾지 못했습니다.");
            return child;
        }

        private static void RequireOne(Vector3 value, string label)
        {
            if ((value - Vector3.one).sqrMagnitude > 0.0001f)
                throw new InvalidOperationException($"{label}이 1이 아닙니다: {value}");
        }

        private static void RequireApproximately(float actual, float expected, string label)
        {
            if (Mathf.Abs(actual - expected) > 0.01f)
                throw new InvalidOperationException($"{label} 오류: {actual:0.00}m, 기획값 {expected:0.00}m");
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            return group.transform;
        }

        private static GameObject CreateLocalCube(
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Transform parent,
            bool keepCollider = true,
            float yaw = 0f)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
            {
                Object.DestroyImmediate(cube.GetComponent<Collider>());
            }
            else
            {
                GameObjectUtility.SetStaticEditorFlags(cube, StaticEditorFlags.BatchingStatic);
            }

            return cube;
        }

        private readonly struct FloorGroups
        {
            internal readonly Transform Floor;
            internal readonly Transform Rooms;
            internal readonly Transform Circulation;
            internal readonly Transform Pending;

            internal FloorGroups(
                Transform floor,
                Transform rooms,
                Transform circulation,
                Transform pending)
            {
                Floor = floor;
                Rooms = rooms;
                Circulation = circulation;
                Pending = pending;
            }
        }
    }
}
