# Git 규약

## 1. 브랜치

| 브랜치 | 용도 |
| --- | --- |
| `main` | 항상 컴파일되고 실행 가능한 상태 유지 |
| `feature/<주제>` | 기능 개발 |
| `fix/<주제>` | 버그 수정 |
| `chore/<주제>` | 설정·문서·빌드 등 비기능 |

- **`main` 직접 커밋 MUST NOT.** 사람도 에이전트도 브랜치를 먼저 판다.
  (2026-08-19 결정. 프로토타입 시기에는 허용했으나 본 프로젝트 승격과 함께 닫는다.)
- 브랜치명은 MUST kebab-case: `feature/player-movement`, `chore/docs-restructure`
  — 접두사(`feature/`)도 소문자다. `Feature/Prototype` 같은 PascalCase를 쓰지 않는다.
- 브랜치는 `main`에서 딴다. 머지된 브랜치는 삭제한다.

## 2. 커밋 메시지

```
<type>: <한 줄 요약 (한국어, 명령형)>

<본문 — 왜 바꿨는지. 선택>
```

| type | 용도 |
| --- | --- |
| `feat` | 기능 추가 |
| `fix` | 버그 수정 |
| `refactor` | 동작 변경 없는 구조 개선 |
| `perf` | 성능 개선 |
| `art` | 에셋·리소스 추가/변경 |
| `docs` | 문서 |
| `test` | 테스트 |
| `chore` | 설정, 패키지, 빌드 |

**규칙**
- 한 커밋 = 한 목적. 관련 없는 변경을 섞지 않는다.
- 코드와 그에 딸린 `.meta`는 **같은 커밋**에 넣는다.
- 요약은 50자 이내, 마침표 없음. 넘치면 본문으로 내린다.

예:
```
feat: 문 여닫기를 서버 권위 NetworkVariable로 구현

트랜스폼 복제 대신 열림/닫힘 bool 하나만 복제한다.
늦게 접속한 클라이언트가 회전 연출 없이 현재 상태로 스냅된다.
```

## 3. 커밋하지 않는 것

`.gitignore`가 대부분 처리하지만, 다음은 특히 확인한다.

- `Library/`, `Temp/`, `Logs/`, `UserSettings/`, `Build(s)/`
- `*.csproj`, `*.sln`, `*.slnx`, `*.user`
- 빌드 산출물, 크래시 리포트, MemoryCaptures

**`ProjectSettings/`는 커밋한다.** 물리 설정, 레이어, 태그, 입력 설정, 빌드 씬 목록이 전부 여기 있다.

## 4. `.meta` 파일

- `.meta`는 MUST 커밋한다. 누락되면 다른 사람 환경에서 참조가 깨진다.
- 파일 삭제 시 `.meta`도 함께 삭제한다.
- `git status`에 `.meta`만 단독으로 남아 있으면 삭제 누락 신호다.
- 파일 이동/삭제는 MUST Unity 에디터 안에서 한다 → [unity-assets.md](unity-assets.md)

## 5. LFS · 바이너리

- `.gitattributes`가 Unity 템플릿 기준으로 설정되어 있다.
- 바이너리 에셋(텍스처, 오디오, 모델, DLL)을 추가할 때 해당 확장자가 LFS 규칙에 포함되어 있는지 MUST 확인한다.
  뒤늦게 추가하면 히스토리 재작성이 필요해진다.

## 6. Steam · 벤더링 패키지

| 파일 | 커밋 | 비고 |
| --- | --- | --- |
| `Packages/com.community.netcode.transport.facepunch/**` | ✅ | **벤더링 사본.** 패치 없이는 컴파일되지 않는다 |
| `Packages/manifest.json` · `packages-lock.json` | ✅ | 함께 커밋 |
| `steam_appid.txt` (프로젝트 루트) | ✅ | 개발용 `480`. 실제 배포 빌드에는 포함하지 않는다 |
| `.mcp.json` (Unity MCP 등록) | ✅ | localhost 주소만 담기므로 비밀 정보 없음 |
| 빌드 산출물 옆 `steam_api64.dll` | ❌ | 빌드 결과물이므로 커밋하지 않는다 |

- 벤더링 패키지를 수정하면 MUST
  [`PATCHES.md`](../../Packages/com.community.netcode.transport.facepunch/PATCHES.md)에 기록하고
  `package.json`의 `version` 접미사(`-ghosthunter.N`)를 올린다 → [ADR-0006](../architecture/decisions/ADR-0006-facepunch-transport-embed.md)
- **이 폴더에 우리 기능을 추가하지 않는다.** 패치는 upstream 버그 대응에 한정한다.
- UPM Git 패키지는 **커밋 SHA 또는 태그로 고정**한다. 버전을 올릴 때 커밋 메시지에 명시한다.

## 7. 병합 충돌 (Unity 특유)

- `.unity`, `.prefab` 충돌은 수동 병합이 매우 위험하다.
  1. 같은 씬/프리팹을 동시에 편집하지 않는 것이 1차 방어. 작업 전에 말로 조율하는 게 merge 도구보다 싸다.
  2. 충돌이 나면 한쪽을 통째로 선택(`--ours` / `--theirs`)하고 나머지를 다시 작업한다.
  3. 에이전트는 씬/프리팹 충돌을 **자동 병합하지 않는다.** 사용자에게 알린다.

## 8. 에이전트 규칙

| 동작 | 허용 |
| --- | --- |
| `git status` / `diff` / `log` | ✅ 자유 |
| 브랜치 생성 | ✅ |
| `git add` / `commit` | 사용자가 요청했을 때만 |
| `git push` | ❌ 명시 요청 시에만 |
| `main`에 직접 커밋 | ❌ 금지 (§1) |
| `git reset --hard`, force push, 히스토리 재작성 | ❌ 사용자 승인 필수 |
| 씬/프리팹 충돌 자동 해결 | ❌ |

---

관련: [code-style.md](code-style.md) · [unity-assets.md](unity-assets.md)

최종 갱신: 2026-08-19
