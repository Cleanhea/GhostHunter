# 네트워킹 규약 (Netcode for GameObjects 2.13.1)

> 네트워크 코드를 작성·수정하기 전 MUST 이 문서를 읽는다.

## 1. 기본 원칙

1. **서버 권위(Server Authoritative)가 기본이다.** 게임 상태의 확정은 서버만 한다.
2. 클라이언트는 **입력을 보내고 결과를 표시**한다. 물리·상태·판정을 클라이언트가 스스로 결정하지 않는다.
3. 클라이언트가 보내는 모든 값은 **신뢰하지 않는다.** 서버에서 범위·쿨다운·소유권·NaN을 검증한다.
4. 시각 효과(파티클, 사운드, 크로스헤어, 게이지)는 클라이언트에서 즉시 재생해도 된다. **상태는 서버 확정을 기다린다.**

**명시적 예외는 하나뿐이다** — 플레이어 이동(§2.3). 그 외에 예외를 만들려면 ADR이 필요하다.

## 2. 토폴로지 — Steam P2P (확정)

| 항목 | 값 |
| --- | --- |
| 방식 | **Host = 리슨 서버(P2P)**. 플레이어 중 1명이 서버 겸 클라이언트 |
| 트랜스포트 | **FacepunchTransport** (Steam Networking Sockets / SDR 릴레이) |
| Steam 래퍼 | **Facepunch.Steamworks 2.5.2** |
| 매치메이킹 | **Steam Lobby** (방 코드 참가 · 친구 초대) |
| 주소 체계 | IP가 아니라 **SteamId**로 연결한다 |
| 데디케이티드 서버 | 사용하지 않음 |
| 최대 인원 | **4명** — Steam Lobby `maxMembers`와 동일 |

근거와 대안 검토: [ADR-0001](decisions/ADR-0001-steam-p2p-facepunch-transport.md)
설치·초기화·로비 규약: [steam.md](steam.md)

### 2.1 Host 권위의 의미

Host는 `IsServer && IsClient`가 **모두 참**이다. 서버 권위 원칙(§1)은 그대로 유지되며,
"서버"는 별도 머신이 아니라 **호스트 플레이어의 프로세스**를 뜻한다.

- 따라서 §1의 "클라이언트를 신뢰하지 않는다"는 **호스트를 제외한** 모든 참가자에 적용된다.
- 호스트 본인의 치팅은 이 구조로 막을 수 없다. **이는 감수하는 비용이며 ADR-0001에 명시되어 있다.**
- 호스트 전용 로직을 `IsOwner`로 판별하지 않는다. 호스트도 자기 플레이어의 Owner이므로 의미가 다르다.
- **호스트는 서버 연산 + 클라이언트 렌더링을 동시에 한다.** 서버 전용 물리가 무거우면 호스트만 프레임이 떨어진다.

### 2.2 가구 물리는 서버 권위다

- **가구 `Rigidbody`는 서버에서만 시뮬레이션된다.** 클라이언트의 가구는 `isKinematic = true`이고
  `NetworkTransform`으로 보간되어 따라온다.
- 클라이언트는 "이 가구를 잡고 싶다 / 이 방향으로 놓겠다"는 **의도만** RPC로 보낸다.
  **클라이언트에서 `Rigidbody`에 직접 힘을 가하지 않는다.**
- 서버 전용 컴포넌트(`FurnitureHoverMotor`, `FurnitureLauncher`)는 `OnNetworkSpawn`에서
  `if (!IsServer) { enabled = false; return; }`로 스스로 꺼진다.

근거·비용: [ADR-0010](decisions/ADR-0010-server-authoritative-furniture-physics.md)

### 2.3 플레이어 이동만 소유자 권위다 (유일한 예외)

- 소유자 클라이언트가 자기 트랜스폼을 직접 쓰고, `ClientNetworkTransform`이 서버 → 다른 클라이언트로 중계한다.
- 이유: 예측·보정 없는 서버 권위 이동은 왕복 지연이 그대로 조작감에 실린다. 1인칭 이동에서 이 체감 손실은
  치팅 방어 이득보다 크다.
- 대가: 이동에 관한 한 클라이언트를 신뢰한다. **위치를 근거로 하는 서버 판정(상호작용 거리 등)은
  서버가 받은 최신 위치로 다시 검증**해서 이 신뢰 범위가 번지지 않게 막는다.
- 재검토 트리거: 경쟁 요소·랭킹·대전 모드 도입 → [ADR-0008](decisions/ADR-0008-owner-authoritative-player-movement.md)

### 2.4 하지 않는 것

- 호스트 마이그레이션을 지원하지 않는다(호스트가 나가면 세션 종료). 변경하려면 ADR이 필요하다.
- Unity Relay / Lobby 서비스를 쓰지 않는다. Steam이 같은 역할을 한다.
- IP/포트 직접 입력 접속 UI를 **제품 UI에** 만들지 않는다. 플레이어에게 노출되는 연결 진입점은 Steam Lobby뿐이다.
  개발용 HUD의 로컬 경로는 §5의 예외 규정을 따른다.

## 3. 코드 규칙

### 3.1 권위 분기는 항상 명시한다

```csharp
public override void OnNetworkSpawn()
{
    if (IsServer) { /* 서버 전용 초기화 */ }
    if (IsOwner)  { /* 로컬 플레이어 전용: 입력·카메라 */ }
    // 그 외: 원격 표현용 초기화
}
```

- `IsServer` / `IsOwner` / `IsClient`를 혼동하지 않는다. Host는 `IsServer && IsClient`가 모두 참이다.
- 서버 전용 컴포넌트는 `if (!IsServer) { enabled = false; return; }`, 로컬 전용(입력/카메라/UI)은
  `if (!IsOwner) { enabled = false; return; }`로 **스스로 꺼진다.** 호출부에서 매번 검사하는 것보다 낫다.
- 권위 분기 없는 상태 변경 코드는 리뷰에서 **Blocker**로 다룬다.

### 3.2 RPC

**Unity 6 / NGO 2.x의 `[Rpc(SendTo.…)]` 통합 속성을 사용한다. 신규 코드에서 레거시
`[ServerRpc]`/`[ClientRpc]`는 MUST NOT.**

```csharp
[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
private void RequestInteractRpc(NetworkObjectReference target, RpcParams rpcParams = default)
{
    // 1) 호출자 검증  2) 거리/쿨다운 검증  3) 상태 변경  4) 필요 시 브로드캐스트
}

[Rpc(SendTo.ClientsAndHost)]
private void NotifyLaunchedRpc(Vector3 position) { /* 연출만 */ }
```

**레거시 → 통합 속성 변환표**

| 레거시 | 통합 |
| --- | --- |
| `[ServerRpc]` | `[Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]` |
| `[ServerRpc(RequireOwnership = false)]` | `[Rpc(SendTo.Server)]` |
| `[ClientRpc]` | `[Rpc(SendTo.ClientsAndHost)]` |
| `[ClientRpc]` + `TargetClientIds = { OwnerClientId }` | `[Rpc(SendTo.Owner)]` — 수동 타겟팅과 `if (IsOwner)` 가드가 둘 다 사라진다 |
| `ClientRpcParams.Send.TargetClientIds` (그 외) | `[Rpc(SendTo.SpecifiedInParams)]` + `RpcTarget.Single(id, RpcTargetUse.Temp)` |
| `ServerRpcParams` / `ClientRpcParams` 파라미터 | `RpcParams` |

> ⚠️ **호출 권한 기본값이 두 체계에서 정반대다.**
> 레거시 `[ServerRpc]`는 `RequireOwnership = true`(소유자만)가 기본인데,
> 통합 `[Rpc]`의 `InvokePermission`은 **`Everyone`이 기본**이다.
> **`[ServerRpc]` → `[Rpc(SendTo.Server)]`로만 바꾸면 권한이 조용히 열린다.**
> 소유자 제한이 필요하면 MUST `InvokePermission = RpcInvokePermission.Owner`를 명시한다.

> 레거시 `ClientRpc`는 서버가 호출하면 **호스트 자신에게도 실행된다.** 정확한 등가는
> `SendTo.ClientsAndHost`다. `SendTo.NotServer`로 바꾸면 동작이 달라진다.

**네이밍**: 메서드 이름은 MUST `Rpc`로 끝난다(NGO 소스 제너레이터 요구사항).
- 서버 요청: `RequestXxxRpc`
- 클라이언트 통지: `NotifyXxxRpc` / `PlayXxxRpc`

**규칙**
- RPC는 **작고 드물게**. 매 프레임 RPC 금지.
- RPC 파라미터는 값 타입·`NetworkObjectReference`·`FixedString`을 쓴다. 일반 `string`/클래스 금지.
- 서버 RPC 진입부에서 MUST 검증한다(호출자 권한, 유효 거리, 쿨다운, 상태 조건, NaN·무한대).
  **클라이언트가 보낸 값을 그대로 물리에 넣지 않는다.**

### 3.3 NetworkVariable

```csharp
private readonly NetworkVariable<bool> _isOpen =
    new(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
```

- 쓰기 권한은 기본 **Server**. Owner 쓰기는 예외적으로만 쓰고 이유를 주석으로 남긴다.
- 자주 변하는 대량 데이터는 NetworkVariable로 올리지 않는다(대역폭). 이벤트/RPC를 검토한다.
- `OnValueChanged` 구독은 `OnNetworkSpawn`에서, 해제는 MUST `OnNetworkDespawn`에서 한다.
- 커스텀 타입은 `INetworkSerializable`을 구현한다.
- **상태를 복제할 수 있으면 트랜스폼을 복제하지 않는다.** 문이 좋은 예다 — 열림/닫힘 `bool` 하나만 보내고
  각 피어가 자기 쪽 문짝을 돌린다. 대역폭이 거의 안 들고 늦게 접속한 클라이언트가 현재 상태로 스냅된다.

### 3.4 스폰 / 디스폰

- 네트워크 프리팹은 MUST `Assets/DefaultNetworkPrefabs.asset`에 등록한다.
  **등록 누락은 에러 없이 조용히 실패한다** — 스폰이 안 될 때 여기부터 본다.
- 스폰은 서버만: `Instantiate` 후 `GetComponent<NetworkObject>().Spawn()`.
- 플레이어 오브젝트는 `NetworkManager`의 Player Prefab으로 처리한다.
- 씬에 미리 배치된 `NetworkObject`(가구·문)는 씬 전환 시 자동 관리된다. 수동 Destroy 금지.
  배치 규칙과 해시 함정: [../conventions/unity-assets.md](../conventions/unity-assets.md)
- **방 프리셋은 스폰이 아니라 이동이다.** 이미 스폰된 가구를 옮기므로 중첩 `NetworkObject`가 생기지 않는다
  → [map-generation.md](map-generation.md)

### 3.5 씬 로딩

- 세션 중에는 MUST `NetworkManager.Singleton.SceneManager.LoadScene(...)`.
- 비네트워크 구간의 씬 전환도 MUST `ISceneFlow`를 경유한다. 어떤 코드도 `SceneManager`를 직접 부르지 않는다
  → [ADR-0004](decisions/ADR-0004-multi-scene-additive.md)
- 클라이언트 동기화 모드는 `LoadSceneMode.Additive`**여야 한다.**
  > ⚠️ **미검증 — 코드가 이 서술을 뒷받침하지 않는다(2026-09-04 확인).** 저장소 어디에서도
  > `NetworkSceneManager.SetClientSynchronizationMode(...)` 를 부르지 않으므로 NGO 기본값
  > **`LoadSceneMode.Single`** 로 동작한다. Single 모드는 클라이언트 동기화 시 이미 로드된 씬을
  > 전부 언로드하므로 **게스트의 `Bootstrap` 이 내려갈 수 있다**(= `NetworkRig`·`Services` 소실).
  > Local 2인(UTP)으로 재현·확인한 뒤, 모드를 명시 호출하거나 이 서술을 고친다
  > → [pause-menu.md §5.5](pause-menu.md), [../project/roadmap.md §5](../project/roadmap.md)
- **NGO가 올린 씬은 MUST `NetworkManager.SceneManager.UnloadScene`으로 내린다.** 로컬로 올린 씬은 각 피어가 직접 내린다.
- additive 전환 중 이전/다음 씬이 공존하므로 `SceneFlowController`가 이전 씬의 `EventSystem`과
  `AudioListener`를 로드 전에 비활성화한다. 전환 시작 실패 시 원상 복구한다.

### 3.6 세션 시작 순서 (MUST)

```
씬 로드 → StartHost → 로비에 시작 신호
```

- 로비 씬에서 바로 `StartHost` 하면 플레이어가 스폰 지점 없는 씬에 스폰된다.
- 신호를 먼저 보내면 게스트가 **세션 없는 호스트**에 접속한다.

### 3.6.1 세션 종료 순서 (MUST)

시작의 역순이다. **세션을 먼저 끊고, 그 다음 씬을 전환한다.**

```
IConnectionService.Disconnect() → (한 프레임 양보) → ISceneFlow.Load(Title)
```

- 세션이 살아 있는 동안 **게스트의 `ISceneFlow.Load` 는 거부된다** — `SceneFlowController` 가
  `useNgo && !IsServer` 를 걸러 경고만 남긴다.
- 세션이 살아 있는 동안 **호스트의 `ISceneFlow.Load` 는 NGO 경로를 타서 게스트까지 끌고 간다.**
  혼자 나가려는 의도라면 반드시 `Disconnect()` 가 먼저다.
- `ConnectionManager.Disconnect()` 는 `NetworkManager.Shutdown()` 뒤에 `ISteamLobbyService.LeaveLobby()`
  까지 부른다. 호출부가 로비 퇴장을 또 부르지 않는다.

연결이 끊겼을 때 플레이어에게 무엇을 보여주고 어디로 보낼지는
[pause-menu.md](pause-menu.md) 와 [../project/pause-menu-system.md §5](../project/pause-menu-system.md) 가 정한다.
UI 는 `NetworkManager` 콜백을 직접 구독하지 않고 **`IConnectionService` 를 통해서만** 세션 종료를 안다.

### 3.7 소유권

- 소유권 이전은 서버에서 `NetworkObject.ChangeOwnership(clientId)`.
- 소유권에 의존하는 로직은 `OnOwnershipChanged`를 처리해 상태를 재설정한다.
- **문·가구처럼 아무도 소유하지 않는 객체**는 소유권 대신 **요청자와 대상 사이 거리**로 검증한다.

## 4. 성능 가이드

| 항목 | 기준 |
| --- | --- |
| Tick Rate | NGO 기본값 사용. 변경 시 이 표와 ADR 갱신 |
| 위치 동기화 | `NetworkTransform` 사용, 보간 활성, 불필요 축 동기화 해제 |
| 플레이어 회전 | **Y만** 동기화. 피치는 로컬 카메라 전용 |
| RPC 빈도 | 입력 요청은 이벤트 기반. 지속 입력(조준 방향)은 샘플링 후 전송 |
| 대역폭 확인 | Multiplayer Tools의 Network Profiler |

**Steam 고유 고려사항**
- Steam 연결은 직결이 안 되면 **SDR 릴레이**를 경유한다. 지연이 LAN 테스트보다 크고 변동이 있다.
  → **LAN·로컬 기준으로 타이밍을 튜닝하지 않는다.** 반드시 실제 원격 접속에서 재확인한다.
  이는 §2.2 서버 권위 물리의 체감에 직접 영향을 준다.
- 업로드 대역폭이 **호스트 플레이어의 가정용 회선**에 묶인다. 인원수 × 초당 전송량을 보수적으로 잡는다.
- 잠든 `Rigidbody`는 전송하지 않지만, 가구가 동시에 여러 개 깨어나면 `NetworkTransform` 트래픽이 몰린다.

## 5. 로컬(UnityTransport) 경로

`NetworkManager`·두 트랜스포트·`ConnectionManager`는 언로드되지 않는
`Bootstrap/NetworkRig` 씬 오브젝트에 함께 있다. `NetworkRig`는 프리팹이 아니며,
`Title`·`Lobby`·`Game`이 별도 리그를 생성하지 않는다. 씬을 넘는 소비자는
`BootstrapInstaller`가 등록한 `IConnectionService`로 접속 기능을 받는다.

`ConnectionManager`가 `TransportMode.Local`(UTP, 127.0.0.1) / `TransportMode.Steam`(Facepunch)을
런타임에 전환한다. **로컬 경로는 개발 전용이며 런타임에 그대로 남는다**
→ [ADR-0011](decisions/ADR-0011-local-transport-path.md)

`ConnectionManager`는 `GhostHunter.Networking` 어셈블리에 있고 직렬화 필드도 `NetworkTransport` 기반 타입이다.
Facepunch 고유 `targetSteamId` 설정은 `ISteamLobbyService.TrySetConnectionTarget`으로 위임하므로,
구체 트랜스포트 참조는 `GhostHunter.Systems/SteamLobbyManager` 안에만 남는다.

존재 이유: 같은 Steam 계정으로는 두 인스턴스를 P2P 연결할 수 없다. SteamId 가 같아 자기 자신에게
연결하는 꼴이 된다. 실제 2인 검증에는 PC 2대 + 계정 2개가 필요하므로, 일상 로직 검증은 UTP 로 한다.

**MUST**
- **게임 로직은 트랜스포트 구현을 직접 참조하지 않는다.** `FacepunchTransport`/`UnityTransport`/`SteamClient`
  타입이 `Gameplay`·`UI`에 등장하면 안 된다 → [steam.md](steam.md)
- 로컬 경로는 **개발용 HUD(F1) 뒤에** 둔다. 제품 메뉴(`Title`/`Lobby`)에 노출하지 않는다.
- 트랜스포트 전환은 **세션 정지 중에만** 허용한다.
- **리그의 직렬화된 기본값은 항상 `Steam`이다.** 로컬 검증은 플레이 중 HUD 로 전환하고 저장하지 않는다.

### 5.1 릴리스 빌드 가드

`_transportMode`는 직렬화 값이라, 로컬 검증 중에 저장·커밋하면 아무 신호 없이 빌드에 실린다.
증상이 크래시가 아니라 **"Steam 로비는 뜨는데 아무도 접속하지 못함"** 이라 원인을 찾기 어렵다.

`Assets/Scripts/Editor/TransportModeBuildGuard.cs`가 릴리스 빌드에서 모든 프리팹과 빌드 씬의
`ConnectionManager`를 검사하고, `Steam`이 아니면 `BuildFailedException`으로 빌드를 중단한다.
개발 빌드는 면제된다.

> 이 가드는 실제로 필요했다. 도입 시점에 `NetworkRig.prefab`이 이미 Local 로 커밋되어 있었다.

## 6. 테스트

- 네트워크 로직은 PlayMode 테스트에서 `NetworkManager`를 코드로 구동해 검증한다. 상세: [../workflow/testing.md](../workflow/testing.md)
- **자동화 테스트는 Steam에 의존하지 않게 만든다.** Steam 클라이언트가 없는 환경(CI, batchmode)에서
  FacepunchTransport는 초기화에 실패한다. 테스트에서는 `UnityTransport` 또는 인메모리 구성으로 대체한다.
- 수동 2인 검증은 Steam 계정·PC 제약을 받는다. 절차: [../workflow/playbooks.md PB-08](../workflow/playbooks.md)

## 7. 체크리스트 (네트워크 PR 리뷰)

- [ ] 모든 상태 변경이 서버에서 일어나는가 (§2.3 예외 제외)
- [ ] 서버 RPC가 호출자 권한과 조건(거리·쿨다운·NaN)을 검증하는가
- [ ] RPC가 통합 속성 `[Rpc(SendTo.…)]`을 쓰고 이름이 `Rpc`로 끝나는가
- [ ] `OnNetworkDespawn`에서 구독/비동기 작업(UniTask)/타이머를 정리하는가
- [ ] 새 네트워크 프리팹이 `DefaultNetworkPrefabs.asset`에 등록되었는가
- [ ] 씬 배치 `NetworkObject`의 `GlobalObjectIdHash`가 0이 아니고 중복이 없는가
- [ ] Host(서버+클라 동시)에서도 정상 동작하는가
- [ ] 게임 로직이 `FacepunchTransport`/`SteamClient`를 직접 참조하지 않는가
- [ ] 씬 전환이 `ISceneFlow`를 경유하는가
- [ ] 세션을 떠나는 경로가 §3.6.1 순서(끊기 → 전환)를 지키는가
- [ ] Steam 미실행·로비 이탈·호스트 종료 상황을 처리하는가

---

관련: [overview.md](overview.md) · [steam.md](steam.md) · [pause-menu.md](pause-menu.md) ·
[../conventions/code-style.md](../conventions/code-style.md)

최종 갱신: 2026-09-04 (§3.6.1 세션 종료 순서 신설, §3.5 클라이언트 동기화 모드 서술이 코드와 어긋남을 ⚠️ 표시. 이전: 2026-08-20)
