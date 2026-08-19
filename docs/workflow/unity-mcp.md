# Unity MCP

> 에이전트가 Unity 에디터를 직접 조작할 수 있게 하는 브리지.
> **MCP 도구를 쓰기 전 MUST 이 문서를 읽는다.** 특히 §4(바뀌는 규칙)와 §5(안전 규칙).

## 1. 채택한 구현체

**MCP for Unity (CoplayDev)** — `com.coplaydev.unity-mcp`

| 항목 | 값 |
| --- | --- |
| 저장소 | https://github.com/CoplayDev/unity-mcp |
| 버전 | ⚠️ **`#main` 추적 중 — 태그 고정 필요 (§8)** |
| UPM URL | `https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#main` |
| 현재 해석된 커밋 | `8bd91ce7bd3a` (`Library/PackageCache` 기준) |
| 요구사항 | Unity 2021.3+ / Python 3.10+ (uv 경유) |
| 트랜스포트 | HTTP — `http://127.0.0.1:8080/mcp` |
| 도구 | 씬·GameObject·스크립트·에셋·테스트·프로파일·빌드 |

### 왜 공식 Unity MCP가 아닌가

Unity 공식 MCP는 `com.unity.ai.assistant` 패키지에 포함되며
(`Edit > Project Settings > AI > Unity MCP`), **유료 구독과 배정된 시트가 필요**하다.
CoplayDev 구현체는 무료·오픈소스이고 Unity 6.3에서 동작하므로 이쪽을 택했다.

**나중에 Unity AI 구독을 도입하면 공식 MCP로 갈아탈 수 있다** — 그때 이 문서를 갱신한다.
버전을 고정해야 하는 이유는 브리지 업데이트가 예고 없이 동작을 바꾸는 것을 막기 위함이다 → §8.

## 2. 설치 상태

| 단계 | 상태 |
| --- | --- |
| `Packages/manifest.json`에 패키지 추가 | ✅ (이전부터 존재) |
| Unity 에디터 임포트 | ✅ `Library/PackageCache/com.coplaydev.unity-mcp@8bd91ce7bd3a` |
| **버전 태그 고정** | ❌ **`#main` 추적 중** → §8 |
| `.mcp.json` (Claude Code 프로젝트 스코프 등록) | ✅ 2026-08-19 생성, IPv4 고정 |
| 브리지 서버 기동 | ✅ 2026-08-20 |
| Claude Code 세션이 인식 | ✅ 2026-08-20 |
| 프로젝트 MCP 서버 승인 | ✅ 2026-08-20 |

> ⚠️ **MCP 서버가 두 개 등록되어 있다** — 프로젝트 스코프 `unity`(이 저장소의 `.mcp.json`)와
> 사용자 스코프 `UnityMCP`. 둘 다 같은 브리지를 가리키므로 도구가 이중으로 보인다.
> 하나로 정리하는 것을 권한다 → roadmap MIG-10.

### 최초 설치 절차 (참고)

```
1. Unity 에디터로 GhostHunter 프로젝트를 연다
2. 콘솔에 컴파일 에러가 없는지 확인한다
3. Window → MCP for Unity 를 연다
4. 설정 마법사가 Python/uv를 점검한다 (없으면 여기서 설치 안내가 뜬다)
5. "Configure All Detected Clients" (또는 Claude Code 선택 후 Auto-Setup) 실행
6. 서버 상태가 Running(녹색)인지 확인한다. Stopped면 Start를 누른다
7. 에이전트가 처음 접속하면 승인 창이 뜬다 → Accept
8. VS Code의 Claude Code 세션을 재시작한다 (.mcp.json을 새로 읽게 하기 위해)
```

**포트**: 기본 8080. 충돌이 나면 Unity 쪽 설정과 `.mcp.json`의 포트를 **둘 다** 바꿔야 한다.

**주소는 MUST `127.0.0.1`을 쓴다. `localhost`를 쓰지 않는다.**
브리지가 띄우는 서버는 **IPv4 루프백에만 바인드**한다.
Windows에서 `localhost`는 IPv6 `::1`로 먼저 해석되는 경우가 많아 연결이 실패한다.

> HTTP 406은 고장이 아니다. MCP Streamable HTTP는 맨몸 GET을 거부한다.
> **응답이 온다는 것 자체가 서버가 살아 있다는 신호**다.

## 3. 연결 확인

연결되면 세션에 `mcp__unity__*` 형태의 도구가 나타난다.

- 확인 방법: Unity 에디터를 켠 상태에서 에이전트에게 씬 계층 조회 같은 읽기 작업을 시켜 본다.
- 도구가 안 보이면 → §7 트러블슈팅.
- **Unity 에디터가 꺼져 있으면 MCP 도구는 전부 실패한다.** 이는 고장이 아니라 정상이다.

## 4. MCP 도입으로 바뀌는 규칙

이전까지 이 프로젝트는 **"Claude는 Unity 에디터를 실행할 수 없다"**를 전제로 규약을 세웠다.
그 전제가 부분적으로 깨진다.

| 항목 | 이전 | MCP 연결 후 |
| --- | --- | --- |
| 씬 계층 조회 | ❌ 불가 (사용자에게 질문) | ✅ MCP로 직접 조회 |
| GameObject 생성·컴포넌트 추가 | ❌ 사용자 지시로 위임 | ✅ MCP로 수행 (§5 규칙 준수) |
| 인스펙터 값 설정 / 프리팹 배선 | ❌ 사용자 지시로 위임 | ✅ MCP로 수행 |
| 에셋 생성 (SO 인스턴스 등) | ❌ 사용자 지시로 위임 | ✅ MCP로 수행 |
| 컴파일 확인 | ❌ 사용자가 알려줘야 함 | ✅ MCP로 확인 |
| 테스트 실행 | batchmode CLI (에디터 종료 필요) | ✅ MCP로 에디터에서 실행 |
| **`.unity`/`.prefab`/`.asset` 파일 직접 편집** | ❌ 금지 | ❌ **여전히 금지** |
| **`.meta` 파일 편집** | ❌ 금지 | ❌ **여전히 금지** |

**핵심 구분**: MCP는 **Unity API를 경유**하므로 GUID·fileID가 안전하게 유지된다.
파일을 텍스트로 직접 건드리는 것은 여전히 파손 위험이 있어 금지다. **이 구분을 흐리지 않는다.**

### 4.1 에디터 생성 도구와의 관계

이 프로젝트는 `Assets/Scripts/Editor/`에 씬·맵 생성 도구를 두고 있다
(`HousePrototypeBuilder`, `PrototypeSceneSetup`, `MenuScenesSetup`, `NetworkRigSetup`).
**MCP가 이 도구를 대체하지 않는다.**

| 대상 | 수단 | 이유 |
| --- | --- | --- |
| 집 구조, 네트워크 리그, 메뉴 씬처럼 **재현 가능해야 하는 것** | **생성 도구** | 결과가 코드로 남아 리뷰·재실행·검증이 된다 |
| 방별 가구 배치, 일회성 조정, 배선 확인 | **MCP** | 손으로 하는 게 자연스럽고 코드로 남길 이유가 없다 |

생성 도구가 만드는 대상을 MCP로 직접 고치면 **다음 도구 실행에서 덮어써진다.**
도구가 만드는 것은 도구를 고쳐서 바꾼다 → [../conventions/unity-assets.md](../conventions/unity-assets.md)

## 5. 안전 규칙 (MUST)

에디터를 직접 바꾼다는 것은 **되돌리기 어려운 작업을 할 수 있다**는 뜻이다.

1. **씬·프리팹을 변경하기 전에 사용자에게 알린다.** 무엇을 바꿀지 먼저 말하고 실행한다.
2. **저장은 명시적으로.** 변경 후 저장 여부를 반드시 보고한다. "바꿨는데 저장 안 됨" 상태를 남기지 않는다.
3. **플레이 모드 중에는 씬을 수정하지 않는다.** 플레이 모드 변경분은 종료 시 버려진다.
4. **대규모 일괄 변경 금지.** 오브젝트 수십 개를 한 번에 지우거나 옮기는 작업은 사용자 승인 후.
5. **삭제 전 확인.** GameObject·에셋 삭제는 되돌리기 어렵다. 지우기 전에 무엇을 지우는지 보고한다.
6. **커밋되지 않은 변경 위에서 대규모 작업을 하지 않는다.** 작업 전 `git status`로 안전망을 확인한다.
7. **MCP로 스크립트를 쓰지 않는다.** C# 파일 작성·수정은 일반 파일 도구(Read/Edit/Write)로 한다.
   이유: diff 추적, 리뷰 가능성, 파일 도구 쪽이 정확하다.
8. **ProjectSettings 변경은 MCP로도 사용자 승인 대상이다.** 도구가 가능하다는 것이 허가를 뜻하지 않는다.
9. **씬에 `NetworkObject`를 배치했으면** `GlobalObjectIdHash` 확정 절차를 거쳤는지 확인한다
   → [../conventions/unity-assets.md](../conventions/unity-assets.md)

## 6. MCP와 batchmode CLI의 관계

둘은 **상호 배타적**이다. Unity는 프로젝트를 한 프로세스만 잠글 수 있다.

| 상황 | 사용할 것 |
| --- | --- |
| 에디터를 켜고 작업 중 | **MCP** (테스트 실행, 씬 조작, 컴파일 확인) |
| 에디터를 끄고 자동화 | **batchmode CLI** ([../../CLAUDE.md](../../CLAUDE.md)) |
| CI | batchmode CLI — MCP는 CI에서 쓸 수 없다 |

에디터가 켜져 있으면 batchmode 명령은 실패한다. **에디터가 켜져 있을 때는 MCP를 우선 사용**하고,
사용자에게 에디터를 끄라고 요청하기 전에 MCP로 해결되는 일인지 먼저 확인한다.

## 7. 트러블슈팅

| 증상 | 원인 | 조치 |
| --- | --- | --- |
| `mcp__unity__*` 도구가 안 보임 | `.mcp.json`은 **세션 시작 시에만** 읽힌다 | Claude Code 세션 재시작 + 프로젝트 서버 승인 |
| 재시작해도 안 보임 | 프로젝트 MCP 서버 미승인 | `/mcp`로 상태 확인, 승인 프롬프트에서 허용 |
| 연결이 거부되는데 서버는 살아 있음 | `localhost` → IPv6 `::1` 해석 | `.mcp.json`을 `127.0.0.1`로 (§2) |
| 도구는 있는데 전부 실패 | Unity 에디터 꺼짐 / 브리지 Stopped | 에디터 실행, Window → MCP for Unity에서 Start |
| 포트 충돌 | 8080을 다른 앱이 점유 | Unity 설정과 `.mcp.json`의 포트를 **둘 다** 변경 |
| 설정 마법사가 Python을 못 찾음 | PATH 문제 | `uv --version` 확인 후 마법사에서 경로 직접 지정 |
| 패키지 임포트 실패 | git URL 접근 불가 / 리비전 오타 | manifest.json의 URL과 `#` 뒤 리비전 확인 |
| 컴파일 에러 발생 | 브리지와 Unity 6.3 비호환 | 태그를 최신으로 올리거나 이슈 확인 후 이 문서 갱신 |

## 8. 유지보수

> ⚠️ **현재 `#main`을 추적 중이다.** 브리지 업데이트가 예고 없이 동작을 바꿀 수 있다.
> 고정하려면 지금 해석된 커밋으로 박는 것이 가장 안전하다 (FacepunchTransport와 같은 방침):
>
> ```json
> "com.coplaydev.unity-mcp": "https://github.com/CoplayDev/unity-mcp.git?path=/MCPForUnity#8bd91ce7bd3a"
> ```
>
> 태그(`#v10.1.2`)로 고정하면 현재 버전보다 낮아질 수 있으므로, 먼저
> `Window → MCP for Unity`에서 실제 버전을 확인한 뒤 결정한다. → roadmap MIG-10

- 버전은 **태그 또는 커밋으로 고정**한다. `#main` 추적을 유지하지 않는다.
- 업그레이드 시: 태그 변경 → 에디터 재임포트 → 연결 확인 → 이 문서의 버전 표 갱신 → 커밋 메시지에 버전 명시.
- `.mcp.json`은 커밋한다(로컬 주소만 담기므로 비밀 정보 없음).

---

관련: [../../CLAUDE.md](../../CLAUDE.md) · [../conventions/unity-assets.md](../conventions/unity-assets.md) · [testing.md](testing.md) · [development-loop.md](development-loop.md)

최종 갱신: 2026-08-20
