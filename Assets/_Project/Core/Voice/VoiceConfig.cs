namespace Marco.Core.Voice
{
    /// <summary>§5.1 표의 음성 3등급 + 무음.</summary>
    public enum VoiceGrade
    {
        /// <summary>발화로 인정되지 않는 수준(§5.1 속삭임 하한 미만).</summary>
        Silence,

        /// <summary>속삭임 — RMS −50~−30 dBFS.</summary>
        Whisper,

        /// <summary>대화 — RMS −30~−15 dBFS.</summary>
        Talk,

        /// <summary>고함 — RMS −15 dBFS 이상.</summary>
        Shout
    }

    /// <summary>§5.8 음성 입력 방식.</summary>
    public enum VoiceActivationMode
    {
        /// <summary>§5.8 기본값 — 오픈마이크. "무의식적으로 말이 새어나가는 순간의 공포"가 핵심 훅이다.</summary>
        VoiceActivation,

        /// <summary>§5.8 대체 — 누르고 말하기. 선택 시 긴장감이 줄어든다는 안내를 노출해야 한다.</summary>
        PushToTalk
    }

    /// <summary>
    /// §5.2 음성 분류 파이프라인의 수치(스프린트 26a). **전부 기획서 원문에서 옮긴 값**이며
    /// 임의로 만든 상수가 없다.
    ///
    /// **§5.1 표 원문**:
    /// <code>
    /// | 속삭임 | 4m  | 0.6초 | 마이크 RMS -50~-30 dBFS |
    /// | 대화   | 9m  | 1.2초 | 마이크 RMS -30~-15 dBFS |
    /// | 고함   | 22m | 2.5초 | 마이크 RMS -15 dBFS 이상 |
    /// </code>
    ///
    /// **§5.2 원문**: 16kHz PCM·20ms 프레임(1단계), 프레임별 RMS → dBFS(2단계),
    /// 최소 3프레임(60ms) 연속(5단계), 0.15초 미만 발화 무시(7단계).
    ///
    /// > §0 문서 갱신 원칙: "수치는 전부 가변. '확정값'이 아니라 '시작값(baseline)'으로 취급한다."
    /// </summary>
    public static class VoiceConfig
    {
        // ── §5.1 등급 임계값(dBFS) ────────────────────────────────────────

        /// <summary>속삭임 하한. 이 미만은 <see cref="VoiceGrade.Silence"/>.</summary>
        public const float WhisperFloorDbfs = -50f;

        /// <summary>속삭임 → 대화 경계.</summary>
        public const float TalkFloorDbfs = -30f;

        /// <summary>대화 → 고함 경계.</summary>
        public const float ShoutFloorDbfs = -15f;

        // ── §5.1 등급별 파문 반경·지속시간 ────────────────────────────────
        // 표 원문: 속삭임 4m/0.6초 · 대화 9m/1.2초 · 고함 22m/2.5초.
        // 서버가 이 값으로 파문을 재계산한다(클라이언트는 등급만 주장 — GAP-24).

        public const float WhisperRadiusMeters = 4f;
        public const float WhisperDurationSeconds = 0.6f;

        public const float TalkRadiusMeters = 9f;
        public const float TalkDurationSeconds = 1.2f;

        public const float ShoutRadiusMeters = 22f;
        public const float ShoutDurationSeconds = 2.5f;

        // ── §5.2 캡처·타이밍 ──────────────────────────────────────────────

        /// <summary>§5.2-1 "16kHz PCM".</summary>
        public const int SampleRateHz = 16000;

        /// <summary>§5.2-1 "20ms 프레임".</summary>
        public const float FrameSeconds = 0.02f;

        /// <summary>한 프레임의 샘플 수(16kHz × 20ms = 320).</summary>
        public const int FrameSamples = (int)(SampleRateHz * FrameSeconds);

        /// <summary>§5.2-5 "최소 3프레임(60ms) 연속되어야 유효 발화".</summary>
        public const int ContinuityFrames = 3;

        /// <summary>§5.2-7 "지속시간 0.15초 미만 발화는 무시".</summary>
        public const float DebounceSeconds = 0.15f;

        // ── §12.6 입력 게인 ───────────────────────────────────────────────

        /// <summary>§12.6 오디오 탭 "입력 게인 | 슬라이더(-20dB~+20dB) | 0dB".</summary>
        public const float MinInputGainDb = -20f;
        public const float MaxInputGainDb = 20f;
        public const float DefaultInputGainDb = 0f;

        /// <summary>
        /// dBFS로 표현할 수 있는 하한. 완전한 무음(RMS 0)은 dBFS가 음의 무한대가 되므로
        /// 유한한 값으로 눌러 둔다 — 계산·표시·직렬화 어디서도 무한대를 다루지 않기 위함이다.
        /// </summary>
        public const float MinDbfs = -120f;
    }
}
