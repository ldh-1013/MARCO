using System;

namespace Marco.Core.Voice
{
    /// <summary>
    /// §5.2-4 대역 필터(스프린트 26c).
    ///
    /// **§5.2 원문**:
    /// <code>
    /// 4. 대역 필터 : 80Hz~3,400Hz(인간 음성 주파수 대역)만 통과시키는 밴드패스 필터 적용
    ///             → 팬 소음·키보드 타건음 등 저역/광대역 노이즈의 오분류 완화
    /// </code>
    ///
    /// 원문이 필터 차수·구현 방식을 정하지 않아(GAP-55) **1차 고역통과 + 1차 저역통과**를
    /// 직렬로 연결한 가장 단순한 형태를 택했다. 목적이 "오분류 완화"이지 정확한 주파수 응답이
    /// 아니고, 20ms 프레임마다 320샘플을 처리하는 경로라 비용도 낮아야 한다.
    ///
    /// 필터는 **상태를 갖는다**(직전 입력·출력을 기억). 프레임 경계에서 상태가 이어져야
    /// 연속 신호로 동작하므로 인스턴스를 계속 재사용해야 한다.
    /// </summary>
    public sealed class VoiceBandpass
    {
        /// <summary>§5.2-4 "80Hz~3,400Hz".</summary>
        public const float HighPassHz = 80f;
        public const float LowPassHz = 3400f;

        private readonly float _highPassAlpha;
        private readonly float _lowPassAlpha;

        private float _hpPrevInput;
        private float _hpPrevOutput;
        private float _lpPrevOutput;

        public VoiceBandpass(int sampleRateHz = VoiceConfig.SampleRateHz)
        {
            if (sampleRateHz <= 0)
                sampleRateHz = VoiceConfig.SampleRateHz;

            float dt = 1f / sampleRateHz;

            // 1차 RC 필터의 표준 이산화. rc = 1/(2πfc).
            float hpRc = 1f / (2f * (float)Math.PI * HighPassHz);
            _highPassAlpha = hpRc / (hpRc + dt);

            float lpRc = 1f / (2f * (float)Math.PI * LowPassHz);
            _lowPassAlpha = dt / (lpRc + dt);
        }

        /// <summary>
        /// 샘플 배열을 제자리에서 필터링한다(추가 할당 없음 — 20ms마다 도는 경로다).
        /// </summary>
        /// <param name="samples">−1~+1 정규화 PCM. null이면 아무것도 하지 않는다.</param>
        /// <param name="count">앞에서부터 처리할 샘플 수. 0 이하거나 초과면 배열 길이로 맞춘다.</param>
        public void ProcessInPlace(float[] samples, int count = 0)
        {
            if (samples == null || samples.Length == 0)
                return;

            if (count <= 0 || count > samples.Length)
                count = samples.Length;

            for (int i = 0; i < count; i++)
            {
                float input = samples[i];

                // 고역통과: 80Hz 아래(팬 소음·에어컨 등 저역)를 깎는다.
                float highPassed = _highPassAlpha * (_hpPrevOutput + input - _hpPrevInput);
                _hpPrevInput = input;
                _hpPrevOutput = highPassed;

                // 저역통과: 3400Hz 위(타건음의 날카로운 성분 등)를 깎는다.
                _lpPrevOutput += _lowPassAlpha * (highPassed - _lpPrevOutput);

                samples[i] = _lpPrevOutput;
            }
        }

        /// <summary>필터 상태를 비운다(캡처 재시작 등 신호가 끊길 때).</summary>
        public void Reset()
        {
            _hpPrevInput = 0f;
            _hpPrevOutput = 0f;
            _lpPrevOutput = 0f;
        }
    }
}
