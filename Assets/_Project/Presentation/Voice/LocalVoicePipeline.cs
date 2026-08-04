using Marco.Core.Settings;
using Marco.Core.Sound;
using Marco.Core.Voice;
using Marco.Presentation.Settings;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.Voice
{
    /// <summary>
    /// §5.2 음성 파이프라인의 로컬 구동자(스프린트 26a — 1단계).
    ///
    /// **이번 스코프**: 캡처(1) → RMS·dBFS(2) → 게인(§12.6) → 등급 분류(6) → **Console 로그**.
    /// 네트워크 전송(8단계 이벤트 생성 → 서버 파문)은 스프린트 26b, 차폐·역할별 청취는 26c다.
    /// 그래서 이 컴포넌트는 <c>PulseNetworkSync</c>를 **건드리지 않는다** — 발소리 파이프라인도 무변경.
    ///
    /// **§5.8 입력 방식**:
    /// - VAD(기본): 마이크가 열려 있고 등급이 <see cref="VoiceGrade.Silence"/>를 넘으면 발화.
    /// - PTT(대체): <see cref="_pushToTalkKey"/>를 누르는 동안만 분류한다.
    /// - **강제 뮤트(Left Alt 홀드)**는 §5.8이 "항상 제공"하라고 명시하므로 두 방식 모두에 적용된다.
    ///
    /// **아직 없는 오탐 억제 단계**(§5.2의 3·4·5·7 — 26b/26c): 개인 캘리브레이션, 80~3400Hz
    /// 대역 필터, 3프레임 연속성, 0.15초 디바운스. 그래서 **환경 노이즈 오탐이 예상되며**,
    /// 1단계 실기의 목적 중 하나가 그 양상을 보는 것이다.
    /// </summary>
    public sealed class LocalVoicePipeline : MonoBehaviour
    {
        [Header("§5.8 키")]
        [Tooltip("§5.8 '강제 마이크 뮤트 | Left Alt(홀드)' — VAD/PTT 어느 쪽이든 적용된다.")]
        [SerializeField] private Key _muteKey = Key.LeftAlt;

        [Tooltip("PTT 모드에서 누르고 말할 키. §4.3 표에 PTT 키가 없어 임시로 둔 값이다(GAP-50).")]
        [SerializeField] private Key _pushToTalkKey = Key.V;

        [Header("진단")]
        [Tooltip("등급이 바뀔 때만 로그를 남긴다(매 프레임 찍으면 Console이 무의미해진다).")]
        [SerializeField] private bool _logGradeChanges = true;

        [Tooltip("켜면 등급이 유지되는 동안에도 주기적으로 dBFS를 남긴다(임계값 조정용).")]
        [SerializeField] private bool _logLevels;

        [SerializeField, Range(0.2f, 5f)] private float _levelLogInterval = 1f;

        private readonly MicrophoneCapture _capture = new MicrophoneCapture();
        private VoiceGrade _lastGrade = VoiceGrade.Silence;
        private float _sinceLevelLog;

        // 발화 파문 송신(스프린트 26b). 발소리와 **같은 브릿지**를 쓴다 — 별도 음성 채널을
        // 만들지 않는다(§5.1 표가 발소리·밸브·음성을 하나의 SoundType 체계로 다룬다).
        private Marco.Core.Net.IPulseNetworkBridge _bridge;

        // 스프린트 26c: §5.2의 오탐 억제 단계. 발신 주기 판단도 게이트가 맡는다.
        private readonly VoiceGate _gate = new VoiceGate();
        private readonly VoiceBandpass _bandpass = new VoiceBandpass();
        private readonly VoiceCalibration _calibration = new VoiceCalibration();

        /// <summary>§5.2-3 캘리브레이션 진행 중인가(설정 화면이 안내 문구에 쓴다).</summary>
        public bool IsCalibrating => _calibration.IsMeasuring;

        /// <summary>캘리브레이션 남은 초.</summary>
        public float CalibrationRemaining => _calibration.Remaining;

        /// <summary>§5.2-3 "가장 작은 목소리로 말해보세요" 3초 프롬프트를 시작한다.</summary>
        public void BeginCalibration()
        {
            _calibration.Begin();
            Debug.Log($"[Voice:Cal] 캘리브레이션 시작 — {VoiceCalibration.PromptSeconds:0}초 동안 " +
                      "가장 작은 목소리로 말해보세요(§5.2-3).");
        }

        /// <summary>가장 최근에 판정된 등급(26b에서 네트워크 전송이 읽을 지점).</summary>
        public VoiceGrade CurrentGrade { get; private set; } = VoiceGrade.Silence;

        /// <summary>가장 최근 dBFS(게인 적용 후). 진단·캘리브레이션 UI가 쓸 값이다.</summary>
        public float CurrentDbfs { get; private set; } = VoiceConfig.MinDbfs;

        /// <summary>마이크가 살아 있는가(없거나 거부되면 false — 게임은 그대로 진행된다).</summary>
        public bool CaptureActive => _capture.IsCapturing;

        private void OnEnable()
        {
            _capture.Start();
        }

        private void OnDisable()
        {
            _capture.Stop();
        }

        private void Update()
        {
            if (!_capture.IsCapturing)
                return;

            // 프레임이 아직 안 쌓였으면 직전 등급을 유지한다(20ms마다 한 번 갱신된다).
            if (!_capture.TryReadFrame(out float[] samples, out int count))
                return;

            GameSettings settings = SettingsStore.Current;

            if (IsSilenced(settings))
            {
                _gate.Reset();
                SetGrade(VoiceGrade.Silence, VoiceConfig.MinDbfs);
                return;
            }

            // §5.2-4 대역 필터(80~3400Hz)를 RMS 계산 **전에** 건다 — 저역 팬 소음·타건음이
            // 진폭에 섞이면 그 뒤 어떤 단계로도 되돌릴 수 없다.
            _bandpass.ProcessInPlace(samples, count);

            // §5.2-3 개인 오프셋 + §12.6 입력 게인을 함께 적용한다(둘 다 dB 덧셈).
            float totalGain = settings.InputGainDb + settings.VoiceCalibrationOffsetDb;
            VoiceGrade grade = VoiceClassifier.ClassifySamples(samples, totalGain, out float dbfs, count);
            SetGrade(grade, dbfs);

            if (TickCalibration(dbfs, settings))
                return; // 측정 중에는 파문을 내보내지 않는다.

            // §5.2-5 연속성 + §5.2-7 디바운스 + 발신 주기(GAP-52).
            if (_gate.Tick(grade, Time.deltaTime, DurationOf(grade)))
                TryEmitPulse(_gate.AcceptedGrade);

            if (_logLevels)
            {
                _sinceLevelLog += Time.deltaTime;
                if (_sinceLevelLog >= _levelLogInterval)
                {
                    _sinceLevelLog = 0f;
                    Debug.Log($"[Voice:Level] {dbfs:0.0} dBFS → {grade} " +
                              $"(게인 {settings.InputGainDb:+0.#;-0.#;0} dB, §5.1 경계 −50/−30/−15)");
                }
            }
        }

        /// <summary>
        /// 발화를 파문으로 내보낸다(§5.2-8 "이벤트 생성", 스프린트 26b).
        ///
        /// **클라이언트는 등급만 주장한다** — 반경·지속·발생 위치·차폐는 전부 서버가
        /// 재계산한다(GAP-24, 발소리·밸브와 완전히 같은 규칙). 그래서 발소리가 쓰는
        /// <c>IPulseNetworkBridge</c>를 그대로 쓰고 음성 전용 채널을 만들지 않는다.
        ///
        /// **발신 주기·연속성·디바운스는 <see cref="VoiceGate"/>가 판단한다**(스프린트 26c) —
        /// 여기 도달했다는 것은 §5.2-5/7을 이미 통과했다는 뜻이라 다시 검사하지 않는다.
        /// </summary>
        private void TryEmitPulse(VoiceGrade grade)
        {
            if (grade == VoiceGrade.Silence)
                return;

            if (!TryGetBridge(out Marco.Core.Net.IPulseNetworkBridge bridge))
                return;

            SoundType type = ToSoundType(grade);
            bridge.SubmitPulse(type);

            if (_logGradeChanges)
                Debug.Log($"[Voice:Net] 발화 파문 전송 — {grade}(§5.1 {type}). 반경·위치·차폐는 서버가 확정합니다.");
        }

        /// <summary>
        /// §5.2-3 측정 중이면 진행하고, 끝나면 오프셋을 설정에 저장한다.
        /// 측정 중에는 true를 돌려줘 발화가 파문으로 나가지 않게 한다.
        /// </summary>
        private bool TickCalibration(float dbfs, GameSettings settings)
        {
            if (!_calibration.IsMeasuring)
                return false;

            if (_calibration.Tick(dbfs, Time.deltaTime))
                return true;

            float offset = _calibration.ResolveOffsetDb();
            settings.VoiceCalibrationOffsetDb = offset;
            SettingsStore.Apply(settings);

            Debug.Log($"[Voice:Cal] 캘리브레이션 완료 — baseline {_calibration.Baseline:0.0} dBFS " +
                      $"(샘플 {_calibration.SampleCount}개) → 오프셋 {offset:+0.0;-0.0;0} dB 저장. " +
                      (_calibration.SampleCount == 0
                          ? "말소리가 잡히지 않아 보정하지 않았습니다(§5.2-3)."
                          : "이제 개인 마이크 기준으로 §5.1 등급이 판정됩니다."));
            return true;
        }

        /// <summary>
        /// 발소리 파이프라인이 쓰는 브릿지를 찾는다. <c>LocalPulsePipelineBehaviour</c>는 같은
        /// 오브젝트에서 <c>GetComponent</c>로 찾지만, 이 컴포넌트는 <c>SceneFlow</c>에 있고
        /// 브릿지는 <c>PulseSystem</c>에 있어 씬에서 찾아야 한다(같은 Lobby 씬이다).
        /// 접속 전에는 브릿지가 없거나 비활성이라 false — 그때는 로컬 로그만 남는다(26a 동작 유지).
        /// </summary>
        private bool TryGetBridge(out Marco.Core.Net.IPulseNetworkBridge bridge)
        {
            if (_bridge == null)
            {
                foreach (MonoBehaviour candidate in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                {
                    if (candidate is Marco.Core.Net.IPulseNetworkBridge found)
                    {
                        _bridge = found;
                        break;
                    }
                }
            }

            bridge = _bridge;
            return bridge != null && bridge.NetworkActive;
        }

        private static SoundType ToSoundType(VoiceGrade grade) => grade switch
        {
            VoiceGrade.Whisper => SoundType.Whisper,
            VoiceGrade.Talk => SoundType.Talk,
            _ => SoundType.Shout
        };

        private static float DurationOf(VoiceGrade grade) => grade switch
        {
            VoiceGrade.Whisper => VoiceConfig.WhisperDurationSeconds,
            VoiceGrade.Talk => VoiceConfig.TalkDurationSeconds,
            _ => VoiceConfig.ShoutDurationSeconds
        };

        /// <summary>
        /// 지금 입을 막아야 하는가 — §5.8 강제 뮤트, 또는 PTT 모드에서 키를 누르지 않은 상태.
        /// </summary>
        private bool IsSilenced(GameSettings settings)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return settings.VoiceMode == VoiceActivationMode.PushToTalk; // 키보드가 없으면 PTT는 발화 불가

            // §5.8 "이 키를 누르는 동안은 어떤 소리도 SoundPulse로 변환되지 않는다."
            if (keyboard[_muteKey].isPressed)
                return true;

            if (settings.VoiceMode == VoiceActivationMode.PushToTalk)
                return !keyboard[_pushToTalkKey].isPressed;

            return false;
        }

        private void SetGrade(VoiceGrade grade, float dbfs)
        {
            CurrentGrade = grade;
            CurrentDbfs = dbfs;

            if (grade == _lastGrade)
                return;

            _lastGrade = grade;

            if (_logGradeChanges)
            {
                Debug.Log(grade == VoiceGrade.Silence
                    ? $"[Voice] Silence ({dbfs:0.0} dBFS)"
                    : $"[Voice] {grade} ({dbfs:0.0} dBFS) — §5.1 반경 적용은 스프린트 26b(네트워크 전송)부터");
            }
        }
    }
}
