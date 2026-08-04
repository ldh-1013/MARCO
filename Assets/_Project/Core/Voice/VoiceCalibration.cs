using System;

namespace Marco.Core.Voice
{
    /// <summary>
    /// §5.2-3 개인 캘리브레이션(스프린트 26c).
    ///
    /// **§5.2 원문**:
    /// <code>
    /// 3. 캘리브레이션 : 로비 입장 시 "가장 작은 목소리로 말해보세요" 3초 프롬프트
    ///               → 개인별 baseline 측정 → 임계값에 개인 오프셋 적용
    /// </code>
    /// > "캘리브레이션 없이 고정 임계값만 쓰면 마이크 감도 편차로 '속삭였는데 고함으로 분류'되는
    /// > 사고가 빈발한다. 개인 baseline 보정은 MVP 필수 항목으로 승격한다."
    ///
    /// **해석**: 측정한 baseline이 "가장 작은 목소리"이므로, 그 값이 §5.1 속삭임 하한
    /// (<see cref="VoiceConfig.WhisperFloorDbfs"/>)에 오도록 dBFS를 평행 이동시킨다.
    /// 마이크가 조용하면(baseline이 낮으면) 오프셋이 양수가 되어 전체가 올라가고,
    /// 마이크가 크면 음수가 되어 내려간다.
    ///
    /// **오프셋 한계**(§5.2에 없음 — GAP-54): 무제한이면 잘못된 측정 한 번이 등급 체계를
    /// 통째로 망가뜨린다. §12.6 입력 게인과 같은 ±20dB로 제한했다.
    /// </summary>
    public sealed class VoiceCalibration
    {
        /// <summary>§5.2-3 "3초 프롬프트".</summary>
        public const float PromptSeconds = 3f;

        /// <summary>오프셋 허용 범위(GAP-54 — §12.6 입력 게인과 같은 폭).</summary>
        public const float MaxOffsetDb = 20f;

        private double _sum;
        private int _samples;
        private float _elapsed;

        /// <summary>측정 중인가.</summary>
        public bool IsMeasuring { get; private set; }

        /// <summary>측정 남은 시간(초). 측정 중이 아니면 0.</summary>
        public float Remaining => IsMeasuring ? Math.Max(0f, PromptSeconds - _elapsed) : 0f;

        /// <summary>§5.2-3 프롬프트를 시작한다.</summary>
        public void Begin()
        {
            _sum = 0d;
            _samples = 0;
            _elapsed = 0f;
            IsMeasuring = true;
        }

        /// <summary>
        /// 측정 중 프레임을 넣는다. 3초가 차면 false를 돌려주며 측정이 끝난다.
        /// <see cref="VoiceConfig.MinDbfs"/>(무음)는 평균에서 제외한다 — 말하지 않은 구간이
        /// 섞이면 baseline이 실제보다 낮게 잡혀 오프셋이 과하게 커진다.
        /// </summary>
        public bool Tick(float dbfs, float deltaTime)
        {
            if (!IsMeasuring)
                return false;

            if (deltaTime > 0f)
                _elapsed += deltaTime;

            if (dbfs > VoiceConfig.MinDbfs && !float.IsNaN(dbfs))
            {
                _sum += dbfs;
                _samples++;
            }

            if (_elapsed < PromptSeconds)
                return true;

            IsMeasuring = false;
            return false;
        }

        /// <summary>
        /// 측정된 baseline(평균 dBFS). 유효 샘플이 없으면 <see cref="VoiceConfig.MinDbfs"/>.
        /// </summary>
        public float Baseline => _samples > 0 ? (float)(_sum / _samples) : VoiceConfig.MinDbfs;

        /// <summary>측정에 쓰인 유효 샘플 수(진단용).</summary>
        public int SampleCount => _samples;

        /// <summary>
        /// baseline으로부터 오프셋을 구한다. 유효 샘플이 없으면 0(보정 없음)이다 —
        /// 측정 실패가 조용히 잘못된 보정으로 이어지지 않게 한다.
        /// </summary>
        public float ResolveOffsetDb() => OffsetFor(Baseline, _samples);

        /// <summary>
        /// 주어진 baseline에 대한 오프셋. 순수 함수라 저장된 값으로도 다시 계산할 수 있다.
        /// </summary>
        public static float OffsetFor(float baselineDbfs, int sampleCount = 1)
        {
            if (sampleCount <= 0 || float.IsNaN(baselineDbfs) || baselineDbfs <= VoiceConfig.MinDbfs)
                return 0f;

            float offset = VoiceConfig.WhisperFloorDbfs - baselineDbfs;

            if (offset > MaxOffsetDb)
                return MaxOffsetDb;

            return offset < -MaxOffsetDb ? -MaxOffsetDb : offset;
        }
    }
}
