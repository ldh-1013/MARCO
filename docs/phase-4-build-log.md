# Phase 4 — 품질 및 강화 (Hardening) 빌드 로그

> 프로젝트: 마르코!(MARCO!) — Unity 6 + FishNet 비대칭 멀티플레이어 호러 파티 게임
> 개시일: 2026-08-04
> 선행: `phase-3-build-log.md`(스프린트 1~25), `프로젝트_현황_종합.md`

---

## 0. Phase 4 개시 선언

Phase 3 완료 체크리스트 **4항목 전부 충족**을 확인하고 Phase 4로 전환한다.

| # | Phase 3 완료 기준 | 결과 |
|---|---|---|
| 1 | 핵심 게임플레이 루프 **구현** | ✅ 13개 루프, 433 테스트 통과, 전 어셈블리 0 error/0 warning |
| 2 | 핵심 루프 **실기 검증** | ✅ **§19~§25 전 항목 통과(2026-07-29, 사용자 실기)** |
| 3 | 네트워크 2단계 실기 검증 | ✅ 2026-07-26 완료 |
| 4 | `NetworkTestBootstrap`/DebugTools 제거 | ✅ 스프린트 19(폴더 부재 확인) |

추가로 스프린트 25 후속에서 발견된 **클라이언트 밸브 미표시 버그**(SceneCondition 부재로 인한 스폰 경쟁)가 해소돼, 2-클라이언트 환경에서 맵 오브젝트 동기화가 성립함을 확인했다.

GAP-1~48 전수 정리도 완료했다(해소 19 / 결정 유지 17 / Phase 4 이월 11 / Deprecated 1 — `프로젝트_현황_종합.md` §4-A).

### Phase 4 플레이북과 이 프로젝트의 차이

`.claude/agents/phase-4-hardening.md`의 Quality Gate는 **웹 애플리케이션**을 전제로 쓰여 있다. Unity 멀티플레이어 게임에 그대로 적용되지 않는 항목이 있어, 대응 관계를 먼저 확정한다.

| # | 플레이북 기준 | 이 프로젝트에서의 해석 |
|---|---|---|
| 1 | 사용자 여정 완결 | **그대로 적용** — Boot→MainMenu→Lobby→InGame→Result→리매치 전 구간 |
| 2 | 크로스 디바이스(데스크톱/태블릿/모바일) | **해당 없음** — PC 전용(§1). 대신 **호스트/클라이언트 2역할 × 에디터/빌드 2환경**으로 대체 |
| 3 | 성능 인증(P95, LCP, CLS) | **게임 지표로 대체** — 프레임타임, 동시 파문 처리량(M1 목표 "동시 파문 30개 @ 60fps"), 네트워크 대역 |
| 4 | 보안 검증(OWASP) | **축소 적용** — 웹 공격면이 없다. 서버 권위 검증(클라 주장 신뢰 범위, GAP-17/24)이 이 프로젝트의 보안 경계 |
| 5 | 규제 준수(GDPR/CCPA) | **해당 없음(현 단계)** — 계정·서버 저장이 없다. Steam 출시 단계에서 재검토 |
| 6 | 사양 준수 100% | **그대로 적용** — 기획서 절 단위 대조. 단 Phase 4 이월 11건은 이 단계에서 구현 |
| 7 | 인프라 준비 | **축소 적용** — 전용 서버가 없다(호스트 권위 P2P). Steam 로비·릴레이 도입 시 재평가 |

접근성(WCAG)은 해당 없지만, **§16.2 색맹 모드**가 그 자리를 대신하며 이미 구현·검증됐다(스프린트 23 §24-2).

### Phase 4 작업 목록 (Phase 3에서 이월된 11건)

| 스프린트 | 작업 | GAP | 기획서 근거 |
|---|---|---|---|
| **26a~26c** | §5.2 음성 분류 파이프라인 | 4, 6, 37, 42 | §5.2 8단계 + §20.1 MVP 필수 |
| 27 | §3.2 메아리 노크 | 37, 39 | §3.1 특수 능력 |
| 28 | §4.3 키 리바인딩 | 42, 44 | §12.6 조작 탭 |
| 29 | 솔로 연습장 | 31 | §12.2 |
| 30 | 플레이어 이름 체계 + 세트/판수 표시 | 36, 41 | §12.3, §2.3 |
| 31 | Steam 방코드 매칭 | 28 | §13 |
| — | 3D 아트 대체(그레이박스 → 미감) | — | §10 "그레이박스 1주 + 미감 2주" |

음성이 가장 앞인 이유: **어워드 1종(최다 비명상)·설정 오디오 탭 4항목·방향 게이지(GAP-4)가 전부 여기에 묶여 있다.** 하나를 풀면 네 곳이 함께 풀린다.

---

## 스프린트 26a — 음성 파이프라인 1단계: 캡처·진폭·분류 골격

> §5.2의 8단계 중 **1(캡처)·2(진폭)·6(분류)·8(이벤트 생성)**을 먼저 세운다.
> 3(캘리브레이션)·4(대역 필터)·5(연속성)·7(디바운스)는 26b/26c로 나눈다.

### 왜 쪼개는가

§5.2는 8단계가 한 덩어리로 서술돼 있지만, **1·2·6·8만 있어도 "말하면 파문이 뜬다"는 루프가 성립**한다. 그 상태에서 실기로 오탐 양상을 본 뒤 3·4·5·7(전부 **오탐 억제** 단계)을 조정하는 편이, 여덟 단계를 한 번에 만들고 어디가 문제인지 찾는 것보다 낫다. 기획서도 이 넷을 "캘리브레이션만으로 못 막는 노이즈 오탐을 **추가로 억제**하기 위한 보강 단계"로 설명한다.

### 계획된 산출물

| 계층 | 산출물 | 비고 |
|---|---|---|
| Core | `VoiceLevelClassifier`(dBFS → §5.1 3구간 매핑) | 순수 함수 — EditMode 검증 |
| Core | `VoiceFrameAnalyzer`(PCM 프레임 → RMS → dBFS) | 순수 계산 |
| Presentation | 마이크 캡처 어댑터 | MVP는 Unity `Microphone`, Steamworks Voice API는 Steam 단계(§13) |
| Net | 발화 파문을 기존 `PulseNetworkSync` 경로에 연결 | **서버 권위 유지** — 클라는 종류만 주장(GAP-24) |

**기존 판정 로직은 건드리지 않는다** — §5.1 반경 표·§5.6 차폐·태그·라운드 판정 전부 무변경. 음성은 `SoundType.Whisper/Talk/Shout`를 **생성**하는 새 입력원일 뿐이다.

### 선행 확인 사항 (착수 전)

1. **Steamworks Voice API vs Unity Microphone** — §5.2는 Steamworks를 명시하지만 Steam 통합은 §13(Phase 4 후반)이다. 어느 쪽으로 시작할지 결정하고 GAP으로 기록해야 한다.
2. **마이크 권한** — §15.4 Boot의 "마이크 권한 확인"이 아직 자리표시자다(`BootFlow`). 이 스프린트에서 실제 권한 흐름을 넣을지 결정.
3. **`SoundType.Whisper`의 파문 반경** — §5.1 표에 세 등급이 모두 있는지 재확인 필요(스프린트 14에서 Walk/Sprint/Valve만 서버 판정 대상이었다).

### §5.1 원문 — 3등급 임계값이 **정확히 명시돼 있다**

| 소리 | 반경 | 지속시간 | 발생 조건 |
|---|---|---|---|
| 속삭임 | 4m | 0.6초 | **마이크 RMS −50~−30 dBFS** |
| 대화 | 9m | 1.2초 | **마이크 RMS −30~−15 dBFS** |
| 고함 | 22m | 2.5초 | **마이크 RMS −15 dBFS 이상** |

§5.2에서 가져온 수치: 16kHz PCM·20ms 프레임(1단계, 프레임당 320 샘플), 3프레임(60ms) 연속(5단계), 0.15초 미만 무시(7단계). §12.6에서: 입력 게인 −20~+20dB(기본 0dB).

**임의로 만든 상수가 없다** — `VoiceConfig`의 모든 값이 위 원문에서 나왔다.

### 구현

- **신규 Core `VoiceConfig`**: §5.1 임계값 + §5.2 캡처·타이밍 + §12.6 게인 범위. `VoiceGrade`(Silence/Whisper/Talk/Shout), `VoiceActivationMode`(VAD/PTT) 열거형.
- **신규 Core `VoiceClassifier`**: 샘플 → RMS → dBFS → 게인 → 등급. 순수 계산이라 EditMode에서 전수 검증된다. 무음의 dBFS는 음의 무한대라 **유한한 하한(−120dBFS)으로 눌렀다** — 계산·표시·직렬화 어디서도 무한대를 다루지 않기 위함이다.
- **신규 Presentation `MicrophoneCapture`**: Unity `Microphone` 링 버퍼 래퍼. 뒤처지면 **최신 한 프레임만 읽고 나머지를 버린다** — 지연된 음성을 뒤늦게 파문으로 만들면 §2.1의 "0~80ms 즉각 피드백" 원칙이 깨진다.
- **신규 Presentation `LocalVoicePipeline`**: 캡처 → 분류 → Console 로그. VAD/PTT 분기와 §5.8 강제 뮤트(Left Alt) 처리.
- **`GameSettings` 확장**: `VoiceMode`·`InputGainDb` 추가 — §12.6 15항목 중 **2항목이 GAP-42에서 풀렸다**(발화 방식, 입력 게인). 설정 화면·저장소·"준비 중" 목록에 반영.
- **씬 배선**: `SceneFlowSetupTool`이 SceneFlow에 부착, 진단 도구에 점검 항목 추가.

**발소리 파이프라인과 판정 로직은 건드리지 않았다** — 음성은 `SoundType`을 생성하는 새 입력원일 뿐이고, 네트워크 전송은 26b다.

### ⚠ Steamworks가 아니라 Unity Microphone으로 시작한 이유 (GAP-49)

§5.2-1은 "Steamworks Voice API"를 명시하지만 Steam 통합은 §13이고 Phase 4 후반(스프린트 31)이다. 그때까지 음성 루프 전체를 막아 두는 것보다, **분류·판정을 먼저 세우고 캡처 계층만 나중에 교체**하는 편이 낫다고 판단했다. `MicrophoneCapture`가 그 교체 지점이며, 상위(`LocalVoicePipeline`)는 캡처 구현을 모른다.

### 검증

- 신규 **22케이스**: §5.1 임계값·§5.2 캡처 상수·§12.6 게인 범위가 원문과 일치하는지, 구간 경계(하한 포함), RMS 부호 무시·count 인자·null 방어, dBFS 무음 하한·풀스케일 0dB·절반 진폭 −6dB, 게인 범위 클램프·NaN 복구·**등급 승격**, 통합 경로 4구간, VAD가 열거형 기본값(0)인지.
- 총계 433 → **455 전수 통과**. 런타임·에디터(경계)·합본 컴파일 각 0 error, 0 warning.
- **실기 검증 미실시** — 절차는 `수동검증_절차.md` §26.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **49** | §5.2는 Steamworks Voice API를 명시하나 Steam 통합은 §13(Phase 4 후반) | Unity `Microphone`으로 시작, 캡처 계층만 교체 가능하게 분리 |
| **50** | PTT 키가 §4.3 키 바인딩 표에 없다(§5.8은 "PTT 토글 제공"만 언급) | 임시로 `V`. §4.3 키 리바인딩(스프린트 28)에서 확정 |
| **51** | 3·4·5·7단계(캘리브레이션·대역 필터·연속성·디바운스) 부재로 **환경 노이즈 오탐 예상** | 26b/26c 대상. 1단계 실기의 목적 중 하나가 오탐 양상 관찰 |

### 지시서와 실제 코드의 차이 (기록)

지시서 §1-3이 `Presentation/GameFlow/GameSettings.cs`의 `VoiceActivationMode`·`InputGain`을 "스프린트 23에서 만든 것"으로 전제했으나, 실제로는 **둘 다 존재하지 않았다**. 스프린트 23에서 §12.6의 VAD/PTT·입력 게인은 "선행 시스템(음성) 부재"를 이유로 **의도적으로 만들지 않았고**(GAP-42), 그 사실을 당시 보고서에 기록했다. 이번 스프린트에서 음성이 들어오며 비로소 추가했다. 파일 위치도 `Core/Settings/GameSettings.cs`다(Presentation이 아니라 Core — 규칙은 Unity 없이 테스트 가능해야 하므로).

### 26a 후속 — 빌드 없는 테스트 경로 2종

실기 반복 비용을 줄이기 위해 추가했다. 게임플레이 코드 변경은 없다.

- **신규 `VoicePipelineTestWindow`**(Editor): `Tools → MARCO → Voice Pipeline Test`. **Play 모드 없이** 마이크 → RMS → dBFS → 등급을 실시간으로 보여주고, dBFS 막대·§5.1 기준표·게인 슬라이더를 제공한다. `EditorApplication.update`로 폴링하며 창을 닫으면 캡처를 놓는다. **Play 중에는 캡처하지 않는다** — 같은 마이크를 `LocalVoicePipeline`과 동시에 잡으면 둘 다 불안정해지기 때문이다.
- **`InGameHud` 음성 표시**: 좌하단에 `🎤 Talk  -22.4 dBFS`. **네트워크 연결 여부와 무관하게 항상 갱신**하고, 씬에 파이프라인이 없으면 줄을 비운다(§12.4 도식에 없는 개발용 표시라 없을 때 자리를 차지하면 안 된다). 등급별 색은 §16.2 팔레트를 그대로 쓴다.

**Play 중 폴백 경로는 이미 성립하고 있었다**: `LocalVoicePipeline`의 네트워크 의존이 **0건**이고(코드 검색으로 확인), `SceneFlow` 오브젝트에는 NetworkObject가 없어 FishNet의 접속 전 비활성화(§20-6a)에도 걸리지 않는다. 즉 **`Lobby.unity`를 열고 Play만 해도** 접속 없이 음성이 돈다 — 새 폴백을 만들 필요가 없었고, 그 사실을 §26-0에 절차로 기록했다.

### 상태

**코드 완료** — 실기 검증(§26) 대기. 총계 **455 통과**, 전 어셈블리 0 error/0 warning.

---

---

## 스프린트 26b — 음성 파문 서버 브로드캐스트 (§5.1/§5.2-8)

### ⚠ 지시서가 계획한 새 파일 2개를 만들지 않았다

지시서 §2는 `Core/Net/IVoiceNetworkBridge.cs`와 `Net/VoiceNetworkSync.cs` 신설을 요구했다. 착수 전 기존 경로를 읽어 본 결과, **그 둘을 만들면 이미 있는 일반 경로를 복제하게 된다**는 것이 드러났다.

기존 파문 경로는 처음부터 **소리 종류에 무관(type-agnostic)하게** 설계돼 있다:

| 계층 | 기존 코드 | 음성에 필요한 변경 |
|---|---|---|
| 브릿지 계약 | `IPulseNetworkBridge.SubmitPulse(SoundType)` | **없음** — 등급을 `SoundType`으로 넘기면 끝 |
| ServerRpc | `PulseNetworkSync.ServerSubmitPulse(SoundType)` | **없음** |
| 서버 위치·차폐 | `caller.FirstObject.transform.position` + `SoundPulseResolver` | **없음** |
| 청취자별 전달 | `TargetRpc` + `PerceivedPulse` | **없음** |
| 시각화 | `PulseVisualRegistry`/`PulseVisualRenderer`가 `PerceivedPulse` 소비 | **없음** |
| 어워드 집계 | `AwardTally.RecordPulse(playerId, type)`가 이미 `Shout`를 센다 | **없음** |

**막고 있던 것은 딱 한 곳**이었다 — `ServerPulseDriver.TryGetPulseSpec`이 음성 3등급에 `false`를 돌려주고 있었다(스프린트 14 주석: "음성 등급은 §5.2 파이프라인 스코프라 아직 대상이 아니다").

별도 음성 스택을 세우면 ServerRpc·차폐·전달·시각화·집계가 **두 벌**이 되어 이후 모든 수정이 두 곳에 필요해진다. §5.1 표 자체가 발소리·밸브·음성·노크를 **하나의 SoundType 체계**로 다루므로, 코드도 그 구조를 따르는 것이 맞다고 판단했다.

### 1. `PulseVisualRenderer` 재사용 가능 여부 — **가능. 코드 변경 0줄**

렌더러는 `PerceivedPulse`(pulseId·반경·지속·방위·좌표공개 여부)만 소비하고 `SoundType`을 보지 않는다. 서버가 음성 파문을 만들면 **기존 링/방위 표시가 그대로 그려진다.**

### 2. 구현 (최소)

- **`VoiceConfig`에 §5.1 반경·지속 추가**: 속삭임 4m/0.6s · 대화 9m/1.2s · 고함 22m/2.5s. 전부 표 원문 값이다.
- **`ServerPulseDriver.TryGetPulseSpec`에 3행 추가**: 클라이언트는 **등급만** 주장하고 반경·지속·위치·차폐는 서버가 정한다 — 발소리·밸브와 완전히 같은 규칙(GAP-24).
- **`LocalVoicePipeline`에 송신 분기**: 접속 중이면 `IPulseNetworkBridge.SubmitPulse(type)`, 아니면 기존 로컬 로그(26a 동작 유지). 브릿지는 씬에서 찾는다 — 이 컴포넌트는 `SceneFlow`에 있고 브릿지는 `PulseSystem`에 있기 때문이다(같은 Lobby 씬).
- **발신 주기**: 등급이 바뀌거나 **직전 파문이 만료된 뒤에만** 보낸다. 매 프레임 보내면 같은 발화가 초당 수십 개의 파문으로 쌓여 §5.1 지속시간이 무의미해진다(GAP-52 — §5.2-7 디바운스는 26c).

**최다 비명상은 배선이 필요 없었다** — `PulseNetworkSync`가 서버 등록 직후 이미 `RoundNetworkSync.ServerRecordPulse(clientId, type)`를 부르고, `AwardTally.RecordPulse`가 `type == Shout`일 때 비명 횟수를 올린다(스프린트 22에서 만들어 둔 자리). 고함이 서버에 도달하는 순간부터 자동으로 집계된다.

**`Setup Everything`에 새 단계도 필요 없다** — 새 컴포넌트가 없다.

### 3. 검증 — 기존 테스트 4건이 **의도대로 실패**했다

`ServerPulseDriverTests`의 4건이 빨간불이 됐다: `TryGetPulseSpec_UnsupportedTypes_AreRejected(Whisper/Talk/Shout)`와 `AddPulse_UnsupportedType_ReturnsNegativeAndAddsNothing`. **스프린트 14가 "음성은 아직 거부한다"를 고정해 둔 테스트**이고, 이번에 그 동작을 의도적으로 바꿨으므로 테스트가 제 역할을 한 것이다.

거부 목록에서 음성을 빼고 **노크만 남긴 뒤**, 음성 3등급의 반경·지속이 §5.1과 일치하는지 검사하는 테스트를 새로 넣었다.

- 신규 **5케이스**: 속삭임/대화/고함 각각의 반경·지속이 표와 일치, **반경이 음량 순으로 커지는지**(뒤집히면 "크게 말할수록 안전"해져 §2.1 훅이 무너진다), 고함이 실제로 파문으로 추적되는지.
- 총계 455 → **457 전수 통과**(기존 4건 갱신 + 신규 5건 − 파라미터 케이스 3건 축소). 런타임·에디터(경계)·합본 컴파일 각 0 error, 0 warning.
- 작성 중 `FindObjectsSortMode`로 **CS0618을 또 냈고**(스프린트 22에 이어 두 번째) 경계 빌드가 잡아내 수정했다.

### 4. GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **52** | 발화 파문 발신 주기가 §5.2에 없다(7단계 디바운스는 "0.15초 미만 무시"만 규정) | 등급 변화 또는 직전 파문 만료 시에만 발신. 디바운스·연속성은 26c |

### 5. 실기 검증

**미실시** — 절차는 `수동검증_절차.md` §27. 핵심 항목: 상대 화면에 §5.1 반경대로 파문이 뜨는지, **발신자 본인에게는 안 보이는지(GAP-1)**, 최다 비명상이 처음으로 수상자를 내는지, **발소리·밸브가 그대로인지(§27-6 회귀)**.

### 상태

**코드 완료** — 실기 검증(§27) 대기. 남은 26c: 역할별 청취 배율(§5.7)·방향 게이지(GAP-4)·캘리브레이션(§5.2-3)·대역 필터(§5.2-4)·연속성(5)·디바운스(7).

---

## 스프린트 26c — 음성 파이프라인 3단계 (§5.7/§3.4/§5.2-3/§5.2-4)

§5.2의 8단계가 이번으로 전부 채워졌다.

### 1. §5.7 역할별 청취 배율 — **이미 구현돼 있었다 (추가 작업 0)**

원문:

> ```
> ├─ Listener A(Runner) : radius × 1.0, duration × 1.0  → 5.6 차폐 계산
> ├─ Listener B(Seeker) : radius × 1.2, duration × 1.5  → 5.6 차폐 계산
> └─ Listener C(Echo)   : radius × 1.0, duration × 1.0
> ```

`SoundPulseResolver.RoleRadiusMultiplier`/`RoleDurationMultiplier`가 스프린트 4부터 이 값을 그대로 갖고 있었고(술래 1.2f/1.5f), 26b에서 음성이 같은 경로를 타면서 **자동으로 적용됐다.** 지시서 §3-2가 "있으면 자동 적용 여부 확인"이라 했는데, 확인 결과 **그대로 적용된다.**

§5.7 마지막 문단("클라이언트는 받은 값을 그대로 렌더링에 쓰고 추가 배율 연산을 하지 않는다")도 이미 지켜지고 있다 — 클라이언트는 `PerceivedPulse`만 받는다.

### 2. §3.4 방향 게이지 (GAP-4 해소)

**§5.4의 "방향 힌트(항상 표시)"와 다른 기능**임을 먼저 확정했다. 그쪽은 판정을 통과한 모든 파문에 누구에게나 뜨는 vignette이고, §3.4 게이지는 **술래 전용 정보 우위**다.

| §3.4 규칙 | 구현 |
|---|---|
| 대화·고함만 트리거(속삭임 제외) | `TriggersGauge` |
| 발소리·밸브·노크 제외 | 〃 |
| 술래 전용 | `CanSeeGauge` |
| 사거리 = 물리 반경 × 1.5(대화 13.5m, 고함 33m) | `MaxRange` |
| 8방위 스냅 | 기존 `DirectionOctant` 재사용 |

신규 `Core/Sound/DirectionGaugeRules.cs`(순수 규칙). **§5.7 배율과 곱하지 않는다** — §3.4 표가 "물리 반경(5.1)" 기준이고, 그 해석에서만 표의 13.5m/33m이 나온다(9×1.5, 22×1.5).

### 3. §5.2-5/7 연속성·디바운스 (GAP-52 해소)

신규 `Core/Voice/VoiceGate.cs`. 두 단계는 **막는 대상이 다르다** — 연속성(3프레임)은 "한 프레임짜리 스파이크"를, 디바운스(0.15초)는 "짧게 스쳐 간 발화"를 거른다. 그래서 합치지 않고 둘 다 검사한다. 발신 주기 판단(26b에서 `LocalVoicePipeline`에 임시로 뒀던 것)도 이 게이트로 옮겨, 파이프라인에는 중복 판정이 남지 않는다.

### 4. §5.2-4 대역 필터

신규 `Core/Voice/VoiceBandpass.cs` — 80~3400Hz. 원문이 차수·구현을 정하지 않아(GAP-55) **1차 고역통과 + 1차 저역통과 직렬**이라는 가장 단순한 형태를 택했다. 목적이 "오분류 완화"이지 정확한 주파수 응답이 아니고, 20ms마다 320샘플을 도는 경로라 비용도 낮아야 한다. **RMS 계산 전에** 건다 — 저역 노이즈가 진폭에 섞이면 그 뒤 어떤 단계로도 되돌릴 수 없다.

### 5. §5.2-3 캘리브레이션

신규 `Core/Voice/VoiceCalibration.cs`. 측정한 baseline("가장 작은 목소리")이 §5.1 속삭임 하한(−50dBFS)에 오도록 dBFS를 평행 이동시킨다. 설계 판단 3가지:

- **무음 프레임은 평균에서 제외** — 말하지 않은 구간이 섞이면 baseline이 실제보다 낮아져 오프셋이 과해진다.
- **유효 샘플 0이면 오프셋 0** — 측정 실패가 조용히 잘못된 보정으로 남지 않게 한다.
- **±20dB 제한**(GAP-54) — 잘못된 측정 한 번이 등급 체계를 통째로 망가뜨리면 안 된다. §12.6 입력 게인과 같은 폭으로 맞췄다.

`GameSettings.VoiceCalibrationOffsetDb`로 저장되며(PlayerPrefs 영속), §12.6 "발화 감도 재보정"이 **"준비 중" 목록에서 빠지고 실제 항목**이 됐다(측정 중에는 남은 초를 표시하고 파문 전송을 멈춘다).

### 검증

- 신규 **26케이스**: 스파이크·짧은 발화 거부, 지속 발화 수용, 등급 흔들림 시 연속성 재시작, 같은 등급 재발신 주기, 등급 상승 시 즉시 발신 / 게이지 트리거·제외·술래 전용·§3.4 표 사거리·**고함이 맵 대각선을 못 덮는지** / 캘리브레이션 평균·무음 제외·양음 오프셋·클램프·측정 실패 / 대역 경계·DC 감쇠·음성 대역 통과·**저역이 음성보다 더 깎이는지**.
- 총계 457 → **483 전수 통과**. 런타임·에디터(경계)·합본 컴파일 각 0 error, 0 warning.
- 작성 중 테스트 계산 실수 1건(100프레임 × 0.02초 = 2초 < 3초 프롬프트) — 테스트가 잡아내 수정.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **53** | §3.4 게이지의 화면 표현(링 두께·밝기·색) 미상세 | 최소 표시 |
| **54** | 캘리브레이션 오프셋 한계 미명시 | §12.6 입력 게인과 같은 ±20dB |
| **55** | §5.2-4 필터 차수·구현 미명시 | 1차 고역+저역 직렬(완전 차단 아닌 완화) |

### 상태

**코드 완료** — 실기 검증(§28) 대기. §5.2 8단계 전부 구현됨.

---

## 스프린트 26 더블체크 — 음성 파이프라인 전수 검증 (26a~26c)

> 스프린트 27(메아리 노크) 착수 전, 26a~26c가 설계 의도대로 구현됐는지 8항목(A~H) 검증.
> 노크도 같은 `PulseNetworkSync` 경로를 탈 예정이라, 경로에 문제가 있으면 27에서 더 얽힌다.

### 검증 결과 — 6항목 정상 / 2항목 문제

| 항목 | 결과 | 근거 |
|---|---|---|
| A 파이프라인 순서 | ❌ | 순서는 정확(**대역 필터 → RMS** 확인), 게이트 시간 기준이 틀림 |
| B 서버 재계산 | ✅ | `TryGetPulseSpec` 3행이 §5.1 표(4/9/22m·0.6/1.2/2.5s)와 일치 |
| C 역할별 배율 | ✅ | 음성이 같은 리졸버를 타 술래 ×1.2/×1.5 자동 적용. 클라 재연산 없음 |
| D 방향 게이지 | ❌ | 규칙은 원문과 일치하나 **프로덕션 호출자 0건** |
| E 발소리 회귀 | ✅ | Walk 2m/0.4s·Sprint 6m/0.8s·Valve 12m/3.0s 무변경 |
| F GAP-1 | ✅ | 리졸버 자기 제외 + ID 체계 일치(`caller.ClientId` ↔ `OrderKey=OwnerId`) + 로컬 렌더 경로 부재 |
| G 최다 비명상 | ✅ | `ServerSubmitPulse → ServerRecordPulse → RecordPulse` 경로 확인. 26b 서술이 맞았다 |
| H 씬 배선 | ✅ | `Lobby.unity`의 `SceneFlow`에 부착·활성 저장됨 |

**26b의 "기존 경로 재사용" 판단이 옳았음이 확인됐다** — B·C·E·F·G가 전부 코드 변경 0으로 성립한 것이 그 증거다.

### ❌ 문제 1 — 게이트에 **렌더 프레임 시간**을 넘기고 있었다

`LocalVoicePipeline.Update`는 오디오 프레임(20ms)이 안 쌓이면 일찍 `return`한다. 그런데 게이트에 `Time.deltaTime`(렌더 프레임 시간)을 넘겨, **빠져나간 프레임의 시간이 통째로 유실**되고 있었다.

| fps | 게이트 호출 실간격 | 넘기던 값 | §5.2-7 디바운스 0.15s가 실제로는 | Talk 재발신 1.2s가 실제로는 |
|---|---|---|---|---|
| 30 | 33ms | 33ms | 0.15s ✅ | 1.2s ✅ |
| **60** | 33ms | 16.7ms | **0.30s** | **2.4s** |
| 144 | 21ms | 6.9ms | **0.45s** | **3.6s** |

§5.2-5 연속성("3프레임=60ms")도 60fps에서 100ms가 됐다. **50fps를 넘는 순간부터** 어긋나며, M1 목표가 60fps라 어긋난 쪽이 정상 동작이었다.

체감상으로는 **계속 말해도 파문이 2.4초마다만 뜨는** 공백이 생겨, §2.1 "말하면 위치가 새어나간다" 훅이 절반만 작동한다.

**수정**: 프레임을 못 읽고 빠져나가는 경로 **앞에서** `Time.deltaTime`을 `_sinceGateTick`에 누적하고, 프레임을 읽은 시점에 그 누적값을 게이트·캘리브레이션에 넘긴 뒤 0으로 되돌린다. §5.2-3의 "3초 프롬프트"도 같은 이유로 벽시계 기준이라 함께 고쳤다(그 전에는 60fps에서 6초가 걸렸다).

**게이트 로직 자체는 무결했다** — 결함은 호출부 한 곳이었다. 테스트가 못 잡은 이유는 `VoicePipelineStage3Tests`가 `VoiceConfig.FrameSeconds`(0.02, **오디오** 프레임)를 넘기는 반면 프로덕션은 렌더 프레임 시간을 넘겨, 둘이 서로 다른 값을 쓰고 있었기 때문이다.

### ❌ 문제 2 — §3.4 방향 게이지가 화면에 뜨지 않았다 (GAP-4 상태 정정)

`DirectionGaugeRules`(26c 신규)를 호출하는 프로덕션 코드가 **하나도 없었다**. 전 저장소 참조처는 `Core.csproj`·테스트 8건·문서뿐이었다. 즉 규칙은 정의됐지만 술래 화면에 게이지가 뜨지 않는 상태였다.

`PulseVisualRenderer`의 8방위 IMGUI 인디케이터는 GAP-2/§5.4 **방향 힌트**로, 26c 자신이 "§3.4 게이지와 다른 기능"이라고 못박은 그쪽이다.

> **기록 정정**: 26c와 `프로젝트_현황_종합.md`가 "GAP-4 해소"로 적었으나, 실제로는 **규칙 정의까지만** 완료였다. 배선은 이번에 했다.

**수정 — 판정은 서버, 클라는 그리기만**:

| 계층 | 변경 |
|---|---|
| Core | `PulseDelivery`에 `GaugeLit` 추가(선택 인자라 기존 호출부 무변경) |
| Core | `ActivePulseTracker.Tick`이 `DirectionGaugeRules.ShouldLight`로 청취자별 판정 |
| Net | `TargetPulseDelivery`에 `gaugeLit` 인자 추가 |
| Presentation | `PulseVisualState.GaugeLit` → 레지스트리 → 렌더러 |
| Presentation | `PulseVisualRenderer`가 §16.2 **술래 색 ▲**로 표시(힌트는 러너 색 ◆) |

**게이지 판정을 클라이언트에 맡기지 않은 이유**: §3.4는 술래 전용 정보 우위다. 클라가 자기 역할을 보고 스스로 켜는 구조면 러너 클라이언트가 그 분기를 켜서 정보 우위를 훔칠 수 있다. 발소리·밸브·음성이 전부 서버 권위인 것과 같은 규칙이다(GAP-24).

**사거리는 §5.7 배율을 곱하지 않은 물리 반경 기준**이다 — 26c의 결정을 그대로 따랐고, 그 해석에서만 §3.4 표의 13.5m/33m이 나온다.

**GAP-4 결정("§5.6 통과 펄스만 트리거")을 코드로 강제**: 판정에서 탈락한 파문은 delivery 자체가 생기지 않으므로 게이지 경로로도 새어나갈 수 없다(테스트로 고정). 이 결정 아래에서는 게이지 사거리(반경×1.5)가 술래 인지 반경(반경×1.2)보다 항상 넓어 **거리 조건이 구조적으로 항상 참**이지만, 배율이 바뀌어도 규칙이 스스로를 지키도록 거리 검사를 남겼다.

**표시 위치 분리**: 게이지는 화면 안쪽(`0.62`), §5.4 힌트는 바깥(`0.82`)에 그려 같은 파문이 둘 다 해당돼도 겹치지 않는다. 화면 표현이 미상세한 것은 여전하다(GAP-53 — 최소 표시 유지).

### 검증

- 신규 **13케이스**: 게이지가 술래에게만·대화/고함에만 뜨는지, 발소리·밸브·속삭임 4종이 술래에게도 안 뜨는지, 소실 통지에는 안 실리는지, §5.6 탈락 파문이 게이지로 새지 않는지 / 플래그가 시각 상태까지 도달하는지, 월드 링과 공존하는지, 갱신이 페이드 타이머를 되감지 않는지 / 디바운스·재발신이 **호출 횟수가 아니라 경과 시간**을 세는지.
- 총계 483 → **496 전수 통과**. 전 어셈블리 0 error/0 warning(Net의 경고 21건은 전부 벤더 `Assets/FishNet` — Marco 코드 0건).

### 경미 사항 3건 (수정하지 않음)

1. 뮤트 시 `_bandpass`는 초기화하지 않는다 — 해제 직후 과도응답이 20ms 남는 수준.
2. 비연결 상태에서 브릿지를 못 찾으면 `FindObjectsByType` 전수 스캔이 반복된다 — 게이트 쿨다운 덕에 초당 1회 미만.
3. §8 어워드가 세션 누적이라 **로비에서 지른 비명도 최다 비명상에 들어간다**. §8 원문("세션 로그")에 부합하지만, 실기에서 "라운드에서 조용했는데 수상"으로 보일 수 있다.

### 상태

**코드 완료** — 실기 검증(§26~§28) 대기. 실기 시 추가 확인 항목: **술래 화면에 ▲ 게이지가 뜨는지**(러너 화면에는 안 뜨는지), 계속 말할 때 파문이 §5.1 지속시간 주기로 이어지는지.

---

## 전체 재검증 (1/3) — Core 계층 전수

> 스프린트 27 착수 전, 스프린트 1~26의 Core 계층을 기획서 원문과 대조 검증(A~J 10항목).
> Net(2/3)·Presentation(3/3)은 별도.

### 결과 — 7항목 정상 / 2항목 문제 / 경미 4건

기획서 원문과 **줄 단위로 일치**함을 확인한 것: §5.6 차폐 의사코드 전 4행(`0.5^wallCount`·`max(attenuation,0.5)`·1차 컷·최종 거리 컷), §6.3 승패 의사코드, §5.1 소리 표 7행, §6.2 인원 표 5행, §3.4 게이지 사거리 표.

| 항목 | 결과 |
|---|---|
| A TagRules · B WinCondition · D SoundPulseResolver · E FallRecovery · F SeekerRotation · H VoiceClassifier · J 인터페이스 | ✅ |
| **G AwardTally** | ❌ 메아리가 무성 생존상 독식 → **이번에 수정** |
| **I DirectionGaugeRules** | ❌ §3.4 "0.5초 게이지 갱신 쿨다운" 미구현(미수정 — 아래) |

**인터페이스 13종 전수 확인**: 사용처 0건인 인터페이스는 **없다**(GAP-4 유형 재발 없음). `IVoiceNetworkBridge`는 26b 판단대로 **의도적 부재**이며, `IPulseNetworkBridge` 구현체가 정확히 1개라 `LocalVoicePipeline`의 씬 검색 바인딩에 모호성이 없다 — **스프린트 27에서 두 번째 구현체를 만들면 이 가정이 깨진다.**

### ❌ 수정 — 메아리가 무성 생존상을 독식했다 (GAP-56)

세 가지가 맞물린 결과였다:

1. **메아리는 발소리를 내지 않는다** — `LocomotionSimulator`의 `if (_role != RoleType.Echo)`(§3.2 비행형)
2. **메아리 속도가 가장 빠르다** — `EchoSpeed = 8.0f`(러너 질주 7.5보다 빠름)
3. **이동거리 집계에 역할 필터가 없었다** — `AccumulateDistances`가 `OrderKey < 0`만 걸렀다

즉 §8 무성 생존상의 조건("파문 0회 + 이동거리 > 0")을 **태그당한 플레이어가 구조적으로 충족**한다. 먼저 죽은 사람이 남은 라운드를 8m/s로 날아다니며 최대 거리를 쌓아, 조심스럽게 살아남은 러너를 거의 항상 이긴다 — 상 이름과 정반대의 수상자가 나온다.

**수정**: `AccumulateDistances`의 누적 조건에 `&& !p.IsTaggedOut` 한 항을 추가했다(집계 한 곳만 변경).

- **`IsTaggedOut`을 쓴 이유**: 태그 상태의 진실은 `TagNetworkSync`의 SyncVar이고, `IsTaggedOut`이 그것을 먼저 보고 `CurrentRole == Echo`를 폴백으로 둔다. `CurrentRole`만 보면 **태그 확정과 역할 반영 사이의 프레임**에서 거리가 새어 들어간다. 초기 배정 제외(`EnsureRolesAssigned`)가 이미 쓰는 술어라 코드도 일관된다.
- **태그 전까지 쌓인 거리는 그대로 남긴다** — 러너로 실제 움직인 몫이다.
- **`_lastPositions` 갱신은 건드리지 않았다** — 제외하면 항목이 낡아서, 리매치로 다시 러너가 될 때 첫 프레임이 엉뚱한 거리를 낸다.

**부수 정정**: `MaxDistancePerFrame` 주석이 최고 속도를 "질주 7.5m/s"로 적고 있었다 → **메아리 8.0m/s**로 정정. 2m 임계값 판정에는 영향이 없다(60fps에서 15배 여유).

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **56** | §8 무성 생존상 — **메아리 제외 여부 미명시** | 메아리는 발소리가 없어 조건을 **구조적으로** 충족하므로 제외하는 것이 의도에 부합한다고 판단. 태그 아웃 이후 이동만 제외하고 그 전 거리는 유지 |

### ⚠ 이번에 고치지 않은 것

- **§3.4 "동일 발화자 게이지 갱신 최소 0.5초 쿨다운"이 미구현**이다. 원문 명시 항목이지만 목적이 "판정이 아니라 UI 재생 스팸 방지"이고, `VoiceGate`의 발신 주기(대화 1.2초·고함 2.5초)가 이미 0.5초를 크게 넘겨 대부분 자동 충족된다. GAP-3 재판정(0.25초)의 `Updated` 경로만 이 쿨다운을 거치지 않는다.
- **§3.4 게이지 표현이 원문의 "화면 가장자리 링 구간"이 아니라 안쪽 ▲ 마커**다. §5.4 방향 힌트(◆)와 겹치지 않게 한 선택이며 GAP-53("최소 표시") 범위 안이지만, 원문이 모양·위치를 지정하고 있다는 점은 기록해 둔다.
- **`RoleAssigner.MaximumPlayers = 6`이 런타임에서 강제되지 않는다.** 참조처가 테스트뿐이다. §6.2의 나머지 열(술래 이속 +5~12%·밸브 수 2~3·제한시간 6~12분)이 4인 값 고정인데, 기획서가 "M3에서는 4인 행만 구현한다"고 명시했으므로 위반은 아니다. 다만 5~6명이 들어오면 배정만 되고 밸런스는 4인 값으로 돈다.
- **`WinConditionEvaluator`에서 `totalValves == 0`이면 탈출 1명으로 즉시 RunnersWin**이 된다(`0 >= 0`). 씬에 밸브 3개가 있어 도달하지 않지만, 게이트 개방 판정이 Presentation에 있어 3/3에서 확인한다.

### 검증

- 신규 **2케이스**(`AwardTallyTests`): 메아리 구간이 빠지면 실제 생존자가 수상하는지 / **반례** — 메아리 거리가 새어 들어가면 메아리가 수상해 버리는지(회귀 시 어떤 값이 나오는지까지 고정).
- 총계 496 → **498 전수 통과**. 전 어셈블리 0 error/0 warning.

> **테스트의 한계**: `AccumulateDistances`는 `NetworkBehaviour` 안이라 EditMode로 직접 실행할 수 없다. 위 2케이스는 **`AwardTally`가 받는 입력의 계약**을 고정할 뿐, Net 계층에서 제외가 빠지는 회귀를 자동으로 잡지는 못한다. 실기(§29)에서 태그당한 뒤 한참 날아다닌 플레이어가 무성 생존상을 받지 않는지 확인해야 한다.

---

## 전체 재검증 (2/3) — Net 계층 전수

> `*NetworkSync` 6종 + 지원 3종을 §14.3 이벤트 표와 대조 검증(A~J 10항목).

### 결과 — 7항목 정상 / 2항목 문제 / 경미 5건

| 항목 | 결과 |
|---|---|
| A 페이즈 상태기계 · D 밸브 · E 태그 · F 파문 · G 준비 · I NRE·static · J 이벤트 표 | ✅ |
| **B 서버 권위 신뢰 모델** | ❌ 역할이 서버 권위가 아님 → **이번에 수정** |
| **C 라운드 중 역할 재배정** | ❌ 술래 교체·소실 → **이번에 수정** |

**잘 되어 있는 것**: 스프린트 10의 NRE 교훈(`NetworkObject != null` 선확인)이 `NetworkBehaviour` 6종 **전부**에 일관 적용됐고, `Spawned` 목록을 가진 5종 모두 도메인 리로드 대비 static 리셋 훅을 갖췄다. `PerceivedPulse`의 와이어 레벨 GAP-2 강제(좌표 비공개면 페이로드에 싣지 않음)도 §14.3 "해당 리스너만"을 정확히 구현한다. `ReadyNetworkSync`는 **유일하게 `RequireOwnership` 기본값(true)** 을 써서 위장을 프레임워크 수준에서 차단한 모범 사례다.

### ❌ 수정 — 라운드 중 합류가 술래를 바꾸거나 없앴다

`TickRound`가 InGame 매 프레임 `EnsureRolesAssigned()`를 부르는데, **미배정자가 하나라도 생기면 전원 재배정**이 돌았다. 그런데 술래 순번이 `SeekerRotation.SeekerOrderIndex(roundNumber, playerCount)` = `roundNumber % playerCount`라 **인원수에 의존**한다.

| 증상 | 재현 |
|---|---|
| **1. 술래 교체** | 5라운드·3인 → 순번 `5%3=2`. 4번째가 합류하면 `5%4=1`로 바뀌어 진행 중이던 술래가 러너로 강등되고 다른 러너가 술래가 된다 |
| **2. 술래 0명** | 새 순번이 **이미 태그당한 플레이어**를 가리키면 `if (p.IsTaggedOut) continue`로 건너뛰어 아무도 술래가 되지 않는다. 동시에 기존 술래는 `RoleForOrder`가 Runner를 돌려줘 강등된다 |

**악의 없이 정상 플레이에서 발생한다** — 라운드 중 한 명만 들어오면 된다.

**수정**: `EnsureRolesAssigned(bool roundStart)`로 두 경로를 분리했다.

- `roundStart: true`(카운트다운 완료 1회) — 이번 판 술래 순번을 `_fixedSeekerOrder`에 **확정**하고 전원에게 §6.2 표대로 배정
- `roundStart: false`(InGame 매 프레임) — `AssignLateJoinersAsRunners`가 **미배정자만 러너로** 채우고 기존 배정은 읽지도 쓰지도 않는다
- `ServerResetWorld`에서 `_fixedSeekerOrder = -1`로 되돌려 다음 라운드가 새 라운드 번호로 다시 정한다

**술래가 도중에 이탈해도 재추첨하지 않는다**: 진행 중인 라운드에서 역할을 바꾸는 것이 술래 공석보다 더 큰 혼란이고, §2.3 로테이션은 다음 판에 정상 동작한다. 배정 규칙 자체(§6.2 표·OwnerId 오름차순·메아리 제외)는 무변경이다.

### GAP-22 기록 정정

스프린트 13이 GAP-22에 적은 **"새 플레이어가 들어와도 기존 술래가 바뀌지 않는다"**는 술래가 항상 정렬 0번이라는 전제에서 나온 성질이었다. 스프린트 21의 로테이션이 그 전제를 없앴으므로, 이제 그 보장은 **정렬 규칙이 아니라 순번 고정**에서 나온다. `phase-3-build-log.md` §21과 `프로젝트_현황_종합.md` 해당 항목에 경고를 추가했다.

### 검증 공백이었던 이유

`RoleAssignerTests.ReassignmentStability`가 **2-인자 오버로드**(`RoleForOrder(0, count)` = `seekerOrderIndex: 0` 고정)만 보고 있었다. 로테이션 이전 형태를 검증하고 있어 스프린트 21이 깨뜨린 불변식을 잡지 못했다. `SeekerRotationTests`도 "인원 변화 + 태그 아웃" 조합은 다루지 않았다.

- 신규 **5케이스**: 순번이 인원수에 의존한다는 사실 자체(2 TestCase — 고정이 필요한 이유를 코드로 못박음) / 고정 순번이면 합류 전후 역할이 동일한지 / 합류 후에도 술래가 **정확히 1명**인지(증상 2 방어, 라운드 0~11 × 2~5인 전수) / 신규 접속자가 술래가 되지 않는지.
- 총계 498 → **503 전수 통과**. 전 어셈블리 0 error/0 warning.

> **테스트의 한계**: `EnsureRolesAssigned`는 `NetworkBehaviour` 안이라 EditMode로 직접 못 돌린다. 위 5케이스는 **고정 순번이라는 전략이 옳다는 것**을 순수 규칙 위에서 고정할 뿐, `_fixedSeekerOrder`를 다시 재계산으로 되돌리는 회귀는 자동으로 잡지 못한다. 실기 확인 항목: **라운드 중 3번째 참가자가 들어와도 술래 표시가 그대로인지**.

### ❌ 수정 — B: 역할이 서버 권위가 아니었다 (GAP-16/18/20 이월분 해소)

ServerRpc 3종이 클라이언트가 보낸 `RoleType`을 그대로 판정에 쓰고 있었다. 세 곳 모두 `NetworkConnection caller`를 **파라미터로 이미 받아 놓고 사용하지 않았다.**

| RPC | 우회 가능했던 규칙 |
|---|---|
| `ValveNetworkSync.ServerSubmitHold` | **GAP-5** — 메아리가 `role=Runner`로 밸브 회전 |
| `TagNetworkSync.ServerRequestTag` | **§3.1** — 러너가 `seekerRole=Seeker`로 태그 |
| `RoundNetworkSync.ServerSubmitEscape` | **GAP-11** — 술래·메아리가 `role=Runner`로 탈출 → 즉시 RunnersWin |

**단일 진입점 신설**: `RoleNetworkSync.TryGetCallerIdentity(caller, out role, out playerId)`.

```
caller == null || caller.FirstObject == null   → false (신원 확정 불가 → 요청 폐기)
RoleNetworkSync 컴포넌트 없음                  → false
그 외 → role = IsTaggedOut ? Echo : CurrentRole,  playerId = (ulong)caller.ClientId
```

- 위치를 `caller.FirstObject`에서, 발생원 ID를 `caller.ClientId`에서 얻는 **`PulseNetworkSync`의 패턴(GAP-24)을 역할까지 확장**한 것이다. 같은 코드베이스 안에 이미 정답이 있었다.
- **태그 아웃 우선**: `IsTaggedOut`이면 역할 SyncVar와 무관하게 Echo로 본다 — 태그 확정과 역할 반영 사이의 프레임에서도 메아리가 물리 상호작용을 하지 못한다.
- NRE 가드가 반환값에 통합돼 있어 세 호출부가 같은 방식으로 거부한다.

**세 RPC의 페이로드에서 주장 가능한 값을 아예 제거했다** — 무시하는 것이 아니라 **보내지 않는다**:

| RPC | 이전 | 이후 |
|---|---|---|
| `ServerSubmitHold` | `(playerId, role, held, caller)` | `(held, caller)` |
| `ServerRequestTag` | `(seekerId, seekerRole, caller)` | `(caller)` — 페이로드 비어 있음 |
| `ServerSubmitEscape` | `(playerId, role, caller)` | `(caller)` |

**Core 인터페이스 3종은 건드리지 않았다** — `IValveNetworkBridge.SubmitHoldIntent`·`ITagTarget.RequestTag`·`IRoundNetworkBridge.SubmitEscapeIntent`의 인자는 로컬 단독 실행 구현체(`TaggableRunner` 등)가 실제로 쓴다. 바뀐 것은 **신뢰 경계인 RPC 페이로드**뿐이다.

**GAP-18의 이월 사유는 이미 만료돼 있었다**: *"로컬 필드 → `RoleAssigned` 네트워크화 시 재검증"*으로 적혀 있었는데 그 네트워크화는 **스프린트 13에서 완료**됐고, 재검증만 누락된 채 남아 있었다.

### 검증

- **기존 503케이스 전수 통과, 회귀 0.** 전 어셈블리 0 error/0 warning.
- **테스트 추가 없음** — Core 역할 규칙은 이미 3종 모두 덮여 있다: `ServerValveDriverTests.BeginHold_ByEcho_IsRejected`, `ServerTagDriverTests.NonSeekerTagger_IsRejected`(Runner·Echo), `ServerRoundDriverTests.Escape_NonRunner_IsRejected`(Seeker·Echo). **바뀐 것은 규칙이 아니라 역할의 출처**이고, 그 출처(`TryGetCallerIdentity`)는 `NetworkConnection`이 필요해 EditMode로 돌릴 수 없다. 중복 테스트를 늘리지 않았다.
- **실기 확인 항목**: 메아리로 전환된 뒤 밸브 E를 눌러도 회전하지 않는지, 러너끼리 붙어도 태그가 성립하지 않는지, 술래가 배수로 출구에 들어가도 라운드가 끝나지 않는지.

### 여전히 이월인 것

**위치(거리) 스푸핑**은 그대로다 — §14.4-3 *"친구 대상 게임이므로 안티치트는 과투자하지 않음"*이 유효하고, 이번 수정은 **역할**이라는 정확성 문제만 다뤘다. 밸브 범위 판정은 여전히 클라이언트가 수행한다(GAP-17 유지).

### ⚠ 고치지 않은 것

- **`ConnectionService.HasStarted`가 되돌아오지 않는다.** 접속 실패(주소 오타·호스트 미기동) 후에도 true로 남아 조기 반환에 걸려, **Play를 다시 시작해야 재시도할 수 있다**. §12.2 "코드 입장" 흐름에 직접 닿는다.
- **`NetworkBridge`는 완전한 죽은 코드**다(코드 참조 0건, 씬·프리팹 배치 0건). §15.3이 "§14.3 이벤트 테이블 전체를 이 계층에서"로 지정했으나 실제 배선은 시스템별 `*NetworkSync`로 갔다 — SyncVar/RPC가 `NetworkBehaviour`에 붙어야 하는 FishNet 구조상 **그 선택이 옳다**. 삭제하거나 §15.3 편차로 명시 기록할 대상.
- **`PlayerTagged`의 `seekerId`가 전파되지 않는다**(§14.3 페이로드는 `{seekerId, targetId, timestamp}`). 대상별 `SyncVar<bool>`이라 targetId는 암시되지만 "누가 태그했는지"는 어느 피어도 모른다. 현재 소비자가 없어 영향 없음.
- **`RoundNetworkSync.ResetForNewSession`이 `ServerInstance`를 되돌리지 않는다**(`ServerPhase`만). Unity의 파괴 객체 `== null` 오버로드 덕에 무해하지만 다른 static을 전부 리셋하는 패턴에서 혼자 빠져 있다.
- **`_lastPositions`가 `ServerResetWorld`에서 정리되지 않는다.** 리매치 시 낡은 좌표와 비교되나 `MaxDistancePerFrame`이 걸러내 무해하다.

### 상태

B·C 수정 완료, 경미 5건 미수정. **실기 검증 대기.**

---

## 전체 재검증 (3/3) — Presentation 계층 전수

> UI·입력·시각화 36파일을 §12.4/§12.5/§12.6/§5.6 원문과 대조 검증(A~J 10항목).

### 결과 — 7항목 정상 / 1항목 문제 / 경미 5건

| 항목 | 결과 |
|---|---|
| A 소유권 가드 · B 라우팅 · C 차폐 구현체 · D HUD · E 결과 화면 · F 설정 · H 지연 바인딩 | ✅ |
| **G 접속 실패 시 재시도 불가** | ❌ → **이번에 수정** |
| **J TagDetector RPC 스팸** | ⚠ → **이번에 수정** |

**잘 되어 있는 것**: 원격 프록시 입력 차단 가드가 상호작용 3종(밸브·태그·탈출) **전부**에 있고, `IsLocallyControlled` 기본값이 true라 로컬 단독 실행 경로가 그대로 보존된다. `PhysicsOcclusionProbe`는 §5.6 태그 매핑표를 정확히 구현하며 런타임 경로와 테스트 경로가 **같은 `OcclusionAccumulator`를 공유**해 규칙이 두 벌이 되지 않는다. `LocalPlayerRegistry`는 실기에서 확정된 "원격 pawn이 Current를 덮어쓰는" 버그를 문서화하고 해제 시 복구까지 구현했다.

**1/3에서 유보했던 항목 해소**: `WinConditionEvaluator`의 `totalValves == 0` 경계는 **도달 불가**다 — `ValveObjectiveTracker.IsEscapeGateOpen`이 `TotalValves > 0`을 요구하고, 탈출은 게이트 개방이 전제라 `runnersEscaped >= 1`이 성립할 수 없다.

### ❌ 수정 — 접속에 한 번 실패하면 빠져나올 수 없었다 (GAP-57)

`ConnectionService.HasStarted`는 **시도 즉시** true가 되고 실패해도 false로 돌아오지 않았다. `LobbyScreen.Update`는 이 플래그로 분기하는데:

```
HasStarted == false → 접속 키 입력 처리 O
HasStarted == true, IsNetworkActive == false → ShowConnecting() 후 return   ← 입력 없음
```

접속이 성립하지 않으면 `IsNetworkActive`는 영영 false다. 즉 **호스트 없는 주소로 참가를 한 번 시도하면 "접속 중…"에서 영구 정지**하고, 타임아웃·에러 표시·취소 경로가 전혀 없어 Play를 재시작해야 했다. §12.2 "코드 입장"의 실사용 흐름에 직접 닿는 문제다.

**수정 3가지**:

1. **자동 복구** — `ConnectionService`가 FishNet `ClientManager.OnClientConnectionState`를 구독해 `LocalConnectionState.Stopped`에서 `HasStarted`를 되돌린다. **실패한 시도도 이 경로로 온다**(연결이 성립하지 않으면 전송 계층이 Starting → Stopped로 떨어뜨린다). 정상 플레이 중 호스트가 나가 끊긴 경우도 같은 경로이며, 그때도 입력 상태로 돌아가는 것이 맞다.
   - 지시서는 `ClientConnectionState.Stopped`로 적었으나 실제 열거형은 **`LocalConnectionState`**(벤더 소스 `Transporting/ConnectionStates.cs`)라 그쪽을 썼다.
   - `OnEnable` 시점에 NetworkManager가 준비되지 않았을 수 있어 **접속 시작 직전에도 구독을 재시도**한다(멱등).
2. **수동 취소** — `IConnectionService.Cancel()` 신설. **전송 계층을 실제로 내린다** — 플래그만 되돌리면 FishNet이 계속 시도하다 나중에 접속돼, 화면은 입력 상태인데 뒤에서 세션이 살아 있는 상태가 된다. 호스트로 시작했으면 서버까지 내린다(`_startedAsHost` 추적).
3. **UI 출구** — `LobbyScreen`의 대기 상태에서 `Esc`를 받고, `ShowConnecting`이 `"Esc — 취소하고 돌아가기"`를 표시한다(이전에는 힌트가 빈 문자열이었다).

**타임아웃은 두지 않았다** — 적정 대기 시간이 회선·환경마다 달라 근거 없는 값을 만들게 된다. GAP-57로 기록.

### ⚠ 수정 — TagDetector가 매 프레임 요청을 보내고 있었다 (GAP-58)

`TagDetector.Update`가 사거리 안의 러너에게 **프레임마다** `RequestTag`를 보냈다. 확정되면 `IsTagged`로 멈추지만 왕복 시간 동안 중복 ServerRpc가 나갔고, 더 나쁜 것은 **서버가 거부할 때**다 — 2/3 B항목으로 서버가 역할을 재검증하게 된 뒤로는 클라이언트가 자기를 술래로 오인하면 사거리 안에 서 있는 내내 초당 수십 건이 무한 전송된다.

**수정**: 대상별 쿨다운(`Dictionary<ulong, float> _nextRequestAt`) — 밸브 `ValveNetworkSync`의 송신 디듀프와 같은 목적이다.

- 요청을 보내면 `_retryIntervalSeconds`(기본 0.5초) 동안 같은 대상에게 재전송하지 않는다
- **사거리를 벗어나거나 태그가 확정되면 항목을 지운다** → 다시 붙었을 때는 즉시 시도한다(§3.1 "1회 접촉 즉시 확정"의 체감 유지)
- 거부가 지속돼도 초당 2회로 수렴한다

재시도 간격은 기획서에 근거가 없어 GAP-58로 기록하고 인스펙터 조정 가능하게 뒀다.

### 🗑 `NetworkBridge` 삭제 — §15.3 편차 기록

코드 참조 0건·씬/프리팹/에셋 GUID 참조 0건을 재확인하고 `NetworkBridge.cs`와 `.meta`를 삭제했다.

§15.3은 이 클래스를 *"14.3 이벤트 테이블 전체를 이 계층에서 송수신"*하는 격리 계층으로 지정했지만, 실제 배선은 시스템별 `*NetworkSync`(`ValveNetworkSync`·`TagNetworkSync`·`RoundNetworkSync`·`RoleNetworkSync`·`ReadyNetworkSync`·`PulseNetworkSync`)로 갔다.

**그 선택이 옳다**: FishNet의 `SyncVar`/`[ServerRpc]`/`[TargetRpc]`는 **`NetworkBehaviour`에 붙어야** 동작한다(IL 위빙 대상). 단일 브릿지로 모으면 모든 이벤트가 한 컴포넌트를 우회 경유하게 되고, 파문의 청취자별 `TargetRpc`처럼 오브젝트 소유가 중요한 경우는 아예 표현할 수 없다. §15.2가 요구한 **"Core가 FishNet을 직접 참조하지 않는다"**는 목적은 Core 인터페이스 13종(`IValveNetworkBridge`·`IPulseNetworkBridge` 등)이 이미 달성하고 있다 — 격리는 유지되고 위치만 달라졌다.

Phase 2에서 만든 골격이 Phase 3의 실제 패턴에 흡수된 것이며, 남겨 두면 1/3에서 세운 "사용처 0건 = GAP-4 유형" 기준에 걸리는 유일한 클래스였다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **57** | 접속 대기 타임아웃 시간이 미명시 | 타임아웃을 두지 않고 **수동 취소(Esc)** + 전송 계층 Stopped 자동 복구로 해소. 적정 대기 시간은 회선·환경 의존이라 값을 만들지 않았다 |
| **58** | 태그 요청 재시도 간격이 미명시(§3.1은 "1회 접촉 즉시 확정"만 규정) | 대상별 0.5초 쿨다운. 사거리 이탈·태그 확정 시 즉시 해제해 첫 접촉 체감은 그대로 |

### 검증

- **기존 503케이스 전수 통과, 회귀 0.** 전 어셈블리 0 error/0 warning(Net의 21건은 전부 벤더 `Assets/FishNet`).
- **테스트 추가 없음** — 세 변경 모두 `MonoBehaviour`/FishNet 콜백 경로라 EditMode로 실행할 수 없다. 순수 규칙이 바뀐 것이 아니라 **전송·UI 상태 전이**가 바뀌었다.
- **실기 확인 항목**: ① 호스트 없이 `J` → "접속 중…"에서 `Esc`로 복귀 → 다시 `H`로 호스트 시작 가능한지 ② 호스트가 나갔을 때 클라이언트가 입력 상태로 돌아오는지 ③ 술래가 러너에 붙어 있을 때 `[TagNet:Server]` 거부 로그가 초당 2회 이하인지.

### ⚠ 고치지 않은 것

- **`PhysicsOcclusionProbe.MaxHits = 32` 초과 시 조용히 잘린다.** `RaycastNonAlloc` 버퍼가 차면 나머지 벽을 세지 않아 감쇠가 약해지고 소리가 스펙보다 멀리 간다. 45×35m 그레이박스에서는 도달하지 않지만 **포화 경고가 없어**, 미감 단계에서 지오메트리가 촘촘해지면 조용히 어긋난다.
- **`LocalPlayerRegistry.WhenReady`가 1회성이다.** 원격 pawn이 `Current`를 덮어쓴 좁은 구간에 바인딩한 소비자는 복구 후에도 다시 불리지 않는다.
- **§3.4 게이지 위치가 원문과 다르다.** §12.4·§3.4 모두 "화면 가장자리 링"으로 명시하는데 현재는 안쪽 ▲ 마커다(§5.4 힌트와 겹치지 않게 한 선택). §12.4는 게이지를 **HUD 요소**로 분류하므로 정식 표현은 `InGameHud`(uGUI)가 자연스러운 자리다.
- **§3.4 "동일 발화자 0.5초 게이지 갱신 쿨다운"** 미구현(26 더블체크에서 기록).
- **`ResultScreen` 클래스 주석이 뒤처져 있다** — 스프린트 17 시점의 "제외 항목"으로 어워드·리매치 투표를 적어 뒀으나 둘 다 스프린트 18·22에서 구현됐다.

---

## 스프린트 27 — 메아리 노크 (§3.2)

> 선행: 26a~26c 음성 파이프라인, 전체 재검증 1~3/3.
> 목적: §8 **최고의 거짓말상**이 처음으로 수상자를 낼 수 있게 한다(GAP-37 잔여분).

### 1. §3.2 원문 — 지시서 전제와 다른 부분이 있었다

```
- 노크 능력:
  - 쿨다운 30초, 사거리 제한 없음(맵 내 임의 지점 지정)
  - 조작: 미니맵/탑다운 뷰에서 지점 클릭 → 1.5초 지연 후 해당 지점에서 소음 발생
  - 발생 소음: "대화" 등급과 동일 취급 (반경 9m, 지속 1.2초) — 술래·도망자 모두 인지 가능
  - 전략적 의미: 도망자를 도와 술래를 유인하거나, 반대로 도망자를 낚아 술래에게
    정보를 흘릴 수도 있음(플레이어 재량)
```

**지시서 §0은 "메아리는 벽을 두드려"로 전제했지만, 원문에 벽·근접 개념이 없다.** 노크는
**원격 지점 지정**이며 "사거리 제한 없음"이 명시돼 있다. 따라서 지시서 Step 2의 "벽 감지"는
구현 대상이 아니고, 대신 **미니맵이 입력 장치로 필요**하다 — 연출이 아니라 능력의 성립 조건이다.

**키는 GAP이 아니었다.** §4.3 표에 명시돼 있다:

```
| 메아리 전용: 미니맵/노크 지정 | Tab(미니맵 토글) + 좌클릭(지점 지정) | 3.2절 노크 UI 근거 |
```

§5.1 노크 행(`9m | 1.2초 | 메아리 능력 사용 | "대화"와 동일 취급`)과 §3.2가 정확히 일치해,
수치는 두 절에서 교차 확인됐다.

**GAP-39("성공 유인" 미정의)는 해소되지 않았다.** §3.2는 "전략적 의미"만 서술하고 판정 기준을
주지 않으며, §8도 "메아리 노크 성공 유인 횟수"라고만 적는다 → **GAP-59로 이번에 정의**했다.

### 2. 기존 `PulseNetworkSync` 경로 재사용 — **가능하되 한 곳만 다르다**

26b(음성)와 마찬가지로 판정·차폐·전달·시각화·집계를 전부 재사용한다. **다만 노크는 위치를
클라이언트가 지정하는 유일한 소리**라, `ServerSubmitPulse`(종류만 받음)를 그대로 쓸 수 없다.

| 계층 | 발소리·밸브·음성 | 노크 |
|---|---|---|
| 발생 위치 | 서버가 `caller.FirstObject`에서 획득(GAP-24) | **클라이언트가 지정**(§3.2 "임의 지점") |
| 반경·지속 | 서버가 §5.1 표에서 재계산 | **동일** |
| 역할·쿨다운·지연 | — | **서버 강제**(`ServerKnockDriver`) |
| 차폐·청취자별 전달·시각화 | `ActivePulseTracker` → `TargetRpc` | **동일(코드 0줄 추가)** |

그래서 **RPC 하나(`ServerSubmitKnock`)만 추가**하고 그 뒤는 기존 경로에 그대로 태웠다.
새 `NetworkBehaviour`도 새 시각화도 없다.

**임의 지점 지정이 취약점이 아닌 이유**: §3.2가 *"도망자를 낚아 술래에게 정보를 흘릴 수도
있음(플레이어 재량)"*이라고 명시한다 — 러너 위치에 노크하는 것도 **설계된 플레이**다. 그래서
지점 자체는 검증하지 않고, 클라이언트가 못 정하는 것(누가·얼마나 자주·언제)만 서버가 쥔다.

**`SoundType.Knock`을 유지하고 `Talk`로 뭉개지 않았다** — §3.4가 게이지 트리거에서 노크를
명시적으로 제외하므로, 종류를 합치면 메아리가 술래에게 방향 게이지를 띄우게 된다. 반경·지속만
같고 종류는 다르다(테스트로 고정).

### 3. 수정·생성 파일

| 계층 | 파일 | 내용 |
|---|---|---|
| Core (신규) | `Sound/KnockConfig.cs` | §3.2 상수 4종(30초·1.5초·9m·1.2초) |
| Core (신규) | `Sound/ServerKnockDriver.cs` | 역할·쿨다운·지연·유인 판정(순수, EditMode 검증) |
| Core (수정) | `SoundPulse/ServerPulseDriver.cs` | `case SoundType.Knock` 1행 |
| Core (수정) | `Net/IPulseNetworkBridge.cs` | `SubmitKnock(Vector3)` — 위치를 받는 유일한 메서드 |
| Net (수정) | `PulseNetworkSync.cs` | `ServerSubmitKnock` RPC + `TickKnocks`(발생·유인 판정) |
| Net (수정) | `RoundNetworkSync.cs` | `ServerRecordKnockLure` — §8 집계 유입점 |
| Presentation (신규) | `Echo/EchoKnockController.cs` | §4.3 Tab 미니맵 + 좌클릭 지점 지정 |
| Editor (수정) | `SceneFlowSetupTool.cs`·`NetworkSetupDiagnostics.cs` | SceneFlow 부착 + 미부착 시 ✖ |
| 씬 | `Lobby.unity` | SceneFlow에 `EchoKnockController` 부착(순수 MonoBehaviour라 YAML 안전 — `InGameHud` 선례) |

**미니맵 카메라는 런타임 생성**이다(`InGameHud`가 uGUI를 코드로 세우는 것과 같은 판단) —
씬 YAML로 카메라 계층을 편집하면 에디터 생성값이 손상되고, 에디터 툴로 만들면 "툴 실행 후
씬 저장 누락"(스프린트 14 실기 실패 원인)이 재발할 수 있다.

### 4. 유인 판정 (GAP-59)

§3.2·§8 어디에도 정의가 없어 이 프로젝트가 정했다. **임의 상수를 만들지 않고 §3.2의 두 수치에
묶었다**:

- 노크가 **발생한 순간**(1.5초 지연 후) 술래가 노크 지점에서 **9m 밖**(= §3.2 파문 반경)에 있었고
- **30초 안**(= §3.2 쿨다운)에 그 반경 **안으로 들어오면** → 유인 1회
- 한 노크당 최대 1회. **이미 그 자리에 있던 술래는 세지 않는다** — "유인"은 이동을 만들어낸
  경우를 뜻하므로, 발생 시점에 이미 반경 안이면 그 노크는 감시에서 버린다.

판정은 서버가 술래의 서버 측 위치로 수행한다(§8 집계는 서버 권위 — 26b·GAP-56과 같은 원칙).

### 5. 검증

- 신규 **19케이스**: §3.2 상수 4종이 원문과 일치 / 비-메아리 요청 거부(러너·술래) / 쿨다운 경계·
  플레이어별 독립·남은 시간 / 1.5초 전 미발생·후 발생·1회만 발생 / **유인 5종**(밖→안 성공,
  이미 안이면 미집계, 창 만료 후 미집계, 노크당 1회, 경계 포함) / 유인 상수가 §3.2 수치에
  묶여 있는지 / `Reset` / 노크 반경·지속이 대화와 같되 **게이지 트리거는 다른지**.
- 기존 테스트 2건 갱신: 스프린트 14가 고정해 둔 "노크는 거부된다"가 이번에 의도적으로 바뀌었다
  (26b에서 음성이 같은 경로를 밟은 것과 동일).
- 총계 503 → **522 전수 통과**. 전 어셈블리 0 error/0 warning.

> 지시서는 "기존 483케이스"로 적었으나 실제 기준은 **503**이었다(26b 이후 재검증 3회분이 누적).

### 6. GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **59** | §3.2·§8이 **"성공 유인" 판정 기준을 정의하지 않는다**(GAP-39 미해소분) | 발생 시 9m 밖 → 30초 내 9m 안 진입 = 1회. 두 수치 모두 §3.2 원문(파문 반경·쿨다운)에서 가져와 임의 상수를 만들지 않았다 |
| **60** | 미니맵 카메라의 **맵 중심·표시 범위**가 기획서에 없다 | §3.4 주석의 맵 크기(45×35m)를 담도록 `orthographicSize = 20`, 중심 원점. 인스펙터 조정 가능 |

### 7. 실기 검증

**미실시** — 절차는 `수동검증_절차.md` §29. 핵심 항목: 메아리만 Tab 미니맵이 열리는지 ·
좌클릭 1.5초 뒤 파문이 뜨는지 · 술래가 그쪽으로 이동하면 `[KnockNet:Server] ★ 유인 성공`이
찍히는지 · **결과 화면에 최고의 거짓말상 수상자가 처음으로 나오는지** · 술래·러너로는 노크 불가.

### 상태

**코드 완료** — 실기 검증(§29) 대기. §8 어워드 3종이 **전부 데이터 소스를 갖게 됐다**
(최다 비명상 = 26b 음성, 무성 생존상 = 스프린트 22 + GAP-56, 최고의 거짓말상 = 이번 스프린트).

---

## 스프린트 27 후속 — 실기 버그: 메아리 로컬 입력이 순수 클라이언트에서 죽어 있었다 (GAP-61)

> 첫 실기(§29, 에디터 호스트 + exe 2대, 술래1·러너2)에서 발견.
> 증상: exe 클라이언트에서 태그당한 본인 화면의 **HUD 역할 표시는 "메아리"로 정확히 바뀌는데
> Tab 미니맵이 에러 없이 아무 반응도 하지 않았다.** 에디터(호스트)에서는 정상이었다.

### 1. 원인 — 서버/호스트 체크가 아니라 **1회성 플레이어 바인딩**이었다

보고된 가설(`IsServer`/`IsHost` 오용)은 **아니었다.** Presentation 계층 전체에
`IsServer`·`IsHost`·`IsClientInitialized`·`base.Owner`가 **한 건도 없다**(전수 검색 확인).
소유권 판별은 전부 `PlayerOwnershipGate`(Net) → `ILocalControlGate`(Core) → `IsLocallyControlled`를
쓰고 있었고, 이번에 문제가 된 컴포넌트도 그 값을 읽고 있었다.

실제 원인은 **어느 pawn을 읽느냐**였다.

```
EchoKnockController.cs:67   LocalPlayerRegistry.WhenReady(player => _player = player);  // 1회성
EchoKnockController.cs:102  if (_player == null) _player = LocalPlayerRegistry.Current; // null일 때만 재조회
```

`LocalPlayerRegistry.WhenReady`는 **1회성**이다(`_pending` 호출 후 즉시 비운다). 그리고
`FirstPersonController.IsLocallyControlled`의 **기본값이 true**라(L124), 모든 pawn이 `OnEnable`에서
자기를 등록한다 — 이 함정은 `LocalPlayerRegistry.Register`/`Unregister`의 주석에 이미 실기 근거와
함께 기록돼 있었다.

| | 첫 등록자 | `_pending` 콜백이 받는 pawn | 결과 |
|---|---|---|---|
| **에디터(호스트)** | 자기 pawn(서버가 자기 커넥션 것을 먼저 스폰) | **로컬** | 정상 동작 |
| **exe(순수 클라이언트)** | 스폰 배치로 먼저 도착한 **원격** pawn(호스트 것 등) | **원격** | `IsLocallyControlled == false` → Tab 무반응 |

소유권이 확정되면 `PlayerOwnershipGate`가 원격 pawn에 `SetLocalControl(false)`를 밀고,
`Unregister`의 복구 로직이 `Current`를 진짜 로컬 pawn으로 되돌린다. **그런데 이 컴포넌트만
`_player`에 원격 pawn을 캐시해 둔 채였고, L102는 `null`일 때만 재조회하므로 영영 고쳐지지 않았다.**

**HUD가 멀쩡했던 이유가 결정적 증거다** — `InGameHud.UpdateRole`(L373)은 매 프레임
`LocalPlayerRegistry.Current`를 다시 읽는다. 같은 `FirstPersonController.Role` 값을 보면서도
한쪽만 맞았던 것은 **읽는 대상이 달랐기 때문**이지 역할 동기화 문제가 아니었다.

### 2. 수정 — 기존 경로로 통일(새 게이팅 없음)

`EchoKnockController`에서 **1회성 캐시를 걷어내고 매 프레임 레지스트리를 다시 읽게** 했다.
`InGameHud`·`FallRecoveryDriver`·`PawnPhaseTeleporter`·`SettingsStore`가 이미 쓰는 방식이며,
소유권 판별은 그대로 `PlayerOwnershipGate` → `ILocalControlGate` → `IsLocallyControlled`다.

```diff
- private FirstPersonController _player;
- private void Awake() { LocalPlayerRegistry.WhenReady(player => _player = player); }
-
- private bool IsLocalEcho()
- {
-     if (_player == null) _player = LocalPlayerRegistry.Current;
-     return _player != null && _player.IsLocallyControlled && _player.Role == RoleType.Echo;
- }
+ private bool IsLocalEcho()
+ {
+     FirstPersonController player = LocalPlayerRegistry.Current;
+     return player != null && player.IsLocallyControlled && player.Role == RoleType.Echo;
+ }
```

**새 인터페이스·새 판별 방식을 만들지 않았고, 계층 경계 변화도 없다** — 참조는 전부
Presentation 내부(`LocalPlayerRegistry`·`FirstPersonController`)이고, Net→Core→Presentation
방향(`PlayerOwnershipGate`가 `ILocalControlGate`에 밀어 넣는 기존 흐름)은 그대로다.

### 3. ⚠ 유령 카메라는 버그가 아니라 **미구현**이다

§3.2는 메아리를 *"자유 비행형 유령 카메라, 충돌 없음(벽 통과), 이동속도 8.0 m/s"*로 규정하는데,
**그 전환을 수행하는 코드가 저장소에 존재하지 않는다.** `RoleType.Echo`를 참조하는 Presentation
코드는 4곳뿐이고(노크 컨트롤러·로컬 대역·HUD 문자열·HUD 색), 카메라 교체나 `CharacterController`
비활성화는 어디에도 없다.

이동 특성도 마찬가지다 — `FirstPersonController.ApplyRole`(L76)은 `_role` 필드만 바꾸고
`_simulator`를 다시 만들지 않는다(`_simulator`는 `Awake`에서 초기 역할로 1회 생성, L188).
그래서 태그 후에도 **8.0m/s 비행 속도와 "메아리는 발소리 없음"이 적용되지 않는다.**
이 사실은 `ApplyRole`의 주석에 *"이동 시뮬레이터는 Awake에서 초기 역할로 만들어지며, 역할 배율의
런타임 재적용은 이번 스코프가 아니다"*로 **이미 기록돼 있던 알려진 이월**이다.

따라서 **"에디터(호스트)에서는 카메라 전환이 정상 동작했다"는 관측에 대응하는 코드가 없다.**
호스트/클라이언트 차이가 실재한 것은 Tab 미니맵 쪽이며, 카메라는 양쪽 모두 1인칭 그대로여야 한다.
실기 재검증 때 이 부분을 다시 확인해 주기를 요청한다(§29-0).

### 4. 동일 패턴 전수 검색 — **3곳 더 있다(이번 스코프 밖, 미수정)**

`WhenReady` 1회성 바인딩을 캐시하는 곳:

| 위치 | 캐시 대상 | 원격 pawn에 물렸을 때의 증상 |
|---|---|---|
| `EscapePointTrigger.cs:47` | `_player` | `IsLocallyControlled == false` → **순수 클라이언트에서 탈출이 영영 안 된다** |
| `LocalPulsePipelineBehaviour.cs:99` | `_player`(+`FootstepPulseEmitted` 구독) | 원격 pawn은 `Update`가 조기 반환해 이벤트를 내지 않는다 → **내 발소리가 파문이 되지 않는다** |
| `PulseVisualRenderer.cs:101` | `_viewCamera` | 방위 인디케이터가 남의 카메라 yaw 기준으로 회전 → **방향 표시가 어긋난다** |

셋 다 같은 뿌리이고 `EscapePointTrigger`는 심각도가 높다. 지시대로 **이번에는 고치지 않고 GAP-61에
함께 기록**한다. 3/3 Presentation 재검증에서 *"`WhenReady`가 1회성이라 잘못 바인딩되면 영구적"*으로
⚠ 기록해 둔 항목이 실기에서 실제 증상으로 확인된 것이다.

### 5. 검증

- **522케이스 전수 통과, 회귀 0.** 전 어셈블리 오류 0 / Marco 코드 진단 0.
- **이 버그는 EditMode로 잡을 수 없다.** 원인이 ① `MonoBehaviour` 수명주기(`OnEnable` 등록 순서),
  ② FishNet 스폰 배치 도착 순서, ③ `static` 레지스트리의 프레임 간 상태 — 셋의 조합이라
  `NetworkConnection`과 실제 스폰이 없으면 재현되지 않는다. **호스트/클라이언트 분리 실행이
  있어야만 드러나는 종류**이며, 그래서 코드 완료·522 통과 상태에서도 첫 실기까지 살아남았다.
  회귀 방지는 실기 절차(§29-0)로 대신한다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **61** | `LocalPlayerRegistry.WhenReady`가 1회성이라, 모든 pawn이 기본값 `IsLocallyControlled=true`로 자기를 등록하는 구조와 맞물려 **순수 클라이언트에서 원격 pawn에 영구 바인딩**될 수 있다 | `EchoKnockController`는 매 프레임 `Current` 재조회로 수정. 같은 패턴 3곳(`EscapePointTrigger`·`LocalPulsePipelineBehaviour`·`PulseVisualRenderer`)은 기록만 하고 이월 |

### 상태

**노크 로컬 입력 수정 완료** — 실기 재검증 대기. §3.2 유령 카메라·8.0m/s는 **미구현 이월**로
남아 있으며, 이번 수정 범위가 아니다.

---

## 스프린트 27 후속 2 — 실기 버그: 노크 지정 모드에서 클릭이 안 먹힘 (GAP-62)

> 증상(에디터 호스트): Tab으로 탑다운 뷰 전환은 정상인데, **좌클릭을 해도 지점이 지정되지 않고
> 1.5초 뒤 파문이 발생하지 않는다.** 미니맵 자체(고정 탑다운, `orthographicSize = 20`)는 설계대로다.

### 1. 원인 — 커서 해제 코드가 **아예 없었다**

`EchoKnockController`에 `Cursor` 참조가 **한 건도 없다**(전수 검색). 즉 Tab으로 지정 모드에
들어가도 커서는 §4.1 1인칭 상태 그대로 **잠기고 숨겨진 채**였다.

커서 정책은 `FirstPersonController`가 소유하며, 로컬 조종 중에는 항상 잠근다:

```
FirstPersonController.cs:176  ApplyCursorLock(IsLocallyControlled);
FirstPersonController.cs:181  Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
FirstPersonController.cs:182  Cursor.visible = !locked;
```

이 상태에서 클릭 좌표를 읽는 쪽은:

```
EchoKnockController.cs  mouse.position.ReadValue() → TryResolveWorldPoint → ScreenPointToRay
```

**`CursorLockMode.Locked`에서는 Input System의 마우스 위치가 물리 마우스를 따라가지 않는다.**
값이 잠긴 지점에 고정되므로 **화면 어디를 눌러도 같은 좌표**가 나온다. 사용자는 커서가 보이지도
않으니 어디를 찍는지 알 수도 없다. 이것이 확정된 결함이다.

**"아무 반응이 없다"로 보인 이유가 하나 더 있다** — 노크 발생원은 지정한 메아리 본인이라,
`SoundPulseResolver`의 GAP-1(발생원 제외)에 걸려 **자기 노크 파문은 자기 화면에 뜨지 않는다**.
즉 고정 좌표로라도 노크가 나갔다면 화면상으로는 실패와 구별되지 않는다. 코드만으로는 둘 중
어느 쪽이었는지 단정할 수 없어, `TryResolveWorldPoint` 실패 경로(유일하게 로그가 없던 조용한
반환)에 경고를 추가해 다음 실기에서 구분되게 했다.

### 2. 수정 — 커서 정책 소유자에 해제 경로를 만들고 양방향으로 호출

`Cursor`를 노크 쪽에서 직접 만지지 않았다. 두 곳이 각자 커서를 건드리면 "여는 쪽과 닫는 쪽이
다른 상태를 쓰는" 어긋남이 그대로 생기기 때문이다.

**`FirstPersonController`**(커서 정책 단독 소유자):

```diff
+ private bool _cursorReleased;
+
+ public void SetCursorReleased(bool released)
+ {
+     if (_cursorReleased == released) return;
+     _cursorReleased = released;
+     ApplyLocalControlState();   // 기존 규칙(IsLocallyControlled)과 함께 재계산
+ }

- ApplyCursorLock(IsLocallyControlled);
+ ApplyCursorLock(IsLocallyControlled && !_cursorReleased);

  private void ApplyLook()
  {
+     // 잠금이 풀려도 delta는 계속 들어온다 — 막지 않으면 모드를 닫았을 때 시점이 돌아가 있다.
+     if (_cursorReleased) return;
```

**`EchoKnockController`**(요청만):

```diff
  private void SetMinimap(bool open)
  {
      ...
+     ApplyCursorRelease(open);          // 열면 해제, 닫으면 복원
+     Debug.Log(open ? "커서 해제됨…" : "1인칭 커서 잠금으로 복원한다(§4.1)");
  }
+
+ private void ApplyCursorRelease(bool released)
+ {
+     FirstPersonController player = LocalPlayerRegistry.Current;   // 캐시 금지(GAP-61)
+     if (player != null) player.SetCursorReleased(released);
+ }
```

**복원 경로 3개가 전부 `SetMinimap(false)` 하나로 모인다**(양방향 확인):

| 경로 | 위치 |
|---|---|
| Tab 토글로 닫기 | `Update` L84 |
| 역할이 메아리가 아니게 됨 | `Update` L75 |
| 컴포넌트 비활성화 | `OnDisable` L66 |

`SetMinimap`은 `_minimapOpen == open`이면 조기 반환해 멱등이다.

**시점 회전 억제를 함께 넣은 이유**: 커서만 풀면 미니맵 위에서 마우스를 움직이는 동안 1인칭
시점이 같이 돌아가, Tab으로 닫았을 때 엉뚱한 방향을 보고 있게 된다. "원래 상태로 정확히 복원"에
시점도 포함된다고 봤다.

**계층 경계 변화 없음** — 두 파일 모두 Presentation이고, Core/Net 참조가 늘지 않았다.
미니맵(고정 탑다운·`orthographicSize`)은 손대지 않았다.

### 3. 검증

- **522케이스 전수 통과, 회귀 0.** 전 어셈블리 오류 0 / Marco 코드 진단 0.
- **이 버그는 EditMode로 잡을 수 없다.** `Cursor.lockState`는 Unity 플레이어 루프의 전역 상태이고,
  잠금 상태에 따른 `Mouse.current.position` 동작은 **실제 입력 디바이스와 창(focus)이 있어야**
  재현된다. `Camera.ScreenPointToRay`도 렌더링 컨텍스트가 필요하다. 셋 다 EditMode 러너에
  존재하지 않아, 이 계층은 실기 절차(§29-0)로만 검증된다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **62** | 마우스를 **포인터로 쓰는 모드**가 §4.1 1인칭 커서 잠금과 충돌한다 — 기획서에 모드 전환 시 커서 정책이 없다. 노크는 이 프로젝트 최초의 마우스 기반 UI다 | `FirstPersonController.SetCursorReleased`(커서 정책 단독 소유)를 신설해 요청/복원을 대칭으로 처리. 해제 중에는 시점 회전도 멈춘다. 이후 마우스 UI(§12.6 설정 화면 등)도 같은 경로를 쓴다 |

### 상태

**노크 클릭 처리 수정 완료** — 실기 재검증 대기(§29-0). GAP-61(원격 pawn 바인딩) 수정과
함께 확인한다.

---

## 스프린트 27 후속 3 — GAP-61 잔여 3곳 + §3.2 유령 카메라 구현

### 1부 — GAP-61 잔여 3곳 (기존 조사와 원인 일치)

세 곳 모두 `WhenReady` 1회성 바인딩을 캐시하는 같은 뿌리였고, 조사 결과가 기존 특정과 일치했다.

| 파일 | 캐시 | 수정 |
|---|---|---|
| `EscapePointTrigger.cs` | `_player` | `ResolvePlayer()` 신설 — 매 프레임 `Current` 조회, 인스펙터 참조는 **비네트워크 폴백**으로만 |
| `LocalPulsePipelineBehaviour.cs` | `_player` + **이벤트 구독** | `Update`가 매 프레임 `BindPlayer(Current)` 호출 |
| `PulseVisualRenderer.cs` | `_viewCamera` | `RefreshViewCamera()` 신설 — `Tick`에서 매 프레임 갱신 |

**이벤트 구독은 단순 재조회로 끝나지 않아 별도로 다뤘다.** 다행히 기존 `BindPlayer`가 이미
"같은 pawn이면 즉시 반환 / 다르면 이전 구독 해제 후 재구독"으로 정확히 짜여 있었다 —
**문제는 로직이 아니라 호출 횟수(1회)뿐**이었다. 그래서 `Update`에서 매 프레임 부르되
`if (_player == player) return;` 가드가 변경 시에만 구독을 갈아타게 했다(매 프레임 구독/해제
반복 없음). `player == null` 방어도 추가했다 — 디스폰 구간에 NRE가 나던 자리다.

`PulseVisualRenderer`는 **인스펙터로 카메라를 직접 지정한 구성을 존중**해야 해서
`_viewCameraOverridden` 플래그를 뒀다(Awake 시점에 이미 값이 있으면 레지스트리로 덮지 않는다).

셋 다 Presentation 내부 수정이며 Core/Net 참조가 늘지 않았다.

### 2부 — §3.2 유령 카메라

#### 이동 방식 선택: **Core 순수 규칙 + Presentation이 Transform 직접 이동**

```
- 이동: 자유 비행형 유령 카메라, 충돌 없음(벽 통과), 이동속도 8.0 m/s
```

| 결정 | 이유 |
|---|---|
| 방향·속도 계산을 **Core 신규 `GhostFlight`**(순수)에 | 기존 분담(“Core가 속도·발소리를 정하고 Presentation이 적용”)을 그대로 따른다. 순수라 EditMode로 전수 검증된다 |
| **`CharacterController`를 끈다**(`enabled = false`) | `Move()`는 컴포넌트가 켜져 있는 한 캡슐 충돌을 한다(`detectCollisions`는 *남이 나를* 밀 때만 관여). §3.2 "벽 통과"는 컨트롤러를 끄고 `Transform`을 직접 움직이는 방법으로만 성립한다 |
| `LocomotionSimulator`는 **계속 돌린다** | 상태 전이와 §5.1 "메아리는 발소리 없음" 판정이 거기 있다. 비행 분기는 그 뒤에 적용된다 |
| 3축 방향은 **카메라 기준** | §3.2 "자유 비행형 유령 **카메라**" — 바라보는 쪽으로 난다 |

**알려진 이월 해소**: `ApplyRole`이 `_role`만 바꾸고 `_simulator`를 재생성하지 않던 문제를
고쳤다 — 이제 역할이 바뀌면 시뮬레이터를 새로 만들고 비행 상태도 함께 반영한다. 이게 없으면
태그 후에도 8.0m/s와 발소리 억제가 적용되지 않았다.

**중력 누적 제거**: 비행 진입 시 `_verticalVelocity = 0`. 남겨 두면 지상 복귀 첫 프레임에
바닥을 뚫는다(`FallRecoveryDriver`가 겪은 것과 같은 함정).

#### 발소리 억제

**Core가 이미 하고 있었다** — `LocomotionSimulator`의 `if (_role != RoleType.Echo)`가
메아리일 때 펄스를 만들지 않는다. 지금까지 적용되지 않았던 이유는 시뮬레이터가 재생성되지
않아서였고, 위 수정으로 실제로 동작한다. 비행 분기는 `tick.Pulse`를 아예 소비하지 않는다.

**GAP-61 2번과 맞물리는 지점을 확인했다**: 발소리 파문이 실제로 끊기려면 ①메아리 시뮬레이터가
펄스를 안 내고(2부) ②`LocalPulsePipelineBehaviour`가 **내 pawn**을 구독하고 있어야 한다(1부).
둘 중 하나만 고치면 "발소리가 안 난다"의 원인을 구분할 수 없다.

#### 조사 결과 — 메아리 캐릭터의 시각적 가시성 (변경하지 않음)

**결론: 메아리도 다른 플레이어에게 캡슐 몸체가 그대로 보인다.**

- `Player.prefab`에 `Body` 자식이 있고 **MeshFilter + MeshRenderer**를 갖는다(스프린트 9 후속에서 추가한 캡슐, `GrayboxWall` 머티리얼)
- **역할이나 태그 상태로 렌더러를 끄는 코드가 저장소에 없다**(전수 검색)
- §16.1은 *"완전한 흑 배경 위에 발광 라인과 파문만으로 그리는 모노크롬"*이라 **스타일**을 규정할 뿐, 플레이어 모델이 없다고는 하지 않는다. §3.2도 "유령 카메라"는 **이동 방식**을 말하고 렌더링 규정이 아니다

즉 기획서에 명시가 없고 코드는 "보인다" 쪽이다. **이번 스코프에서 바꾸지 않았고 GAP-63으로 기록**한다 —
술래가 메아리를 보고 러너로 착각하거나 반대로 메아리 위치로 러너를 추정할 수 있어, 밸런스 판단이 필요하다.

#### 네트워크 동기화 — **기존 경로 그대로, 새 오브젝트 없음**

`Player.prefab`에 이미 **FishNet `NetworkTransform`**이 있다(스프린트 8). 유령 이동도 결국
`transform.position`을 바꾸는 것이라 **그대로 동기화된다**. 새 `NetworkBehaviour`도 새 `SyncVar`도
필요하지 않았다 — §15.1 씬 분리 이후 겪은 `SceneCondition` 류 문제를 새로 만들지 않는다.

> 다만 `CharacterController`를 끄는 것은 **소유자 로컬에서만** 일어난다(`ApplyRole`은 각 피어에서
> 불리지만 원격 pawn은 `Update`가 조기 반환해 이동하지 않는다). 원격 피어에서도 컨트롤러가 꺼지지만
> 위치는 `NetworkTransform`이 덮으므로 표시에 영향이 없다.

### 3부 — 검증

- 신규 **14케이스**(`GhostFlightTests`): 역할 게이트(러너·술래 false / 메아리 true) · §3.2 8.0m/s가
  `LocomotionConfig.EchoSpeed`와 같은 상수인지 · 전진 최대속도 · **3축 대각에서도 8.0 초과 금지** ·
  무입력 0 · 상승/하강 · **카메라를 내려다보면 실제로 하강하는지**(자유 비행의 핵심) ·
  비정규화 축 입력에도 속도 정확 · **메아리는 120틱 내내 발소리 0** · 러너 대조군은 발소리 발생 ·
  메아리 지상 속도도 8.0 · 노크가 §3.4 게이지를 트리거하지 않는지(회귀 감시).
- 총계 522 → **536 전수 통과**. 전 어셈블리 오류 0 / Marco 코드 진단 0.

**EditMode로 검증 불가능한 부분과 이유**:

| 항목 | 이유 |
|---|---|
| GAP-61 3곳 수정 전체 | `MonoBehaviour` 수명주기 + FishNet 스폰 도착 순서 + `static` 레지스트리의 프레임 간 상태 조합. `NetworkConnection`과 실제 스폰이 없으면 재현 불가 |
| `CharacterController.enabled = false`의 벽 통과 | Unity 물리 엔진과 실제 콜라이더가 필요 |
| `NetworkTransform` 동기화 | 실제 접속 2개 이상 필요 |
| 카메라 기준 이동의 체감 | 렌더링 컨텍스트 필요 |

순수 규칙(속도·방향·발소리 억제)은 전부 EditMode로 덮었고, 나머지는 §29 통합 절차로 검증한다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **61** (완료) | `WhenReady` 1회성 바인딩으로 원격 pawn 영구 캐싱 | **잔여 3곳 전부 수정 완료** — `EscapePointTrigger`·`LocalPulsePipelineBehaviour`·`PulseVisualRenderer`. 매 프레임 재조회(이벤트는 변경 감지 후 재구독) |
| **63** | §3.2·§16.1 어디에도 **메아리 캐릭터의 렌더링 여부**가 없다 | 현재는 캡슐 몸체가 그대로 보인다(코드 확인). 이번엔 바꾸지 않고 기록만 — 밸런스 판단 필요 |
| **64** | §4.3 표에 **메아리 비행의 상승·하강 키가 없다** | 임시로 `Space`(상승)·`Left Ctrl`(하강). Left Ctrl은 §4.3상 잠수 키지만 메아리는 잠수 불가라 충돌하지 않는다. §4.3 키 리바인딩(스프린트 28)에서 확정 |

### 상태

**코드 완료** — §29 통합 실기 재검증 대기. 이로써 스프린트 27 관련 실기 결함 3건(GAP-61·62)과
§3.2 미구현 이월이 전부 해소됐다.

---

## 스프린트 27 후속 4 — GAP-63 해소: 메아리를 생존자에게 숨김

### 결정 근거

메아리는 §1상 "유령 상태로 노크 능력을 통해 계속 참여"하는 **활성 역할**이고, §3.2 노크의
의의는 **은밀한 유인**이다. 생존자에게 메아리 몸체가 보이면 술래가 노크 발신자를 눈으로 찾아버려
그 설계가 무너진다 — §3.2가 명시한 *"도망자를 도와 술래를 유인하거나 … 정보를 흘릴 수도 있음"*이
성립하려면 발신자가 보이지 않아야 한다.

### 1. 구현 방식 — 매 프레임 변경 감지

| 계층 | 산출물 |
|---|---|
| Core (신규) | `Role/EchoVisibility.cs` — `ShouldRender(viewerRole, targetRole, isSelf)` 순수 판정 |
| Presentation | `FirstPersonController.RefreshEchoVisibility()` — 판정 결과를 몸체 렌더러에 반영 |

**갱신 트리거는 매 프레임 두 값 비교**다. 이벤트 하나에 걸지 않은 이유는 **역할이 양쪽에서
독립적으로 바뀌기 때문**이다:

- ① 대상이 태그당해 메아리가 되는 순간 → 생존자 화면에서 사라져야 한다
- ② **뷰어 자신**이 나중에 태그당해 메아리가 되는 순간 → 그전까지 안 보이던 **기존 메아리들이
  그때부터 보여야 한다**

한쪽 이벤트만 구독하면 다른 쪽 전이를 놓친다. 그래서 `(viewer.Role, _role, isSelf)`를 매 프레임
다시 읽어 판정하고, **결과가 바뀔 때만** `Renderer.enabled`에 대입한다(같은 값 반복 대입은
렌더러를 불필요하게 더티 처리한다).

**GAP-61 교훈 적용**: 뷰어(`LocalPlayerRegistry.Current`)를 **필드에 굳히지 않는다** — 소유권
확정으로 바뀔 수 있고, 1회성으로 캐시하면 원격 pawn을 기준으로 판정하게 된다. 반면
`_bodyRenderers`는 **자기 오브젝트의 컴포넌트**라 `Awake`에서 1회 수집해도 안전하다(GAP-61이
금지한 것은 *다른 pawn* 참조를 굳히는 것이다).

**호출 위치가 중요하다**: `Update`의 **소유권 조기 반환보다 앞**에 뒀다. "내가 저 pawn을 그려야
하는가"는 소유권과 무관한 판정이고, 숨겨야 할 대상은 **원격 pawn**이기 때문이다.

**뷰어가 아직 없으면(`Current == null`) 아무것도 하지 않는다** — 기본값으로 추측해 깜빡이게
만들지 않고 다음 프레임에 다시 본다.

### 2. 메아리 상호 가시성 — 요구사항의 가정을 그대로 적용

**기획서에 명시가 없다.** §3.2는 *"메아리끼리는 별도 사망자 채널로 자유 대화"*라고만 하고
서로 보이는지는 규정하지 않는다. 지시대로 **메아리끼리는 보이도록** 구현했고, 구현 중 다르게
판단할 수밖에 없던 부분은 **없었다** — 규칙이 단순해 그대로 들어갔다.

부수적으로 **메아리는 생존자를 계속 볼 수 있게** 뒀다. §3.2 노크 지점 선택이 "누가 어디 있는지"를
전제로 하므로 이쪽은 가정이 아니라 기능적 필요다.

### 3. 자기 자신 렌더링 확인 결과

**원래도 사실상 보이지 않는 구조였고, 이번 변경으로 달라지지 않았다.**

프리팹 실측:

| 요소 | 값 |
|---|---|
| `Body` 캡슐 | local y = 0.9, scale (0.7, **0.9**, 0.7) → 높이 1.8m, **y 0 ~ 1.8 범위** |
| `PlayerCamera` | local y = **1.62** → **캡슐 내부** |

카메라가 캡슐 안에 있고 기본 백페이스 컬링이 걸리므로 자기 캡슐은 화면에 나오지 않는다.
**코드로 끈 적은 없었다**(스프린트 9의 "자기 시점 컬링은 범위 밖" 기록 그대로).

이번 규칙은 `isSelf`를 **최우선 단락**으로 두어 자기 pawn을 절대 건드리지 않는다. 규칙 구조상
자기 자신이 숨겨질 경로가 없지만(뷰어 역할 == 대상 역할이므로), 그림자·3인칭 전환 같은 후속
작업이 꼬이지 않도록 명시적으로 막았다.

### 4. 네트워크 동기화 — 불필요 (요구사항 6 충족)

**순수 로컬 판정이다.** "내가 저 pawn을 그려야 하는가"는 각 피어가 자기 화면에 대해 독립적으로
결정하고, 입력이 되는 역할은 이미 `RoleNetworkSync`/`TagNetworkSync`가 전 피어에 전파하고 있다.
새 `NetworkBehaviour`도 새 `SyncVar`도 만들지 않았다. Core/Net/Presentation 경계 변화 없음.

### 5. 검증

- 신규 **14케이스**(`EchoVisibilityTests`): 생존자 2종이 메아리를 못 보는지 · 생존자 4조합은
  서로 보이는지 · 메아리끼리 보이는지(가정) · 메아리가 생존자를 보는지 · **자기 자신 3역할 모두
  항상 렌더**되는지 · 뷰어가 잘못 전달돼도 자기면 안 숨기는지 · **전이 시나리오 2종**(뷰어가
  메아리가 되면 기존 메아리가 드러남 / 대상이 메아리가 되면 생존자 시야에서 사라짐).
- 총계 536 → **550 전수 통과**. 전 어셈블리 오류 0 / Marco 코드 진단 0.
- **EditMode로 검증 불가능한 부분**: 실제 `Renderer.enabled` 반영과 역할 전환 **시점**의 갱신은
  `MonoBehaviour` 수명주기와 렌더링 컨텍스트가 필요하다. 순수 판정만 덮었고 나머지는 §29 실기다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **63** | §3.2·§16.1에 메아리 캐릭터의 렌더링 여부가 없다 | ✅ **해소** — 생존자 시점에서 메아리 몸체를 숨긴다. **메아리 상호 가시성은 명시가 없어 "보인다"로 가정**하고 구현했다(기록 유지) |

### 상태

**코드 완료** — §29 통합 실기 재검증 대기.

---

## 기획서 정합 2단계 — 노크 규칙 정리 (B3·B4·B5)

갱신된 `docs/마르코_상세기획서.md` §3.2·§3.2-1·§4.3을 코드에 반영했다.

### B5. 미니맵 삭제 — 발생 위치를 메아리의 현재 위치로

§3.2-1이 **"미니맵 시스템은 존재하지 않는다"**, §4.3이 `Tab(미니맵 토글) + 좌클릭(지점 지정)` 행의
삭제를 명시하면서, 노크만 예외적으로 지점을 클라이언트가 정하던 구조가 통째로 사라졌다.

**이제 노크는 발소리·밸브·음성과 완전히 같은 규칙이다** — 서버가 `caller.FirstObject`에서 위치를
얻는다(GAP-24). 그 결과 `ServerRpc` 페이로드가 **비었다**: 클라이언트가 정할 수 있는 것은
"지금 쓴다"뿐이다.

**GAP-66 (신규 판단)**: §3.2는 1.5초 지연 **중**에 메아리가 움직였을 때 어느 위치에서 소리가
나는지 규정하지 않는다. **요청 시점의 위치로 고정**했다 — 그래야 "그 자리에 가서 누르고
빠져나온다"는 §3.2의 유인 플레이가 성립한다. 발생 시점 위치를 쓰면 메아리가 소리를 끌고 다니게 된다
(6.0m/s × 1.5초 = 9m, 노크 반경과 같은 크기라 무시할 수 없는 차이다).

### B3. 라운드당 5회 하드캡 · B4. 전환 후 20초 잠금

`ServerKnockDriver`가 **역할 → 횟수 → 잠금 → 쿨다운** 순으로 판정한다. 순서에 이유가 있다:
5회를 다 쓴 사람에게 "쿨다운 12초"라고 알리면 기다리면 다시 쓸 수 있다는 잘못된 기대를 준다.

**20초 잠금의 기준 시각**은 `ObserveEcho`가 기록한다 — `PulseNetworkSync`가 매 틱
`RoleNetworkSync.EffectiveRole`로 메아리를 관측해 넘기며, **최초 1회만** 기록되므로 매 틱 불러도
잠금이 뒤로 밀리지 않는다. 청취자 스냅샷의 `Role`이 아니라 `EffectiveRole`을 쓰는 이유는 태그
직후 몇 프레임 동안 역할 SyncVar가 아직 `Runner`일 수 있고, 잠금은 **태그당한 순간**부터 세야
하기 때문이다(`TryGetCallerIdentity`가 쓰던 판정을 `EffectiveRole` 한 곳으로 모았다).

### `Reset()` 호출부가 없었다 — 이번에 연결했다

지시대로 확인한 결과, **`ServerKnockDriver.Reset()`을 부르는 코드가 어디에도 없었다.** 스프린트 27
당시에는 초기화할 라운드 상태가 없어 드러나지 않던 결함인데, 5회 카운터가 생기는 순간
**2라운드부터 노크가 아예 불가능해지는** 버그가 된다.

`RoundNetworkSync`가 `PulseNetworkSync`를 호출하게 만들지 않고, **`TickKnocks`가 페이즈 전이를
관측**하는 쪽을 골랐다 — 이 컴포넌트는 이미 매 틱 `RoundNetworkSync.ServerPhase`를 읽고 있어
(노크 페이즈 게이트) 새 결합이 생기지 않는다.

### 삭제·신설 목록

| 대상 | 내용 |
|---|---|
| 삭제 (메서드) | `EchoKnockController.HandleDesignationClick` · `TryResolveWorldPoint` · `SetMinimap` · `ApplyCursorRelease` · `EnsureCamera` |
| 삭제 (필드) | `_minimapKey` · `_mapCenter` · `_orthographicSize` · `_cameraHeight` · `_groundPlaneY` · `_minimapCamera` · `_minimapOpen`, 프로퍼티 `MinimapOpen` |
| 삭제 (씬) | `Lobby.unity`의 미니맵 직렬화 필드 5개 → `_knockKey: 20`(Key.F) + `_showHud` |
| 삭제 (시그니처) | `IPulseNetworkBridge.SubmitKnock(Vector3)` → `SubmitKnock()`, `ServerSubmitKnock(Vector3, NetworkConnection)` → `ServerSubmitKnock(NetworkConnection)` |
| 신규 필드 | `ServerKnockDriver._usesThisRound` · `_becameEchoAt`, `PulseNetworkSync._lastKnockPhase` |
| 신규 API | `ServerKnockDriver.ObserveEcho` · `UsesRemaining` · `LockoutRemaining`, `KnockRequestResult.NoUsesLeft` · `Locked`, `RoleNetworkSync.EffectiveRole`, `PulseNetworkSync.ObserveEchoes` |
| **삭제한 파일** | **없다** — `EchoKnockController`는 키 입력 계층으로 남는다 |

### 검증

- Core / Net / Presentation / Core.Tests / Assembly-CSharp / Marco.DebugTools: **오류 0 · Marco 코드 경고 0**.
- EditMode 총계 **557 → 573 전수 통과**(신규·갱신 케이스는 아래 표).
- **EditMode로 검증 불가능한 부분**: 라운드 전이에서 `Reset()`이 실제로 불리는지, 태그 순간부터
  20초가 세지는지, `Key.F` 입력이 실제로 도달하는지는 `MonoBehaviour` 수명주기 + FishNet 스폰이
  필요하다 — §29 실기 항목이다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **62** | 노크 지정 모드에서 커서가 잠겨 클릭 좌표가 고정됨 | ✅ **삭제된 UI와 함께 소멸** — 미니맵·지점 클릭이 §3.2-1로 폐지돼 커서 해제 경로 자체가 없어졌다 |
| **65** | §4.3이 노크 키를 **미확정**으로 둔다 | 🟡 **기록** — 임시로 `Key.F`. 키 리바인딩 작업에서 확정한다(§4.3 "미확정 표기된 키는 이 문서에서 확정하지 않는다") |
| **66** | §3.2가 1.5초 지연 **중** 이동 시 발생 위치를 규정하지 않는다 | 🟡 **판단하여 구현** — **요청 시점 위치로 고정**. 위 근거 참조 |

**GAP-61 재확인**: 이번에 건드린 코드에서 `LocalPlayerRegistry.Current`를 필드에 굳힌 곳은 없다.
`EchoKnockController`는 `Update`·`OnGUI`가 각각 `IsLocalEcho()`를 거쳐 **매 프레임 재조회**하며,
캐시하던 유일한 경로였던 `ApplyCursorRelease`는 이번에 삭제됐다.

### 남은 것 (이번 스코프 밖)

- `FirstPersonController.SetCursorReleased`/`_cursorReleased`가 **호출자를 잃었다**. 커서 정책
  자체는 유지하는 편이 안전해 이번에는 두었다 — 삭제 여부는 별도 판단이 필요하다.
- §8.3 유인 판정이 갱신본(2초 사전 창 · 0.5초 샘플링 · 접근누적 3.0초 · 감시 30초)과 다르다.
  현재 코드는 GAP-59 휴리스틱(9m 밖 → 25초 내 진입)이다. **갭 분석의 별도 단계**다.

### 상태

**코드 완료** — §29 통합 실기 재검증 대기.

---

## 기획서 정합 3단계 — 승리 판정 (B1·B2 + 이월 C1)

### 변경 전 / 후 조건식

```csharp
// 이전
if (valvesOpened >= totalValves && runnersEscaped >= 1)   return RunnersWin;
if (allRunnersTagged || timeRemaining <= 0f)              return SeekerWin;

// 이후 (§6.3 갱신본)
if (totalValves > 0 && valvesOpened >= totalValves
    && runnersEscaped >= EscapeWinThreshold /* 2 */)      return RunnersWin;
if (taggedRunners >= TagWinThreshold /* 2 */
    || timeRemaining <= 0f)                               return SeekerWin;
```

판정 **순서는 그대로 유지**했다 — §6.3 "동일 프레임 처리 우선순위: 탈출 → 태그 → 시간 종료".

### B1 — 왜 코드는 `escaped > (tagged + notEscaped)`가 아니라 "탈출 ≥ 2"인가

§6.3의 정식 판정식은 **미탈출**을 입력으로 쓰는데, 미탈출은 정의상 "시간 종료 시점에 살아
있으나 탈출하지 못함"이라 **라운드 도중에는 존재하지 않는 값**이다. 판정은 매 프레임 호출되므로,
도중에도 계산 가능한 등가식으로 옮겨야 "탈출 2명이 나온 순간 즉시 도망자 승리"가 성립한다.

도망자 3명 고정(§1)에서 세 상태의 합은 항상 3이므로
`escaped > 3 - escaped` ⟺ `2·escaped > 3` ⟺ `escaped ≥ 2`다.

### B2 — 태그 2명 즉시 종료

`allRunnersTagged`(불리언) → `taggedRunners`(정수)로 바꿨다.
§6.3: "**술래는 도망자 3명을 전부 태그할 필요가 없다. 2명이면 확정이다.**"

### ServerRoundDriver의 태그 카운트 — 세는 방식이 바뀌었다

| | 이전 | 이후 |
|---|---|---|
| API | `static bool AllRunnersTagged(IReadOnlyList<ITagTarget>)` | `static int TaggedCount(IReadOnlyList<ITagTarget>)` |
| 계산 | 태그 수 > 0 **&& 미태그 러너 수 == 0** | 태그 수만 센다 |
| 모집단 의존 | **있음** — 미태그 대상이 하나라도 등록돼 있으면 false | **없음** |

**GAP-19가 이 변경으로 소멸했다.** GAP-19의 본체는 "분모(전체 러너 수)를 어떻게 아는가"였고,
그 때문에 씬 대역 러너가 활성화돼 있으면 실제 플레이어를 다 태그해도 라운드가 끝나지 않았다
(스프린트 13에서 지적 → 스프린트 15에서 대역 비활성화로 우회). §6.3이 종료 조건을 **절대 인원**으로
확정하면서 분모가 판정에서 완전히 빠졌다 — 대역이 등록돼 있든 없든 결과가 같다.
`RoundOutcomeTracker`도 같은 이유로 `AreAllRunnersTagged(int totalRunners)`를 걷어냈다(GAP-13 소멸).

### 이월 C1 — `totalValves == 0` 방어

**없었고, 이번에 추가했다.** `totalValves > 0`을 도망자 승리 분기의 선행 조건으로 넣었다.

**요구사항 문구와 다른 방향으로 구현했다.** 지시는 "`totalValves == 0`일 때 즉시 RunnersWin을
반환하는 방어 코드"였는데, 3/3 검증 기록(이 로그 403행)이 **`totalValves == 0`이면 `0 >= 0`이 참이
되어 즉시 RunnersWin이 나오는 것 자체를 결함으로** 적고 있다. 밸브 목표가 구성되지 않은 씬을
"목표를 전부 달성했다"로 읽는 것은 §6.1과 정반대이므로, **미구성은 승리가 아니라 판정 불가**로
다뤘다. 반대 방향을 의도했다면 조건식 한 줄이므로 알려주면 뒤집는다.

### 3 Runner 전수검증 (§6.3 표 10행 — 테스트로 고정)

`ThreeRunnerExhaustive_MatchesDesignDocFormula`가 각 행마다 세 가지를 확인한다:
① §6.3 원식 `escaped > (tagged + notEscaped)`를 테스트 안에서 직접 계산해 표의 판정과 대조,
② 코드의 실제 결과, ③ "탈출 ≥ 2"와의 정확한 일치.

| 탈출 | 태그 | 미탈출 | §6.3 원식 | 코드 결과 | 탈출≥2 | 일치 |
|---:|---:|---:|---|---|---|---|
| 3 | 0 | 0 | 도망자 승 | RunnersWin | ✓ | ✅ |
| 2 | 1 | 0 | 도망자 승 | RunnersWin | ✓ | ✅ |
| 2 | 0 | 1 | 도망자 승 | RunnersWin | ✓ | ✅ |
| 1 | 2 | 0 | 술래 승 | SeekerWin | ✗ | ✅ |
| 1 | 1 | 1 | 술래 승 | SeekerWin | ✗ | ✅ |
| **1** | **0** | **2** | **술래 승** | **SeekerWin** | ✗ | ✅ ← ★ 이전 코드는 도망자 승이었다 |
| 0 | 3 | 0 | 술래 승 | SeekerWin | ✗ | ✅ |
| 0 | 2 | 1 | 술래 승 | SeekerWin | ✗ | ✅ |
| 0 | 1 | 2 | 술래 승 | SeekerWin | ✗ | ✅ |
| 0 | 0 | 3 | 술래 승 | SeekerWin | ✗ | ✅ |

**10/10 일치.** ★ 행은 `TimeoutWithOneEscapedAndNoneTagged_SeekerWins`로 한 번 더 단독 고정했다.

### 함께 바뀐 것

| 파일 | 변경 |
|---|---|
| `RoundNetworkSync` | `AllRunnersTagged` → `TaggedCount` 호출, 종료 로그를 `탈출 n/2, 태그 n/2`로 |
| `RoundCoordinator` | `_forceAllRunnersTagged` → `_forceSeekerTagWin`(씬 필드 포함), `_totalRunners`는 로그 전용으로 강등 |
| `HudFormatter` | "도망자가 전원 붙잡혔다" → "도망자 2명이 붙잡혔다", 러너 승리 문구도 임계값 반영 |
| `수동검증_절차.md` | §8-2·§9-4·§13-4·§14-6을 새 규칙으로. **2인 테스트에서는 태그로 라운드가 끝나지 않는다**는 경고 추가 |

### 실기에 미치는 영향 (반드시 인지할 것)

**2인 테스트 구성에서는 태그로 라운드가 끝나지 않는다.** 러너가 1명뿐이라 태그 최대치가 1이고
§6.3 임계값은 2다. 회귀가 아니라 **도망자 3명 전제(§1)를 벗어난 구성의 정상 귀결**이며,
태그 종료를 확인하려면 **최소 3인(술래 1 + 러너 2)** 이 필요하다.
같은 이유로 탈출 승리도 **2명이 나가야** 확인된다.

### 검증

- Core / Net / Presentation / Core.Tests / Assembly-CSharp / Marco.Editor: **오류 0 · Marco 코드 경고 0**.
- EditMode 총계 **573 → 596 전수 통과**.
- **EditMode로 검증 불가능한 부분**: `TagTargetRegistry`/`EscapeGateRegistry`의 실제 모집단 구성과
  라운드 종료 전파는 FishNet 스폰이 필요하다 — 위 실기 항목이다.

### 상태

**코드 완료** — 실기 재검증 대기(§8-2 · §9-4 · §13-4 · §14-6, 그리고 스프린트 27 §29).

---

## 기획서 정합 4단계 — 발소리 발생 주기(B6) · 재질 배율(C11)

§8 무성 생존상의 **입력값**을 문서와 맞추는 단계다. 어워드 판정식 자체(`PulseCount == 0`
이진 판정)는 손대지 않았다 — 그건 6단계다.

### B6. 시간 기준 → 이동거리 기준

§5.1-1: "발소리 파문은 **자신의 발생 반경과 같은 거리를 이동할 때마다** 1회 발생한다."

| | 이전(구 GAP-8) | 이후(§5.1-1) |
|---|---|---|
| 기준 | `_timeSinceLastPulse` ≥ 지속시간 | 등급별 누적 이동거리 ≥ 발생 반경 |
| 걷기 | 0.4초마다 | **2m마다** |
| 질주 | 0.8초마다 | **6m마다** |
| 첫 파문 | 이동 시작 즉시 | **임계값 도달 시** |
| 제자리 회전 | 계속 소리가 났다 | **소리 없음** |

**필드와 갱신 위치**: `LocomotionSimulator`에 `_walkDistance`·`_sprintDistance` 두 개.
`Tick`의 발소리 블록에서 `speed * deltaSeconds`를 해당 등급 누적에 더하고, 임계값을 넘으면
파문을 내고 **임계값만큼 뺀다**(0으로 밀지 않는다 — 그래야 프레임률에 따라 1m당 파문 수가
달라지지 않는다). Idle·Diving 진입 시 둘 다 0으로 리셋한다.

**걷기와 질주를 따로 세는 이유**: 등급이 바뀌었다고 진행 중이던 누적을 버리면
걷기↔질주를 번갈아 눌러 발소리를 지울 수 있다.

**알려진 한계**: 누적은 시뮬레이터가 낸 **의도 속도**를 적분한 값이라, 벽에 붙어 W를 누르고
있으면 실제로 나아가지 않아도 파문이 난다. 순수 Core를 유지하려는 선택의 결과이며
(`CharacterController`의 실제 변위를 되먹이려면 인터페이스가 Presentation에 묶인다),
방향은 **은신에 불리한 쪽**이라 악용되지 않는다.

**체감 변화**: 이동 시작 후 첫 0.4초가 **무음**이 됐다. §5.1-1의 "미세 이동으로 파문을
양산할 수 없다"가 그대로 적용된 결과다.

### C11. 재질 배율 — 어디서 판정하고 어디에 곱하는가

기존 구조를 그대로 따랐다. `PhysicsOcclusionProbe`가 **Physics 조회(Probe)** 와
**순수 규칙(Classify)** 을 나눠 갖는 그 구조를 복제했다.

| 계층 | 추가물 | 역할 |
|---|---|---|
| Core | `FootstepMaterial` enum · `FootstepMaterialRules` | §5.9 표(배율·물 예외·적용 대상)의 순수 규칙 |
| Core | `IFootstepMaterialProbe` | "이 좌표의 바닥은 무슨 재질인가" 계약 |
| Core | `PulseNetworkRegistry.FootstepMaterialProbe` + `SampleMaterial` | Net ↔ Presentation 지연 바인딩(차폐 프로브와 같은 자리) |
| Presentation | `PhysicsFootstepMaterialProbe` | 발밑으로 짧은 레이 → 콜라이더 **태그** 판독 |
| Net | `PulseNetworkSync.ServerSubmitPulse` | **서버가** 서버 측 위치에서 재질을 조회해 드라이버로 넘김 |
| Core | `ServerPulseDriver.AddPulse(..., FootstepMaterial)` | §5.1 기본 반경 × 배율. 물이면 등록 거부(-1) |

**왜 태그인가**: `PhysicsOcclusionProbe`가 이미 벽을 `Wall`/`HardBlocker` 태그로 구분한다.
새 컴포넌트를 바닥마다 붙이면 그레이박스 정리가 늘고, 레이어는 이미 차폐용으로 의미가 겹친다.
`ProjectSettings/TagManager.asset`에 7개 태그(`FloorConcrete`·`FloorWood`·`FloorMetalGrating`·
`FloorTile`·`FloorMat`·`FloorStage`·`FloorWater`)를 등록했다 —
**미등록 태그에 `CompareTag`를 호출하면 UnityException이 난다.**

**왜 서버가 판정하는가**: 클라이언트가 반경을 정할 수 없다는 GAP-24와 같은 원칙이다.
클라이언트가 "나는 카펫 위다"라고 주장하면 발소리를 마음대로 줄일 수 있다.
로컬 단독 실행 경로(`LocalPulsePipelineBehaviour`)도 **같은 Core 규칙**을 호출해,
두 경로가 다른 반경을 내지 않는다.

**§5.1-1 준수**: 배율은 오직 `ServerPulseDriver.AddPulse`의 **반경**에만 곱해진다.
`LocomotionSimulator`는 재질을 **입력으로 받지도 않는다** — 구조적으로 간격에 섞일 수 없다.

**§5.9 표에서 옮기지 않은 것**: 나무 마루의 "**상시 강제 발생(은신 불가)**".
§11 폐극장 맵 자체가 없어 재현 대상이 없고, "정지 중에도 소리가 난다"는 §5.1-1의 이동거리
규칙을 정면으로 뒤집는 예외라 별도 작업이 맞다. **배율 ×1.2만** 옮겼다.

**음성·밸브·노크에는 적용하지 않는다**(`FootstepMaterialRules.AppliesTo`). §5.9의 제목이
"재질별 **발소리** 배율"이고, 카펫 위에서 고함쳤다고 22m가 15.4m로 줄면 §5.1이 무너진다.

### 검증

- Core / Net / Presentation / Core.Tests / Assembly-CSharp / Marco.Editor: **오류 0 · Marco 코드 경고 0**.
- EditMode 총계 **596 → 637 전수 통과**.
- **EditMode로 검증 불가능한 부분**: 실제 레이캐스트가 바닥을 맞히는지, 태그가 씬에 붙어
  있는지, 서버 물리 씬에 맵이 로드돼 있는지는 §2·§2-1 실기 항목이다.

### 상태

**코드 완료** — 실기 재검증 대기(§2 · §2-1). **현재 그레이박스 맵에는 바닥 태그가 없어
모든 재질이 콘크리트(×1.0)로 판정된다** — 배율의 실기 확인은 태그 배선 후에 가능하다.

---

## 기획서 정합 5단계 — 숨 게이지(§5.9-1) · 술래 외침(§3.5) · 비명(§5.1)

셋이 서로의 입력이라 한 덩어리로 진행했다. 커밋도 나누지 않았다.

### 착수 전 확인 — 기존 기록과의 충돌 여부

| 확인 대상 | 결과 |
|---|---|
| §5.9-1 "4상태" vs 구현 | **충돌 아님**. §5.9-1 표 자신이 회복 대기를 *"위 3상태와 별개로 … 유지되는 **플래그**"* 로 정의한다. 잠수이면서 동시에 회복 대기일 수 있어 한 enum에 넣으면 표현 불가능한 조합이 생긴다 → **3상태 enum + 플래그 1개**로 구현 |
| `MovementState.Diving` 재사용 가능 여부 | **가능. 재사용했다.** 잠수 진입 판정(§4.3 "수면 위에서만, 홀드")은 이미 `LocomotionSimulator`가 소유한다. `BreathConfig.ZoneOf(MovementState, bool)`가 그 결과를 **읽기만** 한다 — 새 잠수 상태를 만들지 않았다 |
| 잠수 중 억제의 게이지 처리 | **§3.5 표에 이미 답이 있었다**: "잠수 중 / 이미 게이지 소모 중 / 자동 억제(물속이라 비명 못 지름)". 즉 **-3이 붙지 않는다** — 억제와 잠수 소모가 같은 프레임에 이중 차감되는 상황이 구조적으로 발생하지 않는다 |

### 신규 클래스 / 필드

| 계층 | 대상 | 내용 |
|---|---|---|
| Core | `BreathZone` (enum) | 잠수 / 수면 / 물 밖 — §5.9-1 3상태 |
| Core | `BreathConfig` | 8초 · -1/s · -3 · +2/s · +4/s · 대기 2초 · 페널티 3초/×0.8 · `ZoneOf` · `MaxConsecutiveSuppressions`(8÷3 유도) |
| Core | `BreathGauge` | `_current` · `_recoveryDelayRemaining` · `_chokePenaltyRemaining`, `Tick` · `TrySuppressScream` · `CanSubmerge` · `SpeedMultiplier` · `Reset` |
| Core | `SuppressionResult` · `BreathTick` | 억제 결과 3종 / 질식 1회성 신호 |
| Core | `ScreamConfig` | §5.1 9m · 1.0초 |
| Core | `SeekerShoutConfig` | **A** `ShoutPulseRadiusMeters`(고함 등급 참조) · **B** `FearRadiusMeters`(독립 상수) · 1초 · 45초 |
| Core | `ServerShoutDriver` | `_cooldownUntil` · `_pending` · `_suppressAttemptAt`, `TryRequest` · `CancelOnMove` · `NotifySuppressAttempt` · `Tick` · `ResolveFear` · `IsInFearRadius` |
| Core | `ShoutRequestResult` · `ScreamOutcome` · `ShoutTarget` · `ScreamReaction` · `ShoutActivation` | 판정 입출력 |
| Core | `SoundType.Scream` | §5.1 신설. `Shout`과 **별개 종류** |
| Core | `LocomotionInput.CanSubmerge` · `SpeedMultiplier` | 기본값 있는 선택 인자 — 기존 호출부 전부 그대로 컴파일된다 |
| Core | `IPulseNetworkBridge.SubmitShout` · `SubmitHoldBreath` | 둘 다 **인자 없음**(GAP-24) |
| Net | `PulseNetworkSync._shoutDriver` · `_breath` · `_shoutAnchor` · `_shoutTargets` · `_movedSeekers` | 서버 권위 상태 |
| Net | `ServerSubmitShout` · `ServerSubmitHoldBreath` (ServerRpc) | 페이로드 없음 |
| Net | `TickBreath` · `TickShouts` · `CancelMovedShouts` · `BuildShoutTargets` · `ZoneOf` · `BreathOf` | 서버 루프 |
| Presentation | `ShoutInputController` | `_shoutKey`(임시 R) · `_holdBreathKey`(Left Ctrl, §4.3 확정) |

### 상태 전이표 (§5.9-1)

| 현재 상태 | 입력 | 다음 상태 | 게이지 변화 | 회복 대기 |
|---|---|---|---|---|
| 물 밖 | Ctrl 홀드 + 수면 존 + 숨 > 0 | **잠수** | -1/초 | **매 틱 2초로 리셋** |
| 물 밖 | Ctrl 홀드 + 수면 존 + **숨 = 0** | 물 밖 (진입 거부) | 없음 | 유지 |
| 잠수 | Ctrl 해제 / 수면 존 이탈 | 수면·물 밖 | 소모 중단 | 부상 시점부터 2초 |
| 잠수 | 게이지 0 도달 | **강제 부상** | 0에서 고정 | 2초 |
| 수면 | 대기 > 0 | 수면 | 없음 | 소진 중 |
| 수면 | 대기 = 0 | 수면 | **+2/초** (8 상한) | — |
| 물 밖 | 대기 = 0 | 물 밖 | **+4/초** (8 상한) | — |
| 수면·물 밖 | 억제 입력 + 숨 ≥ 3 | 그대로 | **-3 즉시** | **2초로 리셋** |
| 수면·물 밖 | 억제 입력 + 숨 < 3 | 그대로 | **변화 없음** (음수 불가) | 유지 |
| 잠수 | 억제(입력 유무 무관) | 그대로 | **변화 없음**(자동 억제) | 잠수 규칙대로 |
| 라운드 시작 | `Reset()` | 물 밖 · 만충 | 8 | 해제 |

### 상태 전이표 (§3.5 외침)

| 현재 | 입력 | 다음 | 비고 |
|---|---|---|---|
| 대기 | 술래 아님 | 거부 `NotSeeker` | — |
| 대기 | 쿨다운 중 | 거부 `OnCooldown` | — |
| 대기 | 술래 + 쿨다운 없음 | **선딜레이(1초)** | 위치 고정점 기록 |
| 선딜레이 | 재요청 | 거부 `AlreadyWindingUp` | 연타로 창을 못 늘린다 |
| 선딜레이 | **이동(>0.15m)** | 대기 | **쿨다운 소모 없음** |
| 선딜레이 | 1초 경과 | **발동** | 쿨다운 45초가 **여기서** 시작 |
| 발동 | — | 대기 | A 파문(22m) + B 판정 |

### 경계 조건 계산 검증표

| # | 상황 | 계산 | 코드 결과 | 테스트 |
|---|---|---|---|---|
| 1 | 5초 잠수 직후 외침 | 8-5=3 ≥ 3 | **억제 가능**(잔여 0) | `DocTable_DiveThenShout(5, true)` |
| 2 | 6초 잠수 직후 외침 | 8-6=2 < 3 | **억제 불가 → 비명 강제** | `DocTable_DiveThenShout(6, false)` |
| 3 | 게이지 3 미만에서 억제 시도 | 2 - 3 = **-1 금지** | 게이지 **2 그대로**, 음수 없음 | `Suppress_BelowThree_FailsAndLeavesGaugeUntouched` |
| 4 | 게이지 0에서 억제 시도 | — | 0 유지, `NotEnoughBreath` | `Suppress_AtZero_FailsAndStaysAtZero` |
| 5 | 정확히 3 남았을 때 | 3-3 = 0 | **성공**, 0 도달 | `Suppress_ExactlyThreeRemaining_Succeeds` |
| 6 | 잠수 중 억제 + 같은 프레임 잠수 소모 | -3 **미적용**, -1×dt만 | `before - 0.02` | `Suppress_ThenSameFrameDiveTick_DoesNotDoubleSpend` |
| 7 | 잠수 중 잔여 2에서 억제 | 비용 0이라 3 미만 규칙 무관 | **자동 억제 성공** | `Suppress_WhileSubmergedBelowThree_StillSucceeds` |
| 8 | 억제 프레임에 회복이 붙는가 | 5 + 2×dt 금지 | **5 그대로** | `Suppress_OnSurface_ThenSameFrameTick_HasNoRecovery` |
| 9 | 연속 억제 한계 | 8 → 5 → 2, 3회차 불가 | 정확히 **2회** | `ConsecutiveSuppressions_MaxTwoOnFullGauge` |
| 10 | 45초 쿨다운이 한계를 만드는가 | 수면 회복 +2/s로도 45초면 만충 | **한계 미발동** | `ShoutCooldownAlone_NeverTriggersTheLimit` |
| 11 | 육상 억제 1회 총 복구 | 2 + 3÷4 = **2.75초** | 2.75 | `DocTable_LandSuppression_TakesTwoPointSevenFive` |
| 12 | 수면 억제 1회 총 복구 | 2 + 3÷2 = **3.5초** | 3.5 | `DocTable_SurfaceSuppression_TakesThreePointFive` |
| 13 | 7초 잠수 후 물 밖 | 2 + 7÷4 = **3.75초** | 3.75 | `DocTable_SevenSecondDive_TakesThreePointSevenFive` |
| 14 | 전체 고갈 후 물 밖 | 2 + 8÷4 = **4.0초** | 4.0 | `DocTable_FullDepletion_TakesFour` |
| 15 | 비명 청취 반경 | 9 × 1.2 = **10.8m** | 새 상수 0개 | `ScreamListeningRadius_IsDerivedFromExistingMultiplier` |
| 16 | 10.8~22m 구간 | 비명은 나지만 술래는 못 들음 | 표와 일치 | `DocTable_DistanceReaction(15, true, false)` |
| 17 | 공포 반경 경계 | 22m 포함 / 22.01m 제외 | 일치 | `FearRadius_BoundaryIsInclusive` |

### §3.5 "세 가지 22m"를 코드에서 분리한 방식

| # | 개념 | 코드 | 차폐 |
|---|---|---|---|
| **A** | 외침 소리 22m | `SeekerShoutConfig.ShoutPulseRadiusMeters` → `_driver.AddPulse(SoundType.Shout, …)` | **적용**(일반 파문 경로) |
| **B** | 공포 반경 22m | `SeekerShoutConfig.FearRadiusMeters` → `ServerShoutDriver.IsInFearRadius` | **미적용**(순수 직선거리) |
| **C** | 비명 청취 10.8m | 상수 없음 — 9m × `SoundPulseResolver.RoleRadiusMultiplier(Seeker)` | 적용 |

A는 §5.1 고함 등급을 **참조**해 함께 조정되게 했고, B는 **독립 상수**로 뒀다.
`FearRadiusAndShoutRadius_AreSeparateConstants`가 이 관계를 고정한다.

### 미해결로 남긴 것 (판단하지 않고 보고만 한다)

**§5.1과 §3.4가 `Scream`의 방향 게이지에 대해 어긋난다.**

- §5.1 표: 비명 방향 표시 **✓ (10.8m)**, 그리고 "대화·비명·노크는 … 방향 표시가 모두 동일 —
  술래는 셋을 구분할 수 없다. 이것이 메아리 심리전의 근간이다."
- §3.4: "**대화·고함 등급만** 트리거된다. 밸브 회전음, 발소리, **노크는 제외**."

스프린트 27이 노크에 대해 이미 **§3.4를 따르기로**(게이지 제외) 결정해 코드에 기록돼 있어,
같은 선례로 이번에도 `DirectionGaugeRules.TriggersGauge`를 **건드리지 않았다**(Scream 제외).
그 결과 술래는 게이지 유무로 대화와 비명을 구분할 수 있어 §5.1의 "구분 불가 그룹"이 깨진다 —
노크에 이미 있던 문제가 이제 두 종류로 늘었다. **결정이 필요한 사항이므로 기록만 남긴다.**

### 범위 밖으로 지킨 것

`AwardTally.RecordPulse`와 `RoundNetworkSync.ServerRecordPulse`를 **건드리지 않았다.**
그래서 비명 파문도 외침 파문도 §8 어워드에 집계되지 않는다 — 6단계 작업이다.
해당 위치에 주석으로 명시해 뒀다.

### 알려진 배선 한계 (기존 결함, 이번에 새로 생긴 것이 아니다)

**물 볼륨(수면 존) 감지가 아직 없다.** `FirstPersonController`가 예전부터
`isOnWaterSurface: false`를 하드코딩하고 있고(§5.9 후속 태스크), 서버도 관측 수단이 없어
`PulseNetworkSync.ZoneOf`가 현재 **항상 `OutOfWater`** 를 돌려준다. 결과:

- 잠수·질식·강제 부상·이동속도 -20%는 **실기에서 아직 도달 불가**다(규칙과 테스트는 완비).
- §5.9-1이 말한 "이 제약이 실제로 작동하는 것은 잠수와 겹칠 때뿐"이라는 긴장도 그 전까지는
  발현되지 않는다 — 외침만으로는 45초 쿨다운 덕에 항상 억제 가능하기 때문이다(계산 검증 #10).
- 물 볼륨이 들어오면 **`ZoneOf` 한 곳만** 고치면 된다. 규칙은 `BreathConfig.ZoneOf`가 전부 갖고 있다.

**숨 게이지는 서버에만 있다.** 소유자 클라이언트에 잔량을 보여주려면 SyncVar가 필요한데,
새 `NetworkBehaviour`/`SyncVar`는 사전 승인 대상이라 만들지 않았다. 현재 클라이언트 표시는
외침 쿨다운 근사값뿐이다.

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **67** | §4.3이 **외침 키를 미확정**으로 둔다 | 🟡 임시 `R`. 키 리바인딩에서 확정(GAP-64·65와 함께) |
| **68** | §5.1(비명 방향 표시 ✓)과 §3.4(대화·고함만 트리거)가 어긋난다 | 🔴 **미결 — 보고만.** 노크 선례를 따라 §3.4 유지 |
| **69** | §3.5 선딜레이 취소의 **이동 허용치**가 미명시 | 🟡 0.15m로 뒀다. NetworkTransform 미세 떨림으로 취소되면 능력을 쓸 수 없다 |

### 검증

- Core / Net / Presentation / Core.Tests / Assembly-CSharp / Marco.Editor: **오류 0 · Marco 코드 경고 0**.
- EditMode 총계 **637 → 706 전수 통과**.
- **EditMode로 검증 불가능한 부분**: ServerRpc 왕복, 선딜레이 중 실제 이동 감지,
  비명 파문의 청취자별 전달은 FishNet 스폰이 필요하다.

### 상태

**코드 완료** — 실기 재검증 대기. 물 볼륨이 없어 잠수 계열은 실기 확인 자체가 불가능하다.

---

## 기획서 정합 6단계 — GAP-68 문서 정정 + 어워드 3종 재정의(§8)

### ① GAP-68 — 문서가 오류였다(코드 변경 0)

§5.1 표의 **비명·노크 행 방향 표시**를 `✓ (10.8m)` → **`✗`** 로 정정했다.
근거는 §3.4가 방향 게이지를 **"말"에 대한 술래 전용 정보 우위**로 한정하고
"밸브 회전음, 발소리, 노크는 트리거 대상에서 제외"를 명시한다는 것이다.
비명 역시 말이 아니라 강제 발생 SFX이므로 같은 이유로 제외 대상이며,
`DirectionGaugeRules.TriggersGauge`(대화·고함만)는 **처음부터 옳았다**.

함께 정정한 것:

| 위치 | 이전 | 이후 |
|---|---|---|
| §5.1 "구분 불가 그룹" | **대화**·비명·노크 셋 | **비명 · 노크** 둘 |
| 부록 [방향 표시 반경] | "대화·비명·노크 10.8m" | "대화 10.8m" + 트리거 대상은 "말"뿐 |

**심리전의 근간은 유지된다** — 반경(9m)·청취(10.8m)·게이지 미표시가 모두 같은
비명과 노크를 술래가 구분할 수 없다는 것이 §3.5 정보 비대칭의 핵심이고,
대화와의 혼동은 애초에 §3.4가 잡아내려는 대상이다.

### ② 어워드 3종 — 변경 전/후 판정식

| 어워드 | 이전 | 이후(§8 갱신본) |
|---|---|---|
| **최다 비명상** | `SoundType.Shout` 횟수 최대 | **`SoundType.Scream` 횟수 최대.** Shout 일절 미반영 |
| **무성 생존상** | `PulseCount == 0 && Distance > 0` 중 거리 최대 (**이진**) | **Σ(반경 × 지속) ÷ 이동거리 최솟값** (연속값). 밸브 제외 · 재질 배율 적용 후 반경 · 이동 1m 미만 제외 · 태그 아웃 제외 |
| **최고의 거짓말상** | 9m 밖 → 25초 내 9m 진입 (**2조건**) | **§8.3 4조건** — ① 9m 밖 ② 직전 2초 비접근 ③ 0.5초 샘플 접근누적 ④ 진입 시 누적 ≥ 3.0초. 감시 30초 |

**§8.1 억제 성공은 자동으로 빠진다**: 억제에 성공하면 `Scream` 파문이 애초에 생성되지 않아
`RecordPulse`가 호출되지 않는다. **청취 여부와도 무관**하다 — 집계는 파문 *발생* 시점에
일어나고 집계 API에 청취자 인자가 아예 없다(10.8m 밖이라 술래가 못 들어도 센다).

**§8.2 "재질 배율 적용 후" 보장**: `ServerPulseDriver.TryGetAppliedSpec`을 새로 만들어
`AddPulse`와 어워드 집계가 **같은 계산 결과**를 쓰게 했다. 두 곳이 각자 계산하면
§5.9 재질 효과가 소음량에서 조용히 사라진다.

### ③ §8.3 4조건 — 계산 검증

| # | 조건 | 경계 | 코드 | 테스트 |
|---|---|---|---|---|
| ① | d(t0) > 9m | d = 9.00m → **감시 안 함** | `distance <= LureRadiusMeters` → Armed=false | `Condition1_ExactlyAtRadius_IsNotStarted` |
| ① | 〃 | d = 9.01m → **감시 시작** | — | `Condition1_JustOutsideRadius_StartsWatch` |
| ② | d(t0) ≥ d(t0−2초) | 30→20m 접근 중, d(t0−2)=22 → 20 ≥ 22 **거짓** → 감시 안 함 | 이력에서 조회 | `Condition2_AlreadyApproaching_WatchIsNotStarted` |
| ② | 〃 | 20→30m 이탈 중 → 30 ≥ 24 **참** | — | `Condition2_MovingAway_StartsWatch` |
| ② | 〃 | 정지(25m 유지) → 25 ≥ 25 **참**(등호) | — | `Condition2_StandingStill_StartsWatch` |
| ② | 이력 없음 | 폴백 = 현재 거리 → 등호 성립 → **통과** | 확인 불가로 정당한 노크를 탈락시키지 않는다 | `Condition2_NoHistory_FailsOpen` |
| ③ | 거리 감소 샘플만 누적 | 20m 정지 10샘플 → 누적 **0** | `distance < LastSampleDistance` | `Condition3_OnlyDecreasingSamplesAccumulate` |
| ④ | 누적 ≥ 3.0초 | 8샘플(4.0초) 접근 → **성공** | — | `Condition4_SustainedApproach_IsCounted` |
| ④ | 〃 | 4샘플(2.0초) 접근 → **실패** | §8.3 표 1 "우연히 지나감" | `Condition4_PassingBy_IsNotCounted` |
| ④ | 〃 | 6샘플(정확히 3.0초) → **성공**(≥) | 경계 포함 | `Condition4_ExactlyThreeSeconds_IsCounted` |
| 표3 | 반대로 갔다 복귀 | 이탈 4샘플 후 복귀 6샘플 → **성공** | 반경 안 진입해도 감시를 끝내지 않는다 | `Table3_AwayThenReturn_IsCounted` |
| 종료 | 30초 경과 | t0+30 → 만료 | `now >= ExpiresAt` | `Watch_ExpiresAfterThirtySeconds` |
| 종료 | 술래 태그 | 전 감시 폐기 | `NotifySeekerTagged` | `Watch_EndsWhenSeekerTags` |
| 종료 | 라운드 종료 | `Reset()` | — | `Watch_EndsOnRoundReset` |

### ④ 감시 30초 vs 쿨다운 25초 — 겹침 처리 판단

**겹친다.** 노크 A가 t0에 발동하면 감시는 t0+30까지 살아 있고, 다음 노크는 요청 기준 25초 뒤,
즉 발동은 **t0+25**다. → **최대 5초간 같은 메아리의 감시 2개가 공존한다.**

§8.3 표 4·5가 이 경우를 명시적으로 다룬다 —
"짧은 시간에 여러 Knock → **1회만**. 진입 시 가장 최근 성공 건 1개만 집계",
"같은 위치에서 반복 Knock → **동일 위치 활성 감시는 최신 1개로 갱신**".

**그래서 `Tick`이 새 감시를 만들 때 같은 플레이어의 이전 감시를 버린다**(`DropWatchesOf`).
그렇게 하지 않으면 술래가 **한 번 접근하는 동안 두 감시가 각각 성공 판정**을 내려
유인 1회가 2회로 세어진다. 다른 메아리의 감시는 건드리지 않는다
(`OtherEchoWatch_IsNotDropped`).

### 신규/변경 API

| 대상 | 내용 |
|---|---|
| `AwardTally` | `Entry.ScreamCount`·`NoiseSum`·`WasTaggedOut` (기존 `ShoutCount`·`PulseCount` 제거), `RecordPulse(int, SoundType, float, float)`, `MarkTaggedOut`, `SilenceScore`, `NoiseOf`, `CountsTowardNoise`, `MinimumDistanceMeters`, `BestBy(..., bool highestWins)` |
| `ServerPulseDriver` | `TryGetAppliedSpec(SoundType, FootstepMaterial, out float, out float)` — 등록 반경의 단일 진입점 |
| `ServerKnockDriver` | `LureWindowSeconds` 25 → **30**, `PreApproachWindowSeconds`·`ApproachSampleSeconds`·`ApproachRequiredSeconds` 신설, `_seekerHistory`, `NotifySeekerTagged`, `DropWatchesOf`, `EvaluateStartConditions`, `RecordSeekerHistory`, `DistanceAt` |
| `RoundNetworkSync` | `ServerRecordPulse(int, SoundType, float, float)`, `ServerMarkTaggedOut` |
| `PulseNetworkSync` | 비명·외침·질식 파문의 §8 집계 개통, `TagTargetRegistry.TargetTagged` 서버 구독 → `NotifySeekerTagged` |

### 범위대로 지킨 것

- 물 볼륨 미구현으로 인한 잠수 계열 실기 불가는 **손대지 않았다**(맵 재작업 때).
- §3.4 게이지 배율 자체는 이미 끝났으므로 **표 텍스트만** 고쳤다.

### 보고: 문서에 있으나 구현하지 않은 것 1건

**§8.1 "동점 — 동점자 전원 공동 수상".** 결과 전파가 수상자 id **하나**(SyncVar 3개)로 되어
있어 공동 수상을 표현할 수단이 없다. 자료구조·전파·HUD를 함께 바꿔야 하는 별도 작업이라,
기존대로 **동점 시 id가 작은 쪽** 단일 수상자를 유지했다(GAP-38 잔존).

### GAP 기록

| # | 내용 | 처리 |
|---|---|---|
| **68** | §5.1(비명·노크 방향 표시 ✓)과 §3.4(대화·고함만)가 어긋난다 | ✅ **해소 — 문서가 오류였다, 코드 변경 없음** |
| **59** | 노크 "성공 유인" 판정 기준 미정의 | ✅ **해소** — §8.3이 4조건을 정의했고 코드가 교체됐다 |
| **40** | 무성 생존상 최소 이동거리 미명시 | ✅ **해소** — §8.2 "1m 미만 제외" |
| **37** | 최다 비명상·최고의 거짓말상에 선행 기능이 없어 수상 불가 | ✅ **해소** — §3.5 외침·§3.2 노크가 모두 들어왔다 |
| **56** | 무성 생존상의 메아리 제외 여부 미명시 | ✅ **해소** — §8.2 "태그당한 메아리는 제외"가 명문화돼 `MarkTaggedOut`으로 구현 |
| **38** | 동점 처리 | 🟡 **잔존** — §8은 공동 수상을 말하지만 전파 구조가 단일 id다 |

### 검증

- Core / Net / Presentation / Core.Tests / Assembly-CSharp / Marco.Editor: **오류 0 · Marco 코드 경고 0**.
- EditMode 총계 **706 → 732 전수 통과**.
- **EditMode로 검증 불가능한 부분**: 서버가 실제로 태그 이벤트를 받아 감시를 끊는지,
  0.5초 샘플링이 네트워크 위치 갱신 주기와 맞물리는지는 FishNet 스폰이 필요하다.

### 상태

**코드 완료** — 실기 재검증 대기(§23 · §29-3 · §30).

---

## 문서 복구 — 기획서 v0.2 덮어쓰기 사고 · v0.4 패치 · §10 좌표 유실분 복구

코드 변경 없음. 기획서(`docs/마르코_상세기획서.md`) 작업 기록이다.

### 경위

1. 작업 트리의 기획서가 **v0.2 사본으로 덮어써져 있었다**(2061줄 → 1022줄, `변경이력_v0.3.md` 삭제).
   덮어쓴 파일과 `01-기획서_패치지시_v0.4.md`의 수정 시각이 9/18 19:02로 같아, 외부 사본으로
   docs/를 통째 교체한 것으로 추정한다. 현 상태를 `stash@{0}`에 보관하고 HEAD(v0.3)로 복구했다.
2. `01-기획서_패치지시_v0.4.md`를 F 순서대로 반영해 v0.4를 만들고 **단독 커밋**했다(`849a50f`).
3. 9/12에 작성됐으나 **커밋되지 않아 유실된** §10.1 좌표표·§10.2 경로 거리표·§10.2-1 2조건 검증을
   v0.4 수치 위에 병합했다(`02-좌표_유실분_복구_지시서.md` + `02a` 정정 패치).

### 좌표 복구에서 바로잡은 것

| 항목 | 지시서 원안 | 반영 | 근거 |
|---|---|---|---|
| A↔C 경로 | 66.5m | **64.1m** | `valve_layout_check.py` 실행값. 66.5는 계단 14.4m 기준이라 같은 표의 다른 C 쌍(12m 기준)과 자기모순 |
| 칸막이(x=40, y=13~20) | 검증 통과에 **필수** | **권장** | 제거해도 불합격 0쌍, A↔B 경로 32.7 → 31.9m(30m 이상 유지) |
| 차폐 임계 검산식 | `14.4 × 2 × 0.5^n` | **`14.4 × (1 + 0.5^n)`** | 표의 28.8/21.6/18.0/16.2는 원래 맞았고 검산식만 틀렸다 |
| C↔D | "합격 사례" | **최대 위험 쌍** | 차폐 4장 상당, 임계 15.30m vs 직선 15.62m — 여유 0.32m |
| 왕복 부담 근거 | 정상 인원만 | 태그 후 인원 감소 추가 | 최장 편도 64.1m = 10.9초 + 회전 8초 = 18.9초 ≪ 역류 180초 |

### GAP 기록

| # | 내용 | 위치 | 처리 |
|---|---|---|---|
| **70** | 배수구 2개소(6.5-2)의 정확한 좌표 미정 | 기획서 §10.1 | ✅ **해소** — (31,21) 메인풀 / (8,9.5) 유아풀 확정. 아래 절 참조 |

---

## 문서 — §6.5 배수구 수심 모순 해소 및 좌표 확정

코드 변경 없음. 기획서 + `docs/tools/valve_layout_check.py` 작업이다.

### 발견한 모순

§6.5-3은 최후 생존자 페이즈의 공정성 전체를 *"수심 3.5m, 태그 반경 1.2m이므로 술래는 수면에서
배수구에 닿을 수 없다"* 한 문장에 걸고 있었다. 그런데 §6.5-2는 배수구를 2개소에 두고 그 하나가
**유아풀**이며, 유아풀은 문서 전체에서 일관되게 얕다(§10.1 "얕은 수중", §6.1 밸브 E 진입·부상 0.5초).
§10.5 표는 아예 **배수구 2를 "수중(얕음)"** 이라고 적고 있었다.

**결과**: 서버가 배수구 2를 고르면(확률 50%) 술래는 걸어 들어가 서 있기만 해도 된다.
도망자 1명에게 남은 유일한 승리 경로가 라운드의 절반에서 사라진다 — 엔드게임의 절반을
프로토타입에서 테스트할 수 없는 상태였다.

**해소**: 유아풀 배수구 지점만 **국소 침강부(sump) 3.5m**로 판다. 레벨 지오메트리 1개소의
수심 값이므로 규칙·상태기계·`SoundType`이 늘지 않는다(§3.3 준수).

### 수평 거리 제약 신설 — §6.5-3 논거의 빠진 절반

수심만 막으면 술래가 **마른 바닥에 선 채로** 부상 지점을 태그한다. 깊이로 막은 것을 거리로
뚫는 셈이다. 그래서 **배수구 ↔ 육상 최단 거리 3.0m 이상**(태그 반경 1.2m + 물가에서 한 걸음
내디딘 여유 1.8m)을 §6.5-3에 명시했다.

**배수구에는 §10.2-1 조건②(동시 감시)를 적용하지 않는다.** 최후 생존자 페이즈에서 밸브는
얼어붙은 상태이고(`T`는 진입 시점 값으로 고정, 추가 개방은 손해), 페이즈 중 밸브 회전음이
발생하지 않으므로 동시 감시할 대상이 없다.

### 확정 좌표와 검증

| 배수구 | 좌표 | 육상 거리 | 밸브 거리 | 수심 |
|---|---|---|---|---|
| 1 (메인 풀) | **(31, 21)** | 4.00m | B까지 3.16m | 3.5m |
| 2 (유아풀) | **(8, 9.5)** | 3.00m | E까지 3.04m | **국소 침강부 3.5m** |

**두 좌표 모두 해당 수면의 기하학적 상한이다** — 사각형 수면에서 육상 최단 거리의 최대값은
`min(폭, 높이) ÷ 2`이고, 메인풀 `min(14,8)/2 = 4.00`, 유아풀 `min(9,6)/2 = 3.00`으로 정확히 일치한다.
즉 **두 배수구 다 수면 중심점**이며, 수면을 줄이면 요구치 3.0m가 즉시 깨진다.

여유가 가장 빠듯한 것은 **배수구 2 ↔ 밸브 E = 3.04m**(요구 3.0m, 여유 0.04m)다.

### GAP 기록

| # | 내용 | 위치 | 처리 |
|---|---|---|---|
| **70** | 배수구 좌표 미정 | 기획서 §10.1 · §6.5-2 | ✅ **해소** — (31,21) / (8,9.5) 확정 |
| **72** | 수면(물) 영역이 프로토타입 잠정값 | 기획서 §10.1 | 🟡 메인풀 (21,17)~(35,25) / 유아풀 (3.5,6.5)~(12.5,12.5). 구역 크기에서 유도한 인셋이며 플레이테스트로 확정. **변경 시 배수구 좌표를 `valve_layout_check.py`로 재산출할 것** |

> GAP-71은 비어 있다(사용 이력 없음). 번호를 메우지 않고 그대로 둔다.

---

## 프로토타입 블록 1 — 선행 결함 4건 + 맵 v2 생성 + 배치 검증 포팅

`docs/프로토타입_구현_프롬프트.md`(v0.4판) 블록 1. 코드 + 에디터 툴 작업이다.

### 지시와 기획서의 충돌 1건 — 기획서를 따랐다

블록 1-A④가 *"MaxConsecutiveSuppressions 가 (…) 8/3 = 2 였던 값이 12/3 = 4 가 된다"* 고 적었다.
그런데 기획서 §5.9-1 [v0.4]는 억제 비용을 **3 → 4.5초로 함께** 올렸고(§3.5 L326·L328,
§5.9-1 L725, 부록 A L2283) 그 이유를 명시했다 —

> 게이지만 12초로 늘리면 억제 가능 횟수가 2.67회에서 **4회로 늘어 3.5절 외침의 위력이 약해진다.**
> **12 ÷ 4.5 = 2.67회**로 비율을 고정해 3.5절 밸런스를 그대로 보존한다.

즉 지시서가 계산한 "4회"는 **기획서가 명시적으로 막은 결과**다. 공통규칙 1(기획서 정본)에 따라
**12초 / 4.5초 → 2회**로 구현하고, 그 값이 4가 되지 않는지를 테스트로 고정했다
(`MaxConsecutiveSuppressions_IsDerivedNotHardcoded`).

### 1-A① 차폐 레이캐스트 절단 (C6)

`PhysicsOcclusionProbe.MaxHits` 32 → **128**. `RaycastNonAlloc`은 버퍼가 차면 **에러 없이**
잘라서 돌려주므로 "히트 수 == 상한"이 우리가 가진 유일한 신호다. 그 조건에서 경고를 **1회만**
남긴다(차폐 재판정은 초당 수십 회 도는 경로라 매번 찍으면 프레임이 떨어진다).
`NonAlloc` 정적 버퍼 재사용은 유지 — GC 할당 증가 0.

### 1-A② 층간 바닥 ×0.25 · 수면 ×0.5 (§5.6)

| 대상 | 레이어 | 태그 | 차폐 가중치 | 근거 |
|---|---|---|---|---|
| 일반 벽·문·락커 | SoundBlocking | `Wall` | 1 (×0.5) | §5.6 표 그대로 |
| **수면 차폐판** | SoundBlocking | `Wall` | 1 (×0.5) | §5.6 표가 태그를 `Wall`로 지정 — **코드 분기 0** |
| **층간 바닥** | SoundBlocking | `FloorSlab` **[신설]** | **2** (×0.25) | 아래 |
| 계단 | — | 없음 | 0 | §5.6 "계단은 개구부라 미적용" |
| 바닥 재질 | Default | `Floor*` | — | 하향 레이 전용 |

**왜 `FloorSlab` 태그를 새로 만들었나.** Unity 태그는 콜라이더당 하나뿐이라 "Wall 2장 상당"을
태그로 표현할 수 없다. 대안인 "콜라이더 2장 겹치기"는 **한 장이 지워졌을 때 조용히 ×0.5로
바뀌는** 무경고 실패를 만든다. §10.2-1 C↔D는 여유가 0.32m뿐이라 벽 1장 차이로 즉시 불합격하는데,
그것이 런타임에 경고 없이 일어나는 것이 이 프로젝트에서 가장 비싼 실패 유형이다.
`OcclusionAccumulator`를 "개수"에서 **"가중치 합"**으로 바꿨다.

**부수 수정**: `PhysicsFootstepMaterialProbe`가 `~0`(전 레이어)으로 하향 레이를 쏘고 있었다.
2층 바닥은 콜라이더가 두 장(밟는 면 Default + 차폐면 SoundBlocking)이고 같은 높이에 겹치므로,
어느 쪽이 먼저 잡히는지 정해지지 않아 **2층 카펫 ×0.7이 조용히 콘크리트 ×1.0으로 떨어질 수
있었다.** SoundBlocking을 마스크에서 제외했다 — §5.6도 같은 것을 요구한다.

**테스트로 고정한 §6.5-3 전제**: 수중 밸브 강제 파문 12m → 술래 배율 1.2 → 14.4m →
수면 1장 ×0.5 → **7.2m**. 경계(7.2 통과 / 7.21 탈락)와, 수면 차폐가 빠지면 14.4m가 되어
§6.5-3이 무너진다는 것까지 함께 고정했다(`OcclusionTagWeightTests`).
층간은 14.4 × 0.25 = **3.6m**.

### GAP-71 — 거리 판정의 수직 성분: **3D로 확정**

`Core/Spatial/DistanceMetric.cs` 한 파일이 이 결정의 유일한 소유자이며, 인지 경로 6곳
(`SoundPulseResolver` · `ActivePulseTracker` · `PulseNetworkSync` · `ServerKnockDriver` ×2 ·
`ServerShoutDriver`)이 전부 이것을 경유한다. 근거 셋:

1. **차폐 레이캐스트가 3D다.** §5.6은 1차 컷을 통과한 뒤 같은 두 점 사이로 레이를 쏜다.
   컷을 XZ로 재고 레이를 3D로 쏘면 두 판정이 서로 다른 선을 본다.
2. **수직 감쇠는 층간 바닥이 담당한다.** Y를 빼면 2층 소리가 수평 거리로만 판정되어 ×0.25와
   어긋나고, §10.2-1이 벽 수 모델로 계산해 둔 여유(C↔D 0.32m)가 전부 무효가 된다.
3. **태그 판정이 3D여야 §6.5-3이 성립한다.** "수심 3.5m > 태그 반경 1.2m" 논거 전체가
   수직 성분에 걸려 있다.

`TagRules`는 **의도적으로 위임하지 않는다** — 소리 척도가 언젠가 2D로 바뀌어도 태그는 3D여야
하므로, 공유하지 않는 것 자체가 규칙이다. 그 이유를 코드 주석에 남겼다.

### 1-A③ 물 볼륨 감지 — `isOnWaterSurface: false` 하드코딩 제거

`Core/Water/WaterVolumeRegistry.cs`(+ `IWaterVolume`) · `Presentation/Water/WaterVolumeBehaviour.cs`.

**트리거가 아니라 좌표 상자다.** `OnTriggerEnter`로 세지 않은 이유는 ① 서버가 임의 시점에
임의 플레이어의 숨 상태를 다시 계산해야 하는데 트리거 이벤트는 그 질문에 답하지 못하고,
② 진입/이탈은 놓칠 수 있고(텔레포트·리스폰·§6.5 페이즈 전환) 한 번 놓치면 그 플레이어의 숨
상태가 라운드 끝까지 틀린 채 남는다. **매번 좌표로 묻는 쪽이 상태를 들고 있지 않아 어긋날 수 없다.**

수심은 **지점별**로 답한다 — §6.5-2가 유아풀 배수구만 국소 침강부 3.5m로 파도록 정했으므로
"수면 하나에 수심 하나"로는 표현할 수 없다.

**잠수는 발이 아니라 머리를 내린다**(`Core/Locomotion/DiveRules.cs`). §5.9-1이 잠수를
*"카메라(머리)가 수면 아래"* 로 정의하고, 발을 내리면 부력·수중 이동·풀 바닥 콜라이더가 전부
딸려 오면서 **얕은 유아풀에서는 바닥에 막혀 잠수가 조용히 실패**한다.

**서버 권위 배분**(GAP-24의 정확한 형태):

| 입력 | 누가 아는가 | 처리 |
|---|---|---|
| 잠수 키 홀드 | 클라이언트만 | `SubmitDiveIntent(bool)` — **상태가 바뀔 때만** 전송 |
| 물속 여부 | 서버 | 동기화된 위치를 §10.1 수면 지오메트리에 대고 재계산 |
| 잠수 가능 | 서버 | 서버가 소유한 숨 게이지(§5.9-1 강제 부상) |

판정식은 `DiveRules.IsDiving` **하나**이고 `LocomotionSimulator`와 서버가 같이 부른다.
따라서 클라이언트는 마른 땅에서 잠수할 수도, 게이지 0으로 잠수를 유지할 수도 없다.

`PulseNetworkSync.ZoneOf`가 `return BreathZone.OutOfWater;` 한 줄에서 지오메트리 재계산으로
바뀌었고, `_diveIntent`는 `OnStartServer`와 **라운드 전이(→ InGame)** 양쪽에서 비운다
(더블체크 2 — 지난 라운드의 홀드 주장이 살아남으면 안 된다).

**계산 오류 1건을 테스트가 잡았다.** `MinDivableDepth`를 `Standing − Submerged = 1.12m`로
유도했는데 틀렸다. 바닥에 선 잠수자의 머리는 `수면 − 수심 + 0.5`이므로 필요 수심은
**0.5m 초과**다(= `SubmergedHeadHeight`). 1.12m는 머리가 내려가는 <b>거리</b>이지 필요 수심이
아니다. 반대쪽 경계도 같이 드러났다 — 수심 ≥ 1.62m면 서 있기만 해도 잠기므로,
"서서 잠수로만 잠기는" 구간은 **0.5m < 수심 < 1.62m**이고 유아풀이 거기 있어야 한다.

### 1-A④ 숨 게이지 12초 / 억제 4.5초

| 항목 | v0.3 | v0.4 |
|---|---|---|
| 총량 | 8초 | **12초** |
| 비명 억제 | -3 | **-4.5** |
| 연속 억제 최대 | 2회 (8÷3) | **2회 (12÷4.5 = 2.67)** — 비율 고정 |
| 육상 억제 1회 총 | 2.75초 | **3.125초** |
| 수면 억제 1회 총 | 3.5초 | **4.25초** |
| 수중 밸브(8초) 후 | — | **잔여 4.0 → 억제 불가**(비용 4.5) |
| 전체 고갈 후 | 4.0초 | **5.0초** |

§5.9-1 [v0.4]가 지목한 경계를 테스트로 고정했다 — **7.5초 잠수(잔여 4.5) → 억제 가능,
8초 잠수(수중 밸브 1회, 잔여 4.0) → 억제 불가.** 0.02초 누적 틱으로는 부동소수 잔차가
4.49999를 만들어 경계가 반대로 넘어가므로 단일 틱으로 정확히 소모한다.

§6.5-2 배수구 "2회 잠수 필수" 부등식도 고정했다 — `T = 14 − (개방 × 3)`, 총 점유 `1 + T + 1`:
0개 16초 / 1개 13초 > 게이지 12초 → **2회**, 2개 10초 ≤ 12초 → 1회. 배수구 상수의 소유자는
블록 4이므로 지금은 프로덕션 상수를 만들지 않고 테스트 안의 §6.5-2 표 값만 썼다(더블체크 8).

### 1-B 맵 v2 자동 생성

`Editor/MapV2Layout.cs`(좌표 전부) + `Editor/MapV2GeneratorTool.cs`(생성).
데이터를 생성기에서 분리한 이유는 **같은 좌표를 생성기와 검증기가 둘 다 알아야** 하기 때문이다 —
각자 들고 있으면 한쪽만 고쳤을 때 "검증은 통과하는데 씬은 틀린" 상태가 된다.

좌표는 §10.1 구역 13개 · 수면 2개 · §10.2 밸브 5개 · §6.5-2 배수구 2개 · 문 18개 ·
출구 2개 · 칸막이 x=40 전부 기획서 원문 그대로이며 `valve_layout_check.py`와 같은 숫자다.
태그·레이어는 **생성하는 그 자리에서** 붙인다(나중에 일괄로 붙이면 반드시 빠지고, 빠진 벽은
`WeightOf`가 0으로 세어 에러도 경고도 없이 무력화된다). 태그·레이어가 없으면 **생성 자체를
중단**한다 — 레이어가 없으면 차폐가 항상 "벽 0개"가 되어 맵은 멀쩡해 보이는데 §5.6이 죽는다.

**풀 측벽을 빼먹었다 — 배치 검증이 잡았다.** 수면 차폐판만 깔면 수중 밸브의 소리가 수면을
지나지 않고 **옆으로** 빠져나온다(풀이 "흙을 파낸 공백"이 아니라 얇은 판 두 장 사이의 열린
틈이 되기 때문). 실제로 A↔B 28지점·B↔D 2지점의 동시 감시가 생겼고, 측벽을 넣자 둘 다 0이 됐다.
**이 측벽이 "수중 밸브 12m → 술래 청취 7.2m"(§5.6 [v0.4])를 성립시키는 물리적 조건이다.**

**2층 문 2개는 조건 ②가 골랐다.** §10.1은 상부 관람층 두 구역의 좌표만 주고 개구부를 적지
않았는데, 문이 없으면 두 방이 사방이 막힌 상자가 되어 밸브 C에 걸어서 갈 수 없다. 그런데
2층 문은 밸브 C의 회전음이 층간 ×0.25를 **우회하는 구멍**이라 위치에 따라 §10.2-1 ②가 깨진다.
후보 전수 판정:

| 물탱크실 문 위치 | 동시 감시 결과 |
|---|---|
| 동벽 (10,26) | **C↔D 9지점 — 불합격** (문을 지나 차폐판 바깥 x>10의 빈 공간으로 내려온다) |
| 남벽 (7,24) | **C↔E 24지점 — 불합격** |
| 북벽 (7,29) · 서벽 (4,26) · **동벽 (10,28)** | 0지점 — 합격 |

합격 후보 중 서 계단 (12,26)에 가장 가까운 **동벽 (10,28)** 을 택했다.

### 1-C 배치 검증 C# 포팅

`Editor/MapV2ValidationTool.cs`. 파이썬 판은 *도면*을 검사하지만(벽을 1m 셀로 근사, 수면 차폐를
모름, 층간을 상수 2로 가정) 이 도구는 **씬에 실제로 놓인 콜라이더**를 `PhysicsOcclusionProbe`로
직접 읽는다. 목적은 "도면은 맞는데 씬이 틀린" 경우를 잡는 것이다.

**검증 결과 — 밸브 10쌍 + 배수구 2개 + 잠수 수심, 전부 합격**
(에디터 없이 돌리기 위해 C# 로직을 파이썬으로 1:1 재현해 산출했다. 0.5m 격자, 두께 0.2m
콜라이더, 몸통 반경 0.35m.)

| 쌍 | 경로(실측) | 도면(§10.2) | 차이 | 동시 감시 | 판정 |
|---|---|---|---|---|---|
| A↔B | 35.5m | 32.7m | +2.8 | 0 | OK |
| A↔C | 63.1m | 64.1m | −1.0 | 0 | OK |
| A↔D | 62.6m | 60.7m | +1.9 | 0 | OK |
| A↔E | 45.6m | 45.3m | +0.3 | 0 | OK |
| B↔C | 42.2m | 41.5m | +0.7 | 0 | OK |
| B↔D | **31.0m** | 31.1m | −0.1 | 0 | OK ← 30m 하한 여유 1.0m, 최소 |
| B↔E | 35.3m | 35.0m | +0.3 | 0 | OK |
| C↔D | 31.1m | 30.5m | +0.6 | 0 | OK |
| C↔E | 30.2m | 30.7m | −0.5 | 0 | OK |
| D↔E | 33.7m | 33.4m | +0.3 | 0 | OK |

| 배수구 | 육상 거리 | 밸브 거리 | 수심 | 판정 |
|---|---|---|---|---|
| 1 (메인 풀 (31,21)) | 4.00m | B까지 3.16m | 3.5m | OK |
| 2 (유아풀 (8,9.5)) | 3.00m(상한) | E까지 3.04m | 3.5m(침강부) | OK |

**격자 해상도에서 버그 3건을 스스로 잡았다** — ① L자 컷에 셀 인덱스를 월드 좌표로 넘기고
있었다(Cell < 1일 때 맵 안쪽까지 잘라내 경로가 무한이 된다). ② BFS 비용이 칸 크기를 무시해
거리가 칸 수로 나왔다. ③ 1m 격자는 몸통 반경 0.35m와 결합해 **폭 2m 문을 한 칸으로** 줄여
A↔B가 38.0m로 부풀었다. 0.5m로 내리니 10쌍 전부 도면값 ±3m 안에 들어왔다.

**도면 기대값과의 차이는 불합격이 아니다** — 합격/불합격을 가르는 것은 §10.2-1의 30m 하한과
동시 감시 0개뿐이고, 차이는 주의 표시로만 찍는다.

§10.2-1 ②(동시 감시)를 **배수구에는 적용하지 않는다** — 최후 생존자 페이즈에서 밸브는 얼어붙어
회전음이 발생하지 않으므로 동시 감시할 대상이 없다(§6.5 / GAP-70 절과 동일한 논거).

**파이썬 판에 없는 검사 1개 추가**: 잠수 가능 수심(> `DiveRules.MinDivableDepth`).
수심이 그 이하면 잠수 키를 눌러도 머리가 잠기지 않아 수중 밸브·배수구가 **조용히** 동작하지 않는다.

### GAP 기록

| # | 내용 | 위치 | 처리 |
|---|---|---|---|
| **71** | 소리 인지 거리에 수직 성분(Y)을 넣는가 | `Core/Spatial/DistanceMetric.cs` | ✅ **해소 — 3D로 확정.** 근거 3가지를 그 파일에 기록. 인지 경로 6곳이 전부 경유한다. `TagRules`는 의도적으로 위임하지 않는다 |
| **73** | ~~머리 높이 이중 정의~~ → **GAP-81로 재배정**(블록 1-D §D-4). 73번은 "상호작용(E 홀드) 유효 범위 미정의"로 확정 | — | ↪ 아래 블록 1-D 절 참조 |
| **74** | 잠수 자세·시야 높이(`SubmergedHeadHeight` 0.5m)가 기획서에 없다 | 기획서 §4.3 · §5.9-1 | 🟡 프로토타입 잠정값. 이 값이 **잠수 가능 최소 수심**을 정한다 |
| **75** | **유아풀 본체 수심이 "얕음"으로만 적혀 숫자가 없다** | 기획서 §10.1 · §6.1 | 🟡 0.9m 잠정. 성립 구간은 **0.5m < 수심 < 1.62m**이며, 이 범위를 벗어나면 §6.1 밸브 E(진입 0.5초 / 부상 0.5초) 배분이 성립하지 않는다. 배치 검증이 자동 판정한다 |
| **76** | 숨 게이지가 소유자 클라이언트에 내려오지 않는다 | `Net/PulseNetworkSync.cs` · `Presentation/Player/FirstPersonController.cs` | 🟡 클라이언트 잠수는 **예측**이고 강제 부상의 권위는 서버에 있다. 그래서 게이지가 0이 되는 순간 클라이언트 카메라가 물속에 남는다. 소유자 전송은 **블록 7(HUD)** 범위 |
| **77** | 그레이박스 벽 두께 0.2m · 침강부 반지름 1.2m가 기획서에 없다 | `Editor/MapV2Layout.cs` | 🟡 프로토타입 잠정값. 벽 두께는 배치 검증의 문 해상도에 영향을 준다 |
| **78** | **계단 지오메트리(경사로·계단참)가 없다 — 밸브 C에 걸어서 갈 수 없다** | 기획서 §10.1 | 🔴 §10.1이 "계단 서·동 2개"라고만 적고 좌표·형태가 없어 **창작하지 않았다.** 유도된 제약은 ①상승 3.5m ②메인 풀 (21,17)~(35,25)을 건너지 않음 ③도착 지점이 2층 문(10,28)·(36,18)과 맞음. 배치 검증은 파이썬 판과 같이 계단을 12m 페널티로 **해석적으로** 다루므로 10쌍 결과에는 영향이 없지만, **블록 2의 실기 검증에는 계단이 필요하다** |
| **79** | 찰칵이 리스폰 4곳 좌표가 기획서에 없다 | 기획서 §7 | 🟡 사분면에 하나씩 잠정 배치(라커룸·세탁실·샤워장·관람석). 블록 6이 로직을 붙인다 |
| **80** | **상부 관람층 개구부(문)가 기획서에 없다** | 기획서 §10.1 | 🟡 문이 없으면 두 방이 사방 막힌 상자가 된다. **위치를 §10.2-1 ②로 골랐다**(후보 5개 전수 판정 — 위 표 참조). 물탱크실 (10,28) · 관람석 (36,18). **옮기면 배치 검증을 다시 돌릴 것** |

### 범위 밖으로 남긴 것

- **§10.1·§10.2-1 기획서 반영**: 풀 측벽 요구와 2층 문 2개는 구현에서 나온 제약이다.
  기획서 수정은 지시 없이 하지 않았다 — GAP-78·80으로 올려 둔다.
- **구 그레이박스 제거**: 맵 v2는 `MapV2` 루트만 멱등 재생성한다. 기존 T2 그레이박스(45×35)는
  씬에 그대로 남아 겹친다. 삭제는 파괴적이라 지시 없이 하지 않았다.
- **밸브·배수구·출구·찰칵이의 동작**: 마커만 배치했다. 로직은 블록 2·4·6 소유.
- **`valve_layout_check.py` 갱신**: 파이썬 판은 도면 검사기로 그대로 둔다. C# 판이 씬 검사기다.

---

## 프로토타입 블록 1-D — 계단 · 지오메트리 문서화 · 검증 정본 일원화

블록 1의 잔여 3건(GAP-77·78·80)을 닫고, **판정 정본을 C# 에디터 툴로 확정**한 작업이다.

### D-1 계단 2개 — 생성

| 계단 | 발자국 | 상단 → 하단 | 수평 | 경사면 | 경사각 | 그래프 간선 |
|---|---|---|---|---|---|---|
| 서 | x 10.0~14.0, y 27.0~29.0 | (10,28) → (14,28) | 4.0m | 5.32m | **41.19°** | 6.32m |
| 동 | x 35.5~37.5, y 18.0~22.5 | (36.5,18) → (36.5,22.5) | 4.5m | 5.70m | **37.87°** | 7.20m |

직선 경사로로 만들었다. 프리팹 `CharacterController`는 `slopeLimit 45` · `stepOffset 0.3`이라
두 경사각 모두 등반 가능하며 **여유는 서 3.81° / 동 7.13°** 다. 계단 형상(step)으로 바꾸면
`stepOffset 0.3`이 새 제약이 되므로 단 높이를 0.3m 이하로 잡아야 한다.

**§5.6상 개구부이므로 차폐 콜라이더를 만들지 않았고**, 층간 바닥 차폐판에서 발자국만큼
구멍을 낸다(점 중심 정사각형이 아니라 **실제 발자국**이다 — 경사로가 차폐판을 비스듬히
가로지르는 구간이 남으면 "계단은 미적용"이 부분적으로만 성립한다).

**§5.9 재질 태그는 붙인다**(콘크리트 ×1.0 — §10.1에 계단 재질 항목이 없다). 태그가 붙어 있어야
배치 검증의 보행 판정이 경사로를 **벽이 아니라 바닥**으로 본다. 이 규칙("재질 태그가 있으면
밟는 면")이 없으면 경사로가 허리 높이에서 벽으로 잡혀 주변 통행을 가로막는다.

**하단 접속점을 보정했다.** 서 계단 하단(x=14)은 라커룸 동벽의 몸통 반경 안이다 —
문 (14,27)이 y 26~28만 열려 있고 y 28~31은 벽이라, 경사로 하단 셀이 통째로 막혀
**밸브 C가 그래프에서 단절**됐다(C 관련 4쌍 전부 경로 ∞). 올라서는 지점을 방 안쪽
(13.5, 28)로 물려 해소했다.

### ★ 재측정 결과 — 계단이 조건 ②는 깨지 않았다

지시서가 경고한 대로 동 계단은 밸브 B(34,22)에서 3~4m이고 B는 수중이라 청취 임계가 7.2m다.
그래서 **청취 지점에 2층과 경사로를 추가해** 다시 쟀다(지상 7026 + 2층 490 + 경사로 19 = 7535개).

**결과: 10쌍 전부 동시 감시 0지점.** B↔C·B↔D도 0이다.

> **왜 안 깨지는가 — 수직 차이가 혼자 막는다.** 밸브 B는 Y −3.0이고 2층 귀 높이는
> Y 5.12(3.5 + 1.62)다. **수직 차이만 8.12m**로, 수면 1장을 통과한 술래 청취 임계
> 7.2m를 이미 넘는다. 수평 위치와 무관하게 2층에서는 B가 들리지 않는다.
> 경사로 중간 높이에서는 여유가 줄지만(최근접 약 7.1m로 B는 들린다) **그 지점에서
> 14.4m 안에 들어오는 두 번째 밸브가 없어** 동시 감시가 성립하지 않는다.
>
> 이 논거는 GAP-71을 **3D로 확정한 덕에** 성립한다. 2D였다면 B가 2층에서 들린다.

### ★ 그러나 조건 ①이 깨졌다 — C↔D 21.8m (GAP-82)

계단이 실제 지오메트리로 들어오면서 **경로 측정 방식 자체가 바뀌었다.** 블록 1에서는
파이썬 판을 따라 지상 평면 BFS + 계단 12m 페널티를 썼지만, 이제는 지상·2층 두 층 격자를
계단 간선으로 이어 **Dijkstra**로 잰다(간선 비용이 6~7m라 균일하지 않아 FIFO BFS로는
최단거리가 보장되지 않는다 — 파이썬 판의 결함이기도 하다).

| 쌍 | 구 도면(12m 페널티) | **실측(계단 경유)** | 차이 |
|---|---|---|---|
| A ↔ B | 32.7m | **32.2m** | −0.5 |
| A ↔ C | 64.1m | **60.1m** | −4.0 |
| A ↔ D | 60.7m | **59.3m** | −1.4 |
| A ↔ E | 45.3m | **45.6m** | +0.3 |
| B ↔ C | 41.5m | **35.3m** | −6.2 |
| B ↔ D | 31.1m | **31.0m** | −0.1 |
| B ↔ E | 35.0m | **35.3m** | +0.3 |
| **C ↔ D** | 30.5m | **21.8m** ⚠ | **−8.7** |
| C ↔ E | 30.7m | **32.7m** | +2.0 |
| D ↔ E | 33.4m | **33.7m** | +0.3 |

**원인은 구조적이다.** 물탱크실(C, 2층)이 라커룸 위에 얹혀 있고 D(19,36)가 가깝다(직선 15.6m).
실제 계단으로 내려오면 라커룸 동문 (14,27) 바로 옆에 도착하므로, 12m 페널티가 가정한
"왕복 비용"이 존재하지 않는다.

**선택지를 실측했다** — 어느 것도 공짜가 아니다.

| 선택지 | C↔D | 부작용 |
|---|---|---|
| 현재 (직선 경사로, 간선 6.32m) | **21.8m** | ① 불합격 |
| 계단 왕복 비용을 늘린다 → 간선 **14.5m** 필요(스위치백 등) | **30.0m** | 딱 하한. A↔C 68.3m로 늘어 왕복 부담 재검증 필요 |
| 도면의 12m 페널티를 그대로 재현 | 27.5m | 여전히 불합격 — **도면의 30.5m는 페널티 때문이 아니라 측정 기점 차이에서 나온 값이었다** |
| 2층 문을 서벽 (4,26)으로 옮기고 계단도 서쪽으로 | 26.4m | **C↔E도 27.3m로 함께 불합격**. 더 나쁘다 |
| 밸브 D를 약품창고 북쪽으로 (§10.2-1 기존 1순위 조정안) | 미측정 | §10.2 좌표 변경 → 10쌍 전부 재검증 |

**조건 ②는 0지점으로 합격**이므로 9.3-1의 이중 봉쇄 차단 자체는 성립한다. 깨진 것은
①(②의 하한 보장)이다. **좌표 결정 권한이 없는 사항이라 창작하지 않고 GAP-82로 올린다.**

### D-2 기획서 §10.1 반영 (GAP-77·78·80 해소)

- **풀 측벽** — 수면 네 변이 구조물임을 §10.1에 명시. 없으면 수중 밸브 소리가 수면을 지나지
  않고 옆으로 새어 §5.6 [v0.4]의 "12m → 7.2m"가 성립하지 않는다(실측: A↔B 28지점 · B↔D 2지점).
- **2층 문 2개** (10,28) 물탱크실 동벽 · (36,18) 관람석 동벽 — **후보 5개 전수 판정표**를 함께 적었다.
  탈락 이유를 남긴 것이 핵심이다: (10,26)은 C↔D 9지점, (7,24)는 C↔E 24지점.
- **계단 2개 좌표** + 경사각·`slopeLimit` 여유·개구부 취급.
- **구조물 치수** 표 신설 — 벽 두께 0.2m / 바닥판 0.2m / 벽 높이 3.5m(층 간격에서 유도) /
  침강부 반지름 1.2m.

### D-3 검증 정본 일원화

- §10.2 거리표 제목을 **"C# 배치 검증 툴 실측값(씬 기준)"** 으로 바꾸고 10쌍을 실측값으로 교체.
- §10.2-1 "검증 절차"에서 `valve_layout_check.py` 재실행 → **C# 툴 판정**으로 교체하고,
  파이썬이 정본이 될 수 없는 이유 4가지를 명시.
- `docs/tools/valve_layout_check.py` 머리 주석에 **"좌표 후보 탐색용 보조. 합격 판정은 C# 툴"** 을 박았다.
- "결정적 지형 요소" 표도 정본 툴 모델로 재산출:

| 요소 | 바꿨을 때 | 판정 |
|---|---|---|
| 기계실 출입구 남쪽 1개 | 북쪽 추가 → A↔B 32.2 → **23.4m 불합격** | **필수** |
| **풀 측벽** | 제거 → A↔B 28지점 · B↔D 2지점 **불합격 2쌍** | **필수** |
| **수면 차폐판 ×0.5** | 제거 → C↔E 1지점 **불합격 1쌍** | **필수** |
| 기계실–풀홀 칸막이 | 제거해도 32.2m 그대로 · 추가 불합격 0 | 권장 |
| 계단 2개 | 제거 → 밸브 C **도달 불가**(경로 ∞) | **필수** |

> 칸막이의 판정이 "권장"에서 바뀌지 않았지만 **근거는 바뀌었다** — 파이썬 판은 A↔B가
> 32.7 → 31.9m로 줄어든다고 봤는데, 0.5m 격자 Dijkstra로는 **변화가 없다**.

- 구 수치 잔존 3건 정정: §6.1-0 "30.5~64.1m" → 21.8~60.1m / §9.3-1 "B↔C 경로 41.5m" → 35.3m /
  부록 A "30.5~64.1m, valve_layout_check.py 산출" → 21.8~60.1m, MapV2ValidationTool.cs 실측.

> **실행 경로에 대한 정직한 단서.** 이 환경에서는 Unity 에디터를 띄울 수 없어, 위 실측값은
> **C# 정본 툴과 동일한 알고리즘·동일 지오메트리를 파이썬으로 1:1 재현해** 산출했다
> (0.5m 격자 · 몸통 반경 0.35m · 2층 Dijkstra · 실제 차폐 가중치 · 청취 지점 7535개).
> 에디터에서 `Tools/MARCO/맵 v2 생성` → `맵 v2 배치 검증`을 한 번 돌려 확인해야 한다.

### D-4 GAP 번호 정리

| # | 확정 내용 |
|---|---|
| **73** | **"상호작용(E 홀드) 유효 범위 미정의"** 로 확정. 밸브·배수구·아이템이 전부 이 값에 의존한다(지시서 §12 표와 일치) |
| **81** | 머리 높이 이중 정의 — `DiveRules.StandingHeadHeight`(1.62)와 `Player.prefab` 카메라 `localPosition.y`. 73번에서 **재배정** |

### GAP 기록

| # | 내용 | 위치 | 처리 |
|---|---|---|---|
| **77** | 벽 두께·침강부 반지름 미정의 | 기획서 §10.1 | ✅ **해소** — §10.1 "구조물 치수" 표 신설(0.2m / 1.2m). 잠정값임을 표에 명시 |
| **78** | 계단 지오메트리 없음 → 밸브 C 도달 불가 | 기획서 §10.1 | ✅ **해소** — 서·동 2개 생성. 좌표·경사각·개구부 취급을 §10.1에 기록 |
| **80** | 상부 관람층 개구부 미정의 | 기획서 §10.1 | ✅ **해소** — (10,28)·(36,18) 확정. **선정 근거(후보 5개 전수)를 §10.1에 남겼다** |
| **81** | 머리 높이가 코드 상수와 프리팹 두 곳에 있다 | `Core/Locomotion/DiveRules.cs` · `Prefabs/Player.prefab` | 🟡 갈라지면 서버와 클라이언트의 머리 높이가 달라진다. `NetworkSetupDiagnostics`에 일치 검사를 넣을 것 |
| **82** | C↔D 경로 21.8m가 §10.2-1 ①(30m) 미달 | 기획서 §10.2 · §10.2-1 | ✅ **해소 — 조건① 자체를 폐지했다.** 지오메트리 변경 없음. 아래 절 참조 |

### 범위 밖으로 남긴 것

- **C↔D 해소**: 좌표 변경(계단 형상·밸브 D 위치·구역 배치)은 결정 사항이라 손대지 않았다.
- **계단 형상(step) 전환**: 직선 경사로로 만들었다. `stepOffset 0.3` 제약은 기록만 했다.
- **계단 재질**: §10.1에 항목이 없어 콘크리트 ×1.0(기본값)으로 뒀다.
- **경사로 ↔ 2층 바닥 겹침**: 동 계단이 관람석 바닥과 x 35.5~36에서 겹친다. 경사로가
  그 구간에서 더 낮아 하향 레이는 항상 관람석 바닥을 먼저 맞지만, **y=18 선 위에서만**
  높이가 같다(재질 ×0.7 vs ×1.0). 0.5m 폭 한 줄이라 그대로 뒀다.

---

## GAP-82 해소 — §10.2-1 조건 ①(경로 30m) 폐지

**지오메트리를 바꾸지 않았다.** 계단 좌표·밸브 좌표·구역 배치 전부 그대로이며,
바뀐 것은 **판정 규칙**과 그것을 읽는 문서·코드다.

### 무엇이 바뀌었나

| | 전 | 후 |
|---|---|---|
| 합격 조건 | ① 경로 30m 이상 **AND** ② 동시 감시 0개 | **② 동시 감시 0개 단독** |
| 경로 거리 | 판정 항목 | **기록 항목.** 30m 미만은 `OK ⚠` + "주의" 목록 |
| C↔D 21.8m | FAIL | **OK ⚠** — §20.3 플레이테스트 관찰 항목 |
| 10쌍 결과 | 9/10 | **10/10 합격** (주의 1쌍) |

### 폐지 근거 — ②가 이미 하한을 내장한다

근거 세 가지를 §10.2-1 본문에 남겼다. 그중 ②는 이번에 직접 검산했다.

동시 감시 임계는 `14.4 × (1 + 0.5ⁿ)`이다(n = 두 밸브 사이 벽 수). 유도는
*감시 지점 P가 한 밸브 쪽에 붙어 서고 벽 n장이 전부 반대편 밸브 쪽에 걸릴 때*
`a ≤ 14.4` · `b ≤ 14.4 × 0.5ⁿ`이므로 최대 밸브 간격이 `a + b`라는 것이다.

| n | 임계 |
|---|---|
| 0 | 28.800m |
| 1 | 21.600m |
| 2 | 18.000m |
| 3 | 16.200m |
| 4 | 15.300m |
| 8 | 14.456m |
| **n → ∞** | **14.400m (하한, 아래로 내려가지 않는다)** |

**따라서 직선 거리가 14.4m 이하인 쌍은 벽을 아무리 끼워도 ②를 통과할 수 없다.**
②를 통과했다는 사실 자체가 "직선 14.4m 초과"를 증명하므로, 별도의 거리 하한이 할 일이 없다.
C↔D는 직선 15.6m로 14.4m를 1.2m 넘으며, 실제로 ②를 통과한다(동시 감시 0지점) — 정합한다.

나머지 두 근거 —
- **① 봉쇄 반경 근거 폐기**: v0.3의 "회전 완료 전 도착 차단"은 6.1절 총 점유 8.0초화로
  봉쇄 반경이 `5.30 × 8 = 42.4m`가 되어 **모든 밸브 쌍 거리를 넘는다.** 이동 시간 기반 유도는 무효.
- **③ 소비처 부재**: v0.4에 경로 거리를 전제하는 규칙이 없다. §9.3-1은 정보(청취 반경) 기반,
  §6.2는 1:1 배치로 왕복을 없애고, 왕복이 생기는 예외도 역류 180초 대비 **여유 162초**로 성립한다.
  (지시서는 161초로 적었는데, §10.2 왕복 부담 검증을 최장 편도 60.1m 실측으로 갱신하면서
  18.1초 → 여유 **161.9 ≈ 162초**가 됐다. 같은 계산의 소수 반올림 차이다.)

### 코드

`MapV2ValidationTool` — `MinPathMeters` → **`PathNoticeMeters`**(이름으로 판정이 아님을 못박음).
불합격 카운터는 동시 감시만 올리고, 경로 미달은 `notices`로 따로 세어 요약에 분리해 찍는다.
판정 문자열도 `OK` / `OK ⚠` / `FAIL` 세 가지로 갈랐다.

> **경로 무한(∞)은 예외적으로 계속 경고한다.** 판정 항목은 아니지만 "밸브에 갈 수 없다"는
> 뜻이라 거의 항상 맵 결함이다(블록 1-D에서 서 계단 접속점이 막혀 실제로 발생했다).

### 문서

- **§10.2-1** 절 제목을 "2조건 동시 충족" → **"동시 감시 단독 조건"** 으로 바꾸고 조건표를 ② 하나로.
  폐지 근거 3가지를 본문에 명시. ② 정의에 **"P는 지상 + 2층 + 계단 경사로 전부"** 를 추가.
- **§10.2** 거리표의 C↔D 주석을 "불합격" → **"주의"** 로 격하.
- **§20.3** 에 관찰 항목 등록 — *"술래가 C↔D를 무정보로 왕복하는 플레이가 나오는가.
  나오고 그것이 유효하면 서 계단을 스위치백(간선 14.5m)으로 되돌린다"* + 부작용(A↔C 68.3m,
  왕복 부담 재검증)까지 표로 기록.
- **§20.3** 의 구 문구 *"임시 배치에서도 10.2-1 최소 이격 30m 제약은 지킨다"* → **조건②를 지킨다**로 교체.
  (거리로 맞추지 말고 검증 툴로 판정하라는 지시를 함께 적었다.)
- **부록 A** 배치 제약 블록을 ② 단독 + 폐지 근거 요약으로 교체. "최소 이격 30m" 주석도 정정.
- `docs/tools/valve_layout_check.py` 머리 주석 — ①이 폐지됐고 경로는 참고 출력임을 명시.

### 더블체크

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | ✅ `PathNoticeMeters`·`notices` 사용처 확인 |
| 2 | 라운드 전이 초기화 | ✅ 런타임 상태 무변경(에디터 툴 전용) |
| 3 | 경계값 | ✅ 임계 수열을 n=0~8·∞로 검산해 14.4m 수렴 확인 |
| 4 | 기존 테스트 의미 변화 | ✅ EditMode 테스트는 배치 검증을 참조하지 않는다(765개 무영향) |
| 5 | 서버 권위 | ✅ 해당 없음 |
| 6 | 캐시 금지 | ✅ 해당 없음 |
| 7 | 계층 경계 | ✅ Editor 어셈블리만 수정 |
| 8 | 상수 중복 | ✅ 30f는 `PathNoticeMeters` 한 곳 |
| 9 | enum 파급 | ✅ 없음 |
| 10 | 문서 정합 | ✅ 구 "30m 제약" 표현 전수 정정(기획서 3곳 · 부록 A 2곳 · 파이썬 머리 주석 · 에디터 주석 4곳) |

### 최종 배치 검증 결과 (지오메트리 무변경, 판정 규칙만 변경)

**밸브 10/10 합격** — 동시 감시 지점 전부 0개. 주의 1쌍(C↔D 21.8m).
**배수구 2/2 합격** · **잠수 가능 수심 4/4 합격**.

---

## 프로토타입 블록 2 — 밸브 8초 균등 · 진행도 감쇠 · 5종 · 역류 · 무작위 활성 · 게이트 latch

### ★ 선행 확인이 실제 결함을 잡았다 — `Valve.Tick()` 호출부가 조건부였다

지시서가 *"Valve.Tick()을 실제로 호출하는 프로덕션 코드가 있는지 먼저 확인해라.
없거나 조건부면 역류도 감쇠도 영영 작동하지 않는다"* 고 경고한 그대로였다.

```csharp
// ValveNetworkSync.Update (변경 전)
if (!_driver.IsRotating)
    return;                     // ← 감쇠·역류가 여기서 영구히 차단된다
bool opened = _driver.Tick(Time.deltaTime);
```

`ServerValveDriver.Tick`의 계약을 "회전 중일 때만 유효"에서 **"모든 상태에서 매 프레임"**
으로 바꾸고, 반환형을 `bool`에서 `ValveTickResult`(Opened / ReflowStarted / Closed /
ReflowPulses)로 교체했다 — 호출부가 무엇을 전파해야 하는지 구조체가 말해 주므로
`if (!IsRotating)` 같은 가드를 다시 넣을 유인이 없어진다.

### 2-A 밸브 5종 — 총 점유 8.0초 균등

`ValveOccupancy`(신설)가 배분만 갖고 **총 점유는 합으로 유도**한다 — 8.0을 다섯 곳에
적으면 배분과 합이 어긋난 채 남는다.

| 밸브 | 진입 | 회전 | 부상 | 총 점유 | 소음 배율 |
|---|---|---|---|---|---|
| A 기계실 | 0 | 8.0 | 0 | **8.0** | ×0.5 (기계 앰비언스 → 6m) |
| B 메인풀 수중 | 1.5 | 5.0 | 1.5 | **8.0** | ×1.0 |
| C 물탱크실 2층 | 0 | 8.0 | 0 | **8.0** | ×1.0 |
| D 약품창고 | 0 | 8.0 | 0 | **8.0** | ×1.0 |
| E 유아풀 수중 | 0.5 | 7.0 | 0.5 | **8.0** | ×1.0 |

`DefaultRotationSeconds`(3.0)는 **삭제하지 않았다** — 씬 프리팹과 v0.3 테스트가 참조한다.
대신 v0.4 밸브는 `ValveOccupancy.RotateSeconds`로 자기 시간을 갖고, 둘이 갈라졌다는
사실 자체를 테스트로 고정했다(`ValveA_MatchesLegacyDefault`).

동시 작업 배율도 §6.1대로 넣었다 — 1인 1.0 / 2인 ×1.6 / 3인 이상 ×1.9(상한).
8.0초 → 2인 5.0초 / 3인 4.21초(기획서는 4.2로 반올림해 적었고, 정본 상수는 1.9다).

### 2-B 진행도 감쇠 — v0.4의 핵심

진행도를 **0.0~1.0 정규화**로 다룬다(초 단위로 다루면 밸브마다 배분이 달라 이어받기
계산이 틀어진다). 중단 시 리셋하지 않고 유예 3초 뒤 −0.10/s로 깎는다.

| 경계 | 결과 |
|---|---|
| 중단 후 t=2.9 | 진행도 유지 · `IsDecaying` false |
| 중단 후 t=3.0 | 감쇠 시작 |
| 만충에서 t=13.0 | 진행도 0.0 → Closed (유도값 `3 + 1/0.10`) |
| 감쇠 중 재상호작용 | **그 시점 값에서 이어짐** (0부터가 아니다) |

**★ 감쇠와 역류는 별개 필드다.** `_sinceInterrupt`(감쇠)와 `_sinceOpen`/`_sinceReflow`(역류)를
따로 두고, `Tick`이 상태별로 갈라 하나만 돌린다. 한 필드로 합치면 "개방 직후 감쇠가
시작되는" 버그가 조용히 생긴다. 양방향을 테스트로 고정했다 —
`Decay_DoesNotRunWhileOpen`(개방 후 13초 방치해도 진행도 1.0 유지) /
`Reflow_DoesNotRunWhileRotating`(회전 200초에도 `OpenHoldRemaining` 0).

수중 밸브 부상·연결 끊김도 중단으로 처리한다(§6.1 / §13.x) — `Interrupt` 하나를 공유한다.

### 2-C 역류

| 경계 | 상태 |
|---|---|
| t=179.9 | Open |
| t=180.0 | **Reflowing** (진행도 0으로 내림) |
| t=209.9 | Reflowing |
| t=210.0 | Closed |

`ValveState.Reflowing`을 추가했고, Reflowing에서도 상호작용이 가능하다.
**진행도를 0으로 내리는 이유**는 §6.1-2가 *"회전 시간은 최초와 동일"* 이라고 못박았기
때문이다 — 역류 잔여 30초는 `Progress01`이 아니라 `ReflowRemaining`이 따로 들고 있다.

역류 물소리 파문은 **역류 시작(0초)·10초·20초에 3회**다(30초는 폐쇄 시점이라 그때 울리면
경고가 아니라 사후 통보가 된다). 횟수는 `30 ÷ 10`에서 유도하고, 기존 `Valve` 등급(12m)을
재사용해 **새 SoundType을 만들지 않았다**(§3.3).

발행 경로로 `PulseNetworkSync.ServerEmitWorldPulse`(신설)를 뒀다 — 플레이어가 아닌
발생원(`WorldSourceId = ulong.MaxValue`)이라 GAP-1이 아무도 배제하지 않고 §8 어워드에도
귀속되지 않는다. 블록 4(페이즈 진입 알림)·블록 3(태그 파문)·블록 7(종반 출구)이 같은 경로를 쓴다.

> **밸브 A의 소음 ×0.5를 역류 물소리에는 적용하지 않았다 — GAP-83.** §6.1의 ×0.5는
> *회전음*에 붙은 환경 규칙이고, 물소리가 같은 보정을 받는다는 근거가 기획서에 없다.

### ★ enum 파급(더블체크 9)에서 **실제 버그 1건**

`ValveState`를 읽는 곳을 전수 확인한 결과 두 곳이 새 값을 처리하지 못했다.

| 위치 | 증상 | 처리 |
|---|---|---|
| `ValveInteractionController.TryStart` | `State != Closed`로 걸러 **Reflowing에서 재상호작용이 조용히 막혔다** — §6.1-2 역류 복구가 영영 불가능 | `State == Open`만 거르도록 고침. Rotating 합류 판정은 Core Valve에 맡긴다 |
| `InGameHud` 밸브 핍 | `default:` 로 떨어져 **Reflowing이 "닫힘"으로 렌더링** | Reflowing 점멸 + 감쇠 색 + 비활성 잠금 색 추가 |

### ★ 감쇠 색 구분 (생략 금지 항목)

§12.4 *"감쇠 중 게이지는 진행 중과 색이 달라야 한다"* — **연출이 아니라 정보다.**
두 곳에 넣었다.

- `ValveVisualIndicator`: 회전 노랑 / **감쇠 주황** / 개방 초록 / 역류 적색 점멸 / 비활성 잠금
- `InGameHud` 핍: 같은 5분법. 감쇠 색이 회전 색(accent)과 다른 것을 코드 주석으로 못박음

### 2-D 무작위 활성

`ValveRoster`(신설)가 §6.2 표를 소유한다. **활성 개수를 표에 적지 않고 요구 개수에서
유도한다**(`ActiveCount = RequiredOpenCount + 1`) — 두 값을 따로 적으면 불변 조건이
깨진 채 남을 수 있다. `AssertInvariant`가 어긋나면 던진다.

| 총원 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|
| 요구 개방 | 2 | 2 | 3 | 3 | 3 |
| 활성(유도) | 3 | 3 | 4 | 4 | 4 |

`UnityEngine.Random`은 Core 계층 위반이라 `DeterministicRandom`(xorshift32, 신설)을
만들어 시드를 주입받는다. 시드는 `라운드 번호 × 73856093 + 총원 × 19349663 + 1`이며
**결과만 네트워크로 나간다** — 클라이언트가 같은 시드로 다시 굴리면 서버 권위가 아니라 합의가 된다.

**호출부**: `RoundNetworkSync.BeginRound` → `ServerSelectActiveValves` → 각 밸브의
`ServerSetActive`. 이 호출부가 없으면 5개가 전부 활성이라 불변 조건이 깨진다(더블체크 1).

### 2-E 게이트 동시 개방 + latch

기존 `EscapeGateRegistry` / `IEscapeGateState`를 **확장했다 — 새 클래스를 만들지 않았다.**
`IEscapeGateState`에 `RequiredOpenValves`를 추가하고, `OpenedValves`의 계약을
*"현재 동시에 Open인 수, 역류로 줄어들 수 있다"* 로 문서화했다.

`ValveObjectiveTracker.CountOpened()`는 이미 **상태를 직접 세고 있어** 누적 카운터가
아니었다(전수 확인). 바뀐 것은 판정식이다 —
`OpenedCount >= TotalValves` → **`OpenedCount >= RequiredOpenCount` + latch**.
latch는 `Rescan()`(라운드 경계)에서만 풀린다.

"현재 동시 개방 수"는 `IEscapeGateState.OpenedValves`로 언제든 조회 가능하다 —
블록 4의 §6.5-2 `T = 14 − (동시 개방 × 3)`이 이 값을 쓴다.

### 2-F 네트워크 이벤트

SyncVar 3종을 추가했다.

| 항목 | 방식 | 근거 |
|---|---|---|
| `_decaying` (bool) | 바뀔 때만 | §12.4 HUD가 색을 달리해야 하므로 **플래그로** 보낸다 |
| `_reflowRemainingAtStart` (float) | **역류 시작 시 1회** | §14.3 "매 프레임 동기화하지 마라" — 클라가 카운트다운 |
| `_active` (bool) | 라운드 시작 시 | §6.1-0 활성 조합 전파. 늦게 접속해도 SyncVar라 현재값을 받는다 |

진행도(`_progress`)는 기존대로 SyncVar다 — FishNet이 값이 같으면 보내지 않으므로
"시작/중단/재개 시점에만 전송"과 실질적으로 같다. `EscapeGateOpened`는 latch이므로
취소 이벤트를 만들지 않았다.

### 라운드 전이 초기화(더블체크 2)

`ValveNetworkSync.ServerResetForNewRound`가 **Valve 인스턴스를 교체하지 않게** 바꿨다.
교체하면 `ServerValveDriver`가 구독한 Opened/ReflowStarted/ReflowPulse/Closed 이벤트가
끊어져 **감쇠·역류 전파가 조용히 사라진다.** `Valve.ResetForNewRound()`로 제자리에서
되돌리며, Open 진입 시각·역류 타이머·진행도·활성 여부가 전부 초기화된다
(`ResetForNewRound_ClearsOpenTimeAndProgress`가 고정).

`ValveBehaviour.ResetValveForNewRound`도 같은 이유로 인스턴스 교체를 제거했다.

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | **수정 후 OK** — `Valve.Tick` 조건부 차단 해제 · `ServerSelectActiveValves` 호출부 신설 · `ServerSetActive` 배선 |
| 2 | 라운드 전이 초기화 | **수정 후 OK** — 인스턴스 교체 제거 · 게이트 latch 해제 지점 확인 |
| 3 | 경계값 | OK — 감쇠 2.9/3.0/13.0 · 역류 179.9/180.0/209.9/210.0 · 배분 합 8.0 |
| 4 | 기존 테스트 의미 변화 | **수정 후 OK** — v0.3 의미를 고정한 **8건** 전수 갱신(아래) |
| 5 | 서버 권위 | OK — 활성 선택·감쇠·역류 전부 서버. 클라이언트는 홀드 의사만 |
| 6 | 캐시 금지 | OK — `LocalPlayerRegistry.Current` 미사용 |
| 7 | 계층 경계 | OK — `UnityEngine.Random` 대신 Core `DeterministicRandom` |
| 8 | 상수 중복 | OK — 총 점유·전손 시간·역류 횟수·활성 개수 전부 유도 |
| 9 | enum 파급 | **수정 후 OK** — 실제 버그 1건 + 렌더링 누락 1건 |
| 10 | 문서 정합 | OK — v0.3 회전 시간 표(3/2/4) → v0.4 배분으로 갱신 |

**의미가 뒤집힌 기존 테스트 8건** — 전부 v0.4 근거를 주석으로 남기고 개명했다.

| 테스트 | v0.3 의미 | v0.4 |
|---|---|---|
| `Interrupt_BeforeCompletion_ResetsProgressToZero` | 중단 시 진행도 0 | → `...KeepsProgressForDecay` |
| `AfterInterrupt_AnotherRunnerCanStartImmediately` | 0부터 다시 | → `...ResumesFromRemainingProgress` |
| `TryBeginRotation_WhileAnotherIsRotating_IsRejected` | 가로채기 거부 | → `...JoinsAsConcurrentWork` |
| `Disconnect_HandledAsInterrupt` | 진행도 0 | 감쇠 대상으로 유지 |
| `EndHold_ByHolder_ResetsProgress` | 진행도 0 | → `...KeepsProgressForDecay` |
| `ReleasingKey_CancelsAndResetsProgress` | 부분 진행 없음 | → `...CancelsButKeepsProgressForDecay` |
| `RestartAfterCancel_BeginsFromZero` | 0부터 | → `...ResumesFromRemainingProgress` |
| `ValveB_OpensFasterThanA_AndCSlower` | 회전 시간 차등 | 총 점유 균등 + 배분 차등 |

### 테스트

신규 `ValveDecayReflowTests`(17) · `ValveRosterTests`(13). 갱신 8건.
**42 클래스 / 805 통과 / 0 실패** (블록 1 종료 765 → 805).

### 전체 재검증

Core / Net / Presentation / Marco.Editor / Core.Tests — **오류 0 · 프로젝트 경고 0**.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **83** | 밸브 A의 회전음 ×0.5 보정이 §6.1-2 역류 물소리에도 적용되는가 | 🟡 기획서 미명시. ×0.5를 **적용하지 않음**(회전음 전용 환경 규칙으로 해석). 적용해야 하면 `EmitReflowPulse` 한 곳만 고치면 된다 |

### 범위 밖으로 남긴 것

- **수중 밸브 진입·부상 시간의 실제 이동 강제**: `ValveOccupancy`가 배분을 데이터로 갖지만,
  잠수 하강 1.5초를 실제로 소비하게 만드는 것은 물 볼륨 이동(블록 1-A③의 잠수)과
  상호작용 범위(GAP-73)에 걸려 있다. 진행도 규칙은 회전 구간만 소유한다.
- **수중 작업 중 2.5초 주기 강제 파문**: §6.1 [v0.4] 신설 항목이며 **블록 4**가
  배수구와 함께 같은 규칙으로 구현한다(§6.5-3이 그것을 전제한다).
- **HUD 역류 잔여 시간 링 / 슬롯 개수 가변**: 블록 7(HUD) 범위. 이 블록에서는 색 구분만 넣었다.

---

## 프로토타입 블록 3 — 승리조건 3분법 · 인원별 보정 · 종반 압박 · 태그

### 지시와 기획서의 표현 차이 1건 — 기획서를 따랐다 (결과 동일)

지시서 3-A는 ①을 `gateOpened && runnersEscaped >= EscapeRequirement`로 적었지만,
§6.3 [v0.4]는 **게이트를 판정식에서 의도적으로 뺐다**:

> 탈출의 정의 자체가 *"출구 게이트 개방 후 출구 접촉"* 이므로 게이트가 닫혀 있으면
> `escaped`가 애초에 증가하지 않는다. 게이트는 판정식의 항이 아니라 **탈출의 전제 조건**이다.

두 식은 탈출 접수 지점이 게이트를 강제하는 한 **논리적으로 같은 판정**을 낸다. 기획서대로
게이트 검사를 `ServerRoundDriver.TryRegisterEscape(gateOpen)`에 두고 판정식에서는 뺐다.
구 `totalValves > 0` 방어의 의도("목표가 구성되지 않은 씬을 승리로 읽지 않는다")도 같은
이유로 그쪽에서 산다 — 밸브가 0개면 게이트가 열리지 않아 탈출이 집계되지 않는다.

### 3-A 승리조건 3분법

```
① 요구 인원 탈출   : escaped >= ceil(runners / 2)          → RunnersWin
② 최후 생존자 탈출 : lastSurvivorEscaped (사건 플래그)      → RunnersWin
③ 술래 승         : alive == 0 || timeRemaining <= 0       → SeekerWin
```

- **`TagWinThreshold`(2) 삭제.** 도망자 1명이 남는 것은 술래 승리가 아니라 §6.5 페이즈 시작이다.
- **`EscapeWinThreshold`(2) 삭제.** `ValveRoster.EscapeRequirement = ⌈n/2⌉` 한 곳이 소유한다.
  두 상수가 코드에서 사라졌는지를 리플렉션 테스트로 고정했다(`RemovedConstants_AreGone`).
- **"살아있는 도망자" 셈의 단일 소유자**로 `RunnerCensus`를 신설했다(`Alive = 전체 − 태그 − 탈출`).
  판정·페이즈 진입·배수구·HUD가 전부 이 구조체를 조회한다.
- **`RoundResult`에 값을 추가하지 않았다.** 최후 생존자 탈출도 `RunnersWin`이며
  `HudFormatter.FormatResultReason`이 문구만 가른다("마지막 한 명이 배수구로 빠져나갔다").
- **`lastSurvivorEscaped`는 사건 플래그다.** `ServerRoundDriver`가 페이즈 중 탈출이 발생한
  순간에 세운다(출구 탈출·배수구 탈출 둘 다). 집계에서 역산하지 않는다.

### 승리 판정 전수 조합표 — 도망자 2·3·4·5 (시간 종료 시점)

`ExhaustiveTable_MatchesDesignDoc`가 전 조합을 돌려 §6.3과 대조한다.

| 도망자 | 탈출 요구 | 케이스 | 판정 규칙 | 결과 |
|---|---|---|---|---|
| 2 | 1 | 6 | 탈출 ≥ 1 → 도망자 승 | 6/6 일치 |
| 3 | 2 | 10 | 탈출 ≥ 2 → 도망자 승 | 10/10 일치 |
| 4 | 2 | 15 | 탈출 ≥ 2 → 도망자 승 | 15/15 일치 |
| 5 | 3 | 21 | 탈출 ≥ 3 → 도망자 승 | 21/21 일치 |
| **계** | | **52** | | **52/52** |

**순서 의존 4건** — 같은 집계에서 마지막 이탈이 탈출이면 도망자 승, 태그면 술래 승
(`OrderDependentCases_SplitOnLastDeparture` + 정확히 4건임을 `OrderDependence_IsExactlyFourCases`가 셈):

| 도망자 | 탈출 | 태그 | 마지막 = 탈출 | 마지막 = 태그 |
|---|---|---|---|---|
| 3 | 1 | 2 | 도망자 승 | 술래 승 |
| 4 | 1 | 3 | 도망자 승 | 술래 승 |
| 5 | 1 | 4 | 도망자 승 | 술래 승 |
| 5 | 2 | 3 | 도망자 승 | 술래 승 |

**"살아있는 1명"은 술래 승이 아니다** — `TwoTaggedOfThree_IsPhaseEntry_NotSeekerWin`.
v0.3과의 차이는 §6.3 표대로 **정확히 1건**(도망자 3, e1 t2)이며 나머지 9건은 결과가 같다
(`DifferenceFromV03_IsExactlyOneCase`).

### 3-B 인원별 술래 이속

`SeekerSpeed = 5.4f`(4인 5.0 × 1.08)를 **`SeekerBaseSpeed = 5.0` + `SeekerSpeedCorrection(n)`**
으로 쪼갰다. 결과값을 상수로 박지 않는다.

| 총원 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|
| 보정 | +5% | +5% | +4% | **+6%** | +8% |
| 속도 | 5.25 | 5.25 | 5.20 | **5.30** | 5.40 |

**하한 +1%** 를 상수로 두고 표 밖 인원수에도 적용된다. 경계값 테스트:
**5.00 → 걷기 등급(2m) / 5.01 → 질주 등급(6m)**, 하한을 적용한 최저 속도 5.05도 질주 등급.
v0.3 호환용 `LocomotionConfig.SeekerSpeed`는 5인 기준 유도 프로퍼티로 남겼다.

`LocomotionSimulator`가 `TotalPlayers`·`EndgamePressure`를 받아 술래 속도를 고른다.
`FirstPersonController`는 이 두 값을 **매 프레임** `RoundStateRegistry`(신설)에서 읽어 꽂는다 —
필드에 굳히면 라운드 중 인원 변화·종반 진입이 반영되지 않는다.

### 3-C 종반 압박

| 잔여 | 효과 | 구현 |
|---|---|---|
| 2분 | 저역 드론 | 상수만(`EndgameDroneSeconds`) — 연출은 블록 7 |
| **1분** | **술래 +5% (1회, 유지)** | `_endgamePressure` SyncVar latch |
| 30초 | 양쪽 출구 고함급 파문 주기 | `EscapePointRegistry`(신설) 경유 |

**곱셈이다.** §6.2-1이 *"5인 기준 5.30 → 5.57"* 이라고 결과값을 직접 적었고
5.30 × 1.05 = 5.565 ≈ 5.57이다. **§9.4 "이중 보정 금지"와 충돌하지 않는다** — 그 원칙이 막는
것은 같은 축(인원)의 보정을 두 번 거는 것이고, 여기서는 인원 축과 잔여 시간 축이 다르다.
라운드 리셋 시 두 보정 모두 해제된다(`ServerResetWorld` + `OnStopNetwork` + 레지스트리 세션 초기화).

출구 좌표를 Net에 박지 않고 `EscapePointTrigger`가 스스로 등록하는 레지스트리를 만들었다 —
§10.5 좌표를 코드에 적으면 §10.1 맵 데이터와 갈라진다.

### 3-D 태그 처리

`TagAftermath`(신설) — 술래 경직 1.0초 / 도망자 암전 3.0초 / 파문 `Shout` 재사용.

- **★ 경직 중 태그 판정도 막힌다.** `TagNetworkSync.ServerRequestTag`가 경직 맵을 먼저 보고
  거부한다 — 이동만 막으면 붙어 있는 도망자 둘이 한 번에 정리된다.
- 태그 지점 파문은 `PulseNetworkSync.ServerEmitWorldPulse(Shout)` — **새 SoundType 없음**.
- **어워드 오염 경로 추적 결과**: 어워드 입구 `RoundNetworkSync.ServerRecordPulse`의 호출부는
  5곳(음성·발소리 / 노크 / 질식 / 술래 외침 / 비명)뿐이고 **`ServerEmitWorldPulse`는 그것을
  부르지 않는다.** 발생원도 `WorldSourceId`라 어떤 플레이어에게도 귀속되지 않는다. 설령
  귀속되더라도 `AwardTally.ScreamCount`는 `type == Scream`일 때만 증가한다 — 이중으로 막혀 있다.
- **§6.5 페이즈 트리거 훅**: `RoundNetworkSync.EvaluateAndPush`가 `RunnerCensus.ShouldEnterLastSurvivorPhase`로
  진입을 판정하고 `OnLastSurvivorPhaseEntered`를 부른다. **호출부 없는 훅이 아니다** — 지금은
  로그만 남기며 블록 4가 배수구·90초 타이머를 붙인다.

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — `TickEndgamePressure`·`PublishRoundState`·페이즈 훅 전부 `TickRound`/`EvaluateAndPush`에서 호출 |
| 2 | 라운드 전이 초기화 | OK — 종반 압박·페이즈·경직 맵·태그 시각을 `ServerResetWorld`/`ServerResetForNewRound`에서 해제 |
| 3 | 경계값 | OK — 5.00/5.01 · 경직 0.99/1.0 · 암전 2.99/3.0 · 페이즈 진입 2/1/0명 · 라운드 89초에 진입 → 89초 |
| 4 | 기존 테스트 의미 변화 | **수정 후 OK** — 아래 표 |
| 5 | 서버 권위 | OK — 인원·종반·페이즈 전부 서버가 쓰고 SyncVar/레지스트리로 읽기만 |
| 6 | 캐시 금지 | OK — 시뮬레이터 입력을 매 프레임 레지스트리에서 재조회 |
| 7 | 계층 경계 | OK — 레지스트리 2종은 Core, Physics·FishNet 미참조 |
| 8 | 상수 중복 | OK — 5.30·5.57·탈출 요구 전부 유도. `EscapeRequirement`는 `ValveRoster` 한 곳 |
| 9 | enum 파급 | OK — `RoundResult` 무변경(값 추가 금지 지시 준수) |
| 10 | 문서 정합 | OK — 결과 문구 "태그 2명" → "전원", 로그의 구 상수 참조 제거 |

**의미가 뒤집힌 기존 테스트** (전부 v0.4 근거 주석 + 개명):

| 테스트 | v0.3 | v0.4 |
|---|---|---|
| `Evaluate_TwoTagged_SeekerWin_EvenWithTimeLeft` | 태그 2 → 술래 승 | → `TwoTaggedOfThree_IsNotSeekerWin_EntersPhaseInstead` |
| `TwoTagged_DecidesSeekerWinImmediately` | 태그 2 → 즉시 술래 승 | → `TwoTaggedOfThree_DoesNotEndRound_AnyMore` |
| `TwoTagged_ThirdRunnerStillFree_RoundStillEnds` | "산술적으로 불가능" 즉시 종료 | → `...RoundContinues` (배수구 경로 존재) |
| `TwoTaggedBeforeEscape_LatchesSeekerWin` | 이후 탈출 거부 | 탈출이 **집계됨** |
| `OneEscapedTwoTagged_SeekerWins` | 결과만 고정 | → **순서 의존 케이스**로 명시 |
| `TaggedCount_TwoRealPlayers_OneTagged_DoesNotEndRound` | 2인 구성 미결 | 생존 0 → **술래 승** |
| `FormatResultReason_SeekerWin_...` | "2명" 문구 | "전원" 문구 |
| `Seeker_MovesAtFivePointFour` | 5.4 | → `...PerCountCorrectedSpeed` (유도값 5.30) |

### 테스트

신규 `SeekerSpeedAndTagAftermathTests`(24), `WinConditionEvaluatorTests` 전면 재작성.
**43 클래스 / 825 통과 / 0 실패** (블록 2 종료 805 → 825).

### 전체 재검증

Core / Net / Presentation / Marco.Editor / Core.Tests — **오류 0 · 프로젝트 경고 0**.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **84** | §3.1 술래 잠행 3.5 m/s가 인원 보정을 받는가 | 🟡 기획서 미명시. **고정값**으로 둠(보정해도 5.0 경계 안이라 발소리 등급은 같지만, 속도 손실이 인원에 따라 달라질 근거가 없다) |
| **85** | §3.1-1 술래 경직의 **이동** 제한이 클라이언트에 전파되는가 | 🟡 경직 맵이 서버에만 있다. **태그 판정 차단은 서버에서 완결**되지만 이동 1초 정지는 클라이언트 이동이 로컬 권위라 걸리지 않는다. 블록 7(연출·태그 연출)에서 SyncVar로 내려보내야 한다 |
| **86** | §6.2-1 30초 출구 파문의 **주기**가 기획서에 없다 | 🟡 "주기 발생"만 있음. 잠정 5초(30초 구간 6회) |

### 범위 밖으로 남긴 것

- 태그 연출(적색 플래시·FOV·암전 화면) — 블록 7 ②.
- 2분 드론 사운드 — 블록 7 연출.

---

## 프로토타입 블록 4 — 최후 생존자 페이즈 · 배수구

기획서 §6.5-1 · §6.5-2 · §6.5-3 · §6.3을 다시 읽고 시작했다.

### ★ 설계 결정 — 페이즈는 **새 `GameFlowState`가 아니라 `InGame`의 하위 상태**다

새 상태를 추가하면 `!= InGame` 게이트 7곳이 페이즈 중에 전부 닫혀 **밸브·태그·파문이 멈춘다**
(정지 조건 "ServerPhase 전이 구독처에서 새 상태를 처리 못 하는 곳"에 정확히 해당). 그래서
`ServerRoundDriver.LastSurvivorPhase` 플래그 + `PhaseRemainingSeconds`로 표현했다.
**구독처 전수 판정:**

| 구독처 | 게이트 | 페이즈 중 동작 | 판정 |
|---|---|---|---|
| `PulseNetworkSync:217` 음성·발소리 파문 | `!= InGame` 거부 | 그대로 발행 | ✅ 필요(도망자 부상 위치 노출이 §6.5-2의 핵심) |
| `PulseNetworkSync:254` 노크 | `!= InGame` 거부 | 그대로 | ✅ §6.5-3 "메아리 노크로 술래를 떼어낸다" |
| `PulseNetworkSync:514` 전이 시 초기화 | `== InGame` 진입 1회 | 재진입 없음 | ✅ 페이즈가 전이가 아니므로 숨·잠수 주장이 **지워지지 않는다**(지워지면 잠수 중 강제 부상) |
| `TagNetworkSync:136` 태그 | `!= InGame` 거부 | 그대로 | ✅ 통과 1.5초 "그 동안 태그 가능" |
| `ValveNetworkSync:173/208` 밸브 조작·Tick | `!= InGame` 거부/동결 | 그대로 | ✅ 최후 생존자가 밸브를 마저 열어 게이트로 나가는 경로도 §6.3상 팀 승리(`TryRegisterEscape`가 페이즈 중 탈출에 `LastSurvivorEscaped`를 세운다) |
| `ReadyNetworkSync:67` 준비 토글 | `!= Lobby` 거부 | 무관 | ✅ |
| `RoundNetworkSync:270` Tick 분기 | `case InGame: TickRound` | `TickRound → TickDrain` | ✅ 배수구가 여기서 돈다 |
| `RoundNetworkSync:601/759` 탈출·배수구 RPC | `!= InGame` 거부 | 그대로 | ✅ |
| `RoundCoordinator:231/267` 클라 페이즈 추종 | 상태 매핑 | 값 변화 없음 | ✅ |
| `GameFlowManager` 전이표 | InGame → RoundEnd | 무변경 | ✅ |

**새 상태를 처리 못 하는 곳 0.** (새 상태 자체가 없다.)

### 4-A 페이즈

| 항목 | 기획서 | 구현 |
|---|---|---|
| 진입 조건 | 살아있는 도망자 1명 | `RunnerCensus.ShouldEnterLastSurvivorPhase`(블록 3) → `OnLastSurvivorPhaseEntered` |
| 제한 | 90초, 라운드 잔여가 짧으면 그쪽 | `EffectiveRemainingSeconds = min(round, phase)` — **페이즈가 라운드를 연장하지 않는다** |
| 진입 알림 | 활성 배수구에서 Shout 22m 1회 | `ServerEmitWorldPulse(Shout, 배수구 위치)` — **새 SoundType 없음** |
| HUD | 양쪽 표시 | 진입 시 라운드 잔여를 1회 SyncVar(`_phaseEntryRoundRemaining`)로 보내고 클라이언트가 센다(§14.3 매 프레임 금지) |

| 경계 | 라운드 잔여 | 페이즈 잔여 | 유효 | 결과 |
|---|---|---|---|---|
| 89초에 진입 | 89 | 90 | **89** | 89초 뒤 술래 승 |
| 300초에 진입, 89.9초 경과 | 210.1 | 0.1 | 0.1 | 진행 중 |
| 300초에 진입, 90.0초 경과 | 210 | 0 | **0** | 술래 승(라운드 잔여 210초 남음) |

### 4-B 배수구

| 항목 | 기획서 | 구현 |
|---|---|---|
| 배치 | 2개소 (31,21)·(8,9.5) | `DrainId` 1·2, 맵 v2 마커에 `DrainPoint` 자동 부착 |
| 활성 | 진입 시 무작위 1개, 양 진영 공개 | `DrainSelection.Choose(seed)` — Core 시드 난수(`UnityEngine.Random` 금지). `_activeDrain` SyncVar |
| 게이트 개방 상태 | 활성화 안 함 | `ShouldActivate(gateOpen)` → `ActiveDrain = 0`, HUD "출구 개방" |
| 작업 시간 | `14 − 개방 × 3` | `DrainConfig.WorkSeconds` — 하한 0 |
| 진행도 | 밸브와 동일(유예 3 + 감쇠 0.10/s) | **`Valve` 인스턴스를 그대로 내장** — 감쇠 상수 한 곳 |
| 통과 | 1.5초, 태그 가능 | 완료 → 통과 → `TryRegisterDrainEscape`. **통과 끝 시점에 서버가 역할을 다시 읽는다**(태그됐으면 Echo → 거부) |
| 수중 강제 파문 | 2.5초 주기, Valve 12m | `UnderwaterWorkPulse` — **배수구와 밸브 B·E가 같은 클래스** |

**T는 페이즈 진입 시점의 동시 개방 수로 1회 확정한다(GAP-87).** 페이즈 중에 역류로 밸브가 닫혀도
T가 늘지 않고, 밸브를 더 열어도 줄지 않는다.

| 진입 시 동시 개방 | T | 총 점유 | 게이지 12초 | 잔여 |
|---|---|---|---|---|
| 0 | 14 | 16 | **2회** | — |
| 1 | 11 | 13 | **2회** | — |
| 2 | 8 | 10 | 1회 | 2초 |
| (경계) 총 점유 = 게이지 | — | 10 vs 10 | 1회 | 0 |
| (경계) 총 점유 = 게이지 + 0.01 | — | 10 vs 9.99 | 2회 | — |

### 4-C 작업 자격 — "주장 보관 + 매 틱 재평가"

클라이언트는 **bool 하나**(누르고 있다/뗐다)만 보낸다. 서버는 거절하고 끝내지 않고 **주장을 보관한 채
매 틱** `DrainHatch.CanWork(역할, 범위, 잠수)`를 다시 판정한다. 처음엔 RPC에서 거리를 한 번 재고 거부했는데,
위치 동기화 지연으로 경계에서 한 번 거부되면 클라이언트는 값이 바뀔 때만 보내므로 **E를 다시 누를 때까지
영영 붙지 않는다** — 잠수 주장(`_diveIntent`)과 같은 패턴으로 바꿨다.

- **역할** — 태그되면 메아리 → 즉시 작업 해제.
- **범위** — `InteractionRules.InRange`, 수중 대상이므로 **수평 거리(GAP-88)**.
- **잠수** — `PulseNetworkSync.ServerIsSubmerged` = 숨 게이지가 쓰는 **바로 그 판정**. 수면에 뜬 채
  작업할 수 있으면 §6.5-2 "2회 잠수 필수"가 숨 0으로 무너진다.

### ★ 상수 중복 1건 제거 (더블체크 8)

블록 4 서버 경로를 처음 쓸 때 배수구 거리로 **2.0m를 새로 만들었다.** 그런데 밸브에는 이미 GAP-10
결정값 2.5m(`ValveInteractionController.DefaultInteractionRange`, Presentation)가 있었다. Net은
Presentation을 볼 수 없으므로 값을 **Core `InteractionRules.RangeMeters` 한 곳으로 옮기고** 밸브 상수가
그것을 가리키게 했다. 2.0m는 삭제. 테스트 `InteractionRange_IsSingleSource`가 고정한다.

### ★ §6.5-3이 잠수 모델과 충돌했다 — 태그 판정 위치 (GAP-89)

§6.5-3의 공정성 논거 전체가 *"수심 3.5m, 태그 반경 1.2m이므로 술래는 수면에서 배수구에 닿을 수 없다"* 이다.
그런데 블록 1-A③의 잠수 모델은 **머리(카메라)만 내리고 발(`transform`)은 수면 근처에 둔다.** 태그는
`transform` 위치로 재므로, 그대로 두면 **술래가 수면에 선 채 바로 위에서 배수구 작업자를 잡는다.**

`DiveRules.ContactPosition` — 서버 판정상 잠수 중인 대상은 **그 지점 바닥**에 있는 것으로 잰다.
`TagNetworkSync.ServerRequestTag`가 대상 위치를 이것으로 바꿔 넘긴다. 잠수 여부는 숨 게이지와 같은 서버 판정.

| 상황 | 술래 | 대상 | 거리 | 태그 |
|---|---|---|---|---|
| 메인 풀 배수구, 대상 잠수 | 수면 (31,−0.9,21) | 바닥 (31,**−3.5**,21) | 2.6 | ✗ (§6.5-3) |
| 같은 자리, 대상 부상 | 〃 | (31,−0.9,21) | 0 | ✓ "그때뿐이다" |
| 유아풀 본체 0.9m, 대상 잠수 | — | 발 = 바닥 | 변화 없음 | 기존과 같음 |

### ★ 블록 2의 enum 파급 누락 1건 — HUD 핍 3개

`InGameHud.MaxValvePips = 3`("§6.2상 맵당 최대 3개")이 v0.3 상수로 남아 있었다. v0.4는 **배치 5개**라
밸브 D·E가 HUD에 영영 안 그려졌다. `ValveRoster.PlacedCount`로 유도하게 바꿨다(잠금 밸브도 그려야
"어느 것이 잠겼나"가 보인다).

### 감쇠 색 구분 (생략 금지 항목)

배수구 진행도는 밸브와 같은 규칙이므로 HUD 페이즈 줄도 **작업 중 = 상호작용 색 / 감쇠 중 = 주황**으로
그린다. 밸브 핍과 **같은 색 상수**(`InGameHud.DecayColor`)를 쓴다.

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — `TickDrain`은 `TickRound`에서, `ApplyDrainIntents`는 `TickDrain`에서, 수중 밸브 파문은 `ValveNetworkSync.Update`에서, `DrainPoint`는 맵 v2 생성기가 부착 |
| 2 | 라운드 전이 초기화 | OK — `_drain`·`_drainIntent`·`_drainPulse`·SyncVar 4종을 `ServerResetForNewRound`에서, 밸브 수중 파문 타이머를 `ValveNetworkSync.ServerResetForNewRound`에서. 페이즈 진입 시에도 의사를 비운다 |
| 3 | 경계값 | OK — T 0/1/2 · 총 점유 = 게이지(1회)/+0.01(2회) · 파문 2.49/2.5 · 통과 1.4/1.5 · 수평 거리 2.5/2.51 · 페이즈 89/90 |
| 4 | 기존 테스트 의미 변화 | OK — 없음(신규만). `ValveInteractionController.DefaultInteractionRange`는 값 2.5 그대로 |
| 5 | 서버 권위 | OK — 클라이언트는 bool 하나. 역할·위치·잠수·활성·T·통과 전부 서버 |
| 6 | 캐시 금지 | OK — `DrainPoint`가 `LocalPlayerRegistry.Current`를 매 프레임 재조회 |
| 7 | 계층 경계 | OK — `DrainHatch`·`InteractionRules`·`ContactPosition`은 Core(Physics·FishNet 미참조), 레지스트리로 Net↔Presentation 연결 |
| 8 | 상수 중복 | **수정 후 OK** — 위 2.0m 삭제. 페이즈 90·통과 1.5·파문 2.5는 각각 한 곳 |
| 9 | enum 파급 | OK — `ValveState`·`GameFlowState`·`SoundType` 무변경. **블록 2 누락(HUD 핍 3) 발견·수정** |
| 10 | 문서 정합 | OK — 밸브 로그 문구 "진행도 0으로 리셋"(v0.3) → "3초 유예 후 감쇠" |

### 테스트

신규 `LastSurvivorDrainTests`(34). **44 클래스 / 859 통과 / 0 실패** (블록 3 종료 825 → 859).

### 전체 재검증

Core / Net / Presentation / Marco.Editor / Core.Tests — **오류 0 · 프로젝트 경고 0**.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **87** | §6.5-2 T를 **언제의** 동시 개방 수로 계산하는가 | 🟡 기획서 미명시. **페이즈 진입 시점 1회 확정.** 매 틱 재계산하면 역류가 작업 도중 T를 늘려 진행도 분모가 흔들린다 |
| **88** | 수중 대상(밸브 B·E, 배수구)의 상호작용 거리 | 🟡 잠수 모델에 수직 이동이 없어 3D로는 3.5m 바닥에 닿지 않는다. **수평 거리 + 잠수 중 조건**으로 둠. 밸브 B·E 쪽 서버 잠수 조건은 **아직 없다**(밸브 홀드는 서버가 거리도 재검증하지 않는다 — 기존 구조). 맵 v2 밸브를 실물화하는 블록 7에서 같은 패턴을 붙인다 |
| **89** | 잠수 중 몸의 수직 위치 | 🟡 기획서 미명시. §6.5-3을 성립시키려고 **태그 판정상 "잠수 = 바닥"**으로 둠 |
| **90** | **맵 v2에서 물에 들어갈 수 없다** | 🔴 수면 차폐판(`SurfaceOccluder`)이 기본 박스 콜라이더를 가진 채 수면 높이에 깔려 **플레이어가 물 위를 걷는다** — 발이 수면(y=0) 이상이라 `WaterVolumeRegistry`가 "덱"으로 판정해 잠수 자체가 성립하지 않는다. 반대로 콜라이더를 빼면 메인 풀 3.5m 바닥에 서서 **항상 잠긴 채 측벽에 갇힌다.** 해법은 "깊은 물에서 떠 있는 높이"(DiveRules 주석이 이미 전제)인데 그 값이 기획서에 없다. 성립 구간은 발 높이 **−1.62 < y < −0.5**(서면 머리가 수면 위, 잠수하면 아래). 풀 밖으로 나오는 계단/경사도 없다. **맵 v2를 플레이 씬으로 바꾸는 블록 7에서 처리** — 지금 플레이 씬은 구 그레이박스라 이 결함이 실기에 드러나지 않았다 |
| **91** | 진입·부상 시간이 시뮬레이션되지 않는다 | 🟡 머리 내리기 모델에는 하강·상승이 없다. 밸브 B의 실제 숨 소모는 **회전 5.0초뿐**이라 §6.1 "8.0초 소모(잔여 4.0) → 비명 억제 불가"가 실기에서 **잔여 7.0 → 억제 1회 가능**으로 바뀐다. 배수구도 T만 잠수한다. 기획서 수치(진입·부상 초)는 있지만 **강제 방법**(하강 중 조작 잠금? 부상 지연?)이 없어 창작하지 않았다 — 결정 필요 |

### 범위 밖으로 남긴 것

- 로컬 단독 실행의 페이즈 — 판정 권위가 서버에만 있어 `RoundCoordinator`는 비활성 값을 돌려준다.
- 맵 v2 물 지오메트리 수정(GAP-90)과 밸브 B·E 서버 잠수 조건(GAP-88) — 블록 7.
- 배수구 시각 표지(활성 배수구 강조) — §6.5-2 "양 진영 공개"는 HUD 문구로만 충족. 블록 7 최소 연출.

---

## 프로토타입 블록 5 — 질주 스태미나 · 술래 잠행 · 캠핑 방지

기획서 §3.1 · §3.6 · §4.3 · §5.1 · §8.2 · §10.2를 다시 읽고 시작했다.

### ⛔ 지시와 기록의 충돌 1건 — 실행하지 않고 기획서를 따랐다 (공통 규칙 8)

지시서 5-A: *"A↔C 64.1 · A↔D 60.7 · A↔E 45.3 · B↔C 41.5 네 쌍이 1회로 도달 불가능해야 한다."*
그런데 **블록 1-D가 §10.2 표를 C# 배치 검증 툴 실측값으로 교체했다**(계단이 실제 지오메트리로 들어옴).
정본 값은 A↔C **60.1** · A↔D **59.3** · A↔E **45.6** · B↔C **35.3**이고, §10.2 본문도
*"A↔C · A↔D · A↔E는 1회로 도달 불가능"* 으로 **B↔C를 이미 뺐다**(35.3 < 37.5).

→ **B↔C 단언은 실행하지 않았다.** 정본 3쌍만 테스트로 고정했다. 지시서의 4개 수치는 v0.4 이전
파이썬 근사(계단 12m 페널티) 값이다. B↔C가 1회 질주로 닿는 것이 설계상 문제인지는 결정 사항이다.

### 5-A 질주 스태미나 (§3.1)

| 항목 | 기획서 | 구현 |
|---|---|---|
| 총량 | 5초 | `SprintConfig.StaminaSeconds` |
| 소진 페널티 | 3초간 -20% | `ExhaustPenaltySeconds` · `ExhaustSpeedMultiplier` — 걷기 5.0 → **4.0** (§10.2-1 "소진 페널티 3초(12m)"와 일치) |
| 1회 사거리 | 7.5 × 5 = 37.5m | `SingleSprintRangeMeters` — **유도값** |
| 소모 | — | **실제로 질주한 틱에만**(이동 중 + Shift + 도망자). 서서 Shift는 소모 없음(§4.3) |
| 회복 | **기획서에 없음** | **GAP-92 잠정** — 소모와 같은 1:1, 페널티 3초 동안 회복 없음 |

**질식(§5.9-1)과 소진(§3.1)이 겹칠 때 — GAP-93 결정: 곱하지 않고 느린 쪽 하나(0.8).**
근거 — §9.4 "이중 보정 금지"는 같은 성질의 보정을 두 번 거는 것을 금지한다. 두 페널티는 원인만 다르고
성질(이동속도 -20%, 3초)이 같다. 곱하면(0.64) 두 절 어디에도 없는 -36%가 생긴다. 각자 타이머는 따로 센다.

**상수 재사용 판단(더블체크 8)**: 소진 -20%·3초는 질식 -20%·3초와 **값이 같지만 공유하지 않았다.**
기획서의 다른 절에 따로 적힌 규칙이고 테스트 범위도 따로 조정된다 — 한쪽을 튜닝할 때 다른 쪽이
딸려 움직이면 그것이 사고다. "같은 값"이 아니라 "같은 규칙"일 때만 공유한다.

### 5-B 술래 잠행 (§3.1)

| 확인 항목 | 결과 |
|---|---|
| 입력(홀드/토글) | §4.3에 **잠행 키가 없다** → **홀드, Left Ctrl 잠정(GAP-94)**. 술래는 질주(Shift)·잠수(Ctrl)를 쓰지 않으므로 Ctrl이 비어 있다 |
| 3.5가 인원 보정을 받는가 | 명시 없음 → **고정(GAP-84, 블록 3에서 기록)**. 2·5·6인·종반 압박 전부 3.5 |
| 발소리가 2m로 내려가는가 | **예, 별도 분기 없이.** §5.1 판정이 속도 기준이라 3.5 ≤ 5.0 → 걷기 2m가 자동 선택된다. 잠행을 풀면 5.30 > 5.0 → 질주급 6m |

### 5-C 캠핑 방지 (§3.6)

`CampingMonitor`(Core) — **서버가 소유한다.** 위치는 서버 관측값, 이동거리는 틱별 위치 변화의 합.

| 항목 | 기획서 | 구현 |
|---|---|---|
| 발동 | 20초 누적 3m **미만** | 20초 슬라이딩 윈도우(중단 시간 제외). 2.99 발동 / 3.00 미발동 |
| 1단계 | 3m / 0.6초 | 발동 즉시 1회 |
| 증폭 | 15초마다 +2m | 20→3m · 35→5m · 50→7m · 65→9m(§3.6 "캠핑 최대 65초~") |
| 최대 | 9m | 이후 15초마다 9m 유지 |
| 초기화 | 5m **이상** | 발동 후 누적 이동 4.99 유지 / 5.00 초기화 → 20초를 처음부터 |
| 미발동 | 밸브 / 잠수 / 메뉴 / 이동불가 연출 | **정지(초기화 아님)** — 중단 중에는 시간·이동 둘 다 세지 않는다 |
| 적용 | 도망자 + 술래 | `CampingConfig.AppliesTo` — 메아리 제외 |

**일시중단 조건의 권위**

| 조건 | 누가 아는가 | 처리 |
|---|---|---|
| 잠수 중 | 서버(숨 판정) | `DiveRules.CanDive(role) && ZoneOf == Submerged` |
| 밸브 조작 중 | 서버 | `ValveNetworkSync.ServerIsInteracting` |
| 메뉴(F1) 개방 | **클라이언트만** | `SubmitMenuIntent(bool)` 주장 — **GAP-95** |
| 이동불가 연출 | 서버(술래 경직) | `TagNetworkSync.IsSeekerStunnedOnServer`. 낙하 복구·스폰은 텔레포트가 5m 이동으로 세어져 **초기화**된다(아래) |

### ★ "술래 물속 대기 = 캠핑 방지 발동"이 성립하는가 — **성립한다(수정 후)**

**블록 5 이전에는 성립하지 않았다.** `DiveRules.IsDiving`에 역할 조건이 없어 **술래도 물에서 Ctrl을 누르면
잠수했다.** 그러면 ① §3.6 "잠수 중 미발동"에 걸려 술래의 캠핑 방지가 꺼지고 ② §6.5-3 "술래에게 잠수를
주지 않는다"가 정면으로 깨진다 — 최후 생존자 페이즈가 술래의 일방적 승리가 된다.

수정 3중:
1. **§3.1 특수 능력 행 — 잠수는 도망자 능력.** `IsDiving(role, …)`에 `CanDive(role)`을 넣었다. 판정식이
   한 곳이라 클라이언트 시뮬레이터·서버 숨 판정이 함께 막힌다(테스트가 역할×입력 24조합 동일성을 확인).
2. **캠핑 중단의 "잠수 중"도 `CanDive(role)`을 요구한다.** 지오메트리만 보면 깊은 물에 선 술래의 머리가
   수면 아래로 판정될 수 있는데(GAP-90), 그 경로로도 술래의 중단이 **원천적으로 불가능**하다.
3. `CampingConfig.AppliesTo(Seeker) == true` — 테스트 고정.

따라서 술래가 물에 서 있든(수면 상태) 물 밖이든 잠수 판정은 거짓이고, 20초 뒤 호흡음이 난다.
**실기 확인은 맵 v2 물 지오메트리(GAP-90) 해결 뒤에 가능하다** — 지금 맵 v2에서 술래는 수면 차폐판 위에 선다.

### ★ Breath enum 파급 (더블체크 9) — 기존 구멍 2개를 함께 닫았다

`SoundType.Breath`는 **맨 끝에** 추가했다(중간에 넣으면 뒤따르는 정수값이 밀려 네트워크로 오가는 기존 종류가
다른 소리로 읽힌다 — 테스트 고정).

| 읽는 곳 | Breath 처리 | 근거 |
|---|---|---|
| `ServerPulseDriver.TryGetPulseSpec` | 3m / 0.6초(시작값). 증폭 반경은 `AddPulse(radiusOverride)` | §5.1 "3m → 9m" |
| `ServerPulseDriver.IsServerOnly` (신설) | **클라이언트 주장 거부** | 서버가 관측한 결과 |
| `DirectionGaugeRules.TriggersGauge` | false (변경 없음 — Talk·Shout만) | §5.1 표 ✗ |
| `FootstepMaterialRules.AppliesTo` | false (변경 없음 — Walk·Sprint만) | 발소리가 아니다 |
| `AwardTally.CountsTowardNoise` | **true** | §8.2 제외 목록은 밸브뿐 — **GAP-96** |
| `AwardTally` 비명 집계 | 해당 없음 | `Scream`만 센다 |
| Presentation 렌더러 | 종류별 분기 없음 | — |

**발견**: `IsServerOnly`를 만들며 보니 **`Scream`·`Knock`도 클라이언트가 `SubmitPulse`로 보낼 수 있었다.**
정당한 클라이언트 경로는 발소리·밸브·음성뿐이다. 그대로 두면 ① 자기 비명을 위조해 §8.1 최다 비명상 오염,
② 노크 전용 경로(역할·5회·20초 잠금·쿨다운)를 통째로 우회한다. **같은 목록에 넣어 닫았다**(GAP-24 원칙).

### 무성 생존상(§8.2)에 호흡음이 들어가는가 — 들어간다(GAP-96)

§8.2는 *"본인이 발생시킨 파문 … 단, 밸브 회전음은 제외"* 이고 제외 사유가 *"목표 수행을 처벌하지 않기 위해"* 다.
캠핑은 목표 수행이 아니며 §3.6의 목적(무기한 정지 억제)과도 방향이 같다. 명시가 없어 GAP으로 남긴다.

### 관찰 — §3.6 "제자리 흔들기로는 3m를 채울 수 없다"

판정은 기획서 문자 그대로 **누적 이동거리**다. 좌우로 실제 스트레이프하면 거리는 쌓이지만, 그 경우 §5.1-1대로
**2m마다 걷기 발소리**가 나므로 무음 회피는 아니다. 기획서의 문장은 스텝 미만의 떨림(회전·미세 입력)을 뜻하는
것으로 읽었고, 코드는 그 문장을 깨지 않는다(제자리 회전은 이동 0).

### 라운드 전이 초기화 (더블체크 2)

- 서버: `_camping`·`_campingLastPos`를 InGame 진입 시 비운다 — **위치도 버려야** 스폰 이동이 "5m 이동"으로
  세어지지 않는다. `_menuOpen`은 `OnStartServer`에서.
- 클라이언트: 역할이 같으면 시뮬레이터가 다시 만들어지지 않으므로(`ApplyRole`) `PawnPhaseTeleporter`가 새 라운드
  배치 시 `ResetLocomotionForNewRound()`로 스태미나를 만충으로 되돌린다.

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — `TickCamping`은 `Update`, 스태미나는 `LocomotionSimulator.Tick` 3분기 전부(잠수·정지·이동), 메뉴 주장은 `ApplyMovement`, 리셋은 `PawnPhaseTeleporter` |
| 2 | 라운드 전이 초기화 | OK — 위 절 |
| 3 | 경계값 | OK — 4.99/5.00 소진 · 2.99/3.00 페널티 · 2.99/3.00 발동 · 4.99/5.00 초기화 · 19.5/20 · 창 만료 20.0/20.5 |
| 4 | 기존 테스트 의미 변화 | **2건** — `IsDiving_MatchesDesignDoc`(역할 인자 추가, 도망자 기준 의미 동일), `LocomotionSimulator_UsesTheSameDivePredicate`(역할 전 조합으로 확장). 판정식이 바뀐 것이므로 **의미가 넓어졌고 기존 기대값은 그대로**다 |
| 5 | 서버 권위 | OK — 캠핑 판정·호흡음 발생은 서버. 클라이언트 주장은 메뉴 bool 하나. Breath·Scream·Knock 클라이언트 경로 차단 |
| 6 | 캐시 금지 | OK — 새 `LocalPlayerRegistry` 캐시 없음. 브릿지 탐색은 `ResolvePulseBridge`로 한 곳 |
| 7 | 계층 경계 | OK — `SprintStamina`·`CampingMonitor`는 Core(Time·Physics 미참조, dt 주입) |
| 8 | 상수 중복 | OK — 소진 -20%/3초를 질식 상수와 **의도적으로 분리**(위 근거). 37.5는 유도 |
| 9 | enum 파급 | OK — Breath 7곳 전수 표. **기존 구멍 2개(Scream·Knock) 발견·수정** |
| 10 | 문서 정합 | OK — §6.5-3이 캠핑 방지를 전제한다는 사실을 `TickCamping` 주석에 명시. 지시서의 구 거리 수치 4개는 기획서 정본으로 대체(위 충돌 절) |

### 테스트

신규 `SprintStealthCampingTests`(43), `WaterVolumeTests` 2건 갱신 + 역할 테스트 3건.
**45 클래스 / 905 통과 / 0 실패** (블록 4 종료 859 → 905). 첫 실행에서 1건 실패 — 4.99 + 0.01 부동소수
누적으로 잔량 1e-7이 남아 한 프레임 더 질주했다. 설계 수치가 아니라 수치 오차라 1e-4 허용오차로 처리했다.

### 전체 재검증

Core / Net / Presentation / Marco.Editor / Core.Tests — **오류 0 · 프로젝트 경고 0**.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **92** | §3.1 질주 스태미나 **회복 규칙**이 없다 | 🟡 잠정: 소모와 같은 1:1, 소진 페널티 3초 동안 회복 없음(0→만충 5초) |
| **93** | 질식 페널티와 소진 페널티가 **겹칠 때** | 🟡 결정: 곱하지 않고 느린 쪽 하나(0.8). 근거 §9.4 이중 보정 금지 |
| **94** | §4.3에 **술래 잠행 키가 없다** | 🟡 홀드, Left Ctrl 잠정(술래가 쓰지 않는 도망자 잠수 키) |
| **95** | §3.6 "메뉴 개방 중" 미발동을 서버가 검증할 수 없다 | 🟡 클라이언트 주장으로 받음. 악용 시 메뉴를 연 채 서 있어야 해 시야·조작이 없다 |
| **96** | §8.2 무성 생존상에 **호흡음이 들어가는가** | 🟡 명시 없음. 제외 목록(밸브)에 없으므로 **포함** |

### 범위 밖으로 남긴 것

- 스태미나 HUD · 잠행 FOV −3° · 소진 화면 수축 · 호흡→떨림→기침 SFX — 블록 7(HUD 기능 위주 · 연출 최소).
- 캠핑 당사자 자신에게 "지금 숨소리가 난다"를 알리는 표시 — 서버 파문은 GAP-1로 본인에게 오지 않는다. 블록 6 Afterglow(자기 파문)와 함께 판단.
- 낙하 복구·스폰 텔레포트는 5m 이동으로 세어져 캠핑을 **초기화**한다(중단이 아니라). 서버가 텔레포트를 구분할 신호가 없다.
- B↔C 1회 질주 도달(35.3m) — 위 충돌 절. 설계 판단 필요.

---

## 프로토타입 블록 6 — 메아리 소나 · 잔상 · 찰칵이

기획서 §3.2-1 · §3.2-2 · §7 · §16.4 · §12.4, 연출.md §7 · §9를 다시 읽고 시작했다.

### ★ 선행 발견 1 — 네트워크에서 **자기 파문이 자기 화면에 안 보였다**

GAP-1은 *"본인 발생 펄스는 서버 경로로 절대 생성되지 않는다 — 본인 목소리는 항상 로컬 0ms 렌더 경로로만
처리된다"* 인데, 네트워크 모드의 발소리·밸브·음성은 `SubmitPulse` 후 **바로 return**했다. 로컬 판정 경로는
네트워크에서 꺼지고(이중 판정 방지), 서버는 GAP-1로 본인에게 돌려주지 않으니 **본인은 자기 파문을 전혀 보지
못했다.** "말해야 보인다"가 멀티플레이에서 성립하지 않았던 것이다. §16.4 잔상의 대상이 "자기 SoundPulse"라
이 경로 없이는 잔상을 붙일 곳 자체가 없었다.

→ `SelfPulseFeed`(Presentation) 신설. 발소리(재질 배율 적용 반경)·밸브·음성(§5.1 표 반경)이 **발생 반경 그대로**
0ms로 자기 화면에 그려진다(인지 배율 없음 — GAP-1 문구). **표시 전용**이며 판정 권위와 무관하다.

### ★ 선행 발견 2 — 메아리의 말소리가 생존자 화면에 파문으로 떴다

§3.2 "음성: 생존자에게 들리지 않음" · 메아리의 소리는 노크뿐인데, `ServerSubmitPulse`와 음성 파이프라인
어디에도 역할 검사가 없었다. **서버에서 메아리 호출자의 파문을 거부**하고(역할은 서버가 호출자에서 읽는다),
클라이언트도 보내지 않게 했다.

### 6-A 메아리 소나 — **서버가 판정해 보내는 구조**로 결정

| 선택지 | 판정 |
|---|---|
| 클라이언트가 "메아리는 다 본다"로 스스로 그리기 | ✗ — 모든 파문의 원본 좌표가 **모든 클라이언트에** 있어야 한다. 그 순간 술래·도망자 클라이언트에도 좌표가 실려 GAP-2(좌표 비공개)가 무너진다 |
| **서버 판정기에 메아리 분기** | ✅ — `SoundPulseResolver.Resolve`에서 청취자가 메아리면 거리·차폐를 건너뛰고 원본(반경·지속·좌표)을 돌려준다. 기존 청취자별 판정·개별 전송 구조를 그대로 타므로 **메아리에게만** 원본이 가고 나머지는 지금과 같다 |

- **발생자 식별 불가**: 전송 페이로드(`PulseDelivery`)에 발생원 ID가 애초에 없다. 렌더 색도 §16.2 환경 파문색
  (#E8E8E8, 채도 0) 하나 — 역할색을 쓰면 발생자가 드러난다.
- **표시 시간 = 본래 지속**(역할 배율 1.0). 차폐 레이도 쏘지 않는다(테스트 고정).
- **정보량 제한은 클라이언트 표시에서**(`EchoSonarView`): 살아 있는 동안 밝기 1.0, **소멸 후 3초 선형 감쇠**,
  **최근 8개** — "최근"의 기준은 파문 발생 시각이고 잔류 흔적도 자기 발생 시각으로 줄을 선다. 오래된 것부터 버린다.
- **렌더러를 새로 만들지 않았다.** 기존 `PulseVisualRenderer`에 메아리 시야 모드를 넣었다(로컬 역할을 매 프레임
  재조회 — 태그 순간 전환).
- **8방위 나침반**(§3.2-2): HUD 가장자리에 아주 흐린 N~NW 8글자, 카메라 yaw로 회전. 위치·지형·러너 정보 없음.
- 자기 실루엣 발광(§3.2-2)은 1인칭이라 본인 몸이 거의 보이지 않아 블록 7 캐릭터 색(메아리 #B79CFF)으로 대신한다.

> **정보량 제한을 서버에서 하지 않은 이유**: 상한(8개)과 잔류(3초)는 **공정성이 아니라 가독성** 규칙이다
> (§3.2-1 "소리로 그린 지도를 갖지 못하게"). 서버가 8개만 보내도 변조 클라이언트는 받은 것을 누적할 수 있어
> 보안상 이득이 없고, 받는 것 자체(메아리에게 전부)는 §3.2-1이 허용한 범위다.

### 6-B 잔상(Afterglow) — **차선책 8% / 8초로 확정**(사용자 지시)

| 판단 | 결과 |
|---|---|
| 모서리 검출 방식 | 현재 렌더러는 LineRenderer **링** 기반이고 지형 셰이더가 없다. 에지 포스트프로세스·파문 셰이더는 §16.3 "셰이더 1개로 수렴" 원칙상 새 파이프라인이 된다 → **시도하지 않음**(지시) |
| 구현 | 자기 파문이 **끝까지 퍼진 순간**(자연 만료) 그 최종 반경에 링을 남긴다. 8% → 0% 선형, 8초 |
| 대상 | **자기 파문만** — `SelfPulseFeed` 경로의 ID(음수)만 잔상을 만든다. 타인 파문·조기 소실(차폐 발생)은 잔상 없음 |

`PulseVisualRegistry`에 `VisualExpired`(자연 만료 직전 상태)를 추가했다 — 조기 소실(Disappeared)과 구분해야
"끝까지 퍼진 뒤의 잔상"과 "소나 잔류"가 성립한다.

### 6-C 찰칵이 — 파문과 **완전히 분리된** 서버 경로

| 항목 | 기획서 | 구현 |
|---|---|---|
| 소지 | 1회용, 동시 1개 | `ServerClickerDriver` — 쓰기 전 재줍기 거부 |
| 섬광 | 6m · 0.3초 | 링 + 실제 점광원(0.3초 페이드). **SFX 없음, 파문 없음** |
| 리스폰 | 맵당 4곳, 120초 | 주운 순간부터 120초. 가용 비트마스크 SyncVar(전원 공개) |
| 잔상 연동 | §16.4 | 자기 섬광만 6m 잔상 |
| 보이는 사람 | 시야 닿는 6m | 서버가 판정 — **3D 6m 이내 + 차폐 레이 벽 0장**(GAP-2 "좌표 공개" 기준과 동일). **메아리 제외**(소나는 파문만, 섬광은 파문이 아니다) |
| 입력 | Q / 우클릭 | 플레이어 컨트롤러(프리팹)에 둠 — 씬 변경 없음 |
| 줍기 | §4.3 E(홀드) | E **누름** — 홀드 시간이 기획서에 없다(GAP-99) |

`ClickerNetworkSync`는 `ServerPulseDriver`·`SoundType`·§8 어워드를 **하나도 참조하지 않는다** — 참조가 생기는 순간
이 아이템의 존재 이유가 사라진다(연출.md §9.1). 술래의 방향 게이지·메아리 소나에도 나타나지 않는다.

소지 중 태그되면 소지를 잃는다(메아리는 물리 상호작용 불가, GAP-5). 서버가 태그 이벤트를 구독해 처리한다.

씬 배치: `Tools/MARCO/Setup Network Round`가 `RoundCoordinator`의 NetworkObject에 함께 부착한다 — **블록 7에서
셋업 파이프라인 재실행 + Reserialize 필요**. 리스폰 지점 4곳은 맵 v2 생성기가 `ClickerSpawnPoint`를 붙인다.

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — 소나 분기는 서버 판정 경로 안, 자기 파문 피드는 발소리·밸브·음성 3곳, 잔상은 `VisualExpired`, 찰칵이 입력은 컨트롤러 `ApplyMovement`, 줍기는 마커 `Update` |
| 2 | 라운드 전이 초기화 | OK — 찰칵이: InGame 진입 시 `Reset` + `ObserversResetHolding`. 소나 잔류: 메아리 시야 전환 시 비움. 잔상은 8초 자연 소멸 |
| 3 | 경계값 | OK — 잔상 7.99/8 · 잔류 2.99/3 · 상한 8/9번째 · 리스폰 119.9/120 · 섬광 6.00/6.01 · 스폰 번호 -1/4 |
| 4 | 기존 테스트 의미 변화 | OK — 없음. `PulseVisualRegistry`는 이벤트 추가만(기존 `VisualRemoved` 순서·횟수 동일) |
| 5 | 서버 권위 | OK — 소나는 서버 판정 결과만 표시. 찰칵이 역할·거리·가용·소지·섬광 대상 전부 서버. **메아리 파문 거부 추가** |
| 6 | 캐시 금지 | OK — 렌더러·HUD·마커·음성 전부 `LocalPlayerRegistry.Current` 매 프레임 재조회 |
| 7 | 계층 경계 | OK — `ServerClickerDriver`·레지스트리·브릿지 계약은 Core. Net↔Presentation은 `ClickerClientEvents`로만 |
| 8 | 상수 중복 | OK — 섬광 반경은 `ClickerConfig` 한 곳(렌더러·서버 공용), 줍기 거리는 `InteractionRules` |
| 9 | enum 파급 | OK — 새 enum 값 없음(`ClickerPickupResult`는 신규 타입). `SoundType` 무변경 |
| 10 | 문서 정합 | OK — `ResolvePulseColor` 주석의 "발생원은 로컬 러너뿐" 전제는 소나 모드에서 환경색으로 분기 |

### 테스트

신규 `SonarAfterglowClickerTests`(29). **46 클래스 / 934 통과 / 0 실패** (블록 5 종료 905 → 934).

### 전체 재검증

Core / Net / Presentation / Marco.Editor / Core.Tests — **오류 0 · 프로젝트 경고 0**.
FishNet RPC 위빙은 Unity 안에서만 일어나므로 `ClickerNetworkSync`의 RPC 동작은 **실기 검증 항목**이다.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **97** | (블록 5 이관) §10.2 **B↔C 경로 35.3m가 1회 질주 사거리 37.5m 안**이다 | 🟡 지시서는 "도달 불가"를 요구했으나 정본(C# 실측)과 충돌해 실행하지 않았다. 기획서는 이미 A 관련 3쌍만 주장한다. B↔C가 1회 질주로 닿아도 되는지는 설계 결정 |
| **98** | §7 찰칵이를 **누가** 주울 수 있는가 | 🟡 명시 없음. **도망자 전용** 잠정(메아리 GAP-5 확정 제외, 술래는 §3.1 특수 능력에 아이템 없음, §19가 도망자 참여 경로로 둠) |
| **99** | §4.3 "아이템 줍기 E(홀드)"의 **홀드 시간** | 🟡 수치 없음. **누르는 즉시** 줍기 |

### 범위 밖으로 남긴 것

- §16.4 본안(지형 모서리 · 15% · 12초) — 사용자 지시로 시도하지 않음.
- 찰칵이 섬광의 "모서리만 밝아진다"(연출.md §9.1) — 점광원이 면을 비춘다. 모서리 렌더와 같은 이유로 보류.
- §8 질문 8번(최후 생존자 페이즈에 메아리 관전이 지루하지 않은가) — 플레이테스트 관찰 항목. `docs/수동검증_절차.md`에 넣는다(블록 7).
- 캠핑 당사자에게 자기 호흡음 표시 — 서버 발생 파문은 `SelfPulseFeed`를 거치지 않는다. 필요하면 서버가 소유자에게
  TargetRpc로 알려야 한다.

---

## 프로토타입 블록 7 — 연출 · 캐릭터 · HUD · 브리핑 · 통합 · 빌드 준비

기획서 §12.3 · §12.4 · §14.3 · §16.1 · §16.2 · §16.4, 연출.md 전체를 다시 읽고 시작했다.
사용자 지시 범위: 캐릭터 = 캡슐 + 역할 색 / 잔상 = 8%·8초 / 연출 = bob 동기화 · 태그 · 밸브 색만 / 브리핑 = 평면도 + 활성 밸브 점 / HUD = 기능 위주.

### ★ 통합 중 발견한 기존 결함 4건 (전부 수정)

| # | 결함 | 영향 | 수정 |
|---|---|---|---|
| 1 | **네트워크에서 밸브 회전 파문이 없었다.** 네트워크 경로(`ValveInteractor.TickNetworked`)는 홀드 의사만 보내고 소리를 내지 않았다 | §6.1 "밸브 작업 = 술래를 부를지 재는 도박"이 멀티플레이에서 통째로 사라짐 | 서버가 **홀드 수락 순간** 밸브별로 발생(`ValveNetworkSync.EmitRotationPulse`) — 발생원은 돌리는 사람, 위치는 밸브 |
| 2 | 밸브 A **×0.5가 어느 경로에도 없었다**(`ValveOccupancy.SoundRadiusMeters`는 테스트만 사용) · 서버 표 지속이 **v0.3 3초** | §6.1 A의 기계실 리스크 설계 · §5.1 [v0.4] "각 밸브 회전 시간" 불일치 | 반경 A 6m / 나머지 12m, 지속 = 밸브 회전 시간(A·C·D 8 / B 5 / E 7). 로컬 경로도 같이 |
| 3 | **리매치 라운드가 게이트 열린 채 시작**. latch가 씬 로드/언로드에서만 풀렸는데 리매치는 맵을 유지한다 | 두 번째 라운드부터 밸브 없이 탈출 가능 | latch 규칙을 Core `EscapeGateLatch`로 옮기고 **라운드 번호가 바뀌면** 푼다 |
| 4 | **맵 v2에서 물에 들어갈 수 없었다**(GAP-90). 수면 차폐판이 고체라 물 위를 걸었다 | 잠수 · 수중 밸브 B·E · 배수구 · §6.5-3 전부 실기 불가 | 수면판 **트리거**(차폐 레이는 트리거 포함으로 변경) + 수영 바닥 −0.9m + 가장자리 경사로 |

### 7-A 캐릭터 (§16.1)

캡슐 몸체에 역할 색을 프로퍼티 블록으로 칠한다 — 도망자 `#7FE7E0` / 술래 `#EF6A4C` / 메아리 `#B79CFF`(알파 0.5).
역할이 바뀌면(태그) 즉시 다시 칠한다. 메아리 은닉은 기존 `EchoVisibility`가 계속 담당(생존자에게 렌더링 안 됨).
메아리 반투명은 머티리얼이 불투명이면 알파가 먹지 않는다 — 메아리끼리만 보이므로 영향이 작다(범위 밖 참조).

### 7-B 연출 (지시 범위만)

| 항목 | 구현 |
|---|---|
| ① **bob ↔ 파문 동기화**(연출.md §2.1, 기능) | bob 위상 = 시뮬레이터의 **발소리 거리 위상**(`StepPhase01`). 오프셋 `진폭 × sin(π·위상)` → 위상 0 = 파문이 난 순간 = 가장 낮은 점. 주기를 시간으로 따로 세지 않으므로 **구조적으로 어긋날 수 없다**. 진폭: 걷기 소 · 질주 대 · 잠행 최소 · 메아리 없음 |
| ② **태그 연출**(§4.2 타임라인) | 잡힌 도망자: 0 백색 → 0.15 암전 시작 → 0.50 완전 암전 → 3.00 해제. 술래: 적색 `#EF6A4C` 플래시(0.15 최대 · 0.50 소멸) + FOV −5° punch(1.0 복귀) + **경직 1.0초 이동 정지**(GAP-85 해소) |
| ③ 밸브 색 | 블록 2·4 그대로(회전 앰버 / **감쇠 주황** / 역류 점멸 / 잠금) — 배수구 줄도 같은 두 색 |
| ⑦ 상태별 FOV | 질주 +5° · 잠행 −3°(0.2초 보간) |
| ④⑤⑥ · 잠수 로우패스 · 역류 데칼 · 탈출 페이드 · 종료 파문 | **최소 — 생략**(지시). 연출.md §11 금지 7종 위반 없음 |

### 7-C HUD (§12.4, 기능 위주)

| 요소 | 구현 |
|---|---|
| 밸브 카운트 | "⚙ 동시 개방 / **요구**(2~3, §6.2 유도)" — 감소 가능(블록 2) |
| 밸브 슬롯 | **활성 개수만큼**(3~4칸). 잠긴 밸브는 슬롯 없음 — 블록 2의 "배치 5칸" 표시를 §12.4대로 고쳤다 |
| 최후 생존자 | 페이즈 잔여 + 활성 배수구 이름 + 진행도 + **카메라 기준 방향 화살표**(양 진영 공통) |
| 숨 게이지 | 하단 중앙, 도망자 · 잠수 중이거나 덜 찼을 때. 억제 비용(4.5) 미만이면 주황. **서버 값**(GAP-76 해소 — 소유자 전용 TargetRpc, 0.25초 해상도 · 상태 변화 · 경계값에서만 전송) |
| 스태미나 · 아이템 · 나침반 | 블록 5·6 |
| 점수판 | "탈출 ●○ / 요구 2" — 칸 수는 §6.2에서 유도(상수 없음) |

### 7-D 로비 브리핑 (§12.4) — **새 GameFlowState 없이**

§15.4 RoleAssign(3초 카운트다운 · 역할 배정) → 맵 로드 완료 → **브리핑 30초(RoleAssign 하위 구간)** → InGame.
블록 4의 최후 생존자 페이즈와 같은 이유로 상태를 추가하지 않았다(`!= InGame` 게이트 전체가 새 상태를 알아야 한다).

- **활성 밸브 선택을 브리핑 시작으로 옮겼다**(전에는 라운드 시작) — 평면도가 "이번 라운드의 활성 밸브"를 보여줘야 한다.
  밸브 초기화(`ServerResetWorld`)는 그보다 앞선 전이에서 끝나므로 선택이 지워지지 않는다.
- 평면도 원천: 맵 v2 생성기가 루트에 `MapPlanData`(§10.1 구역 13 + 수면 2 좌표 복사)를 붙인다 — 런타임은 에디터 어셈블리를 못 읽는다.
- 잔여 시간은 **시작 시 1회**만 보내고 클라이언트가 센다(§14.3).
- 브리핑 30초 동안 **이동 잠금**(GAP-100). 술래 격리 3초는 브리핑 **뒤** 라운드 시작에 건다(전에는 배치 시점이라 브리핑 중에 소진됐을 것).
- 오프닝 가이드(첫 20초): 걷기 → 속삭이기 → "말해야 보입니다". 자기 파문 피드로 단계가 넘어간다.

**ServerPhase 구독처 재판정(브리핑 추가분)**: 브리핑은 RoleAssign 안이므로 `!= InGame` 게이트가 전부 닫힌 상태 그대로다 —
밸브·태그·노크·외침 불가(의도대로), 음성 파문은 로비와 같이 허용(§12.3 튜토리얼). 새 상태를 처리 못 하는 곳 0.

### 7-E 통합 시나리오

| # | 시나리오 | 방법 | 결과 |
|---|---|---|---|
| 1 | 정상 클리어(5인, A·B·C·D, 요구 3) — A→C→B 동시 3 → 게이트 → A 역류 Closed(게이트 유지) → 2명 탈출 | EditMode `Scenario1_NormalClear_FivePlayers` (실제 Valve·Latch·ServerRoundDriver) | ✅ RunnersWin |
| 2 | 감쇠 — 5초 후 중단 → 유예 → 감쇠 → 8초 뒤 이어받기 → 완료 / 13초 방치 전손 | `Scenario2_*` 2건 | ✅ 0.625 → 0.125 → 7초 완료 · 13초 0 |
| 3 | 역류 실패 — t=188 Reflowing → t=218 Closed → 게이트 미개방 → 시간 종료 | `Scenario3_*` | ✅ SeekerWin |
| 4 | 봉쇄 무력화 — 어느 밸브 하나를 지켜도 나머지로 게이트 | `Scenario4_*` 2~6인 × 활성 전부 | ✅ 전 조합 |
| 5 | 최후 생존자(4인) — 2명 태그 → 페이즈 → 배수구 T=14 → **2회 잠수** → 통과 → 팀 승리 / 술래 풀가 대기 20초 호흡음 | `Scenario5_*` 3건(실제 BreathGauge·DrainHatch·CampingMonitor로 잠수 정책 시뮬레이션) | ✅ 90초 안 탈출 · 잠수 정확히 2회 · 술래 20초에 3m |
| 6 | 전체 루프(말하기→파문→잔상→게이지→추격→태그→소나→노크) | 네트워크·렌더링 필요 | ⏳ **실기 항목**(수동검증 §31) |

### 7-F 문서 정합 전수 대조표

| § | 항목 | 기획서 | 코드(정본 위치) | 판정 |
|---|---|---|---|---|
| 3.1 | 도망자 걷기 / 질주 / 메아리 | 5.0 / 7.5 / 6.0 | `LocomotionConfig` | ✅ |
| 3.1 · 6.2 | 술래 기본 × 보정 | 5.0 × (2·3인 +5 · 4인 +4 · 5인 +6 · 6인 +8%, 하한 +1%) | `LocomotionConfig.SeekerSpeedFor` | ✅ |
| 3.1 | 잠행 | 3.5 (발소리 2m) | `SeekerStealthSpeed` + 속도 판정 | ✅ |
| 3.1 | 질주 스태미나 | 5초 · 소진 3초 −20% | `SprintConfig` | ✅ (회복 GAP-92) |
| 3.1 | 태그 반경 · 술래 청취 | 1.2m · ×1.2 / ×1.5 | `TagRules` · `SoundPulseResolver` | ✅ |
| 3.1 | 외침 · 노크 | 45초 · 선딜 1초 / 25초 · 5회 · 20초 잠금 | `SeekerShoutConfig` · `KnockConfig` | ✅ |
| 3.1-1 | 경직 · 암전 · 태그 파문 | 1.0 · 3.0 · Shout 22m/2.5 | `TagAftermath` | ✅ (이동 경직 클라이언트 반영) |
| 3.2-1 | 소나 | 거리·차폐 무시 · 잔류 3초 · 최근 8 | `SoundPulseResolver` 메아리 분기 · `EchoSonarConfig` | ✅ |
| 3.2-2 | 나침반 · 자기 실루엣 발광 | 8방위 · 약한 발광 | HUD 나침반 · (발광 없음) | ⚠ 발광 생략(1인칭) |
| 3.6 | 캠핑 방지 | 20초·3m · 3m/0.6 · 15초 +2m · 최대 9 · 5m 초기화 · 술래 포함 | `CampingConfig` · `TickCamping` | ✅ |
| 5.1 | 음성 3 · 발소리 2 · 비명 · 노크 · 호흡 | 4/0.6 · 9/1.2 · 22/2.5 · 2/0.4 · 6/0.8 · 9/1.0 · 9/1.2 · 3→9/0.6 | `VoiceConfig` · `LocomotionConfig` · `ScreamConfig` · `KnockConfig` · `CampingConfig` | ✅ |
| 5.1 | 밸브 회전 | 12m · **각 밸브 회전 시간** | `ValveOccupancy.SoundRadiusMeters/PulseDurationSeconds` | ✅ (**블록 7 수정** — 전엔 3초) |
| 5.1 | 발소리 경계 · 간격 | 5.0 m/s · 2m/6m 이동마다 | `FootstepSpeedThreshold` · 시뮬레이터 | ✅ |
| 5.6 | 벽 · 수면 · 층간 | ×0.5 · ×0.5(Wall) · ×0.25(가중 2) | `PhysicsOcclusionProbe` | ✅ (수면판 트리거 포함) |
| 5.9-1 | 숨 게이지 | 12 · −1/s · 억제 −4.5 · +2/+4 · 대기 2 · 질식 −20% 3초 | `BreathConfig` | ✅ |
| 6.1 | 회전 배분 · 총 점유 | A8 · B1.5+5+1.5 · C8 · D8 · E0.5+7+0.5 = 전부 8.0 | `ValveOccupancy` | ✅ (Valve의 중복 상수 5개 삭제) |
| 6.1 | 동시 작업 · 감쇠 | ×1.6 · ×1.9 · 유예 3 · −0.10/s | `ValveOccupancy` · `Valve` | ✅ |
| 6.1 | 밸브 A 소음 | ×0.5 (12→6m) | `SoundRadiusMultiplier` | ✅ (**블록 7 수정** — 전엔 미적용) |
| 6.1 | 수중 강제 파문 | 2.5초 주기 · 12m | `UnderwaterWorkPulse` | ✅ |
| 6.1 | **수중 밸브 B 숨 소모** | **8.0초(잔여 4.0 → 억제 불가)** | 회전 5.0초만 잠수 | ❌ **GAP-91** — 진입·부상 미시뮬레이션 |
| 6.1-0 | 배치 · 활성 | 5 · 요구 + 1 | `ValveRoster` | ✅ |
| 6.1-2 | 역류 | 180 · 30 · 10초 × 3 | `Valve` | ✅ |
| 6.2 | 요구 개방 · 탈출 요구 | ≤3인 2 / 그 외 3 · ⌈n/2⌉ | `ValveRoster` | ✅ (3 하드코딩 없음) |
| 6.2-1 | 종반 | 60초 +5%(곱) · 드론 120 · 출구 파문 30 | `LocomotionConfig` | ✅ (출구 파문 주기 GAP-86, 드론 연출 생략) |
| 6.3 | 판정 | 3분법 · 게이트는 전제 조건 | `WinConditionEvaluator` | ✅ |
| 6.5 | 페이즈 · 배수구 | 90 · T=14−3n · 진입1/부상1 · 통과 1.5 · (31,21)/(8,9.5) · 수심 3.5 | `DrainConfig` · `MapV2Layout` | ✅ |
| 7 | 찰칵이 | 6m · 0.3초 · 120초 · 4곳 · 소지 1 · 파문 없음 | `ClickerConfig` | ✅ |
| 10.1 | 구역 · 로비 스폰 | 13구역 · 로비 (2,32)~(12,40) | `MapV2Layout` · 생성기 앵커 | ✅ (스폰 블록 7 이동) |
| 10.2 | 밸브 좌표 | A(46,8) B(34,22) C(7,26) D(19,36) E(5,9) | `MapV2Layout.Valves` | ✅ |
| 12.4 | 브리핑 · 가이드 | 30초 · 20초 | `BriefingConfig` | ✅ |
| 14.3 | 역류 잔여 · 브리핑 잔여 | 시작 시 1회 | SyncVar 1회 | ✅ |
| 16.1 | 캐릭터 색 | #7FE7E0 · #EF6A4C · #B79CFF 반투명 | `FirstPersonController` | ✅ (반투명은 머티리얼 의존) |
| 16.2 | 팔레트 5색 | 배경 · 환경 · 시안 · 레드 · 앰버 | `ColorPalette` | ✅ |
| 16.4 | 잔상 | 모서리만 15% → 0 · 12초 / 차선책 8% · 8초 | 차선책 링 | ⚠ **차선책(사용자 확정)** |

**판정: PASS 아님.** ❌ 1건(§6.1 수중 밸브 숨 소모 — GAP-91), ⚠ 3건(실루엣 발광 생략 · 잔상 차선책 · 반투명 머티리얼 의존).

### 7-G 빌드 — **실행하지 못했다**

- Unity 에디터가 이 프로젝트를 열고 있어(잠금 파일) **배치모드 빌드가 불가**하다.
- 셋업 파이프라인 마지막 단계(FishNet **Reserialize NetworkObjects**)는 벤더 창의 수동 클릭이 필요하다(기존 기록 — 코드 실행 불가).
- 대신 **실제 Unity 컴파일은 확인했다**: 에디터 창을 앞으로 가져와 자동 새로고침을 일으켰고 Core · Presentation · Net ·
  Marco.Editor · Core.Tests · Assembly-CSharp가 **FishNet IL 위빙을 포함해 오류 0**으로 컴파일 · 도메인 리로드됐다
  (경고는 FishNet 벤더 `UpgradeFromMirrorMenu.cs`의 기존 CS0618 2건).
- 빌드 도구 추가: `Tools/MARCO/Build Windows (3인 테스트)` → `Builds/Windows/MARCO.exe`(`.gitignore`에 `[Bb]uilds/` 추가).
  배치모드 진입점 `Marco.EditorTools.MarcoBuildTool.BuildBatch`도 둔다(에디터를 닫은 상태에서).
- **사용자 실행 순서**: 수동검증 §31-0 (백업 → Setup Everything → Reserialize → 저장 → Build → 에디터 Play + exe 2개).

### 통합 — 맵 v2를 플레이 맵으로

| 변경 | 이유 |
|---|---|
| 파이프라인 **1b단계에서 맵 v2 생성**(전: 마지막 10단계) | 4단계 Setup Network Valves가 구 그레이박스 밸브(식별자 전부 A)에 네트워크를 붙이고 있었다 |
| 생성기: 밸브 큐브 + `ValveBehaviour`(식별자) + 상태 색 표시, **콜라이더는 트리거** | 실물 밸브. 고체면 배치 검증이 밸브 자리 셀을 벽으로 막는다 |
| 생성기: 출구 2곳 `EscapePointTrigger` · 배수구 `DrainPoint` · 찰칵이 `ClickerSpawnPoint` · 루트 `MapPlanData` | 탈출 · §6.5 · §7 · 브리핑 |
| 생성기: `SpawnAnchor` → 로비 중심 (7, 36) | §10.1 "로비 — 도망자 스폰"(구 좌표 (12,27)은 맵 v2에서 라커룸 안) |
| 생성기: 구 `Graybox` · `EscapePoint` **비활성화**(삭제 아님) | 밸브·출구 중복 제거. 되돌리려면 켜고 파이프라인 재실행 |
| Setup Network Valves: **꺼진 밸브는 네트워크 컴포넌트 해제** | 꺼진 구 밸브가 §6.1-0 후보에 섞이지 않게 |
| Setup Network Round: `ClickerNetworkSync` 함께 부착 | §7 |
| 물: 수면판 트리거 · 수영 바닥 −0.9 · 가장자리 경사로 | GAP-90 · 102 |

### 더블체크 10항목 (통합 관점)

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — 밸브 회전 파문(홀드 수락) · 브리핑(맵 준비 → `BeginBriefing` → `BeginRound`) · 숨 게이지 전송(`TickBreath`) · latch 라운드 해제(트래커 `Update`) · 생성기 부착물 전부 호출 경로 확인 |
| 2 | 라운드 전이 초기화 | **수정 1건** — 게이트 latch 리매치 결함. 브리핑은 `ServerResetWorld`·카운트다운 중단에서 해제, 숨 전송 기록은 InGame 진입에서 비움 |
| 3 | 경계값 | OK — 유예 2.99/3.00 · 회전 6.99/7.01 · 역류 188/218 · 브리핑 0/30 · 화살표 22.4/22.6 · 숨 전송 경계(0·만충) |
| 4 | 기존 테스트 의미 변화 | **4건** — `TryGetPulseSpec_Valve`(3초 → 8.0, v0.4 §5.1) · `SixPlayerValve_RequiresLongerRotation`(v0.4가 폐기한 6인 보정 → `ValveE_RequiresFullRotation`) · `RotationSeconds_MatchDesignDocTable` 등 2건(삭제된 중복 상수 → 정본) · `Breath_IsServerOnly`(Valve 서버 전용화) |
| 5 | 서버 권위 | OK — 밸브 회전음 서버 발생(클라이언트 Valve 주장 거부) · 숨 게이지 서버 값 · 브리핑 서버 타이머 · 활성 조합 서버 선택 |
| 6 | 캐시 금지 | OK — HUD·렌더러·텔레포터·음성 전부 매 프레임 재조회 |
| 7 | 계층 경계 | OK — `EscapeGateLatch`·`BriefingConfig`·`BreathClientState`는 Core. Unity 실제 컴파일로 어셈블리 참조 확인 |
| 8 | 상수 중복 | **수정** — `Valve.ValveXRotationSeconds` 5개 + v0.3 `SixPlayerRotationSeconds` 삭제(정본 `ValveOccupancy`). 수영 바닥 깊이는 유아풀 수심, 경사로 각도는 계단 경사에서 유도 |
| 9 | enum 파급 | OK — 새 enum 값 없음(`GameFlowState` 무변경 — 브리핑은 하위 구간) |
| 10 | 문서 정합 | **PASS 아님** — 위 대조표 ❌ 1 · ⚠ 3 |

### 테스트

신규 `PrototypeScenarioTests`(21) · 갱신 4건. **47 클래스 / 955 통과 / 0 실패** (블록 6 종료 934 → 955).

### 전체 재검증

- dotnet: Core / Net / Presentation / Marco.Editor / Core.Tests — **오류 0 · 프로젝트 경고 0**.
- **Unity 실제 컴파일(FishNet ILPP 포함)**: 오류 0, 경고는 벤더 코드 기존 2건.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **100** | §12.4 브리핑 30초 동안 **이동 가능 여부** | 🟡 명시 없음. **잠금**(평면도를 보며 밸브로 걸어가면 "인게임 지도 금지"와 충돌, 술래 격리 무력화). 클라이언트 권위 |
| **101** | 맵 v2의 **술래 격리 앵커** 위치 | 🟡 §10.1에 좌표 없음. 구 좌표 (12,17) 유지 — 맵 v2에서 구역 사이 통로. 실기에서 막히지 않는지 확인 |
| **102** | **풀 출입 지오메트리** | 🟡 기획서 없음. 네 변 경사로, 각도는 계단 중 완만한 쪽(37.9°)에서 유도 — 새 각도 없음 |
| **103** | 밸브를 특정할 수 없는 **Valve 등급 파문의 지속** | 🟡 §5.1은 "각 밸브 회전 시간". 역류 물소리 = 해당 밸브 회전 시간 · 수중/배수구 강제 파문 = 주기 2.5초(주기마다 이어져 "작업 내내") · 표 대표값 = 공통 총 점유 8.0 |
| 76 · 85 · 90 | 숨 게이지 소유자 전송 · 술래 경직 이동 · 맵 v2 물 진입 | ✅ **해소**(위) — 90은 잠정값(수영 깊이 0.9)에 기대므로 🟡 유지 |

### 범위 밖으로 남긴 것

- 잠수 로우패스 · 역류 물웅덩이 데칼 · 탈출 청록 페이드 · 종료 파문 다발 · 2분 드론 · 스태미나 화면 수축 — 지시("나머지 최소").
- §12.4 밸브 진행도 **링**·역류 잔여 30초 **링** — 핍 색/점멸로 대체(기능 위주).
- 메아리 자기 실루엣 발광(§3.2-2) · 메아리 반투명 머티리얼.
- 수중 밸브 서버 잠수 재검증(GAP-88) · 진입·부상 시뮬레이션(GAP-91).
- 빌드 · 3인 접속 확인 — 사용자 실행(수동검증 §31-0).

---

## 커밋 전 수정 3건 (블록 7 보고 후 지시)

### 1. 수중 밸브 B·E — 잠수 구간 = 진입 시작 ~ 부상 완료 (GAP-91: 밸브 부분 해소)

**변경 전**: 밸브는 회전만 알았다. 숨은 서버 잠수 판정(`ZoneOf`)으로만 소모됐고 밸브 작업과 무관했다 — 회전 5.0초만
잠수하면 B가 열려 잔여 7.0 ≥ 4.5, **부상 직후 비명 억제 가능**. §6.1의 "8.0초 소모(잔여 4.0) → 억제 불가"가 사라져 있었다.

**변경 후**: Core `UnderwaterWorkSession` — 작업 1회 = **진입 → 회전 → 부상**.

| 구간 | B | E | 서버 동작 |
|---|---|---|---|
| 진입(하강) | 1.5 | 0.5 | 잠수 의도 ON · 밸브에는 아직 손대지 않음 |
| 회전 | 5.0 | 7.0 | 진입이 끝나면 `BeginHold` + 회전 파문 · 수중 강제 파문 2.5초 주기 |
| 부상(상승) | 1.5 | 0.5 | 열렸거나 손을 뗀 뒤에도 잠수 의도 유지 |
| **합계** | **8.0** | **8.0** | = `ValveOccupancy.TotalSeconds` — **새 상수 없음**(블록 2 총 점유 재사용) |

- 서버: `ValveNetworkSync`가 수중 밸브 홀드를 세션으로 받는다. `PulseNetworkSync.ZoneOf`는 세션 구간을 **잠수 키와 같은 의도**로 합친다.
- **강제로 잠그지 않는다** — 머리가 실제로 수면 아래인지는 여전히 지오메트리가 정한다. 숨이 0이면 §5.9-1 강제 부상으로 세션이 즉시 끝나고 밸브에서 손이 떨어진다(감쇠로).
- 하강 전에 `Valve.CheckInteract`(상태 불변 판정)로 거부될 작업을 거른다 — `TryInteract`가 같은 판정을 쓴다(규칙 한 곳).
- 진입 도중 누가 열어 버리면 회전 시작 시 거부 → 곧장 부상.
- 클라이언트: 수중 밸브를 잡는 동안 + 놓은 뒤 부상 초만큼 **잠수 자세**(카메라 표시 전용 — 서버에 잠수 의도로 보내지 않는다). 자기 밸브 파문은 서버 상태가 회전 중이 된 뒤에 낸다.
- **순서 효과**: B는 손을 댄 뒤 **6.5초에 열린다**(전: 5.0). §6.1 총 점유 표 그대로다.

**E와 GAP-75 충돌 확인 — 충돌 없음, 잠수가 정상 발동한다.** 유아풀 본체 수심 0.9(성립 구간 0.5~1.62 안):
서 있으면 머리 +0.72(수면 위)지만, 세션은 잠수 키와 같은 **자세**를 주므로 머리가 −0.4(수면 아래)로 내려간다.
0.9 > 잠수 가능 최소 수심 0.5. 강제 발동은 쓰지 않았다(테스트 `ValveE_ShallowPool_*`).

**남은 한계 2건(수정하지 않음 — 지시 범위 밖)**
- **밸브를 물 밖에서 돌리면 숨이 안 든다(GAP-88 결과).** 수중 대상은 수평 2.5m로 재므로 B(메인풀 동쪽 벽에서 1m)·E(유아풀
  서쪽 벽에서 1.5m)는 **덱 위나 경사로 윗부분**에서도 잡힌다. 거기서는 잠수 자세로도 머리가 잠기지 않아 소모가 0이다.
  서버가 "몸이 물 안인가"를 확인하지 않기 때문 — GAP-88로 보류된 항목의 결과다. 최소 수정: 수중 밸브 홀드에 `BodyInWater` 조건.
- **배수구는 여전히 T만 잠수한다(GAP-91의 배수구 부분).** §6.5-2 표 "개방 1개 → 총 점유 13초 → 2회 잠수"인데 실제로는 T=11초만
  잠기므로 **1회 잠수로 끝난다**(12초 게이지). 같은 세션 규칙(진입 1 + T + 부상 1 = `DrainConfig.TotalOccupancySeconds`)을
  배수구에 적용하면 닫힌다. 이번 지시 범위(B·E)가 아니라 손대지 않았다.

### 2. 술래 격리 앵커 — 맵 v2 좌표로 (GAP-101 해소)

**확인 결과** (파이썬 다층 경로 시뮬레이터 — C# 배치 검증과 같은 벽·문·몸통 반경 규칙):

| 지점 | 맵 안 | 벽 최소거리 | 로비 스폰까지 | 밸브 A · B · C · D · E |
|---|---|---|---|---|
| 구 (12, 17) | ✅ 구역 사이 통로 | 2.90m | **25.8m** | 42.1 · 24.1 · 22.1 · 22.8 · 11.2 |
| **새 (44, 2.25)** | ✅ 직원통로 | 1.15m | **61.0m** | 6.8 · 29.5 · 57.4 · 56.6 · 42.3 |
| 참고 (30, 2.25) | ✅ 직원통로 | 1.15m | 48.2m | 19.7 · 30.1 · 44.5 · 44.4 · 28.9 |

구 좌표는 **벽·맵 밖 문제는 없었다**(맵 v2에서 우연히 구역 사이 빈 통로). 그러나 v0.3 단층 45×35 맵 기준 값이고
러너 스폰과 가까워(25.8m) 격리 효과가 약해 지시대로 옮겼다.

- 새 좌표는 **유도값**이다 — y = 직원통로 (14,1)~(48,3.5) 중앙선 2.25, x = 기계실 문 (45,5)에 가장 가까운 통로 문 (44,3.5)의 44.
  `MapV2Layout.SeekerIsolation` 한 곳이 소유하고 방향은 통로 문(북).
- 앵커를 만드는 **두 곳을 다 고쳤다**: `SceneFlowSetupTool`(없을 때 생성 — 스폰도 구 (12,27) → 로비 중심 `MapV2Layout.RunnerSpawn`)과
  `MapV2GeneratorTool.PlaceAnchors`(있으면 옮기고 없으면 생성 — 파이프라인 순서와 무관).
- **균형 관찰(§20.3 후보)**: 술래가 밸브 A 문 앞 6.8m에서 시작한다. A는 막다른 방이라 초반 A 작업 위험이 커진다 —
  활성 조합 무작위(§6.1-0)가 완충한다. 문제면 통로 중간 (30, 2.25)이 대안(A 19.7m · 로비 48.2m).

### 3. §10.2 — B↔C 질주 도달 가능 반영 (GAP-97 해소)

기획서 §10.2의 질주 사거리 문단을 실측값 기준으로 다시 썼다 — **도달 불가 = A↔C 60.1 · A↔D 59.3 · A↔E 45.6 셋**,
나머지 7쌍은 1회 사거리 안(값 전부 기재). B↔C를 도달 불가로 적었던 것은 서술 오류이며 9.3-1(청취 반경)·10.2-1(동시 감시)·
6.2(1:1 배치) 어느 것도 이 거리 관계를 전제하지 않는다는 근거를 문단에 넣었다. 코드 단언은 추가하지 않았다(방침 유지).

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — 세션 틱은 `ValveNetworkSync.Update`(밸브 틱 직후), 잠수 의도는 `ZoneOf`, 앵커는 생성기·씬 정리 둘 다 |
| 2 | 라운드 전이 초기화 | OK — 세션·홀드 의사를 `ServerResetForNewRound`에서 비움. 라운드 밖에서는 잠수 의도를 주지 않는다 |
| 3 | 경계값 | OK — 진입 1.49/1.51 · 부상 1.49/1.51 · 잔여 4.0 vs 4.5 · 숨 0 강제 부상 |
| 4 | 기존 테스트 의미 변화 | OK — 없음(신규 14) |
| 5 | 서버 권위 | OK — 구간 전이·잠수 의도·회전 시작 전부 서버. 클라이언트 잠수 자세는 표시 전용 |
| 6 | 캐시 금지 | OK |
| 7 | 계층 경계 | OK — `UnderwaterWorkSession`은 Core |
| 8 | 상수 중복 | OK — 진입·부상·총 점유 전부 `ValveOccupancy`. 격리 좌표는 레이아웃에서 유도 |
| 9 | enum 파급 | OK — 새 enum `UnderwaterWorkPhase`는 신규 타입, 기존 enum 무변경 |
| 10 | 문서 정합 | **PASS 아님** — §6.1 B·E ✅로 전환, 그러나 **§6.5-2 배수구 1개 개방 행 ❌**(위 남은 한계). 블록 7 대조표의 6.5 행은 이 사례를 놓쳤다 |

### 테스트 · 재검증

- 신규 `UnderwaterWorkSessionTests`(14). **48 클래스 / 969 통과 / 0 실패**.
- dotnet: 5개 어셈블리 오류 0 · 프로젝트 경고 0.
- **Unity 배치모드 컴파일**(에디터가 닫혀 잠금이 풀린 상태): 종료 코드 0, 오류 0 — 경고는 FishNet 벤더 기존 2건.

### GAP 갱신

| # | 상태 |
|---|---|
| 91 | 🟡 **밸브 B·E 해소** / 배수구 부분 미해소(개방 1개에서 1회 잠수로 끝남) |
| 97 | ✅ 해소 — 서술 오류로 판정, §10.2 수정 |
| 101 | ✅ 해소 — (44, 2.25), 유도값 |
| 88 | 🟡 보류 유지 — 결과로 "물 밖에서 수중 밸브 작업 시 숨 소모 0"이 남는다 |

---

## 커밋 전 수정 4건 — 1·2 커밋(09-23) · 4번(0개 개방)은 재검증 뒤 결정 대기

> 처음 기록 시 제목은 "⛔ 커밋 보류(기획서 §6.5-2 표와 충돌)"였다. 09-23 재검증으로 그 충돌은 **물 위 회복만 가정한 분석의 산물**로
> 확인됐다(맨 아래 「커밋 전 수정 4건 — 후속」). 1·2는 사용자 승인으로 커밋했다.

### 1. 배수구 잠수 구간 = 진입 → 작업 → 부상 (밸브 B·E와 같은 세션)

- **재사용**: `UnderwaterWorkSession`을 그대로 쓴다. 다른 것은 진입·부상 초뿐이라 생성자로 주입한다
  (`new UnderwaterWorkSession(ValveId)` / `UnderwaterWorkSession.ForDrain()` — `DrainConfig` 진입 1 · 부상 1).
  작업 자격 식도 한 곳(`UnderwaterWorkSession.CanWork`)이고 `DrainHatch.CanWork`는 위임한다.
- **순서가 하나 바뀌었다 — 통과는 부상을 마친 뒤.** 이전에는 작업 완료 즉시 통과(1.5초, 태그 가능)가 열렸다.
  부상 구간을 잠수로 치면 통과 앞 1초가 "잠수 중 = 바닥 판정(태그 불가)"과 겹친다. §6.5-2 "통과 1.5초, 그 동안
  태그 가능" + §6.5-3 "술래가 태그할 수 있는 순간은 부상할 그때뿐"을 함께 만족하려면 **완료 → 부상(잠수) → 통과(수면)**
  이어야 한다. `DrainHatch`는 완료 후 부상 대기(`IsCompleted`)로 있다가 서버가 정상 부상 뒤 `BeginTransit`을 부른다.
- 부상 도중 숨이 다하면(§5.9-1 강제 부상) 통과는 열리지 않고 **완료는 유지**된다 — 다음 잠수에서 진입 1 + 부상 1만 하면 나간다.
- 이 순서 덕에 1개 개방의 12초 동점(진입 1 + 작업 11 = 게이지 12)이 틱 순서와 무관하게 결정된다 — 부상 1초를 덮을 숨이 없다.
- 서버 틱 순서: 실제 서버 순서는 세션 → 배수구(`RoundNetworkSync.TickDrain` 976 → 988행 — 밸브의 구동기 → 세션과는 다른 패턴)이며, 09-23 재검증 결과 두 순서의 산출물은 동일함.
  (09-23 정정 — 처음에는 "밸브와 맞췄다: 배수구 틱 → 세션 틱"이라고 적었으나 코드는 바꾸지 않았다.)

**검증(요청 항목)** — `OneValveOpen_Job13s_ExceedsGauge12_ForcesSecondDive`: 만충에서 끝까지 버텨도 첫 잠수는 강제 부상,
두 번째 잠수로 탈출. **2회 잠수 강제 ✅.**

### ⛔ 충돌 — 0개 개방(T=14)은 기획서 "2회 잠수"가 성립하지 않는다 → ⚠ 09-23 정정: 충돌 아님

> **정정**: 아래 분석은 숨을 **물 위(수면 +2/s)에서만** 채운다고 가정했다. 물 밖(+4/s)으로 나가 채우고 귀환 한계선(숨 1.0초)에서
> 떼면 0개 개방도 **질식 없이 2회**로 나간다 — 표와 일치한다. 아래 표·해석은 "물 위 회복만"일 때의 사실로 남긴다.

전 구간 잠수 규칙에 v0.4 감쇠(유예 3초 후 −0.10/s)와 숨 회복(대기 2초 + 초당 2)을 함께 적용한 결과
(C# 시뮬레이터와 파이썬 정책 전수 탐색 — 떼는 숨 0~12 × 다시 누르는 숨 0.5~12, 두 모델 일치. 두 모델은 배수구 → 세션 순서로 돈다 — 실제 서버 순서는 세션 → 배수구(`RoundNetworkSync.TickDrain` 976 → 988행 — 밸브의 구동기 → 세션과는 다른 패턴)이며, 09-23 재검증 결과 두 순서의 산출물은 동일함. 09-23 정정 — 처음에는 "C# 시뮬레이터 = 서버 순서"라고 적었다):

| 동시 개방 | 질식 허용 시 최소 | 질식 없이 | 기획서 표 |
|---|---|---|---|
| 2개 (T=8) | 1회 · 11.5초 | 1회 · 11.5초 (잔여 2.0) | 1회(잔여 2초) ✅ |
| 1개 (T=11) | 2회 · 22.2초 · 질식 1 | 2회 · 30.7초 | 2회 ✅ |
| **0개 (T=14)** | **3회 · 47.7초 · 질식 2** | **90초 안 불가** | **2회 ❌** |

**해석적 확인**: 질식 없이 한 번 잠수에서 작업은 최대 10초(12 − 진입 1 − 부상 1). 숨을 다시 채우는 동안(부상 1 + 대기 2 +
최대 6 + 진입 1) 감쇠가 유예 3초 뒤 초당 1.4 작업초(0.10 × 14)를 깎아, 만충 재잠수 한 주기의 순증가가
10 − 9.8 = **0.2 작업초**다. 첫 잠수 10 이후 남은 4를 채우려면 20주기 — 90초 안에 불가능하다.
**원인은 이번 지시(부상 구간 포함)와 감쇠 규칙의 조합이다** — 수정 전(작업 T만 잠수)에는 2회로 나갔다.

질식을 감수하면 가능하지만(질식 1회 = 22m 고함급 파문 + 3초 감속) 표의 "2회"와는 다르다. 설계 결정이 필요하다 —
테스트는 현재 동작을 고정했다(`NoValvesOpen_WithoutChoking_CannotEscapeIn90s` · `NoValvesOpen_WithChoking_*`).

### 2. 수중 작업 시작 조건 = "이 자리에서 실제로 잠길 수 있음" (GAP-88 해소)

- `DiveRules.CanSubmergeHere(role, water, feetY, canSubmerge)` — **새 판정이 아니다**: 잠수 판정이 쓰는
  `IsDiving`(도망자 · 물 안 · 숨 있음)에 잠수 키를 넣고 `ZoneOf`(그 자세의 머리가 수면 아래인가)를 본다.
  덱(물 밖)과 경사로 윗부분(발 > −0.5)이 모두 걸린다.
- 서버: 밸브 B·E(`ValveNetworkSync`)와 배수구(`RoundNetworkSync`)가 같은 식(`UnderwaterWorkSession.CanWork` =
  도망자 · 수평 2.5m · 실제로 잠길 수 있음)으로 **시작과 유지를 매 틱** 재판정한다. 밸브는 이번에 서버 **거리 재검증도**
  처음 생겼다(수중 밸브 한정 — 지상 밸브 A·C·D는 여전히 서버 거리 검사가 없다, 기존 구조).
- 의사 보관: 덱에서 E를 누른 채 물로 들어가면 다시 누를 필요 없이 자격이 되는 틱에 진입이 열린다. 강제 부상 뒤에는 다시 눌러야 한다.
- 클라이언트 사전 필터(밸브·배수구)도 같은 식 — 덱에서는 잡히지도, 잠수 자세가 되지도 않는다.

**배치 확인**

| 대상 | 가장 가까운 물가 | 덱에서 2.5m 안? | 경사로(수평 1.157m)에서 2.5m 안? | 자격 판정 전 |
|---|---|---|---|---|
| 밸브 B (34,22) | 동쪽 1.0m(벽 바깥 덱 1.2m) | **예** | 예 | 덱에서 작업 가능했다 |
| 밸브 E (5,9) | 서쪽 1.5m(덱 1.7m) | **예** | 예 | 덱에서 작업 가능했다 |
| 배수구 1 (31,21) | 4.0m | 아니오 | 아니오(경사로 안쪽 끝 2.84m) | 구조상 안전했다 |
| 배수구 2 (8,9.5) | 3.0m | 아니오 | **예**(안쪽 끝 1.84m) | 경사로 윗부분에서 잠수 없이 가능했다 |

→ **배수구 1은 같은 구조가 아니었다**(물가에서 4.0m — 덱·경사로 어디서도 2.5m 밖). 걸리는 쪽은 배수구 2(경사로)였고,
자격 판정이 발 −0.5 경계로 막는다(테스트 `ShallowBoundary_HeadMustGoUnder` −0.49 거부 / −0.51 허용).

### 3. 술래 시작 위치 — §20.3 관찰 항목 등록만

"초반 A 선점이 과도하게 불리한가 — 불리하면 (30, 2.25)로 이동(A까지 19.7m)". 좌표 (44, 2.25)는 바꾸지 않았다.

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — 배수구 세션은 `TickDrain`, 자격은 밸브·배수구 서버 틱과 클라이언트 두 곳, 통과는 정상 부상 뒤 |
| 2 | 라운드 전이 초기화 | OK — 배수구 세션을 페이즈 진입·라운드 초기화에서 비움 |
| 3 | 경계값 | OK — 발 −0.49/−0.51 · 1개 개방 12초 동점 · 잔여 2.0 |
| 4 | 기존 테스트 의미 변화 | **4건** — 배수구 완료 즉시 통과 → 부상 뒤 통과(3건: `…CompletesAfterT_TransitAfterSurface…` 개명, `…NotCompleteJustBeforeT`, `…TaggedDuringTransit`), 시나리오 5(0개 → 1개 개방, 0개는 불가해져서) |
| 5 | 서버 권위 | OK — 수중 밸브에 서버 거리·잠수 가능 재검증이 생겼다(GAP-88) |
| 6 | 캐시 금지 | OK |
| 7 | 계층 경계 | OK — 판정은 Core |
| 8 | 상수 중복 | OK — 세션·자격 식 한 곳, 진입·부상 초는 `ValveOccupancy`·`DrainConfig` |
| 9 | enum 파급 | OK — `DrainTickResult.TransitStarted` → `Completed`(의미 변경, 사용처 전부 갱신) |
| 10 | 문서 정합 | **PASS 아님** — §6.5-2 표 0개 개방 행(위 충돌) → 09-23: 충돌 아님(물 밖 회복 미반영 분석), 기획서 보강 여부는 결정 대기 |

### 테스트 · 재검증

신규 `DrainDiveTests`(15) · 갱신 4건. **49 클래스 / 984 통과 / 0 실패**. dotnet 5개 어셈블리 오류 0.
Unity: 에디터가 열려 있어 배치모드 불가 — 에디터의 자동 컴파일(00:40, 이번 서버·클라이언트 변경 포함, FishNet 위빙 포함)에서
`error CS` 0건. 마지막 테스트 파일 수정(00:41)은 dotnet으로만 확인.

---

## 긴급 수정 — 로비 카운트다운 무한 루프 (`RoundNetworkSync.TickCountdown`)

### 증상 · 원인

RoleAssign 페이즈의 서버 틱이 맵 대기·브리핑 중에도 매 틱 `ServerLobbyDriver.Tick`을 불렀다. 드라이버는 StartRound를 낸 뒤
`CountdownActive = false`가 되고, 다음 틱에 전원 준비 상태를 **새 카운트다운**으로 받는다(`ServerLobbyDriverTests.Countdown_CompletesAfterThreeSeconds_ReturnsStartRoundOnce`가
고정하던 계약 그대로). 그래서 **3초 + 1틱마다** StartRound → 라운드 번호 증가 · 역할 재배정 · 맵 로드 재요청 · 브리핑 재시작(30초로 리셋 · 활성 밸브 재선택)이
반복됐고, 브리핑이 0에 닿지 못해 라운드가 끝내 시작되지 않았다. 블록 7 이전(브리핑 없음)에도 맵 로드가 3초를 넘으면 같은 반복이 났을 경로다.

**실기 로그로 확인** — `%LOCALAPPDATA%Low/DefaultCompany/시작/Player.log`(9/22 00:33, 2인 · 당시 경로 — 10-01부터 `…Low/MARCO/MARCO/Player.log`):
`역할 배정 완료` 16회 · `로비 브리핑 30초` 16회 · `라운드 시작 — 서버 권위` **0회**. 로테이션 표기가 1세트 1/3판 → 6세트 1/3판으로
전진(= 라운드 번호 0 → 15), 2인이라 술래가 반복마다 OwnerId 0 ↔ 1로 교대했다. 로그에 시각이 없어 간격 3초는 직접 보이지 않는다 — 코드 경로로
재현(아래 테스트: 주기 정확히 193틱 = 3초 + 1틱).

### 수정

- **`Core/GameFlow/RoundStartSequencer`**(신규) — RoleAssign 순서기: 카운트다운 → 맵 대기 → 브리핑 → 라운드 시작. **가드는 여기 한 곳**:
  `WaitingForMap || Briefing`이면 로비 드라이버를 틱하지 않는다. 결과는 `[Flags] RoundStartEvents`(Aborted / StartRound / BriefingStarted / BeginRound).
  Net을 EditMode에서 돌릴 수 없어 순서 판정을 Core로 옮겼다 — 테스트가 제품 코드를 직접 친다.
- `RoundNetworkSync` — `_waitingForMap` · `_briefing` · `_briefingServerRemaining` 삭제, `TickCountdown`은 이벤트별 부수 효과만(기존 Aborted / StartRound 본문 그대로).
  `ServerResetWorld`는 시퀀서를 초기화한다(부결 경로의 `_waitingForMap = false`도 여기로 흡수).
- `ServerLobbyDriver` — 코드 무변경. `StartRound` 문서에 "이후 계속 틱하면 재무장된다 — 호출자가 멈춰야 한다"를 명시.

### ⚠ 지시 밖 수정 1건 — 루프가 가리던 결함: 첫 브리핑의 "밸브 0개"

같은 로그 첫 반복: `맵 로드 요청` → `밸브가 0개 — 활성 선택을 건너뜁니다` → `로비 브리핑 30초` → `[Valve] 동시 개방 0/3 (활성 5)`.
`MapReadyForRound()`가 Unity의 "씬 로드됨"만 봤는데, 그 틱에는 FishNet이 맵 씬의 NetworkObject를 아직 스폰하지 않았다
(`ValveNetworkSync.Spawned`는 `OnStartNetwork`에서 채워진다). 루프 중에는 두 번째 반복부터 밸브가 잡혀 가려졌지만, **가드만 넣으면 브리핑이 그 1회뿐이라
밸브 5개 전부 활성인 채 라운드가 돈다**(§6.2 "활성 = 요구 + 1" 붕괴 · 평면도도 틀림). 그래서 맵 준비 = **씬 로드 + 밸브 스폰 1개 이상**으로 좁혔다
(씬 오브젝트는 한 번에 스폰된다). 스폰이 끝내 안 오면 조용히 멈추지 않게 "밸브 스폰 대기" 로그를 1회 남긴다. 이 부분은 Net이라 EditMode 테스트가 없다 — 실기 확인 항목(수동검증 §31-1).

### Aborted(GAP-29) 재검토 → GAP-104

- 기획서 확인: §12.3(준비 버튼 "전원 Ready 시 3초 카운트다운 후 역할 추첨 연출로 전환") · §12.4(브리핑 "라운드 시작 전 30초, 라운드 시작 시 사라진다") ·
  §15.4(RoleAssign 종료 조건 "연출 종료" → InGame) — **"브리핑 시작하면 취소 불가"도, "브리핑 중 취소"도 없다.** §15.4 표에는 RoleAssign → Lobby 전이 자체가 없다(GAP-29가 만든 확장).
- 판단: 카운트다운(3초) 동안만 GAP-29 중단을 유지하고, **카운트다운이 끝난 뒤(맵 대기 · 브리핑)는 중단하지 않는다.** 근거 — 그 시점엔 라운드 번호 · 역할 배정 · 맵 로드 ·
  활성 밸브가 이미 확정돼 되돌릴 규칙이 없고, 이탈·합류는 InGame과 같은 규칙으로 처리된다(합류자 = 라운드 시작 후 러너, 술래 이탈 시 재추첨 없음 — 기존 `AssignLateJoinersAsRunners`).
- 확인한 사실: 준비 토글은 원래 **Lobby 페이즈에서만** 받는다(`ReadyNetworkSync.ServerSetReady`). 즉 RoleAssign에서 "준비 해제"는 수정 전에도 불가능했고,
  GAP-29 중단은 카운트다운 중 이탈 · 미준비 신규 접속자로만 걸린다.

### 계산 검증표 (틱 = 1/64초, 이진수 정확)

| 항목 | 값 | 테스트 |
|---|---|---|
| 카운트다운 | 3초 = 192틱 | `Briefing_OneTickLeft_…` |
| 수정 전 반복 주기 | 3초 + 1틱 = 193틱(재무장 틱은 차감 없음) | `BugReproduction_PreFixOrder_…` |
| 수정 전 120초 | StartRound 틱 192 + 193k ≤ 7680 → **39회**, 라운드 번호 0 → 38, 라운드 시작 0회 | 〃 |
| 수정 전 브리핑 | 3.016초마다 30초로 리셋 → 0 도달 불가 | 〃 |
| 수정 후 | 로비 1틱 + 3 + 맵 5 + 브리핑 30 → InGame **38.0초**, StartRound 1회 | `FullSequence_…` |
| 브리핑 경계 | 잔여 1틱(0.015625)은 RoleAssign, 다음 틱 BeginRound | `Briefing_OneTickLeft_…` |
| 맵 로드 50초 지연 | StartRound 1회 유지 | `MapWait_LongerThanCountdown_…` |

### 더블체크 10항목

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — `_start.Tick`(Update → RoleAssign → `TickCountdown`) · `_start.Reset`(`ServerResetWorld` ← 리매치 가결·부결) · 생성(`OnStartServer`) |
| 2 | 라운드 전이 초기화 | OK — 리매치 경계에서 맵 대기·브리핑 초기화. Aborted는 카운트다운 단계에서만 나므로 지울 맵 대기·브리핑이 없다(수정 전에는 Aborted가 `_waitingForMap`을 남길 수 있었다) |
| 3 | 경계값 | OK — 브리핑 잔여 1틱 · 카운트다운 192틱 · 맵 준비 5초 |
| 4 | 기존 테스트 의미 변화 | **0건** — `TickCountdown`을 직접 치는 기존 테스트는 없었다(Net). `ServerLobbyDriverTests` 13건은 드라이버 무변경이라 그대로이며, 재무장 계약 테스트에 주석만 추가 |
| 5 | 서버 권위 | OK — 순서·타이머·맵 준비 판정 모두 서버 |
| 6 | 캐시 금지 | OK — 밸브 목록은 매 호출 조회 |
| 7 | 계층 경계 | OK — 순서기는 Core(Unity·FishNet 무참조), Net은 부수 효과만 |
| 8 | 상수 중복 | OK — `ServerLobbyDriver.CountdownSeconds` · `BriefingConfig.Seconds` 재사용, 새 상수 없음 |
| 9 | enum 파급 | 새 `RoundStartEvents`(순서기 · `RoundNetworkSync`만 사용). `LobbyTickResult` · `GameFlowState` 무변경 |
| 10 | 문서 정합 | 충돌 없음 — 기획서 공백은 GAP-104(🟡)로 기록 |

### 테스트 · 재검증

- 신규 `RoundStartSequencerTests`(11).
  - **커밋 스냅샷(이번 커밋만 · 4건 제외)**: 인덱스를 따로 내보내 빌드 — 5개 어셈블리 오류 0, **49 클래스 / 980 통과 / 0 실패**(969 → 980).
  - 작업 트리(미커밋 「커밋 전 수정 4건」 포함): 50 클래스 / 995 통과 / 0 실패(984 → 995).
- 변이 확인: 가드를 지우면 신규 5건이 실패한다(StartRound 39회 · 50초 맵 대기 중 16회 · 브리핑 타이머 리셋 등).
- dotnet: Core / Net / Presentation / Marco.Editor / Core.Tests 오류 0 · 경고 0.
- **Unity 실제 컴파일(FishNet ILPP) 미확인** — 에디터가 열려 있고 창 포커스 전환이 이번에는 OS에서 거부됐다. 바뀐 Net 코드에 RPC·SyncVar 추가는 없다.

### GAP

| # | 내용 | 처리 |
|---|---|---|
| **104** | 카운트다운(3초)이 끝난 뒤 — **맵 대기 · 브리핑 중의 이탈 · 합류** | 🟡 §12.3 · §12.4 · §15.4에 취소 규칙 없음. **취소하지 않는다**(역할 · 번호 · 맵 · 활성 밸브가 확정된 뒤라 되돌릴 규칙이 없다). 이탈 · 합류는 InGame 규칙. 남는 위험: 브리핑 중 이탈로 2인 미만이 돼도 라운드가 시작된다(InGame 중 이탈과 같은 결과) |

### 범위 밖

- 「커밋 전 수정 4건」은 여전히 ⛔ 보류(§6.5-2 0개 개방 충돌 결정 대기) — 이번 커밋에 넣지 않았다.

---

## 버그 2건 — 투명벽 · §16.1 암전 (⏳ Setup Everything 재실행 · 3인 실기 대기)

### 사실 확인 (씬 직렬화 데이터 — 파이프라인이 만든 현재 `Game.unity`)

| 확인 | 결과 |
|---|---|
| 레이어 충돌 행렬 | 전부 1 — **SoundBlocking도 플레이어(Default)와 부딪힌다**(§5.6 "이동 충돌과 섞이지 않게"가 물리 설정에 반영돼 있지 않다) |
| 풀 측벽 `PoolWall_*` 8장 | 고체 · SoundBlocking · y −3.5 ~ **0.0**(윗면이 덱 윗면과 같은 높이, 구역 바닥 밑). **덱 위를 걷는 몸에는 닿지 않는다** |
| 벽 차폐 콜라이더 `Wall_n/Sound` 71장 | **71장 전부 크기가 벽의 제곱** — `MakeWall`이 부모(이미 size로 늘어난 벽) 아래에 localScale = size로 만들었다. 예: 직원통로 34m 벽 → **1156m × 12.25m**, 메인풀홀 남벽 20m → z=15 선을 따라 **400m**. 고체라 맵을 가로지르는 보이지 않는 벽이 되고 문 개구부도 옆 벽의 연장선이 막는다 |
| 구역 바닥 · 기본 바닥 | `Zone_메인풀홀/Floor`(18~38 × 15~27) · `Zone_유아풀존/Floor` · `BaseFloor_West`(0~40 × 0~40)가 **수면 사각형을 통째로 덮는다** — 풀 위가 고체 뚜껑이라 물에 들어갈 수 없다. 블록 7의 GAP-90 수정(수면판 트리거)은 이 뚜껑을 못 봤다 |

→ 지시 1(측벽 트리거)은 그대로 반영했지만, **"풀 주변 통과"를 막는 것은 측벽이 아니라 제곱된 차폐 콜라이더**다. 지시만으로는 실기 (b)가 통과하지 않아 아래 두 건을 함께 고쳤다(⚠ 지시 밖).

### 수정

| # | 내용 | 구분 |
|---|---|---|
| 1 | `BuildPoolWalls` `Make()` — `isTrigger = true`. GAP-90 주석("SoundBlocking의 트리거는 수면판뿐")을 갱신 | 지시 1 |
| 1a | `MakeWall` — 차폐 자식 `localScale = Vector3.one`(벽과 같은 크기). **투명벽의 실제 원인** | ⚠ 지시 밖 |
| 1b | `MakeFloorAroundWater` — 지상 구역 바닥 · 기본 바닥을 **§10.1 수면 사각형만큼 뚫는다**(새 좌표 없음, 가장자리는 GAP-102 경사로 윗변과 만난다). 2층(관람석)은 뚫지 않는다. 계단 구멍과 같은 `SubtractAll` 재사용 | ⚠ 지시 밖 |
| 2 | `ApplyDarkness` — 생성기가 활성 씬(Game)에 `ambientMode = Flat` · `ambientLight = 흑` · `skybox = null`, `Directional Light` 비활성화(삭제 아님). 추가로 `reflectionIntensity = 0`(스카이박스가 없어도 기본 환경 반사가 Lit 표면을 희미하게 비추지 않게 — §16.1 "라이팅 사용량 0") | 지시 2 (+반사 0) |
| 2a | `NetworkPlayerSetupTool` ⑦ — `FirstPersonController._cameraTransform`의 카메라에 `SolidColor` · 흑 고정(현재 프리팹은 `m_ClearFlags: 1` = Skybox) | 지시 2 |

**기존 기록과의 관계**: §10.2-1 · §6.5-3 배치 검증 수치(블록 1-C · 1-D)는 **C# 로직을 파이썬으로 1:1 재현해 두께 0.2m 벽**으로 산출했다 —
의도한 기하다. 1a는 씬을 그 기록에 맞추는 수정이라 충돌하지 않는다. 반대로 파이프라인 10단계(Unity 배치 검증)가 지금까지 씬에서 돌았다면
**제곱된 벽 위의 과대 차폐**로 판정된 것이라 그 합격은 의미가 없었다 — 재실행 결과를 다시 봐야 한다.

**런타임 활성 씬**: `RenderSettings`는 활성 씬 값이다. FishNet `SceneManager`는 기본(`_setActiveScene = true`)으로 전역 씬 로드 뒤
첫 전역 씬(Game)을 활성으로 바꾼다 → **맵 로드(브리핑)부터 암전**. 그 전(로비)은 Lobby 씬 값(스카이박스 환경광)이다 — 아래 범위 밖.

### 더블체크 10항목 (+ 실기 1)

| # | 항목 | 결과 |
|---|---|---|
| 1 | 호출부 존재 | OK — 측벽 · 차폐 자식 · 바닥 구멍 · 암전은 `Generate()`(파이프라인 1b), 카메라는 `CreateOrUpdatePlayerPrefab`(3단계) |
| 2 | 라운드 전이 초기화 | 해당 없음(에디터 생성 데이터) |
| 3 | 경계값 | OK — 바닥 조각 경계 = 수면 사각형 변(경사로 윗변 y 0과 일치), 0.05m 미만 조각 버림(층간 차폐판과 같은 규칙) |
| 4 | 기존 테스트 의미 변화 | 0건 — 에디터 생성기는 EditMode 대상이 아니다. 차폐 관련 테스트 재확인: Occlusion 11 · SoundPulseResolver 19 · ActivePulseTracker 20 · LocalPulsePipeline 16 · ServerPulseDriver 23 전부 통과 |
| 5 | 서버 권위 | 해당 없음 |
| 6 | 캐시 금지 | 해당 없음 |
| 7 | 계층 경계 | OK — Editor 어셈블리만 수정 |
| 8 | 상수 중복 | OK — 구멍 = `MapV2Layout.Waters`, 빼기 = 기존 `Subtract`(`SubtractAll`로 층간 차폐판과 공용화) |
| 9 | enum 파급 | 없음 |
| 10 | 문서 정합 | OK — §16.1 "완전한 흑 배경" · §16.2 배경 `#000000` · §5.6 "이동 충돌과 분리" |
| 11 | **실기: Play 진입 시 화면이 검게 시작하는가** | ⏳ 사용자 실기 대기. 예상 — Boot(한두 프레임 스카이박스) → MainMenu(전면 흑 Image) → **로비는 완전 암전 아님**(아래) → 맵 로드부터 완전 암전 |

### 테스트 · 재검증

- 50 클래스 / 995 통과 / 0 실패(작업 트리 — 미커밋 「커밋 전 수정 4건」 포함). 차폐 테스트 수치는 위 4번.
- dotnet: Core / Net / Presentation / Marco.Editor / Core.Tests 오류 0(Marco.Editor 프로젝트 경고 0).
- **Unity 실제 컴파일**: 에디터 포커스 → `Marco.Editor.dll` 재빌드(수정 01:39 → 빌드 01:40), Editor.log 전체 `error CS` 0 · 경고는 FishNet 기존 2건.
  같은 로그에서 긴급 수정(`b8b02ec`)도 `Net.dll` 01:11 빌드로 FishNet ILPP 포함 컴파일 확인 — 그때 "미확인"이던 항목 해소.
- **씬 대조 스크립트**(파이프라인 재실행 뒤 `Game.unity` · `Player.prefab` 직렬화 검사 6항목): 현재 씬 = 6항목 전부 FAIL(재실행 전이라 정상).
  ① 차폐 콜라이더 0/71 정상 ② 측벽 트리거 0/8 ③ 수면 덮는 지상 바닥 4장 ④ RenderSettings 스카이박스 모드 ⑤ Directional Light 켜짐 ⑥ 카메라 Skybox.

### 범위 밖 · 알려진 상태

- **로비는 완전 암전이 아니다**: Lobby 씬 RenderSettings는 기본값(스카이박스 환경광), 로비 씬 카메라도 Skybox 클리어 — 접속 전에는
  반투명 딤 뒤로 하늘이, 스폰 뒤에는 흑 배경에 로비 임시 바닥이 보인다. Boot · MainMenu 카메라도 직렬화 값은 Skybox다(지시문 전제와 다름 —
  `EnsureCamera`는 카메라가 **없을 때만** 흑으로 만든다). MainMenu는 전면 흑 Image가 덮어 검게 보인다. §12.3 로비("발화로 밝아지는 방")까지
  암전할지는 결정이 필요하다.
- 레이어 충돌 행렬에서 SoundBlocking × 나머지를 끄는 방식(§5.6 원문에 가장 가까움)은 쓰지 않았다 — 지시대로 트리거 패턴을 유지했다.
- 암전 후 Lit 머티리얼(벽 · 밸브 큐브 색 · 캐릭터)은 파문 없이는 보이지 않는다(§16.1 의도). 밸브 상태 색 · 역할 색을 눈으로 보는
  수동검증 항목(§31-1 · 31-3)은 파문이 닿을 때만 확인된다.

### 09-23 후속 — Setup Everything 재실행 뒤 씬 대조

사용자가 09-22 01:51에 Setup Everything + FishNet Reserialize를 재실행(빌드 01:52), 2라운드 실기로 §31-1 암전 · 일반 이동 무장애를 확인했다.
씬 대조 스크립트(`Game.unity` · `Player.prefab` 직렬화) **6항목 전부 OK**:
차폐 콜라이더 71/71 벽과 같은 크기 · 풀 측벽 트리거 8/8 · **수면을 덮는 지상 바닥 0장**(풀 뚜껑 해소) · RenderSettings Flat 흑 · 스카이박스 없음 · 반사 0 ·
Directional Light 꺼짐 · Player 카메라 흑 단색.
**물 진입은 실기로 아직 확인되지 않았다** — 두 판의 로그에 잠수 · 수중 밸브 · 배수구 이벤트가 없다(09-23 판은 2인이라 시작부터 최후 생존자
페이즈, 배수구 미사용으로 90초 만료).

---

## 커밋 전 수정 4건 — 후속(09-23): 1·2 커밋 · 3 기록 정정 · 4 재검증

### 1·2 커밋 + 옛 동작 주석 정정

- `DrainPoint.cs` 클래스 주석 — "잠수 여부를 여기서 거르지 않는 이유…"(4-2 이전 설명) → 사전 필터가 서버와 같은 식(`UnderwaterWorkSession.CanWork`)이고
  잠수 키는 조건이 아니라는 현재 동작으로.
- `DrainHatch.Tick` 주석 — "작업이 끝나면 통과가 시작되고"(4-1 이전) → 완료(부상 대기) → 서버가 정상 부상 뒤 `BeginTransit` → 통과.
- 같은 파일 `DrainHatch.CanWork` 항목 "잠수 중"도 옛 판정이라 "이 자리에서 실제로 잠길 수 있음(GAP-88)"으로 고쳤다(지시 2건 외 1건).
- `DrainDiveTests`의 0개 개방 테스트 2건은 **물 위 회복만** 조건임을 이름·주석에 밝혔다(`…SurfaceRecoveryOnly…`) — 아래 4번 재검증 결과 반영.
  `PrototypeScenarioTests` 시나리오 5 주석도 같은 이유로 정정.

### 3. 틱 순서 기록 정정

위 절 두 곳(서버 틱 순서 · 시뮬레이터 설명)과 `DrainDiveTests` 12-13행 주석을 사실로 고쳤다 — 실제 서버 순서는 세션 → 배수구(`RoundNetworkSync.TickDrain` 976 → 988행 — 밸브의 구동기 → 세션과는 다른 패턴)이며, 09-23 재검증 결과 두 순서의 산출물은 동일함.
검증: ① 파이썬 정책 전수 탐색을 두 순서로 각각 실행 — 0 · 1 · 2개 개방 결과 동일 ② C# `DrainDiveSim`을 서버 순서로 임시 변경해 `DrainDiveTests` 실행 —
15/15 통과, 같은 값(원복 확인). 코드 순서는 바꾸지 않았다(지시).

### 4. 0개 개방 — 재검증 (결정 대기)

**모델**: 정수 틱(1틱 = 0.05초, 부동소수 누적 오차 없음). 숨 12 · 잠수 −1/s · 수면 +2/s · 물 밖 +4/s · 회복 대기 2초(`BreathConfig`),
T = 14 − 3×개방 · 진입 1 · 부상 1 · 통과 1.5 · 페이즈 90초(`DrainConfig`), 진행도 유예 3초 초과분부터 −0.10/s(`Valve`),
서버 순서(세션 → 배수구), 도망자 걷기 5.0 m/s(물속 감속 없음). 정책 = 떼는 숨 0(=버팀)~11.5 × 다시 누르는 숨 0.5~12(0.5 간격).
물 밖 전략: 작업 가능 지점 중 물가에 가장 가까운 곳 — 배수구 2 (8, 9.5)는 물가 3.0m · 발 −0.5 이하 조건으로 물가에서 0.65m,
배수구 1 (31, 21)은 물가 4.0m · 판정 반경 2.5m로 물가에서 1.5m. 경계 규칙은 두 가지로 돌렸다 —
**기획서**(§5.9-1 "게이지 0초에 부상 완료가 동시 성립하면 부상 완료 우선") / **현재 구현**(질식 우선, 아래 결함).

| 개방 | T · 총 점유 | 표 | 물 위 회복만 (질식 없이 / 최소) | 물 밖 회복 (질식 없이 / 최소) |
|---|---|---|---|---|
| 0 | 14 · 16 | 2회 | 불가 / 3회 · 질식 2 · 47.5~48.7초 | **2회 · 30.1~30.6초** / 2회 · 질식 1 · 25.5~25.8초 |
| 1 | 11 · 13 | 2회 | 2회 · 29.6~30.7초 / 2회 · 질식 1 · 18.8초 | 2회 · 22.9~23.4초 / 2회 · 질식 1 · 18.3초 |
| 2 | 8 · 10 | 1회(잔여 2) | 1회 · 11.5초 | 1회 · 11.5초 |
| 3 | 5 · 7 | — | 1회 · 8.5초 | 1회 · 8.5초 |

- **0개 개방 2회(질식 없이)는 떼는 시점이 좁다** — 0.05초 간격 재탐색: 떼는 숨 **1.00~1.15초**(배수구 2) · **1.00~1.05초**(배수구 1 걷기) ·
  1.00~1.10초(배수구 1 질주). 현재 구현(질식 우선)에서는 1.00이 빠져 1.05부터. 다시 누르는 숨은 11.05~11.3초(거의 만충까지 물 밖에서 기다림).
  §12.4 HUD의 "귀환 한계선 마커 1.0초"에서 떼는 플레이가 곧 이 해다.
- **1 · 2개 개방 행은 표와 일치**(감쇠를 넣어도 횟수 불변). 3개 개방은 도달 불가 — 요구 개방이 2(≤3인) 또는 3(4인 이상)이라
  3개가 열리면 게이트가 열려 배수구가 활성화되지 않는다(§6.5-2 · `DrainSelection.ShouldActivate`). 2개 개방 행도 3인 이하에서는 같은 이유로 도달 불가.
- **결론**: 기획서 §6.5-2 표의 잠수 횟수(0개 2회 · 1개 2회 · 2개 1회)는 **물 밖 회복을 쓰는 최적 플레이의 최소값으로 정확하다**.
  `DrainConfig.RequiredDives`(⌈총 점유 ÷ 12⌉)와 페이즈 진입 로그 "잠수 N회"도 도달 가능한 모든 행에서 최소 잠수 수와 같다 — 바꿀 값이 없다.
  단 표의 계산식(16 ÷ 12)은 감쇠를 빼먹은 식이라 **값은 맞지만 근거가 틀렸다** — 물 위에서만 채우면 0개 개방은 질식 없이 불가하다.
- 지시("0개 행 → 질식 없이 불가 / 최소 3회 · 질식 2회 · 47.7초")는 이 결과와 충돌한다 — 공통 규칙 8로 **기획서를 고치지 않았다**. 결정 대기.

**질식(§5.9-1 고갈) — 실제 효과**: 잠수 중 게이지가 0으로 **떨어지는 그 틱**에 1회(`BreathGauge.TickSubmerged`).
① 강제 부상 — 잠수 불가(`CanSubmerge` = 숨 > 0), 수중 작업 세션 즉시 종료 · 작업 중이면 중단 → 진행도 유예 3초 뒤 감쇠, 의사 삭제(E 다시 눌러야 함),
배수구는 완료 상태면 유지하되 통과 안 열림 ② 이동속도 −20% 3초(`BreathConfig.ChokeSpeedMultiplier` · 스태미나 탈진과는 곱하지 않고 느린 쪽)
③ **고함 등급(Shout) 파문 1회** — 발생 반경 22m · 지속 2.5초, 발생 위치 = 서버가 아는 플레이어 위치, 청취자별 §5.6 차폐 적용,
**술래 방향 게이지를 트리거**(대화 · 고함만 트리거, 사거리 22 × 1.2 = 26.4m) · 어워드 집계는 Shout로 기록(최다 비명상은 Scream만 셈).
HP · 데미지 · 태그 같은 다른 페널티는 없다. 숨은 0에서 회복 대기 2초 뒤 +2/+4로 다시 찬다.

**⚠ 발견한 결함 — §5.9-1 "0초 경계" 우선순위 미구현**: 기획서는 게이지 0과 부상 완료가 같은 시점이면 **부상 완료 우선(질식 무시)**인데,
`UnderwaterWorkSession.Tick`은 `canSubmerge == false`를 먼저 보고 강제 부상시키고, `BreathGauge`는 세션과 무관하게 질식(파문 · 감속)을 낸다.
서버에서 숨 틱(`PulseNetworkSync`)과 세션 틱(`ValveNetworkSync` · `RoundNetworkSync`)은 서로 다른 컴포넌트의 `Update`라 순서도 정해져 있지 않다.
실수 시간에서 정확히 0에 닿는 경우는 드물지만 규칙이 명시한 경계라 결함이다. 수정하지 않았다(지시 범위 밖).

**0개 개방 = 2인 게임의 기본 경로**: 2인은 도망자 1명이라 라운드 시작과 함께 최후 생존자 페이즈에 들어가고 밸브가 멈춘다 —
실기 로그 `Player-prev.log`(09-22 01:16) "최후 생존자 페이즈 진입 — 도망자 1 … 동시 개방 0개", `Player.log`(09-23) 90초 만료 SeekerWin.
즉 2인 판은 매번 "0개 개방 배수구" 한 가지로만 이길 수 있고, 그 해는 물 밖 회복 2회다(떼는 여유는 아래 「§5.9-1 0초 경계 수정」 뒤 재산출: 숨 1.0~1.2초 사이, 조작 창 약 0.15~0.2초).

### 원래 3번 — 술래 시작 위치 메모

`마르코_상세기획서.md` §20.3 관찰 항목(미커밋) 그대로. 좌표 (44, 2.25) 불변. 2인 판은 밸브가 멈춰 A 선점을 관찰할 수 없어 3인 이상 실기가 필요하다.

---

## §5.9-1 0초 경계 수정 · 0개 개방 최종 재산출 (09-23)

### 확인 — 숨 총량

`BreathConfig.TotalSeconds = 12f`(`BreathConfig.cs:33`). 이력: 8f(ae73e6f, 09-10) → 12f(24f525c, 09-21). 게이지 초기값 · 상한 · 클라이언트 표시가
전부 이 상수 하나를 읽는다. 재산출 스크립트는 상수를 C# 소스에서 직접 읽어 쓴다(12 · 1 · 2 · 4 · 2 · 14 · 3 · 1 · 1 · 1.5 · 90 · 3 · 0.10 · 5.0 · 7.5).

### 수정 — 기획서 §5.9-1 "게이지 0초 경계 처리"를 코드가 따르게

기획서: 부상 완료와 게이지 고갈이 같은 시점이면 **순위 1 부상 완료 · 순위 2 고갈(무시)**.
수정 전: ① `UnderwaterWorkSession.Tick`이 `canSubmerge == false`를 먼저 봐 강제 부상 ② `BreathGauge`는 세션과 무관하게 질식(고함급 파문 · 감속)
③ 숨(`PulseNetworkSync`)과 세션(`ValveNetworkSync` · `RoundNetworkSync`)의 `Update` 순서가 정해져 있지 않아 같은 입력에 결과가 갈렸다.

| 변경 | 내용 |
|---|---|
| `Net/ServerTickOrder`(신규) | 실행 순서 상수 — ① 숨 `Breath = 0` → ② 수중 작업 `UnderwaterWork = 100`. `[DefaultExecutionOrder]`로 세 컴포넌트에 명시 |
| `UnderwaterWorkSession.SurfaceCompletesWithin(dt)` | 이번 틱에 부상이 끝나는가. `Tick`은 숨 0이어도 이번 틱에 부상이 끝나면 정상 종료(강제 부상 아님) |
| `BreathGauge.Tick(zone, dt, surfacingCompletes)` | 참이면 같은 틱에 0이 돼도 질식 아님 — 숨은 0까지 쓰고 회복 대기도 그대로, 무시되는 것은 감속 · 파문뿐 |
| `PulseNetworkSync.TickBreath` | 밸브 · 배수구 세션에 "이번 틱 부상 완료?"를 물어 게이지에 넘긴다(`ServerSurfaceCompletesWithin`) |

- 경계 해상도는 **서버 틱**이다(지시: "같은 틱에 겹치면 완료가 이긴다"). 틱 안에서 숨이 부상보다 조금 먼저 떨어지는 경우도 완료로 친다 — 최대 1프레임 관대.
- 경계가 아닌 고갈(작업 중 · 부상 도중 이른 틱)은 기존과 같다 — 강제 부상 · 질식.
- 부수 효과: 세션 소유자 두 개가 기본 스크립트보다 늦게 돈다. 세션이 낸 파문은 `PulseNetworkSync`가 **다음 프레임**에 전달한다(이전에는 순서 미정이라
  같은 프레임일 때도 있었다). FishNet 송신 루프는 `short.MaxValue`라 SyncVar 전송 프레임은 그대로다.

**테스트**: 신규 `ZeroBreathBoundaryTests` 8건(세션 경계 3 · 게이지 2 · 서버 순서 통합 3 — 배수구 2개 개방 숨 10.0 정확히 0 → 통과 · 질식 0,
숨 9.5 → 질식 1 · 강제 부상, 밸브 B 숨 8.0 → 개방 · 질식 0). **51 클래스 / 1003 통과 / 0 실패**. 기존 테스트 결과 변화 0건.
변이 확인: 세션 수정을 되돌리면 신규 3건 실패. Unity 실제 컴파일(FishNet 위빙 포함) 오류 0(Core · Net · Core.Tests 02:16 재빌드).

### 0개 개방 최종 재산출 (수정 후 서버 순서 · 60Hz 정수 틱)

모델: 숨 → 세션(시작 후 같은 틱 진전, 0초 경계 = 완료 우선) → 배수구. 물 밖 전략은 작업 가능 지점 중 물가에 가장 가까운 곳에서 나갔다 온다
(배수구 2: 0.65m, 배수구 1: 1.5m), 도망자 걷기 5.0 / 질주 7.5 m/s.

| 플레이 | 결과 |
|---|---|
| 물 밖 회복 · 한계선에서 뗌(**숨 1.0~1.2초**, 물 밖에서 숨을 약 11초 넘게 채운 뒤 재잠수) | **2회 · 질식 0 · 약 30.2~30.6초** |
| └ 조작 창(질식 없이 2회가 되는 뗌 시점) | 배수구 2 **1.000~1.200초(13프레임 ≈ 0.22초)** · 배수구 1 걷기 1.000~1.133초(9프레임 ≈ 0.15초) · 질주 11프레임 ≈ 0.18초 |
| 물 밖 회복 · 여유 있게 뗌(숨 1.25~2.0초) | 3회 · 질식 0 · 약 39~40.5초 (3.0초에서 떼면 4회 · 약 50초) |
| 물 위에서만 회복 | 질식 없이 **불가** · 끝까지 버텨 3회 · 질식 2 · 47.5초 |
| 물 밖 회복 · 질식 감수(끝까지 버팀) | 2회 · 질식 1 · 약 25.4~25.8초(가장 빠름 — 22m 고함급 파문 1회 대가) |

1 · 2개 개방 행도 수정 후 동일 결론(1개: 2회, 물 밖이면 질식 없이 약 22.8~23.2초 / 2개: 1회 · 11.5초). **표 값(0개 2회 · 1개 2회 · 2개 1회) · `RequiredDives` · 서버 로그 "잠수 N회"는 그대로 맞다.**
이전 기록의 "떼는 여유 0.05~0.15초"는 수정 전 구현(질식 우선)과 0.05초 격자로 잰 값이었다 — 위 값으로 대체한다.

**기획서 §6.5-2 각주**: 처음 지시 문구의 뒷문장("여유 있게 플레이하면 3회 · 강제 질식 2회")은 **물 위에서만 회복할 때**의 결과라, 사실에 맞춘 문구로
승인받았다 — "여유 있게 플레이하면 3회 — 물 밖에서 회복하면 질식 없이(약 40초), 물 위에서만 회복하면 강제 질식 2회(약 47.5초)". §6.5-2 반영은 별도 문서 커밋.

### 백로그 · 메모

- 로비 인원 경고 문구(2인 이하 → "권장 인원 3인 이상", 밸런스 조정 없음) — `팀_역할배분.md` §3.0 #11로 등록(낮은 우선순위, 별도 문서 커밋).
- §20.3 술래 시작 위치 메모 — 밸브 A까지 직선 6.09m / 17.0m로 정정해 커밋(aaa3270). 로비 거리 61.0 / 48.2m는 산출 근거 미확인(직선 50.1 / 40.8m) — 손대지 않았다.
