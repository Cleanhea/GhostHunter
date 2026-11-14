# 도둑질 시스템 구현

> 기획: [project/theft-system.md](../project/theft-system.md) (원문 1.0, 2026-11-02 · 사용자 확정 2026-11-12)
> 구현: 2026-11-12 (`feature/theft-system`). 원문에 없는 값은 `TheftSettings`에 **[TEMP]** 로 표시했다.

## 1. 한눈에

```
[소유자] PlayerInteractor ── 조준(레이캐스트) ─→ CurrentValuable ─→ TheftHud "E 훔치기"
            │ E
            ▼
         PlayerTheft.RequestSteal ── RequestStealRpc(id) ──→ [서버] PlayerTheft.ServerTrySteal
                                                              ├ 생존·거리·이미 훔쳐졌는지 검사
                                                              ├ IStageTheftService.ServerTrySteal → 귀중품 숨김·금액 합산
                                                              ├ IStageShopService.ServerAddStolenMoney → 공동 잔액 즉시 +n
                                                              ├ SanityNetworkState.ServerApplyValuableFound → 정신력 +표
                                                              ├ ValuableStolenRpc(NotServer) → 모든 클라이언트 숨김
                                                              └ StealConfirmedRpc(Owner) → TheftHud "+ n$"·테두리 2초
정산: SanityTeamService.BuildSettlement → StageSettlementRecord.StolenMoney → Result "훔친 금액 $n"
```

## 2. 구성 요소

| 파일 | 역할 |
| --- | --- |
| `Gameplay/Theft/ValuablePrice.cs` | 가격 enum — $2·3·5·10만(정신력 금액표와 같은 4종, TH-Q1 확정) |
| `Gameplay/Theft/Valuable.cs` | 씬에 놓는 귀중품. **네트워크 오브젝트가 아니다.** `Id` = 씬 이름 + 시작 위치(mm)의 FNV-1a — 같은 씬 파일에서 모든 피어가 같은 값을 얻는다. `MarkStolen()`은 렌더러·콜라이더를 끈다. [TEMP] 외형은 `_size`로 코드에서 만든 상자 |
| `Gameplay/Theft/IStageTheftService.cs`·`StageTheftService.cs` | 스테이지 씬 하나의 귀중품 상태(훔친 번호·이번 판 훔친 금액). `GameInstaller`가 씬마다 붙이고 등록한다 — 씬과 함께 사라져 판마다 새로 시작 |
| `Gameplay/Theft/PlayerTheft.cs` | Player 프리팹의 `NetworkBehaviour`. 요청 RPC·서버 판정·결과 전파·늦은 피어 동기화(`ServerSyncOwner`) |
| `Gameplay/Theft/TheftSettings.cs` + `Settings/Gameplay/TheftSettings_Default.asset` | 판정 거리·UI 치수·색·시간. 원문 값(132×50, 378/161, `#54d6ad`, 61px, `#c2fcf3`, 2초)과 [TEMP] 값 |
| `UI/TheftHud.cs` | Player 프리팹. 로컬 소유자만 uGUI 캔버스(기준 1920×1080). "E 훔치기"는 우하단 앵커(오른쪽 378·아래 161), "+ n$"는 가운데 앵커에서 61px 아래. 테두리 세기를 전역 셰이더 값으로 넣는다 |
| `UI/TheftRecoveryGlowFeature.cs` + `Shaders/TheftRecoveryGlow.shader` | URP 렌더러 피처(포스트프로세싱 패스, `AfterRenderingPostProcessing`). `PC_Renderer`·`Mobile_Renderer`에 등록. 전역 `_GH_TheftGlowIntensity`가 0이면 패스를 넣지 않는다 |
| `Prefabs/Valuables/Valuable_Temp_{2,3,5,10}Dollar.prefab` + `Materials/M_Valuable_Temp.mat` | 가격별 임시 프리팹(한 종류 = 한 프리팹). 금색 발광 상자 |

## 3. 권위와 동기화

- **서버 권위.** 클라이언트는 귀중품 번호만 보낸다. 서버가 생존(`SanityNetworkState.HasSanity`), 눈(발 + 1.2m)~귀중품 콜라이더 거리 ≤ 3.5m, 아직 안 훔쳤는지를 다시 본다.
- **동시 입력**(TH-Q3 확정 "서버가 판단"): RPC는 서버에서 차례로 처리되므로 먼저 온 요청만 `ServerTrySteal`을 통과하고 나머지는 "이미 훔쳐짐"으로 거절된다.
- **귀중품이 네트워크 오브젝트가 아닌 이유:** 씬 배치 `NetworkObject`는 `GlobalObjectIdHash` 확정 절차가 필요하다(unity-assets.md §5.2). 상태가 "훔쳤다/안 훔쳤다" 하나뿐이라 번호와 플레이어 RPC로 충분하다.
- **번호 충돌:** 같은 자리(1mm 안)에 둘을 두면 번호가 겹쳐 `StageTheftService`가 오류를 남기고 하나를 무시한다. EditMode 배치 검사가 막는다.
- **늦게 붙은 피어·호스트 이전:** `PlayerTheft.OnNetworkSpawn`(서버)과 스냅샷 복원 뒤에 `ServerSyncOwner`가 그 소유자에게 훔친 번호 목록을 보낸다. `StageRecoverySnapshot.StolenValuables`·`StolenMoney`가 호스트 이전 때 함께 옮겨진다(JSON, 옛 스냅샷은 비어 있음).

## 4. 돈·정신력·정산

| 대상 | 처리 |
| --- | --- |
| 공동 잔액 | `IStageShopService.ServerAddStolenMoney(price)` — Steam 방이면 방장이 `ISteamLobbyService.TryAddShopBalance`로 로비 데이터에, 로컬 세션이면 호스트 메모리에 더하고 클라이언트에 보낸다. 훔친 순간 즉시(TH-5). 사망·실종·전멸에도 되돌리지 않는다(TH-Q2 확정) |
| 정신력 | `SanityNetworkState.ServerApplyValuableFound(price)` — 훔친 개인에게만. 표: $2 +3 / $3 +5 / $5 +10 / $10 +15 |
| 정산 | `StageSettlementRecord`에 8번째 칸 `StolenMoney` 추가. 로비 데이터는 8칸으로 쓰고 옛 7칸도 읽는다(훔친 금액 0). Result에 "훔친 금액 $n (공동 잔액에 반영됨)", 인게임 로비 정산 이력 줄에도 표시. 정산은 **다시 더하지 않는다** |

## 5. 회복 테두리 포스트프로세싱

URP 기본 Vignette는 `input * lerp(color, 1, vfactor)`라 화면을 **어둡게만** 할 수 있다. 그래서 전용 패스로 원본을 읽어 테두리 색을 **더한다**(장면을 덮지 않는다).

| 요소 | 규칙 (2026-11-12 사용자 요청 "답답하다·자연스럽게 차오르게·선형으로" 반영) |
| --- | --- |
| 시간 | **선형.** `GlowFillSeconds`(0.5초) 동안 밝기(`GlowIntensityAt`)가 0 → 최대로, 빛 폭(`GlowWidthAt`)이 최대의 35% → 100%로 함께 차오르고, 남은 1.5초 동안 폭은 유지한 채 밝기만 선형으로 빠진다. 총 2초(원문) |
| 모양 | **둥근 외곽 비네팅**(레퍼런스 이미지 1). 화면에 맞춘 타원 거리 `r = |(uv−0.5)·2|`(변 가운데 1, 모서리 1.414). 빛 = `t^2.2`, `t = (r − (1.414 − 폭)) / 폭` — 화면 네 모서리 끝에서만 최대이고 안쪽으로 곡선을 그리며 빠르게 옅어진다. **꽉 차는 영역이 없어** 테두리 선이 생기지 않고, 눈에 띄는 빛은 모서리 쪽 둥근 영역에만 남는다(변 가운데는 은은). 폭이 넓어지면 모서리에서 안쪽으로 번진다(차오름) |
| 합성 | **스크린 블렌드**(`c + g·a·(1−c)`) — 어두운 곳은 비치고 밝은 곳은 하얗게 날아가지 않는다. 8비트 밴딩을 깨는 ±0.5/255 디더 |
| 값 [TEMP] | 최대 폭 0.62(타원 거리 — 변 가운데에는 아주 은은하게), 최대 세기 0.75 |

처음 버전(같은 날)은 0.2초에 확 켜지는 띠(반폭 35%, smoothstep)를 85%까지 덮어 써서 답답했고, 두 번째(둥근 사각형 거리 + 선형 번짐)는 안쪽 경계가 뚜렷했다(사용자 피드백) — 둘 다 테두리 안쪽에 밝기가 꺾이는 선이 생긴 탓이다. 세 번째(슈퍼타원 t², 폭 0.6)는 화면을 너무 많이 차지했고, 네 번째(테두리에 붙은 얇은 빛 테)는 네모나 레퍼런스(원형 외곽 비네팅)와 달랐다(사용자 피드백) ; 다섯 번째(타원 smoothstep, 폭 0.85)는 화면 절반을 덮었고, 여섯 번째(둥근 사각형 좁은 띠)는 모서리 바깥이 꽉 차 렌즈 구멍처럼 테가 끊겨 보였다 — 지금은 모서리 끝에서만 최대인 곡선 비네팅이다. Scene 뷰·프리뷰 카메라에는 그리지 않는다(`CameraType.Game`만).

"+ n$"도 선형 애니메이션을 준다 — 나타날 때 1.3배에서 0.18초에 1배로, 보이는 동안(1.2초 + 사라짐 0.6초) 28px 위로 떠오르며 마지막 0.6초에 사라진다(모두 [TEMP]).

## 6. 임시 배치 ([TEMP] — TH-Q4 배치 미정, 사용자 요청 2026-11-12)

씬 루트 `Valuables_TEMP` 아래 프리팹 인스턴스. 고정 바닥(리지드바디 없음) 위, 벽에서 0.4~0.8m, 가구 스폰 지점에서 떨어진 자리를 프로브로 골랐다.

| 씬 | 이름 | 가격 | 위치 |
| --- | --- | ---: | --- |
| Tutorial | `Valuable_TEMP_Room202_Bath` | $10 | 202호 욕실 타일 (−5.30, 3.574, −1.35) |
| Tutorial | `Valuable_TEMP_Room202_Corner` | $5 | 202호 방 구석 (−1.30, 3.570, −5.75) |
| Tutorial | `Valuable_TEMP_Corridor2F` | $3 | 2층 복도 (3.75, 3.569, 0.25) |
| Tutorial | `Valuable_TEMP_Lot_VendingMachine` | $2 | 1층 자판기 옆 주차장 (−7.40, 0.032, 0.50) |
| Stage1 | `Valuable_TEMP_Kitchen` | $3 | 1층 부엌 (81.67, 0, 2.63) |
| Stage1 | `Valuable_TEMP_Study` | $5 | 1층 서재 (60.67, 0, 11.33) |
| Stage1 | `Valuable_TEMP_FamilyRoom` | $2 | 2층 가족실 (66.98, 3, −8.75) — 침실은 서버가 가구를 옮겨 채워 피했다 |
| Stage1 | `Valuable_TEMP_AtticStorageRoom` | $10 | 다락 창고방 (75.95, 6, −1.65) |

배치가 정해지면 `Valuables_TEMP`를 지우고 레벨 디자인 문서대로 다시 놓는다. 가구 **위**에 두면 가구가 움직일 때 공중에 남는다(귀중품은 가구와 함께 움직이지 않는다) — 숨김 기믹이 가구 안이면 별도 설계가 필요하다(TH-Q4).

## 7. 알려진 한계

- 외형·폰트가 임시다: 코드 상자 + 금색 머티리얼, 기본 폰트 굵게(Noto Sans KR Bold 에셋 없음).
- 도둑질(손) 애니메이션이 없다(TH-Q5) — "+ n$" 자체의 튀어나옴·떠오름·사라짐만 있다([TEMP]).
- 귀중품 콜라이더는 고정 상자라 귀신 런타임 NavMesh에 작은 장애물로 들어간다.
- Steam 2인·실기 화면 검증 전.

## 8. 검증

- EditMode `TheftSystemTests`: 정산 8칸·옛 7칸 읽기, 가격 ⊂ 정신력 금액표, 테두리·가격 시간 곡선, 번호 계산, Player 프리팹 배선·원문 UI 값,
  두 렌더러의 패스·셰이더 등록, Tutorial·Stage1 임시 배치(4개·가격 4종·번호 중복 없음·스케일 1·고정 바닥·겹침 없음).
- PlayMode `TheftFlowTests`: 실제 세션 Tutorial에서 먼 거리 거절 → 조준 시 "E 훔치기" → 요청·확정 → 숨김·잔액 +10·정신력 +15·"+ 10$"·테두리 켜짐 →
  재도둑질 거절 → 2초 뒤 테두리 꺼짐 → 정산 `StolenMoney` 10·잔액은 기본 보상만 추가.
- PlayMode `TheftRecoveryGlowRenderTests`: 카메라 렌더로 세기 1이면 가장자리가 밝아지고(초록·파랑 > 빨강) 안쪽으로 갈수록 옅어지며 가운데는 그대로, 세기 0이면 그대로.
- 화면 확인(자동 렌더): Tutorial 202호 안 카메라로 0~1.9초 7프레임을 찍어 차오름·빠짐·둥근 모서리·장면 비침을 눈으로 확인했다(실제 플레이 화면은 아님).

---

최종 갱신: 2026-11-12 (회복 테두리를 모서리에만 맺히는 둥근 비네팅으로 줄임. 이전: 회복 테두리를 레퍼런스대로 타원 비네팅으로. 이전: 회복 테두리를 얇은 빛 테로 — 지수 감쇠·스크린 블렌드·폭 0.22. 이전: 회복 테두리 경계 제거 — 슈퍼타원 거리·t²·디더. 이전: 회복 테두리 선형 차오름·둥근 테두리·가산 합성, "+ n$" 애니메이션. 이전: 같은 날 최초 구현)
