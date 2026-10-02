# 모텔 맵 — 발광 / 조명 전달 문서 (초안)

FBX: `Motel_Map.fbx` / 텍스처: `Textures/` 폴더  
조명(Light)은 FBX에 **포함하지 않았습니다.** 아래 2번 표를 보고 Unity에서 배치해 주세요.

## 1. 발광 머티리얼 (Emission 설정 필요)

FBX에는 Emission 값이 제대로 넘어가지 않아서, 아래 머티리얼은 **Unity에서 Emission을 직접 켜야** 합니다. 색은 sRGB(0~255) 기준, Intensity는 Bloom을 켠 상태 기준의 시작값입니다.

| 머티리얼 | 쓰는 오브젝트 | 설명 | Emission 색 (RGB / HEX) | Emission 맵 | Unity Intensity | 연출 제안 |
|---|---|---|---|---|---|---|
| `M_Neon_Motel` | RoofSign_MotelText | 지붕 간판 'MOTEL' 네온 | 255, 71, 65 / `#FF4741` | - | 4.0 | 가끔 지직 (글자 일부 꺼짐) |
| `M_Neon_Sunset` | RoofSign_SunsetText | 지붕 간판 'Sunset' 네온 | 255, 90, 76 / `#FF5A4C` | - | 3.5 | 가끔 지직 |
| `M_Neon_Palm` | RoofSign_NeonPalm | 네온 야자수 (잎 1) | 255, 148, 194 / `#FF94C2` | - | 2.5 | - |
| `M_Neon_Palm_2` | RoofSign_NeonPalm | 네온 야자수 (잎 2) | 186, 255, 104 / `#BAFF68` | - | 2.5 | - |
| `M_Neon_Yellow` | YardSign_MotelText | 도로변 간판 'MOTEL' 네온 | 255, 81, 71 / `#FF5147` | - | 4.0 | 가끔 지직 |
| `M_Bulb_On` | YardSign_Bulbs | 도로변 화살표 간판 전구들 | 255, 217, 156 / `#FFD99C` | - | 3.0 | 체이싱(순서대로 깜빡) 추천 |
| `M_Ceiling_Bulb` | Corridor_CeilingLamp_1F_L/R, 2F_L/R | 복도 천장등 4개 | 220, 236, 255 / `#DCECFF` | - | 1.0 | 1~2개만 불규칙 깜빡 |
| `M_Room_Bulb_Warm` | Room202_Desk_Lamp | 202호 스탠드 전구 | 255, 205, 148 / `#FFCD94` | - | 2.0 | 스탠드 조명과 같이 On/Off |
| `M_Room_LampShade` | Room202_Desk_Lamp | 202호 스탠드 갓 (빛 비침) | 255, 202, 148 / `#FFCA94` | - | 0.3 | 스탠드와 같이 On/Off |
| `M_Room_VanityBulb` | Room202_Bath_VanityLight | 202호 욕실 세면대 전구 3개 | 255, 243, 224 / `#FFF3E0` | - | 1.5 | 깜빡임 (공포 연출 핵심) |
| `M_Room_MsgLight` | Room202_Desk_Phone | 202호 전화기 메시지 램프 | 255, 65, 43 / `#FF412B` | - | 1.5 | 느리게 점멸 (부재중 메시지) |
| `M_Vend_Fluoro` | VM_DisplayTube | 자판기 진열칸 형광등 | 237, 249, 255 / `#EDF9FF` | - | 2.0 | 형광등 지직 |
| `M_Vend_Header` | VM_Header | 자판기 상단 브랜드 패널 | Base Map 텍스처를 Emission에도 연결 | `T_Vend_Header.png` | 0.8 | 형광등과 같이 |
| `M_Vend_Promo` | VM_Promo | 자판기 가운데 포스터 | Base Map 텍스처를 Emission에도 연결 | `T_Vend_Promo.png` | 0.4 | 형광등과 같이 |
| `M_Vend_LED_Green` | VM_SelectLEDs | 자판기 선택 버튼 LED (일부는 꺼진 재질) | 123, 255, 158 / `#7BFF9E` | - | 2.0 | - |
| `M_Vend_LED_Red` | VM_LEDText | 자판기 'SOLD OUT' 표시 | 255, 81, 52 / `#FF5134` | - | 2.0 | - |

**발광 텍스처 목록 (Emission Map으로도 쓰는 것)**: `T_Vend_Header.png`, `T_Vend_Promo.png`

## 2. 조명 목록 (Blender에서 쓰던 것 → Unity에서 새로 배치)

좌표는 FBX 설정(Apply Transform, Y-up) 기준 Unity 좌표로 변환한 값입니다. 변환식: Unity = (-X, Z, -Y). 위치가 어긋나 보이면 '가까운 오브젝트' 기준으로 맞춰 주세요.

| Blender 이름 | 종류 | Unity 위치 | 색 (RGB / HEX) | Blender 세기 | 가까운 오브젝트 | 역할 | Unity 추천 |
|---|---|---|---|---|---|---|---|
| `BulbLight_Lower_0` | Point | (3.60, 3.20, 0.15) | 214, 230, 255 / `#D6E6FF` | 26 W | Corridor_CeilingLamp_1F_L | 1층 복도 천장등 | Point, Intensity 1.2, Range 6 |
| `BulbLight_Lower_1` | Point | (-3.60, 3.20, 0.15) | 214, 230, 255 / `#D6E6FF` | 26 W | Corridor_CeilingLamp_1F_R | 1층 복도 천장등 | Point, Intensity 1.2, Range 6 |
| `BulbLight_Upper_0` | Point | (3.60, 6.76, 0.15) | 214, 230, 255 / `#D6E6FF` | 26 W | Corridor_CeilingLamp_2F_L | 2층 복도 천장등 | Point, Intensity 1.2, Range 6 |
| `BulbLight_Upper_1` | Point | (-3.60, 6.76, 0.15) | 214, 230, 255 / `#D6E6FF` | 26 W | Corridor_CeilingLamp_2F_R | 2층 복도 천장등 | Point, Intensity 1.2, Range 6 |
| `Neon_Glow_Building` | Point | (-4.50, 7.80, 1.90) | 178, 65, 59 / `#B2413B` | 70 W | RoofSign_MotelText | 지붕 네온 간판 빛 번짐 | Point, Intensity 2, Range 8, 네온과 같이 깜빡 |
| `Neon_Glow_YardSign` | Point | (-7.40, 7.90, 7.00) | 206, 255, 232 / `#CEFFE8` | 220 W | YardSign_Bulbs | 도로변 간판 빛 번짐 | Point, Intensity 3, Range 12 |
| `Haunt_RoomGlow` | Point | (2.00, 5.60, -0.90) | 255, 189, 143 / `#FFBD8F` | 34 W | Window_201 | 201호 창문 안쪽 주황빛 (사람 있는 느낌) | Point, Intensity 1, Range 4 |
| `Haunt_RoomGlow2` | Point | (-4.60, 2.40, -0.90) | 255, 205, 148 / `#FFCD94` | 26 W | Door_103 근처 | 103호 안쪽 주황빛 | Point, Intensity 1, Range 4 |
| `L_Vending` | Point | (-5.81, 1.90, 1.00) | 237, 249, 255 / `#EDF9FF` | 22 W | VM_Glass (자판기 앞) | 자판기 앞 바닥 조명 | Point, Intensity 1, Range 4 |
| `L_Vending_Display` | Area | (-5.81, 2.33, 0.38) | 237, 249, 255 / `#EDF9FF` | 6 W | VM_DisplayTube | 자판기 진열칸 내부 | Point 또는 Area(베이크), Intensity 0.5 |
| `L202_DeskLamp` | Point | (0.06, 5.04, -4.40) | 255, 199, 148 / `#FFC794` | 70 W | Room202_Desk_Lamp | 202호 스탠드 (방의 주 조명) | Point, Intensity 1.5, Range 5, 그림자 ON |
| `L202_Bath_Flicker` | Point | (0.12, 5.77, -2.00) | 255, 241, 217 / `#FFF1D9` | 16 W | Room202_Bath_VanityLight | 202호 욕실 조명 | Point, Intensity 1, Range 4, 깜빡임 |
| `L202_Moon_Window` | Area | (-5.86, 5.87, -0.75) | 172, 189, 255 / `#ACBDFF` | 45 W | Window_202 | 202호 창문으로 들어오는 달빛 | Spot(안쪽 방향) Intensity 0.6 또는 Area 베이크 |
| `L202_Fill` | Area | (-3.20, 6.70, -4.60) | 194, 202, 224 / `#C2CAE0` | 25 W | Room202_CeilingLight | 202호 아주 약한 보조광 (형태만 보이게) | Ambient/Light Probe로 대체 추천 |
| `Key_Sun` | Directional | - | 156, 189, 255 / `#9CBDFF` | 1.15 W | - | 밤 하늘 달빛 (푸른색) | Directional, Intensity 0.15 |
| `Ambient_Front` | Area | (-0.50, 9.00, 13.00) | 160, 193, 255 / `#A0C1FF` | 200 W | 주차장 앞쪽 하늘 | 주차장 전체 푸른 환경광 | Ambient(Environment) Color로 대체 추천 |

## 3. 투명 머티리얼 (참고)

| 머티리얼 | 오브젝트 | 설정 |
|---|---|---|
| `M_Vend_Glass_Dirty` | VM_Glass | Transparent, Base Map `T_Vend_GlassDirt.png` (알파 포함) |
| `M_Window_Glass` | Window_202 | Transparent, 알파 약 0.2 |
| `M_Room_ShowerCurtain` | Room202_Bath_ShowerCurtain, 창문 커튼 | Transparent, 알파 약 0.4 (또는 Opaque) |

## 4. 텍스처 (구운 텍스처)

절차적 재질(카펫·벽지·나무결·때·녹 등)은 전부 **오브젝트별 텍스처로 구웠습니다.**  
구운 오브젝트는 머티리얼 이름이 `M_<오브젝트이름>`이고, 텍스처가 3장씩 있습니다.

| 파일 | Unity 슬롯 | Import 설정 |
|---|---|---|
| `T_<오브젝트>_BaseColor.png` | Base Map | 기본 (sRGB) |
| `T_<오브젝트>_MetallicSmoothness.png` | Metallic Map (R=금속도, A=매끄러움) | **sRGB 체크 해제**, 머티리얼 Smoothness Source = **Metallic Alpha** |
| `T_<오브젝트>_Normal.png` | Normal Map | Texture Type = **Normal map** |

- Base Map과 Normal Map은 FBX에 연결 정보가 들어 있어 자동으로 붙을 수 있지만, **Metallic Map은 수동 연결**이 필요합니다.
- 계단 손잡이(곡선 4개)와 네온 글자 3개는 단색/발광 재질이라 텍스처가 없습니다.
- 해상도는 오브젝트 크기에 맞춰 128~2048px로 정했습니다 (건물·바닥 2048, 가구 256~1024, 손잡이·스위치 등 작은 것 128).

## 5. 참고 사항

- 라이트맵이 필요하면 FBX Import Settings에서 **Generate Lightmap UVs**를 켜 주세요. (구운 텍스처용 UV는 UV0)
- FBX Import: Scale Factor 1, 위쪽 = Y (Apply Transform 적용으로 회전값 없음).
