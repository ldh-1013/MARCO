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
