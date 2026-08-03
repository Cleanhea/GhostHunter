# 벤더링 및 패치 기록

이 패키지는 [Multiplayer Community Contributions](https://github.com/Unity-Technologies/multiplayer-community-contributions)의
`Transports/com.community.netcode.transport.facepunch`를 **복사해서 임베드**한 것이다.

- 원본 출처: `https://github.com/Unity-Technologies/multiplayer-community-contributions` (`main` 브랜치)
- 가져온 날짜: 2026-07-26
- 원본 버전: `2.0.0`
- 로컬 버전: `2.0.0-ghosthunter.1`

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

- `version`: `2.0.0` → `2.0.0-ghosthunter.1` (벤더링 사본임을 명시)
- `dependencies`의 NGO: `1.0.0-pre.4` → `2.13.1` (실제 사용 버전과 일치)

NGO 의존성은 최소 버전 표기라 원본 값으로도 해석은 됐지만, 실제로 이 코드가 NGO 2.x API
(`OnEarlyUpdate`, `InvokeOnTransportEvent`)를 쓰고 있으므로 1.x에서는 컴파일되지 않는다.
표기를 실제와 맞췄다.

---

## 패치 4 — macOS 네이티브 `steam_api`를 arm64 포함 유니버설로 교체

**파일:** `Runtime/Facepunch/redistributable_bin/osx/libsteam_api.bundle` (바이너리 교체, `.meta` 유지)

원본에 번들된 바이너리는 fat이지만 슬라이스가 **i386 + x86_64뿐이라 arm64가 없었다.**
Apple Silicon 맥에서 arm64 프로세스(=기본 Unity 에디터/빌드)로 실행하면
`DllNotFoundException: libsteam_api`가 나고 `SteamClient.Init`이 실패한다.

Valve는 **Steamworks SDK 1.52부터 `libsteam_api`를 x86_64 + arm64 유니버설로 배포**한다.
그 버전의 바이너리로 교체했다.

```
교체 전: i386, x86_64            (446,352 bytes, md5 fb32124b2e07ed2aae54fe8823d069b3)
교체 후: x86_64, arm64           (414,656 bytes, md5 957bd26d782858fc09e7d3730298ad79)
```

파일명(`libsteam_api.bundle`)과 `.meta`(guid `7d6647fb9d80f5b4f9b2ff1378756bee`,
Editor + OSXUniversal 활성)는 **그대로 유지했다.** Facepunch의 P/Invoke 이름이
`libsteam_api`라 파일명이 바뀌면 안 되고, guid가 바뀌면 참조가 끊긴다.

### 호환성 검증 (매니지드 DLL은 구버전 그대로임)

번들된 `Facepunch.Steamworks.MacOS.dll`은 그대로 두고 네이티브만 올렸다. 심볼 대조 결과:

- 매니지드가 P/Invoke 선언한 심볼 924개 중 **49개가 신규 바이너리에 없다.**
  Valve가 폐기한 인터페이스들이다 — `ISteamAppList`, `ISteamTV`,
  `ISteamGameServer` heartbeat 계열, `ISteamInput` 햅틱/글리프,
  `ISteamNetworkingSockets_GetQuickConnectionStatus` 등.
- P/Invoke는 **호출 시점에 lazy 바인딩**되므로 부르지 않으면 문제가 없다.
  `SteamClient.Init`이 초기화하는 인터페이스 집합에는 `SteamAppList`/`SteamTV`가 없고,
  `FacepunchTransport.GetCurrentRtt()`는 `return 0`이라 `QuickStatus`를 부르지 않는다.
  우리 코드(`SteamLobbyManager` / `ConnectionManager`)도 해당 API를 쓰지 않는다.

> **따라서 위 49개 중 하나라도 쓰게 되면 맥에서 `EntryPointNotFoundException`이 난다.**
> 특히 핑 표시를 `connection.QuickStatus().Ping`으로 구현하려던 계획(아래 "알려진 한계")은
> 이 교체 이후 **맥에서 깨진다.** 그때는 매니지드 DLL까지 최신 Facepunch
> (`Facepunch.Steamworks.Posix.dll`, Linux/macOS 통합본)로 함께 올려야 한다.

## 패치하지 않은 알려진 한계

- **`GetCurrentRtt()`가 항상 0을 반환한다.** 핑 표시가 필요해지면
  `connection.QuickStatus().Ping`으로 구현할 수 있다. 지금은 쓰는 곳이 없어 원본 유지.
- **`SteamClient.RunCallbacks()`가 프레임당 두 번 호출된다.** 트랜스포트의 `OnEarlyUpdate`와
  `SteamLobbyManager.Update`에서 각각. Steam의 `RunCallbacks`는 프레임당 다중 호출이 안전하도록
  설계되어 있어 문제없다. 단일 호출자로 정리하는 건 upstream divergence를 늘릴 뿐이라 하지 않았다.
