# ADR-0001: Steam P2P + Facepunch.Steamworks를 네트워크 트랜스포트로 사용한다

- **상태**: Accepted
- **날짜**: 2026-08-19 (AlienGhost 프로젝트에서 이관, 내용 유지)
- **결정자**: MinGiHong
- **관련**: [../networking.md](../networking.md), [../steam.md](../steam.md), [ADR-0006](ADR-0006-facepunch-transport-embed.md)

## 배경 (Context)

GhostHunter는 Netcode for GameObjects 2.13.1 기반 멀티플레이 게임이다.
플레이어를 서로 연결하려면 트랜스포트와 매치메이킹 수단을 정해야 한다.

제약:
- Steam으로 배포하는 PC 게임을 전제로 한다.
- 전용 서버를 상시 운영할 인력·비용 계획이 없다.
- 가정용 회선 뒤의 플레이어끼리 연결되어야 한다(NAT 통과 필요).
- 포트포워딩을 요구하는 UX는 채택할 수 없다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| **A. UnityTransport (직접 IP)** | NGO 기본, 설정 불필요, 테스트 쉬움 | NAT 통과 불가. 실사용자 간 연결이 사실상 안 됨 |
| **B. Unity Relay + Lobby** | NAT 통과, 크로스플랫폼, 공식 지원 | 사용량 과금, Unity 계정·서비스 종속, Steam 친구/초대와 별개의 매치메이킹이 생김 |
| **C. Steam P2P + Facepunch.Steamworks** | Valve 릴레이(SDR)로 NAT 통과 무료, Steam 친구·초대·로비를 그대로 사용, Steam 배포와 자연스럽게 통합 | Steam 클라이언트 필수, 테스트 마찰 큼, 트랜스포트가 커뮤니티 유지보수, Steam 외 플랫폼 확장 시 재작업 |
| **D. Steamworks.NET + 자체 트랜스포트** | 성숙한 바인딩, 세밀한 제어 | C API에 가까워 코드량 많음, 트랜스포트를 직접 만들어야 함 |

## 결정 (Decision)

**선택지 C를 채택한다.**

- 트랜스포트: **FacepunchTransport** (Steam Networking Sockets / SDR 릴레이)
- Steam 래퍼: **Facepunch.Steamworks**
- 토폴로지: **Host = 리슨 서버(P2P)**. 데디케이티드 서버는 두지 않는다.
- 매치메이킹: **Steam Lobby** — 게임 내 연결 진입점은 이것 하나뿐이다.
- 개발 중 App ID는 **480 (Spacewar)**, 실제 발급 후 교체한다.

## 근거 (Rationale)

- **비용**: SDR 릴레이는 Steam 게임에 무료로 제공된다. Unity Relay는 동시접속에 비례해 과금된다. 운영 인력이 없는 프로젝트에서 이 차이가 결정적이다.
- **UX**: Steam 친구 목록에서 바로 초대·참가가 된다. 별도 계정·친구 시스템을 만들 필요가 없다 — 만들지 않아도 되는 기능이 가장 싸다.
- **배포 정합성**: Steam 배포가 전제이므로 Steam 클라이언트 의존은 새로 생기는 제약이 아니라 이미 존재하는 제약이다.
- **API 편의**: Facepunch.Steamworks는 async/await와 C# 이디엄을 제공해 Steamworks.NET보다 코드량이 적다. NGO용 트랜스포트도 이미 존재한다(선택지 D 대비 이점).

## 결과 (Consequences)

### 긍정
- NAT 통과·연결 암호화를 직접 다루지 않아도 된다.
- 로비·초대·친구 참가를 Steam 기능으로 대체해 개발 범위가 줄어든다.
- 서버 운영 비용이 0이다.

### 부정 / 감수하는 비용
- **호스트 치팅을 막을 수 없다.** 호스트가 곧 서버다. 경쟁적 랭킹을 도입하려면 이 결정을 재검토해야 한다.
- **호스트 이탈 = 세션 종료.** 호스트 마이그레이션은 지원하지 않는다.
- **호스트 회선이 성능 상한**이다. 인원 확장에 한계가 있다.
- **테스트 마찰이 크다.** PC 2대 + Steam 계정 2개가 있어야 실경로 검증이 된다. CI에서 Steam 경로를 자동 검증할 수 없다.
  → 완화책: 게임 로직이 트랜스포트를 직접 참조하지 못하게 막고, 로직 테스트는 `UnityTransport`로 수행한다.
- **FacepunchTransport는 커뮤니티 유지보수**다. NGO 2.13.1 호환이 깨져 있을 수 있으며, 그 경우 포크해서 직접 관리한다.
- **플랫폼 종속.** 콘솔·모바일 확장 시 트랜스포트 계층을 다시 만들어야 한다.
  → 완화책: `Steamworks` 네임스페이스를 `Systems/Steam` 안에 가둔다.

### 후속 작업
- [x] FacepunchTransport 설치 — 임베드 + 5건 패치 → [ADR-0006](ADR-0006-facepunch-transport-embed.md)
- [x] NGO 2.13.1 호환성 검증 — 컴파일·Host 경로 확인, `package.json` 의존성 표기 정정
- [x] Steam 생명주기 소유권 정리 — 트랜스포트 패치 2(`m_OwnsSteamClient`) → [../steam.md](../steam.md)
- [x] `SteamLobbyManager` 초기화·콜백·종료 구현
- [x] 로비 생성·참가(6자리 방 코드)·친구 초대 구현
- [x] macOS / Apple Silicon 네이티브 지원 — 패치 4
- [x] 빌드 지문 검사로 버전 불일치 접속 차단 (`gh_net_fingerprint`)
- [ ] **실제 Steam 2인 접속 검증** — PC 2대 + Steam 계정 2개 필요 → [PB-08](../../workflow/playbooks.md)
- [ ] 실제 Steam App ID 발급 후 480 교체

## 재검토 조건

아래 중 하나가 발생하면 이 결정을 다시 본다.

- 경쟁 랭킹·전적 등 **호스트 치팅이 실질적 문제**가 되는 기능을 도입할 때
- 동시 인원이 호스트 회선으로 감당 안 되는 규모(대략 8명 초과)로 커질 때
- Steam 외 플랫폼(콘솔·모바일) 출시를 결정할 때
- FacepunchTransport 유지보수가 중단되어 NGO 버전을 따라가지 못할 때
