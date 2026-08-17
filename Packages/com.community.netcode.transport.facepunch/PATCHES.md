# 벤더링 및 패치 기록

이 패키지는 [Multiplayer Community Contributions](https://github.com/Unity-Technologies/multiplayer-community-contributions)의
`Transports/com.community.netcode.transport.facepunch`를 **복사해서 임베드**한 것이다.

- 원본 출처: `https://github.com/Unity-Technologies/multiplayer-community-contributions` (`main` 브랜치)
- 가져온 날짜: 2026-07-26
- 원본 버전: `2.0.0`
- 로컬 버전: `2.0.0-ghosthunter.4`

## 왜 git URL로 설치하지 않고 임베드했는가

Package Manager의 git URL로 설치하면 패키지가 **읽기 전용**이 되어 아래 패치를 적용할 수 없다.
그리고 아래 1번은 패치하지 않으면 **프로젝트 전체가 컴파일되지 않는다.**

임베드의 대가: upstream 업데이트가 자동으로 오지 않는다. 갱신이 필요하면 원본을 다시 받아
이 문서의 패치를 다시 적용한다.

---

## 패치 1 — 짝 없는 `#endregion` 제거 (필수, 컴파일 에러)

**파일:** `Runtime/FacepunchTransport.cs` 끝부분

원본에 `#region`이 3개인데 `#endregion`이 4개다. C#에서 짝 없는 `#endregion`은
**컴파일 에러(CS1028)** 이므로 upstream을 그대로 설치하면 프로젝트가 빌드되지 않는다.

```diff
         #endregion
-
-        #endregion
     }
 }
```

> upstream 버그다. 여유가 되면 MCC 저장소에 PR을 올릴 것.

---

## 패치 2 — SteamClient 수명주기 소유권

**파일:** `Runtime/FacepunchTransport.cs` — `Initialize()`, `Shutdown()`

문제:

- 원본 `Initialize()`는 무조건 `SteamClient.Init()`을 부른다. 그런데 `SteamLobbyManager`가
  부팅 시점에 이미 Steam을 초기화한다 — 로비 생성/참가는 `StartHost()`보다 **먼저** 일어나야
  하기 때문이다. Facepunch는 중복 `Init`에 예외를 던지므로 매번 에러 로그가 찍힌다.
- 원본 `Shutdown()`은 무조건 `SteamClient.Shutdown()`을 부른다. 이러면 네트워크 세션을 끝낼 때
  앱 전체의 Steam이 죽어서, **호스팅 중단 후 다시 호스팅하는 흐름이 깨진다.**

해결: "내가 초기화했을 때만 내가 종료한다"는 소유권 플래그 `m_OwnsSteamClient`를 추가.

```csharp
// Initialize()
if (SteamClient.IsValid) { m_OwnsSteamClient = false; return; }
try { SteamClient.Init(steamAppId, false); m_OwnsSteamClient = true; }

// Shutdown()
if (m_OwnsSteamClient) { SteamClient.Shutdown(); m_OwnsSteamClient = false; m_SteamInitialized = false; }
```

이 프로젝트에서 Steam 수명주기의 소유자는 `SteamLobbyManager`다.

---

## 패치 3 — `package.json` 메타데이터

- `version`: `2.0.0` → `2.0.0-ghosthunter.3` (벤더링 사본임을 명시)
- `dependencies`의 NGO: `1.0.0-pre.4` → `2.13.1` (실제 사용 버전과 일치)

NGO 의존성은 최소 버전 표기라 원본 값으로도 해석은 됐지만, 실제로 이 코드가 NGO 2.x API
(`OnEarlyUpdate`, `InvokeOnTransportEvent`)를 쓰고 있으므로 1.x에서는 컴파일되지 않는다.
표기를 실제와 맞췄다.

---

## 패치 4 — Facepunch.Steamworks 2.5.2 관리/네이티브 파일을 한 세트로 갱신

**파일:** `Runtime/Facepunch/` 아래 공식 2.5.2 Unity 런타임 파일 세트

원본 트랜스포트에 번들된 macOS 바이너리는 **i386 + x86_64뿐이라 arm64가 없었다.**
Apple Silicon 맥에서 arm64 프로세스(=기본 Unity 에디터/빌드)로 실행하면
`DllNotFoundException: libsteam_api`가 나고 `SteamClient.Init`이 실패했다.

처음에는 네이티브 파일만 x86_64 + arm64 유니버설 바이너리로 바꿨지만, 구버전
`Facepunch.Steamworks.MacOS.dll`은 폐기된 `SteamAPI_Init` 엔트리포인트를 계속 호출했다.
그 결과 Apple Silicon에서 다음 오류로 초기화가 다시 실패했다.

```
EntryPointNotFoundException: SteamAPI_Init
```

해결을 위해 공식 Facepunch.Steamworks `2.5.2` 릴리스의 Unity 폴더를 **관리 DLL과
네이티브 라이브러리 모두 함께** 가져왔다.

- Linux/macOS 관리 DLL: `Facepunch.Steamworks.Posix.dll`
- macOS 네이티브: `redistributable_bin/osx/libsteam_api.bundle`
- Windows 관리/네이티브 파일도 같은 2.5.2 릴리스로 동기화
- 기존과 같은 asset GUID는 유지되어 Unity 참조가 끊기지 않음

최신 Posix DLL은 네이티브 파일과 일치하는 `SteamInternal_SteamAPI_Init`을 호출한다.
macOS 네이티브 파일은 x86_64 + arm64 유니버설이며 SHA-256은
`b2260d2b2ff6ac8d2d10770047967ceb18022fc5c27f94e3246bd7d2a1da82c0`이다.

공식 릴리스의 파일명은 `.dylib`이지만, 이 프로젝트의 embedded package에서 Mac Unity
에디터의 Mono P/Invoke가 `libsteam_api`를 해당 파일에 연결하지 못하고 프로젝트 루트만
검색해 `DllNotFoundException`을 냈다. 동일한 바이너리를 기존 `.bundle` 이름과 검증된
PluginImporter 메타로 유지하면 Unity가 플러그인 이름을 정상적으로 매핑한다.

확인:

```bash
lipo -archs Runtime/Facepunch/redistributable_bin/osx/libsteam_api.bundle
# -> x86_64 arm64
```

## 패치 5 — 전송 실패 감지 + 512KB 메시지 상한 등록

**파일:** `Runtime/FacepunchTransport.cs` — `Initialize()`, `Send()`

문제 (둘 다 "신뢰 메시지가 소리 없이 유실되는" 계열이다):

1. 원본 `Send()`는 `Connection.SendMessage()`의 반환값(`Result`)을 버린다. Steam은 단일
   메시지를 512KB(`k_cbMaxSteamNetworkingSocketsMessageSizeSend`)까지만 받아주고 초과분은
   `InvalidParam`으로 거부하며, 송신 버퍼가 가득 차면 `LimitExceeded`를 돌려준다.
   반환값을 버리면 **Reliable 메시지가 유실되어도 로그 한 줄 없다.**
2. NGO는 `ReliableFragmentedSequenced` 메시지(씬 동기화가 대표)를 쪼개지 않고 **한 번의
   `Send`로 통째로** 넘긴다. 상한(`FragmentedMessageMaxSize`) 기본값이 `int.MaxValue`라,
   맵이 커져 씬 동기화 페이로드가 512KB를 넘는 순간 1번과 결합해 **클라이언트가 씬 동기화를
   영영 못 받고 무한 대기**하는 형태로 조용히 깨진다.

해결:

```csharp
// Initialize(): NGO에 Steam의 단일 메시지 상한을 알린다. 초과 메시지는 이제
// 송신 시점에 NGO가 명확한 에러를 낸다.
if (networkManager != null)
    networkManager.MaximumFragmentedMessageSize = MaxSteamMessageSize; // 512 * 1024

// Send(): SendMessage 의 Result 를 확인하고 실패를 LogError 로 드러낸다.
```

> upstream에도 없는 결함이다. 여유가 되면 MCC 저장소에 PR을 올릴 것.

**한계:** 씬 동기화 페이로드가 실제로 512KB를 넘게 되면 에러만 나고 여전히 접속은 안 된다.
그때는 트랜스포트에 청크 분할/재조립을 구현하거나 씬 배치 `NetworkObject` 수를 줄여야 한다.
(현재 Prototype 씬 ~200개 기준 동기화 페이로드는 수십 KB 수준으로 여유가 크다.)

---

## 패치하지 않은 알려진 한계

- **`GetCurrentRtt()`가 항상 0을 반환한다.** 핑 표시가 필요해지면
  `connection.QuickStatus().Ping`으로 구현할 수 있다. 지금은 쓰는 곳이 없어 원본 유지.
- **`SteamClient.RunCallbacks()`가 프레임당 두 번 호출된다.** 트랜스포트의 `OnEarlyUpdate`와
  `SteamLobbyManager.Update`에서 각각. Steam의 `RunCallbacks`는 프레임당 다중 호출이 안전하도록
  설계되어 있어 문제없다. 단일 호출자로 정리하는 건 upstream divergence를 늘릴 뿐이라 하지 않았다.
