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
            Assert.AreEqual(14f, VoicePulseLightConfig.TalkPeakIntensity, Eps, "09-30 실기 결정값 — 바꾸면 이 단언도 함께 바꾼다");
            Assert.AreEqual(28f, VoicePulseLightConfig.MaxPeakIntensity, Eps, "09-30 실기 결정 상한");
            Assert.AreEqual(2, VoicePulseLightConfig.MaxConcurrent, "동시 조명 상한 2");
            Assert.IsFalse(VoicePulseLightConfig.LightOthersVoicePulses, "기본은 자기 목소리만");
        }

        // ── 등급별 세기: 대화 기준 × (반경 ÷ 대화 반경) — 반경에 1제곱 비례 ─────────────
        // (09-30 실기: 제곱 비례 × 1/d² 감쇠가 겹쳐 2m 벽에서 대화 10 · 고함 ≈ 60이 됐다 — 대화는 회백색, 고함은 완전 백색, 속삭임은 안 보임.)

        [SetUp]
        [TearDown]
        public void ResetQa() => VoicePulseLightConfig.ResetQaMultiplier();

        [TestCase(SoundType.Whisper, 4f)]
        [TestCase(SoundType.Talk, 9f)]
        [TestCase(SoundType.Shout, 22f)]
        public void PeakIntensity_DefaultsDerivedFromTalk_ByRadiusRatio_CappedAt28(SoundType type, float radius)
        {
            float expected = Mathf.Min(VoicePulseLightConfig.TalkPeakIntensity * (radius / 9f), VoicePulseLightConfig.MaxPeakIntensity);
            Assert.AreEqual(expected, VoicePulseLightConfig.PeakIntensityFor(type), 1e-3f);
        }

        [Test]
        public void PeakIntensity_ShoutIsCapped_NotRadiusProportional()
        {
            // 14 × 22/9 ≈ 34.2 — 상한 28에서 멈춘다(가까운 벽에서 고함이 하얗게 날아가던 것, 09-30 실기).
            Assert.Greater(VoicePulseLightConfig.TalkPeakIntensity * 22f / 9f, VoicePulseLightConfig.MaxPeakIntensity);
            Assert.AreEqual(VoicePulseLightConfig.MaxPeakIntensity, VoicePulseLightConfig.ShoutPeakIntensity, 1e-3f);
        }

        [Test]
        public void PeakIntensity_PinnedDefaults()
        {
            // 09-30 실기 결정: 대화 14 · 속삭임 14 × 4/9 ≈ 6.22 · 고함 min(14 × 22/9 ≈ 34.2, 상한 28) = 28.
            Assert.AreEqual(6.222f, VoicePulseLightConfig.WhisperPeakIntensity, 1e-3f);
            Assert.AreEqual(14f, VoicePulseLightConfig.TalkPeakIntensity, Eps);
            Assert.AreEqual(28f, VoicePulseLightConfig.ShoutPeakIntensity, 1e-3f);
        }

        [Test]
        public void PeakIntensity_AtTwoMetreWall_IsNotBlownOut()
        {
            // 09-30 실기 재현: ⑫ 배수로 출구에서 약 2m 앞 벽. 1/d² 감쇠라 벽에 닿는 조도 = 세기 ÷ 4.
            float Wall(SoundType t) => VoicePulseLightConfig.PeakIntensityFor(t) / (2f * 2f);
            Assert.Less(Wall(SoundType.Shout), 12f, "이전 값은 ≈ 60(완전 백색) — 대화의 3배를 넘지 않게");
            Assert.Less(Wall(SoundType.Talk), 5f, "이전 값은 10(회백색)");
            Assert.Greater(Wall(SoundType.Whisper), 1f, "이전 값은 ≈ 2였지만 대화(10) 옆에서 묻혔다 — 대화와의 비가 1:2.25이면 구분된다");
            Assert.Less(Wall(SoundType.Whisper), Wall(SoundType.Talk));
            Assert.Less(Wall(SoundType.Talk), Wall(SoundType.Shout));
        }

        // ── QA 배율(F6/F7) ───────────────────────────────────────────────

        [Test]
        public void QaMultiplier_DefaultsToOne_AndScalesAllThreeGrades()
        {
            Assert.AreEqual(1f, VoicePulseLightConfig.QaMultiplier, Eps);

            float bright = VoicePulseLightConfig.AdjustQaMultiplier(+1);
            Assert.AreEqual(1.25f, bright, Eps, "F7 = ×1.25");
            foreach (SoundType t in new[] { SoundType.Whisper, SoundType.Talk, SoundType.Shout })
                Assert.AreEqual(VoicePulseLightConfig.BasePeakIntensityFor(t) * 1.25f, VoicePulseLightConfig.PeakIntensityFor(t), 1e-3f,
                    $"{t} — 세 값이 함께 움직인다");

            float dim = VoicePulseLightConfig.AdjustQaMultiplier(-2);
            Assert.AreEqual(0.8f, dim, Eps, "F6 = ×0.8");
        }

        [Test]
        public void QaMultiplier_UpThenDown_ReturnsToExactlyOne()
        {
            for (int i = 0; i < 5; i++)
                VoicePulseLightConfig.AdjustQaMultiplier(+1);
            for (int i = 0; i < 5; i++)
                VoicePulseLightConfig.AdjustQaMultiplier(-1);

            Assert.AreEqual(1f, VoicePulseLightConfig.QaMultiplier, 0f, "부동소수 누적 오차 없이 정확히 1");
        }

        [Test]
        public void QaMultiplier_StopsAtLimits()
        {
            for (int i = 0; i < 40; i++)
                VoicePulseLightConfig.AdjustQaMultiplier(+1);
            float max = VoicePulseLightConfig.QaMultiplier;
            Assert.AreEqual(Mathf.Pow(VoicePulseLightConfig.QaStepFactor, VoicePulseLightConfig.QaStepLimit), max, 1e-3f);

            for (int i = 0; i < 80; i++)
                VoicePulseLightConfig.AdjustQaMultiplier(-1);
            Assert.AreEqual(1f / max, VoicePulseLightConfig.QaMultiplier, 1e-3f, "대칭 범위");
        }

        [Test]
        public void QaSummary_ShowsMultiplierAndAllThreeValues()
        {
            Assert.AreEqual("음성 조명 ×1.00 — 속삭임 6.2 · 대화 14 · 고함 28", VoicePulseLightConfig.FormatQaSummary());

            VoicePulseLightConfig.AdjustQaMultiplier(+1);
            Assert.AreEqual("음성 조명 ×1.25 — 속삭임 7.8 · 대화 17.5 · 고함 35", VoicePulseLightConfig.FormatQaSummary());
        }

        [Test]
        public void VoicePulseLighting_AppliesQaMultiplier_ToTheLightsIntensity()
        {
            var root = new GameObject("VoiceLightRoot");
            try
            {
                VoicePulseLightConfig.AdjustQaMultiplier(+1);
                var lighting = new VoicePulseLighting(root.transform);
                lighting.Emit(SoundType.Talk, isLocalSource: true, Vector3.zero, radius: 9f, duration: 1.2f, now: 10f);
                lighting.Tick(10f); // 진행도 0 — 세기 = 피크

                Light light = root.GetComponentInChildren<Light>();
                Assert.AreEqual(VoicePulseLightConfig.TalkPeakIntensity * 1.25f, light.intensity, 1e-3f,
                    "실제 조명 컴포넌트가 QA 배율이 적용된 세기를 쓴다");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
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

        [TestCase(0f, 0f, 14f)]
        [TestCase(0.5f, 4.5f, 7f)]
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
