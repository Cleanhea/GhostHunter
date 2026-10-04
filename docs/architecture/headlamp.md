# 헤드라이트 구현

> 상태: **코드·Player 프리팹 배선 구현 (2026-09-28).** 규칙의 권위는 [헤드라이트 기획서](../project/headlamp-system.md).
> 실제 화면(밝기·그림자)과 Host/Client·Steam 확인은 아직이다.

## 1. 구성

| 대상 | 경로 | 역할 |
| --- | --- | --- |
| `PlayerHeadlamp` | `Assets/Scripts/Gameplay/Player/PlayerHeadlamp.cs` | Player 프리팹. 입력·배터리 진행(소유자), 전원·배터리 복제, 조명 표시(전 피어), 어둠 노출 판정(서버) |
| `HeadlampBattery` | `Assets/Scripts/Gameplay/Player/HeadlampBattery.cs` | 전원·배터리·깜빡임 규칙 순수 클래스 — EditMode `HeadlampBatteryTests` |
| `HeadlampSettings` | `Assets/Settings/Gameplay/HeadlampSettings_Default.asset` | 배터리·깜빡임·조명·효과음 튜닝값 |
| `HeadlampChargeHud` | `Assets/Scripts/UI/HeadlampChargeHud.cs` | Player 프리팹. 로컬 소유자만 캔버스를 만들고 헤드라이트가 켜져 있는 동안 배터리 게이지를 띄운다. `MoleSkillUiSettings`로 두더지 스킬 UI와 같은 크기·색. 원 안은 목업 `ICON_HeadLight.png` 배치(위 `nn%`, 아래 아이콘), 보일 때는 초록·`ICON_HeadLight_on`(`ICON_HeadLight_off`·흰색은 배선만 유지) — 검증 `ProjectWiringTests.Player_프리팹의_헤드라이트_충전_HUD_에_on_off_아이콘이_배선되어_있다` |
| 입력 | `Player/Headlamp` (F) | `PlayerInputReader.HeadlampPressedThisFrame` |

## 2. 권위와 복제

| 데이터 | 권위 | 복제 |
| --- | --- | --- |
| 전원 | Owner | `NetworkVariable<bool>` Owner 쓰기·Everyone 읽기, 바뀌는 즉시 |
| 배터리 | Owner | `NetworkVariable<float>` Owner 쓰기, 0.25초 간격 |
| 충전 중 여부 | Owner 로컬 | 복제 안 함 |
| 저전력 깜빡임 | 각 피어 로컬 | 복제된 전원·배터리로 각자 재생 |
| 어둠 노출 | Server | `SanityNetworkState.ServerSetDarknessExposed` |

전원·배터리는 `MoleBurrowController.IsBurrowed`·`PlayerMotor.IsCrouching`과 같은 소유자 권위 패턴이다. 물리 상태가 아니라
ADR-0010(서버 권위 가구 물리) 예외에 해당하지 않는다. 서버는 복제된 전원 값을 그대로 믿는다.

## 3. 판정

**어둠 노출(서버, 매 프레임).** `스테이지 씬 && 생존 && 전원 꺼짐 && 드릴카(DrillCarSafeZone) 밖`이 바뀔 때만
`ServerSetDarknessExposed`를 부른다. 판정이 매 프레임 덮어쓰므로 F1 HUD의 `어둠 노출 토글`은 다음 판정까지만 간다.
인게임 로비는 스테이지가 아니라 어둠 판정을 하지 않는다.

**땅굴 안 헤드라이트.** `SanityNetworkState.IsBurrowed`(귀신이 읽는 값)가 `MoleBurrowController.IsBurrowed && !헤드라이트 켜짐`이다.
켜 두면 귀신은 땅 위에 서 있는 플레이어처럼 탐지·포획한다. 몸 숨기기·이름표는 `MoleBurrowController.IsBurrowed`를 직접 읽어 영향이 없다.
굴착 중 꺼 둔 채 매몰됐다가 켜고 다시 끄면 `BurrowExposureTracker`에는 새 매몰로 보인다 — 그 순간 쫓기고 있었는지로 다시 판정된다.

**은신 중 사용 불가(소유자).** `HidingSpot.Contains` 또는 `엎드림 && BedHideZone.Contains`면 끄고 켜기를 막는다.
귀신의 은신 판정과 같은 기하를 쓰지만, 은신 **성립**(서버 `BedHideEvaluator`)이 아니라 **위치** 기준이다.

## 4. 조명

- 스폰 시 Player 루트 아래 `Headlamp` 오브젝트(Spot Light + 2D `AudioSource`)를 만든다 — 저장된 프리팹에는 없다.
- 위치: `(0, PlayerMotor.CameraLocalHeight, 0) + 피치 × LocalOffset`, 방향: 몸 yaw + `PlayerLook.Pitch`.
  둘 다 원격에서도 복제되는 값이라 다른 플레이어의 불빛이 그 사람의 시선을 따라간다.
- `LocalOffset` 기본 `(0, 0.12, 0.18)` — 원격 머리 메시가 자기 불빛을 가리지 않도록 앞으로 뺐다.
- 그림자는 `PC_RPAsset`의 Additional Light Shadows(켜짐)에 기댄다. `Mobile_RPAsset`은 꺼져 있어 그림자가 없다.
  Additional Lights Per Object 한도 4 — 방 조명이 많은 곳에서 가까운 오브젝트 위주로 잘릴 수 있다.

### 4.1 쿠키·빛줄기 (2026-10-03)

스포트 라이트만으로는 표면에 동그란 원 하나만 맺혀 어색했다. 표면 무늬와 공기 중 빛줄기를 더한다. 둘 다 런타임 생성물이고 네트워크와 무관한 로컬 연출이다.

| 대상 | 경로 | 역할 |
| --- | --- | --- |
| `HeadlampVisuals` | `Assets/Scripts/Gameplay/Player/HeadlampVisuals.cs` | 쿠키 텍스처(128², Clamp)·원뿔 메시 생성 — EditMode `HeadlampVisualsTests` |
| 빛줄기 셰이더 | `Assets/Shaders/HeadlampBeam.shader` (`GhostHunter/HeadlampBeam`) | 가산 반투명 원뿔(가짜 볼류메트릭) |
| 빛줄기 재질 | `Assets/Materials/M_HeadlampBeam.mat` | 윤곽 흐림·길이 감쇠·벽 경계 흐림·자기 시점 배율 |

- **쿠키** — 가운데 핫스팟(1) + 테두리 링 + 바깥으로 흐려지는 주변광, 바깥 각도(반지름 1) 이상은 0. `HeadlampSettings`의
  `CookieHotspotRadius`·`CookieSpill`·`CookieRingStrength`로 만들고, `CookieOverride`에 직접 그린 텍스처를 넣으면 그걸 쓴다.
  스포트의 안쪽·바깥 각도 감쇠와 곱해진다. `PC_RPAsset`·`Mobile_RPAsset` 모두 Light Cookies 켜짐.
- **빛줄기** — `Headlamp` 아래 `HeadlampBeam`(MeshRenderer, 그림자 끔). 각도 `BeamAngle`(36°, 조명 바깥 62°보다 좁게), 최대 길이 `BeamLength`(6m),
  세기 `BeamIntensity`(조명 색에 곱함). 켜짐은 조명과 같다(저전력 깜빡임 포함).
  - 길이는 매 프레임 램프 정면 레이캐스트(`BeamOcclusionMask`, 트리거·자기 몸 제외)로 가로막는 표면 + 0.3m까지 줄인다 — 벽 너머로 새지 않게.
    줄어들 땐 즉시, 늘어날 땐 부드럽게. 스케일 1 규칙 때문에 트랜스폼이 아니라 `MaterialPropertyBlock`의 `_BeamLength`로 정점을 늘린다.
  - 자기 시점: 셰이더가 카메라–램프 거리로 판단해 `_NearApexScale`(0.35)만큼 약하게 그린다. 관전 1인칭 추종도 같은 규칙이라 코드 분기가 없다.
  - 벽·바닥 경계 흐림은 카메라 깊이 텍스처가 필요하다 — `PC_RPAsset` 켜짐, `Mobile_RPAsset` 꺼짐(쓰려면 재질의 Soft Intersection을 끈다).
- 화면 검증 전 임시값이다(HL-2와 함께 조정).

## 5. 호스트 이전

`StageRecoverySnapshot.PlayerState`에 `HeadlampOn`·`HeadlampDrained`(쓴 양)를 싣는다. 남은 양이 아니라 쓴 양이라서
이 필드가 없는 스냅샷은 가득 찬 배터리로 복원된다. 복원은 `[Rpc(SendTo.Owner)]`로 소유자에게 보낸다.

## 6. 검증

- EditMode `HeadlampBatteryTests`(배터리·토글·충전·깜빡임), `HeadlampVisualsTests`(쿠키 모양·빛줄기 메시),
  `ProjectWiringTests`(F 바인딩·프리팹 배선·빛줄기 재질).
- 실제 화면·효과음·Host/Client·Steam 2PC는 미검증.

## 7. 라이터 (2026-09-29)

규칙은 [lighter-system.md](../project/lighter-system.md). 헤드라이트와 같은 권위·복제 구조를 그대로 쓰는 두 번째 광원이다.

| 대상 | 경로 | 역할 |
| --- | --- | --- |
| `PlayerLighter` | `Assets/Scripts/Gameplay/Player/PlayerLighter.cs` | Player 프리팹. 켜짐·연료 진행(소유자), `NetworkVariable` 복제(켜짐 즉시·연료 0.25초), 불빛·모양 표시(전 피어) |
| 연료 규칙 | `HeadlampBattery` 재사용 | 켜져 있을 때 소모·드릴카 충전·0이면 꺼짐이 배터리와 같다. 깜빡임은 쓰지 않는다 |
| `LighterSettings` | `Assets/Settings/Gameplay/LighterSettings_Default.asset` | 연료·불빛·흔들림·재질·효과음 [TEMP] |
| `LighterFuelHud` | `Assets/Scripts/UI/LighterFuelHud.cs` | Player 프리팹. 헤드라이트 충전 칸(2) 아래 칸(3)에 "라이터 nn%" |
| 퀵슬롯 아이템 | `QuickSlotItem_Lighter.asset` (`IsLighter`) | `QuickSlotLoadout_Default` 3번(빈 칸이던 자리) |
| 불꽃 재질 | `Assets/Materials/M_LighterFlame.mat` (URP Unlit 주황) | 몸체는 `Map_Trim` |

- **켜짐 = 들고 있음.** 입력 액션이 없다. 소유자가 매 프레임 `PlayerCleaningController.EquippedSlot`(서버가 확인해 돌려준 퀵슬롯 장착 —
  대걸레 컨트롤러가 모든 슬롯의 장착을 맞춘다)의 아이템이 `IsLighter` 인지 보고, `생존 && 은신 위치 아님`이면 붙인다.
  연료가 0이면 붙지 않고, 든 채로 드릴카에서 연료가 차면 다시 붙는다.
- **판정 연결** — 헤드라이트 어둠 판정(§3)이 `!헤드라이트 && !라이터`로, `SanityNetworkState.IsBurrowed` 가 `… && !라이터 켜짐`으로 바뀌었다.
  둘 다 같은 오브젝트의 `PlayerLighter` 를 `GetComponent` 로 찾는다(없으면 헤드라이트만 본다).
- **불빛** — 스폰 시 Player 루트 아래 `Lighter`(몸체·불꽃 도형 + 점광원 `FlameLight`)를 만들고, 소리는 항상 켜져 있는 `LighterAudio` 에 둔다
  (꺼질 때 `Lighter` 가 비활성화되므로 "집어넣는 소리"를 내려면 분리해야 한다). 위치는 헤드라이트와 같은 방식
  `(0, 눈높이, 0) + 피치 × LocalOffset(0.22, −0.2, 0.42)`. 밝기는 Perlin 잡음으로 흔든다 — 피어마다 `NetworkObjectId` 씨앗이 달라 박자가 다르다.
  도형은 런타임 생성물이라 충돌체를 지우고 그림자를 끈다.
- **호스트 이전** — `PlayerState.LighterDrained`(쓴 연료)를 싣는다. 켜짐은 장착에서 다시 나오므로 싣지 않는다.
- 검증: `ProjectWiringTests.Player_프리팹에_라이터와_연료_HUD_가_배선되고_퀵슬롯에_라이터가_있다`. 실제 화면·Host/Client 는 미검증.

---

관련: [headlamp-system.md](../project/headlamp-system.md) · [sanity-system.md](sanity-system.md) ·
[player-controller.md](player-controller.md) · [ghost-prototype.md](ghost-prototype.md)
