# 플레이북 — 반복 작업 레시피

> 같은 작업을 두 번 이상 하게 되면 여기에 절차를 추가한다.

---

## PB-01. 새 게임플레이 컴포넌트 추가

1. `docs/project/gdd.md`에서 해당 기능의 규칙·수치를 확인한다. `TBD`면 **중단하고 질문**.
2. 배치 결정: `Assets/Scripts/Gameplay/<기능>/<이름>.cs`
3. 네임스페이스: `GhostHunter.Gameplay.<기능>`
4. 수치가 있으면 SO를 먼저 만든다 → `Assets/Scripts/Data/<기능>Config.cs`
5. 컴포넌트 작성 ([code-style.md](../conventions/code-style.md) 준수)
   - `[SerializeField] private` 필드
   - `Awake`에서 참조 캐싱 + null 검증
   - 이벤트 구독은 `OnEnable`, 해제는 `OnDisable`
6. 컴파일 검증 (MCP 또는 batchmode)
7. SO 에셋 생성·프리팹 배선 — MCP 연결 시 직접 수행, 아니면 **번호 매긴 사용자 지시**로 넘긴다
8. 로드맵 상태 갱신

---

## PB-02. 새 NetworkBehaviour 추가

1. [../architecture/networking.md](../architecture/networking.md) 정독
2. **권위 설계를 먼저 문장으로 적는다**: 무엇을 서버가 정하고, 클라이언트는 무엇을 요청하며, 무엇이 연출인가
3. 배치: 게임 로직이면 `Assets/Scripts/Gameplay/<기능>/`, 연결·세션 관리면 `Assets/Scripts/Networking/`.
   `NetworkBehaviour` 상속. 네임스페이스는 폴더와 1:1
4. 구현 순서
   - `NetworkVariable` 선언 (쓰기 권한 = Server)
   - `OnNetworkSpawn`에서 `IsServer` / `IsOwner` 분기 + 구독
   - `OnNetworkDespawn`에서 구독 해제
   - `[Rpc(SendTo.Server)] RequestXxxRpc` — 진입부에서 검증 (호출자·거리·쿨다운·NaN)
     소유자가 없는 대상(문·가구)이면 `InvokePermission = RpcInvokePermission.Everyone` 추가
   - `[Rpc(SendTo.ClientsAndHost)] NotifyXxxRpc` — 연출/통지
5. 프리팹 루트에 `NetworkObject` 존재 확인 요청
6. **NetworkPrefabsList 등록 요청** (누락 시 접속 실패)
7. **레거시 `[ServerRpc]`/`[ClientRpc]`를 쓰지 않았는지 확인** → [networking.md §3.2](../architecture/networking.md)
8. [networking.md §7 체크리스트](../architecture/networking.md) 전항 확인
9. 트랜스포트·`Steamworks`를 직접 참조하지 않았는지 확인 ([../architecture/steam.md](../architecture/steam.md))
10. Host + Client 2인 수동 검증 지시 작성 → **PB-08**

---

## PB-03. 새 씬 추가

1. [../architecture/overview.md](../architecture/overview.md) §4의 씬 표에 행을 추가한다
2. 씬 생성
   - **MCP 연결됨**: MCP로 씬을 만든다. 만들기 전에 무엇을 만들지 사용자에게 알린다
   - **MCP 미연결**: 사용자에게 지시한다
     ```
     1. Assets/Scenes/ 에서 우클릭 → Create → Scene
     2. 이름: <SceneName>
     3. File → Build Profiles → Scene List에 추가
     ```
3. 계층 정리: `--- Environment ---`, `--- Lighting ---`, `--- Systems ---`, `--- UI ---`
4. **`Assets/Settings/Scenes/SceneNameSO.asset`에 씬 에셋을 등록한다.**
   여기 등록한 씬은 MUST 빌드 씬 목록에도 있어야 한다 → [../conventions/unity-assets.md](../conventions/unity-assets.md)
5. 씬 단위 서비스가 필요하면 `SceneInstaller` 파생 컴포넌트를 배치한다
6. 씬 전환 코드는 MUST `ISceneFlow`를 경유한다. `SceneManager`를 직접 부르지 않는다
   → [ADR-0004](../architecture/decisions/ADR-0004-multi-scene-additive.md)
7. **씬에 `NetworkObject`를 배치했다면** 저장 → 빌드 목록 등록 → 씬 재오픈 → 재저장 순서로
   `GlobalObjectIdHash`를 확정시키고 0/중복이 아닌지 검증한다
   → [../conventions/unity-assets.md](../conventions/unity-assets.md)
8. 문서 갱신

---

## PB-04. 새 입력 액션 추가

1. `Assets/`의 Input Actions 에셋 확인 (없으면 먼저 생성 요청)
2. 사용자에게 액션 추가 지시 (액션 이름, 액션 타입, 바인딩)
3. 생성된 C# 클래스(Generate C# Class 옵션)를 통해 코드에서 구독
4. 구독 `OnEnable` / 해제 `OnDisable` MUST
5. `docs/project/gdd.md §3.1` 조작 표 갱신

---

## PB-05. 패키지 추가

1. **사용자 승인 먼저.** 대안(내장 기능으로 해결 가능한지) 검토 결과를 함께 제시한다
2. 승인 시 `Packages/manifest.json`에 버전 명시로 추가
3. `Packages/packages-lock.json`이 갱신되므로 함께 커밋
4. Unity 재임포트 필요함을 안내
5. 프로젝트 전반에 영향이 크면 [ADR](../architecture/decisions/README.md) 작성
6. `CLAUDE.md §1` 스냅샷 표 갱신

---

## PB-06. 성능 문제 조사

1. **측정 먼저.** 추측으로 최적화하지 않는다
   - Window → Analysis → Profiler
   - 네트워크는 Multiplayer Tools의 Network Profiler
2. 병목 분류: CPU(스크립트/피직스) / GPU(드로우콜·오버드로우) / GC 할당 / 대역폭
3. 흔한 원인 순서로 확인
   - `Update` 안의 `GetComponent`/`Find`/할당
   - 매 프레임 RPC 또는 NetworkVariable 갱신
   - 배칭 안 되는 머티리얼, 과도한 실시간 그림자
4. 수정 후 **같은 방법으로 재측정**해 수치를 보고한다
5. 규약을 의도적으로 어겼다면 ADR 또는 코드 주석으로 이유를 남긴다

---

## PB-07. 문서 갱신 (Scribe)

트리거가 발생하면 즉시 수행한다.

| 트리거 | 갱신 대상 |
| --- | --- |
| 폴더·어셈블리·씬 구조 변경 | `architecture/overview.md` |
| RPC/NetworkVariable 패턴 변경 | `architecture/networking.md` |
| Steam 초기화·로비·App ID·트랜스포트 패치 변경 | `architecture/steam.md` + `Packages/…/PATCHES.md` |
| 밸런스 수치 확정/변경 | `project/gdd.md` 밸런스 표 + 해당 시스템 문서의 초기값 표 |
| 코딩 규약 합의 변경 | `conventions/code-style.md` |
| 태스크 시작/완료 | `project/roadmap.md` |
| 되돌리기 비싼 결정 | `architecture/decisions/ADR-XXXX-*.md` + 목록 |
| 플레이어 조작·입력 매핑 변경 | `architecture/player-controller.md` + `project/gdd.md` 조작 표 |
| 던지기 규칙·상태 머신 변경 | `architecture/throw-system.md` |
| 맵 생성 규칙·방 프리셋 변경 | `architecture/map-generation.md` |
| 새 문서 추가 | `docs/README.md` 인덱스 |

각 문서 하단의 `최종 갱신` 날짜도 함께 바꾼다.

---

## PB-08. Steam 멀티플레이 수동 검증

**전제**: PC 2대, 서로 다른 Steam 계정 2개, 각 PC에 Steam 실행 및 로그인.
단일 PC·단일 계정 검증은 신뢰할 수 없다 → [../architecture/steam.md §6](../architecture/steam.md)

### 준비
1. 두 PC 모두 **같은 커밋**으로 빌드한다. 버전이 다르면 로비 필터에서 걸러진다.
2. 각 빌드 출력 폴더의 실행 파일 옆에 `steam_appid.txt`(`480`)가 있는지 확인한다.
3. 두 계정을 Steam 친구로 등록해 둔다(초대 경로 검증용).

### 검증 시나리오
```
A. 초기화
   1. Steam을 끈 상태로 실행 → 안내가 뜨고 크래시하지 않는가
   2. Steam 실행 후 재시작 → 정상 진입하는가

B. 로비
   3. PC1에서 방 만들기 → 로비 생성 로그 / 로비 UI 표시
   4. PC2에서 로비 목록 → PC1의 방이 보이는가 (남의 게임 방이 섞여 보이면 필터 결함)
   5. PC2 참가 → 양쪽 인원 목록이 일치하는가

C. 연결
   6. 매치 시작 → 양쪽이 Game 씬으로 전환되는가
   7. 이동 동기화 — 양쪽 화면에서 서로의 위치가 따라오는가
   8. 상호작용 동기화 — 클라이언트 행동이 호스트에서 확정되는가

D. 예외
   9. 클라이언트가 나감 → 호스트가 정상 유지되는가
  10. 호스트가 나감 → 클라이언트가 로비로 복귀하는가 (멈추거나 크래시 금지)
  11. 재접속 → 유령 로비가 남지 않았는가

E. 초대 경로
  12. Steam 친구 목록 → 게임 참가 → OnGameLobbyJoinRequested 경로로 참가되는가
```

### 보고
- 통과/실패를 **항목 번호로** 기록한다. 실패 시 양쪽 PC의 로그를 함께 첨부한다.
- 에이전트는 이 절차를 직접 수행할 수 없다. **사용자에게 위 목록을 그대로 전달**하고, 돌아온 결과만 근거로 판단한다.
- 지연·끊김은 SDR 릴레이 경유 여부에 따라 달라진다. 같은 건물 내 2대 결과를 원격 접속 성능으로 일반화하지 않는다.

---

최종 갱신: 2026-08-19
