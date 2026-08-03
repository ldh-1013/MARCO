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
| T2 | 맵 그레이박스 (§10.1 8구역) | ✅ 완료 (2026-07-19, 큐브 방식으로 우회) | 구조 검증 전항 통과 |
| T3 | 밸브 상태기계 (§6.1) | ✅ 완료 (2026-07-19) | 17케이스 통과 |
| T4 | 승패 판정 (§6.3) | ✅ 완료 (2026-07-19) | 11케이스 통과 |
| T7 | PerceivedPulse 재판정 오케스트레이션 (GAP-3) | ✅ 완료 (2026-07-19) | 12케이스 통과 |

**테스트 누계: 186케이스 전수 통과** (SoundPulseResolver 19 + GameFlow 9 + Valve 17 + WinCondition 11 + ActivePulseTracker 12 + LocomotionSimulator 18 + LocalPulsePipeline 10 + LocalPulsePipelineExpiry 6 + PulseVisualRegistry 14 + ValveInteractionController 15 + RoundFlow 18 + Tagging 21 + LocalControlGate 6 + PlayerIdentity 10)

---

## 스프린트 9 — 발생원 ID 하드코딩 제거 (네트워크 2단계 선행, 2026-07-19)

지시서: `마르코_스프린트9_발생원ID_prompt.md`. 스프린트 8 완료 보고에서 직접 지적한 선행 조건. 발소리·밸브·태그·탈출이 발생원을 상수 `1`로 식별하던 것을, 실제 네트워크 소유자 ID로 교체 가능한 구조로 바꿨다. **네트워크 전파 자체는 구현하지 않는다** — "누가 발생시켰는가" 식별까지만.

### 하드코딩 ID 전수 조사

| 위치 | 하드코딩 | 처리 |
|---|---|---|
| `LocalPulsePipelineBehaviour.cs` | `const LocalSourceId = 1` | 제거 → 플레이어 `PlayerId` 읽음 |
| `ValveInteractor.cs` | `const LocalPlayerId = 1` | 제거 → `_player.PlayerId` |
| `EscapePointTrigger.cs` | `const LocalPlayerId = 1` | 제거 → `_player.PlayerId` |
| `TaggableRunner.cs` | `_playerId = 100`(인스펙터, 씬 101~103) | **유지** — 대역 러너의 안정적 로컬 ID(주석 보강). 2단계에서 OwnerId로 대체 |

로컬 플레이어 ID `1`이 **3개 파일에 각각 별도 const**로 흩어져 있던 게 핵심 문제였다 — 값을 바꾸려면 세 곳을 동시에 고쳐야 했고, 네트워크 시 전원이 같은 1을 참조했다.

### Core 인터페이스 신설 (GAP-14와 같은 패턴)

`Core/Net/IPlayerIdentity.cs` — `ulong PlayerId { get; }` + `SetPlayerId(ulong)`. `ILocalControlGate`(스프린트 8)와 완전히 같은 원리: 실제 소유자 ID는 FishNet(Net)이 알고 소비자는 Presentation에 있는데 §15.2상 서로 참조 못 하므로, Core에 계약만 두고 `GetComponentsInChildren`로 연결한다.

### FishNet API 확인 (추측 없음)

벤더링 소스에서 직접: `NetworkBehaviour.OwnerId`(QOL.cs:194)는 **`int`**, 소유자 없으면 `-1`(NetworkObject.QOL.cs:195). 파이프라인 전체가 `ulong`을 쓰므로, `PlayerOwnershipGate`가 **소유권 확정된 값(OwnerId ≥ 0)만 `(ulong)`로 캐스팅**해 전달한다.

### 스펙 갭 1건 신규 (GAP-15)

| # | 갭 | 결정 | 근거 |
|---|---|---|---|
| **GAP-15** | 폴백값을 무엇으로, ID 타입을 무엇으로 | 폴백 = **1**(기존 하드코딩과 동일), 타입 = **ulong** | ① 지시서 명령: 폴백을 기존 값과 동일하게 둬 로컬 워크플로우 완전 보존. ② Core 파이프라인 전체가 이미 `ulong`(SoundPulse.SourcePlayerId·Valve 조작자·RoundOutcomeTracker 집합). FishNet의 int→ulong 변환을 Net 경계에서 1회 수행 |

폴백값 `1`은 `FirstPersonController.LocalFallbackPlayerId` 상수로 한 곳에 모았고, 흩어져 있던 3개 const를 이 하나로 대체했다. `LocalPulsePipeline`(순수 클래스)만 Presentation 상수를 참조할 수 없어 초기값 1을 직접 갖지만, **두 값이 같아야 함을 테스트가 고정**한다(`LocalFallbackId_IsExactlyOne`).

### 신규·수정 파일

| 계층 | 파일 | 내용 |
|---|---|---|
| Core (신규) | `Net/IPlayerIdentity.cs` | `PlayerId`/`SetPlayerId` 계약 |
| Presentation | `Player/FirstPersonController.cs` | `IPlayerIdentity` 구현. `LocalFallbackPlayerId=1` 상수, `PlayerId` 기본 폴백 |
| Presentation | `SoundPulse/LocalPulsePipeline(.Behaviour).cs` | const 제거, `SourcePlayerId` 세터 + 바인딩 시 플레이어 ID 반영 |
| Presentation | `Objectives/ValveInteractor.cs`·`EscapePointTrigger.cs` | const 제거 → `_player.PlayerId` |
| Presentation | `Tagging/TaggableRunner.cs` | 주석만 보강(로컬 대역 ID 유지) |
| Net | `PlayerOwnershipGate.cs` | `IPlayerIdentity`도 찾아 `(ulong)OwnerId` 전달 |
| 테스트 (신규) | `Tests/EditMode/PlayerIdentityTests.cs` | 10케이스 |

### 검증

**186케이스 전수 통과**(기존 176 무손상 + 신규 10). Core·**Net 어셈블리 재컴파일** 에러 0. 씬 미변경(인스펙터 옛 `_playerId` 필드가 남아 있어도 무해 — 런타임에 무시되거나 대역 러너용으로 유지). 테스트는 폴백값 정확성(=1)·인터페이스 경유·OwnerId 캐스팅 값 보존·발소리 파이프라인이 실제로 그 ID로 GAP-1 본인 판정하는지까지 확인.

### 2단계 착수 조건 — 이제 충족됨

발소리·밸브·태그·탈출이 전부 `IPlayerIdentity`에서 발생원을 읽으므로, 2단계에서 실제 원격 플레이어가 접속하면 각자 다른 OwnerId로 이벤트를 구분할 수 있다. 남은 것은 **이벤트 전파 자체**(현재 로컬 스모크 리그로만 도는 것을 서버 권위 RPC로 올리는 것)와 **대역 러너를 실제 원격 플레이어로 교체**(스프린트 8 프리팹 자산 작업 완료 후).

---

## 스프린트 8 — 네트워크 동기화 1단계: Transform 동기화 (2026-07-19)

지시서: `마르코_스프린트8_네트워크Transform_prompt.md`. 3단계 분할 중 1단계 — **실제 원격 플레이어가 서로 움직이는 걸 보는 것**까지. 발소리·밸브·태그·라운드의 네트워크 전파는 2단계, 권위 모델은 3단계로 이월.

이번 세션은 **코드 계층(Step 1~3)만 구현·검증**하고, 에디터에서만 안전하게 되는 자산 작업(프리팹화·FishNet 컴포넌트 부착·스폰 배선)은 **`수동검증_절차.md §10`에 GUI 절차서로 넘겼다**(사용자 결정 (B)).

### 사전 정리 — CS0618 (직전 턴 완료)

지시서 §0의 `FindObjectsByType<T>(FindObjectsSortMode)` → `FindObjectsByType<T>()` 치환 4곳은 직전 턴에 완료했다. 경고 8→0, 순서 의존 없음 확인, 회귀 없음.

### 핵심 설계 문제와 해결 (GAP-14)

"내 캐릭터가 아니면 입력을 막아야" 하는데, 이 판단은 **FishNet 개념(Net)**이고 입력 처리는 **`FirstPersonController`(Presentation)**에 있다. 그런데 §15.2상 Net과 Presentation은 서로를 참조하지 않는다.

**결정 (GAP-14)**: `IOcclusionProbe`가 Core와 Physics를 갈라놨던 것과 같은 원리를 Net-Presentation 사이에도 적용한다 — **Core에 최소 인터페이스 `ILocalControlGate`를 두고, 양쪽이 그것만 통해 연결**한다. Net은 `GetComponentsInChildren<ILocalControlGate>()`로 구체 타입(`FirstPersonController`)을 전혀 모르는 채 소유권을 전달한다.

### 신규·수정 파일

| 계층 | 파일 | 내용 |
|---|---|---|
| Core (신규) | `Core/Net/ILocalControlGate.cs` | `SetLocalControl(bool)` 단일 멤버. using 없는 순수 계약 |
| Presentation (수정) | `Player/FirstPersonController.cs` | 인터페이스 구현. 원격이면 `Update` 조기 반환 + 카메라·AudioListener·커서락 해제. **기본값 = 로컬 조종**(네트워크 없는 실행 보존) |
| Presentation (신규) | `Player/LocalPlayerRegistry.cs` | 지연 바인딩(아래) |
| Net (신규) | `Net/PlayerOwnershipGate.cs` | `NetworkBehaviour`. `OnStartClient`/`OnOwnershipClient`에서 `IsOwner`를 게이트에 전달 |
| 테스트 (신규) | `Tests/EditMode/LocalControlGateTests.cs` | 6케이스 |
| 하네스 (신규) | `scratchpad/NetVerify.csproj` | **Net 어셈블리 최초 컴파일 검증** |

### 부차 설계 문제와 해결 — 지연 바인딩

플레이어가 씬 고정 오브젝트에서 **네트워크 스폰 프리팹**으로 바뀌면, 씬에 미리 놓인 컴포넌트 3개(`LocalPulsePipelineBehaviour`·`EscapePointTrigger`·`PulseVisualRenderer`)의 인스펙터 참조가 끊어진다 — 프리팹 인스턴스는 편집 시점에 없기 때문. `Awake`의 `FindAnyObjectByType` 폴백도 스폰 전이라 실패한다.

`LocalPlayerRegistry`(스폰 시 등록 → 대기자 통보, 이미 있으면 즉시 콜백)로 해결했다. 세 컴포넌트를 지연 바인딩으로 전환했고, **기본값을 "로컬 조종"으로 둬 네트워크 없는 스프린트 3~7 스모크 리그가 그대로 동작**한다. 이 계약을 `DefaultState_IsLocallyControlled` 테스트가 고정한다.

### FishNet API 확인 (추측 없음)

벤더링 소스에서 직접 확인: `OnStartClient()`, `OnOwnershipClient(NetworkConnection)`, `IsOwner`(QOL.cs:166), `PlayerSpawner`가 `Spawns[]` 비면 프리팹 Transform 위치에서 스폰(`SetSpawnUsingPrefab`), Tugboat 기본 `Port 7770`/`localhost`. `PlayerOwnershipGate`는 실제 `FishNet.Runtime.dll`을 참조해 컴파일까지 확인했다.

### 검증

**176케이스 전수 통과**(기존 170 무손상 + 신규 6). Core·Presentation·**Net 어셈블리 최초 컴파일** 전부 에러 0. 하네스에 `AudioModule`·`Unity.Scripting`·`Facepunch.Steamworks.Win64` 참조를 추가(전부 Unity 본체는 기본 참조 — 프로젝트 결함 아니라 하네스 누락).

### 남은 것

에디터 GUI 자산 작업(§10 절차서) — NetworkManager+Tugboat+PlayerSpawner 배치, Player 프리팹화 + NetworkObject·NetworkTransform·PlayerOwnershipGate 부착, 씬 인스턴스 제거. 완료 후 에디터 검증(원격 플레이어 이동·입력 차단·카메라 단일화). **2단계(이벤트 동기화) 착수 전 확인 필요**: 발소리/밸브/태그가 현재 로컬 스모크 리그로만 도는데, 2단계에서 이를 서버 권위로 올릴 때 각 시스템의 발생원 ID(현재 하드코딩 1·100~103)를 실제 `OwnerId`로 바꾸는 작업이 선행돼야 한다.

---

## 스프린트 7 — 태그 판정 (2026-07-19)

지시서: `마르코_스프린트7_태그판정_prompt.md`. 술래가 시간 초과로만 이길 수 있던 상태를 해소. **§6.3 세 판정 분기가 모두 도달 가능해졌다.** Core 파일 무수정.

### 확인부터: Core는 이미 준비돼 있었고, 사양도 명확했다

지시서가 "새로 만들기 전에 기존 필드부터 확인"하라 한 대로 확인한 결과:

- **`WinConditionEvaluator.Evaluate`는 이미 `allRunnersTagged`를 받고 있었다**(T4 산출물). **Core 확장이 전혀 필요 없었다.**
- 지시서가 참조한 §14.1은 실제로는 "네트워크 스택 선정"이고, **태그 사양은 §3.1 역할 표와 §3.3 요약 도식**에 있었다. 내용은 오히려 예상보다 구체적이다:

| 기획서 문구 | 위치 | 구현 |
|---|---|---|
| "접촉 트리거, 반경 **1.2m**, **1회 접촉 즉시 확정**" | §3.1 · §3.3 | `TagRules.TagRadiusMeters = 1.2f`, 홀드·쿨다운 없음 |
| "태그 1회 → **메아리로 즉시 전환**" | §3.1 사망/탈락 처리 | `TaggableRunner.MarkTagged()` → `Role = Echo` |
| 메아리는 "**태그 불가**(비활성 콜라이더)" | §3.1 메아리 열 | `TagRules.CanTag`가 Echo 대상 거부 |

**재태그 쿨다운을 만들지 않은 이유**: 태그당한 즉시 메아리가 되고 메아리는 태그 불가라, 같은 대상을 두 번 태그하는 상황이 **구조적으로 성립하지 않는다.** 기획서에 쿨다운 언급이 없는 것도 이 때문으로 보이며, 스프린트 5의 GAP-9 판단 방식("명시 없으면 없는 대로")을 유지해 규칙을 창작하지 않았다.

### 신규 파일

| 파일 | 역할 |
|---|---|
| `Presentation/Tagging/TagRules.cs` | §3.1 거리·역할 규칙(순수). 1.2m 상수 포함 |
| `Presentation/Tagging/TaggableRunner.cs` | 태그 대상 도망자 — **로컬 단독 실행용 대역**(아래 참조) |
| `Presentation/Tagging/TagDetector.cs` | 술래 역할일 때만 1.2m 근접 판정 → 코디네이터에 보고 |
| `Tests/EditMode/TaggingTests.cs` | 21케이스 |

수정: `RoundOutcomeTracker`에 태그 집계 추가(`TryRegisterTag`/`TaggedCount`/`AreAllRunnersTagged`) — **기존 `Evaluate` 시그니처는 그대로 둬 스프린트 6 테스트 18건이 깨지지 않게 했다.** `RoundCoordinator`에 `TryRegisterTag` 추가 및 `_allRunnersTagged` → `_forceAllRunnersTagged`로 의미 정정(수동 강제용). `Game.unity`에 대역 도망자 3명 + `TagDetector` 배치.

### 판정 권한 단일화 원칙 유지

스프린트 6에서 확립한 **"판정은 `RoundCoordinator`만"** 원칙을 그대로 지켰다. `TagDetector`는 승패를 계산하지 않고 태그 사실만 보고하며, 역할 규칙도 `RoundOutcomeTracker`가 최종 강제한다(스프린트 5 GAP-5 처리와 동일 원칙 — 규칙을 한 곳에만 둔다).

### 로컬 단독 실행용 대역 도망자 (실제 게임플레이 기능 아님)

플레이어가 한 명뿐이라 "술래가 도망자를 태그한다"를 확인할 상대가 없다. 그레이박스의 밸브 큐브가 실제 밸브를 대신하듯, **씬에 놓인 `TaggableRunner` 3개가 도망자를 대신한다**(로비 인근 캡슐, 콜라이더 없음 — 이동 방해 안 함). 네트워크 스프린트에서 실제 원격 플레이어로 대체된다. 이 대역 덕분에 검증 체크리스트 1~5번을 전부 에디터에서 확인할 수 있다.

### 스펙 갭 1건 신규 (GAP-13)

| # | 갭 | 결정 | 근거 |
|---|---|---|---|
| **GAP-13** | 일부가 탈출한 상태에서 §6.3의 `allRunnersTagged`는 어떻게 읽는가 | **문자 그대로 "모든 러너가 태그됨"**. 탈출한 러너는 태그된 적이 없으므로, 누군가 탈출했다면 false | 혼재 상황이 실질적으로 문제되지 않는다 — 탈출은 §6.1상 게이트 개방(밸브 전부) 이후에만 가능하고, 그 조건이면 §6.3 첫 분기(RunnersWin)가 이미 성립해 라운드가 그 시점에 끝난다. 테스트 `EscapeWithValvesComplete_BeatsRemainingTags`가 이 우선순위를 고정 |

부수 결정: 러너가 0명이면 "전원 태그"로 치지 않는다(공허한 참 방지). 탈출한 러너는 태그 불가(이미 맵을 벗어남).

### §6.3 세 분기가 모두 도달 가능해졌다

테스트 `AllThreeVerdictPaths_AreReachable`이 이 스프린트의 목적 자체를 고정한다:

| 분기 | 조건 | 열린 시점 |
|---|---|---|
| `RunnersWin` | 밸브 전부 + 1인 이상 탈출 | 스프린트 6 |
| `SeekerWin` (태그) | 전원 태그 — **시간이 남아도 즉시** | **스프린트 7 ← 이번** |
| `SeekerWin` (시간) | 제한시간 만료 | 스프린트 6 |

### 신규 테스트 21케이스

거리 4건(명시값 1.2m·이내·초과·경계 포함) · 역할 5건(술래→러너 허용, 메아리 대상 거부, 잘못된 조합 3종) · 집계 5건(집계·메아리 재태그 거부·중복 방지·탈출자 태그 불가·라운드 종료 후 무시) · §6.3 판정 7건(일부 태그 시 미결·**전원 태그 즉시 승리**·러너 0명·혼재 2건·래치·**세 분기 도달성**). 총계 149 → 170.

---

## 스프린트 6 — 탈출 지점 + 라운드 타이머 (2026-07-19)

지시서: `마르코_스프린트6_탈출타이머_prompt.md`. **게임이 처음으로 "끝난다."** 지금까지 §6.3 판정이 항상 `InProgress`였던 이유(탈출·타이머 부재)를 해소했다. Core 파일 무수정.

### 확인부터: 필요한 것이 대부분 이미 있었다

- **제한시간은 기획서에 명시돼 있다** — §6.2 표의 4인 행 `10분`. 지시서가 "명시값을 못 찾으면 GAP으로 기록"하라 했으나 찾았으므로 **임의값을 쓰지 않았다**. 2/3/5/6인 값도 상수로 함께 넣어 뒀다(v1.x).
- **§6.3 판정식은 T4에서 이미 완성**(11케이스)돼 `runnersEscaped`·`timeRemainingSeconds`를 이미 받고 있었다. 이번 작업은 그 두 입력에 실제 값을 흘려보내는 배선이다.
- **`InGame → RoundEnd`** 전이도 §15.4 전이표에 이미 있었다(9케이스).

### 신규 파일

| 파일 | 역할 |
|---|---|
| `Presentation/GameFlow/RoundTimer.cs` | §6.2 카운트다운. 만료 신호 **1회만** 발행, 음수 방지(순수) |
| `Presentation/GameFlow/RoundOutcomeTracker.cs` | 탈출 집계 + §6.3 판정 **래치**(순수). 판정식은 Core에 위임 |
| `Presentation/GameFlow/RoundCoordinator.cs` | 씬 글루: 밸브 수·탈출 수·남은 시간을 모아 단일 지점 판정 → `InGame→RoundEnd` 전이 |
| `Presentation/Objectives/EscapePointTrigger.cs` | §10.1 배수로 출구 탈출 지점(거리 기반, 밸브와 동일 패턴) |
| `Tests/EditMode/RoundFlowTests.cs` | 18케이스 |

### 판정 권한을 한 곳으로 통합 (기존 코드 수정 1건)

`ValveObjectiveTracker`(스프린트 5)가 자체적으로 `WinConditionEvaluator`를 호출하고 있었다. 여기에 `RoundCoordinator`가 추가되면 **두 컴포넌트가 각자 판정해 서로 다른 결론을 로그로 찍는다.** 그래서 트래커는 집계 전담(`OpenedCount`/`TotalValves`/`IsEscapeGateOpen`)으로 바꾸고 판정은 코디네이터로 일원화했다. 기능 추가가 아니라 **중복 권한 제거**다.

### 스펙 갭 2건 신규 (GAP-11, GAP-12)

| # | 갭 | 결정 | 근거 |
|---|---|---|---|
| **GAP-11** | 술래·메아리가 탈출 지점에 도달하면? §6.3은 `runnersEscaped`만 말하고 다른 역할을 언급하지 않음 | **러너만 집계** | 판정식 변수명이 `runnersEscaped`이고, §3.2상 메아리는 유령(물리 상호작용 불가), 술래는 탈출할 이유가 없다. 명시가 없으니 가장 보수적인 해석 |
| **GAP-12** | 탈출 판정 반경 수치 미명시 | 2.0m (조정 가능) | 밸브 상호작용(GAP-10, 2.5m)보다 약간 좁게 — 탈출은 "지나가면 발동"이 아니라 "도달"이어야 한다 |

**GAP 아님(기획서 명시 규칙)**: "밸브 3개 모두 Open → 배수로 게이트 Open → 탈출 가능"(§6.1)은 명시돼 있으므로 게이트가 닫힌 상태의 탈출을 코드로 차단했다. 테스트 `Escape_BeforeGateOpens_IsRejected`가 고정한다.

### 구현 결정

| 결정 | 근거 |
|---|---|
| 판정은 **한 번 결정되면 래치** | 라운드 종료는 되돌릴 수 없다. 종료 처리(전이·로그)가 1회만 일어나도록 `Evaluate`가 "이번에 새로 결정됨"을 반환 |
| 같은 러너의 중복 탈출 집계 방지 | `HashSet<ulong>`으로 ID 관리 |
| 탈출은 범위에 **들어온 순간** 1회 시도 | 서 있는 동안 매 프레임 시도하지 않도록 |
| 역할·게이트 조건을 트리거가 아닌 `RoundOutcomeTracker`에서 검사 | 스프린트 5 GAP-5 처리와 같은 원칙 — 규칙을 한 곳에만 둔다 |
| `RoundCoordinator`가 Start에서 Boot→…→InGame까지 전이 | §15.4 전이표는 순서대로만 진행 가능. 로컬 단독 실행 스캐폴딩이며, 실제로는 로비·역할 배정 시스템이 구동할 자리 |

### 아직 안 열린 경로

**태그 판정(§14.1)은 이번 스코프가 아니다**(사용자 우선순위 2번). 따라서 술래가 직접 이기는 경로는 여전히 닫혀 있고, 술래는 **시간 초과로만** 이길 수 있다 — 지시서가 명시한 대로 정상 상태다. `_allRunnersTagged`를 인스펙터에 남겨 §6.3의 해당 분기는 수동으로 확인할 수 있다.

### 신규 테스트 18케이스

타이머 5건(명시값 확인·감소·**만료 1회성**·0 고정·정지) · 탈출 집계 5건(게이트 전 거부·게이트 후 집계·비러너 2역할 거부·중복 방지·복수 러너) · §6.3 판정 7건(러너 승·시간초과 술래 승·미결·**래치**·종료 후 탈출 무시·탈출 우선순위·타이머→판정 흐름). 총계 131 → 149.

---

## 스프린트 5 — 밸브 E 상호작용 배선 (2026-07-19)

지시서: `마르코_스프린트5_밸브E배선_prompt.md`. T3에서 완성된 Core `Valve`(17케이스)를 아무도 호출하지 않던 상태를 해소. **이번 스프린트로 "플레이어 입력 → 밸브 상태 → 승패 판정"까지 코어 게임 루프가 처음으로 닫혔다.** Core 파일 무수정(준수).

### 신규 파일

| 파일 | 역할 |
|---|---|
| `Presentation/Objectives/ValveInteractionController.cs` | E 홀드 → §6.1 상태기계 배선의 **순수 로직**(취소 규칙 포함, Unity 비의존 → 테스트 가능) |
| `Presentation/Objectives/ValveBehaviour.cs` | 씬 밸브 오브젝트 1개당 Core `Valve` 인스턴스를 소유하는 얇은 래퍼 |
| `Presentation/Objectives/ValveInteractor.cs` | 씬 글루: 입력 폴링·최근접 밸브 탐색·로그·§5.1 소음 발행 |
| `Presentation/Objectives/ValveObjectiveTracker.cs` | 개방 수 집계 → §6.3 `WinConditionEvaluator` 연결 → Console 로그 |
| `Tests/EditMode/ValveInteractionControllerTests.cs` | 15케이스 |

수정: `LocalPulsePipeline`/`LocalPulsePipelineBehaviour`에 범용 `EmitPulse` 추가(발소리 전용이던 진입점을 §5.1 전 등급용으로 일반화, 기존 `OnFootstepPulse`는 위임으로 유지 — 호출부 무변경). `Game.unity`에 컴포넌트 5개 배치.

### GAP-5는 Core가 이미 강제하고 있었다 — 중복 검사 안 함

지시서가 확인을 요청한 항목. `Valve.cs:62`가 `RoleType.Echo`를 이미 거부하므로 **Presentation에서 역할을 다시 검사하지 않고**, Core의 거부를 `ValveInteractionEvent.Rejected`로 그대로 전달한다. 규칙이 두 곳에 흩어져 나중에 어긋나는 것을 막기 위함. 테스트 `EchoRole_IsRejectedByCore`가 이 경로를 고정한다.

### 스펙 갭 2건 신규 (GAP-9, GAP-10)

| # | 갭 | 결정 | 근거 |
|---|---|---|---|
| **GAP-9** | §6.1이 취소 조건을 `interrupt(이탈)`로만 쓰고 **구체적 트리거를 열거하지 않음** | 이탈 = ① E 키를 뗌 ② 상호작용 범위 이탈. **범위 안에서의 단순 이동은 취소하지 않는다** | 기획서에 "움직이면 취소"라는 문구가 없다. 없는 규칙을 만들지 않는 쪽을 택했다. §6.4가 "연결 끊김"을 별도 항목으로 분리한 것도 "이탈"이 상호작용 자체를 놓는 행위를 가리킨다는 근거 |
| **GAP-10** | 밸브 **상호작용 거리 수치가 기획서 어디에도 없음** | 2.5m (인스펙터·생성자로 조정 가능) | §14.1 태그 판정 1.2m(접촉급)보다 넉넉해야 밸브 앞에 서서 누를 수 있고, 캐릭터 콜라이더 0.35m + 밸브 큐브 0.6m를 감안하면 2.5m가 "바로 옆"에 해당. 플레이테스트 조정 대상 |

### §5.1 밸브 소음(12m) 연결

§6.1 두 번째 줄("밸브 회전 중 소음 12m/3초 지속 발생 — 중간에 멈춰도 이미 발생한 소음은 취소되지 않음")을 구현했다. 회전 시작 시점에 회전 시간 전체를 덮는 펄스를 **한 번만** 발행하고 취소 시에는 아무것도 하지 않는다. Core `Valve`가 `RotationStarted`/`SoundRadiusMeters`를 노출해 둔 것이 정확히 이 용도였다.

> 스코프 판단: 지시서의 포함 목록에 "소음"이 명시돼 있진 않았지만, §6.1 본문에 있는 동작이고 T7/T8 파이프라인이 이미 있어 연결만 하면 됐다. 이걸 빼면 밸브가 무음이 되어 §5.1의 "의도된 유인 장치" 설계가 성립하지 않는다.

### 승패 판정 연결의 현재 한계

`ValveObjectiveTracker`가 개방 수를 세어 §6.3 `WinConditionEvaluator`에 넘긴다. 다만 **탈출·태그·라운드 타이머가 아직 미구현**이라 판정은 대부분 `InProgress`로 남는다. 이번 스프린트에서 눈으로 확인 가능한 이정표는 §6.1 마지막 줄인 "밸브 3개 모두 Open → 배수로 게이트 Open → 탈출 가능"이며, 이를 별도 로그로 찍는다. 미구현 시스템의 판정 입력(`_runnersEscaped` 등)은 인스펙터에 노출해 수동으로 넣어보며 §6.3 분기를 확인할 수 있게 했다.

### 신규 테스트 15케이스

역할 제약(메아리 거부/술래 허용) · 취소 2종(GAP-9 (a)(b)) · **범위 내 이동은 취소 안 됨**(GAP-9 반대편 고정) · 범위 밖 시작 불가 · 이미 열린 밸브 무시 · 완료 후 재시작 안 됨 · 취소 후 0부터 재시작 · 진행률 추적 · §6.3 연동. 총계 116 → 131.

---

## 스프린트 4 — T8 파문 렌더러 / 시각화 (2026-07-19)

지시서: `마르코_스프린트4_T8파문렌더러_prompt.md`. `LocalPulsePipelineBehaviour`가 델리버리를 Debug.Log로만 소비하던 것을 **실제 시각 표현으로 교체**. 판정 로직은 이미 Core에 있으므로 이번 작업은 순전히 "그 결과를 눈에 보이게 그리는 것"이다. **Core 파일 무수정**(준수).

### 신규 파일 (전부 Presentation/SoundPulse/)

| 파일 | 역할 |
|---|---|
| `PulseVisualState.cs` | 화면에 살아있는 파문 하나의 상태. `Progress01(now)`·`IsExpired(now)`로 자체 만료 판정 |
| `PulseVisualRegistry.cs` | 델리버리 → 시각 오브젝트 수명 매핑(순수 로직, Unity 의존 없음 → 테스트 가능) |
| `PulseVisualRenderer.cs` | 실제 렌더: LineRenderer 링 풀 + IMGUI 8방위 인디케이터, ColorPalette 연동 |
| `Tests/EditMode/PulseVisualRegistryTests.cs` | 12케이스 |

수정: `LocalPulsePipelineBehaviour.cs` — 델리버리를 렌더러로 전달(`OnDelivery`), 매 프레임 `_visuals.Tick(now)` 구동, Debug.Log는 `_logDeliveries` 토글 뒤로(기본 꺼짐), 스트레스 테스트 키 추가. `Game.unity` — `PulseSystem`에 렌더러 컴포넌트 추가 및 팔레트·카메라 배선.

### GAP-2 분기가 처음으로 눈에 보인다

Core가 이미 "좌표를 줄지 방향만 줄지" 판정해 넘겨주므로 Presentation은 그 판단을 다시 하지 않고 **표현만 분기**한다.

| Core 판정 | 시각 표현 |
|---|---|
| `WorldSpaceRingVisible == true` (벽 0개) | 발생 지점에서 `PerceivedRadius`까지 확장하며 페이드아웃하는 월드스페이스 링(§5.4) |
| `WorldSpaceRingVisible == false` (차폐) | 좌표 없이 화면 가장자리 8방위 인디케이터만(§3.4) |

이것으로 **"차폐돼서 방향만 보이는 것"과 "반경 밖이라 아예 안 보이는 것"이 처음으로 시각적으로 구별된다** — 스프린트 3에서 로그만으로는 구분 불가했던 한계의 해소다.

### 자연 만료는 자체 타이머로 (필수 제약)

스프린트 3 버그 조사에서 확정했듯 **자연 만료 시 `Disappeared` 델리버리는 오지 않는다**(T7 의도된 설계). 따라서 렌더러는 Appeared 시점에 받은 `PerceivedDuration`으로 자체 타이머를 돌려 스스로 사라진다. `Disappeared`는 조기 소실(차폐/거리 변화) 시 즉시 제거 용도로만 쓴다. 이 계약을 `Tick_PastDuration_SelfRemovesWithoutDisappearedDelivery` 테스트가 고정한다 — 어기면 파문이 화면에 영원히 남는다.

### 구현 결정

| 결정 | 근거 |
|---|---|
| `Updated` 시 `StartTime` 보존, 반경·지속만 갱신 | 펄스는 같은 시각에 발생했고 차폐로 인지값만 바뀐 것. 타이머 재시작은 오표현 |
| 지속시간이 줄면(차폐 감쇠) 그만큼 일찍 사라짐 | `PerceivedDuration`이 §5.6 감쇠를 이미 반영하므로 그대로 따름 |
| 링은 단위원 1회 생성 + `localScale`로만 확장, 풀링 | §15.5 동시 30개 목표. 프레임당 정점 재계산 회피 |
| 링 재질은 URP Unlit 런타임 생성(인스펙터 덮어쓰기 가능) | 에셋 추가 없이 동작. 가산 반투명으로 암전 맵(§16.1)에서 파문만 떠오르게 |
| 방향 인디케이터는 IMGUI(OnGUI) | UI 시스템이 아직 없음. 기능 확인용 최소 구현이며 §12 HUD 작업 때 정식 UI로 교체 |
| 인디케이터는 카메라 yaw를 빼서 화면 상대 각도로 표시 | `DirectionOctant`는 월드 기준(N=+z). 정면이 화면 위가 되어야 방향 힌트로 쓸모 있음 |
| 색상은 `GetRunner(colorblind)` 고정 | 현재 발생원은 로컬 플레이어(러너)뿐. 역할별 색 분기는 역할이 네트워크로 오는 시점에 배선(§16.2) |
| 재등장(차폐 해소) 시 타이머 재시작 | 원 발생 시각을 `PerceivedPulse`가 갖고 있지 않음. 드문 케이스이고 시각 표현상만의 오차 — 코드 주석에 명시 |

### 스트레스 테스트 키

`P`(인스펙터 변경 가능) 입력 시 청취점 주변에 파문 30개를 한꺼번에 생성한다. §15.5 성능 목표(동시 30개 @60fps) 체감 확인용이며, 정밀 실측(프로파일러 수치)은 T6 스코프로 남긴다.

### 검증 하네스 보강

`PulseVisualRenderer`가 IMGUI를 쓰면서 스크래치패드 검증 csproj에 `UnityEngine.IMGUIModule`·`UnityEngine.TextRenderingModule` 참조가 없어 컴파일이 실패했다. Unity 본체는 이 모듈들을 기본 참조하므로 **프로젝트 결함이 아니라 검증 하네스의 누락**이었고, 두 csproj(`CoreVerify`/`PresentationVerify`)에 참조를 추가해 해소했다.

### 에디터 확인 체크리스트

→ **`docs/수동검증_절차.md`로 분리**했다(반복 가능한 절차 문서). 요약: 파문 링 표시 · 차폐 시 방향 인디케이터 전환 · 자연 소멸 · `P` 성능 · `C` 색맹 토글.

---

## 스프린트 4 후속 — 성능 조사 및 수동 검증 노출 (2026-07-19)

지시서: T8 에디터 검증 중 확인된 3개 항목(프레임 드롭 / 색맹 토글 미노출 / 스폰 반경 한계) 처리. 새 기능 추가 없음.

### A. 성능 병목 — 코드 검토로 4건 특정, 전부 수정

프로파일러를 직접 돌릴 수 없는 환경이라 **Unity의 알려진 할당·오버헤드 패턴을 근거로 코드 검토**해 특정했다. 4건 모두 T8에서 내가 새로 넣은 코드에 있었다.

| # | 병목 | 왜 비싼가 | 수정 |
|---|---|---|---|
| 1 | `HitBuffer[i].collider.tag` (`PhysicsOcclusionProbe`) | `Component.tag` 게터는 **호출마다 문자열을 새로 할당**한다(Unity의 대표적 GC 원인). 재판정이 초당 수십 회 도는 경로 | `CompareTag()`로 무할당 비교. 판정 규칙은 `OcclusionAccumulator` 구조체로 `Probe`/`Classify` 양쪽이 공유해 중복 방지 |
| 2 | `foreach (… in _registry.Visuals)` ×2 (`PulseVisualRenderer`) | 사전을 `IReadOnlyDictionary`로 노출해 순회하면 **struct 열거자가 박싱**돼 힙 할당. `UpdateRings`는 매 프레임, `OnGUI`는 프레임당 여러 번 | 레지스트리에 `CopyTo(List<T>)` 추가, 렌더러는 재사용 버퍼 사용. 방향 인디케이터 목록도 `Tick`에서 미리 확정해 **OnGUI는 레지스트리를 아예 건드리지 않음** |
| 3 | `OnGUI`에 이벤트 필터 없음 | OnGUI는 프레임마다 **Layout·Repaint·모든 입력 이벤트마다** 호출된다. 실제 그려지는 건 Repaint뿐인데 전량 실행 중이었음 | `Event.current.type != EventType.Repaint`면 즉시 반환. 색·카메라 yaw·문자열도 프레임당 1회로 호이스팅 |
| 4 | `line.material = _ringMaterial` | `Renderer.material`은 **머티리얼 사본을 인스턴스화**한다 → 링 개수만큼 사본 생성 + 배칭 불가 | `sharedMaterial`로 변경. `widthMultiplier`도 생성 시 1회 설정으로 이동 |

추가로 **링 풀 프리워밍**(`_prewarmRingCount`, 기본 32)을 넣었다. 30개가 동시에 뜨는 순간 GameObject를 한꺼번에 만들면 그 프레임만 튀기 때문이다.

**정직한 한계**: 위 4건은 모두 "확실히 비용이고 고치는 게 맞는" 항목이지만, **어느 것이 지배적이었는지는 프로파일러 없이는 단정할 수 없다.** 수정 후 체감 확인은 사용자의 에디터 세션에 남긴다(절차: `수동검증_절차.md` §6). 스트레스 로그에 프레임 시간(ms)을 함께 찍도록 해서 수치 비교가 가능하다.

### B. 수동 검증 절차 노출

`docs/수동검증_절차.md` 신규 작성. 색맹 토글은 기존 `P` 키 컨벤션을 따라 **`C` 키**를 추가했다(Console에 상태 로그, 인스펙터 경로도 병기). 자연 소멸 확인 절차도 구체적 동작으로 기술("한 걸음만 움직여 발소리 하나를 낸 뒤 멈춰서 0.4초 안에 사라지는지 관찰").

### C. 스폰 반경 한계 — 렌더러 문제 아님을 코드 경로로 확인

관찰된 "스폰 지점 근처에서만 보인다"는 **예상된 동작이 맞다**. 인과 경로:

```
발소리(플레이어 현재 위치, 반경 2m/6m)
  → SoundPulseResolver.Resolve(pulse, 청취자=스폰지점 고정)
  → straightDist > baseRadius → null 반환          ← Core의 1차 컷(SoundPulseResolver.cs:56)
  → 델리버리 없음 → 레지스트리에 Appeared 없음 → 렌더러가 그릴 것 자체가 없음
```

렌더러는 델리버리가 있어야만 시각 오브젝트를 만들므로, 판정이 `null`이면 렌더 경로에 진입조차 하지 않는다. 이 동작은 **스프린트 3에서 이미 테스트로 고정**돼 있었다(`LocalPulsePipelineExpiryTests.SourceWalkingAwayFromFixedListener_LaterPulsesNeverAppear`). 수정 불필요 — 대신 검증 절차에 "스폰 반경 6m 이내에서 테스트할 것"을 §1로 크게 명시했다.

### 신규 테스트 2케이스

`CopyTo_FillsBufferWithAllVisuals`, `CopyTo_ClearsPreviousBufferContents` — 새로 만든 무할당 순회 API의 계약(특히 버퍼 재사용 시 이전 프레임 잔여물이 남지 않을 것)을 고정. 총계 114 → 116.

---

## 버그 조사 — "Disappeared 델리버리가 한 번도 안 뜬다" (2026-07-19)

지시서: `마르코_버그_펄스만료_prompt.md`. 스프린트 3 배선 완료 후 에디터 Play 검증 중 발견된 증상 조사.

### 관찰된 증상 (재기술)

Play 모드 WASD 이동 중 `[Pulse] Appeared pulse=0`, `pulse=1` 로그 후 `pulse=5`, `pulse=6`으로 건너뛰고(2,3,4 미관측), 이후 로그가 멈췄다가 약 2분 뒤 3개가 추가로 발생. **세션 전체에서 `Disappeared` 로그가 한 번도 없었다.**

### 조사 결과: **버그 아님 — 의도된 설계 + 임시 스모크 리그의 알려진 한계가 겹친 것**

두 가지 사실을 코드와 기존 테스트로 확인했다.

1. **`ActivePulseTracker`의 "자연 만료는 통지 없음" 설계는 의도적이며 이미 검증돼 있었다.** `ActivePulseTracker.cs` 60~62행 주석: "자연 만료는 delivery 없이 조용히 끝난다 — 클라이언트는 이미 받은 perceivedDuration으로 스스로 렌더를 종료하므로 통지가 불필요하다. Disappeared는 지속시간이 남았는데 차폐·거리로 소실된 경우 전용이다." 기존(스프린트 3 이전) `ActivePulseTrackerTests.AfterListenerDuration_NoMoreDeliveriesForRunner`가 이 동작을 이미 통과시키고 있었다 — **T7 산출물 자체의 기존 계약**이지 이번에 생긴 결함이 아니다.
2. **디버그 청취자가 스폰 위치에 고정돼 있다(`LocalPulsePipelineBehaviour.Start()`, 스프린트 3에서 이미 "임시 스모크 리그"로 명시).** 발생원(플레이어)이 발소리 반경(걷기 2m/질주 6m) 밖으로 걸어 나가면, 그 뒤에 추가되는 펄스는 `Resolve()`가 처음부터 `null`을 반환해 **Appeared 자체가 생기지 않는다**(로그에 안 뜬다). 이게 "2,3,4가 건너뛴 것처럼" 보인 이유다 — `AddPulse()`는 ID를 스킵하지 않는다(테스트로 확정).
3. Presentation 배선 자체(`Update()`의 `Time.time` 전달, `LogDelivery()`의 `Disappeared` 분기)는 코드 검토 결과 정상이었다.

### 재현 테스트 (버그 아님을 실증)

`Assets/_Project/Tests/EditMode/LocalPulsePipelineExpiryTests.cs` (신규 6케이스, 첫 실행에 전부 통과 — 별도 수정 없이 기존 동작이 기대대로였음을 의미):

| 테스트 | 확인 내용 |
|---|---|
| `NaturalExpiry_WithoutOcclusionOrRangeChange_EmitsNoDisappeared` | 반경 안·차폐 불변 상태로 duration 경과 → Appeared 1건만, Disappeared 없음 |
| `NaturalExpiry_WithRoleDurationMultiplier_StillSilent` | 술래(×1.5) 청취자도 동일하게 조용히 만료 |
| `EarlyOcclusionLoss_WithinDuration_EmitsDisappeared` | 대조군: duration 중 하드블로커 등장 → Disappeared 정상 발생(Core의 Disappeared 자체는 살아있음) |
| `ContinuousAddition_OldPulsesEvictedWithoutUnboundedGrowth` | 연속 추가해도 `ActivePulseCount`가 무한히 안 쌓임(조용한 제거가 실제로 동작) |
| `SequentialAddPulse_ReturnsGaplessIds` | `AddPulse()` ID는 항상 연속 — ID 스킵은 없음 |
| `SourceWalkingAwayFromFixedListener_LaterPulsesNeverAppear` | **스모크 리그 재현**: 청취자 고정 + 발생원이 멀어지며 펄스 5개 발생 시 앞 2개만 Appeared, 나머지 3개는 ID는 발급되지만 델리버리가 없음 — 관찰된 "2,3,4 누락" 패턴과 정확히 일치 |

### 코드 수정 없음

증거가 "정상 동작"을 가리켜 `SoundPulseResolver.cs`/`ActivePulseTracker.cs`/`LocalPulsePipeline.cs`/`LocalPulsePipelineBehaviour.cs` 중 어느 것도 수정하지 않았다. 지시서 원칙("증거 없이 고치지 않는다")에 따라 재현 테스트로 결론을 고정하는 것으로 조사를 종결한다.

### 참고: 스모크 리그의 한계는 이미 알려진 것이었다

`LocalPulsePipelineBehaviour`의 기존 주석이 "청취 기준점은 Start 시점의 플레이어 위치에 고정된 가상 청취자"라고 이미 명시하고 있었다. 고정 청취자를 넘어서는 검증(플레이어가 멀리 이동해도 계속 관측하고 싶다면 청취자 위치를 매 프레임 갱신하거나 여러 지점에 배치하는 등)은 스프린트 3 문서에서도 T8/네트워크 스코프로 명확히 분리돼 있어 이번 조사 범위 밖이다.

---

## 스프린트 3 — 발소리 펄스 → 게임플레이 파이프라인 배선 (2026-07-19)

지시서: `마르코_스프린트3_발소리펄스배선_prompt.md`. 이미 완성된 두 시스템(발소리 발생 ↔ 펄스 판정)을 잇는 순수 배선 작업 — **새 게임플레이 규칙 없음, Core 파일 수정 없음, Net 참조 추가 없음**(전부 준수).

### 신규 파일 (전부 Presentation)

| 파일 | 역할 |
|---|---|
| `Presentation/SoundPulse/PhysicsOcclusionProbe.cs` | §5.6 차폐 판정의 **첫 Unity Physics 구현체**. `RaycastNonAlloc`(SoundBlocking 레이어) → 태그 수집 → 순수 판정부 `Classify()` 분리. 재판정 주기 로직은 중복 구현하지 않음(트래커 책임) |
| `Presentation/SoundPulse/LocalPulsePipeline.cs` | 순수 배선: 발소리 이벤트 → §5.5 SoundPulse 변환 → `AddPulse()` → `Tick(now)` 구동 → `PulseDelivery` 방출. 시간·차폐·청취자 전부 주입식이라 EditMode 테스트 가능 |
| `Presentation/SoundPulse/LocalPulsePipelineBehaviour.cs` | Unity 수명주기 어댑터. `FootstepPulseEmitted` 구독, `Time.time` 틱, 델리버리를 `[Pulse] Appeared radius=6 octant=NE` 형식 Console 로그로만 출력(T8 전까지 시각 연출 없음) |
| `Tests/EditMode/LocalPulsePipelineTests.cs` | 10케이스 |

수정: `Core.Tests.asmdef`에 `Presentation` 참조 추가(테스트가 파이프라인·Classify를 보기 위함). `Game.unity`에 `PulseSystem` 오브젝트 추가(`_player` → Player의 FirstPersonController 배선, 기존 오브젝트 무수정).

### 임시 self-listen 스모크 리그 (실제 게임플레이 기능 아님)

로컬 단일 클라이언트에는 "남의 소리를 듣는 청취자"가 없다. GAP-1(본인 발생 펄스 제외) 때문에 자기 자신을 청취자로 두면 아무 델리버리도 안 나오므로, **Start 시점의 플레이어 스폰 위치에 고정된 가상 청취자(id=999, 러너 역할)**를 등록했다. 러너 역할을 준 이유: §5.7 배율 ×1.0이라 로그 수치가 §5.1 원본값 그대로 나와 눈으로 검증하기 쉽다. 플레이어가 스폰 지점에서 멀어지거나 벽 뒤로 돌아가면 로그의 Appeared/Updated/Disappeared 변화로 차폐가 실물 검증된다. 실제 청취자 목록은 네트워크 스프린트에서 원격 플레이어 스냅샷으로 대체된다.

### 신규 테스트 10케이스

배선 계약 5건(걷기 펄스 Appeared·GAP-1 self 제외·질주 값 보존·반경 밖 무전달·차폐 변화 Updated→Disappeared) + `Classify` 순수 판정 5건(빈 히트·벽1·벽2·하드블로커·혼합). Physics 호출부(Probe)는 리플렉션 러너에서 실행 불가라 **에디터 Play 검증 항목**으로 남김.

### 구현 결정(소소)

- 발소리 발생·청취 지점 모두 발 높이(transform.position, y≈0.05) 기준 — 벽(y 0~3)과 교차하므로 그레이박스에서 문제없음. 눈높이 기준 필요성이 생기면 그때 조정
- `LayerMask.GetMask("SoundBlocking")==0`이면 생성자에서 경고 로그(레이어 미설정 조기 발견용)

### 에디터 확인 체크리스트 (이번에 반드시 — Physics 의존 기능 최초 도입)

1. Game.unity 로드, Graybox·Player·PulseSystem 정상 표시
2. Play에서 WASD·마우스룩·Shift 질주·FOV 90·커서 락
3. 이동 중 Console `[Pulse]` 로그가 발소리 타이밍(걷기 1초 3회)과 일치하는지
4. 스폰 지점에서 벽 뒤로 이동 시 로그상 차폐 상태 변화 — **SoundBlocking 레이어 첫 실물 검증**
5. Test Runner 전체 케이스 재확인(96)
6. 예외 없이 5분 자유 이동

---

## 스프린트 2 추가분 — §4 플레이어 이동 시스템 (2026-07-19)

M3 태스크 분해(T1~T25)에서 누락됐던 §21 M1 산출물("1인칭 컨트롤러")을 구현. 이것으로 프로젝트가 처음으로 **플레이 가능 상태**가 됨(그레이박스를 걸어다닐 수 있음).

### 계층 분리 (§15.2 유지)

| 계층 | 파일 | 담당 |
|---|---|---|
| Core | `Core/Locomotion/MovementState.cs` | §4.2 상태 4종 (Idle/Walk/Sprint/Diving) |
| Core | `Core/Locomotion/LocomotionConfig.cs` | §3.1/§6.2 속도 상수 (러너 5.0/7.5, 술래 5.4, 메아리 8.0) |
| Core | `Core/Locomotion/LocomotionSimulator.cs` | 순수 로직: 입력→상태·속도·발소리 펄스 타이밍 |
| Presentation | `Presentation/Player/FirstPersonController.cs` | Input System 폴링, CharacterController.Move, 마우스룩, 커서 락 |

### 구현 결정

| 결정 | 근거 |
|---|---|
| 발소리 등급을 상태가 아니라 **실제 속도**로 판정 | §5.1 발생 조건 열의 문자 그대로("≤5.0" / ">5.0"). **귀결: 술래(5.4)는 이동만 해도 질주 등급(6m) 펄스** — 러너에게 술래 접근 경보로 작용. 플레이테스트 검증 항목 |
| 메아리는 발소리 펄스 없음 | §3.2 "상시 비행형"(지면 접촉 없음) |
| 잠수 중 수평 이동 0 + 펄스 없음 | §5.9 잠수=정지 은신. 수면 존 감지는 후속(§5.9 숨 게이지와 함께) — 현재 컨트롤러는 `isOnWaterSurface=false` 고정이라 잠수 진입 불가 |
| GAP-8: 펄스 발생 주기 = 지속시간 (걷기 0.4s, 질주 0.8s) | 기획서에 주기 명시 없음. 이동 중 파문 커버리지가 끊기지 않는 최소 빈도 |
| 입력은 Keyboard/Mouse 디바이스 직접 폴링 | Input System 패키지 API. 씬 YAML 수작업 배선 단계에서 InputActionReference 직렬화 의존 회피. §12.6 리바인딩(M4)에서 액션 에셋 전환 |
| 발소리는 이벤트(`FootstepPulseEmitted`)로만 방출 | Resolver 직접 호출 금지 — 차폐 판정은 서버(Net) 책임(§5.6) |
| 네트워크 미부착 (로컬 전용) | 후속에서 NetworkTransform(§14.2) 부착 시 이 클래스 무수정 |
| CC 높이 1.8m, 눈높이 1.62m, 중력 -9.81 | 기획서 미명시 물리 필수값 — 표준 인체 기준 |

### 씬 배치 (Game.unity)

- `Player` (RunnerSpawn1 위치 12, 0.05, 27 · 남향): CharacterController(반경 0.35 §4.2) + FirstPersonController(_role=Runner)
- `PlayerCamera` (자식, y 1.62): Camera FOV 90(§4.1) + URP AdditionalCameraData + AudioListener
- 기존 `Main Camera`는 비활성화(삭제하지 않음 — 관전/메뉴 카메라로 재사용 여지)
- 구조 검증: 문서 305, fileID 중복 0, 미해결 참조 0, SceneRoots 등록 확인

### Presentation 컴파일 검증

`Presentation.asmdef`에 `Unity.InputSystem` 참조 추가. 스크래치패드 `PresentationVerify.csproj`(소스 glob + `Library/ScriptAssemblies/Unity.InputSystem.dll` 참조)로 컴파일 에러 0 확인.

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

### T2 — 맵 그레이박스 (블로킹 해소: 큐브 프리미티브 방식)

당초 ProBuilder 메시를 에디터 밖에서 만들 수 없어 블로킹으로 보고했으나, **큐브 프리미티브(내장 메시) 기반 그레이박스로 우회해 완료**했다. 기획서 §16.3의 요구는 "블록아웃 수준"이므로 ProBuilder는 수단이지 요건이 아니다. 미감 단계(M4)에서 ProBuilder로 대체하면 된다.

**생성물 (Game.unity에 59오브젝트, `Graybox` 루트 아래)**

| 구역 | 위치(x, z) | 문 | §10.1 대응 |
|---|---|---|---|
| 로비 | 10..18, 24..30 | 데크·라커룸A·라커룸B | 도망자 스폰 3점 |
| 라커룸 A | 4..10, 24..29 | 로비·샤워장 | 은신처(벽 ×0.5 감쇠) |
| 샤워장 | 4..9, 19..24 | 라커룸A·데크 | 통로 겸 안전지역 |
| 라커룸 B | 18..24, 24..29 | 로비 | ⚠ 샤워장 비인접(하단 참조) |
| 메인 풀 홀 | 중앙 오픈 데크 | — | 수면 마커(14×7) 포함 |
| 기계실 | 30..37, 16..22 | 데크(단일) | **밸브 #1** |
| 라이프가드실 | 30..35, 9..13 | 데크(단일) | **밸브 #2** (유리창은 미감 단계) |
| 보일러 통로 | 39..42, 8..18 | 데크·배수로 | **밸브 #3**, 협소 통로 |
| 배수로 출구 | 38..42, 2..8 | 보일러·데크 | EscapeGate(동벽, 밸브 3개 시 개방) |
| 술래 격리실 | 30..34, 30..34 | 데크 | 술래 스폰(§10.1 격리 스폰 규칙) |

**§5.6 배선**: 벽 세그먼트 49개 전부 `Wall` 태그 + `SoundBlocking` 레이어(6). TagManager에 `Wall`/`HardBlocker` 태그와 레이어 6을 등록했다. EscapeGate는 닫힌 상태에서 벽 취급(§5.6 문 규칙 준용).

**머티리얼**: URP Lit(디스크에서 GUID 검증) 기반 4종 — Floor(암전 톤)·Wall·Interact(#FFB84D, §16.2)·Water. `Assets/_Project/Maps/Graybox/`.

**의도적 단순화(미감 단계 이월)**: ① 메인 풀 홀을 벽 없는 오픈 데크로 표현(수영장 홀 특성상 자연스럽고, 라커룸 등이 차폐 포켓 제공) ② 라커룸B↔샤워장 비인접 — §10.1 연결표와 다름, 재배치 검토 ③ 라이프가드실 유리창 생략.

**QA(구조 검증)**: 문서 296개, fileID 중복 0, 미해결 참조 0, SceneRoots 등록 확인, 부모-자식 역참조 58:58 일치. 검증 스크립트는 스크래치패드 `gen_graybox.py` 참조.

### T7 — PerceivedPulse 재판정 오케스트레이션

**신규 파일**
- `Assets/_Project/Core/SoundPulse/ActivePulseTracker.cs` — GAP-3 재판정 루프의 순수 구현
- `Assets/_Project/Tests/EditMode/ActivePulseTrackerTests.cs` — 12케이스

**구현 결정**

| 결정 | 근거 |
|---|---|
| 등록 후 첫 Tick에서 최초 판정, 이후 0.25초 간격 | GAP-3 의사코드 그대로. `ReevaluationInterval = 0.25f` |
| 변화분만 delivery 방출 (Appeared/Updated/Disappeared) | GAP-3 "changed from last: emit" — 대역폭 보호 |
| 리스너별 유효 시간 = duration × 역할 배율 | §5.7 — 러너 만료 후에도 술래(×1.5)는 계속 갱신받음 (테스트 9) |
| 자연 만료는 통지 없음 | 클라가 받은 perceivedDuration으로 스스로 종료. Disappeared는 차폐·이탈 소실 전용 |
| 펄스 보관 상한 = duration × 1.5 | 최대 역할 배율. 초과 시 트래커에서 제거 |
| 재판정 주기 미도래 시 Probe 호출 자체를 생략 | 레이캐스트 예산 보호 — 테스트 3이 호출 횟수로 검증 |
| 시간(now)·리스너·Physics 전부 주입 | Core 격리 원칙 유지 — Net 레이어가 `Time.time`과 실제 스냅샷·`Physics.Linecast` 프로브를 공급 |

---

## 다음 스프린트 후보 (6주차 잔여 + 7주차)

| # | 태스크 | 에디터 필요? |
|---|---|---|
| T5 | SoundPulseResolver + 테스트 | ✅ 완료 (Phase 2) |
| T6 | 차폐 재판정 0.25s 주기 실측 | ⚠ 에디터 프로파일러 필요 — 이제 그레이박스가 있어 측정 환경은 준비됨 |
| T7 | PerceivedPulse 재판정 오케스트레이션 | ✅ 완료 |
| T8 | 파문 렌더러 (셰이더 + 30개 @60fps) | ⚠ 에디터 필요 |
| T9~T12 | 음성 파이프라인 (7주차) | T9 일부(RMS·대역필터·연속성 검사의 순수 로직)는 에디터 없이 가능 |

**Unity 에디터에서 확인할 것**: ① Game.unity가 정상 로드되고 Graybox 59오브젝트가 보이는지 ② TagManager의 Wall/HardBlocker 태그와 SoundBlocking 레이어(6)가 인식되는지 ③ Test Runner에서 68케이스 통과 재확인.

---

### 스프린트 9 후속 — §10 에디터 자산 작업 자동화 도구

수동검증_절차.md §10-1~10-5(NetworkManager+Tugboat+PlayerSpawner 배치, Player 프리팹화, NetworkObject/NetworkTransform/PlayerOwnershipGate 부착, PlayerSpawner 배선, 씬 Player 인스턴스 제거)는 원래 사람이 GUI로 손수 해야 하는 절차였다. 이를 `Tools → MARCO → Setup Network Player` 메뉴 한 번으로 실행하는 Editor 스크립트로 대체했다.

**신규 파일**
- `Assets/_Project/Editor/NetworkPlayerSetupTool.cs` — 자동화 본체
- `Assets/_Project/Editor/Marco.Editor.asmdef` — Editor 전용 어셈블리(`Core`/`Net`/`Presentation`/`FishNet.Runtime` 참조, `includePlatforms: ["Editor"]`)

**설계 원칙(요구사항 그대로)**: `NetworkObject`의 `PrefabId`·`AssetPathHash` 등 FishNet/Unity 에디터가 생성하는 값은 스크립트가 손으로 채우지 않는다. `PrefabUtility.SaveAsPrefabAssetAndConnect`(GUI 드래그와 동일 결과)와 `PrefabUtility.EditPrefabContentsScope` 안에서의 `AddComponent`만 사용하고, 그 값들은 컴포넌트 부착·프리팹 저장 시점에 `NetworkObject.OnValidate`/`Reset`(FishNet 자체 로직, `Assets/FishNet/Runtime/Object/NetworkObject/NetworkObject.cs`)이 스스로 채우도록 맡긴다.

**확인한 공개 API(사전 조사, 추측 없이 벤더 소스로 검증)**
- `PlayerSpawner.SetPlayerPrefab(NetworkObject)` — public 세터 메서드
- `NetworkManager.SpawnablePrefabs` — public get/set 프로퍼티(`NetworkManager.QOL.cs`)
- `PrefabObjects.AddObject(NetworkObject, checkForDuplicates, initializeAdded)` — FishNet 공식 스폰 가능 프리팹 등록 API. `DefaultPrefabObjects`(`SinglePrefabObjects` 상속)가 구현을 제공하며 `checkForDuplicates: true`로 재실행 시 중복 등록 방지
- `NetworkTransform._synchronizeScale`(private 직렬화 필드, 기본값 true) — §10-3 표대로 Scale만 끄기 위해 `SerializedObject`로 접근(Position/Rotation은 기본값 그대로 두므로 손대지 않음)
- 하위 매니저(TransportManager 등)는 `NetworkManager.Awake()`가 `GetOrCreateComponent`로 런타임에 직접 생성하므로 에디터에서 미리 붙이지 않음(§10-1 원문 그대로)

**멱등성(idempotent)**: 이미 존재하는 `NetworkManager`/`Tugboat`/`PlayerSpawner`/`Player.prefab`/그 위의 컴포넌트는 재사용하고, 이미 등록된 스폰 프리팹은 중복 추가하지 않는다 — 재실행해도 안전하다.

**되돌리기**: 씬 오브젝트 생성·삭제는 `Undo.RegisterCreatedObjectUndo`/`Undo.AddComponent`/`Undo.DestroyObjectImmediate`로 하나의 Undo 그룹에 묶어 Ctrl+Z로 되돌릴 수 있다. 다만 프리팹·에셋 저장(`Player.prefab`, `DefaultPrefabObjects.asset`)은 에디터 Undo 대상이 아니므로, 실행 전 확인 대화상자에서 씬 백업을 명시적으로 권고한다.

**문서 갱신**: `docs/수동검증_절차.md` §10을 "자동화 도구 + 에디터 GUI 절차"로 개편 — §10-0에 도구 사용법을 추가하고, 기존 §10-1~10-5는 도구가 내부적으로 수행하는 작업의 서술로 재배치(도구 실패 시 수동 대체 경로로 남김). §10-6(빌드/접속)·§10-7(검증 체크리스트)은 런타임 절차라 자동화 대상 밖이며 그대로 유지.

**검증 한계**: 이번 세션은 라이브 Unity 에디터/MCP 연결이 없어 실제 메뉴 클릭 실행은 검증하지 못했다. 사용된 UnityEditor API(`PrefabUtility.SaveAsPrefabAssetAndConnect`, `PrefabUtility.EditPrefabContentsScope`, `Object.FindFirstObjectByType(FindObjectsInactive)` 등)는 모두 Unity 6000.x에서 안정적으로 지원되는 표준 API이지만, 다음 에디터 접속 시 실제 클릭 실행으로 최종 확인이 필요하다.

---

### 스프린트 9 후속 2 — NetworkTestBootstrap (임시 H/J 접속 디버그 도구)

로비 UI가 없는 상태에서 로컬 2-클라이언트(에디터 호스트 + 빌드 exe 클라이언트) 네트워크 테스트를 키 입력만으로 시작할 수 있게 하는 임시 도구를 추가했다(§10-6에서 안내하던 "임시 코드 스니펫을 아무 오브젝트에 붙여라" placeholder를 실제 동작하는 컴포넌트로 대체).

**신규 파일**
- `Assets/_Project/DebugTools/NetworkTestBootstrap.cs` — H(호스트)/J(참가) 키 처리 MonoBehaviour
- `Assets/_Project/DebugTools/Marco.DebugTools.asmdef` — `Core`/`Net`/`Presentation`/`FishNet.Runtime`/`Unity.InputSystem`을 모두 참조하는 별도 어셈블리(Editor 전용이 아니라 Play 모드에서도 동작해야 하므로 `includePlatforms: []`)
- `Game.unity`에 `NetworkTestBootstrap` 루트 오브젝트 배치(fileID `900000000000000001`~`3`, `SceneRoots`에 등록) — YAML을 직접 편집해 배치했고, fileID 중복·미해결 참조 0건을 스크립트로 재검증했다(작업 중 신규 fileID가 기존 Graybox 벽 오브젝트의 MeshFilter/MeshRenderer/BoxCollider fileID(`800000000000000100~102`)와 충돌한 적이 있어, 되돌리고 `900000000000000001~3`대로 재배정했다 — 씬 YAML을 손으로 건드릴 때 fileID 충돌 검증이 왜 필수인지 보여주는 사례)

**설계 결정**
- Net(`InstanceFinder`)과 Presentation(`LocalPlayerRegistry`)을 동시에 참조해야 해서, §15.2가 금지하는 "Net↔Presentation 직접 참조"를 어기지 않으려고 **제3의 임시 어셈블리**로 분리했다. 로비 UI가 생기면 `Assets/_Project/DebugTools/` 폴더 전체와 씬의 `NetworkTestBootstrap` 오브젝트를 삭제하면 끝나므로, Net/Presentation 자체의 경계는 건드리지 않는다
- H = `ServerManager.StartConnection()` + `ClientManager.StartConnection()`(호스트), J = `ClientManager.StartConnection("localhost")`(클라이언트 참가) — 둘 다 FishNet 공식 API(`Assets/FishNet/Runtime/Managing/Server/ServerManager.cs`, `.../Client/ClientManager.cs`)를 벤더 소스로 확인 후 사용
- 연결 전 화면이 완전히 비지 않도록 `Camera.main`을 임시로 켜 두고, `LocalPlayerRegistry.WhenReady()` 콜백(스프린트 8에서 만든 지연 바인딩 지점)으로 로컬 플레이어 스폰 시점에 자동으로 끈다 — 새 상태 추적 없이 기존 배선을 재사용
- 기존 P/C 키 디버그 컨벤션(`LocalPulsePipelineBehaviour`)을 따라 New Input System(`Key` enum 필드 + `Keyboard.current`)을 쓰고, `[SerializeField] private Key _hostKey = Key.H` 형태로 키를 하드코딩하지 않았다

**회귀 확인**: `Assets/_Project/Tests/EditMode/**` 전체(186케이스, TaggingTests·ValveInteractionControllerTests·ValveTests·WinConditionEvaluatorTests)를 실제 Unity/FishNet/InputSystem DLL을 참조하는 스크래치패드 하네스로 컴파일 및 실행 — **186 passed, 0 failed**. `NetworkTestBootstrap.cs`를 포함한 전체(Core+Net+Presentation+DebugTools) 컴파일도 별도로 확인해 경고 5건(기존 코드의 미사용 인스펙터 필드 경고, 이번 변경과 무관)을 제외하고 오류 0건.

**문서 갱신**: `docs/수동검증_절차.md` §10-6의 "접속" 항목을 placeholder 코드 스니펫에서 실제 `NetworkTestBootstrap` 사용법(H/J 키, Game 뷰 포커스 필요성)으로 교체.

---

### 스프린트 9 후속 3 — Player 시각적 몸체(캡슐)

원격 플레이어가 화면에 아예 보이지 않던 문제(NetworkTransform은 동기화하지만 렌더링할 메시가 없었음)를 해소하기 위해 `Assets/_Project/Prefabs/Player.prefab`에 캡슐 몸체를 추가했다. 코드 변경은 없다 — 순수 프리팹 데이터(YAML) 편집.

**추가 내용**: `Player` 루트 아래 자식 `Body`(Transform + MeshFilter + MeshRenderer).
- 메시: Unity 내장 Capsule(`fileID: 10208`)
- 스케일 `{0.7, 0.9, 0.7}`, 로컬 위치 `{0, 0.9, 0}` — `CharacterController`(`m_Height: 1.8`, `m_Radius: 0.35`, `m_Center: {0, 0.9, 0}`)와 정확히 일치하도록 계산(내장 캡슐 기본 반경 0.5·높이 2 기준 스케일 0.35/0.5=0.7, 1.8/2=0.9). 이 스케일 값은 §9의 대역 도망자 캡슐(`Runner_A/B/C`, `{0.7, 0.9, 0.7}`)과 동일해 프로젝트 내 기존 관례와도 일치한다
- 머티리얼: 새로 만들지 않고 기존 `GrayboxWall.mat`(URP Lit, 회색, guid `0f719196b517450bb7fd736445f7922a`) 재사용 — Unity 내장 `Default-Material`은 Standard 셰이더라 URP 프로젝트에서 마젠타(에러 색)로 렌더링될 위험이 있어 피했다

**의도적으로 미구현 — 자기 시점 컬링**: 로컬 플레이어가 자기 카메라로 자기 캡슐을 보지 않게 하려면 전용 Layer + `Camera.cullingMask` 배선이 필요한데, 이번 스프린트 목표는 "원격 플레이어가 최소한 보이는지" 확인이라 범위 밖으로 뒀다(사용자 지시대로 판단을 맡아 간단한 쪽 선택). 카메라 로컬 Y(`1.62`)가 캡슐 상단(`1.8`)보다 낮아 자기 시점에서 캡슐 머리와 카메라가 겹쳐 보일 수 있음을 §10-3에 알려진 한계로 문서화했다.

**검증**: `Player.prefab` YAML의 fileID 중복·미해결 참조를 스크립트로 재검증(0건). 프리팹 데이터만 바뀌었으므로 EditMode 186케이스는 그대로 재실행해 회귀 없음을 재확인(186 passed, 0 failed).

**문서 갱신**: `docs/수동검증_절차.md` §10-3에 "④ Body(시각적 몸체)" 항목과 자기 시점 컬링 미구현 안내 추가.

---

## 스프린트 10 — 밸브 서버 권위 동기화 (네트워크 2단계 파일럿, §14.3)

발소리·밸브·태그·라운드를 한 번에 네트워크로 옮기지 않고, **밸브 하나로 "서버 권위 RPC 패턴"을 먼저 확립**했다. 이후 스프린트에서 같은 골격을 태그·탈출/라운드·발소리에 반복 적용한다. 밸브를 고른 이유: 상태 변화가 이산적·저빈도라 첫 패턴 검증에 안전하다.

### 신뢰 모델 (기존 클라이언트 권위 → 서버 권위)

- **이전**: 클라이언트가 자기 화면에서 Core `Valve`를 직접 조작(스프린트 5, `ValveInteractionController`).
- **이후**: 클라이언트는 **홀드 의사만** 서버에 요청(ServerRpc), 밸브를 실제로 돌렸는지는 **서버만** 결정(`ServerValveDriver`가 Core `Valve`를 구동), 확정 상태를 SyncVar로 전 클라이언트에 전파. 클라이언트는 "완료됐다"를 보낼 수단이 없어 **밸브를 즉시 열 수 없다** — 이것이 이 파일럿이 확립한 패턴의 핵심.

### 수정·생성 파일

**신규 (Core)**
- `Assets/_Project/Core/Objectives/IValveHost.cs` — 밸브 오브젝트가 자신의 Core `Valve` 인스턴스를 Net에 노출하는 계약(`GetComponent<IValveHost>()`로 연결, 구체 타입 은닉). `ILocalControlGate`·`IPlayerIdentity`와 같은 패턴.
- `Assets/_Project/Core/Net/IValveNetworkBridge.cs` — Presentation↔Net 계약. `NetworkActive`/`State`/`Progress01` 읽기 + `SubmitHoldIntent(playerId, role, held)`. Core는 FishNet을 모른다.
- `Assets/_Project/Core/Objectives/ServerValveDriver.cs` — **순수 서버 권위 구동기**. Core `Valve`를 감싸 홀드/해제/틱을 관리하고 홀더를 추적한다. 새 규칙 없음(역할 GAP-5·상태·리셋 전부 `Valve` 재사용). FishNet·UnityEngine 미참조 → EditMode로 서버 권위 성질을 직접 검증 가능.

**신규 (Net)**
- `Assets/_Project/Net/ValveNetworkSync.cs` — `NetworkBehaviour, IValveNetworkBridge`. 클라: 홀드 의사가 바뀔 때만 `[ServerRpc(RequireOwnership=false)]`로 전송(디듀프). 서버: `OnStartServer`에서 `IValveHost`로 권위 `Valve`를 잡아 `ServerValveDriver` 생성, `Update`에서 회전 중일 때만 `Time.deltaTime`으로 틱, 상태·진행도를 `SyncVar<ValveState>`/`SyncVar<float>`로 전파. 양쪽 창에서 반영을 눈으로 확인하도록 서버(`[ValveNet:Server]`)·클라(`[ValveNet:Client]`, SyncVar OnChange) 로그를 남긴다.

**신규 (Editor)**
- `Assets/_Project/Editor/NetworkValveSetupTool.cs` — `Tools/MARCO/Setup Network Valves`. 씬의 각 `ValveBehaviour`에 `NetworkObject` + `ValveNetworkSync`를 `Undo.AddComponent`로 부착. **SceneId는 FishNet이 씬 저장 시 자동 생성**하므로 손으로 채우지 않는다(스프린트 8·9 원칙). 멱등.

**수정 (Presentation)**
- `ValveBehaviour.cs` — `IValveHost` 구현. `IsOpen`이 네트워크 활성 시 브릿지의 서버 확정 상태를 읽도록 위임(로컬이면 기존 `Valve` 그대로) → `ValveObjectiveTracker`의 개방 집계가 클라이언트에서도 그대로 맞는다.
- `ValveInteractor.cs` — ① 최근접 밸브가 `NetworkActive`면 로컬 Core를 직접 조작하지 않고 브릿지에 홀드 의사만 전송(로컬 밸브면 기존 경로 그대로). ② **소유권 가드 추가**: 원격 플레이어 프록시가 내 키보드로 밸브를 돌리지 않도록 `_player.IsLocallyControlled`가 아니면 즉시 반환(로컬 단독 실행에선 기본 true라 무해).

### 테스트 결과

- **신규 12케이스** `ServerValveDriverTests` — 서버 권위 계약 고정. 핵심: `RepeatedBeginHold_WithoutServerTick_NeverOpens`(요청 1000회 폭주해도 서버 Tick 없이는 개방 불가 = §5.3 "클라이언트가 임의로 상태 변경 불가"), `ServerTick_ForFullDuration_OpensExactlyOnce`, 단일 홀더·비홀더 해제 무시·GAP-5 메아리 거부 등.
- **회귀**: 기존 186케이스 + 신규 12 = **198 passed, 0 failed**. `ValveTests`(17)·`ValveInteractionControllerTests`(15)는 손대지 않아 로컬 클라이언트 권위 워크플로우가 그대로 보존됨을 확인.
- 검증 방식: 스크래치패드 하네스로 Core+Net+Presentation 컴파일(0 error) + 리플렉션 러너로 198 실행.

### 기획서 대응

- §14.3 `ValveRotateStart/Progress/Opened | Client → Server → All | {valveId, playerId, progress}` — 이번 구현이 이 이벤트에 대응한다. 3개의 개별 RPC 대신 `홀드 의사(ServerRpc) → 서버 구동 → 상태/진행도 SyncVar 전파`로 같은 프로토콜 의도를 구현했다(SyncVar가 "All에게 전파"를 담당).
- §14.4-3 "친구 대상 게임이므로 안티치트 과투자 금지" — 위치·역할 스푸핑 방지를 이번엔 골격만 두고 이월(GAP-16/17)한 근거.
- §15.2 3분할 유지: Net↔Presentation 직접 참조 0, Core 인터페이스 3개로만 연결.

### 스펙 갭 2건 신규 (GAP-16, GAP-17)

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-16** | 서버 권위 밸브의 신뢰 모델을 어떻게 설계하나 | 클라는 홀드 의사만(ServerRpc), 서버가 `ServerValveDriver`로 Core `Valve`를 검증·타이밍, `SyncVar`로 확정 상태 전파. 개방은 **서버 Tick만** 결정 → 즉시 개방 불가 | ① 이산·저빈도 이벤트라 첫 패턴 검증에 안전(발소리 같은 고빈도보다 위험 적음). ② 서버가 타이머를 소유하는 것이 서버 권위의 최소·본질적 형태. ③ **이월**: `ServerRpc`의 `caller`(FishNet 주입)와 클라가 주장한 `playerId` 대조, 역할(role)의 서버 권위화(현재 role은 로컬 직렬화 필드라 서버가 클라 주장을 신뢰 — `RoleAssigned`(§14.3) 네트워크화 시 재검증) |
| **GAP-17** | 상호작용 거리(범위) 판정을 누가 하나 | **클라이언트**가 범위를 판정해 `held`에 반영, 서버는 역할·상태만 재검증 | 서버가 신뢰할 위치 소스가 아직 없다(NetworkTransform 위치는 있으나 밸브-플레이어 거리 서버 재계산은 파일럿 범위 밖). 위치 스푸핑 방지는 §14.4-3 방침대로 후속 과제. 지금은 "서버가 최종 결정권을 가진다"는 골격 확립이 목적 |

### 확립된 패턴을 태그/탈출/발소리에 적용할 때 (다음 스프린트 선행 정보)

1. **소유권 가드는 모든 입력 컴포넌트에 필요하다.** 이번에 `ValveInteractor`에 `IsLocallyControlled` 가드를 넣었듯, `TagDetector`·발소리 방출부도 네트워크화 시 원격 프록시에서 입력을 막아야 한다(현재는 로컬 단독이라 미적용).
2. **이산 vs 고빈도**: 밸브는 SyncVar로 충분했지만, 발소리(초당 2~4개)는 §14.3대로 `SoundPulse` 이벤트를 서버 판정(§5.6) 후 리스너별 `PerceivedPulse`로 개별 전송하는 구조가 필요 — SyncVar가 아니라 `ObserversRpc`/타깃 RPC가 맞다.
3. **진행도 SyncVar의 대역폭**: 현재 `_progress`를 서버 매 틱 세팅한다(회전 3초간 연속). 저빈도 밸브라 무해하지만, 다른 데 적용 시 "시작 타임스탬프만 보내고 클라가 로컬 보간"으로 최적화 여지 있음.
4. **권위 상태기계의 소유권**: 밸브는 씬 NetworkObject라 서버가 자연히 권위를 갖는다. 플레이어 소유 오브젝트(태그 등)는 `RequireOwnership` 기본값(소유자만)과 씬 오브젝트(`RequireOwnership=false`)의 차이를 태스크별로 판단할 것.

### 남은 작업 우선순위 제안

1. **(선행 조건) 실기 검증** — 이번 세션은 라이브 에디터가 없어 FishNet IL 위빙(ServerRpc/SyncVar 코드 생성)과 2-클라이언트 실동작을 확인하지 못했다. 다음 에디터 접속 시 `Setup Network Valves` 실행 → 씬 저장 → H/J로 §수동검증 절차 수행이 **최우선**.
2. 태그 판정 서버 권위화(같은 패턴, `PlayerTagged` §14.3) — 소유권 가드 선반영 필요.
3. 탈출/라운드 결과 서버 권위화(`GameEnd` §14.3) — 판정을 서버 단일 지점으로.
4. 발소리/PerceivedPulse 네트워크화 — 가장 복잡(고빈도·리스너별 차폐), 위 3개로 패턴이 굳은 뒤.
5. 역할 배정 네트워크화(`RoleAssigned`) — GAP-16 이월분(서버 역할 재검증)의 전제.

### 검증 한계

라이브 Unity 에디터/MCP 연결이 없어 다음은 미검증(다음 접속 시 확인 필요): ① FishNet IL 위빙(`ServerRpc`/`SyncVar` 코드 생성) — 스크래치패드는 C# 컴파일까지만 보증. ② 씬 밸브의 NetworkObject SceneId 자동 생성. ③ 2-클라이언트 실동작(한쪽이 돌리면 다른 쪽 반영). 사용된 FishNet API(`SyncVar<T>`, `[ServerRpc(RequireOwnership=false)]`, `NetworkConnection` 주입, `OnStartServer/Network`, `IsServerStarted`/`IsSpawned`)는 전부 벤더 소스(`Assets/FishNet/`)로 확인했다.

---

## 스프린트 10 후속 — ValveNetworkSync NRE 수정 (실기 검증 중 발견)

라이브 에디터에서 `Reserialize NetworkObjects(Prefabs+Scenes)`로 ObjectId 에러를 해결한 뒤, Play는 되지만 아직 H/J로 접속하지 않은 상태(연결 전)에서 `NullReferenceException`이 매 프레임 반복되는 문제가 보고됐다.

### 원인

`NetworkBehaviour.IsSpawned`/`IsServerStarted` 등은 내부적으로 `private NetworkObject _networkObjectCache` 필드를 그대로 역참조한다(`Assets/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.QOL.cs`). 이 캐시는 FishNet이 해당 컴포넌트를 실제로 초기화(프리스폰 준비)할 때만 채워지므로, Play는 됐지만 아직 그 초기화가 일어나지 않은 시점(연결 전 — 로컬 단독 실행도 이 상태에 해당)에 `IsSpawned`/`IsServerStarted`를 직접 호출하면 NRE가 난다.

`ValveNetworkSync.NetworkActive => IsSpawned;`가 이 패턴이었고, `ValveObjectiveTracker.Update()`가 매 프레임 `ValveBehaviour.IsOpen` → `ValveBehaviour.NetworkActive` → `ValveNetworkSync.NetworkActive`를 거치며 매 프레임 예외를 던져 Console을 도배했다. 같은 파일 안에 동일 패턴이 두 곳 더 있었다(`SubmitHoldIntent`의 `IsSpawned` 직접 호출, `Update()`의 `IsServerStarted` 직접 호출 — 후자는 `ValveNetworkSync` 자신의 `Update()`가 매 프레임 무조건 도는 경로라 트래커 유무와 무관하게도 재현 가능했다).

### 수정

`Assets/_Project/Net/ValveNetworkSync.cs` — `NetworkActive`에 `NetworkObject != null` 널 가드를 먼저 검사하도록 수정(`NetworkObject`는 `_networkObjectCache`를 그대로 반환하는 public 프로퍼티라, 역참조 없이 null 여부만 안전하게 읽을 수 있다):

```csharp
public bool NetworkActive => NetworkObject != null && IsSpawned;
```

그리고 `SubmitHoldIntent`·`Update()`의 직접 `IsSpawned`/`IsServerStarted` 호출을 전부 `NetworkActive` 경유로 통일해 같은 NRE가 재발하지 않도록 했다(가드 지점을 한 곳으로 모음).

### 검증

- Core+Net+Presentation 컴파일 0 error(스크래치패드 하네스)
- 기존 198케이스 그대로 재실행 — **198 passed, 0 failed**(순수 null 가드 추가라 Core 로직 영향 없음, `ServerValveDriverTests`는 `ValveNetworkSync`를 직접 다루지 않으므로 신규 테스트는 추가하지 않음)

### 검증 한계

라이브 에디터에서 실제로 재현된 버그를 코드 리뷰 기반으로 수정했다 — 이 세션은 여전히 라이브 에디터 연결이 없어, 수정된 코드가 실제로 NRE를 없애는지는 다음 에디터 세션에서 Play(연결 전 대기 상태 유지) → Console에 예외가 더 이상 안 뜨는지 확인이 필요하다.

---

## 스프린트 10 후속 2 — 밸브 최소 시각 표시 (검증용 색상)

서버 권위 동기화가 Console 로그로만 확인돼 검증이 불편했던 문제를, 밸브 큐브 색으로 상태를 보이게 해 해소했다. 정식 UI/셰이더가 아니라 **검증용 최소 색상 표시**다.

- 닫힘=회색, 회전 중=진행률 따라 회색→노랑 보간(`Color.Lerp`), 열림=초록 고정

### 수정·생성 파일

- **신규** `Assets/_Project/Presentation/Objectives/ValveVisualIndicator.cs` — `ValveBehaviour`의 상태·진행률을 읽어 `MeshRenderer.material.color`를 갱신. 색 3종은 `[SerializeField]`(기본 회색/노랑/초록). 밸브가 3개뿐이라 `Renderer.material`(인스턴스 사본)을 쓴다(T8 성능 이슈는 동시 30개 링 때문이었고 무관). `OnDestroy`에서 인스턴스 사본 정리.
- **수정** `Assets/_Project/Presentation/Objectives/ValveBehaviour.cs` — `State`/`Progress01` 표시용 프로퍼티 추가. `IsOpen`과 **같은 규칙**으로 네트워크면 서버 확정 브릿지값, 아니면 로컬 `Valve`값을 고른다 — **새 계산 없이 기존 값을 읽기만** 한다. 그래서 SyncVar로 전파된 서버 상태가 그대로 색에 반영돼 양쪽 화면이 자동 동기화된다.
- **씬** `Assets/Scenes/Game.unity` — `Valve_Machine/Lifeguard/Boiler` 3개에 `ValveVisualIndicator`를 부착(YAML 직접 편집). 이 컴포넌트는 에디터 생성값(SceneId 등)이 없는 순수 MonoBehaviour라 손으로 안전하게 배선 가능. fileID `910000000000000001~3`, 중복·미해결 참조 0건 스크립트 재검증.

### 회귀

`ValveBehaviour`에 프로퍼티만 추가(로직 변경 없음). Core+Net+Presentation 컴파일 0 error, EditMode **198 passed, 0 failed**(색상 매핑은 단순 `switch`+`Lerp`라 지시서 판단대로 유닛 테스트 생략).

### 검증 방법

`수동검증_절차.md §12-2`에 색 확인 항목 추가: 한쪽에서 홀드하면 양쪽 화면에서 회색→노랑→초록으로 실시간 변하는지. Console 로그 없이도 눈으로 동기화를 확인할 수 있다.

### 검증 한계

라이브 에디터 부재로 실제 색 변화·양쪽 동기화는 미검증(다음 세션에서 H/J로 확인 필요). 씬에 `ValveVisualIndicator`를 하드 배선했으므로, 에디터가 씬을 다시 저장하면 인스펙터에서 색 필드 3개가 보이고 조정 가능하다.

---

## 스프린트 11 — 태그 서버 권위 동기화 (네트워크 2단계, §3.1/§14.3)

스프린트 10(밸브)에서 확립·실기검증한 서버 권위 패턴을 태그에 반복 적용했다. 태그는
밸브보다 단순(홀드/진행률 없는 순간 판정)하지만, **두 주체(술래+대상)**가 있고 **대상의
역할이 바뀐다**는 점이 달라 구조가 조금 더 크다.

### 신뢰 모델 (클라이언트 권위 → 서버 권위)

- **이전**(스프린트 7): 술래 클라이언트가 로컬에서 거리·역할을 판정하고 대상 역할을 직접 Echo로 바꿈.
- **이후**: 술래는 "이 대상을 태그하겠다"는 요청만 서버에 보내고(`ServerRpc`), 서버가 §3.1
  규칙(거리 1.2m·술래→도망자·이미태그됨)으로 **재검증**한 뒤에만 확정. 확정되면 대상의
  `SyncVar<bool>`가 전 피어에 전파되어 대상 역할이 Echo로 바뀐다 — 대상 본인 화면에도 반영.

### 밸브와 달랐던 점 (§6-4)

| 쟁점 | 밸브(스프린트 10) | 태그(스프린트 11) |
|---|---|---|
| 주체 수 | 1(플레이어→밸브) | 2(술래→대상 플레이어) |
| 서버 거리 재검증 | 클라 판정 신뢰(GAP-17) | **서버가 재검증**(§5.3 요구) — RPC 호출자 `NetworkConnection.FirstObject`(술래 플레이어)의 위치로 계산 |
| 판정 규칙 위치 | Core `Valve` (이미 Core) | `TagRules`가 Presentation에 있어 **Core로 이동**해야 Net이 재사용 가능 |
| 대상 열거 | 밸브는 씬 고정 | 대상이 로컬 대역+네트워크 플레이어 혼재 → `TagTargetRegistry`로 통일 |
| 상태 반영 | 밸브 상태 표시 | 대상 **역할 전환**(Echo) → `IRoleState`로 Presentation 역할을 Net이 바꿈 |

### 수정·생성 파일

**Core (신규/이동)**
- `Core/Tagging/TagRules.cs` — **Presentation → Core 이동**(로직 불변). Net 서버가 재사용하려면 Core에 있어야 한다(Net은 Presentation 미참조). Valve·WinConditionEvaluator가 Core인 것과 같은 이유.
- `Core/Tagging/ServerTagDriver.cs` — 순수 서버 검증(`TagRules` 재사용 + 이미태그됨). 테스트 대상.
- `Core/Net/ITagTarget.cs` — "태그 대상" 통일 계약(PlayerId/Role/IsTagged/WorldPosition/NetworkActive/RequestTag). 로컬 대역·네트워크 플레이어를 TagDetector가 같은 방식으로 다룬다.
- `Core/Net/TagTargetRegistry.cs` — 대상 중앙 등록소 + `TargetTagged` 이벤트(라운드 집계 통지). `LocalPlayerRegistry` 패턴을 Core에 둔 것. Play 진입 시 static 리셋.
- `Core/Net/IRoleState.cs` — 역할 읽기 + `ApplyRole`(네트워크 확정 역할 적용). Net이 대상 역할을 Echo로 바꾸는 통로.

**Net (신규)**
- `Net/TagNetworkSync.cs` — `NetworkBehaviour, ITagTarget`(Player에 부착). 클라: `RequestTag` → `[ServerRpc(RequireOwnership=false)]`. 서버: `caller.FirstObject`로 술래 위치를 얻어 `ServerTagDriver.Validate`(거리 재검증 포함), 통과 시 `SyncVar<bool>` 확정. 전 피어: OnChange → `IRoleState.ApplyRole(Echo)` + `TagTargetRegistry.NotifyTagged`. **NetworkActive에 `NetworkObject != null` 가드를 처음부터** 넣어 스프린트 10 NRE 재발 방지.

**Presentation (수정)**
- `Tagging/TagDetector.cs` — ① **소유권 가드**(`IsLocallyControlled`, 원격 프록시 입력 차단) ② 씬 스캔 대신 `TagTargetRegistry` 순회 ③ 직접 조작·RoundCoordinator 등록 제거, `RequestTag`만 호출.
- `Tagging/TaggableRunner.cs` — `ITagTarget` 구현(로컬 대역, `NetworkActive=false`, 즉시 확정). 레지스트리 등록.
- `Player/FirstPersonController.cs` — `IRoleState` 구현(`ApplyRole` 세터). 태그 확정 시 서버가 이 플레이어를 Echo로 전환.
- `GameFlow/RoundCoordinator.cs` — `TagTargetRegistry.TargetTagged` 구독 → 서버 확정 태그를 집계. TagDetector 직접 호출을 대체(로컬/네트워크 공통 경로, "서버 확정 기준").
- `GameFlow/RoundOutcomeTracker.cs` — `using` 갱신(TagRules 이동).

**DebugTools (수정)**
- `NetworkTestBootstrap.cs` — **K 키 추가**: 로컬 플레이어를 술래로 지정(태그 검증용). 역할 배정이 아직 네트워크화 안 돼(다음 스프린트) 2-클라 테스트에서 술래를 만들 방법이 필요.

**Editor (수정)**
- `Editor/NetworkPlayerSetupTool.cs` — Player 프리팹 셋업에 `TagNetworkSync` 부착 추가(멱등). NetworkBehaviour의 ComponentIndex·NetworkObject.NetworkBehaviours는 FishNet이 관리하므로 **YAML 직접 편집이 아니라 이 도구(AddComponent)로** 붙인다.

### 테스트 결과

- **신규 13케이스**: `ServerTagDriverTests`(9) — 핵심 `OutOfRange_IsRejectedByServer`(§5.3 거리 밖 요청을 서버가 거부), 역할·이미태그됨·경계값. `TagTargetRegistryTests`(4) — 등록/통지/세션 리셋.
- **회귀**: 기존 198 + 신규 13 = **211 passed, 0 failed**. `TaggingTests`(17: TagRules 이동 후 using만 갱신)·`ServerValveDriverTests` 등 전부 통과.
- 검증: 스크래치패드 하네스로 Core+Net+Presentation 컴파일(0 error) + 리플렉션 러너 211 실행.

### 스펙 갭 1건 신규 (GAP-18)

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-18** | 태그 서버 권위의 검증 범위와 역할·라운드 연동 | 서버가 **거리(1.2m)·대상 역할·이미태그됨을 재검증**(밸브 GAP-17보다 강화 — §5.3 요구). 술래 역할과 라운드 집계 분모는 이월 | ① 거리는 `caller.FirstObject`(술래 플레이어)의 동기화된 위치로 서버가 재계산 가능해 재검증에 넣음. ② **이월**: 술래 role은 아직 서버 권위 아님(로컬 필드라 클라 주장 신뢰 — `RoleAssigned`(§14.3) 네트워크화 시 재검증). 클라 권위 NetworkTransform 위치 자체의 스푸핑도 이월(GAP-17과 동류). ③ `RoundCoordinator`의 전원태그 분모(`_totalRunners`)는 여전히 대역 기준 — 라운드 결과 네트워크화(다음 스프린트) 몫. 태그 **집계는 서버 확정분만** 각 피어가 반영하므로 카운트는 일치 |

### 실기 검증 (Reserialize 필요 여부)

이번엔 태그가 **이미 NetworkObject가 있는 Player 프리팹**에 NetworkBehaviour(TagNetworkSync)를 추가하는 것이라, 스프린트 10처럼 씬에 새 NetworkObject를 만들지는 않는다. 다만 **프리팹에 NetworkBehaviour를 추가**하면 FishNet이 ComponentIndex·NetworkObject.NetworkBehaviours 목록을 다시 만들어야 하므로, `Setup Network Player` 재실행 후 **`Fish-Networking → Utility → Reserialize NetworkObjects(Prefabs+Scenes)`가 필요할 가능성이 높다**(스프린트 10에서 밸브에 필요했던 것과 같은 이유). 라이브 에디터 부재로 실제 2-클라 태그 동작·위빙은 미검증 — 다음 세션에서 확인 필요.

### 다음 스프린트 선행 정보 / 우선순위

1. **탈출/라운드 결과 네트워크화** — GAP-18 이월분(전원태그 분모, 라운드 결과 일치)을 여기서 해소. `EscapePointTrigger`도 소유권 가드 + 서버 권위로.
2. **발소리/PerceivedPulse** — 가장 복잡(고빈도·리스너별 차폐). SyncVar 아닌 타깃 RPC(§14.3).
3. **역할 배정 네트워크화**(`RoleAssigned`) — GAP-16/18 이월분(술래·대상 역할 서버 권위)의 전제. 이게 되면 `NetworkTestBootstrap`의 K키 디버그를 제거할 수 있다.

### 검증 한계

라이브 에디터/MCP 부재로 미검증: ① FishNet IL 위빙(ServerRpc/SyncVar 코드 생성) ② Player 프리팹에 TagNetworkSync 추가 후 Reserialize·인덱스 재구성 ③ 2-클라 실동작(술래가 러너 태그 → 러너 화면 역할 전환). C# 컴파일(0 error)·유닛 211 통과·FishNet API(`NetworkConnection.FirstObject`, `SyncVar<T>`, `[ServerRpc(RequireOwnership=false)]`) 벤더 소스 확인은 완료.

---

## 스프린트 12 — 탈출/라운드 결과 서버 권위 동기화 (네트워크 2단계)

스프린트 10(밸브)·11(태그)에서 확립·실기검증한 서버 권위 패턴을 **라운드 전체**(탈출·타이머·최종 판정)에 적용했다. 지금까지 각 클라이언트가 로컬 타이머를 돌리고 §6.3을 독립 계산하던 것을, **서버 하나만의 단일 판정**으로 통일해 GAP-18 이월분(전원태그 분모·크로스 클라이언트 판정 일치)을 해소한다.

### 신뢰 모델 (클라이언트 독립 판정 → 서버 단일 판정)

- **이전**(스프린트 6): 클라이언트마다 `RoundTimer`를 돌리고 `RoundCoordinator`가 로컬에서 `WinConditionEvaluator`를 호출. 타이밍 차이로 클라이언트별 결과가 갈릴 위험.
- **이후**: **서버**가 `ServerRoundDriver`로 타이머를 소유하고, 탈출을 재검증하고(§5.3), `WinConditionEvaluator`를 **서버에서만** 호출해 확정한 `RoundResult`·남은 시간·탈출 수를 `SyncVar`로 전 클라이언트에 전파. 클라이언트(`RoundCoordinator`)는 자체 판정을 멈추고 서버값만 반영한다.

### 판정 입력의 출처 (이미 서버 권위인 것을 재사용)

| 입력 | 출처 | 서버 권위 확보 방법 |
|---|---|---|
| 밸브 게이트(개방수·전체·게이트) | `EscapeGateRegistry`(Core) ← `ValveObjectiveTracker` | 밸브 SyncVar(스프린트 10)를 집계한 값이라 이미 서버 권위 |
| 전원 태그 | `TagTargetRegistry`(Core) | 태그 SyncVar(스프린트 11) 집합을 서버가 직접 읽음 |
| 탈출 | `RoundNetworkSync`가 `ServerRpc`로 직접 재검증·집계 | 이번 신규 |
| 타이머 | `RoundNetworkSync`(서버)가 소유 | 이번 신규 |

### 수정·생성 파일

**Core (신규)**
- `Core/GameFlow/IRoundNetworkBridge.cs` — Presentation↔Net 라운드 계약(`NetworkActive`/`RemainingSeconds`/`EscapedCount`/`Result` + `SubmitEscapeIntent`). `IValveNetworkBridge`와 같은 패턴.
- `Core/GameFlow/ServerRoundDriver.cs` — **순수 서버 판정기**(타이머 소유·탈출 재검증·§6.3 위임·1회 래치). `WinConditionEvaluator`를 그대로 호출(변경 없음). FishNet·UnityEngine 미참조 → EditMode 테스트 가능.
- `Core/Objectives/IEscapeGateState.cs` — 밸브 게이트 상태 읽기 계약(`OpenedValves`/`TotalValves`/`IsGateOpen`).
- `Core/Objectives/EscapeGateRegistry.cs` — 게이트 집계기 중앙 등록소(Net이 §15.2 넘어 읽는 통로). `TagTargetRegistry` 패턴을 Core에 둔 것.

**Net (신규)**
- `Net/RoundNetworkSync.cs` — `NetworkBehaviour, IRoundNetworkBridge`. 서버: `OnStartServer`에서 `ServerRoundDriver` 생성, `Update`에서 타이머 틱+판정, `SyncVar<float>`(남은 시간)·`SyncVar<int>`(탈출 수)·`SyncVar<RoundResult>`(최종 결과) 전파. `ServerSubmitEscape`(`[ServerRpc(RequireOwnership=false)]`)로 탈출 재검증. **NetworkActive에 `NetworkObject != null` 가드를 처음부터**(스프린트 10 NRE 재발 방지).

**Presentation (수정)**
- `Objectives/ValveObjectiveTracker.cs` — `IEscapeGateState` 구현 + `EscapeGateRegistry` 등록/해제. 서버가 게이트 상태를 읽는 통로(로직 무변경, 프로퍼티/등록만 추가).
- `GameFlow/RoundCoordinator.cs` — 브릿지 인지형으로. `IsNetworkActive`면 `ReflectServerRound`(서버 남은 시간 로그 + 결과 변화 시 1회 `RoundEnd` 전이)만 하고 로컬 타이머·판정을 멈춘다. `RequestEscape`가 네트워크면 브릿지로, 로컬이면 기존 `TryRegisterEscape`로 라우팅. 전원태그 로컬 집계(`OnTargetTagged→Evaluate`)도 네트워크면 스킵. **로컬 경로는 그대로**(스프린트 6 보존).
- `Objectives/EscapePointTrigger.cs` — ① **소유권 가드**(`IsLocallyControlled`) ② 게이트 개방 사전 필터(RPC 낭비 방지) ③ `TryRegisterEscape` 직접 호출 → `RoundCoordinator.RequestEscape` 라우팅.

**Editor (신규)**
- `Editor/NetworkRoundSetupTool.cs` — `Tools/MARCO/Setup Network Round`. `RoundCoordinator` 오브젝트에 `NetworkObject`+`RoundNetworkSync`를 `Undo.AddComponent`로 부착(멱등). SceneId는 FishNet 자동 생성.

### 테스트 결과

- **신규 22케이스** `ServerRoundDriverTests`(Core) — 타이머 소유(감소·0 클램프·결정 후 정지) · 탈출 재검증(게이트 닫힘/비러너 거부·중복 방지·결정 후 무시) · §6.3 위임+래치(러너승·시간초과·전원태그·미결·**래치**·탈출 우선순위) · **GAP-19 전원태그**(널/빈/미태그러너잔존/전원Echo/술래만/널항목).
- **회귀**: 기존 211 + 신규 22 = **233 passed, 0 failed**. 스프린트 11에서 재구성한 하네스(NUnitLite + Unity 관리 DLL `AssemblyResolve`)로 **실제 실행**.
- **Net 컴파일 검증**: Core+Net를 FishNet.Runtime 참조로 컴파일 → **0 error/0 warning**(`RoundNetworkSync`의 `SyncVar<T>`·`[ServerRpc]`·`NetworkBehaviour` C# 유효성 확인).

### 스펙 갭 2건 신규 (GAP-19, GAP-20)

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-19** | 역할이 아직 네트워크 동기화 안 되는데 "전원 태그" 분모를 어떻게 얻나(GAP-18 이월) | **고정 분모를 쓰지 않는다.** `AllRunnersTagged = (태그된 대상 ≥ 1) && (태그 안 된 Role==Runner 대상 == 0)` | 태그된 대상은 §3.1대로 Echo가 되어 더는 Runner가 아니다. 따라서 "안 태그된 러너 0"이면 전원 태그다 — 총 러너 수를 셀 필요가 없어진다. 서버가 `TagTargetRegistry`(서버 권위 태그 SyncVar 집합)를 그대로 읽어 계산하므로, 역할 배정 네트워크화(미구현) 없이도 크로스 클라이언트 일치가 성립한다. 러너 0명이면 공허한 참 방지로 false |
| **GAP-20** | 탈출의 서버 재검증 범위 | 서버가 **게이트 개방(서버 권위 밸브)·역할(러너)을 재검증**. 탈출 지점까지의 거리는 클라 신뢰(이월) | 게이트 개방은 이미 서버 권위 밸브 상태라 서버가 확실히 안다. 거리 재검증은 서버가 탈출 지점 위치를 알아야 하는데(밸브 GAP-17·태그 위치 스푸핑과 동종) 이번 파일럿 범위 밖. §14.4-3 "친구 대상, 안티치트 과투자 금지" 방침 유지 |

### 밸브/태그와 다르게 처리해야 했던 점

1. **판정 주체가 오브젝트 하나가 아니다.** 밸브(오브젝트당 상태)·태그(대상당 상태)는 대상별 SyncVar였지만, 라운드는 **여러 시스템의 상태를 모아 하나의 결론**을 낸다. 그래서 `RoundNetworkSync`가 밸브 게이트(`EscapeGateRegistry`)·태그(`TagTargetRegistry`)를 **읽어와** 판정하는 집약 구조가 필요했다.
2. **클라이언트의 로컬 판정을 적극적으로 꺼야 했다.** 밸브·태그는 클라가 원래 상태를 안 만들었지만, 라운드는 스프린트 6부터 클라가 타이머·판정을 **이미 돌리고 있었다.** `RoundCoordinator`가 네트워크 활성 시 로컬 타이머 틱·`Evaluate`·`OnTargetTagged` 집계를 전부 우회하도록 명시적으로 게이트했다 — "서버 판정 하나만 신뢰"의 핵심 요건.
3. **게이트 상태를 Net이 읽는 새 통로**가 필요했다. 태그는 대상이 스스로 등록(`ITagTarget`)했지만, 밸브 게이트 집계는 Presentation(`ValveObjectiveTracker`)에만 있어 `IEscapeGateState`+`EscapeGateRegistry`로 Core에 노출했다.

### 실기 검증 (다음 세션 필요)

라이브 에디터/MCP 부재로 미검증 — 다음 세션에서 확인:
1. `Tools/MARCO/Setup Network Round` 실행 → 씬 저장 → **`Fish-Networking → Utility → Reserialize NetworkObjects` 필요**(RoundCoordinator 오브젝트에 새 씬 NetworkObject 추가 — 스프린트 10 밸브와 동일 이유).
2. 세 종료 시나리오 각각 **양쪽 클라이언트 결과 일치**: (a) 게이트 개방 후 러너 탈출 → RunnersWin (b) 시간 초과 → SeekerWin (c) 전원 태그 → SeekerWin.
3. 남은 시간이 양쪽에서 **서버 동일 값**으로 로그되는지(`[Round] … (서버 권위)`), 게이트 미개방 탈출을 서버가 거부하는지(`[RoundNet:Server] 탈출 거부`).
4. **주의(스프린트 11 K/R 교훈)**: 전원 태그 시나리오는 술래가 러너 전원(원격 + 씬 대역 `TaggableRunner`)을 태그해야 성립한다 — 대역이 씬에 남아 있으면 그들도 러너 모집단에 포함된다(GAP-19).

### 남은 작업 우선순위 제안

1. **발소리/PerceivedPulse 네트워크화** — 가장 복잡(고빈도·리스너별 차폐). SyncVar 아닌 타깃/ObserversRpc(§14.3). 이제 이산 이벤트(밸브·태그·라운드)는 전부 서버 권위가 됐다.
2. **역할 배정 네트워크화**(`RoleAssigned`) — GAP-16/18/19/20 이월분(역할·위치 서버 권위)의 전제. 되면 `NetworkTestBootstrap`의 K/R 디버그와 씬 대역 러너를 제거할 수 있다.

---

## 스프린트 13 — 역할 배정 네트워크화 (`RoleAssigned`, §6.2/§14.3)

지금까지 역할은 로컬 디버그 키(K/R)로만 지정 가능했고, 스프린트 12 실기에서 **Player 프리팹 `_role` 기본값이 원격 피어가 보는 역할을 결정**하던 한계가 드러났다. 이번 스프린트로 **서버가 §6.2 표대로 배정하고 전 피어가 일관되게 인지**하도록 만들었다.

### 1. 인원수별 역할 배분 — 기획서에서 찾음 (임의 생성 없음)

지시서가 "표를 찾으면 그대로 쓰고 임의로 만들지 않는다"고 했고, **찾았다**. 두 곳이 서로 일관된다:

| 인원 | 도망자 | 술래(= 인원 − 도망자) | 출처 |
|---|---|---|---|
| 2 | 1 | 1 | §6.2 표(AI 술래 권장 — v1.x) |
| 3 | 2 | 1 | §6.2 표(v1.x) |
| **4** | **3** | **1** | **§6.2 표 — MVP 대상** |
| 5 | 4 | 1 | §6.2 표(v1.x) |
| 6 | 5 | 1 | §6.2 표(v1.x) |

보강 근거: **용어집** "리스너(Seeker) | 술래 역할. **1인**" / "도망자(Runner) | … 3~5인", **§1 게임 개요** "인원 최소 2 / 권장 4~5 / 최대 6 (v1.x: 8~10인, **술래 2인**)".

→ 규칙은 **"술래 1인 고정, 나머지 전원 도망자"**로 표 전 행에서 직접 도출된다(`인원 − 도망자 = 1`이 5개 행 모두 성립). 술래 2인은 v1.x 8~10인 전용이라 MVP 범위 밖. **메아리(Echo)는 초기 배정 대상이 아니다** — §3.1상 "태그당해 탈락한 도망자"이므로 게임플레이 이벤트로만 도달한다.

### 2. 수정·생성 파일

**Core (신규 1)**
- `Core/Role/RoleAssigner.cs` — §6.2 표의 순수 구현. `SeekerCount=1`·`MinimumPlayers=2`·`MaximumPlayers=6` 상수, `CanAssign`/`SeekersFor`/`RunnersFor`/`RoleForOrder`. FishNet·UnityEngine 미참조.

**Net (신규 1 / 수정 1)**
- `Net/RoleNetworkSync.cs` (신규) — Player 프리팹의 `NetworkBehaviour`. `SyncVar<RoleType>`(배정 역할) + `SyncVar<bool>`(배정 여부) → OnChange에서 `IRoleState.ApplyRole`. 스폰 목록을 `internal static Spawned`로 노출(소비자도 Net이라 Core 레지스트리 불필요). **Echo 가드**·**NRE 가드**·늦은 스폰 즉시 반영 포함.
- `Net/RoundNetworkSync.cs` (수정) — 서버 `Update`에 `EnsureRolesAssigned()` 추가. 미배정 플레이어가 있을 때만 OwnerId 오름차순 정렬 후 `RoleAssigner.RoleForOrder`로 배정(무할당 재사용 버퍼).

**Editor (수정 1)**: `NetworkPlayerSetupTool.cs` — Player 프리팹에 `RoleNetworkSync` 부착 추가(멱등, ⑤번 항목).
**Tests (신규 1)**: `RoleAssignerTests.cs` — 31케이스.

### 3. 테스트 결과

- **신규 31케이스** `RoleAssignerTests` — 핵심은 **§6.2 표 재현 고정**(`RunnersFor_MatchesDesignDocTable` 2→1, 3→2, 4→3, 5→4, 6→5). 그 외: 술래 항상 1명·합계=인원·상수값(1/2/6) 검증 · GAP-21(1명 이하는 배정 없이 러너 유지 = 로컬 폴백 보존) · GAP-22(순서 0번이 술래, 음수 인덱스는 러너, **Echo는 절대 배정 안 됨**, 인원 늘어도 기존 술래 불변).
- **회귀**: 기존 233 + 신규 31 = **264 passed, 0 failed** (NUnitLite 실제 실행).
- **Net 컴파일**: Core+Net를 FishNet.Runtime 참조로 컴파일 → **Build succeeded, 0 Error / 0 Warning**.

### 4. 스펙 갭 3건 신규 (GAP-21 ~ GAP-23)

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-21** | 2인 행이 "1 (AI 술래 권장) — AI 술래 구현 후에만 유효"인데, 실기 테스트는 2인(호스트+클라)이다 | 2인이면 **인간 술래 1 + 러너 1**로 배정. 1명 이하면 **배정하지 않고 러너 유지** | §6.2 표의 "도망자 1"은 그대로 지키고 AI 자리에 인간을 넣는 것이 가장 보수적인 해석(표의 수치를 바꾸지 않는다). 1명일 때 술래로 만들면 탈출(GAP-11 러너만)·전원태그 판정이 로컬 단독 실행에서 깨지므로, 최소 인원 미만은 배정하지 않아 스프린트 3~7 워크플로우를 보존한다 |
| **GAP-22** | "**누구를** 술래로 뽑는가"가 기획서에 없음 | **OwnerId 오름차순 첫 1명**(결정론적 순서) | ① 서버 권위 검증·EditMode 테스트에 유리(무작위는 시드 동기화 필요). ② 새 플레이어는 항상 더 큰 OwnerId라 **기존 술래가 바뀌지 않는다** — 재배정 안정성이 공짜로 따라온다. ③ §2.2의 "술래 로테이션 3판 1세트"가 순서 개념을 이미 함의(로테이션 오프셋은 후속 확장 자리). 부작용: 호스트(OwnerId 0)가 항상 술래 — 실기 검증에는 편리하나 로테이션 구현 시 해소 대상 |
| **GAP-23** | 배정을 **언제** 확정하나 — MVP에 로비 준비완료가 없다 | **미배정 플레이어가 있으면 즉시 (재)배정**(멱등) | 명확한 "라운드 시작" 트리거가 없어 스폰 완료 시점을 배정 시점으로 삼았다. GAP-22의 결정론적 정렬 덕에 재배정이 기존 배정을 흔들지 않고, 같은 값이면 SyncVar를 건드리지 않아 대역폭도 0이다. 정식 로비 UI가 생기면 "준비완료 → 배정 1회"로 좁힌다 |

### 5. 초기 배정 ↔ 태그 전환(Echo) 충돌 방지 — 지시서 §2 요구사항

`ApplyRole` 호출부가 이제 3곳(배정·태그·디버그)이라 서로 덮어쓸 위험을 구조적으로 막았다:

| 경로 | 시점 | 값 |
|---|---|---|
| `RoleNetworkSync`(신규) | 라운드 시작 시 1회 | Seeker 또는 Runner **only** |
| `TagNetworkSync`(스프린트 11) | 태그 확정 시 | Echo |
| `NetworkTestBootstrap` K/R | 수동(디버그) | Seeker/Runner |

- **Echo 가드**: `ApplyAssignedRole()`이 `IsTaggedOut`(= `ITagTarget.IsTagged` 또는 현재 역할 Echo)이면 **반영을 건너뛴다.** 초기 배정 SyncVar(Runner)가 뒤늦게 도착해 태그 결과(Echo)를 되돌리는 것을 막는다 — 두 SyncVar의 도착 순서·컴포넌트 콜백 순서에 의존하지 않는다.
- **서버 측에서도 제외**: `EnsureRolesAssigned()`가 `IsTaggedOut`인 플레이어를 배정 대상에서 빼되 **인원 수 계산에는 포함**한다(라운드에 참가한 사람이므로 §6.2 분모가 흔들리지 않는다).
- 태그는 단방향(Runner→Echo)이고 되돌아가지 않으므로, 이 두 가드로 충돌이 사라진다.

**배정 플래그를 따로 둔 이유(스프린트 12 교훈 반영)**: `SyncVar<RoleType>`의 기본값은 열거형 0번 = **Seeker**다. 플래그 없이 값만 보면 배정 전 전원이 술래로 보인다 — 스프린트 12 실기 버그(프리팹 `_role: 0`)와 **똑같은 함정**이라, `_hasAssignment`가 true일 때만 반영한다.

### 6. 실기 검증 (다음 세션 필요)

라이브 에디터/MCP 부재로 미검증 — `docs/수동검증_절차.md §15`에 절차서로 남겼다:
1. `Setup Network Player` 재실행(프리팹에 `RoleNetworkSync` 추가) → **`Reserialize NetworkObjects` 필요**(프리팹에 NetworkBehaviour 추가 시 ComponentIndex 재구성 — 스프린트 11과 같은 이유).
2. 2-클라 접속 시 **K를 누르지 않아도** 호스트=술래, 원격=러너로 자동 배정되고 양쪽 로그가 일치하는지.
3. 태그 발생 시 역할 전환(Echo)이 초기 배정과 충돌 없이 동작하는지(스프린트 11 회귀).
4. 밸브 GAP-5(메아리 거부)·전원태그(GAP-19)가 실제 배정 역할 기준으로 동작하는지.
5. 로컬 단독 실행(1명) 시 배정 없이 러너 유지(회귀).

### 7. K/R 디버그 키 제거를 위해 남은 작업

지시서대로 **이번엔 제거하지 않고 공존**시켰다(검증 경로 보존). 제거 조건:
- [ ] 위 실기 검증 2~4번 통과(자동 배정이 K 없이 동작함을 확인)
- [ ] 씬 대역 러너(`TaggableRunner` 3명) 처리 방침 결정 — 이들은 `RoleNetworkSync`가 없어 배정 대상이 아니지만 `TagTargetRegistry`에는 등록되므로 GAP-19 전원태그 모집단에 포함된다. 실제 플레이어만으로 전원태그를 검증하려면 대역을 씬에서 제거하거나 비활성화해야 한다.
- 제거 시 삭제 대상: `NetworkTestBootstrap`의 `_becomeSeekerKey`/`_becomeRunnerKey`/`SetLocalRole`, 수동검증 부록의 K/R 행, §13-2의 K 안내.
- **H/J 접속 키는 남긴다**(정식 로비 UI 전까지 필요).

### 8. 남은 작업 우선순위 제안

1. **발소리/PerceivedPulse 네트워크화** — 마지막 미동기화 시스템이자 가장 복잡(고빈도·리스너별 차폐). SyncVar 아닌 타깃/`ObserversRpc`(§14.3). 이제 역할까지 서버 권위이므로 §5.7 역할별 인지 배율을 **실제 배정 역할**로 계산할 수 있다.
2. **K/R 제거 + 씬 대역 러너 정리** (위 §7 조건 충족 후 짧게).
3. **술래 로테이션**(§2.2 "3판 1세트") — GAP-22 부작용(호스트 고정 술래) 해소. 라운드 종료 → 재시작 흐름이 생길 때.

### 검증 한계

라이브 에디터/MCP 부재로 미검증: ① FishNet IL 위빙(`SyncVar<RoleType>`·`SyncVar<bool>` 코드 생성) ② Player 프리팹에 `RoleNetworkSync` 추가 후 Reserialize·ComponentIndex 재구성 ③ 2-클라 실동작(자동 배정·양쪽 인지 일치·태그 전환 공존). C# 컴파일(Core+Presentation+Tests 0 error, Net 0 error/0 warning)·유닛 **264 실제 실행 통과**·FishNet API(`SyncVar<T>`·`OnStartNetwork`/`OnStopNetwork`·`OwnerId`) 벤더 소스 확인은 완료.

---

## 스프린트 14 — 발소리 펄스 네트워크화 (네트워크 2단계 마무리, §5.6/§14.3)

마지막 미동기화 시스템. 지금까지 로컬 스모크 리그(고정 청취자 id=999, 스프린트 3~9)로만 돌던 파문 파이프라인이 **처음으로 실제 다른 플레이어에게 전달**된다.

### 왜 지금까지와 구조가 다른가

밸브·태그·라운드·역할은 전부 "상태 하나를 전원에게 똑같이" 전파하는 문제라 `SyncVar` 하나로 끝났다. 파문은 **GAP-2**(벽 0개면 좌표, 아니면 방위만)와 **GAP-3**(청취자별 재판정) 때문에 같은 소리라도 청취자마다 내용이 다르다 — 술래 A는 좌표, 러너 B는 방위만, 러너 C는 아예 미수신. 그래서 브로드캐스트가 아니라 **`[TargetRpc]`로 청취자 한 명에게만** 보낸다. §14.3이 이 구조를 명시한다:

| 이벤트 | 발신 | 수신 | 이번 구현 |
|---|---|---|---|
| `SoundPulse` | Client → Server | Server(내부 판정용) | `ServerSubmitPulse`(ServerRpc) — 종류만 전송 |
| `PerceivedPulse` | **Server → 해당 리스너만** | 개별 클라이언트 | `TargetPulseDelivery`(TargetRpc) — 판정 통과자에게만 |

§14.3 주석("차폐로 걸러진 리스너는 아예 수신하지 않음")도 그대로 성립한다 — `SoundPulseResolver`가 null을 반환하면 델리버리 자체가 생기지 않는다.

### 확인부터: 필요한 것이 이미 다 있었다

지시서가 "새로 판정 로직을 짜지 않는다"고 했고, 실제로 **판정·상태관리·시각화를 하나도 새로 만들지 않았다**:

1. **`ActivePulseTracker.Tick(now, listeners, probe)`가 이미 청취자 목록을 받는 시그니처**였다. 주석에 "Net 레이어가 매 Tick 구성해 넘긴다"고 적혀 있다 — T7 시점부터 이 구조를 전제로 설계된 것.
2. **청취자별 트래커 인스턴스가 불필요하다**(지시서 Step 3의 질문). `TrackedPulse.LastResults`가 이미 `Dictionary<청취자ID, PerceivedPulse?>`라, 트래커 하나가 청취자별 직전 결과를 구분 보관한다. 서버 인스턴스 1개로 전원 처리.
3. **`PulseVisualRenderer.Apply(in PulseDelivery, float now)`가 그대로 재사용 가능**했다. 수신 델리버리를 로컬 판정 결과와 **완전히 같은 경로**로 넣는다 — 시각화 코드 추가 0줄. 인터페이스(`IPulseDeliverySink`) 시그니처를 기존 `Apply`에 맞췄다.
4. **시계 동기화가 불필요하다**. 렌더러가 `PerceivedDuration`으로 자체 만료 타이머를 돌리므로(스프린트 4 결론), 수신 측 로컬 `Time.time`을 넘기면 된다.

### 수정·생성 파일

**Core (신규 3)**
- `Core/SoundPulse/ServerPulseDriver.cs` — 서버 권위 구동기. `TryGetPulseSpec`(§5.1 표 서버 재계산)·`AddPulse`·`Tick`(트래커 위임). FishNet·수명주기 미참조 → EditMode 테스트 가능.
- `Core/Net/IPulseNetworkBridge.cs` — 발생원 계약(`NetworkActive`/`SubmitPulse(SoundType)`) **+ `IPulseDeliverySink`**(수신 소비자 계약, T8 `Apply`와 동일 시그니처).
- `Core/SoundPulse/PulseNetworkRegistry.cs` — 서버가 §15.2를 넘어 Presentation 구현체 2개(차폐 프로브·렌더러)에 닿는 지연 바인딩 지점.

**Net (신규 1)**
- `Net/PulseNetworkSync.cs` — 씬 `NetworkBehaviour`. `[ServerRpc(RequireOwnership=false)]`로 소리 수집 → 서버가 `RoleNetworkSync.Spawned`로 청취자 스냅샷(ID·위치·**배정된 역할**) 구성 → `ServerPulseDriver.Tick` → 델리버리마다 `[TargetRpc]` 개별 전송. 무할당 재사용 버퍼·NRE 가드 포함.

**Presentation (수정 2)**
- `SoundPulse/LocalPulsePipelineBehaviour.cs` — 네트워크 활성 시 ① 로컬 트래커 틱 **정지**(이중 판정·스모크 리그 결과 혼입 방지) ② 발소리·밸브 소음을 `SubmitPulse(type)`로 서버에 전달 ③ 차폐 프로브를 레지스트리에 등록. 렌더러 `Tick`은 양쪽 경로에서 계속 돌린다(자연 만료 처리).
- `SoundPulse/PulseVisualRenderer.cs` — `IPulseDeliverySink` 구현 + 레지스트리 자기등록(`OnEnable`/`OnDisable`). **렌더 로직 무변경.**

**Editor (신규 1)**: `Editor/NetworkPulseSetupTool.cs` — `Tools/MARCO/Setup Network Pulse`(PulseSystem 오브젝트에 `NetworkObject`+`PulseNetworkSync` 부착, 멱등).
**Tests (신규 1)**: `Tests/EditMode/ServerPulseDriverTests.cs` — 20케이스.

### 테스트 결과

- **신규 20케이스** `ServerPulseDriverTests` — §5.1 표 재현 3건(걷기 2m/0.4s·질주 6m/0.8s·밸브 12m/3s) · 미지원 종류 거부 5건(음성 3등급·노크·AddPulse 거부) · **클라가 반경을 부풀릴 수 없음**(`AddPulse_ServerRecalculatesRadius_ClientCannotInflate`) · **청취자별 결과 분기** 5건(근/원거리·GAP-1 자기제외·GAP-2 좌표공개/비공개·하드블로커 미전달) · **§5.7 역할별 인지 배율**(술래만 듣는 거리·술래가 더 큰 반경) · 자연 만료 무통지 · 차폐 발생 시 Disappeared.
- **회귀**: 기존 264 + 신규 20 = **284 passed, 0 failed**(NUnitLite 실제 실행).
- **Net 컴파일**: Core+Net를 FishNet.Runtime 참조로 컴파일 → **Build succeeded, 0 Error / 0 Warning**.

### 기획서 대응

| 기획서 | 이번 구현 |
|---|---|
| §14.3 `SoundPulse` Client → Server(내부 판정용) | `ServerSubmitPulse` ServerRpc |
| §14.3 `PerceivedPulse` **Server → 해당 리스너만** | `TargetPulseDelivery` TargetRpc |
| §14.3 주석 "차폐로 걸러진 리스너는 아예 수신하지 않음" | Resolver가 null → 델리버리 미생성 |
| §5.1 표(걷기 2m/0.4s, 질주 6m/0.8s, 밸브 12m/회전내내) | `TryGetPulseSpec`가 서버에서 재계산 |
| §5.6 리스너별 차폐 판정 | `SoundPulseResolver` 재사용(무변경) |
| §5.7 역할별 인지 배율 | **스프린트 13 배정 역할**(`RoleNetworkSync.CurrentRole`)로 계산 |
| §14.4-2 "리스너별 차폐 레이캐스트를 매 이벤트마다 감당하는 서버 부하" | 6인×초당 2~4개 = 초당 10~20회(§5.6 주석의 예상 범위 그대로) |

### 스펙 갭 2건 신규 (GAP-24, GAP-25)

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-24** | 클라이언트가 보낸 파문 정보를 서버가 얼마나 신뢰하나 | 클라는 **소리 종류(`SoundType`)만** 주장. 반경·지속은 서버가 §5.1 표에서 재계산, 발생 위치는 `caller.FirstObject`의 서버 측 위치, 발생원 ID는 `caller.ClientId` | ① §5.1 표가 이미 Core 상수(`LocomotionConfig`·`Valve.SoundRadiusMeters`)로 있어 재계산이 공짜다. ② 그래서 클라는 반경을 부풀리거나(테스트로 고정) 남의 발소리로 위장할 수 없다 — 밸브 GAP-17·태그 거리 재검증보다 **강한** 서버 권위. ③ 남은 신뢰: "소리가 났다"는 사실 자체와 종류(걷기↔질주 위장). 이동 상태를 서버가 재계산하려면 `LocomotionSimulator`를 서버에서 돌려야 하는데 그건 예측·보정 스코프라 이월 |
| **GAP-25** | 밸브 소음(§5.1 12m)을 이번 스코프에 넣을지 | **넣는다.** `SoundType.Valve`도 같은 경로로 서버가 재계산·전송 | 지시서 포함 목록은 "발소리"지만, 네트워크에서 로컬 판정을 끄면 **밸브가 무음이 되어** §6.1 "의도된 유인 장치" 설계가 붕괴한다(회귀). 기존 `EmitPulse`가 이미 범용이라 추가 비용이 없다. 스프린트 5에서 같은 판단(§6.1 본문 동작이고 빼면 설계가 성립 안 함)을 한 전례를 따랐다. 회전시간은 4인 MVP 기준 `Valve.DefaultRotationSeconds`(3초) — 6인 보정(3.75초)은 §6.2상 v1.x |

### 이전 스프린트와 다르게 처리한 점

1. **와이어 레벨 GAP-2 강제**: `PerceivedPulse`를 구조체 통째로 보내지 않고 필드를 원시값으로 분해해 전송한다. 좌표 비공개 판정이면 `hasSourcePos=false`+`Vector3.zero`를 보내 **비공개 좌표가 페이로드에 아예 실리지 않는다**. `PerceivedPulse` 문서의 의도("클라이언트에 정밀 좌표를 아예 보내지 않기 위함")를 전송 계층까지 관철한 것이며, 부수적으로 FishNet의 `Nullable<Vector3>` 직렬화 지원 여부에 의존하지 않게 된다(라이브 위빙 검증 불가 상황에서의 안전한 선택).
2. **판정 주체가 씬 오브젝트 하나**: 밸브(오브젝트당)·태그/역할(플레이어당)과 달리, 파문은 서버 트래커 1개가 전원의 상태를 들고 있어야 GAP-3 재판정이 성립한다. 그래서 씬 오브젝트(PulseSystem)에 붙였다.
3. **로컬 판정을 명시적으로 정지**: 라운드(스프린트 12)와 같은 문제 — 클라가 이미 로컬로 판정하고 있었으므로, 네트워크 활성 시 로컬 틱을 끊어야 스모크 리그의 고정 청취자 결과가 화면에 섞이지 않는다.
4. **성능**: `ActivePulseTracker.Tick`이 매 호출 `List<PulseDelivery>`를 새로 할당한다(기존 Core 코드, 리팩토링 금지 대상). 6인·초당 10~20회 판정에서는 무해하다고 판단해 그대로 뒀다 — 지시서의 "과도한 최적화 금지"에 따름. 청취자 스냅샷 버퍼는 재사용해 프레임 할당을 없앴다.

### 실기 검증 — ✅ 완료·확정 (2026-07-26, 스프린트 14 후속 디버그 세션에서 확인)

실기 1차 실패(씬에 `PulseNetworkSync` 미부착, 아래 후속 항목 참고)를 잡은 뒤, `[PulseNet:Judge]` 진단 로그로 재검증해 **완전히 정상 동작을 확인했다**:

- **§5.1 반경 판정이 소수점 단위로 정확**: 걷기 반경 2m 기준, 거리 1.90m는 통과·2.10m는 탈락 — 판정 경계가 스펙값과 정확히 일치.
- **질주(6m) 반경도 정상**: Shift 이동 시 더 먼 거리에서도 시각 효과가 정상적으로 나타남을 확인.
- **초기 오인의 원인**: "태그 거리(1.2m)까지 붙어야 겨우 보였다"는 관측은 버그가 아니라 **걷기 반경(2m) 자체가 태그 거리(1.2m)와 가까운 값**이라 체감상 그렇게 느껴진 것이었다. GAP-24(서버가 §5.1 표에서 재계산)·GAP-2(좌표/방위 분기)·`[TargetRpc]` 개별 전송 전부 설계대로 동작.

이로써 이 시점 기준 **스프린트 14(파문 네트워크화)는 코드뿐 아니라 실기까지 검증 완료**다. 아래 §16 체크리스트 중 미확인으로 남은 항목(밸브 소음 GAP-25 등)은 후속 세션에서 확인.

### 네트워크 2단계 완료 여부

**코드 계층은 완료.** 이벤트 동기화 대상 6종이 전부 서버 권위가 됐다:

| 시스템 | 스프린트 | 방식 | 실기 |
|---|---|---|---|
| 이동(Transform) | 8 | NetworkTransform | ✅ 검증됨 |
| 밸브 | 10 | SyncVar + ServerRpc | ✅ 검증됨(2026-07-26) |
| 태그 | 11 | SyncVar + ServerRpc(거리 재검증) | ✅ 검증됨(2026-07-26) |
| 라운드 결과·타이머·탈출 | 12 | SyncVar + ServerRpc | ✅ 검증됨(2026-07-26) |
| 역할 배정 | 13 | SyncVar | ✅ 검증됨(2026-07-26) |
| **파문(발소리·밸브 소음)** | **14** | **ServerRpc + TargetRpc(청취자별)** | **✅ 검증됨(2026-07-26)** |

§14.3 이벤트 테이블에서 남은 것은 로비 계열(`JoinRoom`·`ReadyToggle`)·음성(§5.2)·메아리 노크(미구현 능력)·`ItemUse`(v1.x)·`HostMigration`(v1.x 제외)뿐이고, 전부 별도 스코프다. **네트워크 2단계(이벤트 동기화)는 이 시점 기준 코드·실기 양쪽으로 완전히 닫혔다**(위 표 전체 ✅ — 상세 근거는 "스프린트 10~13 실기 검증 완료 확인" 섹션 참고).

### 남은 작업 우선순위 제안

1. **정식 UI**(§12 HUD·결과 화면) — 네트워크 2단계가 닫혔으므로 다음 단계 최우선 후보. 현재 Console 로그·IMGUI 임시 구현을 대체.
2. **K/R 제거 + 씬 대역 러너 정리** — 스프린트 13 §7 조건 충족 후(→ 스프린트 15에서 처리 완료).
3. **음성 파이프라인**(§5.2/§5.8) — Steam Voice 원시 PCM → 진폭 → 소리 등급. §14.4-2가 지목한 난제.
4. **술래 로테이션**(§2.2) — GAP-22 부작용 해소.

### 검증 한계

라이브 에디터/MCP로 **2-클라 실동작(원격 발소리 시각화·좌표/방위 전환·§5.7 배율)을 확인 완료**(위 실기 검증 항목 참고). 남은 미검증: ① 씬 PulseSystem에 새 NetworkObject 추가 후 Reserialize 필요 여부의 세부 확인 ② 밸브 소음(GAP-25) 원격 전달. C# 컴파일(Core+Presentation+Tests 0 error, Net 0 error/0 warning)·유닛 **284 실제 실행 통과**·FishNet API(`[TargetRpc]` 시그니처·`ServerManager.Clients`(`Dictionary<int, NetworkConnection>`)·`NetworkConnection.ClientId`/`FirstObject`·`OwnerId => Owner.ClientId`) 벤더 소스 확인은 완료. **`[TargetRpc]`(이번에 처음 쓴 RPC 종류)의 IL 위빙도 실기로 정상 동작이 확인됐다** — 청취자 ID(OwnerId)와 발생원 ID(caller.ClientId)가 같은 번호 공간임을 벤더 소스로 확인한 전제(GAP-1 자기제외)가 실기에서도 그대로 성립했다.

---

## 스프린트 14 후속 — 실기 1차 실패 원인 규명: 씬에 PulseNetworkSync 미부착 (2026-07-26)

### 보고된 증상

`Setup Network Pulse` + `Reserialize NetworkObjects` + 재빌드까지 했는데도 **발소리 시각 효과가 자기 화면에만 나타나고 상대 화면에는 전혀 안 보인다.** 연결 중에도 `[Pulse] 디버그 청취점 고정 … (id=999, 임시 스모크 리그)` 로그가 계속 찍힌다.

### 증상 자체가 원인을 가리킨다

**자기 화면에 자기 발소리가 보인다는 것이 결정적 증거다.** 네트워크 경로에서는 GAP-1(자기 제외)로 발생원 자신은 자기 파문을 **받지 않는다** — 즉 자기 화면에 보인다는 건 로컬 스모크 리그(고정 청취자 id=999)가 여전히 돌고 있다는 뜻이고, `IsNetworkActive == false`라는 의미다.

### 원인 확정 — 코드가 아니라 씬 자산 상태

씬 파일(`Assets/Scenes/Game.unity`)을 GUID로 직접 조회한 결과:

| 컴포넌트 | GUID | 씬 출현 |
|---|---|---|
| NetworkObject | `26b716c4…` | ✅ PulseSystem에 있음 |
| RoundNetworkSync(스프린트 12) | `41b8a6db…` | ✅ PulseSystem에 있음 |
| **PulseNetworkSync(스프린트 14)** | `39001efa…` | ❌ **씬 전체에 0회** |

`LocalPulsePipelineBehaviour.Awake`의 `GetComponent<IPulseNetworkBridge>()`가 **null**을 받아 `IsNetworkActive`가 영구히 false가 되고, 발소리가 계속 로컬 경로로 처리됐다. 관찰 증상과 정확히 일치한다.

**왜 이렇게 됐나**: 셋업 도구들은 `EditorSceneManager.MarkSceneDirty`로 **dirty 표시만** 하고 저장은 사용자가 해야 한다(설계상 안전 장치). 저장을 놓치면 변경이 사라지고, **빌드는 저장된 씬을 사용**하므로 재빌드해도 반영되지 않는다. `Reserialize`는 이미 존재하는 NetworkObject만 갱신하므로 이 누락을 메워주지 않는다.

> 참고: 사용자 질문 중 "PulseNetworkSync가 Player 프리팹에 붙어 있는지"는 설계와 다르다 — 이 컴포넌트는 **씬 PulseSystem 오브젝트**에 붙는다(서버 트래커 1개가 전원의 GAP-3 재판정 상태를 들고 있어야 하므로). Player 프리팹에는 `TagNetworkSync`·`RoleNetworkSync`가 붙는다(확인 결과 둘 다 정상 부착됨).

### 확인: 네트워크 판단 조건과 타이밍은 정상이었다

사용자가 의심한 "BindPlayer가 너무 이른 시점에 실행돼 네트워크 상태가 고정되는 것"은 **아니다**:

- `Awake`에서 캐시하는 것은 **컴포넌트 참조(`_bridge`)뿐**이고, 네트워크 상태는 캐시하지 않는다.
- 판단 조건은 `IsNetworkActive => _bridge != null && _bridge.NetworkActive` 하나이며 **매 프레임 새로 평가**된다. 그래서 스폰이 늦어도(BindPlayer가 먼저 실행돼도) 스폰 완료 시점에 자동 전환된다.
- 다만 `[Pulse] 디버그 청취점 고정` 로그가 **네트워크 여부와 무관하게 항상 찍히도록** 되어 있어(청취점을 미리 준비만 하는 것) 오진을 유발했다 — 이번에 문구를 "준비"로 바꾸고 "실제 사용 여부는 `[Pulse:Diag]` 경로 로그로 판단"을 명시했다.

### 추가한 진단 (요청대로 로직 변경 없음 — 관측만)

**Presentation** `LocalPulsePipelineBehaviour`
- **①** `Awake`에서 브릿지 부착 여부 1회 로그. 미부착이면 `LogWarning`으로 조치 안내(툴 실행 + **씬 저장** + Reserialize).
- **②** `[Pulse:Diag]` 경로 로그 — 2초 주기 + **경로 전환 순간 즉시**. `경로={서버 권위|로컬 스모크} | 브릿지={없음|미스폰|스폰됨} | 차폐프로브 | 렌더싱크`.
- 오해를 부른 스모크 리그 로그 문구 정정.

**Net** `PulseNetworkSync`
- **③** `OnStartNetwork`에서 스폰 확인(`objectId`·서버/클라 컨텍스트).
- **④** **첫 ServerRpc 수신 1회 로그**(`★`) + 폐기 사유 경고(FirstObject 없음 / §5.1 표에 없는 종류).
- **⑤** 서버 상태 2초 요약 — **청취자 수**·추적 파문 수·차폐프로브 등록 여부·누계(수신 RPC / 개별 전송).
- **⑥** **첫 TargetRpc 수신 1회 로그**(`★`) + 싱크 미등록 경고. 커넥션 조회 실패 시 경고.

**Editor (신규)** `NetworkSetupDiagnostics.cs` — `Tools/MARCO/Diagnose Network Setup`
- 씬(파문·라운드·밸브)과 Player 프리팹(NetworkObject·OwnershipGate·Tag·Role)의 부착 상태, **씬 dirty 여부**를 한 번에 점검하는 읽기 전용 메뉴. 이번 같은 "툴 실행 후 저장 누락"을 런타임 전에 즉시 잡는다.
- 보너스: 프리팹 `_role` 기본값이 Runner인지도 검사 — 스프린트 12 실기 버그(전원이 술래로 보임) 재발 감지.

### 검증

- **회귀 284 passed / 0 failed**(로그·카운터 추가뿐이라 판정 로직 무영향).
- **전 어셈블리 컴파일 0 error / 0 warning** — Core+Presentation+**Net**+**Editor**+**DebugTools**를 한 번에 컴파일해 확인(Editor·DebugTools까지 검증한 것은 이번이 처음).

### 다음 실기 세션 절차

1. `Tools/MARCO/Diagnose Network Setup` → 누락 항목 확인.
2. `Setup Network Pulse` 실행 → **반드시 씬 저장(Ctrl+S)** → `Reserialize NetworkObjects` → 재빌드.
3. 다시 `Diagnose Network Setup`으로 전 항목 ✔ 확인(저장 반영 여부까지).
4. H/J 접속 후 `수동검증_절차.md §16-9` 표대로 ①~⑦ 로그를 위에서 아래로 확인 — 처음 끊긴 지점이 원인이다.

---

## 스프린트 10~13 실기 검증 완료 확인 (2026-07-26)

스프린트 14 후속 디버그 세션과 같은 실기 접속에서, 그동안 "실기 검증 대기"로 남아 있던 스프린트 10~13(밸브·태그·라운드·역할 배정)도 **전부 검증 완료됐다.** 아래에 각 스프린트가 문서화 당시 남겼던 "검증 한계"를 사용자가 실기로 직접 확인한 근거로 갱신한다.

| 스프린트 | 시스템 | 실기 확인 근거 |
|---|---|---|
| 10 | 밸브 서버 권위 | 밸브 3/3 개방 확인. 후속 색상 시각화(회색→노랑→초록)까지 **양쪽 화면**에서 동기화 확인 |
| 11 | 태그 서버 권위 | `[TagNet:Server] targetId=… 태그 확정 — 서버 재검증 통과` + `[TagNet:Client] … 메아리로 전환 — 서버 확정 수신` — 서버 재검증→클라 반영 경로 확인 |
| 12 | 라운드/탈출 서버 권위 | `[RoundNet:Server] 라운드 종료 판정 = RunnersWin — 전 피어 전파` 로그로 **크로스 클라이언트 결과 일치** 확인 |
| 13 | 역할 배정 자동화 | K 키 없이 호스트=Seeker/원격=Runner 자동 배정 로그 확인 + 전원태그 시나리오(`SeekerWin`)까지 확인 |
| 14 | 파문 네트워크화 | 위 "스프린트 14" 섹션의 실기 검증 항목 참고(§5.1 반경 판정 정확성 등) |

**이로써 네트워크 2단계(이벤트 동기화) — 이동·밸브·태그·라운드·역할 배정·파문 6종 전부 — 코드와 실기 양쪽에서 완전히 닫혔다.** 아래 스프린트 10~13 원문의 "검증 한계"·"다음 세션 필요" 문구는 작성 시점 기준 정확했던 기록이며, 이 확인으로 대체된다.

---

## 스프린트 15 — 정리: K/R 디버그 키 제거 + 씬 대역 러너 비활성화

새 기능 없음. 스프린트 8~14 네트워크 2단계에서 검증을 도와준 임시 장치를, 그 역할을 대체한 실제 시스템이 자리잡았으므로 걷어냈다.

### 무엇을 왜 걷어냈나

| 임시 장치 | 도입 | 대체한 실제 시스템 | 처리 |
|---|---|---|---|
| `K`/`R` 역할 수동 지정 키 | 스프린트 11 | 스프린트 13 서버 역할 자동 배정(`RoleAssigner`/`RoleNetworkSync`) | **코드 제거** |
| 씬 대역 러너 `Runner_A/B/C`(101~103) | 스프린트 7 | 스프린트 11 실제 원격 플레이어 태그 · 스프린트 14 실제 원격 청취자 | **비활성화**(코드 보존) |

K/R은 단순히 불필요해진 게 아니라 **해로웠다** — 로컬 `_role`만 바꾸므로 서버가 배정한 실제 역할과 어긋나 오진을 유발했다(스프린트 12 실기에서 이미 혼란의 원인이 됐다).

### 수정·생성 파일

- `DebugTools/NetworkTestBootstrap.cs` — `_becomeSeekerKey`·`_becomeRunnerKey` 필드, `SetLocalRole()` 메서드, `Update`의 두 분기, 미사용 `using Marco.Core.Role` 제거. 안내 로그를 "역할은 접속 후 서버가 자동 배정한다 — 수동 지정 키는 없다"로 정정. **`H`/`J`는 유지**(정식 로비 UI 없음).
- `Assets/Scenes/Game.unity` — `Runner_A/B/C` 3개를 `m_IsActive: 0`으로 변경(YAML 직접 편집. 이 오브젝트들은 `NetworkObject`가 없는 순수 MonoBehaviour라 FishNet 생성값이 걸려 있지 않아 안전하다 — 스프린트 10 후속 2와 같은 판단).
- `Tests/EditMode/ServerRoundDriverTests.cs` — 정리 후 모집단을 고정하는 3케이스 추가.
- 문서 3종 갱신.

### 대역 비활성화가 GAP-19 모집단에서 실제로 제외되는지 — 코드로 확인

`TaggableRunner`의 등록 시점이 결정적이다:

```csharp
private void OnEnable() => TagTargetRegistry.Register(this);
private void OnDisable() => TagTargetRegistry.Unregister(this);
```

비활성 상태로 시작하는 GameObject는 **`OnEnable`이 호출되지 않으므로 애초에 등록되지 않는다.** 따라서 `TagTargetRegistry.Targets`에 들어가지 않고, 서버의 `ServerRoundDriver.AllRunnersTagged(TagTargetRegistry.Targets)`가 보는 모집단에서도 빠진다 — **비활성화만으로 충분하며 코드 변경이 필요 없다.**

새 테스트 3건이 이 결과를 고정한다:

| 테스트 | 확인 |
|---|---|
| `AllRunnersTagged_TwoRealPlayers_SeekerPlusTaggedRunner_IsTrue` | 정리 후 2인 구성(술래+태그된 러너)에서 전원 태그 성립 |
| `AllRunnersTagged_TwoRealPlayers_BeforeTag_IsFalse` | 태그 전에는 성립하지 않음(공허한 참 방지) |
| `AllRunnersTagged_StandInsStillCountedIfRegistered_DocumentsCleanupReason` | **대역이 등록돼 있으면** 실제 러너를 다 태그해도 전원 태그가 안 됨 — 이번 정리의 근거를 코드로 남김 |

### 부수 영향 — 로컬 단독 실행의 "전원 태그" 확인 경로

`RoundCoordinator.Awake`의 `_totalRunners = FindObjectsByType<TaggableRunner>().Length`는 기본적으로 **비활성 오브젝트를 제외**하므로 이제 0이 된다. 로컬 경로의 `AreAllRunnersTagged(0)`은 `totalRunners > 0` 가드로 false를 반환한다 — 즉 **로컬 단독 실행에서는 전원 태그 분기가 열리지 않는다.**

이건 회귀가 아니라 일관된 상태다(대역이 없으면 태그할 대상 자체가 없으므로). 로컬에서 그 분기를 눈으로 확인하려면 두 방법이 있고 문서에 명시했다:
1. Hierarchy에서 `Runner_A/B/C`를 직접 활성화(§9 상단 주의 — 네트워크 테스트 전 반드시 되돌릴 것)
2. `Round Coordinator` → `Force All Runners Tagged` 인스펙터 플래그(스프린트 6부터 있던 수동 확인 수단)

### 테스트 결과

- **기존 284 + 신규 3 = 287 passed, 0 failed**(NUnitLite 실제 실행).
- **전 어셈블리 컴파일 0 error / 0 warning** — Core+Presentation+Net+Editor+DebugTools. K/R 제거로 생길 수 있는 미사용 참조·끊긴 호출이 없음을 확인했다.

### 실기 확인 (다음 세션)

라이브 에디터 부재로 미검증:
1. Play + H/J → 안내 로그에 K/R 문구가 없고, 눌러도 아무 반응 없음(코드 제거 확인).
2. 대역 캡슐 3개가 화면에 보이지 않음.
3. `[Tag:Diag] 등록대상` 수가 줄어듦 — 2인 접속 시 **2**(플레이어 2명)여야 한다(이전에는 대역 3 + 플레이어 2 = 5).
4. 술래가 원격 러너 1명을 태그하면 곧바로 `판정: SeekerWin`(§16 시나리오 C가 짧아진다).

### 남은 작업 우선순위 제안

1. **정식 UI/HUD**(§12) — 가장 유력. 네트워크 2단계(이동·밸브·태그·라운드·역할 배정·파문)가 코드·실기 양쪽으로 완전히 닫혔으므로(§"스프린트 10~13 실기 검증 완료 확인" 참고), 다음 단계로 넘어갈 조건이 갖춰졌다. 현재 Console 로그·IMGUI 임시 구현이 검증 수단 전부이고, 로비 UI가 생기면 `DebugTools` 전체(H/J 포함)를 삭제할 수 있다. §12.1 로비·§12.4 결과 화면·타이머 HUD가 대상.

   > 스프린트 14 후속의 "파문 인지 반경" 이슈(6m 기대 vs 1.2m 관측)는 2026-07-26 실기 재검증에서 **버그가 아님이 확정**됐다 — `[PulseNet:Judge]` 로그로 걷기(2m)·질주(6m) 반경 판정이 소수점 단위로 스펙과 정확히 일치함을 확인했고, 초기 관측은 걷기 반경(2m)이 태그 거리(1.2m)와 가까워 생긴 체감 오차였다.

2. **음성 파이프라인**(§5.2/§5.8) — §14.4-2가 지목한 난제.
3. **술래 로테이션**(§2.2) — GAP-22 부작용(호스트 고정 술래) 해소.

---

## 스프린트 16 — 정식 UI 1단계: 인게임 HUD (§12.4)

Console 로그·IMGUI 임시 표시로만 확인하던 정보를 화면 UI로 옮겼다. **이미 서버 권위로 동기화되고 있는 값을 읽어 그리기만 하는 작업** — 새 게임플레이 로직·네트워크 설계가 없다.

### 1. UI 프레임워크 선택 — uGUI(코드 구축)

| 후보 | 판단 |
|---|---|
| **uGUI (`com.unity.ugui 2.5.0`)** | **채택.** 해상도 대응(`CanvasScaler`)·색상 팔레트 연동·향후 확장(§12.5 결과 화면·§12.1 로비)이 필요한 정식 UI 스코프에 맞다 |
| IMGUI(`OnGUI`) | 제외. T8 방위 인디케이터가 임시로 쓰는 방식이고, **프레임당 여러 번 호출되는 비용 문제를 스프린트 4 후속에서 이미 지적**했다(Repaint 필터링으로 완화한 상태) |
| TextMeshPro | **사용 불가**. 이 프로젝트에 **TMP Essentials가 임포트되지 않아** 기본 폰트 자산이 없다 → 런타임 실패 위험. 빌트인 폰트를 쓰는 legacy `Text`를 선택했다 |

**UI 계층을 씬이 아니라 코드로 만든 이유**: Canvas·RectTransform·Font 참조가 얽힌 계층은 씬 YAML 직접 편집이 위험하고(이 프로젝트가 금지한 방식), 에디터 도구로 만들면 "툴 실행 후 씬 저장"이 필요해 **스프린트 14 실기 실패(씬 저장 누락)와 같은 사고가 재발**할 수 있다. 그래서 `InGameHud`가 `Awake`에서 자기 UI 계층을 만든다 — 씬에는 이 컴포넌트 하나만 있으면 되고, 레이아웃·색·표시 토글은 인스펙터로 조정한다. 아이콘·커스텀 폰트가 들어오는 §12.5/§12.1 시점에 프리팹 기반으로 옮기는 것이 자연스럽다(전환 조건을 코드 주석에 남겼다).

### 2. 수정·생성 파일

**Presentation (신규 2)**
- `Presentation/UI/HudFormatter.cs` — 표시 문자열 순수 함수(타이머 `M:SS`·밸브 `⚙ 0/3`·역할·게이트 안내·결과 배너). UnityEngine 미의존 → EditMode 테스트 대상.
- `Presentation/UI/InGameHud.cs` — `MonoBehaviour`. `Awake`에서 Canvas+CanvasScaler+Text/Image 계층을 만들고, `Update`에서 값 5종을 폴링해 갱신. `GraphicRaycaster`는 **일부러 붙이지 않았다**(입력을 받는 UI가 없어 매 프레임 레이캐스트 비용만 생기고 조작을 가로챌 수 있다).

**Presentation (수정 3)**
- `Presentation.asmdef` — `UnityEngine.UI` 참조 추가(기존엔 Core·Unity.InputSystem만 있어 uGUI 타입을 컴파일할 수 없었다).
- `GameFlow/RoundCoordinator.cs` — 표시용 **읽기 전용 접근자** `RemainingSeconds`/`Result`/`EscapedCount` 추가. 새 계산 없이 기존의 네트워크/로컬 소스 선택 규칙을 그대로 노출한다(`ValveBehaviour.IsOpen`과 같은 패턴) — HUD가 그 판단을 중복하지 않게 하려는 것.
- `Objectives/ValveObjectiveTracker.cs` — `Valves` 읽기 전용 배열 노출(집계기가 이미 찾아둔 목록 재사용, 중복 탐색 방지).

**씬 (수정 1)**: `Game.unity` — `PulseSystem` 오브젝트에 `InGameHud` 부착(fileID `920000000000000001`). 순수 MonoBehaviour라 YAML 직접 편집이 안전하다(`ValveVisualIndicator` 선례). 인스펙터 참조 4개(RoundCoordinator·ValveObjectiveTracker·ColorPalette·PulseVisualRenderer)를 함께 배선했다. 문서 351개·중복 fileID 0건 검증.

**Editor (수정 1)**: `NetworkSetupDiagnostics.cs` — `Diagnose Network Setup`에 HUD 점검 추가(부착 여부 + 팔레트 연결 여부).

**Tests (신규 1)**: `Tests/EditMode/HudFormatterTests.cs` — 22케이스.

### 3. 테스트 결과

- **신규 22케이스** `HudFormatterTests` — 타이머 표기 10건(§6.2 600초=`10:00` 등 + **음수 클램프**·**마지막 1초가 `0:00`이 되지 않도록 올림**·올림 경계) · 밸브 카운트 5건(§12.4 도식 `⚙ 0/3` 형식·음수 클램프·**기호 자체 고정**) · 역할 3건(§3 용어집 한국어 명칭) · 게이트 안내 2건 · 결과 배너 3건(진행 중엔 빈 문자열).
- **회귀**: 기존 287 + 신규 22 = **309 passed, 0 failed**(NUnitLite 실제 실행).
- **전 어셈블리 컴파일 0 error / 0 warning** — Core+Presentation+Net+Editor+DebugTools. 검증 하네스에 `UnityEngine.UI.dll` 참조를 추가했다(asmdef 변경 반영).

### 4. 기획서 §12.4 대응 + 스펙 갭 1건 (GAP-26)

§12.4 표에 명시된 5개 요소 중 이번에 구현한 것과 이월한 것:

| §12.4 요소 | 사양 | 이번 스프린트 |
|---|---|---|
| 밸브 카운트 | 좌상단, 소형, 상시 · 도식 `⚙ 0/3` | ✅ **사양 그대로**(기호·형식 포함). 밸브별 상태 표시(닫힘/회전중/개방)를 작은 사각형으로 덧붙임 |
| 아이템 슬롯 | 우하단, 1슬롯 | ❌ 이월 — "찰칵이" 아이템 자체가 미구현(§7) |
| 숨 게이지 | 하단 중앙, 잠수 중에만 | ❌ 이월 — 잠수(§5.9 수면 존)가 미구현("현재 잠수 진입 불가") |
| 방향 게이지(술래 전용) | 화면 가장자리 링 | ✅ **이미 존재** — T8 렌더러의 IMGUI 방위 인디케이터가 이 역할을 수행 중(§3.4). 정식 UI 이관은 후속 |
| 파문 자막(접근성 옵션) | 하단 구석, §12.6 옵션 켜짐 시 | ❌ 이월 — §12.6 설정 화면이 미구현 |

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-26** | 지시서가 요구한 **타이머·역할 표시가 §12.4 표에 없다** | 구현하되 배치를 임의 결정: **타이머 = 상단 중앙**, **역할 = 좌상단 밸브 카운트 아래**(소형) | ① §12.4 도식이 좌상단(밸브)·우하단(아이템)·하단 중앙(숨 게이지)·화면 가장자리(방향 게이지)를 이미 점유하므로 **비어 있는 주요 위치는 상단 중앙**뿐이고, 타이머의 관례적 위치와도 일치한다. ② 역할은 라운드 내내 거의 불변(태그 시 Echo로 1회 전환)이라 크게 상시 노출하면 §16.1 "완전한 흑 배경 + 파문만" 미니멀 원칙에 반한다 → 밸브 카운트와 같은 "내 상태" 그룹으로 좌상단에 소형 배치. ③ 색은 §16.2 팔레트를 그대로 쓴다(술래=레드/도망자=시안/메아리=환경파문색), §12.5의 승패 배너 색 규칙(도망자=시안·술래=레드)과 일관 |

부수 결정(사양 명시 없음이나 §6.1 본문 동작): **배수로 게이트 개방 안내**와 **최소 결과 배너**를 추가했다. 지금까지 Console 로그로만 보이던 §6.1 이정표("밸브 전부 개방 → 탈출 가능")와 §6.3 판정 결과를 화면에서 확인할 수 있어야 HUD가 실용적이기 때문이다. 정식 결과 화면(§12.5)은 다음 단계이므로 중앙 텍스트 한 줄로 최소화했다.

### 5. 구현 결정

| 결정 | 근거 |
|---|---|
| 폴링(`Update`) 사용 — SyncVar `OnChange` 구독 아님 | 이 프로젝트 관례는 OnChange 구독이지만, HUD가 읽는 값은 **여러 소유자에 흩어져 있고**(라운드 브릿지·밸브 3개·로컬 플레이어 역할) 그중 타이머는 매 프레임 변한다. 값마다 콜백을 다는 것보다 한곳에서 읽는 폴링이 단순하고 실수 여지가 적으며, 표시 대상 5개 비용은 무의미하다 |
| 색맹 모드 단일 진실 소스 = T8 렌더러 | `C` 키 토글(§19)이 렌더러에 있으므로, HUD가 `PulseVisualRenderer.ColorblindMode`를 읽어 **함께 전환**된다. HUD가 자체 플래그를 들면 두 표시가 desync된다 |
| 팔레트 미연결 시 하드코딩 색 폴백 | 팔레트가 없어도 HUD가 보이지 않는 사태를 막는다. 진단 도구가 미연결을 경고한다 |
| 빌트인 폰트 3단 폴백 | `LegacyRuntime.ttf`(Unity 2022.2+) → `Arial.ttf`(구버전) → OS 폰트. 폰트가 null이면 글자가 아예 안 보여 원인 파악이 어려우므로 마지막에 경고 로그를 남긴다 |
| 밸브 상태 색 = §16.2 `interactable` | 팔레트의 "상호작용 오브젝트(주변)" 색이 밸브에 의미상 정확히 맞는다. 이 색은 그동안 **정의만 있고 미사용**이었다 |

### 6. 실기 검증 (다음 세션 필요)

라이브 에디터 부재로 미검증 — `수동검증_절차.md §17`에 절차서로 남겼다:
1. Play(로컬 단독) → 좌상단 `⚙ 0/3` + 밸브 3칸, 상단 중앙 타이머(`10:00`부터 감소), 좌상단 역할("도망자") 표시.
2. `E`로 밸브 홀드 → 해당 칸이 진행률만큼 밝아지고 개방 시 카운트 증가.
3. 밸브 3개 전부 → 게이트 안내 문구 노출.
4. `C` 키 → HUD 색과 파문 색이 **함께** 색맹 팔레트로 전환.
5. H/J 2-클라 → 타이머가 양쪽에서 **서버 동일 값**, 역할이 호스트=술래/원격=도망자로 표시, 라운드 종료 시 양쪽에 같은 결과 배너.
6. 폰트 경고(`[HUD] 빌트인 폰트를 찾지 못했습니다`)가 없는지 Console 확인.

### 7. 남은 작업 — 2단계(결과 화면) 착수 조건

- **충족됨**: 결과 값(`RoundCoordinator.Result`)이 이미 서버 권위로 동기화·노출되고, 이번에 최소 배너로 표시까지 확인 경로가 생겼다. §12.5가 요구하는 추가 데이터는 **어워드 3종**(§6.5: 최다 비명상/무성 생존상/최고의 거짓말상)인데, 이는 세션 로그 집계(SoundType.Shout 횟수·이동거리 대비 파문 0회·노크 유인 횟수)가 필요해 **아직 수집되지 않는다**.
- 따라서 2단계는 ① 어워드 집계 없이 승패 배너+재시작만 먼저 만들거나 ② 어워드 집계를 먼저 추가하는 두 갈래가 있다. ①이 범위가 작고 §12.5 골격을 먼저 세울 수 있어 유력하다.
- 그 외 남은 것: 음성 파이프라인(§5.2/§5.8), 술래 로테이션(§2.2), §12.6 설정 화면(색맹 토글을 `C` 키에서 정식 UI로 이관).

---

## 스프린트 17 — 정식 UI 2단계: 결과 화면 + 재시작 골격 (§12.5)

스프린트 16의 HUD 한 줄 배너를 **화면 전체를 덮는 결과 화면**으로 승격하고, 같은 세션에서 다음 라운드를 시작하는 최소 골격을 만들었다.

### 1. 기획서 §12.5 대응 — 무엇이 범위이고 무엇이 아닌가

| §12.5 요소 | 사양 | 이번 스프린트 |
|---|---|---|
| **승패 배너** | "승리 진영 색(도망자=시안 / 술래=레드, 색맹 모드는 §16.2 대체 팔레트)으로 **대형 텍스트**" | ✅ **사양 그대로**(팔레트 색·대형 폰트·색맹 연동) |
| 어워드 카드 3종 | §8 판정 로직 결과를 카드형으로 | ❌ 범위 밖(지시서 명시) — 자리에 "다음 단계에서 집계" 플레이스홀더 한 줄 |
| 사운드맵 타임랩스 | 판의 SoundPulse 로그를 30초로 압축 재생 | ❌ **기획서가 "포스트 MVP"로 명시** |
| 클립 저장 버튼 | mp4/gif 로컬 저장 | ❌ **기획서가 "포스트 MVP"로 명시** |
| 리매치 투표 | "15초 카운트다운, 과반 찬성 시 즉시 재시작" | 🔶 **골격만**(GAP-27) — 투표·과반 없이 키 입력 1회로 즉시 재시작 |

**승패 사유는 새 상태 없이 유도했다.** §6.3 판정식이 사유를 결정론적으로 함의하기 때문이다:
- `RunnersWin` → 첫 분기가 "밸브 전부 + 1인 이상 탈출"뿐이라 사유가 하나로 확정.
- `SeekerWin` → 남은 시간이 0이면 시간 초과, 0보다 크면 전원 태그. 서버 구동기가 판정 확정 시 타이머를 멈추므로(`ServerRoundDriver.Tick`의 `IsDecided` 가드) 확정 시점 값이 보존돼 이 유도가 성립한다.

### 2. 수정·생성 파일

**Presentation (신규 1)**
- `Presentation/UI/ResultScreen.cs` — 전체 화면 결과 UI. 스프린트 16과 **같은 방식**(런타임 uGUI 구축·legacy Text·팔레트 연동)이며, Canvas `sortingOrder=200`으로 HUD(100) 위를 덮는다. 재시작 키 입력 + 오조작 방지 잠금(기본 1초).

**Presentation (수정 3)**
- `UI/HudFormatter.cs` — `FormatResultReason(result, remainingSeconds)` 추가(순수 함수).
- `UI/InGameHud.cs` — `_showResultBanner` 기본값을 **꺼짐**으로(결과 화면과 중복 표시 방지). 씬에서도 0.
- `GameFlow/RoundCoordinator.cs` — `RequestRestart()`(네트워크→브릿지 / 로컬→직접 리셋) · 서버 결과가 승패→`InProgress`로 돌아올 때 종료 래치를 풀고 §15.4 `RoundEnd→RoleAssign→InGame` 전이 · `RestartLocalRound()`. `_outcome`을 재할당 가능하게 변경(래치를 되돌리는 전이가 Core에 없어 인스턴스 교체 방식).
- `Objectives/ValveBehaviour.cs` — `ResetValveForNewRound()`(Core `Valve` 인스턴스 교체).

**Core (수정 2 — 계약 추가만, 판정 로직 무변경)**
- `GameFlow/IRoundNetworkBridge.cs` — `RequestRestart()` 추가.
- `Objectives/IValveHost.cs` — `ResetValveForNewRound()` 추가.

**Net (수정 4)**
- `RoundNetworkSync.cs` — `RequestRestart()` + `[ServerRpc(RequireOwnership=false)] ServerRequestRestart` + `ServerRestartRound()`(전체 초기화 오케스트레이션).
- `ValveNetworkSync.cs` — `Spawned` 목록 + `ServerResetForNewRound()`(호스트의 Valve 교체 후 **구동기도 새로 생성**) + static 세션 리셋.
- `TagNetworkSync.cs` — `Spawned` 목록 + `ServerResetForNewRound()`(태그 SyncVar·1회성 가드 해제) + static 세션 리셋.
- `RoleNetworkSync.cs` — `ServerClearAssignmentForNewRound()` + **`IsTaggedOut` 정정**(아래 §4).

**Editor (수정 1)**: `NetworkSetupDiagnostics.cs` — 결과 화면 부착 여부 + HUD 배너 중복 경고.
**씬 (수정 1)**: `PulseSystem`에 `ResultScreen` 부착(fileID `920000000000000002`), HUD `_showResultBanner: 0`. 문서 352개·중복 fileID 0건 검증.

### 3. 재시작이 없어서 새로 만들어야 했던 것 (GAP-27)

지시서대로 **기존 재시작 지원을 먼저 확인했고, 없었다**:

| 대상 | 확인 결과 |
|---|---|
| `GameFlowManager` | ✅ **이미 지원** — §15.4 전이표에 `RoundEnd → RoleAssign`이 있다. 상태기계는 손대지 않았다 |
| `ServerRoundDriver` | ❌ 판정을 래치하고 되돌리는 경로가 없음 → **새 인스턴스로 교체**(`OnStartServer`와 같은 초기화 재사용 — 초기값 규칙이 두 곳에 흩어지지 않는다) |
| Core `Valve` | ❌ §6.1 상태기계에 **Open → Closed 전이가 없다**(라운드 중 개방은 되돌릴 수 없다는 의도된 설계) → 리셋 전이를 추가하는 대신 **라운드 경계에서 인스턴스 교체**. Core 판정 로직 무변경 |
| `RoundOutcomeTracker` | ❌ 같은 이유로 인스턴스 교체(로컬 경로) |
| 태그·역할 | ❌ SyncVar 되돌리는 경로 없음 → 서버 전용 리셋 메서드 추가(규칙은 기존 경로 재사용) |

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-27** | §12.5 리매치 투표(15초·과반)를 이번에 구현할지 | **투표 없이 즉시 재시작**하는 골격만. 요청은 아무 피어나 보낼 수 있고 **서버만 실행** | ① 투표 UI·과반 판정·15초 카운트다운은 로비(3단계)가 소유할 흐름이고, 지시서가 "완벽한 재시작 흐름을 요구하지 않음"을 명시했다. ② 그래도 **재시작 자체는 서버 단독 수행**이라 스프린트 10~14의 권위 모델을 깨지 않는다. ③ 진행 중 재시작은 거부한다(`IsDecided` 확인) — §12.5 흐름이 아니고, 라운드 중 리셋은 판정 무결성을 흔든다. ④ 술래 재추첨(§2.2 로테이션)은 범위 밖이라 GAP-22의 결정론적 배정대로 **같은 사람이 다시 술래**가 된다 |

### 4. 재시작 구현 중 발견한 실제 버그 — 메아리 고착 (수정함)

`RoleNetworkSync.IsTaggedOut`이 태그 SyncVar **와 현재 역할(Echo)을 함께** 보고 있었다:

```csharp
return (tagTarget != null && tagTarget.IsTagged) || CurrentRole == RoleType.Echo;  // 수정 전
```

재시작 시 태그 SyncVar를 풀어도 `FirstPersonController._role`은 아직 Echo이므로 `IsTaggedOut`이 계속 true → **역할 재배정이 영구히 차단되어 그 플레이어가 메아리로 고착**된다. 태그 상태의 진실은 서버가 확정한 SyncVar이므로, 그것이 있으면 **그것만 신뢰**하도록 정정했다(`ITagTarget`이 없는 구성에서만 현재 역할로 폴백). 스프린트 13의 Echo 가드 의도(SyncVar 도착 순서 무관)는 그대로 유지된다.

**리셋 순서도 이 때문에 중요하다**: ① 태그 해제 → ② 역할 배정 해제 → ③ 밸브 → ④ 라운드 상태. 순서를 바꾸면 같은 고착이 재발한다(코드 주석에 명시).

### 5. 테스트 결과

- **신규 5케이스**(`HudFormatterTests`에 추가) — 사유 유도: 러너 승리는 남은 시간과 무관하게 탈출 · 시간 0이면 시간 초과 · 시간 남으면 전원 태그 · 음수도 시간 초과 · 진행 중이면 빈 문자열.
- **회귀**: 기존 309 + 신규 5 = **314 passed, 0 failed**.
- **전 어셈블리 컴파일 0 error / 0 warning**(CS0618 억제 없이) — Core+Presentation+Net+Editor+DebugTools.

### 6. 실기 검증 (다음 세션 필요)

라이브 에디터 부재로 미검증 — `수동검증_절차.md §18`에 절차서로 남겼다:
1. 로컬 단독: 라운드 종료 → 전체 화면 결과(배너+사유) → `Enter`로 재시작 → 타이머·밸브 초기화 확인.
2. 2-클라: 양쪽에 **동시에 같은 결과**가 뜨는지, 한쪽이 `Enter`를 누르면 **양쪽 모두** 새 라운드로 넘어가는지.
3. 재시작 후 **밸브 색·카운트가 0/3으로, 태그된 플레이어가 도망자로 복구**되는지(§4 메아리 고착 회귀 확인 — 가장 중요).
4. 진행 중 `Enter`는 무시되는지(`재시작 요청 무시 — 라운드가 아직 진행 중`).
5. `C` 색맹 토글이 결과 화면 색에도 반영되는지.

### 7. 남은 작업 — 3단계(로비) 착수 조건 · 어워드 필요성

- **로비(3단계) 착수 조건**: §12.1 로비는 `JoinRoom`·`ReadyToggle`(§14.3 미구현 이벤트)과 방 코드·플레이어 목록이 필요하다. 이번 재시작 골격이 **§12.5 리매치 투표의 자리**를 만들어 뒀으므로, 로비 작업 시 그 골격을 투표로 승격하면 된다. `DebugTools`(H/J)를 삭제할 수 있는 시점도 이때다.
- **어워드 스프린트 필요성 재확인**: §12.5 어워드 3종은 §6.5 판정 기준(`SoundType.Shout` 발생 횟수 · 이동거리 대비 파문 0회 · 노크 유인 성공 횟수)이 필요하다. 그중 **노크는 메아리 능력 자체가 미구현**이라 어워드 3종을 지금 완성할 수 없다. 따라서 순서는 ① 로비 또는 ② 음성 파이프라인(§5.2 — 비명/대화 등급이 어워드 집계의 입력이기도 하다)이 어워드보다 앞서는 것이 자연스럽다.
- 그 외: 술래 로테이션(§2.2 — 재시작이 생겼으므로 이제 의미가 있다), §12.6 설정 화면.

---

## 스프린트 18 — 정식 UI 3단계: 로비 (Ready-up · 시작 게이팅 · 리매치 투표, §12.2/§12.3/§14.3/§15.4)

지금까지와 다른 종류의 스프린트다 — 기존 로직을 옮기는 것이 아니라 **존재하지 않던 상태(플레이어별 준비 여부)와 게이팅(전원 준비 전 시작 금지)을 새로 설계**했다. "서버 시작 즉시 라운드 시작"(스프린트 12~17의 전제)이 폐지되고, §15.4 진행 흐름 전체가 서버 페이즈 상태기계로 처음 구동된다.

### 1. Ready-up 설계 근거 (지시서 §5-1 답변)

지시서가 "RoleNetworkSync와 유사할 가능성 vs 태그의 '모두가 서로를 알아야 함' 구조"를 직접 판단하라 했다. 결론: **둘의 하이브리드**다.

| 측면 | 채택한 패턴 | 이유 |
|---|---|---|
| 복제 | **RoleNetworkSync**(Player 프리팹의 per-플레이어 `SyncVar<bool>`) | SyncVar는 모든 관전자에게 복제되므로 "전원이 서로의 준비 상태를 안다"(§14.3 ReadyToggle의 Server → All)가 추가 비용 없이 성립. 별도 브로드캐스트 불필요 |
| 열거 | **TagTargetRegistry**(Core 레지스트리 `ReadyStateRegistry`) | 로비 UI(Presentation)가 §15.2를 넘어 전원 목록을 그려야 함 |
| 입력 검증 | **신규 — 프레임워크 위임** | `ServerRpc`의 `RequireOwnership` **기본값(true, 벤더 확인)**이 "자기 pawn만 토글"을 강제. 밸브·태그가 `false`+수동 재검증이 필요했던 것과 달리, 기본값이 정확히 맞는 첫 사례 |

서버 게이팅의 입력은 레지스트리가 아니라 Net 내부 실측(`ReadyNetworkSync.Spawned`)이다 — 서버 판정 입력은 Net이 직접 소유한다(스프린트 13 원칙).

### 2. 페이즈 상태기계 — §15.4가 처음으로 전부 돈다

`RoundNetworkSync`에 `SyncVar<GameFlowState>` 페이즈를 추가하고 서버 Update를 페이즈 분기로 재구성했다:

```
Lobby ──(전원 Ready, ServerLobbyDriver)──▶ RoleAssign ──(3초 §12.3, 완료 시 역할 배정)──▶ InGame
  ▲                                            ▲                                          │
  │ 부결: 전원 준비 해제                         │ 가결: 즉시 재시작(Lobby 안 거침)             │ §6.3 판정
  └────────────── RoundEnd (리매치 투표 15초·과반, RematchVoteDriver) ◀────────────────────┘
```

- **§15.4의 양갈래 분기(RoundEnd → Lobby 부결 / RoleAssign 가결)가 처음으로 실사용**됐다 — Phase 2에서 만들어 둔 `GameFlowManager` 전이표가 설계대로 맞아떨어졌다.
- 새 enum을 만들지 않고 기존 `GameFlowState`를 SyncVar로 그대로 실었다.
- 클라이언트(`RoundCoordinator`)는 결과 래치 추론(스프린트 17)을 버리고 **페이즈 미러링**으로 단순화 — 서버 페이즈까지 §15.4 허용 전이만 밟아 이동한다(`NextStepToward`).
- 역할 배정 시점이 "매 프레임(미배정 시 즉시, GAP-23)"에서 **"카운트다운 완료 시점"**으로 이동(§15.4 "RoleAssign: 역할 배정" 그대로). 카운트다운 중단 시 되돌릴 배정이 없도록 끝에서 확정한다. GAP-23은 이로써 폐기.
- 페이즈 가드: 밸브 홀드·태그·탈출 ServerRpc는 `InGame`에서만 유효(`RoundNetworkSync.ServerPhase` 정적). **파문은 게이트하지 않는다** — §12.3 "이 화면 자체가 튜토리얼"(조작 학습 = 로비에서 발소리 확인)이 명시된 기능이다.

### 3. 수정·생성 파일

**Core (신규 5 / 수정 1)**
- `GameFlow/ServerLobbyDriver.cs` — 로비 게이트 순수 구현(§12.3 3초 = §15.4 RoleAssign, GAP-29 중단 규칙, 리매치용 `BeginCountdown`).
- `GameFlow/RematchVoteDriver.cs` — §12.5 15초·과반(n/2+1) 순수 구현. 이탈자는 분자·분모에서 자동 제외.
- `Net/IReadyState.cs` + `Net/ReadyStateRegistry.cs` — 준비 상태 계약·등록소.
- `Net/IConnectionService.cs`(+`ConnectionServiceRegistry`) — 접속 시작 계약(§12.2). DebugTools H/J의 정식 대체 경로.
- `GameFlow/IRoundNetworkBridge.cs` — `Phase`/`CountdownRemaining`/`RematchVotesFor·Needed`/`RematchSecondsRemaining` 추가, `RequestRestart` 의미가 "찬성 투표"로 승격.

**Net (신규 2 / 수정 4)**
- `ReadyNetworkSync.cs` (신규, Player 프리팹) — 준비 SyncVar + `[ServerRpc]`(소유자 전용) + 로비 페이즈 가드.
- `ConnectionService.cs` (신규, 순수 MonoBehaviour) — `InstanceFinder` 호출을 Core 계약 뒤로.
- `RoundNetworkSync.cs` — 페이즈 상태기계 전면 개편(위 §2). `OnStartServer`는 이제 **로비 대기**로 시작.
- `ValveNetworkSync.cs`·`TagNetworkSync.cs` — InGame 페이즈 가드(밸브는 회전 틱도 동결 — 라운드 종료 후 열림 방지).

**Presentation (신규 1 / 수정 3)**
- `UI/LobbyScreen.cs` (신규) — 접속 전(§12.2 H/J)·로비(§12.3 방코드/목록/준비완료 n/m/R 토글)·카운트다운 3상태 화면. sortingOrder 150(HUD 위, 결과 화면 아래).
- `UI/HudFormatter.cs` — 로비·투표 문자열 5종(§12.3 "준비완료 (2/4)" 문구 그대로).
- `UI/ResultScreen.cs` — 재시작 안내를 §12.5 투표 상태("리매치? (1/2 찬성) · 12초 · Enter — 찬성")로 승격.
- `GameFlow/RoundCoordinator.cs` — 페이즈 미러링 + UI 패스스루 5종.

**DebugTools (수정 1)**: `NetworkTestBootstrap.cs` — `ConnectionServiceRegistry.Current`가 있으면 **키 처리 전부 양보**(같은 H/J를 두 곳이 받아 이중 접속되는 것 방지). **제거는 하지 않았다** — 아래 §6.
**Editor (수정 2)**: `NetworkPlayerSetupTool`(⑥ ReadyNetworkSync), `NetworkSetupDiagnostics`(프리팹 Ready + 씬 ConnectionService/LobbyScreen 점검).
**씬**: `PulseSystem`에 `ConnectionService`·`LobbyScreen` 부착(순수 MB — YAML 안전 선례). 문서 354·중복 fileID 0.
**Tests (신규 2 / 수정 1)**: `ServerLobbyDriverTests`(13)·`RematchVoteDriverTests`(15)·`HudFormatterTests`(+9).

### 4. 테스트 결과

- **신규 37케이스** — 로비 게이트 13(3초 상수·최소 인원·전원 준비·1회성 시작 신호·GAP-29 중단 3종·중단 후 전체 재카운트·리매치 직행·솔로 오버라이드·0명 공허 참 방지) · 리매치 15(15초 상수·과반표 5, 즉시 가결·만료 부결·멱등·래치·이탈 분모 축소·2인 만장일치) · 포맷터 9(§12.3/§12.5 문구 고정).
- **회귀: 기존 314 + 신규 37 = 351 passed, 0 failed**(NUnitLite 실제 실행).
- **전 어셈블리 컴파일 0 error / 0 warning**(경고 억제 없음) — Core+Presentation+Net+Editor+DebugTools.

### 5. 기획서 대응 + 스펙 갭 (GAP-28·29, GAP-23 폐기)

| 사양 | 구현 |
|---|---|
| §14.3 ReadyToggle (Client → Server, Server → All) | `ServerSetReady` RPC + SyncVar 복제(playerId는 소유권으로 암묵 검증) |
| §14.3 JoinRoom (roomCode, playerName) | **GAP-28로 축소** — 아래 참조 |
| §12.3 "전원 Ready 시 3초 카운트다운 후 역할 추첨" | `ServerLobbyDriver` + RoleAssign 페이즈, **사양 그대로** |
| §12.3 준비 표시 "준비완료 (2/4)" | `FormatReadyCount` **문구 그대로**(테스트 고정) |
| §12.5 "리매치 투표: 15초 카운트다운, 과반 찬성 시 즉시 재시작" | `RematchVoteDriver` **사양 그대로**(GAP-27 해소) |
| §15.4 RoundEnd → Lobby(부결)/RoleAssign(가결) | 페이즈 상태기계 **표 그대로** |
| §12.3 뮤테이터 투표 | 제외 — 기획서가 **v1.x 명시** |
| §12.3 아바타 마이크 파문 | 제외 — 음성 파이프라인(§5.2) 스코프 |

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-28** | ① 4자리 방코드·playerName(§14.3 JoinRoom)이 Tugboat LAN 직결에는 존재하지 않음 ② 로비를 별도 씬으로 분리할 것인가 | ① 방코드 = **접속 주소로 대체 표시**, 이름 = P{OwnerId}. Steam 전환 시 §12.2 코드 매칭으로 대체 ② **로비 = Game 씬 내 페이즈**로 구현, 씬 분리는 18b로 명시 분할 | ② §12.3 스스로가 로비를 "이 화면 자체가 튜토리얼"(아바타가 걸어다니며 조작 학습)로 정의하고, §10.1 맵의 "입구 로비"가 도망자 스폰 구역이다 — pawn이 실제 맵의 로비 공간에서 파문을 확인하며 기다리는 현 구현이 그 컨셉의 직접 구현이다. 씬 분리는 검증 불가 리스크(아래 §7)가 커 서브 스프린트로 나눴다(지시서 §3 "서브 스프린트 권장") |
| **GAP-29** | 카운트다운 중 준비 해제·이탈·신규 접속의 처리 미명시 | **조건이 깨지면 즉시 중단 → 로비**(전체 3초 재카운트). 준비 토글 자체는 **로비 페이즈에서만** 서버가 반영 | 전원 합의가 유지되는 동안만 진행하는 보수적 해석. 라운드 중 토글을 잠가 리매치 직행 경로(준비 상태 유지 전제)가 성립한다 |
| GAP-23 (폐기) | "미배정 발생 시 즉시 배정" | 로비 게이트 도입으로 **RoleAssign 완료 시점 배정**으로 대체 | §15.4가 명시하는 시점이 생겼으므로 임시 규칙 폐기 |

### 6. NetworkTestBootstrap 제거 — **보류(지시서 규칙대로)**

지시서 §4가 "실기 검증 전 제거 금지"를 명시했고 이 세션은 라이브 에디터가 없어 실기 검증을 수행할 수 없다. 따라서 **양보 가드만 넣고 파일은 보존**했다 — `ConnectionServiceRegistry`에 정식 경로가 등록돼 있으면 키 처리를 전부 건너뛰므로 이중 접속 위험은 없다. **실기에서 §19 절차가 전부 통과하면 다음 정리 커밋에서 `Assets/_Project/DebugTools/` 전체(asmdef 포함)를 삭제**하면 된다(다른 곳에서 참조하지 않음 — 삭제는 폴더 제거만으로 끝난다).

### 7. 씬 전환(18b) — 이번에 하지 않은 것과 이미 확인해 둔 것

씬 전환을 이번에 구현하지 않은 이유: NetworkManager를 다른 씬으로 옮기고 PlayerSpawner 타이밍을 바꾸는 작업은 **에디터 자산 작업 + 실기 검증 없이는 정합성을 보증할 수 없는 조합**이고(스프린트 14의 "씬 저장 누락" 사고가 도구 기반 절차에서도 일어났다), 로비의 게임플레이 기능 전부(준비·게이팅·투표)는 씬 분리 없이도 완결된다. 대신 18b에 필요한 벤더 API를 **전부 이번에 확인해 뒀다**:

| 확인 항목 | 결과(벤더 소스) |
|---|---|
| 전역 씬 로드 | `SceneManager.LoadGlobalScenes(SceneLoadData)` — 서버가 로드하면 전 클라이언트+후발 접속자에 동기화. `LoadConnectionScenes` 3종 오버로드도 존재 |
| 씬 교체·오브젝트 이동 | `SceneLoadData.ReplaceScenes = ReplaceOption.All`, `MovedNetworkObjects`(pawn을 새 씬으로 운반) |
| 오프라인↔온라인 자동 전환 | **`DefaultScene` 컴포넌트가 벤더에 존재**(`_offlineScene`/`_onlineScene`, 서버 시작 시 `LoadGlobalScenes` 자동 호출) — 손으로 짜지 않아도 된다 |
| NM 중복 처리 | `NetworkManager.PersistenceType` 기본값 **DestroyNewest** — 기존 NM이 살아남고 새 씬의 사본이 파괴됨(MainMenu에 NM을 둬도 Game 씬 NM과 공존 규칙이 명확) |
| 스폰 타이밍 | `PlayerSpawner`는 `SceneManager.OnClientLoadedStartScenes`에서 스폰 — 로비 씬 분리 시 이 훅을 커스텀 스포너로 대체해야 함(핵심 작업 항목) |

18b 권장 경로: MainMenu(오프라인, NM+DefaultScene) → 온라인 씬 = Game 유지(최소) 또는 Lobby 경유(정식, `MovedNetworkObjects`로 pawn 운반). 세부는 실기 검증과 함께.

### 8. 실기 검증 (다음 세션 — 이번 스프린트에서 가장 중요)

`수동검증_절차.md §19`에 전체 절차를 남겼다. 핵심 확인 항목:
1. **선행**: `Setup Network Player` 재실행(프리팹에 `ReadyNetworkSync` 추가) → **Reserialize NetworkObjects** → 씬 저장 → `Diagnose Network Setup` 전 항목 ✔.
2. H/J(이제 LobbyScreen 경유) 접속 → 로비 화면(플레이어 목록·준비 상태) → **양쪽 모두 R** → 3초 카운트다운 → 역할 배정 → 라운드 시작.
3. 준비 전에 밸브·태그가 서버에서 거부되는지("홀드 무시 — 라운드 중이 아님"), 발소리 파문은 로비에서도 전달되는지(§12.3 튜토리얼).
4. 카운트다운 중 R(준비 해제) → 중단 → 로비 복귀(GAP-29).
5. 라운드 종료 → 결과 화면에 투표 상태 → **한쪽만 Enter = 재시작 안 됨(과반 미달)** → 양쪽 Enter = 3초 카운트다운 → 재시작. 15초 방치 = 로비 복귀 + 전원 준비 해제.
6. **지시서 §1.6 필수 관찰**: 상대 클라이언트 접속 종료 시 `NetworkObject.OnDestroy` NRE가 **Play 도중** 재현되는지 — 로비/게임 중 각각 끊어보고 Console 기록. 재현되면 별도 조사(스프린트 17 보류 조건 발동).
7. 이탈 시나리오: 로비에서 이탈(목록 갱신), 카운트다운 중 이탈(중단), 투표 중 이탈(분모 축소).

### 9. 남은 작업 우선순위 제안

1. **실기 검증(§19)** — 이 스프린트의 성패가 여기 달렸다. 특히 §1.6 NRE 관찰.
2. **18b: 씬 전환**(§7의 설계 노트 기반) + 검증 후 **DebugTools 삭제**.
3. **술래 로테이션**(§2.2 "3판 1세트") — 리매치가 생겨 이제 자리가 있다. GAP-22(호스트 고정 술래) 해소.
4. **음성 파이프라인**(§5.2/§5.8) — §12.3 로비 아바타 마이크 파문·어워드 집계의 전제.
5. 어워드 3종 · §12.6 설정 화면.

---

## 스프린트 18b — 씬 분리: MainMenu 독립 + 맵 지연 로드 + Lobby↔Game 네트워크 씬 전환 (§15.1/§15.4/§12.2)

스프린트 18의 사양 이탈(로비를 Game 씬 내 페이즈로 구현)을 정정하는 스프린트. **코드·도구는 완성했고, 씬 자산 마이그레이션은 에디터에서 사용자가 실행해야 한다**(아래 §7 — 이 프로젝트가 스프린트 8~14에서 확립한 원칙 그대로).

### 1. 스프린트 18 로직의 씬 독립성 — 코드로 검증 완료 (지시서 §5-1)

지시서가 "스프린트 18의 자체 보고를 그대로 신뢰하지 말고 검증하라"고 했다. 검증 결과 **보고가 정확했다**:

| 파일 | `SceneManagement` 의존 | 씬 오브젝트 탐색 | 판정 |
|---|---|---|---|
| `ServerLobbyDriver.cs` | 없음(using 0개) | 없음 | ✅ 순수 |
| `RematchVoteDriver.cs` | 없음(`System.Collections.Generic`만) | 없음 | ✅ 순수 |
| `ReadyNetworkSync.cs` | 없음 | 없음(자기 SyncVar + 정적 목록) | ✅ 씬 무관 |
| `ConnectionService.cs` | 없음 | 없음(`InstanceFinder`) | ✅ 씬 무관 |

→ **재설계 없이 재배선만으로 충분하다**는 전제가 성립했다. Ready-up·게이팅·투표 로직은 이번 스프린트에서 **한 줄도 바꾸지 않았다.**

### 2. 씬 참조 그래프 조사 — 분리 시 끊어지는 지점을 먼저 특정

씬 YAML의 `fileID` 참조를 전수 조사해 이동 안전성을 확인했다:

| 오브젝트 | 외부 참조 | 이동 가능성 |
|---|---|---|
| **PulseSystem**(라운드 상태기계·HUD·결과·로비 UI·파문·밸브 집계) | **없음** — 모든 참조가 자기 자신의 컴포넌트 또는 팔레트 **에셋** | ✅ 다른 씬으로 이동 안전 |
| NetworkManager | 없음(하위 매니저는 런타임 생성) | ✅ 안전 |
| 밸브 3개 / 대역 러너 | 자기완결적 | ✅ 맵에 잔류 |
| **EscapePoint** | `_roundCoordinator` → **PulseSystem** | ⚠ **유일한 교차 씬 파괴 지점** |

또한 `ValveObjectiveTracker`가 `Awake`에서 밸브를 스캔하므로, **맵이 늦게 오면 빈 배열**이 되는 문제를 확인했다. 이 두 가지가 이번 스프린트의 실제 코드 작업 대상이었다.

### 3. 확인한 FishNet 멀티 씬 API (벤더 소스 — 추측 없음)

| 항목 | 확인 결과 |
|---|---|
| 전역 애디티브 로드 | `SceneManager.LoadGlobalScenes(SceneLoadData)` + `SceneLoadData.ReplaceScenes = ReplaceOption.None`. 전역 씬은 **이후 접속자에게도 자동 적용** |
| 언로드 | `UnloadGlobalScenes(SceneUnloadData)`, `SceneUnloadData(string sceneName)` 생성자 존재 |
| **런타임 로드 씬의 NetworkObject 스폰** | **자동** — `ServerObjects.SceneManager_sceneLoaded`가 `Scenes.GetSceneNetworkObjects` → `InitializeRootNetworkObjects` 수행(서버 시작 상태에서). 맵의 밸브 3개는 로드 시 스스로 스폰된다 |
| NetworkManager 씬 생존 | `_dontDestroyOnLoad = true`(**기본값**) — NM이 씬 전환에 자동 생존. `PersistenceType.DestroyNewest`로 중복 시 새 사본이 파괴됨 |
| 플레이어 스폰 시점 | `PlayerSpawner`가 `SceneManager.OnClientLoadedStartScenes`에서 스폰 → **시작 씬(=Lobby)에서 스폰**되므로 로비에 pawn이 존재한다(§12.3 아바타의 자리) |

### 4. 수정·생성 파일

**Core (신규 1)**
- `GameFlow/SpawnAnchorRegistry.cs` — 맵의 스폰 지점(§10.1 입구 로비)을 씬 간 참조 없이 알리는 레지스트리 + `SpawnPose` 구조체. **Transform이 아니라 좌표 스냅샷 + 식별 토큰**을 보관한다(맵 언로드 시 파괴된 Transform을 붙들지 않기 위함 — 테스트 작성 중 발견한 개선).

**Net (신규 1 / 수정 1)**
- `SceneFlowController.cs` (신규) — §15.1 씬 흐름 구동자. 오프라인 전환(Boot→MainMenu→Lobby)은 Unity `SceneManager`, **맵 로드는 FishNet `LoadGlobalScenes`(애디티브)**. `MapLoaded`로 서버가 로드 완료를 판정.
- `RoundNetworkSync.cs` — 카운트다운 완료 시 **맵 로드를 요청하고 완료를 기다린 뒤** 라운드를 시작한다(§15.4 "InGame: 맵 로드"). 부결로 로비 복귀 시 맵을 언로드. `MapReadyForRound()`는 `SceneFlowController`가 없으면 true를 반환해 **씬 분리 전 배치에서도 그대로 동작**한다(마이그레이션 안전장치).

**Presentation (신규 4 / 수정 3)**
- `UI/MainMenuScreen.cs` (신규) — §12.2 메인 메뉴. 접속 의도만 정적으로 남기고 로비 씬을 로드한다(**접속은 로비 도착 후** — `RoundNetworkSync`가 로비 씬의 씬 NetworkObject라 서버 시작 시 존재해야 하기 때문).
- `GameFlow/LobbyEntry.cs` (신규) — 로비 씬에서 그 의도를 소비해 `IConnectionService`로 접속 시작.
- `GameFlow/BootFlow.cs` (신규) — §15.4 Boot 행(마이크 권한은 §5.2 스코프라 자리표시자).
- `GameFlow/SpawnAnchor.cs` (신규) — 맵의 스폰 지점 표시자.
- `GameFlow/PawnPhaseTeleporter.cs` (신규) — 맵 도착 시 **로컬 소유 pawn만** 스폰 지점으로 이동(NetworkTransform 소유자 권위 준수). `CharacterController`를 잠시 끄는 표준 순간이동 처리.
- `Objectives/ValveObjectiveTracker.cs` — `Rescan()` 추가 + **스스로** 씬 로드/언로드를 구독(Net이 Presentation을 호출하면 §15.2 위반이라 자체 구독으로 설계).
- `Objectives/EscapePointTrigger.cs` — 라운드 지휘부를 **런타임 지연 탐색**(교차 씬 참조 파괴 대응). 못 찾아도 비활성화하지 않는다.
- `UI/InGameHud.cs` — 밸브 핍을 §6.2 최대치(3개) 고정 생성 후 실제 개수만큼 표시. 매 프레임 집계기에서 최신 배열을 읽는다(맵 0↔3 변동 대응).

**Editor (신규 1 / 수정 1)**
- `SceneFlowSetupTool.cs` (신규) — 4단계 마이그레이션 메뉴(`Tools/MARCO/Scene Flow — 1~4`): 드라이런 점검 → Lobby 씬 구성(시스템 이동 + 임시 바닥) → 맵 씬 정리(스폰 앵커) → Boot·MainMenu 구성 + Build Settings 등록. 각 단계 멱등, 파괴적 단계는 확인 대화상자.
- `NetworkSetupDiagnostics.cs` — 씬 흐름 점검 추가(SceneFlowController 유무로 18/18b 배치 구분).

**Tests (신규 1)**: `SpawnAnchorRegistryTests`(8) — 등록/해제/토큰 불일치/맵 재로드 시나리오.

### 5. 테스트 결과

- **신규 8케이스**. **회귀: 기존 351 + 8 = 359 passed, 0 failed**.
- **전 어셈블리 컴파일 0 error / 0 warning**(경고 억제 없음).
- 작성 중 발견·수정: ① `SceneFlowController`가 `ValveObjectiveTracker`(Presentation)를 직접 호출해 **§15.2 경계를 위반**하고 있었다 → 집계기 자체 씬 구독으로 전환. ② 테스트가 `GameObject`/`Quaternion.Euler`(둘 다 네이티브 호출)를 써서 하네스에서 실패 → 레지스트리를 좌표 스냅샷 기반으로 재설계하고 Quaternion을 (x,y,z,w) 생성자로 교체. **이 프로젝트 EditMode 테스트는 Unity 런타임 없이 돌아야 한다는 제약을 다시 확인**한 사례다.

### 6. 기획서 대응 + 스펙 갭 (GAP-30·31)

| 사양 | 구현 |
|---|---|
| §15.1 `Boot → MainMenu → Lobby → Game(맵별 어디티브 로드)` | 4씬 흐름 + **맵을 `ReplaceOption.None`(애디티브)로 로드** — "어디티브"를 문자 그대로 구현 |
| §15.4 `InGame: 맵 로드, 타이머 시작` | 카운트다운 완료 → 맵 로드 → **로드 완료 후** 타이머 시작 |
| §15.4 `RoundEnd → Lobby(부결)` | 맵 언로드 + 전원 준비 해제 |
| §12.2 방 만들기 / 코드 입장 | `MainMenuScreen`(독립 씬) |
| §12.2 솔로 연습장(오프라인 씬) | **제외 — GAP-31로 이월**(지시서 명시) |
| §12.3 로비 = UI 화면 | 로비 씬에 UI만. pawn은 임시 바닥 위에서 대기(§12.3 아바타의 최소 대응) |

| GAP | 쟁점 | 결정 | 근거 |
|---|---|---|---|
| **GAP-30** | 맵 로드 중의 페이즈가 §15.4에 없다(Loading 상태 부재) | **RoleAssign 페이즈를 유지**하며 로드한다. 3초 카운트다운이 끝나도 맵이 안 오면 그 페이즈에 머문다 | §15.4는 RoleAssign을 "3초 연출"로, InGame을 "맵 로드"로 적었지만 **연출 중 로드**가 자연스러운 해석이다(로딩 화면을 새로 만들지 않고 카운트다운 표시를 그대로 쓴다). 라운드 타이머는 로드 완료 후 시작하므로 §6.2 제한시간이 로딩 시간에 잠식되지 않는다 |
| **GAP-31** | §12.2 솔로 연습장(오프라인 씬) | 이번 스코프 제외, 별도 스프린트로 이월 | 지시서 §2가 명시적으로 제외. 마이크 캘리브레이션 겸용이라 §5.2 음성 파이프라인과 함께 하는 것이 자연스럽다 |

### 7. 씬 자산 마이그레이션 — **도구 제공, 실행은 사용자** (가장 중요한 한계)

**이번 스프린트는 씬 파일을 직접 바꾸지 않았다.** 이유:
- NetworkObject의 `SceneId`·`ComponentIndex`는 FishNet/Unity 에디터가 생성하는 값이라 씬 YAML로 손댈 수 없다(스프린트 8~14 원칙). PulseSystem을 다른 씬으로 옮기면 **SceneId가 바뀌므로** 반드시 에디터 API + 씬 저장 + Reserialize를 거쳐야 한다.
- Game 씬은 모든 작업이 의존하는 단일 자산이다. 검증할 수 없는 대규모 씬 수술을 무검증으로 커밋하면 실패 시 전체 작업이 막힌다.

그래서 **마이그레이션을 도구로 만들고**(`SceneFlowSetupTool`, 4단계·멱등·드라이런), 코드는 **분리 전/후 두 배치에서 모두 동작하도록** 작성했다(`MapReadyForRound()`가 `SceneFlowController` 부재 시 true, `EscapePointTrigger`·`ValveObjectiveTracker`가 지연 탐색·재스캔). 즉 **도구를 실행하기 전에도 현재 프로젝트는 스프린트 18 상태로 정상 동작하고**, 도구 실행 후 §15.1 구조로 전환된다.

실행 순서(§20 절차서에 상세):
1. `Tools/MARCO/Scene Flow — 1. 현재 상태 점검` (변경 없음, 무엇이 어디 있는지 확인)
2. `— 2. Lobby 씬 구성(시스템 이동)` → **두 씬 저장(Ctrl+S)** → **Reserialize NetworkObjects**
3. `— 3. 맵 씬 정리(스폰 앵커)` → 저장
4. `— 4. Boot·MainMenu 구성 + 빌드 설정`
5. `Diagnose Network Setup`으로 전 항목 확인

### 8. 실기 검증 (다음 세션) — NRE 재검토 조건 포함

`수동검증_절차.md §20`에 절차서를 남겼다. 핵심:
1. 마이그레이션 4단계 실행 후 `Boot`에서 Play → MainMenu → H/J → Lobby(**맵 미로드 확인**: 그레이박스가 보이지 않고 밸브 카운트가 `⚙ 0/0`) → 양쪽 준비 → 3초 → **맵 로드 확인**(그레이박스 등장 + `⚙ 0/3` + pawn이 §10.1 입구 로비로 이동) → 라운드.
2. 부결(15초 방치) → **맵 언로드 확인**(그레이박스 사라짐, `⚙ 0/0`) → 로비 복귀.
3. 가결 → 맵 유지 + 월드 리셋 → 3초 → 새 라운드.
4. **⚠ 스프린트 17 NRE 재검토 조건**: 접속 종료 **및 이번에 추가된 씬 언로드 시점**에 `NetworkObject.OnDestroy` NRE가 Play 도중 재현되는지 관찰. 맵 언로드는 다수의 씬 NetworkObject(밸브 3개)를 한꺼번에 파괴하므로 **재현 가능성이 가장 높은 새 시나리오**다 — 반드시 기록할 것.
5. `MovedNetworkObjects`는 이번 설계에서 쓰지 않았다(pawn은 시스템 씬에 계속 머물고 좌표만 이동) — 씬 간 오브젝트 운반의 위험을 피한 선택이며, 대신 pawn이 맵 씬에 속하지 않는다는 차이를 확인할 것.

### 9. `NetworkTestBootstrap` 제거 가능 여부

**아직 아니다.** 스프린트 18의 양보 가드가 그대로 유효하고(정식 경로 등록 시 키 처리 건너뜀), §20 실기 검증이 통과해야 제거 조건이 충족된다. 다만 이번에 `MainMenuScreen`이 §12.2 접속 UI를 정식으로 제공하므로, **제거 후에도 접속 경로가 비지 않는다**는 조건은 갖춰졌다.

### 10. 남은 작업 우선순위

1. **§20 마이그레이션 + 실기 검증** — 이번 스프린트의 성패. 특히 맵 언로드 시 NRE 관찰.
2. **DebugTools 삭제**(§20 통과 후).
3. **솔로 연습장**(GAP-31) — §5.2 음성 파이프라인과 묶어서.
4. 술래 로테이션(§2.2), 어워드 3종, §12.6 설정 화면.

---

## 스프린트 19 — DebugTools 완전 제거

**배경**: 스프린트 8에서 만든 임시 접속 도구(`NetworkTestBootstrap`)는 "정식 로비 UI 실기 검증 완료 후 제거"를 매 스프린트 명시적으로 유보해왔다. 스프린트 18/18b로 `MainMenuScreen → LobbyEntry → ConnectionService` 정식 경로가 완성돼 제거 조건이 충족됐다.

### 역방향 참조 검색 (Step 1)

제거 전에 **무엇이 이 도구에 의존하는지**부터 확인했다.

- `NetworkTestBootstrap` / `Marco.DebugTools` / `DebugTools`를 코드·asmdef 전체에서 검색 → **코드 의존 0건**. 걸린 4곳은 전부 XML 주석("이 역할을 임시로 맡았다", "검증 후 제거한다")이었다.
- asmdef 그래프 확인: `Marco.DebugTools`는 Core·Net·Presentation·FishNet·InputSystem을 **참조하지만**, 이 어셈블리를 **참조하는 쪽은 없다**. 단말 노드이므로 삭제해도 다른 어셈블리에 영향이 없다.
- 씬 오브젝트 구성 확인(`Game.unity`): 루트 `NetworkTestBootstrap`(fileID `900000000000000001`)은 **Transform + MonoBehaviour 2개뿐, 자식 없음** → 통째로 삭제해도 다른 배선이 끊기지 않는다.

### 제거 (Step 2~3)

- `Assets/_Project/DebugTools/` 폴더 전체 삭제(스크립트·asmdef·meta 4개 파일).
- 씬 오브젝트는 **YAML을 직접 편집하지 않고** 신규 `DebugToolsCleanupTool`(Editor)로 제거한다 — 스프린트 8~18b에서 확립한 원칙 그대로다.
  - **이름으로 찾는 이유**: 타입이 이미 삭제돼 Editor 어셈블리에서 참조할 수 없다. 대신 이름이 일치하고 **`Transform` + 스크립트가 사라진 컴포넌트(null)만** 가진 루트인지 검사해, 예상 밖의 컴포넌트나 자식이 있으면 **지우지 않고 경고만** 남긴다.
  - `Setup Everything` 파이프라인 **8단계**로 편입(맵 씬을 다시 열어 정리 → 저장).

### 진단·주석 갱신 (Step 4)

- `NetworkSetupDiagnostics`에 **잔재 감지** 추가: 열린 씬에 `NetworkTestBootstrap` 오브젝트가 남아 있으면 ✖로 보고한다(남기면 "Missing script" 경고가 계속 나고 빈 오브젝트가 빌드에 포함된다).
- 접속 서비스 점검 주석을 갱신 — **이제 폴백이 없어 이것이 유일한 접속 경로**임을 명시.
- `IConnectionService`·`ConnectionService`·`LobbyScreen`의 "검증 후 제거 예정" 주석을 완료 시제로 정정.

### 문서 갱신 (Step 5)

`docs/수동검증_절차.md`의 §10-6 접속 절을 **정식 흐름(Boot → MainMenu H/J → Lobby → ConnectionService)**으로 다시 썼다. 히스토리 절(§12~§19)의 "H/J 접속 없이 Play만 하면…" 표현은 **의미가 그대로 유효**(접속하지 않은 상태)하므로 개별 수정 대신 §10-6에 해석 규칙을 한 줄로 명시했다 — 과거 기록을 덮어쓰지 않는 이 문서의 원칙과 일치한다. §19-7의 "통과하면 삭제 가능"은 ✅ 완료로 갱신.

### 검증 (Step 6)

- 런타임(Core+Net+Presentation) / 에디터(경계 분리) / 합본 — **각 0 error, 0 warning**. DebugTools 어셈블리가 사라져도 나머지 컴파일에 영향이 없음을 경계 분리 빌드로 확인했다.
- EditMode 회귀 **368/368 통과**.
- 씬 오브젝트 제거는 **에디터 실행이 필요**하므로 미실시 — `Setup Everything` 또는 `Tools/MARCO/Cleanup Debug Tools` 실행 후 저장이 남아 있다.

### 남은 작업

1. **낙하 한계/리스폰** — 이번 실기에서 "떨어지면 돌아올 방법이 없다"가 확인됐다(§20-6d).
2. 술래 로테이션(§2.2) → 어워드 3종 → §12.6 설정 화면.
3. 솔로 연습장(GAP-31) — §5.2 음성 파이프라인과 묶어서.
4. GAP-19~31 정리.

---

## 스프린트 20 — 낙하 한계 / 리스폰

**배경**: 스프린트 18b 실기 디버깅에서 "낙하 한계·리스폰 로직이 코드 검색 0건"이 실제 증상으로 확인됐다 — 떨어진 플레이어는 돌아올 방법이 없고, 상대 화면에서는 사라져 **접속이 끊긴 것처럼** 보인다.

### 기획서 스펙 확인 (Step 1)

`마르코_상세기획서.md` 전체에서 리스폰·낙하·추락·킬 플레인을 검색한 결과 **플레이어 낙하/리스폰 스펙은 없다**. 걸린 "리스폰"은 §7 **아이템** 리스폰(찰칵이 90초, 소리통)뿐이다. §10.1 맵이 실내수영장(밀폐 구조)이라 애초에 추락이 설계 시나리오가 아니었던 것으로 보인다. → **GAP-32**로 기록하고 최소 안전 구현을 채택했다.

관련 스펙 중 설계에 반영한 것:
- §4.2 `CharacterController` 기반(Rigidbody 물리 동기화 없음) — 감지 방식 선택 근거.
- §4.2 `Diving(수면 아래)` + §10.1 메인 풀 홀 — **잠수가 낙하로 오인되면 안 된다**는 제약.

(부수 발견: §10.1은 "술래는 격리 공간에서 3초 후 별도 진입"이라고 명시하는데 현재 술래·러너가 같은 스폰 링을 쓴다. 이번 범위 밖이라 손대지 않고 기록만 남긴다.)

### 킬 플레인 방식 선택 (Step 2)

**y 임계값 방식**을 택했다. 트리거 콜라이더 대비 근거:

- **터널링 없음**: 중력이 상한 없이 누적돼(`_verticalVelocity += -9.81 * dt`) 몇 초만 지나도 프레임당 이동량이 수 m에 이른다. 얇은 트리거 판은 그대로 통과할 수 있고, 두껍게 만들면 맵 지오메트리와 간섭한다. 좌표 비교는 속도와 무관하게 항상 성립한다.
- **씬 배선 불필요**: 맵마다 트리거를 깔 필요가 없다. 맵이 애디티브로 바뀌는 §15.1 구조에서 맵별 배선은 그대로 유지 비용이 된다.
- **로비에서도 동작**: 맵이 없는 로비 페이즈에도 같은 규칙이 적용된다.
- **비용**: 프레임당 float 비교 1회.

기본값 −10m는 §10.1 바닥(y≈0)과 얕은 수면보다 충분히 아래라 §4.2 잠수와 혼동되지 않는다(테스트가 이 여유를 강제한다).

### 서버 권위 여부 (Step 3)

**소유자(로컬) 처리**로 결정했다. 지시서가 참고하라 한 태그·탈출 서버 권위 패턴과 **성격이 다르다**:

- pawn Transform은 스프린트 8부터 **소유자 권위**(`NetworkTransform`)다. 위치를 정하는 권한은 이미 소유 클라이언트에 있고, 여기서 옮긴 좌표는 기존 동기화로 상대에게 그대로 전파된다.
- 태그·탈출·밸브를 서버가 재검증하는 이유는 그것들이 **라운드 결과를 바꾸기** 때문이다. 리스폰은 판정에 개입하지 않는다.
- ServerRpc를 거쳐도 **소유자가 이미 가진 위치 권한을 회수하지 못한다** — 왕복 지연만 늘고 얻는 보증이 없다.
- 이동 자체가 서버 권위로 바뀌면 이 판정도 함께 옮겨야 한다 → **GAP-33**으로 기록.

### 구현 (Step 4)

- **Core `FallRecovery`**(신규): 순수 판정 — 안전 지점 기록 시점(접지 상태 + 주기), 낙하 여부, 복구 지점(안전 지점 → 폴백). Unity 의존 없음(Vector3만). **공중 좌표는 기록하지 않는다** — 뛰어내리는 도중 좌표가 기록되면 다시 떨어지는 자리로 복구되기 때문이다. 킬 플레인 아래에서 접지해도 기록하지 않는다.
- **Presentation `FallRecoveryDriver`**(신규): SceneFlow 오브젝트에 두고 `LocalPlayerRegistry.Current`만 다룬다(`PawnPhaseTeleporter`와 같은 배치 — **Player 프리팹을 건드리지 않아** 프리팹 마이그레이션이 필요 없다). 순간이동 시 `CharacterController`를 잠시 끄고, 폴백은 맵 스폰 지점(`SpawnAnchorRegistry`)을 쓴다.
- **`FirstPersonController.ResetVerticalVelocity()`**(신규 메서드): 복구 직후 누적 낙하 속도를 0으로. **이게 없으면 다음 프레임 한 번의 `Move`로 바닥을 뚫고 다시 떨어진다**(오래 떨어질수록 프레임당 이동량이 커진다). 이동 규칙 자체는 그대로다.
- **역할 상태 불변**: 드라이버는 Transform과 수직 속도만 만지고 `IRoleState`/`RoleNetworkSync`/태그 SyncVar에 접근하지 않는다 — 메아리로 바뀐 뒤 떨어져도 역할이 유지된다(구조적 보장).
- **Editor**: `SceneFlowSetupTool`이 SceneFlow에 부착(2단계 + 5단계 보강 경로 양쪽), 진단 도구가 미부착 시 ✖.

### 검증 (Step 5)

- 신규 테스트 **12케이스**(첫 접지 즉시 기록 / 공중 미기록 / 주기 준수 / 킬 플레인 아래 접지 미기록 / 경계값 / 잠수 여유 / 폴백 / Reset / 잘못된 주기·음수 deltaTime 방어) — 총계 368 → **380 전수 통과**.
- 런타임·에디터(경계 분리)·합본 컴파일 **각 0 error, 0 warning**.
- **실기 검증 미실시** — 절차는 `수동검증_절차.md` §21에 작성했다(역할 보존 3종·2클라이언트 전파·잠수 오인 여부 포함).

### 남은 작업

1. 술래 로테이션(§2.2) → 어워드 3종 → §12.6 설정 화면.
2. §10.1 "술래 격리 공간 3초 후 진입" 미구현(스폰 링 공유) — 스펙 대비 갭.
3. 솔로 연습장(GAP-31) — §5.2 음성 파이프라인과 묶어서.
4. GAP-19~33 정리.

---

## 스프린트 21 — 술래 로테이션 (§2.3, GAP-22 해소)

### ⚠ 지시서의 절 번호 정정 (Step 1)

지시서는 §2.2를 지목했지만, **§2.2는 "한 판 루프(8~12분)" 페이즈 표이고 로테이션 언급이 없다.** 실제 근거는 **§2.3 메타 루프**이며 원문은 다음 한 줄이 전부다:

> - 술래 로테이션 3판 1세트 (~30분)

기획서 전체를 `로테이션|1세트|세트|3판|순환`으로 검색해 확인한 나머지 언급도 규칙을 정하지 않는다:
- §9 밸런스: "어느 한쪽이 상시 우세하면 '술래 하기 싫어서' **로테이션 동기가 무너지고**" — 로테이션의 *목적*만 말한다.
- §19 모드표: "모드 — 클래식 술래 | 코어 규칙 그대로의 로테이션".
- §20.3 합격 기준: "4인 3세트(9판) 자발적 연속 플레이", "술래 승률 40~60%".

**확정적으로 도출되는 것은 "세트 = 3판"뿐**이고, 순환 주기·순번 규칙·중도 이탈 처리는 전부 미명시다.

### 해석과 그 근거

| 규칙 | 원문 | 구현 의미 |
|---|---|---|
| 세트 크기 | "3판 1세트" | `SeekerRotation.RoundsPerSet = 3` — **표시·집계 단위** |
| 순환 주기 | **명시 없음(GAP-34)** | **매 판 순환**을 채택 |
| 순번 결정 | **명시 없음** | OwnerId 오름차순 정렬 인덱스(스프린트 13 GAP-22 정책 유지) + 라운드 번호 나머지 |
| 교체 시점 | **명시 없음** | 카운트다운 완료 → 역할 배정 **직전**(§15.4 RoleAssign) |
| 중도 이탈 | **명시 없음(GAP-35)** | 그 시점의 접속 인원으로 재계산(이탈 전 순서를 기억하지 않음) |

**왜 "매 판 순환"인가**: 세트마다 순환하면 한 사람이 ~30분 내내 술래여서 이 스프린트의 목적(GAP-22 "호스트가 항상 술래" 해소)이 달성되지 않는다. §20.3의 "술래 승률 40~60%"도 술래가 자주 바뀌는 것을 전제로 읽힌다. 다만 원문에서 도출된 것이 아니므로 GAP-34로 남긴다.

### 구현

- **신규 Core `SeekerRotation`**: `SeekerOrderIndex(roundNumber, playerCount)`(순번), `SetNumber`/`RoundInSet`(표시), `RoundsPerFullCycle`(한 바퀴 = 인원). 음수 라운드·0 인원까지 방어한다.
- **`RoleAssigner` 최소 확장**: `RoleForOrder(orderIndex, playerCount, seekerOrderIndex)` 오버로드 추가. **§6.2 규칙("술래 1인 고정, 나머지 도망자")은 그대로**이고 "누가 그 1인인가"만 호출자가 정한다. 기존 2인자 오버로드는 `seekerOrderIndex: 0`으로 위임해 스프린트 13 호출부·테스트가 그대로 통과한다. 범위 밖 인덱스는 0으로 접어 **술래 0명 상태를 만들지 않는다**(인원이 줄어든 경우 방어).
- **Net `RoundNetworkSync`**: `_roundNumber` SyncVar 추가(`OnStartServer`에서 −1 → 첫 판이 0). 카운트다운 통과(`LobbyTickResult.StartRound`) 시 **배정 직전에** 증가시킨다 — 라운드 중 늦게 들어온 플레이어의 재배정(`TickRound` → `EnsureRolesAssigned`)에도 같은 번호가 쓰여 그 판의 술래가 흔들리지 않는다. 리매치 가결도 같은 경로를 지나므로 자동으로 다음 순번이 된다.
- 배정 로그에 세트/판수와 술래 순번·OwnerId를 남겨 실기에서 로테이션을 눈으로 확인할 수 있게 했다.

### 검증

- 신규 **15케이스**: 2·3·4인 순환, **한 바퀴 안 중복 없음**(2~6인 전수), 순번 범위 불변, 잘못된 인원 방어, 세트/판수 1-기반 표시, 첫 판 전(−1) 안전, **로테이션 후에도 술래는 정확히 1명**(2~6인 × 2바퀴 전수), 범위 밖 인덱스 폴백, 기존 오버로드 호환, 2인 미만 미배정(GAP-21 유지).
- 총계 380 → **395 전수 통과**. 런타임·에디터(경계)·합본 컴파일 각 0 error, 0 warning.
- **실기 검증 미실시** — 절차는 `수동검증_절차.md` §22.

### GAP-22 해소 여부

**코드 수준에서는 해소**됐다: 술래는 더 이상 "가장 낮은 OwnerId 고정"이 아니라 라운드마다 이동하며, 테스트가 2~6인 전 구간에서 한 바퀴 내 중복이 없음을 강제한다. **실기 확인(§22-1에서 2판째 술래가 참가자로 바뀌는지)까지 통과해야 최종 해소**로 기록한다.

### 남은 작업

1. 어워드 3종(§2.3) → §12.6 설정 화면.
2. §10.1 "술래 격리 공간 3초 후 진입" 미구현(스폰 링 공유).
3. 솔로 연습장(GAP-31) — §5.2 음성 파이프라인과 묶어서.
4. GAP-19~36 정리.

---

## 스프린트 22 — 어워드 3종(§8) + 리매치 스폰 리셋

### ⚠ 지시서의 절 번호 정정 (Step 1)

지시서는 §6.5를 지목했지만 **§6에는 6.5가 없다**(6.1 밸브 상태기계 / 6.2 인원별 밸런스 / 6.3 승패 판정 / 6.4 연결 끊김 처리로 끝). 스프린트 21의 §2.2와 같은 유형의 절 번호 착오다.

어워드의 실제 근거는 **§8 승패·라운드 종료 처리**이며 원문은 다음 한 줄이다:

> - 결과 화면 전환 즉시 어워드 3종 판정(최다 비명상/무성 생존상/최고의 거짓말상 — 판정 기준은 세션 로그의 SoundType.Shout 발생 횟수, 이동거리 대비 파문 발생 0회 여부, 메아리 노크 성공 유인 횟수로 각각 산출)

보조 근거: §2.3(이름 3종 + "v1.x: 통계 누적"), §12.5 도식(가로 3열 카드) 및 요소표("어워드 카드 3종 | 8절 판정 로직 결과를 카드형으로 노출"), §15.4("RoundEnd | 어워드 판정(8절)").

### 집계 조건 해석

| 어워드 | §8 원문 기준 | 구현 |
|---|---|---|
| 최다 비명상 | `SoundType.Shout` 발생 횟수 | 서버가 등록한 Shout 파문 수 최대(1회 이상) |
| 무성 생존상 | 이동거리 대비 파문 발생 0회 여부 | 파문 0회 **그리고** 이동거리 > 0 중 이동거리 최대 |
| 최고의 거짓말상 | 메아리 노크 성공 유인 횟수 | 노크 유인 수 최대(1회 이상) |

**집계 단위는 세션**이다 — §8이 "세션 로그"라고 명시하므로 라운드마다 비우지 않고 접속 시작에 한 번만 비운다.

### ⚠ 선행 기능 미구현으로 실제 수여가 불가능한 2종 (GAP-37)

코드에서 데이터 소스를 전수 확인한 결과:

- **최다 비명상**: `SoundType.Shout`은 음성 등급이라 §5.2 음성 파이프라인이 있어야 발생한다. `ServerPulseDriver` 주석과 테스트가 "음성 등급은 §5.2 스코프"로 명시하고 있으며, 현재 코드에서 Shout를 만드는 곳이 **하나도 없다**.
- **최고의 거짓말상**: 메아리 노크(§3.2)가 미구현이다(`ServerPulseDriver`: "노크는 메아리 능력(미구현)"). 게다가 "**성공 유인**"의 판정 기준이 §8·§3.2 어디에도 정의돼 있지 않다(GAP-39).

따라서 이 둘은 **집계 경로와 표시만 준비**하고 실제 수상자는 항상 "수상자 없음"으로 나온다. 조건을 만들어내지 않았다(지시서 §4). 기능이 생기면 `AwardTally.RecordPulse`/`RecordKnockLure` 호출자만 추가하면 된다.

### 구현 — 어워드

- **신규 Core `AwardTally`**(`Marco.Core.Awards`): 플레이어별 비명·파문·이동거리·노크유인 누적과 §8 3종 판정. Unity·FishNet 비의존이라 EditMode에서 전수 검증된다. 동점은 id가 작은 쪽(GAP-38 — 결정론적 재현성).
- **서버 전용 집계**: 파문은 `PulseNetworkSync`의 ServerRpc가 **서버가 실제로 등록한 건**만 넘긴다(클라이언트 보고를 믿지 않는다). 이동거리는 `RoundNetworkSync`가 InGame 동안 서버 쪽 위치 변화로 잰다 — 클라이언트가 값을 부풀릴 수 없다. 프레임당 2m를 넘는 변화는 순간이동(스폰·리스폰)으로 보고 제외한다(§4.2 최고 속도 7.5m/s 근거).
- **전파**: 수상자 id 3개를 SyncVar로만 보낸다(집계 원장은 서버에만 둔다). `IRoundNetworkBridge`에 3종 + `RoundNumber` 추가.
- **표시**: `ResultScreen`의 플레이스홀더 한 줄을 §12.5 도식대로 **가로 3열 카드**로 교체. 수상자가 없으면 빈칸이 아니라 "수상자 없음"을 명시한다 — 집계 고장과 조건 미달을 구분하기 위해서다.

### 구현 — 리매치 스폰 리셋

**원인**: `PawnPhaseTeleporter`가 `SpawnAnchorRegistry.HasAnchor`(맵 로드 신호)만 보고 한 번 배치한 뒤 래치를 걸었다. 리매치는 **맵이 유지된 채** 새 라운드가 시작되므로 신호가 바뀌지 않아 아무도 움직이지 않았다.

**해법**: 스프린트 21에서 추가한 **라운드 번호**를 두 번째 신호로 쓴다. 번호가 바뀌면 다시 배치한다. 새 신호를 만들지 않고 이미 서버 권위로 관리되는 값을 재사용한 것이다.

**순서 보장**(지시서 §1-6): 서버는 카운트다운 통과 시 `_roundNumber` 증가 → `EnsureRolesAssigned()` → 맵 확인 → `BeginRound()` 순으로 진행한다. 클라이언트가 새 번호를 관측한 시점에는 **서버에서 역할 배정이 이미 끝나 있다**. 덧붙여 스폰 슬롯은 `PlayerId % 슬롯수`로만 정해져 **역할과 무관**하므로, 배정과 텔레포트 사이에 경합이 성립하지 않는다.

**부결 경로 무변경**: 리매치 부결은 맵 언로드 → `HasAnchor=false` → 기존 로비 복귀 흐름 그대로다(래치가 풀려 다음 맵 로드에서 재배치).

### 검증

- 신규 **17케이스**(AwardTally 15 + HudFormatter 2): 비명 최다·비-Shout 무시·동점, 무성 생존 자격(파문 0 + 이동 > 0)·정지 플레이어 제외·비명도 실격, 노크 미구현 시 수상자 없음, 세션 누적, 음수 거리 무시, Reset, 카드 문구.
- 총계 395 → **412 전수 통과**. 런타임·에디터(경계)·합본 컴파일 각 0 error, 0 warning.
- **실기 검증 미실시** — 절차는 `수동검증_절차.md` §23.

### 남은 작업

1. §12.6 설정 화면.
2. §5.2 음성 파이프라인 — 이게 들어와야 최다 비명상이 실제로 동작한다.
3. §3.2 메아리 노크 — 최고의 거짓말상 전제.
4. §10.1 "술래 격리 공간 3초 후 진입" 미구현.
5. GAP-19~41 정리.
