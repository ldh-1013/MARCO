namespace Marco.Core.Voice
{
    /// <summary>
    /// §5.2의 오탐 억제 단계(5 연속성 · 7 디바운스)와 발신 주기를 담당하는 순수 상태기계
    /// (스프린트 26c, GAP-52 해소).
    ///
    /// **§5.2 원문**:
    /// <code>
    /// 5. 연속성 검사 : RMS가 임계값을 넘는 프레임이 최소 3프레임(60ms) 연속되어야 유효 발화로 인정
    ///               → 순간적 임팩트 노이즈(마우스 클릭, 책상 두드림) 배제
    /// 7. 디바운스   : 지속시간 0.15초 미만 발화는 무시(클릭·마찰음 오탐 방지)
    /// </code>
    ///
    /// 두 단계는 **막는 대상이 다르다**: 연속성은 "한 프레임짜리 스파이크"를, 디바운스는
    /// "짧게 스쳐 지나간 발화"를 거른다. 그래서 둘 다 필요하며 하나로 합치지 않았다.
    ///
    /// **발신 주기**(§5.2에 명시 없음 — GAP-52로 기록): 같은 등급이 이어지는 동안에는
    /// 직전 파문이 만료된 뒤에만 다시 보낸다. 매 프레임 보내면 §5.1 지속시간이 무의미해진다.
    /// </summary>
    public sealed class VoiceGate
    {
        private VoiceGrade _candidate = VoiceGrade.Silence;
        private int _consecutiveFrames;
        private float _heldSeconds;
        private VoiceGrade _accepted = VoiceGrade.Silence;

        private VoiceGrade _emitted = VoiceGrade.Silence;
        private float _emitCooldown;

        /// <summary>연속성·디바운스를 통과한 등급. 아직 통과 전이면 <see cref="VoiceGrade.Silence"/>.</summary>
        public VoiceGrade AcceptedGrade => _accepted;

        /// <summary>연속성 검사에서 지금까지 이어진 프레임 수(진단용).</summary>
        public int ConsecutiveFrames => _consecutiveFrames;

        /// <summary>
        /// 프레임 하나를 넣는다. 반환값은 **이번 프레임에 파문을 보내야 하는가**이며,
        /// true일 때 <see cref="AcceptedGrade"/>가 보낼 등급이다.
        /// </summary>
        /// <param name="grade">분류기가 낸 이번 프레임 등급.</param>
        /// <param name="deltaTime">경과 시간(초).</param>
        /// <param name="pulseDurationSeconds">보낼 파문의 §5.1 지속시간(재발신 간격이 된다).</param>
        public bool Tick(VoiceGrade grade, float deltaTime, float pulseDurationSeconds)
        {
            if (deltaTime > 0f && _emitCooldown > 0f)
                _emitCooldown -= deltaTime;

            // ── §5.2-5 연속성: 등급이 흔들리면 처음부터 다시 센다 ──────────
            if (grade != _candidate)
            {
                _candidate = grade;
                _consecutiveFrames = grade == VoiceGrade.Silence ? 0 : 1;
                _heldSeconds = grade == VoiceGrade.Silence ? 0f : Max(deltaTime, 0f);
            }
            else if (grade != VoiceGrade.Silence)
            {
                if (_consecutiveFrames < int.MaxValue)
                    _consecutiveFrames++;

                if (deltaTime > 0f)
                    _heldSeconds += deltaTime;
            }

            if (grade == VoiceGrade.Silence)
            {
                _accepted = VoiceGrade.Silence;
                _emitted = VoiceGrade.Silence;
                return false;
            }

            // ── §5.2-5/7: 3프레임 연속 + 0.15초 지속을 모두 넘겨야 유효 발화 ──
            if (_consecutiveFrames < VoiceConfig.ContinuityFrames || _heldSeconds < VoiceConfig.DebounceSeconds)
            {
                _accepted = VoiceGrade.Silence;
                return false;
            }

            _accepted = grade;

            // ── 발신 주기: 등급이 바뀌었거나 직전 파문이 끝났을 때만 ────────
            bool gradeChanged = grade != _emitted;
            if (!gradeChanged && _emitCooldown > 0f)
                return false;

            _emitted = grade;
            _emitCooldown = pulseDurationSeconds > 0f ? pulseDurationSeconds : 0f;
            return true;
        }

        /// <summary>마이크 정지·뮤트 등으로 발화가 끊길 때 상태를 비운다.</summary>
        public void Reset()
        {
            _candidate = VoiceGrade.Silence;
            _consecutiveFrames = 0;
            _heldSeconds = 0f;
            _accepted = VoiceGrade.Silence;
            _emitted = VoiceGrade.Silence;
            _emitCooldown = 0f;
        }

        private static float Max(float a, float b) => a > b ? a : b;
    }
}
