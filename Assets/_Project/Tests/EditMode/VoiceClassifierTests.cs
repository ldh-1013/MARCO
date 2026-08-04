using System;
using Marco.Core.Voice;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 26a §5.2 음성 분류. 순수 계산이라 Unity 없이 그대로 돈다.
    /// </summary>
    public class VoiceClassifierTests
    {
        /// <summary>주어진 dBFS를 내는 정현파 대신, RMS가 정확히 그 값이 되는 상수 신호를 만든다.</summary>
        private static float[] SamplesAtDbfs(float dbfs, int count = 320)
        {
            float amplitude = (float)Math.Pow(10d, dbfs / 20d);
            var samples = new float[count];
            for (int i = 0; i < count; i++)
                samples[i] = amplitude;

            return samples;
        }

        // ── §5.1 임계값이 기획서와 같은가 ────────────────────────────────

        [Test]
        public void Thresholds_MatchDesignDoc()
        {
            // §5.1: 속삭임 -50~-30 / 대화 -30~-15 / 고함 -15 이상
            Assert.AreEqual(-50f, VoiceConfig.WhisperFloorDbfs, 0.001f);
            Assert.AreEqual(-30f, VoiceConfig.TalkFloorDbfs, 0.001f);
            Assert.AreEqual(-15f, VoiceConfig.ShoutFloorDbfs, 0.001f);
        }

        [Test]
        public void CaptureConstants_MatchDesignDoc()
        {
            // §5.2-1 "16kHz PCM, 20ms 프레임" → 프레임당 320 샘플
            Assert.AreEqual(16000, VoiceConfig.SampleRateHz);
            Assert.AreEqual(0.02f, VoiceConfig.FrameSeconds, 0.0001f);
            Assert.AreEqual(320, VoiceConfig.FrameSamples);

            // §5.2-5 "최소 3프레임(60ms)", §5.2-7 "0.15초 미만 무시"
            Assert.AreEqual(3, VoiceConfig.ContinuityFrames);
            Assert.AreEqual(0.15f, VoiceConfig.DebounceSeconds, 0.0001f);
        }

        [Test]
        public void InputGainRange_MatchesDesignDoc()
        {
            // §12.6 "입력 게인 | 슬라이더(-20dB~+20dB) | 0dB"
            Assert.AreEqual(-20f, VoiceConfig.MinInputGainDb, 0.001f);
            Assert.AreEqual(20f, VoiceConfig.MaxInputGainDb, 0.001f);
            Assert.AreEqual(0f, VoiceConfig.DefaultInputGainDb, 0.001f);
        }

        // ── 구간 판정 ────────────────────────────────────────────────────

        [Test]
        public void Classify_MapsEachBand()
        {
            Assert.AreEqual(VoiceGrade.Silence, VoiceClassifier.Classify(-60f));
            Assert.AreEqual(VoiceGrade.Whisper, VoiceClassifier.Classify(-40f));
            Assert.AreEqual(VoiceGrade.Talk, VoiceClassifier.Classify(-20f));
            Assert.AreEqual(VoiceGrade.Shout, VoiceClassifier.Classify(-5f));
        }

        [Test]
        public void Classify_BoundariesIncludeLowerEdge()
        {
            // 표가 "-50~-30", "-30~-15", "-15 이상"으로 연속 구간이라 한쪽만 포함해야 겹치지 않는다.
            Assert.AreEqual(VoiceGrade.Whisper, VoiceClassifier.Classify(-50f));
            Assert.AreEqual(VoiceGrade.Talk, VoiceClassifier.Classify(-30f));
            Assert.AreEqual(VoiceGrade.Shout, VoiceClassifier.Classify(-15f));

            // 경계 바로 아래는 한 단계 낮은 등급.
            Assert.AreEqual(VoiceGrade.Silence, VoiceClassifier.Classify(-50.01f));
            Assert.AreEqual(VoiceGrade.Whisper, VoiceClassifier.Classify(-30.01f));
            Assert.AreEqual(VoiceGrade.Talk, VoiceClassifier.Classify(-15.01f));
        }

        [Test]
        public void Classify_NaNIsSilence()
        {
            Assert.AreEqual(VoiceGrade.Silence, VoiceClassifier.Classify(float.NaN));
        }

        // ── RMS · dBFS ───────────────────────────────────────────────────

        [Test]
        public void Rms_OfConstantSignal_IsThatAmplitude()
        {
            var samples = new[] { 0.5f, 0.5f, 0.5f, 0.5f };

            Assert.AreEqual(0.5f, VoiceClassifier.Rms(samples), 0.0001f);
        }

        [Test]
        public void Rms_IgnoresSign()
        {
            // 음압은 부호가 있지만 세기는 없다 — 제곱 평균이므로 부호가 상쇄되면 안 된다.
            var samples = new[] { 0.5f, -0.5f, 0.5f, -0.5f };

            Assert.AreEqual(0.5f, VoiceClassifier.Rms(samples), 0.0001f);
        }

        [Test]
        public void Rms_HandlesNullAndEmpty()
        {
            Assert.AreEqual(0f, VoiceClassifier.Rms(null), 0.0001f);
            Assert.AreEqual(0f, VoiceClassifier.Rms(new float[0]), 0.0001f);
        }

        [Test]
        public void Rms_RespectsCountArgument()
        {
            // 링 버퍼에서 앞부분만 읽는 경우 — 뒤쪽 쓰레기 값이 섞이면 안 된다.
            var samples = new[] { 1f, 1f, 0f, 0f };

            Assert.AreEqual(1f, VoiceClassifier.Rms(samples, count: 2), 0.0001f);
        }

        [Test]
        public void Rms_ClampsOversizedCount()
        {
            var samples = new[] { 0.5f, 0.5f };

            Assert.AreEqual(0.5f, VoiceClassifier.Rms(samples, count: 999), 0.0001f);
        }

        [Test]
        public void ToDbfs_SilenceIsFiniteFloor()
        {
            // 무음의 dBFS는 음의 무한대다 — 유한한 값으로 눌러야 계산·표시가 안전하다.
            float db = VoiceClassifier.ToDbfs(0f);

            Assert.AreEqual(VoiceConfig.MinDbfs, db, 0.001f);
            Assert.IsFalse(float.IsInfinity(db));
        }

        [Test]
        public void ToDbfs_FullScaleIsZero()
        {
            Assert.AreEqual(0f, VoiceClassifier.ToDbfs(1f), 0.001f);
        }

        [Test]
        public void ToDbfs_HalfAmplitudeIsAboutMinusSix()
        {
            // 진폭 절반 = 약 -6.02 dB(전기음향 상식) — 변환식이 맞는지 확인.
            Assert.AreEqual(-6.02f, VoiceClassifier.ToDbfs(0.5f), 0.05f);
        }

        // ── 게인 ─────────────────────────────────────────────────────────

        [Test]
        public void ApplyGain_AddsDecibels()
        {
            Assert.AreEqual(-20f, VoiceClassifier.ApplyGain(-30f, 10f), 0.001f);
            Assert.AreEqual(-40f, VoiceClassifier.ApplyGain(-30f, -10f), 0.001f);
        }

        [Test]
        public void ApplyGain_ClampsToSliderRange()
        {
            // §12.6 슬라이더 범위 밖 값이 들어와도 그 범위로 잘린다.
            Assert.AreEqual(-30f + VoiceConfig.MaxInputGainDb, VoiceClassifier.ApplyGain(-30f, 99f), 0.001f);
            Assert.AreEqual(-30f + VoiceConfig.MinInputGainDb, VoiceClassifier.ApplyGain(-30f, -99f), 0.001f);
        }

        [Test]
        public void ApplyGain_RecoversFromCorruptedValue()
        {
            Assert.AreEqual(-30f, VoiceClassifier.ApplyGain(-30f, float.NaN), 0.001f);
        }

        [Test]
        public void ApplyGain_CanPromoteWhisperToTalk()
        {
            // 게인은 등급을 실제로 바꿀 수 있어야 한다(설정이 의미를 가지려면).
            float boosted = VoiceClassifier.ApplyGain(-35f, 10f);

            Assert.AreEqual(VoiceGrade.Talk, VoiceClassifier.Classify(boosted));
        }

        // ── 통합 경로 ────────────────────────────────────────────────────

        [Test]
        public void ClassifySamples_MatchesDesignBandsEndToEnd()
        {
            VoiceGrade whisper = VoiceClassifier.ClassifySamples(SamplesAtDbfs(-40f), 0f, out _);
            VoiceGrade talk = VoiceClassifier.ClassifySamples(SamplesAtDbfs(-20f), 0f, out _);
            VoiceGrade shout = VoiceClassifier.ClassifySamples(SamplesAtDbfs(-5f), 0f, out _);
            VoiceGrade silence = VoiceClassifier.ClassifySamples(SamplesAtDbfs(-70f), 0f, out _);

            Assert.AreEqual(VoiceGrade.Whisper, whisper);
            Assert.AreEqual(VoiceGrade.Talk, talk);
            Assert.AreEqual(VoiceGrade.Shout, shout);
            Assert.AreEqual(VoiceGrade.Silence, silence);
        }

        [Test]
        public void ClassifySamples_ReportsDbfs()
        {
            VoiceClassifier.ClassifySamples(SamplesAtDbfs(-20f), 0f, out float dbfs);

            Assert.AreEqual(-20f, dbfs, 0.1f);
        }

        [Test]
        public void ClassifySamples_EmptyInputIsSilence()
        {
            Assert.AreEqual(VoiceGrade.Silence, VoiceClassifier.ClassifySamples(new float[0], 0f, out _));
        }

        [Test]
        public void DefaultActivationMode_IsVoiceActivation()
        {
            // §5.8 "PTT를 기본값으로 채택하지 않는다" — 기본은 VAD.
            Assert.AreEqual(0, (int)VoiceActivationMode.VoiceActivation,
                "VAD가 열거형의 첫 값이어야 기본값(0)으로 직렬화된다.");
        }
    }
}
