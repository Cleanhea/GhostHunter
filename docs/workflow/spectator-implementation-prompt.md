# 사망 후 관전 기능 구현 모델용 프롬프트

GhostHunter 저장소에서 사망 후 관전 기능을 구현하라. 아래 내용을 작업 지시로 사용하되,
게임 규칙의 최신 권위는 `docs/project/spectator-system.md`다. **기획서만 다시 쓰고 끝내지 말고,
확정된 범위의 코드·배선·검증·문서 갱신까지 수행하라.**

## 1. 컨텍스트 로드와 범위 확인

1. 루트 `AGENTS.md`, `CLAUDE.md`의 규약과 라우팅을 읽고 현재 작업 트리 변경을 확인하라.
   기존 사용자 변경을 덮거나 되돌리지 마라. 현재 코드와 문서의 차이를 확인한 뒤 파일 단위 계획을 먼저 제시하라.
2. 다음 문서를 읽어라.
   - `docs/project/overview.md`, `gdd.md` §3, `spectator-system.md`, `roadmap.md` §1.5.
   - `docs/architecture/player-controller.md`, `networking.md`, `quick-slot.md`.
   - `docs/project/mole-skill-system.md` §3.1·§4·§5·MS-20, `quick-slot-system.md` §4·§7,
     `pause-menu-system.md`의 입력 잠금·사망 중 메뉴 규칙.
   - `docs/conventions/code-style.md`, `docs/workflow/development-loop.md`, `testing.md`.
   - 씬·프리팹·SO 배선 전에 `docs/conventions/unity-assets.md`, `docs/workflow/unity-mcp.md`.
3. **관전 기획서 §5의 미결정 사항을 먼저 확인하라.** 이미 사용자 답변으로 확정된 내용은 다시 묻지 마라.
   남은 조작키·기본 모드·대상 무효 처리·진행 중 스킬/홀드 정리·속도·대상 순서·복귀 위치는
   한 번에 묶어 사용자에게 물어라. 제안값을 확정값처럼 코드나 테스트에 고정하지 마라.
   답변을 기다리는 동안 기존 상태·생존 검사·입력 구조 분석 등 독립 작업은 계속하라.

## 2. 구현 목표

- 사망 상태에서는 탐지·굴착 등 특수능력을 사용할 수 없다.
- **자유시점**: 사망한 몸과 분리된 로컬 카메라가 충돌·중력 없이 맵의 벽·바닥·천장·문·가구를
  통과하며 전후·좌우·상하로 이동하고 회전한다.
- **플레이어 관전**: 확정된 키 입력으로 생존자의 실제 1인칭 위치·눈높이·보는 방향을 계속 따라간다.
  해당 위치로 한 번 텔레포트하는 것으로 끝내지 마라. 자유시점 복귀와 생존 대상 변경도 제공하라.
- 대상은 접속·스폰·생존 상태가 유효한 다른 플레이어다. 대상의 사망·연결 종료·디스폰과 대상
  1명/0명 상황을 기획서의 확정 정책대로 처리하라. 전원 사망을 새 패배 규칙으로 만들지 마라.
- 퀵슬롯은 사망 시 선택 취소 후 닫고 재개방을 막아라. 일반 상호작용과 진행 중 능력/홀드는
  §5 SP-2에서 확정된 정책대로 차단·정리하라.
- ESC 메뉴는 관전 중에도 사용 가능하다. 메뉴가 열리면 관전 입력도 잠그되 `Time.timeScale`을
  변경하지 마라. 메뉴 중 사망해도 메뉴를 유지하고, 닫았을 때 올바른 관전 상태로 돌아가라.

## 3. 먼저 확인할 기존 코드와 함정

경로는 모두 `Assets/Scripts/` 기준이다. 아래는 2026-09-12 문서 작성 시 정적으로 확인한 내용이며,
구현 전에 현재 코드를 다시 대조하라.

| 경로 | 확인할 내용 |
| --- | --- |
| `Gameplay/Sanity/SanityNetworkState.cs` | 서버 쓰기 `_isAlive`, `HasSanity`, `ServerMarkDead`, `ServerRevive`, 초기 상태·변경 통지. 현재 공개 생존 전용 이벤트가 없으므로 필요한 통지 경로를 검토 |
| `Gameplay/Sanity/ISanityTeamService.cs`, `SanityTeamService.cs` | `TryGetLocalState`, `CopyPlayerStates`로 로컬 생존 상태와 참가자 목록 조회 가능 |
| `Gameplay/Player/PlayerInputReader.cs` | 메뉴·굴착·퀵슬롯 세 독립 잠금과 `RawLookDelta`. 관전 입력이 생존 입력으로 새거나 메뉴 잠금을 우회하지 않도록 확장 |
| `Gameplay/Player/PlayerMotor.cs`, `PlayerLook.cs` | 몸 이동·중력, 자세와 카메라 높이, 로컬 피치, 스폰/디스폰의 카메라·리스너·커서 관리 |
| `Gameplay/Player/PlayerVisuals.cs`, `PlayerNetworkSpawn.cs` | 원격 몸체 표현과 스폰 위치, 관전 종료 시 복구할 기존 책임 |
| `Gameplay/Player/ILocalPlayerContext.cs`, `LocalPlayerContext.cs` | UI에 로컬 컴포넌트를 노출하는 패턴. 현재 정신력 참조는 없음. 기존 정신력 서비스와 비교해 최소 변경 경로 선택 |
| `Gameplay/Player/DetectionSkillController.cs`, `DetectionSkillStateMachine.cs`, `DetectionSkillSettings.cs` | 생존 검사와 `CancelOnDeath` 임시 정책. 탐지 표시/파란빛 종료와 사망 게이팅 |
| `Gameplay/Player/MoleBurrowController.cs` | 사망 시 도약 없는 취소·종료가 이미 있음. 종료가 `MovementLocked`/스킬 잠금/카메라 높이를 복구하는 순서 |
| `Gameplay/Interaction/GrabController.cs`, `FurnitureTargeter.cs`, `PlayerInteractor.cs`, `DoorInteractable.cs` | 로컬 타기팅·입력·개발용 F12 홀드와 서버 요청의 생존 검사 누락 |
| `Gameplay/Furniture/FurnitureGrabTarget.cs` | 사망자의 홀더 제거와 서버 상태 정리. `ServerForceRelease`의 `forced = true` 경로 활용 가능 |
| `UI/QuickSlotWheelUi.cs`, `MoleSkillHud.cs`, `MoleBurrowCameraEffect.cs`, `SanityCameraNoise.cs` | 사망 시 휠·하이라이트·시전 오버레이·비네트·노이즈·잠금 잔여 상태 |
| `Assets/InputSystem_Actions.inputactions` (저장소 루트 기준) | 기존 키와 충돌하지 않는 관전 액션. 신규 Input System만 사용 |

특히 다음을 빠뜨리지 마라.

1. **생존과 정신력 수치를 구분하라.** `HasSanity`는 `_isAlive`다. 정신력 0인 생존자를 죽었다고
   처리하지 마라. 별도 로컬 사망 bool을 SSOT로 만들지 마라. 참조 누락을 생존으로 간주하는
   기존 스킬 경로는 실제 프리팹 배선과 함께 검토하라.
2. **원격 피치가 현재 복제되지 않는다.** 생존자의 루트 Y 회전만 따라가면 위/아래를 볼 때 잘못된
   관전 화면이 된다. 실제 생존자 눈높이·yaw·pitch를 관전자 로컬 카메라에 재현할 데이터 경로를
   추가하라. 자세·굴착·메뉴 중에도 표현이 일관되어야 한다.
   현재 이동 권위 예외의 범위인지 검토하고, 새 권위 예외나 되돌리기 비싼 구조 선택이면 ADR을 작성하라.
3. **사망한 네트워크 Player 루트를 자유비행시키지 마라.** 관전 카메라의 위치·방향은 로컬 표현이고
   캐릭터 위치·가구 조준 원점·귀신 탐지·은신 판정에 쓰여서는 안 된다. 생존자의 입력이나 소유권을
   넘겨받지 마라. 원격 `PlayerLook`은 비활성화돼 있으므로 관전용 데이터 수신을 그 `Update`에만 의존하지 마라.
4. **메뉴용 `GrabController.ForceRelease()`는 발사할 수 있다.** 사망 시 발사 없는 놓기가 확정되면
   서버의 강제 홀더 해제 경로를 사용하라. 함께 잡던 생존자의 홀드는 보존하고, 사망자의 요청 중
   상태·F12 입력 고정·조준 갱신도 정리하라. 사망 시 RPC를 막기만 해 홀더가 영구 잔류하면 안 된다.
5. **로컬 입력 차단만으로 끝내지 마라.** SP-2 범위의 잡기·조준·발사·문 토글 요청은 서버가
   송신자·현재 생존·소유권·거리·벡터 유효성을 확인해야 한다. 사망 직전 전송되어 늦게 도착한
   요청도 같은 검사를 받는다. 귀신의 서버 전용 문 조작은 플레이어 생존 검사로 막지 마라.
6. **잠금 해제 순서가 권한을 복원하지 않도록 하라.** 굴착 취소가 `MovementLocked = false`로
   돌리는 순간 사망한 몸이 다시 움직이거나, 휠 닫기·메뉴 닫기가 스킬 사용을 열면 안 된다.
   `SetGameplayInputLocked(true)` 하나만 영구 적용해 자유시점 입력까지 없애는 구조도 피하라.
7. **카메라·효과는 로컬 관전자 범위에서 관리하라.** 중복 Camera/AudioListener와 대상 몸체의 시야
   가림을 처리하라. 대상 개인의 탐지 결과·HUD·정신력 효과를 복제하는 새 기능은 만들지 마라.

## 4. 구현과 정리 절차

1. 사망 상태 통지/조회와 대상 목록 수집, 로컬 생존/관전 상태 전이를 구성하라.
   초기 스폰 시 이미 사망한 상태도 적용하고 생존자를 매 프레임 전역 검색하지 마라.
2. 사망 시 입력·스킬·홀드·UI 정리와 서버 요청 검증을 연결하라. 기획의 확정된 취소 정책을
   설정값으로 우회할 수 없도록 실제 설정/코드를 맞추되, 쿨타임 등 미정 밸런스를 새로 정하지 마라.
3. 관전 입력과 자유 카메라를 구현하고, 생존자 시선 정보 및 1인칭 추종을 연결하라.
   전이 로직과 대상 선택은 가능하면 순수 로직으로 분리하고 현재 어셈블리 경계를 유지하라.
4. 디버그 `ServerRevive()`·스테이지 리셋·디스폰·씬 종료에서 카메라/리스너/입력/잠금/이벤트 구독을
   정리하거나 복구하라. 사망자 몸의 위치를 관전 카메라 위치로 옮겨 복구하지 마라.
   호스트 플레이어가 죽어도 서버·세션·귀신 업데이트가 계속되게 하라.
5. 필요하면 현재 Editor 설치 도구 패턴을 따라 **멱등 설치·배선 검증**을 추가하라.
   `.meta`를 직접 만들거나 수정하지 말고, `.unity`·`.prefab`·`.asset`을 텍스트로 고치지 마라.
   MCP 사용 전 실제 연결을 확인하고 씬·에셋 변경 내용을 고지하라. 전체 `Game` 씬 재생성은 하지 마라.
   MCP가 없으면 수행할 배선을 번호로 안내하고 미실행으로 기록하라.

새 패키지·Unity/URP 버전·프로젝트 설정·Steam 설정·맵 규모는 바꾸지 마라. 가구 물리는 서버 권위를
유지하고 입력은 신규 Input System, RPC는 `[Rpc(SendTo.…)]`를 사용하라. 새 `static Instance`,
매 프레임 `Find`/`Camera.main`/LINQ 할당, `async void`·새 코루틴을 도입하지 마라.
튜닝 수치는 SO로 관리하라. 시체 생성·부활 게임 규칙·승패·실제 인벤토리는 이번 범위 밖이다.
기존 미정 항목 전체를 이번 작업에 끌어들이지 마라.

## 5. 검증·문서·최종 보고

관전 기획서 §6 AC-1~9를 완료 기준으로 사용하라.

- **컴파일** → 관련 **EditMode** → **PlayMode/Host·Client 확인** 순으로 검증하라.
  에디터가 열려 있으면 MCP 경로부터 확인하고 잠긴 프로젝트에 batchmode를 실행하지 마라.
- 의미 있는 자동 검증에 집중하라: 생존 0 정신력과 사망 구분, 유효 대상 필터/순회·0명 처리,
  잠금 충돌·사망/부활 전이, 사망자 서버 요청 거절과 홀더 정리. 구현을 그대로 베끼는 테스트는 피하라.
- 수동 검증은 Host 사망→Client 관전과 Client 사망→Host/다른 Client 관전 양방향으로 수행하라.
  위/아래 시선, 웅크림·엎드림·굴착, 맵 층간 관통, 대상 이탈, 메뉴 중 사망, 휠/홀드 중 사망,
  디버그 부활·씬 종료를 확인하라. Steam 2인 접속은 PC 2대·계정 2개에서만 검증 완료로 보고하라.
- `spectator-system.md`의 현황, `roadmap.md` §1.5, 영향받은 `player-controller.md`·`networking.md`·
  스킬/퀵슬롯 문서를 실제 구현과 맞춰라. 피치 복제 후에도 "피치는 로컬 전용" 설명을 남겨 두지 마라.
- 완료 보고에는 구현 내용, 변경 경로, 확정/잔여 TBD, 실행한 검증 결과와 미실행 이유,
  씬·프리팹·설정 저장 여부, 필요한 사용자 수동 작업을 적어라. 검증하지 않은 동작을 완료로 쓰지 마라.
  원격 push·이력 변경은 별도 명시적 지시 없이 하지 마라.

최종 갱신: 2026-09-12 (관전 기획서와 현재 코드에 근거한 구현 지시. 이 파일 작성 자체는 구현 완료가 아니다.)
