# 청소 프로토타입 구현

기획: [청소 시스템](../project/cleaning-system.md). 권위·풀 구성:
[ADR-0014](decisions/ADR-0014-server-cleaning-prototype.md).

## 1. 구성

| 파일 | 역할 |
| --- | --- |
| `Gameplay/Player/QuickSlotItemDefinition.cs` | 대걸레 여부(`IsMop`) |
| `Gameplay/Player/PlayerCleaningController.cs` | 장착 요청·소유자 입력·서버 청소 검증·시점 대걸레 |
| `Gameplay/Cleaning/CleaningStain.cs` | 위치·회전·배치 세대·제거 상태를 하나의 서버 쓰기 상태로 복제 |
| `Gameplay/Cleaning/CleaningController.cs` | 씬 풀 랜덤 배치·HUD 초기화·첫 충돌 기준 얼룩 조준 |
| `Gameplay/Cleaning/ICleaningService.cs` | 플레이어·디버그 HUD 접근 계약. `GameInstaller`에서 등록 |
| `Gameplay/Cleaning/CleaningSettings.cs` | 임시 수량·사거리·연출·충돌 설정 SO |
| `Shaders/CleaningStain.shader` | 바닥 얼룩 모양과 방향성 닦임 연출 |
| `Editor/CleaningSetup.cs` | `GhostHunter > 청소 시스템 설치/검증` 메뉴 |

## 2. 흐름과 권위

1. `QuickSlotWheelUi`가 `ILocalPlayerContext.CleaningController.EquipSlot`을 호출한다.
   서버는 같은 로드아웃의 슬롯과 생존·운반·굴착 상태를 확인하고 소유자에게 결과를 회신한다.
   장착은 다른 플레이어에게 표시하지 않는다(QS-6 유지). 인벤토리·소모품 시스템은 없음.
2. 대걸레를 든 소유자가 좌클릭하면 첫 레이 충돌에 해당하는 활성 얼룩 ID·배치 세대·조준을 전송한다.
3. 서버는 RPC 발신자, 대걸레 장착, 생존·운반·매몰, 요청 간격, 벡터 유효성, 원점 거리,
   몸→원점 가림, 조준 사거리·첫 충돌, 얼룩 ID·세대를 다시 검증한다. 요청자 자신의 충돌체와
   얼룩 외 트리거를 제외한다. 64개 레이 버퍼가 가득 차면 가림 순서를 보장할 수 없어 청소를 거부한다.
4. 성공 시 서버 상태의 `Cleaned`를 바꾼다. 각 피어는 충돌·탐지 마커를 즉시 끄고 닦임 연출을 재생한다.
   중복 청소는 실패하며, 후접속자는 현재 상태로 표시한다.
5. Host 초기화는 세대를 증가시켜 풀을 복원하고 바닥 후보 순서를 다시 섞는다. 이전 세대의
   클릭은 무시한다. 씬과 SO의 저장 데이터는 플레이 중 수정하지 않는다.

가구 배치 준비와 모든 얼룩의 NGO 스폰을 기다린 뒤 최초 배치를 실행한다. 대기는 디스폰·파괴 시
취소한다. 후보는 기존 Floor 지점에서 바닥 지지·법선·가구/벽 겹침을 검사한다. 바닥 표면보다
`CleaningSettings.SurfaceOffset`(기본 0.05m)만큼 위에 얼룩을 놓는다. B안의 비충돌 장식 바닥은
실제 충돌 바닥보다 최대 0.0395m 높아, 그 위로 표시하기 위한 기술적 여유다.

## 3. 에셋과 설치

- `Assets/Prefabs/CleaningStain.prefab`: 재사용 씬 배치 프리팹, `DefaultNetworkPrefabs` 등록.
- `Game/CleaningPrototype/Stains`: 최대 12개 [TEMP] 씬 풀. 숨겨진 루트 자체는 활성 상태를 유지한다.
- `Game/CleaningPrototype/Controller`: 풀과 후보를 참조하는 독립 NetworkObject. 얼룩 NetworkObject의 부모가 아니다.
- `Player/CameraPivot/PlayerCamera/MopView`: 로컬 대걸레 메시. 충돌 없음, 모든 Transform scale 1.
- `CleaningSettings_Default.asset`, `QuickSlotItem_Mop.asset`: `Assets/Settings/Gameplay`.
- 기존 가구·집 구조를 재생성하지 않는다. 설치 후 씬 NGO 해시 갱신과 0/중복 검증, 명시적 저장.

## 4. 검증 상태

2026-09-12: 설치·저장과 Local Host 검증 완료. 최종 자동 테스트 결과는 아래에 기록한다.

| 검증 | 결과 |
| --- | --- |
| 컴파일 | Unity 6000.3.20f1에서 오류 0건 |
| EditMode | 최종 **216/216 통과**, 실패·스킵 0 |
| PlayMode | 최종 **15/15 통과**, 실패·스킵 0. 청소 회귀 검사 3개 포함 |
| 설치 | `CleaningSetup.Install/Validate` 통과. Game 씬·Player 프리팹·SO·얼룩 프리팹 저장 |
| Local Host | B안 얼룩 **12/12** 배치. 대걸레 슬롯 확인 후 실제 Input System 좌클릭 주입으로 **12→11** 제거 |
| 초기화 | HUD가 호출하는 `ICleaningService.ResetStains`로 **11→12**, 활성 탐지 마커 12개 복원 |
| 장비 변경 | Tab 누름·아래 방향 마우스 델타·Tab 해제 입력으로 2번 맨손 확정. 로컬·서버 모두 대걸레 해제, 모델 숨김·휠 잠금 해제 확인 |
| 화면 | `Logs/Cleaning/mop-before-clean.png`·`mop-after-clean.png`로 얼룩 표시/제거 확인 |
| 남은 검증 | 사람의 닦기 조작감 평가, 실제 원격 Client·Steam 2PC 복제/후접속 검증 |

최초 PlayMode 실행은 MCP가 완료 콜백을 놓쳐 초기화 타임아웃으로 보고했지만 Unity XML은
15/15 통과였다. 재실행에서 MCP도 15/15 통과를 반환했다. 화면 캡처 때 나타난 PlayerLoop
재귀 오류는 스택의 `MCPForUnity...ScreenshotUtility.CaptureCompositedAfterFrame`에서 발생했다.
게임플레이 코드 오류와 구분하며, 화면 캡처를 포함한 세션 전체가 오류 0건이었다고 보고하지 않는다.

- EditMode: 조준 원점 거리·0/비정규화·NaN/Infinity 거부.
- PlayMode: 서버 청소의 중복 거부, 초기화 전 세대 거부, 재배치 좌표 반영, 벽 가림·거리, 미스폰 변경 거부.
- 후속 수동 확인: Tab 장착→좌클릭 닦기 체감, Q 탐지, 맨손 가구 잡기 복귀, Host 초기화의 양측 복제.
