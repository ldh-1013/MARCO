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
