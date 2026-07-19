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

**테스트 누계: 102케이스 전수 통과** (SoundPulseResolver 19 + GameFlow 9 + Valve 17 + WinCondition 11 + ActivePulseTracker 12 + LocomotionSimulator 18 + LocalPulsePipeline 10 + LocalPulsePipelineExpiry 6)

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
