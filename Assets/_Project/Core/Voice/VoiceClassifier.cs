using System;

namespace Marco.Core.Voice
{
    /// <summary>
    /// §5.2 음성 분류의 순수 계산(스프린트 26a — 2단계 진폭 · 6단계 분류).
    ///
    /// Unity·FishNet을 모르는 순수 클래스다. 마이크 캡처(1단계)와 이벤트 생성(8단계)은
    /// Presentation/Net이 맡고, 여기서는 **샘플 배열 → dBFS → 등급**만 계산한다.
    ///
    /// **이번 스코프에 없는 것**(§5.2의 오탐 억제 단계 — 스프린트 26b/26c):
    /// 3단계 개인 캘리브레이션, 4단계 대역 필터(80~3400Hz), 5단계 연속성 검사,
    /// 7단계 디바운스. 그래서 이 분류기만으로는 **환경 노이즈 오탐이 예상된다** —
    /// 기획서도 캘리브레이션 없이는 "속삭였는데 고함으로 분류"가 빈발한다고 명시한다.
    /// 상수 자리(<see cref="VoiceConfig.ContinuityFrames"/> 등)는 미리 잡아 뒀다.
    /// </summary>
    public static class VoiceClassifier
    {
        /// <summary>
        /// 샘플 구간의 RMS를 구한다(§5.2-2 "프레임별 RMS 산출").
        /// 샘플은 −1~+1 정규화된 PCM을 가정한다(Unity <c>AudioClip.GetData</c> 규약).
        /// </summary>
        /// <param name="samples">대상 배열. null·빈 배열이면 0.</param>
        /// <param name="count">앞에서부터 볼 샘플 수. 0 이하거나 배열보다 크면 배열 길이로 맞춘다.</param>
        public static float Rms(float[] samples, int count = 0)
        {
            if (samples == null || samples.Length == 0)
                return 0f;

            if (count <= 0 || count > samples.Length)
                count = samples.Length;

            double sum = 0d;
            for (int i = 0; i < count; i++)
            {
                float s = samples[i];
                sum += (double)s * s;
            }

            return (float)Math.Sqrt(sum / count);
        }

        /// <summary>
        /// RMS를 dBFS로 바꾼다(§5.2-2 "→ dBFS 변환"). 무음(0)은 음의 무한대가 되므로
        /// <see cref="VoiceConfig.MinDbfs"/>로 눌러 유한한 값만 내보낸다.
        /// </summary>
        public static float ToDbfs(float rms)
        {
            if (rms <= 0f || float.IsNaN(rms))
                return VoiceConfig.MinDbfs;

            float db = (float)(20d * Math.Log10(rms));
            return db < VoiceConfig.MinDbfs ? VoiceConfig.MinDbfs : db;
        }

        /// <summary>
        /// §12.6 "입력 게인" 적용. dB 덧셈이므로 dBFS에 그대로 더하며, 슬라이더 범위를
        /// 벗어난 값은 잘라낸다. 게인을 적용해도 하한(<see cref="VoiceConfig.MinDbfs"/>) 아래로는
        /// 내려가지 않는다.
        /// </summary>
        public static float ApplyGain(float dbfs, float gainDb)
        {
            if (float.IsNaN(gainDb) || float.IsInfinity(gainDb))
                gainDb = VoiceConfig.DefaultInputGainDb;

            if (gainDb < VoiceConfig.MinInputGainDb)
                gainDb = VoiceConfig.MinInputGainDb;
            else if (gainDb > VoiceConfig.MaxInputGainDb)
                gainDb = VoiceConfig.MaxInputGainDb;

            float result = dbfs + gainDb;
            return result < VoiceConfig.MinDbfs ? VoiceConfig.MinDbfs : result;
        }

        /// <summary>
        /// §5.1 표의 3구간에 매핑한다(§5.2-6 "분류").
        /// 경계값은 **하한 포함**으로 읽는다 — 표가 "-50~-30", "-30~-15", "-15 이상"으로
        /// 연속 구간을 이루므로 겹치지 않게 하려면 한쪽만 포함해야 한다.
        /// </summary>
        public static VoiceGrade Classify(float dbfs)
        {
            if (float.IsNaN(dbfs))
                return VoiceGrade.Silence;

            if (dbfs >= VoiceConfig.ShoutFloorDbfs)
                return VoiceGrade.Shout;

            if (dbfs >= VoiceConfig.TalkFloorDbfs)
                return VoiceGrade.Talk;

            if (dbfs >= VoiceConfig.WhisperFloorDbfs)
                return VoiceGrade.Whisper;

            return VoiceGrade.Silence;
        }

        /// <summary>캡처 → 게인 → 등급까지 한 번에. 호출자가 중간값도 볼 수 있게 dBFS를 함께 돌려준다.</summary>
        public static VoiceGrade ClassifySamples(float[] samples, float gainDb, out float dbfs, int count = 0)
        {
            dbfs = ApplyGain(ToDbfs(Rms(samples, count)), gainDb);
            return Classify(dbfs);
        }
    }
}
