# ADR-0006: FacepunchTransport를 임베드하고 5건을 패치한다

- **상태**: Accepted
- **날짜**: 2026-08-19
- **결정자**: MinGiHong
- **관련**: [ADR-0001](ADR-0001-steam-p2p-facepunch-transport.md), [../steam.md](../steam.md), [`PATCHES.md`](../../../Packages/com.community.netcode.transport.facepunch/PATCHES.md)

## 배경 (Context)

`com.community.netcode.transport.facepunch`(MCC)를 UPM Git URL로 설치하면 패키지가 **읽기 전용**이 된다.
그런데 이 패키지는 패치 없이는 쓸 수 없다.

1. **컴파일 불가** — `FacepunchTransport.cs`에 짝 없는 `#endregion`이 하나 있어 설치하는 순간
   프로젝트 전체가 `CS1028`로 컴파일되지 않는다.
2. **Steam 수명주기 충돌** — `Initialize()`가 무조건 `SteamClient.Init()`을 부르고,
   `Shutdown()`이 무조건 `SteamClient.Shutdown()`을 부른다.
   NGO는 `StartHost`/`StartClient` 때마다 `Initialize`를 호출하므로 로비 생성 후 세션을 시작하면
   중복 Init 에러가 찍히고, 세션을 끝내면 **앱 전체의 Steam이 죽어** 로비·친구 기능이 함께 멈춘다.
3. **의존성 표기 오류** — `package.json`은 NGO `1.0.0-pre.4`를 요구한다고 적혀 있으나 실제 코드는
   NGO 2.x API(`OnEarlyUpdate`, `InvokeOnTransportEvent`)를 쓴다. 1.x에서는 오히려 컴파일되지 않는다.
4. **Apple Silicon 미지원** — 번들된 `redistributable_bin/osx/libsteam_api.bundle`이 i386 + x86_64만
   담고 있어 M시리즈 맥에서 `DllNotFoundException`이 난다.
5. **전송 실패 무시** — 메시지 전송 실패를 감지하지 않고, 대형 메시지 상한이 등록되어 있지 않다.

## 검토한 선택지 (Options)

| 선택지 | 장점 | 단점 |
| --- | --- | --- |
| A. `LogLevel` 억제로 중복 Init 로그만 가림 | 패키지를 안 건드림 | **진짜 에러도 함께 묻힌다.** `Shutdown` 문제는 해결 못 함 |
| B. `SteamClient.IsValid` 폴링 후 재초기화 | 패키지를 안 건드림 | Shutdown 직후 구간에서 로비 핸들·콜백이 이미 무효. 증상을 늦게 되돌릴 뿐 |
| C. `StartHost` 직전 `SteamClient.Shutdown()` | 패키지를 안 건드림 | 보유 중인 `Lobby` 핸들과 콜백이 무효화됨. 훨씬 위험 |
| D. 저장소 포크 | 업스트림 병합이 쉬움 | 외부 저장소를 하나 더 관리 |
| E. **임베드 + 패치** | 외부 저장소 불필요. 수정이 프로젝트 커밋에 남고 리뷰된다 | 업스트림 추적이 수동 |
| F. 다른 트랜스포트로 교체 | — | 대안이 **같은 커뮤니티 저장소**라 품질 이득이 없고, 콜백 API라 로비 코드를 전면 재작성해야 함 |

**1번(컴파일 불가) 하나만으로 A·B·C가 전부 탈락한다.** git URL 설치는 읽기 전용이라 고칠 수가 없다.

## 결정 (Decision)

**E를 채택한다.** 패키지를 `Packages/com.community.netcode.transport.facepunch/`로 임베드하고
아래 5건을 패치한다. 전문은 [`PATCHES.md`](../../../Packages/com.community.netcode.transport.facepunch/PATCHES.md).

| # | 내용 |
| --- | --- |
| 1 | 짝 없는 `#endregion` 제거 (필수, 컴파일 에러) |
| 2 | **`m_OwnsSteamClient` 소유권 플래그** — 내가 초기화했을 때만 내가 종료한다 |
| 3 | `package.json` 메타데이터 — `version`에 `-ghosthunter.N` 접미사, NGO 의존성 `2.13.1`로 정정 |
| 4 | Facepunch.Steamworks 2.5.2 관리/네이티브 파일을 한 세트로 갱신 (macOS 유니버설 포함) |
| 5 | 전송 실패 감지 + 512KB 메시지 상한 등록 |

**패치 2가 이 ADR의 핵심이다.** `Initialize()`는 `SteamClient.IsValid`면 아무것도 하지 않고,
`Shutdown()`은 자기가 초기화한 경우에만 `SteamClient.Shutdown()`을 부른다.
이 프로젝트에서 Steam 수명주기의 소유자는 `SteamLobbyManager`다.

## 근거 (Rationale)

**1. 폴링 재초기화(B)보다 근본적이다.** B는 "죽은 뒤 되살린다"이고 패치 2는 "죽지 않게 한다"이다.
`SteamClient.Shutdown()`은 보유 중인 `Lobby` 핸들과 등록된 콜백을 무효화하므로, 되살려도
로비 상태를 복구해야 한다. 애초에 내리지 않는 편이 상태가 적다.

**2. 어차피 패치해야 한다.** 컴파일 에러(패치 1) 때문에 임베드가 강제된다.
임베드한 이상 패치 2를 추가하는 한계 비용은 거의 0이다.

**3. 수정이 리뷰 가능한 곳에 남는다.** 포크(D)는 수정이 다른 저장소로 빠져 PR 리뷰에서 보이지 않는다.
4줄~수십 줄 수정에 저장소를 하나 더 두는 것은 과하다.

## 결과 (Consequences)

### 긍정
- 세션을 끝내고 로비로 돌아와도 Steam이 살아 있다. "호스팅 중단 → 재호스팅" 흐름이 성립한다.
- 중복 Init 에러 로그가 사라져 로그 억제 우회가 불필요해졌다.
- Apple Silicon 맥에서 네이티브로 동작한다. Rosetta·Intel 에디터가 필요 없다.

### 부정 / 감수하는 비용
- **업스트림 업데이트가 자동으로 오지 않는다.** 갱신하려면 원본을 다시 받아 5건을 재적용한다.
- 패키지 폴더가 저장소 크기에 포함된다(네이티브 바이너리 포함).
- **macOS 관리 DLL 이슈**: 2.5.2 세트를 적용했지만 Valve가 폐기한 API 49개가 맥에서만 없다.
  `QuickStatus().Ping` 같은 것을 쓰면 맥에서만 `EntryPointNotFoundException`이 난다.

### 규칙
- 이 폴더에 **우리 기능을 추가하지 않는다.** 패치는 upstream 버그 대응에 한정한다.
- 수정하면 MUST `PATCHES.md`에 기록하고 `package.json`의 `-ghosthunter.N`을 올린다.

### 후속 작업
- [x] 패키지 임베드 + 패치 1~5 적용
- [x] `PATCHES.md` 작성
- [ ] 업스트림 최신 커밋과 diff 확인 주기 정하기
- [ ] macOS 폐기 API 49개 목록을 `PATCHES.md`에 명시

## 재검토 조건

- 업스트림이 위 문제를 모두 고쳐 패치가 0건이 될 때 → git URL 설치로 되돌린다
- NGO 메이저 버전이 올라 트랜스포트 API가 바뀔 때
- 패치 건수가 관리 한계(대략 10건)를 넘어 실질적 포크가 될 때 → D로 전환
