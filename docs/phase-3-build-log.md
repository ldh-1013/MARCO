# Phase 3 빌드 로그 — 마르코! (MARCO!)

> 태스크 기준: `docs/phase-1-분석.md` §3 (M3 태스크 분해 T1~T25)
> Dev↔QA 루프: 태스크 구현 → 컴파일 검증 → 유닛 테스트 → 통과 시 다음 태스크
> 검증 방식: 스크래치패드의 glob 기반 `CoreVerify.csproj`(Unity 6000.5.4f1 관리 DLL 참조) + 리플렉션 테스트 러너

---

## 스프린트 1 (5주차 — 코어 루프 골격)

### 진행 상태 보드

| # | 태스크 | 상태 | QA 결과 |
|---|---|---|---|
| T1 | GameFlowManager 상태기계 | ✅ Phase 2에서 선완료 | 9케이스 통과 |
| T2 | 맵 그레이박스 (ProBuilder, §10.1 8구역) | ⛔ **블로킹** — 아래 참조 | — |
| T3 | 밸브 상태기계 (§6.1) | ✅ 완료 (2026-07-19) | 17케이스 통과 |
| T4 | 승패 판정 (§6.3) | ✅ 완료 (2026-07-19) | 11케이스 통과 |

**테스트 누계: 56케이스 전수 통과** (SoundPulseResolver 19 + GameFlow 9 + Valve 17 + WinCondition 11)

---

### T3 — 밸브 상태기계

**신규 파일**
- `Assets/_Project/Core/Objectives/ValveState.cs` — `Closed / Rotating / Open`
- `Assets/_Project/Core/Objectives/Valve.cs` — 순수 로직 상태기계
- `Assets/_Project/Tests/EditMode/ValveTests.cs` — 17케이스

**구현 결정 (기획서 §6.1 / §6.4 / GAP-5 반영)**

| 규칙 | 출처 | 구현 |
|---|---|---|
| 3초 홀드 → Open (4인 MVP) | §6.2 | `DefaultRotationSeconds = 3f`, `Tick(dt)` 누적 |
| 6인 3.75초는 상수만 준비 | §6.2 v1.x | `SixPlayerRotationSeconds = 3.75f` + 생성자 주입 |
| 완료 전 중단 → 진행도 0 리셋 | §6.1 | `Interrupt()`가 `_elapsedSeconds = 0` |
| 소음은 시작 시 1회, 중단해도 취소 안 됨 | §6.1 | `RotationStarted` 이벤트만 발행, 중단 시 무발행 → 재시작하면 다시 발행 |
| 연결 끊김 = 이탈과 동일 | §6.4 | 별도 API 없음 — Net 레이어가 이탈자 ID로 `Interrupt()` 호출 |
| 밸브 잠금 없음 — 즉시 이어받기 가능 | §6.4 | `Interrupt` 후 `TryBeginRotation` 즉시 허용(진행도는 0부터) |
| 메아리는 밸브 조작 불가 | GAP-5 | `TryBeginRotation`이 `RoleType.Echo`를 거부 |
| 제3자가 남의 회전을 중단 불가 | 안전장치 | `Interrupt`는 `InteractorId` 본인만 허용 |
| 회전 중 가로채기 불가 | §6.1 상태기계 | `Rotating`에서 `TryBeginRotation` 거부 |

소음 발생(§5.1 Valve 12m)과 네트워크 동기화는 이벤트(`RotationStarted`/`Opened`)를 구독하는 Net 레이어 책임으로 분리 — SoundPulseResolver와 같은 격리 원칙.

### T4 — 승패 판정

**신규 파일**
- `Assets/_Project/Core/Objectives/RoundResult.cs` — `InProgress / RunnersWin / SeekerWin`
- `Assets/_Project/Core/Objectives/WinConditionEvaluator.cs` — §6.3 의사코드의 순수 함수 구현
- `Assets/_Project/Tests/EditMode/WinConditionEvaluatorTests.cs` — 11케이스

**구현 결정**

| 결정 | 근거 |
|---|---|
| 러너 승리 조건을 먼저 판정 | §6.3 의사코드의 if/else 순서 그대로. 탈출+시간초과가 같은 프레임에 겹치면 러너 승 — 탈출은 이미 달성된 결과라 취소 불가 |
| `valvesOpened >= totalValves` (기획서는 `==`) | 초과 상태가 생겨도 승리를 놓치지 않도록 방어. 동작 의미는 동일 |
| 시간 음수 오버슛도 술래 승 | `timeRemaining <= 0` 그대로 |
| T3↔T4 통합 테스트 1건 포함 | Valve 3개를 실제로 돌려 Open 카운트로 판정까지 연결되는지 확인 |

### QA 기록 — 이번 스프린트에서 잡은 프로세스 결함

**stale csproj 함정.** Unity가 생성한 `Core.Tests.csproj`는 소스 파일을 명시적으로 나열하는데, Unity 에디터가 임포트를 다시 돌리기 전까지는 새 .cs 파일이 반영되지 않는다. 첫 QA에서 `dotnet build`가 0.7초 만에 "성공"했지만 실제로는 **밸브 코드가 아예 컴파일 대상에 없는 헛빌드**였다. 빌드 시간이 비정상적으로 짧은 것을 보고 `grep`으로 확인해 발견.

**해결**: 스크래치패드에 `CoreVerify.csproj`를 새로 만들어 `Assets/_Project/Core/**/*.cs` + `Tests/EditMode/**/*.cs`를 glob으로 잡게 했다. Unity 생성물을 수정하지 않으므로 (Unity가 언제 재생성해도) 충돌이 없고, 항상 디스크의 현재 소스 전체를 컴파일한다. 이후 DLL 바이너리에서 신규 타입명(`Valve`, `TryBeginRotation` 등)을 직접 확인해 glob 동작을 재검증했다.

**meta 파일 선제 생성.** Phase 2에서 겪은 "백그라운드 생성기가 1줄짜리 불완전 meta를 만드는" 문제를 피하기 위해, 이번에는 신규 파일 6개 + 폴더 1개의 meta를 완전한 `MonoImporter`/`DefaultImporter` 형식으로 직접 생성했다. 생성 후 전체 검사: GUID 1186개 중복 0, meta 누락 0, 불완전 meta 0.

---

### ⛔ T2 블로킹 보고 — 맵 그레이박스

T2(§10.1 8구역 그레이박스)는 **ProBuilder 메시 에셋을 Unity 에디터 밖에서 만들 수 없어** 이 환경에서는 진행 불가.

- ProBuilder 메시는 씬 YAML에 직렬화된 컴포넌트 데이터 + 에디터 생성 메시라, 텍스트 편집으로 만들면 임포트가 깨질 위험이 높다.
- 대안 두 가지:
  1. **(권장)** Unity 에디터에서 MCP(unity-mcp)를 연결한 상태로 세션을 열면 `manage_probuilder` 도구로 §10.1 표 그대로 8구역을 생성할 수 있다.
  2. 사용자가 ProBuilder로 직접 블로킹하고, 구역 치수(§10.1)와 `SoundBlocking` 레이어 태깅 규칙(§5.6 표)만 이 문서 기준으로 맞춘다.
- **T2가 막혀도 6주차(T5~T8 사운드 코어)의 로직 절반은 진행 가능** — T5는 이미 완료됐고, T7(PerceivedPulse 프로토콜)도 순수 로직이다. 다만 T6(재판정 주기 실측)과 T8(파문 렌더러)은 실제 씬·프로파일러가 필요해 에디터 세션을 요구한다.

---

## 다음 스프린트 후보 (6주차 — 사운드 코어)

| # | 태스크 | 에디터 필요? |
|---|---|---|
| T5 | SoundPulseResolver + 테스트 | ✅ 완료 (Phase 2) |
| T6 | 차폐 재판정 0.25s 주기 실측 | ⚠ 에디터 프로파일러 필요 |
| T7 | PerceivedPulse 재판정 오케스트레이션 (Core 로직) | ❌ 순수 로직 — 진행 가능 |
| T8 | 파문 렌더러 (셰이더 + 30개 @60fps) | ⚠ 에디터 필요 |

에디터 없이 진행 가능한 다음 작업: **T7** — 펄스 수명주기 관리(0.25초 간격 재판정 루프를 도는 `ActivePulseTracker` 같은 Core 클래스)와 그 유닛 테스트.
