# 08. 로드맵 / 작업 체크리스트

각 마일스톤은 **눈으로 확인 가능한 결과**로 끝난다. 다음으로 넘어가기 전에 그 결과를 실제로 확인한다.

---

## M0 — 프로젝트 정지 작업

> 완료 기준: 빈 프로토타입 씬에서 캡슐 하나가 돌아다닌다.

- [x] `Assets/Scripts/` 하위 폴더 생성 (`Core`, `Networking`, `Player`, `Interaction`, `Furniture`, `UI`, `DebugTools`, `Editor`)
- [x] `GhostHunter.Runtime` / `GhostHunter.Editor` asmdef 생성
- [ ] 씬 분리: `Bootstrap`, `Lobby`, `Prototype` (`SampleScene` 정리)
- [x] Project Settings 에 레이어 추가: `Player`, `Furniture`
- [x] `Prototype` 씬에 바닥, **벽 1개**, 플레이어/가구 스폰 포인트 배치
- [x] `Assets/Scripts/Temp.cs` 삭제
- [x] 프로젝트 설정: Company Name `GhostHunter`

> 현재는 빠른 검증을 위해 `Prototype` 한 씬에 네트워크 리그와 게임플레이를 통합했다.
> `Bootstrap` / `Lobby` 분리는 실제 씬 전환 UI가 필요해질 때 진행한다.

---

## M1 — 1인칭 이동 (싱글)

> 완료 기준: 혼자서 WASD로 걷고 마우스로 둘러보고 점프한다. 네트워크 없음.

- [x] `PlayerMoveSettings` ScriptableObject
- [x] `PlayerInputReader` — Input System 액션 바인딩
- [x] `PlayerLook` — 요/피치, 피치 클램프, 커서 잠금 + Esc 토글
- [x] `PlayerMotor` — CharacterController 이동/중력/점프
- [x] `Player` 프리팹 조립

→ [04-player-controller.md](04-player-controller.md)

---

## M2 — 멀티플레이 접속 (UTP 로컬)

> 완료 기준: 빌드와 에디터가 127.0.0.1로 연결되어 서로의 캡슐이 움직이는 게 보인다.

- [x] NGO 패키지 설치 (2.13.1) + UTP 2.7.3
- [x] `NetworkManager` 세팅 — `NetworkRigSetup` 에디터 도구로 자동 배선
- [x] `ConnectionManager` — Host/Join, 트랜스포트 스위칭 구조
- [x] 임시 접속 UI — `ConnectionHud` (F1)
- [x] `ClientNetworkTransform` (소유자 권위)
- [x] `Player` 프리팹에 `NetworkObject` + 스폰 등록
- [x] 로컬/원격 구분 — 카메라·입력은 소유자만 활성
- [ ] **실제 2인 접속 확인** (빌드 + 에디터, 127.0.0.1)

→ [03-multiplayer-setup.md](03-multiplayer-setup.md)

**여기서 UTP로 먼저 끝내는 이유:** Steam을 붙이기 전에 게임 로직을 검증해두면, 나중에 문제가 생겼을 때 원인이 Steam 쪽인지 로직 쪽인지 바로 갈린다.

---

## M3 — Steam 연결

> 완료 기준: 다른 PC(또는 다른 Steam 계정)의 친구가 초대를 통해 접속한다.

- [x] Facepunch.Steamworks DLL 배치 — 트랜스포트 패키지에 번들되어 별도 배치 불필요
- [x] `com.community.netcode.transport.facepunch` 설치 + **NGO 2.x 호환 검증 및 패치**
      (upstream 컴파일 에러 1건 포함, 총 3건 — `PATCHES.md`)
- [x] `steam_appid.txt` (480) 배치
- [x] `SteamLobbyManager` — Init / RunCallbacks / Shutdown
- [x] 로비 생성·참가, 친구 초대 콜백
- [ ] **실제 Steam 접속 확인** — PC 2대 또는 Steam 계정 2개 필요
- [ ] 트랜스포트 스위치로 UTP ↔ Facepunch 전환 확인 (실기)

---

## M4 — 가구 + 타겟팅

> 완료 기준: 가구를 바라보면 윤곽선이 켜지고, 두 클라이언트 모두 서버 물리를 따라 같은 가구를 본다.

- [x] light/heavy 가구 프리팹 (Rigidbody, NetworkObject, NetworkTransform)
- [x] 클라이언트 kinematic 처리
- [x] `DevFurnitureSpawner` — 서버 스폰 + `R` 리스폰
- [x] `Outline` 셰이더 + `FurnitureOutline` 컴포넌트
- [x] `FurnitureTargeter` — 카메라 레이캐스트 타겟팅
- [x] `CrosshairUI`

→ [06-furniture-physics.md](06-furniture-physics.md)

---

## M5 — 던지기 (1인)

> 완료 기준: 혼자 가구를 조준해 투척 준비하고, 마우스를 놓을 때 붕 띄워 벽에 맞힌다. **여기가 프로토타입의 핵심 판정 지점이다.**

- [x] `FurnitureThrowSettings` ScriptableObject
- [x] `FurnitureGrabTarget` — 홀더 슬롯, 상태 NetworkVariable, 서버 검증
- [x] `GrabController` — 홀드 입력, Grab/Release/UpdateAim ServerRpc
- [x] `FurnitureHoverMotor` — 2인 동시 입력에서만 스프링 부양
- [x] `FurnitureLauncher` — 강화된 속도 변화 + 수평 기준 20°~70° 발사각 보정
- [x] `Launched` 상태와 재흡착 잠금
- [x] `ChargeGaugeUI`
- [ ] **플레이테스트 + 파라미터 튜닝** ([05-throw-system.md](05-throw-system.md) 튜닝 순서)

자동 Windows 런타임 스모크 테스트에서 Local Host, 1인 투척 준비/발사와 속도 변화를 확인했다.
손맛 수치의 최종 판정은 수동 플레이테스트가 필요하다.

---

## M6 — 2인 흡착

> 완료 기준: 두 명이 같은 가구에 입력을 유지하는 동안에만 가구를 잡아 움직일 수 있다.

- [x] 홀더 2슬롯 지원, 목표점 중점 계산
- [x] 2인 힘 계산, 방향 평균
- [x] heavy 가구 + `heavySoloMultiplier`
- [x] "1명만 해제" 정책 구현 + `launchOnFirstRelease` 토글
- [x] 홀더 상태 윤곽선 색 (내가/남이/2인)

자동 런타임 스모크 테스트에서 2슬롯 배정, 2인 잡기 진입, 첫 홀더 해제 시
1인 투척 준비 복귀, 마지막 홀더 해제 시 발사를 확인했다. 실제 두 PC의 체감 검증은
M2/M3의 실기 항목과 함께 남아 있다.

---

## M7 — 가구 간 물리 + 정리

> 완료 기준: 던진 가구가 다른 가구를 쳐서 연쇄로 밀려나는 게 두 클라이언트에서 자연스럽게 보인다.

- [ ] 가구 여러 개 배치 후 연쇄 충돌 확인
- [ ] Continuous Dynamic 충돌 검증 (벽 관통 없는지)
- [ ] 네트워크 대역폭 확인 (`NetStatsHUD`)
- [ ] 튜닝 결과를 `docs/05`, `docs/06` 수치에 반영

---

## 원본 요구사항 대응표

| 원본 요구 | 마일스톤 |
|---|---|
| 움직이는 기능, 멀티플레이 동기화 | M1, M2, M3 |
| 벽 1개 배치하기 | M0 |
| 가구같은 큐브 하나 만들어서 던져보기 | M4, M5 |
| 루시우처럼 허공에 뜨게 던지기 | M5 (`minLaunchAngle` / `maxLaunchAngle`) |
| 홀드 방식 | M5 |
| 가구를 바라봐야 됨 / 윤곽선 | M4 |
| 범위 X, 타겟팅 | M4 |
| 혼자 던지기 / 2명 흡착 | M5, M6 |
| 가구 간 물리효과 | M7 |
