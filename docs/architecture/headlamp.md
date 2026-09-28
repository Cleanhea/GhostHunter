# 헤드라이트 구현

> 상태: **코드·Player 프리팹 배선 구현 (2026-09-28).** 규칙의 권위는 [헤드라이트 기획서](../project/headlamp-system.md).
> 실제 화면(밝기·그림자)과 Host/Client·Steam 확인은 아직이다.

## 1. 구성

| 대상 | 경로 | 역할 |
| --- | --- | --- |
| `PlayerHeadlamp` | `Assets/Scripts/Gameplay/Player/PlayerHeadlamp.cs` | Player 프리팹. 입력·배터리 진행(소유자), 전원·배터리 복제, 조명 표시(전 피어), 어둠 노출 판정(서버) |
| `HeadlampBattery` | `Assets/Scripts/Gameplay/Player/HeadlampBattery.cs` | 전원·배터리·깜빡임 규칙 순수 클래스 — EditMode `HeadlampBatteryTests` |
| `HeadlampSettings` | `Assets/Settings/Gameplay/HeadlampSettings_Default.asset` | 배터리·깜빡임·조명·효과음 튜닝값 |
| `HeadlampChargeHud` | `Assets/Scripts/UI/HeadlampChargeHud.cs` | Player 프리팹. 로컬 소유자만 캔버스를 만든다. `MoleSkillUiSettings`로 두더지 스킬 UI와 같은 크기·색 |
| 입력 | `Player/Headlamp` (F) | `PlayerInputReader.HeadlampPressedThisFrame` |

## 2. 권위와 복제

| 데이터 | 권위 | 복제 |
| --- | --- | --- |
| 전원 | Owner | `NetworkVariable<bool>` Owner 쓰기·Everyone 읽기, 바뀌는 즉시 |
| 배터리 | Owner | `NetworkVariable<float>` Owner 쓰기, 0.25초 간격 |
| 충전 중 여부 | Owner 로컬 | 복제 안 함 — 충전 UI만 읽는다 |
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

## 5. 호스트 이전

`StageRecoverySnapshot.PlayerState`에 `HeadlampOn`·`HeadlampDrained`(쓴 양)를 싣는다. 남은 양이 아니라 쓴 양이라서
이 필드가 없는 스냅샷은 가득 찬 배터리로 복원된다. 복원은 `[Rpc(SendTo.Owner)]`로 소유자에게 보낸다.

## 6. 검증

- EditMode `HeadlampBatteryTests`(배터리·토글·충전·깜빡임), `ProjectWiringTests`(F 바인딩·프리팹 배선).
- 실제 화면·효과음·Host/Client·Steam 2PC는 미검증.

---

관련: [headlamp-system.md](../project/headlamp-system.md) · [sanity-system.md](sanity-system.md) ·
[player-controller.md](player-controller.md) · [ghost-prototype.md](ghost-prototype.md)
