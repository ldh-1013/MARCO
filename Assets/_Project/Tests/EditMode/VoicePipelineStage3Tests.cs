using System;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Core.Voice;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 26c — §5.2 오탐 억제(연속성·디바운스·대역 필터·캘리브레이션)와
    /// §3.4 방향 게이지 규칙. 전부 순수 로직이라 Unity 없이 돈다.
    /// </summary>
    public class VoicePipelineStage3Tests
    {
        private const float Frame = VoiceConfig.FrameSeconds; // 20ms

        // ── §5.2-5 연속성 · §5.2-7 디바운스 ──────────────────────────────

        [Test]
        public void Gate_SingleSpike_IsRejected()
        {
            // §5.2-5의 목적: "순간적 임팩트 노이즈(마우스 클릭, 책상 두드림) 배제"
            var gate = new VoiceGate();

            bool emit = gate.Tick(VoiceGrade.Shout, Frame, VoiceConfig.ShoutDurationSeconds);

            Assert.IsFalse(emit);
            Assert.AreEqual(VoiceGrade.Silence, gate.AcceptedGrade);
        }

        [Test]
        public void Gate_ShortBurst_IsRejectedByDebounce()
        {
            // 3프레임(60ms)은 넘겼지만 0.15초에 못 미치는 발화 — §5.2-7이 무시하라고 한 구간.
            var gate = new VoiceGate();

            bool emitted = false;
            for (int i = 0; i < 4; i++) // 80ms
                emitted |= gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);

            Assert.IsFalse(emitted, "0.15초 미만 발화는 파문이 되면 안 된다.");
        }

        [Test]
        public void Gate_SustainedSpeech_IsAccepted()
        {
            var gate = new VoiceGate();

            bool emitted = false;
            for (int i = 0; i < 10; i++) // 200ms > 0.15초
                emitted |= gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);

            Assert.IsTrue(emitted);
            Assert.AreEqual(VoiceGrade.Talk, gate.AcceptedGrade);
        }

        // ── 게이트의 시간 기준 = **벽시계** (스프린트 26 더블체크) ────────────
        //
        // §5.2-7의 "0.15초"와 §5.1 지속시간은 실제로 흐른 시간을 말한다. 파이프라인이
        // 렌더 프레임 시간(Time.deltaTime)을 넘기면 오디오 프레임을 못 읽고 빠져나간
        // 프레임의 시간이 유실돼, 60fps에서 디바운스가 0.30초·재발신이 2.4초로 늘어났다.
        // 아래 두 테스트는 "몇 번 호출됐는가"가 아니라 "얼마나 흘렀는가"가 기준임을 고정한다.

        [Test]
        public void Gate_Debounce_MeasuresElapsedTime_NotTickCount()
        {
            // 프레임이 드문드문 들어와도(틱당 50ms) 0.15초가 차면 통과해야 한다.
            // 연속성 3프레임도 함께 만족하는 최소 조합이다.
            var gate = new VoiceGate();

            bool emitted = false;
            for (int i = 0; i < 3; i++) // 3틱 × 50ms = 150ms
                emitted |= gate.Tick(VoiceGrade.Talk, 0.05f, VoiceConfig.TalkDurationSeconds);

            Assert.IsTrue(emitted, "0.15초가 흘렀는데도 막히면 디바운스가 틱 수를 세고 있는 것이다.");
        }

        [Test]
        public void Gate_ReEmitInterval_MeasuresElapsedTime_NotTickCount()
        {
            var gate = new VoiceGate();

            bool first = false;
            for (int i = 0; i < 10 && !first; i++)
                first = gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);
            Assert.IsTrue(first);

            // 큰 deltaTime 한 번으로 §5.1 지속시간(1.2초)을 넘기면 즉시 재발신 가능해야 한다.
            bool afterExpiry = gate.Tick(VoiceGrade.Talk, VoiceConfig.TalkDurationSeconds + 0.01f,
                VoiceConfig.TalkDurationSeconds);

            Assert.IsTrue(afterExpiry, "재발신 주기가 실제 경과 시간이 아니라 호출 횟수를 따르고 있다.");
        }

        [Test]
        public void Gate_FlickeringGrade_RestartsContinuity()
        {
            // 등급이 흔들리면 연속성 카운트가 처음부터 다시 세어져야 한다.
            var gate = new VoiceGate();
            gate.Tick(VoiceGrade.Talk, Frame, 1.2f);
            gate.Tick(VoiceGrade.Talk, Frame, 1.2f);
            gate.Tick(VoiceGrade.Shout, Frame, 2.5f); // 흔들림

            Assert.AreEqual(1, gate.ConsecutiveFrames);
        }

        [Test]
        public void Gate_SameGrade_WaitsForPulseToExpire()
        {
            var gate = new VoiceGate();

            // 발신될 때까지 진행.
            bool first = false;
            for (int i = 0; i < 10 && !first; i++)
                first = gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);
            Assert.IsTrue(first);

            // 직전 파문(1.2초)이 살아 있는 동안에는 다시 보내지 않는다.
            bool duringPulse = false;
            for (int i = 0; i < 20; i++) // 400ms
                duringPulse |= gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);
            Assert.IsFalse(duringPulse, "같은 등급이 이어지는 동안 파문이 겹쳐 쌓이면 안 된다.");

            // 만료된 뒤에는 다시 보낸다.
            bool afterExpiry = false;
            for (int i = 0; i < 50; i++) // 1초 더
                afterExpiry |= gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);
            Assert.IsTrue(afterExpiry);
        }

        [Test]
        public void Gate_GradeChange_EmitsImmediately()
        {
            var gate = new VoiceGate();
            for (int i = 0; i < 10; i++)
                gate.Tick(VoiceGrade.Talk, Frame, VoiceConfig.TalkDurationSeconds);

            // 속삭임 → 고함처럼 등급이 오르면 직전 파문을 기다리지 않는다.
            bool emitted = false;
            for (int i = 0; i < 10; i++)
                emitted |= gate.Tick(VoiceGrade.Shout, Frame, VoiceConfig.ShoutDurationSeconds);

            Assert.IsTrue(emitted);
            Assert.AreEqual(VoiceGrade.Shout, gate.AcceptedGrade);
        }

        [Test]
        public void Gate_Silence_ClearsState()
        {
            var gate = new VoiceGate();
            for (int i = 0; i < 10; i++)
                gate.Tick(VoiceGrade.Talk, Frame, 1.2f);

            gate.Tick(VoiceGrade.Silence, Frame, 0f);

            Assert.AreEqual(VoiceGrade.Silence, gate.AcceptedGrade);
            Assert.AreEqual(0, gate.ConsecutiveFrames);
        }

        // ── §3.4 방향 게이지 ─────────────────────────────────────────────

        [Test]
        public void Gauge_TriggersForTalkAndShoutOnly()
        {
            // §3.4 "속삭임 등급은 트리거하지 않는다. 대화·고함 등급만 트리거된다."
            Assert.IsTrue(DirectionGaugeRules.TriggersGauge(SoundType.Talk));
            Assert.IsTrue(DirectionGaugeRules.TriggersGauge(SoundType.Shout));
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Whisper));
        }

        [Test]
        public void Gauge_ExcludesFootstepsValveAndKnock()
        {
            // §3.4 "밸브 회전음, 발소리, 노크는 트리거 대상에서 제외된다."
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Walk));
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Sprint));
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Valve));
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Knock));
        }

        [Test]
        public void Gauge_IsSeekerOnly()
        {
            // §3.4 "술래 전용 정보 우위" / §3.1 방향 게이지: 도망자·메아리는 "없음".
            Assert.IsTrue(DirectionGaugeRules.CanSeeGauge(RoleType.Seeker));
            Assert.IsFalse(DirectionGaugeRules.CanSeeGauge(RoleType.Runner));
            Assert.IsFalse(DirectionGaugeRules.CanSeeGauge(RoleType.Echo));
        }

        [Test]
        public void Gauge_RangeMatchesDesignTable()
        {
            // §3.4 표: 대화 9m → 13.5m, 고함 22m → 33m
            Assert.AreEqual(13.5f, DirectionGaugeRules.MaxRange(SoundType.Talk, 9f), 0.001f);
            Assert.AreEqual(33f, DirectionGaugeRules.MaxRange(SoundType.Shout, 22f), 0.001f);
        }

        [Test]
        public void Gauge_ShoutDoesNotCoverWholeMap()
        {
            // §3.4 주석: "맵 전체(45m×35m, 대각선 약 57m) … 고함조차 맵 전체를 커버하지 못하도록"
            float mapDiagonal = (float)Math.Sqrt(45f * 45f + 35f * 35f);

            Assert.Less(DirectionGaugeRules.MaxRange(SoundType.Shout, 22f), mapDiagonal);
        }

        [Test]
        public void Gauge_LightsOnlyWithinRange()
        {
            Assert.IsTrue(DirectionGaugeRules.ShouldLight(SoundType.Talk, RoleType.Seeker, 9f, 13f));
            Assert.IsFalse(DirectionGaugeRules.ShouldLight(SoundType.Talk, RoleType.Seeker, 9f, 14f));
        }

        [Test]
        public void Gauge_RunnerNeverLights()
        {
            Assert.IsFalse(DirectionGaugeRules.ShouldLight(SoundType.Shout, RoleType.Runner, 22f, 1f));
        }

        // ── §5.2-3 캘리브레이션 ──────────────────────────────────────────

        [Test]
        public void Calibration_PromptIsThreeSeconds()
        {
            Assert.AreEqual(3f, VoiceCalibration.PromptSeconds, 0.001f);
        }

        [Test]
        public void Calibration_AveragesMeasuredLevels()
        {
            var cal = new VoiceCalibration();
            cal.Begin();

            // 3초(§5.2-3 프롬프트) = 0.02초 × 150프레임. 넉넉히 넘긴다.
            for (int i = 0; i < 160; i++)
                cal.Tick(-40f, 0.02f);

            Assert.AreEqual(-40f, cal.Baseline, 0.01f);
            Assert.IsFalse(cal.IsMeasuring, "3초가 지나면 측정이 끝나야 한다.");
        }

        [Test]
        public void Calibration_IgnoresSilentFrames()
        {
            // 말하지 않은 구간이 섞이면 baseline이 실제보다 낮아져 오프셋이 과해진다.
            var cal = new VoiceCalibration();
            cal.Begin();

            for (int i = 0; i < 50; i++)
            {
                cal.Tick(-40f, 0.02f);
                cal.Tick(VoiceConfig.MinDbfs, 0.02f);
            }

            Assert.AreEqual(-40f, cal.Baseline, 0.01f);
        }

        [Test]
        public void Calibration_QuietMicGetsPositiveOffset()
        {
            // 마이크가 조용하면(baseline이 속삭임 하한보다 낮으면) 전체를 올려야 한다.
            float offset = VoiceCalibration.OffsetFor(-60f);

            Assert.Greater(offset, 0f);
            Assert.AreEqual(VoiceConfig.WhisperFloorDbfs, -60f + offset, 0.001f);
        }

        [Test]
        public void Calibration_LoudMicGetsNegativeOffset()
        {
            float offset = VoiceCalibration.OffsetFor(-35f);

            Assert.Less(offset, 0f);
            Assert.AreEqual(VoiceConfig.WhisperFloorDbfs, -35f + offset, 0.001f);
        }

        [Test]
        public void Calibration_OffsetIsClamped()
        {
            // 잘못된 측정 한 번이 등급 체계를 통째로 망가뜨리면 안 된다(GAP-54).
            Assert.AreEqual(VoiceCalibration.MaxOffsetDb, VoiceCalibration.OffsetFor(-119f), 0.001f);
            Assert.AreEqual(-VoiceCalibration.MaxOffsetDb, VoiceCalibration.OffsetFor(0f), 0.001f);
        }

        [Test]
        public void Calibration_NoSamplesMeansNoCorrection()
        {
            var cal = new VoiceCalibration();
            cal.Begin();

            for (int i = 0; i < 200; i++)
                cal.Tick(VoiceConfig.MinDbfs, 0.02f); // 아무 말도 하지 않음

            Assert.AreEqual(0f, cal.ResolveOffsetDb(), 0.001f,
                "측정 실패가 조용히 잘못된 보정으로 이어지면 안 된다.");
        }

        // ── §5.2-4 대역 필터 ─────────────────────────────────────────────

        [Test]
        public void Bandpass_BandEdgesMatchDesignDoc()
        {
            // §5.2-4 "80Hz~3,400Hz"
            Assert.AreEqual(80f, VoiceBandpass.HighPassHz, 0.001f);
            Assert.AreEqual(3400f, VoiceBandpass.LowPassHz, 0.001f);
        }

        [Test]
        public void Bandpass_AttenuatesDcOffset()
        {
            // 상수 신호(0Hz)는 고역통과가 걷어내야 한다 — 팬 소음 등 저역 노이즈의 극단 사례.
            var filter = new VoiceBandpass();
            var samples = new float[VoiceConfig.SampleRateHz]; // 1초
            for (int i = 0; i < samples.Length; i++)
                samples[i] = 0.5f;

            filter.ProcessInPlace(samples);

            float tailRms = VoiceClassifier.Rms(samples, samples.Length);
            Assert.Less(tailRms, 0.5f, "DC 성분이 그대로 남으면 대역 필터가 동작하지 않는 것이다.");
        }

        [Test]
        public void Bandpass_PassesSpeechBandTone()
        {
            // 1kHz(음성 대역 한가운데)는 상당 부분 통과해야 한다.
            var filter = new VoiceBandpass();
            float[] samples = Tone(1000f, VoiceConfig.SampleRateHz);
            float before = VoiceClassifier.Rms(samples);

            filter.ProcessInPlace(samples);

            float after = VoiceClassifier.Rms(samples);
            Assert.Greater(after, before * 0.5f, "음성 대역이 절반 이상 깎이면 분류가 망가진다.");
        }

        [Test]
        public void Bandpass_AttenuatesSubBassMoreThanSpeech()
        {
            // 30Hz(팬·에어컨 대역)가 1kHz보다 더 많이 깎여야 §5.2-4의 목적이 성립한다.
            float speechRatio = SurvivalRatio(1000f);
            float rumbleRatio = SurvivalRatio(30f);

            Assert.Less(rumbleRatio, speechRatio);
        }

        [Test]
        public void Bandpass_HandlesNullAndEmpty()
        {
            var filter = new VoiceBandpass();

            Assert.DoesNotThrow(() => filter.ProcessInPlace(null));
            Assert.DoesNotThrow(() => filter.ProcessInPlace(new float[0]));
        }

        private static float[] Tone(float frequencyHz, int sampleRate)
        {
            var samples = new float[sampleRate]; // 1초
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)Math.Sin(2d * Math.PI * frequencyHz * i / sampleRate);

            return samples;
        }

        private static float SurvivalRatio(float frequencyHz)
        {
            float[] samples = Tone(frequencyHz, VoiceConfig.SampleRateHz);
            float before = VoiceClassifier.Rms(samples);

            new VoiceBandpass().ProcessInPlace(samples);

            return VoiceClassifier.Rms(samples) / before;
        }
    }
}
