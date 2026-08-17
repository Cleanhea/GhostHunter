using System;
using System.Collections.Generic;
using GhostHunter.Core;
using GhostHunter.Furniture;
using GhostHunter.Interaction;
using GhostHunter.Map;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 09-map-generation의 House 1 도면을 자동 생성 로직 없이 씬에 고정 배치한다.
    /// 프리미티브만 사용해 방 크기, 출입구, 이동 동선을 빠르게 검증하는 용도다.
    ///
    /// 방 안에는 가구를 놓지 않는다. 던질 수 있는 가구는 <see cref="CreateFurnitureLibrary"/>가
    /// 집 북쪽에 종류별로 한 개씩 일렬로 만들어 두고, 방 배치는 거기서 복사해 붙여 넣는다.
    /// </summary>
    internal static class HousePrototypeBuilder
    {
        /// <summary>
        /// 평면(X·Z) 배율. 도면 좌표에 이 값을 곱해 방을 넓힌다.
        ///
        /// <b>높이와 사람 기준 치수는 배율을 받지 않는다</b> — 벽 높이·벽 두께·문 폭·창 크기·
        /// 붙박이·계단·가구는 그대로다. 방만 넓어지고 그 안의 물건은 원래 크기로 남는다.
        /// 그래서 배율은 트랜스폼 스케일이 아니라 <b>좌표에만</b> 곱한다. 아래 상수 중
        /// MapScale 이 붙은 것이 "평면 좌표", 붙지 않은 것이 "사람 기준 치수"다.
        /// </summary>
        internal const float GameplayMapScale = 2f;
        internal const float OriginalMapScale = 1f;
        internal const float SideBySideGap = 3f;

        private static float _activeMapScale = GameplayMapScale;
        internal static float MapScale => _activeMapScale;

        // ── 사람 기준 치수: 배율 없음.
        private const float WallThickness = 0.18f;
        private const float HalfWall = WallThickness * 0.5f;
        private const float WallHeight = 2.5f;
        private const float BedroomDoorWidth = 1.2f;
        private const float ServiceDoorWidth = 0.9f;
        private const float KitchenPassageWidth = 1.4f;
        private const float FrontDoorWidth = 1.5f;

        // ── 평면 좌표: 전부 "벽 중심선" 기준이고 배율이 곱해져 있다.
        //    도면 실측 외곽 12.8 x 10.4m → 실제 25.6 x 20.8m.
        private static float OuterWest => -6.4f * MapScale;
        private static float OuterEast => 6.4f * MapScale;
        private static float OuterNorth => 5.2f * MapScale;

        /// <summary>
        /// 남쪽 외벽. 도면은 -5.2(세로 10.4m)지만 그러면 창고가 폭보다 두 배 가까이 긴
        /// 복도 같은 방이 된다. 창고 세로를 도면값(1.8m)으로 지키고 <b>남쪽 외벽을 창고
        /// 남쪽 벽선에 맞춰</b> 끌어올렸다 — 밑 벽이 거실·창고를 가로질러 한 줄로 이어진다.
        /// </summary>
        private static float OuterSouth => BathroomSouth - WallThickness - 1.8f * MapScale;

        // 침실 슬롯은 Bedroom 프리셋(3.8 x 3.6m) 치수를 따른다. 어떤 프리셋이 뽑혀도 슬롯에
        // 그대로 들어가야 하므로 침실 1·2가 같은 값을 쓴다 — 도면의 침실2(3.4 x 3.2m)는 버린다.
        // 남는 폭은 주방이 흡수한다.
        private static float BedroomWidth => 3.8f * MapScale;
        private static float BedroomDepth => 3.6f * MapScale;

        // 북측 3개 방(침실1 · 주방 · 침실2)의 남쪽 벽과 칸막이.
        private static float NorthRoomSouth => OuterNorth - WallThickness - BedroomDepth;
        private static float Bedroom01East => OuterWest + WallThickness + BedroomWidth;
        private static float Bedroom02West => OuterEast - WallThickness - BedroomWidth;
        private static float KitchenCenter => (Bedroom01East + Bedroom02West) * 0.5f;
        private static float KitchenWestFace => Bedroom01East + HalfWall;
        private static float KitchenEastFace => Bedroom02West - HalfWall;
        private static float NorthRoomNorthFace => OuterNorth - HalfWall;
        private static float NorthRoomCenterZ => (NorthRoomSouth + OuterNorth) * 0.5f;

        // 거실은 L자로 들어간 서쪽 외벽과, 욕실·창고를 가르는 서비스 벽 사이다.
        // 서비스 벽 위치는 욕실·창고 내부 폭(도면 2.4m)에서 역산한다.
        private static float LivingWest => -4.25f * MapScale;
        private static float ServiceWest => OuterEast - WallThickness - 2.4f * MapScale;
        private static float BathroomSouth => -0.9f * MapScale;

        // ── 개구부: 자리는 배율을 받고 폭은 그대로다. 방이 넓어져도 문은 사람 크기로 남는다.
        private static float Bedroom01DoorEast => Bedroom01East - HalfWall - 0.2f;
        private static float Bedroom01DoorWest => Bedroom01DoorEast - BedroomDoorWidth;
        private static float Bedroom02DoorEast => ServiceWest - HalfWall;
        private static float Bedroom02DoorWest => Bedroom02DoorEast - BedroomDoorWidth;
        private static float KitchenPassageWest => KitchenCenter - KitchenPassageWidth * 0.5f;
        private static float KitchenPassageEast => KitchenCenter + KitchenPassageWidth * 0.5f;
        private static float FrontDoorWest => 0.15f * MapScale;
        private static float FrontDoorEast => FrontDoorWest + FrontDoorWidth;
        private static float StorageDoorSouth => -2.2f * MapScale;
        private static float StorageDoorNorth => StorageDoorSouth + ServiceDoorWidth;
        private static float BathroomDoorSouth => 0.45f * MapScale;
        private static float BathroomDoorNorth => BathroomDoorSouth + ServiceDoorWidth;

        // 바닥으로 취급할 그룹 이름. 가구 겹침 검사에서 "지지면에 닿은 것"은 겹침이 아니다.
        private const string FloorGroupName = "Rooms_Fixed";
        private const string GroundGroupName = "Ground";

        /// <summary>실제 침실 한 칸의 내부 치수. 프리셋은 이 크기에 맞춰 가구를 벽에 붙인다.</summary>
        private static float SlotWidth => BedroomWidth;
        private static float SlotDepth => BedroomDepth;
        private static float SlotHalfX => SlotWidth * 0.5f;
        private static float SlotHalfZ => SlotDepth * 0.5f;

        /// <summary>프리셋 가구를 벽·이웃 가구에서 띄우는 최소 간격.</summary>
        private const float WallGap = 0.06f;
        private const float ItemGap = 0.08f;

        // 가구 라이브러리: 집 북쪽(z+) 바깥에 일렬로 둔다. 집과 겹치지 않고, 씬 뷰를 위에서
        // 내려다볼 때 집 위쪽에 한 줄로 보이는 자리다.
        private static float LibraryRowZ => OuterNorth + 5f;
        private const float LibraryGap = 0.6f;

        // 방 프리셋 전시장: 집 남쪽(z-) 바깥. 현관 계단보다 더 남쪽이다.
        private static float PresetRowZ => OuterSouth - SlotHalfZ - 6f;
        private static float PresetPitch => SlotWidth + 1.4f;
        private const float PresetWallHeight = 0.5f;

        /// <summary>
        /// 프리셋 가구를 놓을 수 없는 남쪽 띠. 두 침실의 문 위치가 서로 달라서, 어느 슬롯에
        /// 들어가도 문이 쓸고 지나가지 않으려면 이 선보다 남쪽은 비워야 한다
        /// (문짝이 남쪽 벽 중심선에서 그리는 사분원의 최북단).
        /// 예외는 두 문 사이 가운데 띠(<see cref="PresetDoorFreeWest"/>~<see cref="PresetDoorFreeEast"/>)로,
        /// 어느 문도 지나가지 않으므로 남쪽 벽까지 쓸 수 있다.
        /// </summary>
        private static float PresetSouthKeepOut => -SlotHalfZ - HalfWall + BedroomDoorWidth;

        private static float PresetDoorFreeWest =>
            Bedroom02DoorEast - (Bedroom02West + OuterEast) * 0.5f;

        private static float PresetDoorFreeEast =>
            Bedroom01DoorWest - (OuterWest + Bedroom01East) * 0.5f;

        /// <summary>문짝이 도는 동안 훑어볼 각도 개수. 열림·닫힘 두 점만 보면 중간에 걸리는 걸 놓친다.</summary>
        private const int DoorSwingSamples = 12;

        /// <summary>맵 가구를 잡고 던질 수 있게 만드는 데 필요한 공용 에셋.</summary>
        internal sealed class PhysicsAssets
        {
            internal readonly FurnitureDefinition LightDefinition;
            internal readonly FurnitureDefinition HeavyDefinition;
            internal readonly FurnitureThrowSettings ThrowSettings;
            internal readonly Material OutlineMaterial;

            internal PhysicsAssets(
                FurnitureDefinition lightDefinition,
                FurnitureDefinition heavyDefinition,
                FurnitureThrowSettings throwSettings,
                Material outlineMaterial)
            {
                LightDefinition = lightDefinition;
                HeavyDefinition = heavyDefinition;
                ThrowSettings = throwSettings;
                OutlineMaterial = outlineMaterial;
            }
        }

        internal sealed class Palette
        {
            internal readonly Material WoodFloor;
            internal readonly Material TileFloor;
            internal readonly Material Wall;
            internal readonly Material Trim;
            internal readonly Material Wood;
            internal readonly Material Fabric;
            internal readonly Material Bedding;
            internal readonly Material Ceramic;
            internal readonly Material Metal;
            internal readonly Material Window;

            internal Palette(
                Material woodFloor,
                Material tileFloor,
                Material wall,
                Material trim,
                Material wood,
                Material fabric,
                Material bedding,
                Material ceramic,
                Material metal,
                Material window)
            {
                WoodFloor = woodFloor;
                TileFloor = tileFloor;
                Wall = wall;
                Trim = trim;
                Wood = wood;
                Fabric = fabric;
                Bedding = bedding;
                Ceramic = ceramic;
                Metal = metal;
                Window = window;
            }
        }

        /// <summary>
        /// 만든 가구를 물리·네트워크 배선까지 끝내 배치 목록에 넣는 콜백.
        /// <paramref name="supportHeight"/>는 가구가 앉을 지지면 높이다 — 바닥은 0,
        /// 책상 위 소품은 상판 윗면을 넘긴다(<see cref="TableTopSurface"/>).
        /// </summary>
        private delegate void AddFurniture(
            Transform item,
            FurnitureWeightClass weightClass,
            float supportHeight = 0f);

        internal static Transform Create(Palette palette)
        {
            return CreateAtScale(palette, GameplayMapScale, "House_01", Vector3.zero, null);
        }

        /// <summary>
        /// 도면 치수 그대로(배율 ×1) 지은 비교용 집. 게임플레이용 House_01 과 달리
        /// <b>방마다 가구를 깔아서</b> 만든다 — "도면 크기에서 사람과 가구가 어떻게 느껴지는가"를
        /// 보려고 세워 둔 집이라, 라이브러리에서 복사해 넣을 때까지 비워 둘 이유가 없다.
        /// </summary>
        internal static Transform CreateOriginalScaleHouseRight(Palette palette, PhysicsAssets physics)
        {
            if (physics == null)
                throw new ArgumentNullException(nameof(physics));

            float centerX = 6.4f * GameplayMapScale
                + SideBySideGap
                + 6.4f * OriginalMapScale;
            return CreateAtScale(
                palette,
                OriginalMapScale,
                "House_01_OriginalScale_Right",
                new Vector3(centerX, 0f, 0f),
                physics);
        }

        /// <param name="furnishings">
        /// null 이면 방을 비운 채로 만든다(House_01 규칙). 넘기면 방마다 가구를 깔아 준다.
        /// </param>
        private static Transform CreateAtScale(
            Palette palette,
            float mapScale,
            string rootName,
            Vector3 worldPosition,
            PhysicsAssets furnishings)
        {
            if (palette == null)
                throw new ArgumentNullException(nameof(palette));
            if (mapScale <= 0f)
                throw new ArgumentOutOfRangeException(nameof(mapScale));

            float previousScale = _activeMapScale;
            _activeMapScale = mapScale;

            try
            {
                var house = new GameObject(rootName);
                Transform rooms = CreateGroup(FloorGroupName, house.transform);
                Transform shell = CreateGroup("Walls_Doors_Windows", house.transform);
                Transform fixtures = CreateGroup("Fixtures", house.transform);
                Transform lights = CreateGroup("RoomLights", house.transform);

                // 방에 놓을 가구가 들어가는 자리. House_01 은 생성 직후 비어 있다.
                Transform furniture = CreateGroup("PhysicsFurniture", house.transform);

                CreateFloors(rooms, palette);
                CreateWalls(shell, palette);
                CreateDoorsAndWindows(shell, palette);
                CreateKitchenFixtures(fixtures, palette);
                CreateBathroom(fixtures, palette);
                CreateFrontSteps(house.transform, palette);
                CreateRoomLights(lights);

                if (furnishings != null)
                    FurnishRooms(furniture, palette, furnishings);

                house.transform.position = worldPosition;
                return house.transform;
            }
            finally
            {
                _activeMapScale = previousScale;
            }
        }

        private static void CreateFloors(Transform parent, Palette palette)
        {
            CreateFloor("Bedroom_01_A_Floor", OuterWest, Bedroom01East,
                NorthRoomSouth, OuterNorth, palette.WoodFloor, parent);
            CreateFloor("Kitchen_Floor", Bedroom01East, Bedroom02West,
                NorthRoomSouth, OuterNorth, palette.TileFloor, parent);
            CreateFloor("Bedroom_02_B_Floor", Bedroom02West, OuterEast,
                NorthRoomSouth, OuterNorth, palette.WoodFloor, parent);
            CreateFloor("LivingRoom_Floor", LivingWest, ServiceWest,
                OuterSouth, NorthRoomSouth, palette.WoodFloor, parent);
            CreateFloor("Bathroom_Floor", ServiceWest, OuterEast,
                BathroomSouth, NorthRoomSouth, palette.TileFloor, parent);
            CreateFloor("Storage_Floor", ServiceWest, OuterEast,
                OuterSouth, BathroomSouth, palette.TileFloor, parent);
        }

        private static void CreateWalls(Transform parent, Palette palette)
        {
            // 외벽: 도면의 남서쪽 L자 홈만 남은 외곽. 남쪽 벽은 창고 남쪽 벽선에서 한 줄로 이어진다.
            WallX("North_Outer", OuterWest, OuterEast, OuterNorth, palette.Wall, parent);
            WallZ("West_Bedroom_Outer", NorthRoomSouth, OuterNorth, OuterWest, palette.Wall, parent);
            WallZ("West_Living_Outer", OuterSouth, NorthRoomSouth, LivingWest, palette.Wall, parent);
            WallX("Bedroom01_South_Outer", OuterWest, LivingWest, NorthRoomSouth, palette.Wall, parent);
            WallZ("East_Outer", OuterSouth, OuterNorth, OuterEast, palette.Wall, parent);

            // 남측 현관 개구부.
            WallX("South_Outer_West", LivingWest, FrontDoorWest, OuterSouth, palette.Wall, parent);
            WallX("South_Outer_East", FrontDoorEast, OuterEast, OuterSouth, palette.Wall, parent);

            // 북측 방 구획. 침실 슬롯 폭(3.8m)이 두 칸막이 위치를 정한다.
            WallZ("Bedroom01_Kitchen", NorthRoomSouth, OuterNorth, Bedroom01East, palette.Wall, parent);
            WallZ("Kitchen_Bedroom02", NorthRoomSouth, OuterNorth, Bedroom02West, palette.Wall, parent);

            // 침실 1 문. 칸막이 쪽에 붙여 두어 거실에서 바로 들어오게 한다.
            WallX("Bedroom01_South_West", LivingWest, Bedroom01DoorWest, NorthRoomSouth, palette.Wall, parent);
            WallX("Bedroom01_South_East", Bedroom01DoorEast, Bedroom01East, NorthRoomSouth, palette.Wall, parent);

            // 주방-거실 실내 이동문.
            WallX("Kitchen_South_West", Bedroom01East, KitchenPassageWest, NorthRoomSouth, palette.Wall, parent);
            WallX("Kitchen_South_East", KitchenPassageEast, Bedroom02West, NorthRoomSouth, palette.Wall, parent);

            // 침실 2 문. 서비스 벽(욕실 모서리) 쪽에 붙인다 — 그 서쪽은 전부 거실이다.
            WallX("Bedroom02_South_West", Bedroom02West, Bedroom02DoorWest, NorthRoomSouth, palette.Wall, parent);
            WallX("Bedroom02_South_East", ServiceWest, OuterEast, NorthRoomSouth, palette.Wall, parent);

            // 거실과 서비스 공간 사이: 욕실/창고 문. 북쪽 조각은 남쪽 벽 두께까지 물려 모서리를 막는다.
            // 남쪽 조각은 창고 아래로 내려가면 거실의 동쪽 외벽이 된다.
            WallZ("Service_Wall_South", OuterSouth, StorageDoorSouth, ServiceWest, palette.Wall, parent);
            WallZ("Service_Wall_Middle", StorageDoorNorth, BathroomDoorSouth, ServiceWest, palette.Wall, parent);
            WallZ("Service_Wall_North", BathroomDoorNorth, NorthRoomSouth + HalfWall, ServiceWest, palette.Wall, parent);
            WallX("Bathroom_Storage", ServiceWest, OuterEast, BathroomSouth, palette.Wall, parent);

            CreateBaseTrim(parent, palette.Trim);
        }

        private static void CreateBaseTrim(Transform parent, Material material)
        {
            const float trimHeight = 0.12f;
            const float trimDepth = 0.04f;
            CreateCube("North_BaseTrim", new Vector3(0f, trimHeight * 0.5f, NorthRoomNorthFace - 0.03f),
                new Vector3(12.6f * MapScale, trimHeight, trimDepth), material, parent, false);
            CreateCube("East_BaseTrim",
                new Vector3(OuterEast - HalfWall - 0.03f, trimHeight * 0.5f, (OuterSouth + OuterNorth) * 0.5f),
                new Vector3(trimDepth, trimHeight, OuterNorth - OuterSouth - 0.2f), material, parent, false);
            CreateCube("Living_West_BaseTrim",
                new Vector3(LivingWest + HalfWall + 0.03f, trimHeight * 0.5f, (OuterSouth + NorthRoomSouth) * 0.5f),
                new Vector3(trimDepth, trimHeight, NorthRoomSouth - OuterSouth - 0.2f), material, parent, false);
        }

        private static void CreateDoorsAndWindows(Transform parent, Palette palette)
        {
            CreateDoor("FrontDoor_1.5m", new Vector3(FrontDoorEast, 0f, OuterSouth),
                FrontDoorWidth, 90f, true, -1f, palette, parent);
            CreateDoor("Bedroom01_Door_1.2m", new Vector3(Bedroom01DoorWest, 0f, NorthRoomSouth),
                BedroomDoorWidth, -90f, true, palette, parent);
            CreateDoor("Bedroom02_Door_1.2m", new Vector3(Bedroom02DoorEast, 0f, NorthRoomSouth),
                BedroomDoorWidth, 90f, true, -1f, palette, parent);
            CreateDoor("Bathroom_Door_0.9m", new Vector3(ServiceWest, 0f, BathroomDoorNorth),
                ServiceDoorWidth, -90f, false, -1f, palette, parent);
            CreateDoor("Storage_Door_0.9m", new Vector3(ServiceWest, 0f, StorageDoorNorth),
                ServiceDoorWidth, -90f, false, -1f, palette, parent);

            CreateDoorFrameX("Kitchen_OpenPassage_1.4m", KitchenCenter, NorthRoomSouth,
                KitchenPassageWidth, palette.Trim, parent);

            float northGlassZ = OuterNorth - 0.11f;
            float southGlassZ = OuterSouth + 0.11f;
            CreateWindowX("Bedroom01_Window", (OuterWest + Bedroom01East) * 0.5f, northGlassZ, 1.45f, palette, parent);
            CreateWindowX("Kitchen_Window", KitchenCenter, northGlassZ, 1.35f, palette, parent);
            CreateWindowX("Bedroom02_Window", (Bedroom02West + OuterEast) * 0.5f, northGlassZ, 1.45f, palette, parent);
            CreateWindowX("Living_SouthWindow_West", -2.25f * MapScale, southGlassZ, 1.25f, palette, parent);
            CreateWindowX("Living_SouthWindow_East", 2.75f * MapScale, southGlassZ, 1.25f, palette, parent);
        }

        /// <summary>
        /// 주방 붙박이(카운터·싱크·쿡탑). 던질 수 없고 정적 배칭 대상이다.
        /// 벽에 붙어 있어야 하므로 좌표를 방 내부 면에서 잡는다.
        /// </summary>
        private static void CreateKitchenFixtures(Transform parent, Palette palette)
        {
            Transform builtIn = CreateGroup("Kitchen_BuiltIn", parent);

            // 붙박이 치수는 사람 기준이라 배율이 없다. 방이 넓어지면 벽을 따라갈 뿐이다.
            float northCounterX = KitchenWestFace + 1.71f;
            float northCounterZ = NorthRoomNorthFace - 0.29f;
            float eastCounterX = KitchenEastFace - 0.28f;
            float eastCounterZ = NorthRoomNorthFace - 1.85f;
            CreateCube("NorthCounter", new Vector3(northCounterX, 0.45f, northCounterZ),
                new Vector3(3.25f, 0.9f, 0.58f), palette.Wood, builtIn);
            CreateCube("NorthCounterTop", new Vector3(northCounterX, 0.93f, northCounterZ),
                new Vector3(3.3f, 0.08f, 0.62f), palette.Metal, builtIn);
            CreateCube("EastCounter", new Vector3(eastCounterX, 0.45f, eastCounterZ),
                new Vector3(0.58f, 0.9f, 2.3f), palette.Wood, builtIn);
            CreateCube("EastCounterTop", new Vector3(eastCounterX, 0.93f, eastCounterZ),
                new Vector3(0.62f, 0.08f, 2.35f), palette.Metal, builtIn);
            CreateCube("Sink", new Vector3(northCounterX + 0.3f, 0.99f, northCounterZ),
                new Vector3(0.7f, 0.05f, 0.38f), palette.Ceramic, builtIn);
            CreateCooktop(new Vector3(eastCounterX - 0.01f, 1f, eastCounterZ + 0.55f), palette, builtIn);
        }

        /// <summary>욕실 붙박이. 치수는 사람 기준이라 그대로고, 자리만 방 안쪽 면에서 잡는다.</summary>
        private static void CreateBathroom(Transform parent, Palette palette)
        {
            float west = ServiceWest + HalfWall;
            float east = OuterEast - HalfWall;
            float south = BathroomSouth + HalfWall;
            float north = NorthRoomSouth - HalfWall;

            Transform room = CreateGroup("Bathroom_BuiltIn", parent);
            CreateCube("ShowerTray", new Vector3(east - 0.53f, 0.08f, north - 0.58f),
                new Vector3(0.95f, 0.16f, 1.05f), palette.Ceramic, room);
            CreateCube("ShowerBack", new Vector3(east - 0.04f, 1.05f, north - 0.58f),
                new Vector3(0.08f, 2.05f, 1.05f), palette.Window, room);
            CreateToilet(new Vector3(west + 0.51f, 0f, south + 0.46f), palette, room);
            CreateVanity(new Vector3(east - 0.76f, 0f, south + 0.36f), palette, room);
        }

        /// <summary>현관 계단. 계단참 높이는 사람 기준이라 그대로고, 현관문 앞으로만 옮긴다.</summary>
        private static void CreateFrontSteps(Transform parent, Palette palette)
        {
            float doorCenter = (FrontDoorWest + FrontDoorEast) * 0.5f;
            Transform steps = CreateGroup("FrontEntrance", parent);
            CreateCube("Porch", new Vector3(doorCenter, -0.08f, OuterSouth - 0.35f),
                new Vector3(2.2f, 0.16f, 0.65f), palette.TileFloor, steps);
            CreateCube("Step_01", new Vector3(doorCenter, -0.18f, OuterSouth - 0.75f),
                new Vector3(1.9f, 0.18f, 0.35f), palette.TileFloor, steps);
            CreateCube("Step_02", new Vector3(doorCenter, -0.28f, OuterSouth - 1.05f),
                new Vector3(1.55f, 0.18f, 0.3f), palette.TileFloor, steps);
        }

        /// <summary>
        /// 방마다 채움광 하나. 천장 높이는 그대로인데 바닥만 넓어졌으므로 높이는 두고
        /// 사거리와 밝기만 올린다 — 사거리를 안 늘리면 넓어진 방의 구석이 아예 안 닿는다.
        /// </summary>
        private static void CreateRoomLights(Transform parent)
        {
            float serviceX = (ServiceWest + OuterEast) * 0.5f;

            void Room(string name, Vector3 position, float range, float intensity)
            {
                CreatePointLight(name, position, range * MapScale, intensity * MapScale, parent);
            }

            Room("LivingRoom_Light",
                new Vector3((LivingWest + ServiceWest) * 0.5f, 2.25f, (OuterSouth + NorthRoomSouth) * 0.5f),
                8f, 5.6f);
            Room("Bedroom01_Light",
                new Vector3((OuterWest + Bedroom01East) * 0.5f, 2.1f, NorthRoomCenterZ), 5f, 3.4f);
            Room("Kitchen_Light", new Vector3(KitchenCenter, 2.1f, NorthRoomCenterZ), 5f, 3.7f);
            Room("Bedroom02_Light",
                new Vector3((Bedroom02West + OuterEast) * 0.5f, 2.1f, NorthRoomCenterZ), 5f, 3.6f);
            Room("Bathroom_Light",
                new Vector3(serviceX, 2f, (BathroomSouth + NorthRoomSouth) * 0.5f), 3.5f, 2.7f);
            Room("Storage_Light",
                new Vector3(serviceX, 2f, (OuterSouth + BathroomSouth) * 0.5f), 3.5f, 2.8f);
        }

        // ── 도면 배율(×1) 비교용 집의 방 배치.
        //    좌표는 전부 방 안쪽 면에서 역산하므로 벽을 옮기면 가구가 따라간다. 문짝이 도는
        //    사분원과 방 한가운데 통행로는 비워 둔다 — ValidateFurnishedHouse 가 그걸 검사한다.

        private static void FurnishRooms(Transform parent, Palette palette, PhysicsAssets physics)
        {
            void Add(Transform item, FurnitureWeightClass weightClass, float supportHeight = 0f)
            {
                MakePhysical(item, physics, weightClass, supportHeight);
            }

            FurnishBedroom01(parent, palette, Add);
            FurnishBedroom02(parent, palette, Add);
            FurnishKitchen(parent, palette, Add);
            FurnishLivingRoom(parent, palette, Add);
            FurnishStorage(parent, palette, Add);
        }

        /// <summary>침실1 — 도면 Bedroom_A 구성(옷장·싱글침대·협탁·책상·의자)에 소품을 얹었다.</summary>
        private static void FurnishBedroom01(Transform items, Palette palette, AddFurniture add)
        {
            float west = OuterWest + HalfWall;
            float east = Bedroom01East - HalfWall;
            float north = OuterNorth - HalfWall;
            float south = NorthRoomSouth + HalfWall;

            // 침대는 동쪽 벽에 머리를 북쪽으로. 문이 도는 남서쪽 사분원에서 가장 먼 자리다.
            float bedX = east - 0.54f - WallGap;
            float bedHeadZ = north - 1.01f - WallGap;
            add(CreateBed("SingleBed_1.0x2.0", new Vector3(bedX, 0f, bedHeadZ),
                    1f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);

            float nightstandX = bedX - 0.54f - ItemGap - 0.225f;
            float nightstandZ = north - WallGap - 0.2f;
            add(CreateTable("Nightstand_0.45x0.4", new Vector3(nightstandX, 0f, nightstandZ),
                    new Vector3(0.45f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);

            // 옷장(위) · 책상(아래): 서쪽 벽. 둘 다 앞면이 방 안쪽(동쪽)을 본다.
            float westWallX = west + 0.3f + WallGap;
            float wardrobeZ = north - WallGap - 0.6f;
            add(CreateCabinet("Wardrobe_1.2x0.6", new Vector3(westWallX, 0f, wardrobeZ),
                    new Vector3(1.2f, 1.85f, 0.6f), palette, items, -90f),
                FurnitureWeightClass.Heavy);

            float deskZ = ((wardrobeZ - 0.6f) + south) * 0.5f;
            add(CreateTable("Desk_1.2x0.6", new Vector3(westWallX, 0f, deskZ),
                    new Vector3(1.2f, 0.6f), 90f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            add(CreateChair("DeskChair_0.55x0.55",
                    new Vector3(westWallX + 0.3f + ItemGap + 0.275f, 0f, deskZ),
                    90f, palette, items, 0.55f),
                FurnitureWeightClass.Light);

            // 소품: 책상과 옷장 사이 벽에 쓰레기통, 책상 위에 책.
            add(CreateProp("TrashBin_0.3",
                    new Vector3(west + WallGap + 0.15f, 0f, ((deskZ + 0.6f) + (wardrobeZ - 0.6f)) * 0.5f),
                    new Vector3(0.3f, 0.4f, 0.3f), palette.Metal, items),
                FurnitureWeightClass.Light);
            add(CreateProp("Book_Stack_0.22", new Vector3(westWallX, 0f, deskZ - 0.28f),
                    new Vector3(0.22f, 0.09f, 0.3f), palette.Trim, items),
                FurnitureWeightClass.Light, TableTopSurface(0.72f));
        }

        /// <summary>침실2 — 도면 Bedroom_C 구성(싱글침대·책상·의자·책장·서랍장·수납함).</summary>
        private static void FurnishBedroom02(Transform items, Palette palette, AddFurniture add)
        {
            float west = Bedroom02West + HalfWall;
            float east = OuterEast - HalfWall;
            float north = OuterNorth - HalfWall;
            float south = NorthRoomSouth + HalfWall;

            add(CreateBed("SingleBed_1.1x2.0",
                    new Vector3(west + 0.59f + WallGap, 0f, north - 1.01f - WallGap),
                    1.1f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);

            // 책상 + 의자: 북동쪽 모서리. 의자는 책상을 보고 앉는다.
            float deskX = east - WallGap - 0.5f;
            float deskZ = north - WallGap - 0.25f;
            add(CreateTable("Desk_1.0x0.5", new Vector3(deskX, 0f, deskZ),
                    new Vector3(1f, 0.5f), 0f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            add(CreateChair("Chair_0.5x0.5",
                    new Vector3(deskX, 0f, deskZ - 0.25f - ItemGap - 0.25f), 180f, palette, items),
                FurnitureWeightClass.Light);

            // 서랍장(남쪽) + 책장(가운데): 동쪽 벽.
            float dresserZ = south + 0.44f;
            add(CreateCabinet("Dresser_0.8x0.45", new Vector3(east - WallGap - 0.225f, 0f, dresserZ),
                    new Vector3(0.8f, 0.82f, 0.45f), palette, items, 90f),
                FurnitureWeightClass.Heavy);
            add(CreateShelf("Bookshelf_0.9x0.3",
                    new Vector3(east - WallGap - 0.169f, 0f, dresserZ + 1.05f),
                    new Vector3(0.3f, 1.6f, 0.9f), palette, items),
                FurnitureWeightClass.Heavy);

            // 수납함은 문 동쪽 남쪽 벽. 문짝(서쪽으로 열림)이 지나가지 않는 쪽이다.
            add(CreateProp("StorageChest_0.9x0.45",
                    new Vector3(Bedroom02DoorEast + 0.92f, 0f, south + WallGap + 0.225f),
                    new Vector3(0.9f, 0.5f, 0.45f), palette.Wood, items),
                FurnitureWeightClass.Light);

            float deskTop = TableTopSurface(0.72f);
            add(CreateProp("Cup_0.12", new Vector3(deskX - 0.3f, 0f, deskZ),
                    new Vector3(0.12f, 0.14f, 0.12f), palette.Ceramic, items),
                FurnitureWeightClass.Light, deskTop);
            add(CreateProp("Book_Stack_0.22", new Vector3(deskX + 0.3f, 0f, deskZ),
                    new Vector3(0.22f, 0.09f, 0.3f), palette.Trim, items),
                FurnitureWeightClass.Light, deskTop);
        }

        /// <summary>주방 — 붙박이(북·동 카운터)를 뺀 자리에 식탁 한 벌.</summary>
        private static void FurnishKitchen(Transform items, Palette palette, AddFurniture add)
        {
            float south = NorthRoomSouth + HalfWall;

            // 식탁은 서쪽으로 붙인다. 방 한가운데를 비워야 주방을 가로질러 다닐 수 있다.
            float tableX = KitchenWestFace + 1.08f;
            float tableZ = south + 1.49f;
            add(CreateTable("DiningTable_1.55x0.85", new Vector3(tableX, 0f, tableZ),
                    new Vector3(1.55f, 0.85f), 0f, palette.Wood, items, 0.74f),
                FurnitureWeightClass.Heavy);
            add(CreateChair("Chair_0.5x0.5",
                    new Vector3(tableX, 0f, tableZ - 0.425f - ItemGap - 0.26f), 180f, palette, items),
                FurnitureWeightClass.Light);
            add(CreateChair("Chair_0.5x0.5",
                    new Vector3(tableX, 0f, tableZ + 0.425f + ItemGap + 0.26f), 0f, palette, items),
                FurnitureWeightClass.Light);

            add(CreateProp("Crate_0.62", new Vector3(KitchenEastFace - 1.13f, 0f, south + 0.44f),
                    new Vector3(0.62f, 0.64f, 0.62f), palette.Wood, items),
                FurnitureWeightClass.Light);
        }

        /// <summary>거실 — 소파·좌탁·TV장. 현관문이 도는 반경 1.5m와 문 개구부는 비워 둔다.</summary>
        private static void FurnishLivingRoom(Transform items, Palette palette, AddFurniture add)
        {
            float west = LivingWest + HalfWall;
            float north = NorthRoomSouth - HalfWall;
            float south = OuterSouth + HalfWall;
            float east = ServiceWest - HalfWall;

            float sofaX = west + 1.66f;
            add(CreateSofa("Sofa_2.2x0.9", new Vector3(sofaX, 0f, south + WallGap + 0.45f),
                    180f, palette, items),
                FurnitureWeightClass.Heavy);

            float coffeeZ = south + 1.64f;
            add(CreateTable("CoffeeTable_1.25x0.65", new Vector3(sofaX, 0f, coffeeZ),
                    new Vector3(1.25f, 0.65f), 0f, palette.Wood, items, 0.42f),
                FurnitureWeightClass.Light);
            add(CreateChair("Chair_0.5x0.5", new Vector3(sofaX + 1.5f, 0f, coffeeZ),
                    90f, palette, items),
                FurnitureWeightClass.Light);

            // TV장은 침실1 문과 주방 이동문 사이 북쪽 벽. 둘 다 개구부를 가리면 안 된다.
            float consoleX = (Bedroom01DoorEast + KitchenPassageWest) * 0.5f;
            float consoleZ = north - WallGap - 0.225f;
            add(CreateCabinet("LivingConsole_1.6x0.45", new Vector3(consoleX, 0f, consoleZ),
                    new Vector3(1.6f, 0.62f, 0.45f), palette, items),
                FurnitureWeightClass.Heavy);

            Transform television = CreateTelevision(new Vector3(consoleX, 0f, consoleZ), palette, items);
            television.rotation = Quaternion.Euler(0f, 90f, 0f);
            add(television, FurnitureWeightClass.Light, 0.62f);

            add(CreateProp("Crate_0.45", new Vector3(east - 0.73f, 0f, south + 0.39f),
                    new Vector3(0.45f, 0.44f, 0.45f), palette.Wood, items),
                FurnitureWeightClass.Light);
        }

        /// <summary>창고 — 선반과 상자. 문짝이 도는 반경 0.9m 안쪽은 비워 둔다.</summary>
        private static void FurnishStorage(Transform items, Palette palette, AddFurniture add)
        {
            float west = ServiceWest + HalfWall;
            float east = OuterEast - HalfWall;
            float south = OuterSouth + HalfWall;
            float north = BathroomSouth - HalfWall;

            add(CreateShelf("Shelving_1.65x0.45",
                    new Vector3((west + east) * 0.5f, 0f, south + WallGap + 0.225f),
                    new Vector3(1.65f, 1.65f, 0.45f), palette, items),
                FurnitureWeightClass.Heavy);

            // 큰 상자 위에 작은 상자를 얹는다 — 얹을 자리를 지지면 높이로 넘기면 정확히 앉는다.
            var cratePosition = new Vector3(east - 0.41f, 0f, north - 0.61f);
            add(CreateProp("Crate_0.62", cratePosition,
                    new Vector3(0.62f, 0.64f, 0.62f), palette.Wood, items),
                FurnitureWeightClass.Light);
            add(CreateProp("Crate_0.45", cratePosition,
                    new Vector3(0.45f, 0.44f, 0.45f), palette.Wood, items),
                FurnitureWeightClass.Light, 0.64f);
        }

        /// <summary>
        /// 던질 수 있는 가구를 종류별로 한 개씩 집 북쪽에 일렬로 만든다.
        ///
        /// 방 안에 미리 배치하지 않는 이유: 방 구성은 손으로 잡는 것이 빠르고, 여기 있는 것을
        /// 복사해 붙여 넣으면 물리·네트워크 배선이 이미 끝난 가구가 그대로 하나 더 생긴다.
        /// 붙여 넣을 자리는 <c>House_01/PhysicsFurniture</c> 다.
        ///
        /// 자리 계산은 실제 콜라이더 크기로 한다 — 가구 치수를 바꿔도 줄 간격이 알아서 맞는다.
        /// </summary>
        internal static Transform CreateFurnitureLibrary(Palette palette, PhysicsAssets physics)
        {
            if (palette == null)
                throw new ArgumentNullException(nameof(palette));
            if (physics == null)
                throw new ArgumentNullException(nameof(physics));

            var libraryObject = new GameObject("Furniture_Library");
            Transform root = libraryObject.transform;
            Transform ground = CreateGroup(GroundGroupName, root);
            Transform items = CreateGroup("Items", root);

            float cursor = 0f;
            float rowDepth = 0f;

            void Add(Transform item, FurnitureWeightClass weightClass)
            {
                MakePhysical(item, physics, weightClass);

                Bounds bounds = MeasureColliderBounds(item);
                item.position += new Vector3(cursor - bounds.min.x, 0f, LibraryRowZ - bounds.center.z);
                cursor += bounds.size.x + LibraryGap;
                rowDepth = Mathf.Max(rowDepth, bounds.size.z);
            }

            // 침실
            Add(CreateBed("SingleBed_1.0x2.0", Vector3.zero, 1f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateBed("DoubleBed_1.6x2.0", Vector3.zero, 1.6f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateCabinet("Wardrobe_1.2x0.6", Vector3.zero,
                    new Vector3(0.6f, 1.85f, 1.2f), palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateCabinet("Wardrobe_1.5x0.6", Vector3.zero,
                    new Vector3(0.6f, 1.85f, 1.5f), palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateCabinet("Dresser_1.2x0.5", Vector3.zero,
                    new Vector3(0.5f, 0.82f, 1.2f), palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateTable("Desk_1.2x0.6", Vector3.zero,
                    new Vector3(1.2f, 0.6f), 0f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            Add(CreateTable("Vanity_1.0x0.5", Vector3.zero,
                    new Vector3(1f, 0.5f), 0f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            Add(CreateTable("Nightstand_0.45x0.4", Vector3.zero,
                    new Vector3(0.45f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);
            Add(CreateTable("BedsideTable_0.5x0.4", Vector3.zero,
                    new Vector3(0.5f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);
            Add(CreateChair("DeskChair_0.55x0.55", Vector3.zero, 0f, palette, items, 0.55f),
                FurnitureWeightClass.Light);
            Add(CreateChair("VanityStool_0.45x0.45", Vector3.zero, 0f, palette, items, 0.45f),
                FurnitureWeightClass.Light);

            // 주방
            Add(CreateTable("DiningTable_1.55x0.85", Vector3.zero,
                    new Vector3(1.55f, 0.85f), 0f, palette.Wood, items, 0.74f),
                FurnitureWeightClass.Heavy);
            Add(CreateChair("Chair_0.5x0.5", Vector3.zero, 0f, palette, items),
                FurnitureWeightClass.Light);

            // 거실
            Add(CreateSofa("Sofa_2.2x0.9", Vector3.zero, 0f, palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateTable("CoffeeTable_1.25x0.65", Vector3.zero,
                    new Vector3(1.25f, 0.65f), 0f, palette.Wood, items, 0.42f),
                FurnitureWeightClass.Light);
            Add(CreateCabinet("LivingConsole_1.6x0.45", Vector3.zero,
                    new Vector3(0.45f, 0.62f, 1.6f), palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateTelevision(Vector3.zero, palette, items), FurnitureWeightClass.Light);

            // 창고
            Add(CreateShelf("Shelving_2.6x0.55", Vector3.zero,
                    new Vector3(0.55f, 1.9f, 2.6f), palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateShelf("Shelving_1.65x0.45", Vector3.zero,
                    new Vector3(1.65f, 1.65f, 0.45f), palette, items),
                FurnitureWeightClass.Heavy);
            Add(CreateCube("Crate_0.62", new Vector3(0f, 0.32f, 0f),
                    new Vector3(0.62f, 0.64f, 0.62f), palette.Wood, items).transform,
                FurnitureWeightClass.Light);
            Add(CreateCube("Crate_0.45", new Vector3(0f, 0.22f, 0f),
                    new Vector3(0.45f, 0.44f, 0.45f), palette.Wood, items).transform,
                FurnitureWeightClass.Light);

            // 줄 전체를 집 중앙(x=0)에 맞춘다.
            float rowWidth = Mathf.Max(cursor - LibraryGap, 0f);
            items.position = new Vector3(-rowWidth * 0.5f, 0f, 0f);

            // 받침 바닥이 없으면 세션이 시작되는 순간 전부 허공으로 떨어진다.
            CreateCube("Library_Floor", new Vector3(0f, -0.1f, LibraryRowZ),
                new Vector3(rowWidth + 1.2f, 0.2f, rowDepth + 1.2f), palette.TileFloor, ground);

            return root;
        }

        /// <summary>
        /// 프리셋이 들어갈 자리. 방 중심(바닥면)이고 회전은 항등이라 +Z 가 방의 북쪽이다.
        /// 두 슬롯의 치수가 같아야(3.8 x 3.6m) 어떤 프리셋이 뽑혀도 그대로 들어간다.
        /// </summary>
        /// <summary>
        /// 거실 남쪽 현관 근처 스폰 자리. 집 외곽에서 역산하므로 남쪽 벽을 옮겨도 따라온다.
        /// 높이는 플레이어 기준이라 배율과 무관하다.
        /// </summary>
        internal static Vector3[] PlayerSpawnPositions()
        {
            return new[]
            {
                new Vector3(-0.4f * MapScale, 0.05f, OuterSouth + 1f * MapScale),
                new Vector3(1f * MapScale, 0.05f, OuterSouth + 1.8f * MapScale),
            };
        }

        /// <summary>집 전체가 화면에 들어오는 부감 카메라 자리.</summary>
        internal static (Vector3 Position, Vector3 LookAt) OverviewCameraPose()
        {
            float depth = OuterNorth - OuterSouth;
            return (
                new Vector3(0f, depth, OuterSouth - depth * 0.7f),
                new Vector3(0f, 1.2f, (OuterSouth + OuterNorth) * 0.5f));
        }

        internal static Transform[] CreateBedroomSlots(Transform houseRoot)
        {
            // 스케일은 절대 주지 않는다. 프리셋 가구는 이 기준점의 로컬 좌표로 옮겨지므로,
            // 스케일이 붙으면 가구가 커지거나 배치가 통째로 밀려난다.
            Transform group = CreateGroup("RoomSlots", houseRoot);

            Transform first = CreateGroup("BedroomSlot_01", group);
            first.position = new Vector3((OuterWest + Bedroom01East) * 0.5f, 0f, NorthRoomCenterZ);

            Transform second = CreateGroup("BedroomSlot_02", group);
            second.position = new Vector3((Bedroom02West + OuterEast) * 0.5f, 0f, NorthRoomCenterZ);

            return new[] { first, second };
        }

        /// <summary>
        /// 침실 프리셋 A · B · C 를 집 남쪽 바깥에 나란히 만든다. 세션이 시작되면
        /// <see cref="RoomSlotAssigner"/>가 이 중 둘을 뽑아 침실 슬롯으로 옮긴다.
        ///
        /// 전시장에는 방 윤곽만 낮은 벽으로 그려 둔다 — 위에서 내려다보며 배치를 고치기 좋고,
        /// 실제로 옮겨 가는 건 가구뿐이라 벽은 높을 이유가 없다.
        /// </summary>
        internal static Transform CreateBedroomPresets(Palette palette, PhysicsAssets physics)
        {
            if (palette == null)
                throw new ArgumentNullException(nameof(palette));
            if (physics == null)
                throw new ArgumentNullException(nameof(physics));

            var rootObject = new GameObject("Room_Presets");
            Transform root = rootObject.transform;

            CreateBedroomPreset("A", new Vector3(-PresetPitch, 0f, PresetRowZ), root, palette, physics);
            CreateBedroomPreset("B", new Vector3(0f, 0f, PresetRowZ), root, palette, physics);
            CreateBedroomPreset("C", new Vector3(PresetPitch, 0f, PresetRowZ), root, palette, physics);
            return root;
        }

        private static void CreateBedroomPreset(
            string id,
            Vector3 center,
            Transform parent,
            Palette palette,
            PhysicsAssets physics)
        {
            Transform room = CreateGroup($"BedroomPreset_{id}", parent);
            room.position = center;
            CreatePresetShell(room, palette);

            Transform items = CreateGroup("Items", room);
            items.position = center;

            var collected = new List<NetworkObject>();

            void Add(Transform item, FurnitureWeightClass weightClass, float supportHeight = 0f)
            {
                MakePhysical(item, physics, weightClass, supportHeight);
                collected.Add(item.GetComponent<NetworkObject>());
            }

            switch (id)
            {
                case "A":
                    FillBedroomPresetA(items, center, palette, Add);
                    break;
                case "B":
                    FillBedroomPresetB(items, center, palette, Add);
                    break;
                case "C":
                    FillBedroomPresetC(items, center, palette, Add);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(id), id, "알 수 없는 침실 프리셋입니다.");
            }

            RoomPreset preset = room.gameObject.AddComponent<RoomPreset>();
            PrototypeSceneSetup.SetString(preset, "_presetId", id);
            PrototypeSceneSetup.SetObjectReference(preset, "_anchor", room);
            PrototypeSceneSetup.SetObjectArray(preset, "_furniture", collected.ToArray());
        }

        private static void CreatePresetShell(Transform room, Palette palette)
        {
            const float thickness = WallThickness;
            Vector3 center = room.position;

            Transform ground = CreateGroup(GroundGroupName, room);
            CreateCube("Floor", center + new Vector3(0f, -0.1f, 0f),
                new Vector3(SlotWidth + thickness * 2f, 0.2f, SlotDepth + thickness * 2f),
                palette.WoodFloor, ground);

            Transform outline = CreateGroup("Outline", room);
            float halfX = SlotHalfX + thickness * 0.5f;
            float halfZ = SlotHalfZ + thickness * 0.5f;
            float height = PresetWallHeight * 0.5f;
            Vector3 alongX = new(SlotWidth + thickness * 2f, PresetWallHeight, thickness);
            Vector3 alongZ = new(thickness, PresetWallHeight, SlotDepth);

            CreateCube("North", center + new Vector3(0f, height, halfZ), alongX, palette.Wall, outline);
            CreateCube("South", center + new Vector3(0f, height, -halfZ), alongX, palette.Wall, outline);
            CreateCube("West", center + new Vector3(-halfX, height, 0f), alongZ, palette.Wall, outline);
            CreateCube("East", center + new Vector3(halfX, height, 0f), alongZ, palette.Wall, outline);

            // 문짝이 쓸고 지나가는 구역을 바닥에 표시해 둔다. 여기에 가구를 놓으면 생성 검증이 막는다.
            // 두 침실의 문 위치가 달라 서쪽·동쪽 두 군데이고, 그 사이 띠는 어느 문도 지나가지 않는다.
            float sweepDepth = PresetSouthKeepOut + SlotHalfZ;
            float sweepZ = (-SlotHalfZ + PresetSouthKeepOut) * 0.5f;
            CreateCube("DoorSweep_KeepOut_West",
                center + new Vector3((-SlotHalfX + PresetDoorFreeWest) * 0.5f, 0.005f, sweepZ),
                new Vector3(PresetDoorFreeWest + SlotHalfX, 0.01f, sweepDepth),
                palette.Trim, outline, false);
            CreateCube("DoorSweep_KeepOut_East",
                center + new Vector3((PresetDoorFreeEast + SlotHalfX) * 0.5f, 0.005f, sweepZ),
                new Vector3(SlotHalfX - PresetDoorFreeEast, 0.01f, sweepDepth),
                palette.Trim, outline, false);
        }

        // ── 프리셋 배치는 09-map-generation 의 Bedroom_A/B/C 도면을 그대로 옮긴 것이다.
        //    가구 종류·치수·어느 벽에 붙는지·바라보는 방향까지 도면을 따른다.
        //    좌표는 방 안쪽 면에서 역산하므로 맵 배율을 바꿔도 벽에 붙는 가구는 벽을 따라간다.
        //
        //    도면과 다를 수밖에 없는 것: 방이 도면(3.8 x 3.6m)의 배율배이므로 가구 사이가 벌어진다.
        //    벽을 따라가는 자리(위/아래, 좌/우)는 도면의 비율을 지키고, 벽에 붙는 면만 벽에 맞춘다.

        private static float RoomWestFace => -SlotHalfX;
        private static float RoomEastFace => SlotHalfX;
        private static float RoomNorthFace => SlotHalfZ;

        /// <summary>침대 머리맡이 북쪽 벽에 붙는 z. 헤드보드가 프레임보다 0.01 더 튀어나온다.</summary>
        private static float BedHeadZ => RoomNorthFace - 1.01f - WallGap;

        /// <summary>
        /// Bedroom_A — 옷장 · 싱글침대 · 협탁 · 책상 · 의자.
        /// 도면: 옷장은 서쪽 벽 위쪽, 책상은 서쪽 벽 아래쪽에 의자를 방 안쪽으로 두고,
        /// 침대는 동쪽 벽에 머리를 북쪽으로, 협탁은 북쪽 벽의 침대 머리맡 옆.
        /// </summary>
        private static void FillBedroomPresetA(
            Transform items,
            Vector3 origin,
            Palette palette,
            AddFurniture add)
        {
            Vector3 At(float x, float z) => origin + new Vector3(x, 0f, z);

            float bedX = RoomEastFace - 0.54f - WallGap;
            add(CreateBed("SingleBed_1.0x2.0", At(bedX, BedHeadZ), 1f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);

            float nightstandX = bedX - 0.54f - ItemGap - 0.225f;
            float nightstandZ = RoomNorthFace - WallGap - 0.2f;
            add(CreateTable("Nightstand_0.45x0.4", At(nightstandX, nightstandZ),
                    new Vector3(0.45f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);

            // 옷장: 서쪽 벽 위쪽. 문이 방 안쪽(동쪽)을 보도록 돌린다.
            float westCabinetX = RoomWestFace + 0.3f + WallGap;
            add(CreateCabinet("Wardrobe_1.2x0.6", At(westCabinetX, RoomNorthFace - 0.9f),
                    new Vector3(1.2f, 1.85f, 0.6f), palette, items, -90f),
                FurnitureWeightClass.Heavy);

            // 책상: 서쪽 벽 아래쪽. 의자는 책상 동쪽에서 서쪽(책상)을 본다.
            const float deskZ = -1.1f;
            add(CreateTable("Desk_1.2x0.6", At(westCabinetX, deskZ),
                    new Vector3(1.2f, 0.6f), 90f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            add(CreateChair("DeskChair_0.55x0.55",
                    At(westCabinetX + 0.3f + ItemGap + 0.275f, deskZ), 90f, palette, items, 0.55f),
                FurnitureWeightClass.Light);

            // 서랍장(옷장과 책상 사이)과 책장: 도면에는 없지만 방이 도면의 배율배라 벽이 비어
            // 남는다. 09-map-generation "도면과 달라진 점"에서 채우기로 한 여백이다.
            add(CreateCabinet("Dresser_1.2x0.5", At(RoomWestFace + 0.25f + WallGap, 0.7f),
                    new Vector3(1.2f, 0.82f, 0.5f), palette, items, -90f),
                FurnitureWeightClass.Heavy);
            add(CreateShelf("Bookshelf_0.9x0.3", At(-1.6f, RoomNorthFace - WallGap - 0.15f),
                    new Vector3(0.9f, 1.6f, 0.3f), palette, items),
                FurnitureWeightClass.Heavy);

            // 바닥 Spawn Point 자리(상자·쓰레기통·빨래바구니)와 책상 위 Spawn Point 자리(컵·책).
            // 상자만 남쪽 벽까지 내려가는데, 두 문 사이 가운데 띠라 문짝이 지나가지 않는다.
            add(CreateProp("Crate_0.6", At(1f, -3f),
                    new Vector3(0.6f, 0.6f, 0.6f), palette.Wood, items),
                FurnitureWeightClass.Light);
            add(CreateProp("TrashBin_0.3", At(-3.5f, -2.15f),
                    new Vector3(0.3f, 0.4f, 0.3f), palette.Metal, items),
                FurnitureWeightClass.Light);
            add(CreateProp("LaundryBasket_0.5x0.4", At(-2.9f, -2.2f),
                    new Vector3(0.5f, 0.45f, 0.4f), palette.Fabric, items),
                FurnitureWeightClass.Light);
            add(CreateProp("Book_Stack_0.22", At(westCabinetX, deskZ - 0.25f),
                    new Vector3(0.22f, 0.09f, 0.3f), palette.Trim, items),
                FurnitureWeightClass.Light, TableTopSurface(0.72f));
            add(CreateProp("Cup_0.12", At(nightstandX, nightstandZ),
                    new Vector3(0.12f, 0.14f, 0.12f), palette.Ceramic, items),
                FurnitureWeightClass.Light, TableTopSurface(0.5f));
        }

        /// <summary>
        /// Bedroom_B — 서랍장 · 옷장 · 화장대 · 스툴 · 협탁 2 · 더블침대.
        /// 도면: 서쪽 벽에 서랍장(위)과 옷장(아래), 북쪽 벽에 화장대와 스툴, 그 오른쪽에
        /// 머리맡 협탁, 동쪽 벽에 더블침대(머리 북쪽)와 발치 협탁.
        /// </summary>
        private static void FillBedroomPresetB(
            Transform items,
            Vector3 origin,
            Palette palette,
            AddFurniture add)
        {
            Vector3 At(float x, float z) => origin + new Vector3(x, 0f, z);

            float bedX = RoomEastFace - 0.84f - WallGap;
            add(CreateBed("DoubleBed_1.6x2.0", At(bedX, BedHeadZ), 1.6f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);

            float headTableX = bedX - 0.84f - ItemGap - 0.25f;
            float headTableZ = RoomNorthFace - WallGap - 0.2f;
            float footTableX = RoomEastFace - WallGap - 0.25f;
            float footTableZ = BedHeadZ - 1f - ItemGap - 0.2f;
            add(CreateTable("BedsideTable_Head_0.5x0.4", At(headTableX, headTableZ),
                    new Vector3(0.5f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);
            add(CreateTable("BedsideTable_Foot_0.5x0.4", At(footTableX, footTableZ),
                    new Vector3(0.5f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);

            // 화장대 + 스툴: 북쪽 벽 왼쪽. 스툴은 화장대를 보고 앉는다.
            const float vanityX = -1.2f;
            float vanityZ = RoomNorthFace - WallGap - 0.25f;
            add(CreateTable("Vanity_1.0x0.5", At(vanityX, vanityZ),
                    new Vector3(1f, 0.5f), 0f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            add(CreateChair("VanityStool_0.45x0.45",
                    At(vanityX, vanityZ - 0.25f - ItemGap - 0.225f), 180f, palette, items, 0.45f),
                FurnitureWeightClass.Light);

            // 서랍장(위) + 옷장(아래): 서쪽 벽. 둘 다 문이 방 안쪽을 본다.
            add(CreateCabinet("Dresser_1.2x0.5",
                    At(RoomWestFace + 0.25f + WallGap, RoomNorthFace - 0.9f),
                    new Vector3(1.2f, 0.82f, 0.5f), palette, items, -90f),
                FurnitureWeightClass.Heavy);
            add(CreateCabinet("Wardrobe_1.5x0.6", At(RoomWestFace + 0.3f + WallGap, 0.6f),
                    new Vector3(1.5f, 1.85f, 0.6f), palette, items, -90f),
                FurnitureWeightClass.Heavy);

            // 책장(동쪽 벽)과 수납함(서쪽 벽 아래): 넓어진 방의 남쪽 절반을 채운다.
            add(CreateShelf("Bookshelf_0.9x0.3", At(RoomEastFace - WallGap - 0.169f, -1f),
                    new Vector3(0.3f, 1.6f, 0.9f), palette, items),
                FurnitureWeightClass.Heavy);
            add(CreateProp("StorageChest_0.9x0.45", At(RoomWestFace + WallGap + 0.225f, -1.5f),
                    new Vector3(0.45f, 0.5f, 0.9f), palette.Wood, items),
                FurnitureWeightClass.Light);

            // 바닥·화장대 위·협탁 위 Spawn Point 자리.
            add(CreateProp("Crate_0.6", At(0.5f, -3f),
                    new Vector3(0.6f, 0.6f, 0.6f), palette.Wood, items),
                FurnitureWeightClass.Light);
            add(CreateProp("LaundryBasket_0.5x0.4", At(-2.2f, -2.2f),
                    new Vector3(0.5f, 0.45f, 0.4f), palette.Fabric, items),
                FurnitureWeightClass.Light);
            add(CreateProp("TrashBin_0.3", At(-0.45f, 3.3f),
                    new Vector3(0.3f, 0.4f, 0.3f), palette.Metal, items),
                FurnitureWeightClass.Light);
            add(CreateProp("CosmeticBox_0.25", At(vanityX - 0.3f, vanityZ + 0.06f),
                    new Vector3(0.25f, 0.2f, 0.18f), palette.Ceramic, items),
                FurnitureWeightClass.Light, TableTopSurface(0.72f));
            add(CreateProp("Cup_0.12", At(headTableX, headTableZ),
                    new Vector3(0.12f, 0.14f, 0.12f), palette.Ceramic, items),
                FurnitureWeightClass.Light, TableTopSurface(0.5f));
            add(CreateProp("Book_Stack_0.22", At(footTableX, footTableZ),
                    new Vector3(0.22f, 0.09f, 0.3f), palette.Trim, items),
                FurnitureWeightClass.Light, TableTopSurface(0.5f));
        }

        /// <summary>
        /// Bedroom_C — 싱글침대 · 장난감 수납함 · 책상 · 의자 · 책장 · 서랍장.
        /// 도면: 서쪽 벽에 침대(머리 북쪽)와 그 아래 수납함, 북동쪽 모서리에 책상과 의자,
        /// 동쪽 벽에 책장(가운데)과 서랍장(아래).
        /// </summary>
        private static void FillBedroomPresetC(
            Transform items,
            Vector3 origin,
            Palette palette,
            AddFurniture add)
        {
            Vector3 At(float x, float z) => origin + new Vector3(x, 0f, z);

            add(CreateBed("SingleBed_1.1x2.0", At(RoomWestFace + 0.59f + WallGap, BedHeadZ),
                    1.1f, 2f, 0f, palette, items),
                FurnitureWeightClass.Heavy);

            // 장난감 수납함: 서쪽 벽, 침대 발치 아래.
            add(CreateCube("ToyChest_0.9x0.45",
                    At(RoomWestFace + 0.45f + WallGap, 1f) + new Vector3(0f, 0.25f, 0f),
                    new Vector3(0.9f, 0.5f, 0.45f), palette.Wood, items).transform,
                FurnitureWeightClass.Light);

            // 책상 + 의자: 북동쪽 모서리. 의자는 책상을 보고 앉는다.
            float deskX = RoomEastFace - WallGap - 0.5f;
            float deskZ = RoomNorthFace - WallGap - 0.25f;
            add(CreateTable("Desk_1.0x0.5", At(deskX, deskZ),
                    new Vector3(1f, 0.5f), 0f, palette.Wood, items),
                FurnitureWeightClass.Heavy);
            add(CreateChair("Chair_0.5x0.5", At(deskX, deskZ - 0.25f - ItemGap - 0.25f),
                    180f, palette, items),
                FurnitureWeightClass.Light);

            // 책장(가운데) + 서랍장(아래): 동쪽 벽. 책장은 기둥이 선반보다 0.019 더 튀어나온다.
            add(CreateShelf("Bookshelf_0.9x0.3", At(RoomEastFace - WallGap - 0.169f, 0.9f),
                    new Vector3(0.3f, 1.6f, 0.9f), palette, items),
                FurnitureWeightClass.Heavy);
            add(CreateCabinet("Dresser_0.8x0.45", At(RoomEastFace - WallGap - 0.225f, -1f),
                    new Vector3(0.8f, 0.82f, 0.45f), palette, items, 90f),
                FurnitureWeightClass.Heavy);

            // 옷장(북쪽 벽)과 협탁(침대 머리맡): 넓어진 방에서 비는 북쪽 벽을 채운다.
            // 옷장은 벽 가운데 창(폭 1.45m)을 가리지 않도록 책상 쪽으로 붙인다.
            add(CreateCabinet("Wardrobe_1.2x0.6", At(1.5f, RoomNorthFace - WallGap - 0.3f),
                    new Vector3(1.2f, 1.85f, 0.6f), palette, items),
                FurnitureWeightClass.Heavy);
            float nightstandX = -2.25f;
            float nightstandZ = RoomNorthFace - WallGap - 0.2f;
            add(CreateTable("Nightstand_0.45x0.4", At(nightstandX, nightstandZ),
                    new Vector3(0.45f, 0.4f), 0f, palette.Wood, items, 0.5f),
                FurnitureWeightClass.Light);

            // 바닥·책상 위 Spawn Point 자리. 상자는 두 문 사이 가운데 띠에 둔다.
            add(CreateProp("Crate_0.45", At(1.5f, -3.1f),
                    new Vector3(0.45f, 0.44f, 0.45f), palette.Wood, items),
                FurnitureWeightClass.Light);
            add(CreateProp("LaundryBasket_0.5x0.4", At(-2.5f, -2.2f),
                    new Vector3(0.5f, 0.45f, 0.4f), palette.Fabric, items),
                FurnitureWeightClass.Light);
            add(CreateProp("TrashBin_0.3", At(2.55f, 2.6f),
                    new Vector3(0.3f, 0.4f, 0.3f), palette.Metal, items),
                FurnitureWeightClass.Light);
            add(CreateProp("Book_Stack_0.22", At(deskX - 0.29f, deskZ + 0.01f),
                    new Vector3(0.22f, 0.09f, 0.3f), palette.Trim, items),
                FurnitureWeightClass.Light, TableTopSurface(0.72f));
            add(CreateProp("Cup_0.12", At(deskX + 0.26f, deskZ - 0.09f),
                    new Vector3(0.12f, 0.14f, 0.12f), palette.Ceramic, items),
                FurnitureWeightClass.Light, TableTopSurface(0.72f));
        }

        /// <summary>
        /// 생성 직후 실제 Collider를 대상으로 치수, 스폰 겹침, 방 사이 이동 가능 여부를 검사한다.
        /// CharacterController 반경 0.35m보다 1cm 작은 캡슐로 그리드 탐색해 부동소수점 접촉 오차만 허용한다.
        /// </summary>
        internal static void ValidateLayout(
            Transform houseRoot,
            Transform libraryRoot,
            Transform presetsRoot,
            Transform[] slots,
            Transform[] playerSpawns)
        {
            if (houseRoot == null)
                throw new ArgumentNullException(nameof(houseRoot));
            if (libraryRoot == null)
                throw new ArgumentNullException(nameof(libraryRoot));
            if (presetsRoot == null)
                throw new ArgumentNullException(nameof(presetsRoot));
            if (slots == null || slots.Length == 0)
                throw new ArgumentException("방 슬롯이 없습니다.", nameof(slots));

            DoorInteractable[] doors = houseRoot.GetComponentsInChildren<DoorInteractable>(true);
            var doorRotations = new Quaternion[doors.Length];
            for (int i = 0; i < doors.Length; i++)
                doorRotations[i] = doors[i].transform.localRotation;

            OpenDoorsForValidation(doors);

            FurnitureGrabTarget[] furniture;
            RoomPreset[] presets;
            try
            {
                Physics.SyncTransforms();
                ValidateHouseDimensions(houseRoot);
                ValidateBedroomSlots(houseRoot, slots);
                ValidateHouseHasNoFurniture(houseRoot);
                ValidateFurnitureDimensions(libraryRoot);
                furniture = ValidatePhysicsFurniture(libraryRoot);
                ValidatePhysicsFurniture(presetsRoot);
                presets = ValidateRoomPresets(houseRoot, presetsRoot, slots);
                ValidatePlayerSpawns(houseRoot, playerSpawns);
                ValidateDoorPortals(houseRoot);
                ValidateRoomReachability(houseRoot, playerSpawns[0].position);
            }
            finally
            {
                for (int i = 0; i < doors.Length; i++)
                    doors[i].transform.localRotation = doorRotations[i];
                Physics.SyncTransforms();
            }

            Debug.Log(
                "[HousePrototypeBuilder] House_01 검증 완료: " +
                $"평면 배율 x{MapScale:0.##} / 외곽 {OuterEast - OuterWest:0.#}x{OuterNorth - OuterSouth:0.#}m / " +
                $"벽 높이 {WallHeight:0.#}m·침실 문 {BedroomDoorWidth:0.0}m(배율 없음) / " +
                $"침실 슬롯 {slots.Length}개 {SlotWidth:0.0}x{SlotDepth:0.0}m / " +
                $"집 안 물리 가구 0개 / 스폰 겹침 / 문 개구부 및 전 방 이동 가능 / " +
                $"여닫이 문 {doors.Length}개 / Furniture_Library 가구 {furniture.Length}종 / " +
                $"방 프리셋 {presets.Length}종 × 슬롯 {slots.Length}개 전 조합 배치 가능");
        }

        /// <summary>
        /// 가구를 미리 깔아 둔 집(도면 배율 비교용)을 검사한다. House_01 과 반대로
        /// "방이 비어 있는가"가 아니라 <b>깔아 둔 가구가 성립하는가</b>를 본다.
        ///
        /// (1) 배선·겹침 (2) 문짝이 도는 동안 가구를 쓸지 않는지 (3) 문 개구부와 방 한가운데를
        /// 가구가 막고 있지 않은지. 벽·붙박이는 이 집에서도 도면 그대로라 통행 판정은
        /// <b>가구 레이어만</b> 본다 — 손대지 않은 붙박이 때문에 생성이 막히면 안 된다.
        /// </summary>
        internal static void ValidateFurnishedHouse(Transform houseRoot, float mapScale)
        {
            if (houseRoot == null)
                throw new ArgumentNullException(nameof(houseRoot));

            float previousScale = _activeMapScale;
            _activeMapScale = mapScale;

            try
            {
                Physics.SyncTransforms();
                FurnitureGrabTarget[] furniture = ValidatePhysicsFurniture(houseRoot);
                ValidateFurnitureDoorSwing(houseRoot);
                ValidateFurnitureClearsPaths(houseRoot);

                Debug.Log(
                    $"[HousePrototypeBuilder] {houseRoot.name} 검증 완료: " +
                    $"평면 배율 x{mapScale:0.##} / 방에 깔아 둔 물리 가구 {furniture.Length}개 / " +
                    "가구 겹침 · 문짝 동선 · 문 개구부와 방 한가운데 통행 확인");
            }
            finally
            {
                _activeMapScale = previousScale;
            }
        }

        /// <summary>
        /// 집에 깔아 둔 가구를 문짝이 쓸고 지나가지 않는지 본다. 문짝은 키네마틱이라
        /// 가구를 밀어내지 못하고 그대로 뚫거나 튕겨 날린다.
        /// </summary>
        private static void ValidateFurnitureDoorSwing(Transform houseRoot)
        {
            int furnitureMask = 1 << LayerMask.NameToLayer(GameLayers.FurnitureName);

            foreach (DoorInteractable door in houseRoot.GetComponentsInChildren<DoorInteractable>(true))
            {
                var serialized = new SerializedObject(door);
                float openYaw = serialized.FindProperty("_openYaw").floatValue;
                float closedYaw = serialized.FindProperty("_closedYaw").floatValue;
                Quaternion restore = door.transform.localRotation;

                try
                {
                    for (int step = 0; step <= DoorSwingSamples; step++)
                    {
                        float yaw = Mathf.Lerp(closedYaw, openYaw, (float)step / DoorSwingSamples);
                        door.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Physics.SyncTransforms();

                        Collider hit = FindDoorLeafOverlap(door, houseRoot, furnitureMask);
                        if (hit == null)
                            continue;

                        throw new InvalidOperationException(
                            $"{houseRoot.name} 의 '{door.name}' 문짝이 " +
                            $"'{hit.transform.parent?.name}/{hit.name}' 을(를) 쓸고 지나갑니다.");
                    }
                }
                finally
                {
                    door.transform.localRotation = restore;
                    Physics.SyncTransforms();
                }
            }
        }

        /// <summary>
        /// 문 개구부와 방 한가운데를 가구가 막고 있지 않은지 플레이어 캡슐로 확인한다.
        /// 방마다 사람이 설 자리가 남아 있어야 배치를 눈으로 확인하러 들어갈 수 있다.
        /// </summary>
        private static void ValidateFurnitureClearsPaths(Transform houseRoot)
        {
            int furnitureMask = 1 << LayerMask.NameToLayer(GameLayers.FurnitureName);
            Vector3 origin = houseRoot.position;
            float serviceCenterX = (ServiceWest + OuterEast) * 0.5f;

            (string Name, Vector3 Position)[] checkpoints =
            {
                ("현관문", new Vector3((FrontDoorWest + FrontDoorEast) * 0.5f, 0f, OuterSouth)),
                ("Bedroom01 문", new Vector3((Bedroom01DoorWest + Bedroom01DoorEast) * 0.5f, 0f, NorthRoomSouth)),
                ("Kitchen 이동문", new Vector3(KitchenCenter, 0f, NorthRoomSouth)),
                ("Bedroom02 문", new Vector3((Bedroom02DoorWest + Bedroom02DoorEast) * 0.5f, 0f, NorthRoomSouth)),
                ("Bathroom 문", new Vector3(ServiceWest, 0f, (BathroomDoorSouth + BathroomDoorNorth) * 0.5f)),
                ("Storage 문", new Vector3(ServiceWest, 0f, (StorageDoorSouth + StorageDoorNorth) * 0.5f)),
                ("LivingRoom 한가운데", new Vector3((LivingWest + ServiceWest) * 0.5f, 0f, (OuterSouth + NorthRoomSouth) * 0.5f)),
                ("Bedroom01 한가운데", new Vector3((OuterWest + Bedroom01East) * 0.5f, 0f, NorthRoomCenterZ)),
                ("Kitchen 한가운데", new Vector3(KitchenCenter, 0f, NorthRoomCenterZ)),
                ("Bedroom02 한가운데", new Vector3((Bedroom02West + OuterEast) * 0.5f, 0f, NorthRoomCenterZ)),
                ("Bathroom 한가운데", new Vector3(serviceCenterX, 0f, (BathroomSouth + NorthRoomSouth) * 0.5f)),
                ("Storage 한가운데", new Vector3(serviceCenterX, 0f, (OuterSouth + BathroomSouth) * 0.5f)),
            };

            foreach ((string name, Vector3 position) in checkpoints)
            {
                // 통과 반경은 플레이어 캡슐 기준이라 맵 배율과 무관하다.
                Collider blocker = FindCapsuleBlocker(houseRoot, origin + position, 0.34f, furnitureMask);
                if (blocker != null)
                {
                    throw new InvalidOperationException(
                        $"{houseRoot.name} 의 {name} 자리를 " +
                        $"'{blocker.transform.parent?.name}/{blocker.name}' 가구가 막고 있습니다.");
                }
            }
        }

        /// <summary>
        /// 물리 가구가 (1) 배선이 빠짐없고 (2) 시작부터 다른 콜라이더에 끼어 있지 않은지 본다.
        ///
        /// 겹친 채로 물리를 켜면 PhysX 가 서로 밀어내면서 세션 시작과 동시에 가구가 튀어나간다.
        /// 눈으로는 "왜 가구가 벽을 뚫고 날아가지?"로 보이고 원인을 찾기 어렵다.
        /// </summary>
        private static FurnitureGrabTarget[] ValidatePhysicsFurniture(Transform furnitureRoot)
        {
            FurnitureGrabTarget[] furniture = furnitureRoot.GetComponentsInChildren<FurnitureGrabTarget>(true);
            if (furniture.Length == 0)
                throw new InvalidOperationException("물리 가구가 하나도 만들어지지 않았습니다.");

            int furnitureLayer = LayerMask.NameToLayer(GameLayers.FurnitureName);

            foreach (FurnitureGrabTarget target in furniture)
            {
                Transform root = target.transform;

                if (root.GetComponent<NetworkObject>() == null
                    || root.GetComponent<Rigidbody>() == null
                    || root.GetComponent<NetworkTransform>() == null
                    || root.GetComponent<FurnitureNetworkPhysics>() == null
                    || root.GetComponent<FurnitureHoverMotor>() == null
                    || root.GetComponent<FurnitureLauncher>() == null
                    || root.GetComponent<FurnitureOutline>() == null)
                {
                    throw new MissingComponentException($"'{root.name}' 의 가구 컴포넌트가 빠졌습니다.");
                }

                foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                {
                    if (part.gameObject.layer != furnitureLayer)
                    {
                        throw new InvalidOperationException(
                            $"'{root.name}/{part.name}' 이(가) {GameLayers.FurnitureName} 레이어가 아닙니다. " +
                            "조준 레이캐스트에 걸리지 않습니다.");
                    }

                    if (GameObjectUtility.GetStaticEditorFlags(part.gameObject) != 0)
                    {
                        throw new InvalidOperationException(
                            $"'{root.name}/{part.name}' 이(가) 정적으로 표시돼 있습니다. " +
                            "정적 배칭된 메시는 던져도 그림이 제자리에 남습니다.");
                    }
                }

                ValidateFurnitureClearance(furnitureRoot, root);
            }

            return furniture;
        }

        private static void ValidateFurnitureClearance(Transform furnitureRoot, Transform root)
        {
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (collider is not BoxCollider box)
                    continue;

                Transform part = box.transform;
                Vector3 center = part.TransformPoint(box.center);

                // 딱 맞닿은 배치(바닥 위, 콘솔 위)는 정상이므로 1cm 줄여 접촉 오차만 허용한다.
                Vector3 halfExtents = Vector3.Scale(box.size, part.lossyScale) * 0.5f;
                halfExtents = Vector3.Max(halfExtents - Vector3.one * 0.01f, Vector3.one * 0.001f);

                Collider[] overlaps = Physics.OverlapBox(
                    center,
                    halfExtents,
                    part.rotation,
                    Physics.AllLayers,
                    QueryTriggerInteraction.Ignore);

                foreach (Collider overlap in overlaps)
                {
                    if (overlap.transform.IsChildOf(root)
                        || !overlap.transform.IsChildOf(furnitureRoot)
                        || IsFloorCollider(overlap))
                    {
                        continue;
                    }

                    throw new InvalidOperationException(
                        $"'{root.name}/{collider.name}' 이(가) '{overlap.name}' 와 겹칩니다. " +
                        "시작하자마자 물리가 서로 밀어내므로 배치 좌표를 띄우세요.");
                }
            }
        }

        /// <summary>
        /// 문은 여닫히므로 "닫힌 문에 막혔다"는 건 통로 오류가 아니다. 개구부 폭과 방 연결성은
        /// 언제나 문이 열린 상태로 검사하고, 끝나면 원래 각도로 되돌린다 — 초기 상태를 닫힘으로
        /// 바꿔도 이 검증이 엉뚱하게 실패하지 않는다.
        /// </summary>
        private static void OpenDoorsForValidation(DoorInteractable[] doors)
        {
            foreach (DoorInteractable door in doors)
            {
                var serialized = new SerializedObject(door);
                float openYaw = serialized.FindProperty("_openYaw").floatValue;
                door.transform.localRotation = Quaternion.Euler(0f, openYaw, 0f);
            }
        }

        /// <summary>
        /// 배율은 그룹 트랜스폼에 구워져 있으므로 <see cref="Transform.lossyScale"/>로 잰다.
        /// localScale 로 재면 배율을 안 준 것처럼 보여서, 굽는 걸 빠뜨려도 검사를 통과한다.
        /// </summary>
        private static void ValidateHouseDimensions(Transform houseRoot)
        {
            Transform northWall = RequireDescendant(houseRoot, "North_Outer");
            Transform southWall = RequireDescendant(houseRoot, "South_Outer_West");
            RequireApproximately(northWall.lossyScale.x, OuterEast - OuterWest, "House 가로");
            RequireApproximately(
                northWall.position.z - southWall.position.z,
                OuterNorth - OuterSouth,
                "House 세로");

            // 높이와 문 폭은 배율을 받지 않는다 — 여기서 배율이 새면 문이 사람보다 커진다.
            RequireApproximately(northWall.lossyScale.y, WallHeight, "벽 높이");
            Transform bedroomDoor = RequireDescendant(
                RequireDescendant(houseRoot, "Bedroom01_Door_1.2m"), "DoorLeaf");
            RequireApproximately(bedroomDoor.lossyScale.x, BedroomDoorWidth, "침실 문 폭");
            RequireApproximately(bedroomDoor.lossyScale.y, 2.04f, "침실 문 높이");
        }

        /// <summary>
        /// 프리셋을 슬롯마다 실제로 놓아 보고 벽·가구 겹침과 문 동선을 검사한다.
        /// 뽑기는 런타임에 일어나므로 어떤 조합이 나와도 방이 성립해야 한다 —
        /// 여기서 걸러 두지 않으면 "가끔 옷장이 벽에 박힌 판"이 나온다.
        /// </summary>
        private static RoomPreset[] ValidateRoomPresets(
            Transform houseRoot,
            Transform presetsRoot,
            Transform[] slots)
        {
            RoomPreset[] presets = presetsRoot.GetComponentsInChildren<RoomPreset>(true);
            if (presets.Length < slots.Length)
            {
                throw new InvalidOperationException(
                    $"프리셋이 {presets.Length}종뿐이라 슬롯 {slots.Length}개에 중복 없이 배정할 수 없습니다.");
            }

            foreach (RoomPreset preset in presets)
            {
                // 지금(전시 자리) 배치를 기준 포즈로 잡아야 슬롯으로 옮겼다가 되돌릴 수 있다.
                preset.CacheLocalPoses();

                foreach (Transform slot in slots)
                {
                    preset.ApplyTo(slot);
                    Physics.SyncTransforms();
                    ValidatePresetInSlot(houseRoot, preset, slot);
                }

                preset.ApplyTo(preset.Anchor);
                Physics.SyncTransforms();
            }

            return presets;
        }

        private static void ValidatePresetInSlot(Transform houseRoot, RoomPreset preset, Transform slot)
        {
            foreach (FurnitureGrabTarget target in preset.GetComponentsInChildren<FurnitureGrabTarget>(true))
            {
                foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
                {
                    if (collider is not BoxCollider box)
                        continue;

                    Transform part = box.transform;
                    Vector3 halfExtents = Vector3.Scale(box.size, part.lossyScale) * 0.5f;
                    halfExtents = Vector3.Max(halfExtents - Vector3.one * 0.01f, Vector3.one * 0.001f);

                    Collider[] overlaps = Physics.OverlapBox(
                        part.TransformPoint(box.center),
                        halfExtents,
                        part.rotation,
                        Physics.AllLayers,
                        QueryTriggerInteraction.Ignore);

                    foreach (Collider overlap in overlaps)
                    {
                        if (overlap.transform.IsChildOf(target.transform) || IsFloorCollider(overlap))
                            continue;

                        throw new InvalidOperationException(
                            $"프리셋 {preset.PresetId} 를 {slot.name} 에 놓으면 " +
                            $"'{target.name}/{collider.name}' 이(가) '{overlap.name}' 와 겹칩니다.");
                    }
                }
            }

            ValidateDoorSwing(houseRoot, preset, slot);
        }

        /// <summary>
        /// 문짝이 닫힘에서 열림까지 도는 동안 프리셋 가구를 쓸고 지나가지 않는지 본다.
        /// 문짝은 키네마틱이라 가구를 밀어내지 못하고 그대로 뚫고 지나가거나 튕겨 날린다.
        /// </summary>
        private static void ValidateDoorSwing(Transform houseRoot, RoomPreset preset, Transform slot)
        {
            int furnitureMask = 1 << LayerMask.NameToLayer(GameLayers.FurnitureName);

            foreach (DoorInteractable door in houseRoot.GetComponentsInChildren<DoorInteractable>(true))
            {
                var serialized = new SerializedObject(door);
                float openYaw = serialized.FindProperty("_openYaw").floatValue;
                float closedYaw = serialized.FindProperty("_closedYaw").floatValue;
                Quaternion restore = door.transform.localRotation;

                try
                {
                    for (int step = 0; step <= DoorSwingSamples; step++)
                    {
                        float yaw = Mathf.Lerp(closedYaw, openYaw, (float)step / DoorSwingSamples);
                        door.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
                        Physics.SyncTransforms();

                        Collider hit = FindDoorLeafOverlap(door, preset.transform, furnitureMask);
                        if (hit == null)
                            continue;

                        throw new InvalidOperationException(
                            $"프리셋 {preset.PresetId} 를 {slot.name} 에 놓으면 '{door.name}' 문짝이 " +
                            $"'{hit.transform.parent?.name}/{hit.name}' 을(를) 쓸고 지나갑니다.");
                    }
                }
                finally
                {
                    door.transform.localRotation = restore;
                    Physics.SyncTransforms();
                }
            }
        }

        /// <summary><paramref name="scope"/> 안에 있으면서 문짝 자신은 아닌 가구 콜라이더를 찾는다.</summary>
        private static Collider FindDoorLeafOverlap(DoorInteractable door, Transform scope, int furnitureMask)
        {
            foreach (BoxCollider leaf in door.GetComponentsInChildren<BoxCollider>(true))
            {
                Transform part = leaf.transform;
                Vector3 halfExtents = Vector3.Scale(leaf.size, part.lossyScale) * 0.5f;
                halfExtents = Vector3.Max(halfExtents - Vector3.one * 0.01f, Vector3.one * 0.001f);

                Collider[] overlaps = Physics.OverlapBox(
                    part.TransformPoint(leaf.center),
                    halfExtents,
                    part.rotation,
                    furnitureMask,
                    QueryTriggerInteraction.Ignore);

                foreach (Collider overlap in overlaps)
                {
                    if (overlap.transform.IsChildOf(scope)
                        && !overlap.transform.IsChildOf(door.transform))
                    {
                        return overlap;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 침실 슬롯 내부 치수를 실제로 만들어진 벽 면 사이 거리로 잰다. 상수만 비교하면
        /// 상수는 맞는데 벽을 옮기다 만 상태를 못 잡으므로 트랜스폼에서 되읽는다.
        /// 슬롯 기준점이 방 한가운데 있는지도 같이 본다 — 어긋나면 프리셋이 통째로 밀려 들어간다.
        /// </summary>
        private static void ValidateBedroomSlots(Transform houseRoot, Transform[] slots)
        {
            float westFace = FaceX(RequireDescendant(houseRoot, "West_Bedroom_Outer"), 1f);
            float bedroom01EastFace = FaceX(RequireDescendant(houseRoot, "Bedroom01_Kitchen"), -1f);
            float bedroom02WestFace = FaceX(RequireDescendant(houseRoot, "Kitchen_Bedroom02"), 1f);
            float eastFace = FaceX(RequireDescendant(houseRoot, "East_Outer"), -1f);
            float northFace = FaceZ(RequireDescendant(houseRoot, "North_Outer"), -1f);
            float bedroom01SouthFace = FaceZ(RequireDescendant(houseRoot, "Bedroom01_South_East"), 1f);
            float bedroom02SouthFace = FaceZ(RequireDescendant(houseRoot, "Bedroom02_South_East"), 1f);

            RequireApproximately(bedroom01EastFace - westFace, SlotWidth, "침실1 가로");
            RequireApproximately(northFace - bedroom01SouthFace, SlotDepth, "침실1 세로");
            RequireApproximately(eastFace - bedroom02WestFace, SlotWidth, "침실2 가로");
            RequireApproximately(northFace - bedroom02SouthFace, SlotDepth, "침실2 세로");

            (Vector3 Center, float SouthFace)[] rooms =
            {
                (new Vector3((westFace + bedroom01EastFace) * 0.5f, 0f, 0f), bedroom01SouthFace),
                (new Vector3((bedroom02WestFace + eastFace) * 0.5f, 0f, 0f), bedroom02SouthFace),
            };

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] == null)
                    throw new InvalidOperationException($"{i}번 방 슬롯이 비어 있습니다.");

                RequireApproximately(slots[i].position.x, rooms[i].Center.x, $"{slots[i].name} 가로 중심");
                RequireApproximately(
                    slots[i].position.z,
                    (rooms[i].SouthFace + northFace) * 0.5f,
                    $"{slots[i].name} 세로 중심");
                RequireApproximately(slots[i].position.y, 0f, $"{slots[i].name} 바닥 높이");

                if (Quaternion.Angle(slots[i].rotation, Quaternion.identity) > 0.01f)
                    throw new InvalidOperationException($"{slots[i].name} 이(가) 회전돼 있습니다. 프리셋의 +Z가 방의 북쪽이어야 합니다.");
            }
        }

        /// <summary>
        /// 방 안에는 가구가 없어야 한다. 배치는 <see cref="CreateFurnitureLibrary"/> 에서
        /// 복사해 넣는 방식이므로, 생성 도구가 다시 가구를 심어 놓으면 손으로 한 배치와 겹친다.
        /// </summary>
        private static void ValidateHouseHasNoFurniture(Transform houseRoot)
        {
            FurnitureGrabTarget[] inside = houseRoot.GetComponentsInChildren<FurnitureGrabTarget>(true);
            if (inside.Length > 0)
            {
                throw new InvalidOperationException(
                    $"집 안에 물리 가구가 {inside.Length}개 있습니다(예: '{inside[0].name}'). " +
                    "방 배치는 Furniture_Library 에서 복사해 넣습니다.");
            }
        }

        private static void ValidateFurnitureDimensions(Transform libraryRoot)
        {
            ValidateFootprint(libraryRoot, "SingleBed_1.0x2.0", "Frame", 1f, 2f);
            ValidateFootprint(libraryRoot, "DoubleBed_1.6x2.0", "Frame", 1.6f, 2f);
            ValidateFootprint(libraryRoot, "Wardrobe_1.2x0.6", "Body", 1.2f, 0.6f);
            ValidateFootprint(libraryRoot, "Wardrobe_1.5x0.6", "Body", 1.5f, 0.6f);
            ValidateFootprint(libraryRoot, "Dresser_1.2x0.5", "Body", 1.2f, 0.5f);
            ValidateFootprint(libraryRoot, "Desk_1.2x0.6", "Top", 1.2f, 0.6f);
            ValidateFootprint(libraryRoot, "Vanity_1.0x0.5", "Top", 1f, 0.5f);
            ValidateFootprint(libraryRoot, "Nightstand_0.45x0.4", "Top", 0.45f, 0.4f);
            ValidateFootprint(libraryRoot, "BedsideTable_0.5x0.4", "Top", 0.5f, 0.4f);
            ValidateFootprint(libraryRoot, "DeskChair_0.55x0.55", "Seat", 0.55f, 0.55f);
            ValidateFootprint(libraryRoot, "VanityStool_0.45x0.45", "Seat", 0.45f, 0.45f);
            ValidateFootprint(libraryRoot, "DiningTable_1.55x0.85", "Top", 1.55f, 0.85f);
            ValidateFootprint(libraryRoot, "Chair_0.5x0.5", "Seat", 0.5f, 0.5f);
            ValidateFootprint(libraryRoot, "Sofa_2.2x0.9", "Base", 2.2f, 0.9f);
            ValidateFootprint(libraryRoot, "CoffeeTable_1.25x0.65", "Top", 1.25f, 0.65f);
            ValidateFootprint(libraryRoot, "LivingConsole_1.6x0.45", "Body", 1.6f, 0.45f);
            ValidateFootprint(libraryRoot, "Shelving_2.6x0.55", "Shelf_1", 2.6f, 0.55f);
            ValidateFootprint(libraryRoot, "Shelving_1.65x0.45", "Shelf_1", 1.65f, 0.45f);
        }

        private static float FaceX(Transform wall, float direction)
        {
            return wall.position.x + direction * wall.lossyScale.x * 0.5f;
        }

        private static float FaceZ(Transform wall, float direction)
        {
            return wall.position.z + direction * wall.lossyScale.z * 0.5f;
        }

        private static void ValidateFootprint(
            Transform houseRoot,
            string rootName,
            string bodyName,
            float expectedX,
            float expectedZ)
        {
            Transform objectRoot = RequireDescendant(houseRoot, rootName);
            Transform body = RequireDescendant(objectRoot, bodyName);
            float actualX = body.localScale.x;
            float actualZ = body.localScale.z;
            bool direct = Approximately(actualX, expectedX) && Approximately(actualZ, expectedZ);
            bool rotated = Approximately(actualX, expectedZ) && Approximately(actualZ, expectedX);
            if (!direct && !rotated)
            {
                throw new InvalidOperationException(
                    $"{rootName} 크기 오류: {actualX:0.00}x{actualZ:0.00}m, " +
                    $"기획값 {expectedX:0.00}x{expectedZ:0.00}m");
            }
        }

        private static void ValidatePlayerSpawns(Transform houseRoot, Transform[] spawns)
        {
            if (spawns == null || spawns.Length == 0)
                throw new InvalidOperationException("플레이어 스폰 지점이 없습니다.");

            foreach (Transform spawn in spawns)
            {
                Vector3 position = spawn.position;
                Collider blocker = FindCapsuleBlocker(houseRoot, position, 0.34f);
                if (blocker != null)
                {
                    throw new InvalidOperationException(
                        $"{spawn.name}이(가) '{blocker.name}' Collider와 겹칩니다.");
                }
            }
        }

        private static void ValidateDoorPortals(Transform houseRoot)
        {
            (string Name, Vector3 Position)[] portals =
            {
                ("Bedroom01 문", new Vector3((Bedroom01DoorWest + Bedroom01DoorEast) * 0.5f, 0f, NorthRoomSouth)),
                ("Kitchen 이동문", new Vector3(KitchenCenter, 0f, NorthRoomSouth)),
                ("Bedroom02 문", new Vector3((Bedroom02DoorWest + Bedroom02DoorEast) * 0.5f, 0f, NorthRoomSouth)),
                ("Bathroom 문", new Vector3(ServiceWest, 0f, (BathroomDoorSouth + BathroomDoorNorth) * 0.5f)),
                ("Storage 문", new Vector3(ServiceWest, 0f, (StorageDoorSouth + StorageDoorNorth) * 0.5f)),
            };

            foreach ((string name, Vector3 position) in portals)
            {
                // 통과 반경은 플레이어 캡슐 기준이라 맵 배율과 무관하다.
                Collider blocker = FindCapsuleBlocker(houseRoot, position, 0.34f);
                if (blocker != null)
                {
                    throw new InvalidOperationException(
                        $"{name} 문 개구부가 '{blocker.name}' Collider에 막혀 있습니다.");
                }
            }
        }

        private static void ValidateRoomReachability(Transform houseRoot, Vector3 startPosition)
        {
            // 격자 범위는 집 외곽을 따라가지만 간격은 그대로다 — 통로 판정 기준(플레이어 캡슐)이
            // 배율을 받지 않으므로, 격자도 같은 해상도를 유지해야 좁은 틈을 놓치지 않는다.
            float minX = OuterWest + 0.2f;
            float maxX = OuterEast - 0.2f;
            float minZ = OuterSouth + 0.2f;
            float maxZ = OuterNorth - 0.2f;
            const float step = 0.2f;
            int width = Mathf.RoundToInt((maxX - minX) / step) + 1;
            int height = Mathf.RoundToInt((maxZ - minZ) / step) + 1;
            var walkable = new bool[width, height];

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector3 position = new(minX + x * step, 0f, minZ + z * step);
                    walkable[x, z] = HasFloor(houseRoot, position)
                        && FindCapsuleBlocker(houseRoot, position, 0.34f) == null;
                }
            }

            Vector2Int start = FindNearestWalkable(walkable, startPosition, minX, minZ, step);
            var visited = new bool[width, height];
            var queue = new Queue<Vector2Int>();
            visited[start.x, start.y] = true;
            queue.Enqueue(start);
            Vector2Int[] directions =
            {
                Vector2Int.left,
                Vector2Int.right,
                Vector2Int.down,
                Vector2Int.up,
            };

            while (queue.Count > 0)
            {
                Vector2Int current = queue.Dequeue();
                foreach (Vector2Int direction in directions)
                {
                    Vector2Int next = current + direction;
                    if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height)
                        continue;
                    if (visited[next.x, next.y] || !walkable[next.x, next.y])
                        continue;

                    visited[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }

            (string Name, Vector3 Position)[] roomTargets =
            {
                ("Bedroom01", new Vector3((OuterWest + Bedroom01East) * 0.5f, 0f, NorthRoomCenterZ)),
                ("Kitchen", new Vector3(KitchenCenter, 0f, NorthRoomCenterZ)),
                ("Bedroom02", new Vector3((Bedroom02West + OuterEast) * 0.5f, 0f, NorthRoomCenterZ)),
                ("Bathroom", new Vector3((ServiceWest + OuterEast) * 0.5f, 0f, (BathroomSouth + NorthRoomSouth) * 0.5f)),
                ("Storage", new Vector3((ServiceWest + OuterEast) * 0.5f, 0f, (OuterSouth + BathroomSouth) * 0.5f)),
            };

            foreach ((string name, Vector3 position) in roomTargets)
            {
                Vector2Int target = FindNearestWalkable(walkable, position, minX, minZ, step);
                if (!visited[target.x, target.y])
                    throw new InvalidOperationException($"거실 시작점에서 {name}까지 이동할 수 없습니다.");
            }
        }

        private static Vector2Int FindNearestWalkable(
            bool[,] walkable,
            Vector3 position,
            float minX,
            float minZ,
            float step)
        {
            Vector2Int best = new(-1, -1);
            float bestDistance = float.PositiveInfinity;
            for (int x = 0; x < walkable.GetLength(0); x++)
            {
                for (int z = 0; z < walkable.GetLength(1); z++)
                {
                    if (!walkable[x, z])
                        continue;

                    Vector2 delta = new(minX + x * step - position.x, minZ + z * step - position.z);
                    float distance = delta.sqrMagnitude;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = new Vector2Int(x, z);
                    }
                }
            }

            if (best.x < 0 || bestDistance > 0.36f)
                throw new InvalidOperationException($"검증 지점 {position} 주변에 이동 가능한 바닥이 없습니다.");
            return best;
        }

        private static bool HasFloor(Transform houseRoot, Vector3 position)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                position + Vector3.up * 0.8f,
                Vector3.down,
                1.2f,
                Physics.AllLayers,
                QueryTriggerInteraction.Ignore);
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.transform.IsChildOf(houseRoot)
                    && IsFloorCollider(hit.collider))
                {
                    return true;
                }
            }

            return false;
        }

        private static Collider FindCapsuleBlocker(
            Transform houseRoot,
            Vector3 position,
            float radius,
            int layerMask = ~0)
        {
            Vector3 bottom = position + Vector3.up * 0.36f;
            Vector3 top = position + Vector3.up * 1.45f;
            Collider[] overlaps = Physics.OverlapCapsule(
                bottom,
                top,
                radius,
                layerMask,
                QueryTriggerInteraction.Ignore);
            foreach (Collider collider in overlaps)
            {
                if (!collider.transform.IsChildOf(houseRoot) || IsFloorCollider(collider))
                    continue;
                return collider;
            }

            return null;
        }

        /// <summary>
        /// 바닥 위에 얹힌 것은 겹침이 아니다. 집 바닥(<see cref="FloorGroupName"/>)과
        /// 라이브러리·프리셋 받침(<see cref="GroundGroupName"/>)이 여기 해당한다.
        /// </summary>
        private static bool IsFloorCollider(Collider collider)
        {
            Transform current = collider.transform;
            while (current != null)
            {
                if (current.name == FloorGroupName || current.name == GroundGroupName)
                    return true;
                current = current.parent;
            }

            return false;
        }

        private static Transform RequireDescendant(Transform root, string name)
        {
            Transform[] descendants = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform descendant in descendants)
            {
                if (descendant.name == name)
                    return descendant;
            }

            throw new MissingReferenceException($"House_01에서 '{name}' 오브젝트를 찾지 못했습니다.");
        }

        private static void RequireApproximately(float actual, float expected, string label)
        {
            if (!Approximately(actual, expected))
                throw new InvalidOperationException($"{label} 오류: {actual:0.00}m, 기획값 {expected:0.00}m");
        }

        private static bool Approximately(float left, float right)
        {
            return Mathf.Abs(left - right) <= 0.01f;
        }

        private static Transform CreateBed(
            string name,
            Vector3 position,
            float width,
            float length,
            float rotationY,
            Palette palette,
            Transform parent)
        {
            Transform root = CreateGroup(name, parent);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
            CreateLocalCube("Frame", new Vector3(0f, 0.22f, 0f),
                new Vector3(width, 0.28f, length), palette.Wood, root);
            CreateLocalCube("Mattress", new Vector3(0f, 0.42f, -0.02f),
                new Vector3(width * 0.92f, 0.22f, length * 0.9f), palette.Bedding, root);
            CreateLocalCube("Headboard", new Vector3(0f, 0.72f, length * 0.48f),
                new Vector3(width + 0.08f, 0.95f, 0.1f), palette.Wood, root);
            CreateLocalCube("Pillow", new Vector3(0f, 0.58f, length * 0.3f),
                new Vector3(width * 0.68f, 0.16f, 0.35f), palette.Ceramic, root);
            return root;
        }

        /// <summary>
        /// 문짝 선과 손잡이는 로컬 -Z 면에 붙는다. 벽에 붙이는 가구는 그 면이 방 안쪽을
        /// 향하도록 <paramref name="rotationY"/>로 돌린다 — 크기는 회전 전 기준으로 넘긴다.
        /// </summary>
        private static Transform CreateCabinet(
            string name,
            Vector3 position,
            Vector3 scale,
            Palette palette,
            Transform parent,
            float rotationY = 0f)
        {
            Transform root = CreateGroup(name, parent);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
            CreateLocalCube("Body", new Vector3(0f, scale.y * 0.5f, 0f), scale, palette.Wood, root);
            CreateLocalCube("DoorLine", new Vector3(0f, scale.y * 0.55f, -scale.z * 0.51f),
                new Vector3(0.035f, scale.y * 0.72f, 0.025f), palette.Trim, root, false);
            CreateLocalCube("Handle", new Vector3(scale.x * 0.12f, scale.y * 0.55f, -scale.z * 0.54f),
                new Vector3(0.05f, 0.15f, 0.05f), palette.Metal, root, false);
            return root;
        }

        private static Transform CreateTable(
            string name,
            Vector3 position,
            Vector3 topSize,
            float rotationY,
            Material material,
            Transform parent,
            float height = 0.72f)
        {
            Transform root = CreateGroup(name, parent);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
            CreateLocalCube("Top", new Vector3(0f, height, 0f),
                new Vector3(topSize.x, 0.1f, topSize.y), material, root);

            float x = topSize.x * 0.4f;
            float z = topSize.y * 0.37f;
            Vector3 legScale = new(0.08f, height, 0.08f);
            CreateLocalCube("Leg_NW", new Vector3(-x, height * 0.5f, z), legScale, material, root);
            CreateLocalCube("Leg_NE", new Vector3(x, height * 0.5f, z), legScale, material, root);
            CreateLocalCube("Leg_SW", new Vector3(-x, height * 0.5f, -z), legScale, material, root);
            CreateLocalCube("Leg_SE", new Vector3(x, height * 0.5f, -z), legScale, material, root);
            return root;
        }

        private static Transform CreateChair(
            string name,
            Vector3 position,
            float rotationY,
            Palette palette,
            Transform parent,
            float size = 0.5f)
        {
            Transform root = CreateGroup(name, parent);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
            CreateLocalCube("Seat", new Vector3(0f, 0.45f, 0f),
                new Vector3(size, 0.12f, size), palette.Fabric, root);
            CreateLocalCube("Back", new Vector3(0f, 0.78f, size * 0.42f),
                new Vector3(size, 0.62f, 0.1f), palette.Wood, root);
            CreateLocalCube("FrontLeg", new Vector3(0f, 0.22f, -size * 0.33f),
                new Vector3(size * 0.72f, 0.44f, 0.08f), palette.Wood, root);
            CreateLocalCube("BackLeg", new Vector3(0f, 0.22f, size * 0.33f),
                new Vector3(size * 0.72f, 0.44f, 0.08f), palette.Wood, root);
            return root;
        }

        private static Transform CreateSofa(
            string name,
            Vector3 position,
            float rotationY,
            Palette palette,
            Transform parent)
        {
            Transform root = CreateGroup(name, parent);
            root.SetPositionAndRotation(position, Quaternion.Euler(0f, rotationY, 0f));
            CreateLocalCube("Base", new Vector3(0f, 0.35f, 0f),
                new Vector3(2.2f, 0.35f, 0.9f), palette.Fabric, root);
            CreateLocalCube("Back", new Vector3(0f, 0.75f, 0.34f),
                new Vector3(2.2f, 0.78f, 0.18f), palette.Fabric, root);
            CreateLocalCube("Arm_Left", new Vector3(-1.05f, 0.58f, 0f),
                new Vector3(0.18f, 0.62f, 0.9f), palette.Fabric, root);
            CreateLocalCube("Arm_Right", new Vector3(1.05f, 0.58f, 0f),
                new Vector3(0.18f, 0.62f, 0.9f), palette.Fabric, root);
            CreateLocalCube("Cushion_Left", new Vector3(-0.51f, 0.58f, -0.08f),
                new Vector3(0.94f, 0.12f, 0.62f), palette.Bedding, root);
            CreateLocalCube("Cushion_Right", new Vector3(0.51f, 0.58f, -0.08f),
                new Vector3(0.94f, 0.12f, 0.62f), palette.Bedding, root);
            return root;
        }

        private static Transform CreateTelevision(Vector3 position, Palette palette, Transform parent)
        {
            Transform root = CreateGroup("Television", parent);
            root.position = position;
            CreateLocalCube("Screen", new Vector3(0f, 1.25f, 0f),
                new Vector3(0.12f, 0.9f, 1.45f), palette.Metal, root);
            CreateLocalCube("Display", new Vector3(-0.07f, 1.25f, 0f),
                new Vector3(0.03f, 0.76f, 1.3f), palette.Window, root, false);
            return root;
        }

        private static void CreateCooktop(Vector3 position, Palette palette, Transform parent)
        {
            Transform root = CreateGroup("Cooktop", parent);
            root.position = position;
            CreateLocalCube("Plate", Vector3.zero, new Vector3(0.45f, 0.04f, 0.65f), palette.Metal, root, false);
            CreateLocalCylinder("Burner_01", new Vector3(-0.12f, 0.035f, 0.18f), 0.11f, 0.025f, palette.Trim, root, false);
            CreateLocalCylinder("Burner_02", new Vector3(0.12f, 0.035f, 0.18f), 0.11f, 0.025f, palette.Trim, root, false);
            CreateLocalCylinder("Burner_03", new Vector3(-0.12f, 0.035f, -0.18f), 0.11f, 0.025f, palette.Trim, root, false);
            CreateLocalCylinder("Burner_04", new Vector3(0.12f, 0.035f, -0.18f), 0.11f, 0.025f, palette.Trim, root, false);
        }

        private static void CreateToilet(Vector3 position, Palette palette, Transform parent)
        {
            Transform root = CreateGroup("Toilet", parent);
            root.position = position;
            CreateLocalCube("Tank", new Vector3(0f, 0.55f, 0.25f),
                new Vector3(0.55f, 0.7f, 0.28f), palette.Ceramic, root);
            CreateLocalCylinder("Bowl", new Vector3(0f, 0.32f, -0.08f), 0.32f, 0.32f, palette.Ceramic, root);
            CreateLocalCylinder("Seat", new Vector3(0f, 0.58f, -0.08f), 0.34f, 0.04f, palette.Trim, root, false);
        }

        private static void CreateVanity(Vector3 position, Palette palette, Transform parent)
        {
            Transform root = CreateGroup("BathroomVanity", parent);
            root.position = position;
            CreateLocalCube("Cabinet", new Vector3(0f, 0.42f, 0f),
                new Vector3(0.8f, 0.84f, 0.48f), palette.Wood, root);
            CreateLocalCube("Sink", new Vector3(0f, 0.88f, 0f),
                new Vector3(0.72f, 0.12f, 0.42f), palette.Ceramic, root);
            CreateLocalCube("Mirror", new Vector3(0f, 1.55f, 0.23f),
                new Vector3(0.68f, 0.72f, 0.04f), palette.Window, root, false);
        }

        /// <summary>
        /// Spawn Point 자리를 채우는 소품(상자·쓰레기통·컵·책 등). 프로토타입 문서대로
        /// 큐브 하나로 때운다 — 나중에 실제 메시가 생기면 이 함수만 갈아 끼우면 된다.
        /// <paramref name="floorPosition"/>은 놓을 자리의 바닥 점이고, 정확한 높이는
        /// <see cref="RestOnSupport"/>가 맞춘다.
        /// </summary>
        private static Transform CreateProp(
            string name,
            Vector3 floorPosition,
            Vector3 size,
            Material material,
            Transform parent)
        {
            return CreateCube(
                name,
                floorPosition + new Vector3(0f, size.y * 0.5f, 0f),
                size,
                material,
                parent).transform;
        }

        /// <summary><see cref="CreateTable"/>이 만든 상판의 윗면. 위에 놓을 소품의 지지면이다.</summary>
        private static float TableTopSurface(float tableHeight)
        {
            return tableHeight + 0.05f;
        }

        private static Transform CreateShelf(
            string name,
            Vector3 position,
            Vector3 scale,
            Palette palette,
            Transform parent)
        {
            Transform root = CreateGroup(name, parent);
            root.position = position;
            CreateLocalCube("LeftPost", new Vector3(-scale.x * 0.43f, scale.y * 0.5f, 0f),
                new Vector3(0.08f, scale.y, scale.z), palette.Metal, root);
            CreateLocalCube("RightPost", new Vector3(scale.x * 0.43f, scale.y * 0.5f, 0f),
                new Vector3(0.08f, scale.y, scale.z), palette.Metal, root);
            for (int i = 0; i < 4; i++)
            {
                float height = 0.12f + i * (scale.y - 0.2f) / 3f;
                CreateLocalCube($"Shelf_{i + 1}", new Vector3(0f, height, 0f),
                    new Vector3(scale.x, 0.08f, scale.z), palette.Wood, root);
            }

            return root;
        }

        /// <summary>
        /// 맵 가구를 실제로 잡고 던질 수 있는 물리 오브젝트로 만든다.
        ///
        /// 파츠 콜라이더는 그대로 두고 루트에 <see cref="Rigidbody"/>만 달아 컴파운드 콜라이더로 쓴다.
        /// 프리팹 가구와 달리 씬에 놓이므로, 세션이 시작되기 전에는 키네마틱으로 얼려 둔다 —
        /// 안 그러면 접속 전 클라이언트에서도 물리가 돌아 호스트와 다른 자리에 가구가 멈춘다.
        /// <see cref="FurnitureNetworkPhysics"/>가 스폰 시 서버에서만 푼다.
        ///
        /// 손으로 배치한 뒤에도 <see cref="RestOnSupport"/>가 지지면에 정확히 얹어 준다.
        /// 몇 cm 떠 있는 채로 물리를 켜면 세션 시작과 동시에 가구가 떨어진다.
        /// </summary>
        /// <param name="supportHeight">
        /// 가구가 앉을 지지면 높이. 바닥은 0, 책상·장식장 위 소품은 그 윗면을 넘긴다.
        /// </param>
        private static void MakePhysical(
            Transform root,
            PhysicsAssets physics,
            FurnitureWeightClass weightClass,
            float supportHeight = 0f)
        {
            SetLayerRecursively(root, LayerMask.NameToLayer(GameLayers.FurnitureName));
            ClearStaticFlags(root);
            RestOnSupport(root, supportHeight);

            Renderer[] outlineRenderers = CreateOutlineShells(root, physics.OutlineMaterial);
            FurnitureDefinition definition = weightClass == FurnitureWeightClass.Heavy
                ? physics.HeavyDefinition
                : physics.LightDefinition;

            Rigidbody body = root.gameObject.AddComponent<Rigidbody>();
            body.mass = definition.Mass;
            body.linearDamping = 0.05f;
            body.angularDamping = 0.5f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.isKinematic = true;

            root.gameObject.AddComponent<NetworkObject>();
            NetworkTransform networkTransform = root.gameObject.AddComponent<NetworkTransform>();
            PrototypeSceneSetup.SetBoolean(networkTransform, "Interpolate", true);

            FurnitureNetworkPhysics networkPhysics = root.gameObject.AddComponent<FurnitureNetworkPhysics>();
            FurnitureGrabTarget target = root.gameObject.AddComponent<FurnitureGrabTarget>();
            FurnitureHoverMotor hover = root.gameObject.AddComponent<FurnitureHoverMotor>();
            FurnitureLauncher launcher = root.gameObject.AddComponent<FurnitureLauncher>();
            FurnitureOutline outline = root.gameObject.AddComponent<FurnitureOutline>();

            PrototypeSceneSetup.SetObjectReference(networkPhysics, "_definition", definition);
            PrototypeSceneSetup.SetObjectReference(target, "_settings", physics.ThrowSettings);
            PrototypeSceneSetup.SetObjectReference(hover, "_settings", physics.ThrowSettings);
            PrototypeSceneSetup.SetObjectReference(launcher, "_settings", physics.ThrowSettings);
            PrototypeSceneSetup.SetObjectArray(outline, "_outlineRenderers", outlineRenderers);
        }

        /// <summary>
        /// 파츠마다 같은 메시의 윤곽선 셸을 하나씩 만든다. 파츠의 자식으로 두어 스케일·회전을
        /// 그대로 물려받게 한다. 조준 전에는 전부 꺼져 있다.
        /// </summary>
        private static Renderer[] CreateOutlineShells(Transform root, Material outlineMaterial)
        {
            MeshFilter[] parts = root.GetComponentsInChildren<MeshFilter>(true);
            var shells = new List<Renderer>(parts.Length);

            foreach (MeshFilter part in parts)
            {
                if (part.sharedMesh == null)
                    continue;

                var shell = new GameObject($"{part.name}_Outline")
                {
                    layer = part.gameObject.layer,
                };
                shell.transform.SetParent(part.transform, false);
                shell.AddComponent<MeshFilter>().sharedMesh = part.sharedMesh;

                MeshRenderer shellRenderer = shell.AddComponent<MeshRenderer>();
                shellRenderer.sharedMaterial = outlineMaterial;
                shellRenderer.enabled = false;
                shells.Add(shellRenderer);
            }

            return shells.ToArray();
        }

        /// <summary>
        /// 가구가 지지면에 정확히 닿도록 높이를 맞춘다. 도면 좌표는 눈으로 배치한 값이라
        /// 몇 cm씩 떠 있는 것들이 있는데, 그대로 물리를 켜면 세션 시작과 동시에 전부 떨어진다.
        /// </summary>
        private static void RestOnSupport(Transform root, float supportHeight)
        {
            Bounds bounds = MeasureColliderBounds(root);
            float offset = supportHeight - bounds.min.y;
            if (Mathf.Abs(offset) > 0.0005f)
                root.position += new Vector3(0f, offset, 0f);
        }

        /// <summary>
        /// 가구가 실제로 차지하는 월드 부피. 콜라이더 기준이라 윤곽선 셸이나 콜라이더를 뺀
        /// 장식 파츠(손잡이 등)는 들어가지 않는다. <see cref="Collider.bounds"/>는 물리 상태에서
        /// 읽으므로 방금 옮긴 트랜스폼을 먼저 반영시킨다.
        /// </summary>
        private static Bounds MeasureColliderBounds(Transform root)
        {
            Physics.SyncTransforms();

            bool hasBounds = false;
            Bounds bounds = default;
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
            {
                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                    continue;
                }

                bounds.Encapsulate(collider.bounds);
            }

            if (!hasBounds)
                throw new InvalidOperationException($"'{root.name}' 에 콜라이더가 없어 크기를 잴 수 없습니다.");

            return bounds;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            if (layer < 0)
                throw new InvalidOperationException($"'{GameLayers.FurnitureName}' 레이어가 없습니다.");

            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = layer;
        }

        private static void ClearStaticFlags(Transform root)
        {
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
        }

        private static void CreateDoor(
            string name,
            Vector3 hingePosition,
            float width,
            float openAngle,
            bool runsAlongX,
            float direction,
            Palette palette,
            Transform parent)
        {
            Transform pivot = CreateGroup(name, parent);
            pivot.SetPositionAndRotation(hingePosition, Quaternion.Euler(0f, openAngle, 0f));

            Vector3 localPosition = runsAlongX
                ? new Vector3(width * 0.5f * direction, 1.02f, 0f)
                : new Vector3(0f, 1.02f, width * 0.5f * direction);
            Vector3 scale = runsAlongX
                ? new Vector3(width, 2.04f, 0.06f)
                : new Vector3(0.06f, 2.04f, width);

            // 문짝은 런타임에 회전하므로 정적 배칭에서 빼야 한다 — 배칭된 메시는 정점이
            // 월드 좌표로 구워져서, 콜라이더만 돌고 그림은 제자리에 남는다.
            CreateLocalCube("DoorLeaf", localPosition, scale, palette.Wood, pivot, true, false);
            CreateLocalCube("Handle", localPosition + new Vector3(0f, 0f, -0.05f),
                new Vector3(0.08f, 0.08f, 0.08f), palette.Metal, pivot, false);

            ConfigureDoorInteraction(pivot, openAngle);
        }

        /// <summary>
        /// E 로 여닫는 배선. 씬에 저장된 각도가 곧 "열린 상태"이므로 닫힘 각도는 0(벽과 나란함)이다.
        ///
        /// 키네마틱 <see cref="Rigidbody"/>를 다는 이유: 콜라이더만 있는 오브젝트를 매 프레임
        /// 움직이면 PhysX 가 정적 콜라이더 트리를 다시 만든다. 문처럼 도는 물체는 키네마틱
        /// 바디로 잡아야 이동 비용이 정상 경로를 탄다.
        /// </summary>
        private static void ConfigureDoorInteraction(Transform pivot, float openAngle)
        {
            Rigidbody body = pivot.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            pivot.gameObject.AddComponent<NetworkObject>();
            DoorInteractable door = pivot.gameObject.AddComponent<DoorInteractable>();
            PrototypeSceneSetup.SetFloat(door, "_closedYaw", 0f);
            PrototypeSceneSetup.SetFloat(door, "_openYaw", openAngle);
            PrototypeSceneSetup.SetBoolean(door, "_startsOpen", true);
        }

        private static void CreateDoor(
            string name,
            Vector3 hingePosition,
            float width,
            float openAngle,
            bool runsAlongX,
            Palette palette,
            Transform parent)
        {
            CreateDoor(name, hingePosition, width, openAngle, runsAlongX, 1f, palette, parent);
        }

        private static void CreateDoorFrameX(
            string name,
            float centerX,
            float z,
            float width,
            Material material,
            Transform parent)
        {
            Transform frame = CreateGroup(name, parent);
            CreateCube("LeftPost", new Vector3(centerX - width * 0.5f, 1.05f, z),
                new Vector3(0.1f, 2.1f, 0.25f), material, frame, false);
            CreateCube("RightPost", new Vector3(centerX + width * 0.5f, 1.05f, z),
                new Vector3(0.1f, 2.1f, 0.25f), material, frame, false);
            CreateCube("Header", new Vector3(centerX, 2.08f, z),
                new Vector3(width + 0.1f, 0.12f, 0.25f), material, frame, false);
        }

        private static void CreateWindowX(
            string name,
            float centerX,
            float z,
            float width,
            Palette palette,
            Transform parent)
        {
            Transform root = CreateGroup(name, parent);
            CreateCube("Glass", new Vector3(centerX, 1.55f, z),
                new Vector3(width, 0.78f, 0.035f), palette.Window, root, false);
            CreateCube("Frame_Top", new Vector3(centerX, 1.96f, z - 0.015f),
                new Vector3(width + 0.12f, 0.07f, 0.07f), palette.Trim, root, false);
            CreateCube("Frame_Bottom", new Vector3(centerX, 1.14f, z - 0.015f),
                new Vector3(width + 0.12f, 0.07f, 0.07f), palette.Trim, root, false);
            CreateCube("Frame_Center", new Vector3(centerX, 1.55f, z - 0.015f),
                new Vector3(0.055f, 0.78f, 0.07f), palette.Trim, root, false);
        }

        private static void CreatePointLight(
            string name,
            Vector3 position,
            float range,
            float intensity,
            Transform parent)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            light.intensity = intensity;
            light.color = new Color(1f, 0.78f, 0.58f);
            // 포인트 라이트 하나당 6면의 그림자 맵이 필요하므로 방 조명은 채움광으로만 쓴다.
            // 구조물 그림자는 메인 Directional Light가 담당한다.
            light.shadows = LightShadows.None;
        }

        private static void WallX(
            string name,
            float fromX,
            float toX,
            float z,
            Material material,
            Transform parent)
        {
            CreateCube(name, new Vector3((fromX + toX) * 0.5f, WallHeight * 0.5f, z),
                new Vector3(toX - fromX, WallHeight, WallThickness), material, parent);
        }

        private static void WallZ(
            string name,
            float fromZ,
            float toZ,
            float x,
            Material material,
            Transform parent)
        {
            CreateCube(name, new Vector3(x, WallHeight * 0.5f, (fromZ + toZ) * 0.5f),
                new Vector3(WallThickness, WallHeight, toZ - fromZ), material, parent);
        }

        /// <summary>방 바닥 슬래브. 벽 중심선에서 중심선까지 깔아 벽 밑에서 바닥이 끊기지 않게 한다.</summary>
        private static void CreateFloor(
            string name,
            float fromX,
            float toX,
            float fromZ,
            float toZ,
            Material material,
            Transform parent)
        {
            CreateCube(
                name,
                new Vector3((fromX + toX) * 0.5f, -0.1f, (fromZ + toZ) * 0.5f),
                new Vector3(toX - fromX, 0.2f, toZ - fromZ),
                material,
                parent);
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent);
            return group.transform;
        }

        private static GameObject CreateCube(
            string name,
            Vector3 position,
            Vector3 scale,
            Material material,
            Transform parent,
            bool keepCollider = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.SetPositionAndRotation(position, Quaternion.identity);
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Object.DestroyImmediate(cube.GetComponent<Collider>());
            else
                GameObjectUtility.SetStaticEditorFlags(cube, StaticEditorFlags.BatchingStatic);
            return cube;
        }

        private static GameObject CreateLocalCube(
            string name,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            Transform parent,
            bool keepCollider = true,
            bool markStatic = true)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = localPosition;
            cube.transform.localScale = localScale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Object.DestroyImmediate(cube.GetComponent<Collider>());
            else if (markStatic)
                GameObjectUtility.SetStaticEditorFlags(cube, StaticEditorFlags.BatchingStatic);
            return cube;
        }

        private static GameObject CreateLocalCylinder(
            string name,
            Vector3 localPosition,
            float radius,
            float height,
            Material material,
            Transform parent,
            bool keepCollider = true)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, false);
            cylinder.transform.localPosition = localPosition;
            cylinder.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            cylinder.GetComponent<Renderer>().sharedMaterial = material;
            if (!keepCollider)
                Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            else
                GameObjectUtility.SetStaticEditorFlags(cylinder, StaticEditorFlags.BatchingStatic);
            return cylinder;
        }
    }
}
