using Marco.Core.Sound;
using Marco.Presentation.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 목소리 조명 리빌(2026-09-27 디자인 변경) — range = 링 반경(0 → R), intensity = Peak × (1 − 진행도).
    /// 반경 · 지속은 §5.1(ServerPulseDriver) 값을 그대로 쓴다.
    /// </summary>
    public class VoicePulseLightTests
    {
        private const float Eps = 1e-4f;

        // ── 설정값 고정 ───────────────────────────────────────────────

        [Test]
        public void Config_PrototypeValues()
        {
            Assert.AreEqual(40f, VoicePulseLightConfig.TalkPeakIntensity, Eps, "프로토타입 값 — 튜닝하면 이 단언도 함께 바꾼다");
            Assert.AreEqual(2, VoicePulseLightConfig.MaxConcurrent, "동시 조명 상한 2");
            Assert.IsFalse(VoicePulseLightConfig.LightOthersVoicePulses, "기본은 자기 목소리만");
        }

        // ── 등급별 세기: 대화 기준 × (반경 ÷ 대화 반경)² ──────────────────

        [TestCase(SoundType.Whisper, 4f)]
        [TestCase(SoundType.Talk, 9f)]
        [TestCase(SoundType.Shout, 22f)]
        public void PeakIntensity_DefaultsDerivedFromTalk_ByRadiusRatioSquared(SoundType type, float radius)
        {
            float expected = VoicePulseLightConfig.TalkPeakIntensity * (radius / 9f) * (radius / 9f);
            Assert.AreEqual(expected, VoicePulseLightConfig.PeakIntensityFor(type), 1e-3f);
        }

        [Test]
        public void PeakIntensity_PinnedDefaults()
        {
            // 40 × (4/9)² = 7.901 · 40 × (22/9)² = 239.012 — 기본값이 유도식에서 벗어나면(한쪽만 튜닝) 여기서 드러난다.
            Assert.AreEqual(7.901f, VoicePulseLightConfig.WhisperPeakIntensity, 1e-3f);
            Assert.AreEqual(40f, VoicePulseLightConfig.TalkPeakIntensity, Eps);
            Assert.AreEqual(239.012f, VoicePulseLightConfig.ShoutPeakIntensity, 1e-3f);
        }

        [Test]
        public void PeakIntensity_SameBrightnessAtSameFractionOfRadius()
        {
            // 1/d² 감쇠 — 반경의 40% 거리에서 세 등급이 같은 조도(세기 ÷ 거리²)가 된다는 것이 유도식의 뜻이다.
            float Illuminance(SoundType t, float r) => VoicePulseLightConfig.PeakIntensityFor(t) / ((0.4f * r) * (0.4f * r));
            float talk = Illuminance(SoundType.Talk, 9f);
            Assert.AreEqual(talk, Illuminance(SoundType.Whisper, 4f), 1e-3f);
            Assert.AreEqual(talk, Illuminance(SoundType.Shout, 22f), 1e-3f);
        }

        [Test]
        public void Config_ColorIsAchromatic()
        {
            Color c = VoicePulseLightConfig.LightColor;
            float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
            Assert.LessOrEqual(max - min, 0.1f, "§16.1 모노크롬 — 흰색~약간 차가운 회색");
            Assert.GreaterOrEqual(c.b, c.r, "차가운 쪽(푸른 기)으로만 기운다");
        }

        // ── 시간 규칙 ────────────────────────────────────────────────

        [TestCase(0f, 0f, 40f)]
        [TestCase(0.5f, 4.5f, 20f)]
        [TestCase(1f, 9f, 0f)]
        public void RangeAndIntensity_FollowRing(float progress, float expectedRange, float expectedIntensity)
        {
            // 대화 9m — 링 반경과 같이 넓어지고, 링의 페이드 곡선으로 흐려진다.
            Assert.AreEqual(expectedRange, VoicePulseLightMath.Range(9f, progress), Eps);
            Assert.AreEqual(expectedIntensity, VoicePulseLightMath.Intensity(VoicePulseLightConfig.TalkPeakIntensity, progress), Eps);
        }

        [Test]
        public void Progress_ClampsAndHandlesZeroDuration()
        {
            Assert.AreEqual(0f, VoicePulseLightMath.Progress(10f, 10f, 1.2f), Eps);
            Assert.AreEqual(0.5f, VoicePulseLightMath.Progress(10.6f, 10f, 1.2f), Eps);
            Assert.AreEqual(1f, VoicePulseLightMath.Progress(20f, 10f, 1.2f), Eps);
            Assert.AreEqual(1f, VoicePulseLightMath.Progress(10f, 10f, 0f), Eps);
        }

        [TestCase(SoundType.Whisper, 4f, 0.6f)]
        [TestCase(SoundType.Talk, 9f, 1.2f)]
        [TestCase(SoundType.Shout, 22f, 2.5f)]
        public void VoiceGrades_UseSection51RadiusAndDuration(SoundType type, float radius, float duration)
        {
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(type, out float r, out float d));
            Assert.AreEqual(radius, r, Eps, "§5.1 반경 — 새로 만들지 않는다");
            Assert.AreEqual(duration, d, Eps, "§5.1 지속");

            Assert.IsTrue(VoicePulseLightMath.ShouldLight(type, isLocalSource: true));
            Assert.AreEqual(radius, VoicePulseLightMath.Range(r, 1f), Eps, "링 끝 = §5.1 반경");
            Assert.AreEqual(radius * 0.5f, VoicePulseLightMath.Range(r, VoicePulseLightMath.Progress(d * 0.5f, 0f, d)), Eps);
        }

        [TestCase(SoundType.Walk)]
        [TestCase(SoundType.Sprint)]
        [TestCase(SoundType.Valve)]
        [TestCase(SoundType.Scream)]
        [TestCase(SoundType.Knock)]
        [TestCase(SoundType.Breath)]
        public void NonVoice_NeverLights(SoundType type)
        {
            Assert.IsFalse(VoicePulseLightMath.ShouldLight(type, isLocalSource: true), "발소리 등은 윤곽선만");
        }

        [Test]
        public void OthersVoice_NotLit_ByDefault()
        {
            Assert.IsFalse(VoicePulseLightMath.ShouldLight(SoundType.Talk, isLocalSource: false));
        }

        // ── 동시 2개 제한 ─────────────────────────────────────────────

        [Test]
        public void Slots_ThirdEvictsOldest()
        {
            var slots = new VoiceLightSlots(VoicePulseLightConfig.MaxConcurrent);
            int a = slots.Acquire(1f);
            int b = slots.Acquire(2f);
            Assert.AreNotEqual(a, b);
            Assert.AreEqual(2, slots.ActiveCount);

            int c = slots.Acquire(3f);
            Assert.AreEqual(a, c, "가득 차면 가장 오래된 슬롯을 회수");
            Assert.AreEqual(3f, slots.StartTime(c), Eps);
            Assert.AreEqual(2, slots.ActiveCount, "상한 2 유지");

            int d = slots.Acquire(4f);
            Assert.AreEqual(b, d, "다음으로 오래된 것");
        }

        [Test]
        public void Slots_ReleasedSlotReusedBeforeEvicting()
        {
            var slots = new VoiceLightSlots(2);
            int a = slots.Acquire(1f);
            int b = slots.Acquire(2f);
            slots.Release(b);
            Assert.AreEqual(b, slots.Acquire(3f), "빈 슬롯이 있으면 회수하지 않는다");
            Assert.IsTrue(slots.IsActive(a));
        }
    }
}
