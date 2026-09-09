using Marco.Core.Awards;
using Marco.Core.Locomotion;
using Marco.Core.Sound;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// §8 어워드 집계(6단계 갱신본). 순수 로직이라 Unity 없이 그대로 돈다.
    ///
    /// 이전 판정 기준 둘이 기획서에서 **명시적으로 폐기**됐다:
    /// 최다 비명상의 `Shout` 집계(→ `Scream`), 무성 생존상의 "파문 0회" 이진 판정
    /// (→ Σ소음량 ÷ 이동거리 최솟값). 이 파일은 그 교체를 고정한다.
    /// </summary>
    public class AwardTallyTests
    {
        // §5.1 표에서 가져온 실측 규격 — 테스트가 임의 수치를 만들지 않도록.
        private const float WalkRadius = 2f;      // 걷기
        private const float WalkDuration = 0.4f;
        private const float ScreamRadius = 9f;    // 비명
        private const float ScreamDuration = 1f;
        private const float ShoutRadius = 22f;    // 고함
        private const float ShoutDuration = 2.5f;
        private const float ValveRadius = 12f;    // 밸브 회전음
        private const float ValveDuration = 3f;

        private static void Walk(AwardTally t, int id) =>
            t.RecordPulse(id, SoundType.Walk, WalkRadius, WalkDuration);

        private static void Scream(AwardTally t, int id) =>
            t.RecordPulse(id, SoundType.Scream, ScreamRadius, ScreamDuration);

        private static void Shout(AwardTally t, int id) =>
            t.RecordPulse(id, SoundType.Shout, ShoutRadius, ShoutDuration);

        private static void Valve(AwardTally t, int id) =>
            t.RecordPulse(id, SoundType.Valve, ValveRadius, ValveDuration);

        [Test]
        public void NoData_YieldsNoWinners()
        {
            AwardResults results = new AwardTally().Evaluate();

            Assert.AreEqual(AwardTally.NoWinner, results.LoudestScream);
            Assert.AreEqual(AwardTally.NoWinner, results.SilentSurvivor);
            Assert.AreEqual(AwardTally.NoWinner, results.BestLiar);
        }

        // ── §8.1 최다 비명상 (Scream 집계) ───────────────────────────────

        [Test]
        public void LoudestScream_CountsScreams()
        {
            var t = new AwardTally();
            Scream(t, 1);
            Scream(t, 2);
            Scream(t, 2);

            Assert.AreEqual(2, t.Evaluate().LoudestScream);
        }

        [Test]
        public void LoudestScream_IgnoresShoutEntirely()
        {
            // §8.1 "**`Shout` 제외** — 고함 횟수는 판정에 일절 반영하지 않는다".
            var t = new AwardTally();
            for (int i = 0; i < 10; i++)
                Shout(t, 1);

            Scream(t, 2);

            Assert.AreEqual(2, t.Evaluate().LoudestScream,
                "고함 10회가 비명 1회를 이기면 §8.1 갱신이 반영되지 않은 것이다.");
        }

        [Test]
        public void LoudestScream_ShoutOnly_HasNoWinner()
        {
            var t = new AwardTally();
            Shout(t, 1);
            Shout(t, 2);

            Assert.AreEqual(AwardTally.NoWinner, t.Evaluate().LoudestScream);
        }

        [Test]
        public void LoudestScream_SuppressedScreamIsNotCounted()
        {
            // §8.1 "억제 성공 시 — 비명이 발생하지 않았다면 집계하지 않는다(이벤트 자체가 없음)".
            // 억제에 성공하면 Scream 파문이 아예 만들어지지 않아 RecordPulse가 불리지 않는다.
            var t = new AwardTally();
            Scream(t, 1);
            // 플레이어 2는 숨을 참아 비명이 발생하지 않았다 → 아무것도 기록되지 않는다.

            AwardResults r = t.Evaluate();

            Assert.AreEqual(1, r.LoudestScream);
            t.Peek(2, out int screams, out _, out _, out _);
            Assert.AreEqual(0, screams);
        }

        [Test]
        public void LoudestScream_CountsEvenIfSeekerCouldNotHear()
        {
            // §8.1 "청취 여부 무관 — 술래가 10.8m 밖이라 듣지 못했어도 집계한다".
            // 집계는 **발생** 시점에 이뤄지므로 청취자 판정과 무관하다 —
            // 그 사실을 여기서 못박는다(집계 API에 청취자 인자가 아예 없다).
            var t = new AwardTally();
            Scream(t, 7);

            Assert.AreEqual(7, t.Evaluate().LoudestScream);
        }

        [Test]
        public void LoudestScream_TieGoesToLowerId()
        {
            var t = new AwardTally();
            Scream(t, 5);
            Scream(t, 3);

            Assert.AreEqual(3, t.Evaluate().LoudestScream);
        }

        // ── §8.2 무성 생존상 (Σ소음량 ÷ 이동거리, 최솟값) ────────────────

        [Test]
        public void NoiseOf_IsLinearNotSquared()
        {
            // §8.2 "파문 소음량(1회) = 발생 반경 × 지속시간 ← 제곱이 아니라 선형".
            Assert.AreEqual(0.8f, AwardTally.NoiseOf(WalkRadius, WalkDuration), 0.0001f);
            Assert.AreEqual(9f, AwardTally.NoiseOf(ScreamRadius, ScreamDuration), 0.0001f);
            Assert.AreEqual(55f, AwardTally.NoiseOf(ShoutRadius, ShoutDuration), 0.0001f);
        }

        [Test]
        public void SilentSurvivor_GoesToLowestScore()
        {
            var t = new AwardTally();

            // 1: 걷기 2회(0.8×2 = 1.6) ÷ 100m = 0.016
            Walk(t, 1);
            Walk(t, 1);
            t.AddDistance(1, 100f);

            // 2: 걷기 1회(0.8) ÷ 10m = 0.08  ← 더 시끄럽다
            Walk(t, 2);
            t.AddDistance(2, 10f);

            Assert.AreEqual(0.016f, t.SilenceScore(1), 0.0001f);
            Assert.AreEqual(0.08f, t.SilenceScore(2), 0.0001f);
            Assert.AreEqual(1, t.Evaluate().SilentSurvivor, "무성 점수는 **낮을수록** 좋다.");
        }

        [Test]
        public void SilentSurvivor_ExcludesValveNoise()
        {
            // §8.2 "**밸브 제외** — 목표 수행을 처벌하지 않기 위해".
            var t = new AwardTally();
            Valve(t, 1);
            Valve(t, 1);
            t.AddDistance(1, 10f);

            Assert.AreEqual(0f, t.SilenceScore(1), 0.0001f,
                "밸브만 돌린 플레이어의 소음량은 0이어야 한다(목표 수행 무처벌).");
        }

        [Test]
        public void SilentSurvivor_ValveDoerBeatsWalker()
        {
            // 갱신 전 제곱 공식의 결함: 밸브 수행자가 처벌당했다.
            var t = new AwardTally();

            Valve(t, 1);          // 밸브는 합계에서 빠진다
            Walk(t, 1);           // 0.8
            t.AddDistance(1, 50f);

            Walk(t, 2);           // 0.8
            Walk(t, 2);           // 0.8
            t.AddDistance(2, 50f);

            Assert.AreEqual(1, t.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_ShoutOnceStillEligible()
        {
            // 갱신 전 결함: "고함 1회로 수상이 불가능"해지는 것을 선형화로 해소했다.
            var t = new AwardTally();

            Shout(t, 1);              // 55
            t.AddDistance(1, 1000f);  // 0.055

            Walk(t, 2);               // 0.8
            t.AddDistance(2, 5f);     // 0.16

            Assert.AreEqual(1, t.Evaluate().SilentSurvivor,
                "선형 공식에서는 고함 1회를 낸 플레이어도 수상할 수 있다.");
        }

        [Test]
        public void SilentSurvivor_MovingIsNotDisqualifying()
        {
            // 갱신 전 기준("파문 0회")은 달성 불가능했다 — 움직이면 반드시 발소리가 난다.
            var t = new AwardTally();
            for (int i = 0; i < 50; i++)
                Walk(t, 1);
            t.AddDistance(1, 100f);

            Assert.AreEqual(1, t.Evaluate().SilentSurvivor,
                "발소리를 냈다는 이유만으로 후보에서 빠지면 갱신이 반영되지 않은 것이다.");
        }

        [TestCase(0.99f, false)]
        [TestCase(1f, true)]
        [TestCase(1.01f, true)]
        public void SilentSurvivor_RequiresOneMeter(float distance, bool eligible)
        {
            // §8.2 "총 이동거리 **1m 미만이면 수상 대상 제외**"(0 나누기 방어).
            var t = new AwardTally();
            Walk(t, 1);
            t.AddDistance(1, distance);

            Assert.AreEqual(eligible ? 1 : AwardTally.NoWinner, t.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_StandingStillCannotWin()
        {
            // 가만히 서 있으면 소음량 0이라 점수가 0이지만, 이동거리 0이라 후보가 아니다.
            var t = new AwardTally();
            t.Track(1);

            Walk(t, 2);
            t.AddDistance(2, 50f);

            Assert.AreEqual(2, t.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_ExcludesTaggedOutPlayers()
        {
            // §8.2 "생존 요건 — 탈출 또는 미탈출만 수상 대상. **태그당한 메아리는 제외**".
            var t = new AwardTally();

            Walk(t, 1);
            t.AddDistance(1, 1000f);  // 점수 0.0008 — 압도적 1위
            t.MarkTaggedOut(1);

            Walk(t, 2);
            t.AddDistance(2, 10f);    // 0.08

            Assert.AreEqual(float.PositiveInfinity, t.SilenceScore(1));
            Assert.AreEqual(2, t.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_TagIsIrreversible()
        {
            // §3.2상 태그당하면 라운드 종료까지 메아리다 — 이후 이동으로 자격이 돌아오지 않는다.
            var t = new AwardTally();
            Walk(t, 1);
            t.MarkTaggedOut(1);
            t.AddDistance(1, 500f);

            Assert.AreEqual(AwardTally.NoWinner, t.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_CountsPulseOncePerEvent()
        {
            // §8.2 "중복 집계 금지 — 한 파문이 여러 명에게 들려도 1회만. 청취자 수 무관".
            // 집계 API에 청취자 인자가 없다는 것이 그 구조적 보장이다.
            var t = new AwardTally();
            Walk(t, 1);
            t.AddDistance(1, 10f);

            t.Peek(1, out _, out float noise, out _, out _);
            Assert.AreEqual(0.8f, noise, 0.0001f);
        }

        [Test]
        public void SilentSurvivor_UsesMaterialAdjustedRadius()
        {
            // §8.2 "발생 반경 — **재질 배율 적용 후**의 값 사용(타일 걷기 = 2 × 1.3 = 2.6m)".
            float tiled = FootstepMaterialRules.ApplyToRadius(LocomotionConfig.WalkPulseRadius, FootstepMaterial.Tile);
            Assert.AreEqual(2.6f, tiled, 0.0001f);

            var t = new AwardTally();
            t.RecordPulse(1, SoundType.Walk, tiled, LocomotionConfig.WalkPulseDuration);
            t.AddDistance(1, 10f);

            t.Peek(1, out _, out float noise, out _, out _);
            Assert.AreEqual(2.6f * 0.4f, noise, 0.0001f,
                "재질 배율이 빠지면 §5.9 효과가 소음량에서 사라진다.");
        }

        [Test]
        public void SilentSurvivor_TieGoesToLowerId()
        {
            var t = new AwardTally();
            Walk(t, 5);
            t.AddDistance(5, 10f);
            Walk(t, 3);
            t.AddDistance(3, 10f);

            Assert.AreEqual(3, t.Evaluate().SilentSurvivor);
        }

        // ── §8.3 최고의 거짓말상 ─────────────────────────────────────────

        [Test]
        public void BestLiar_GoesToMostLures()
        {
            var t = new AwardTally();
            t.RecordKnockLure(1);
            t.RecordKnockLure(2);
            t.RecordKnockLure(2);

            Assert.AreEqual(2, t.Evaluate().BestLiar);
        }

        [Test]
        public void BestLiar_NoLures_NoWinner()
        {
            var t = new AwardTally();
            Walk(t, 1);

            Assert.AreEqual(AwardTally.NoWinner, t.Evaluate().BestLiar);
        }

        // ── 공통 ─────────────────────────────────────────────────────────

        [Test]
        public void Track_RegistersWithoutData()
        {
            var t = new AwardTally();
            t.Track(9);

            Assert.AreEqual(1, t.TrackedPlayers);
            t.Peek(9, out int screams, out float noise, out float distance, out int lures);
            Assert.AreEqual(0, screams);
            Assert.AreEqual(0f, noise, 0.0001f);
            Assert.AreEqual(0f, distance, 0.0001f);
            Assert.AreEqual(0, lures);
        }

        [Test]
        public void AddDistance_IgnoresNonPositive()
        {
            var t = new AwardTally();
            t.AddDistance(1, -5f);
            t.AddDistance(1, 0f);

            t.Peek(1, out _, out _, out float distance, out _);
            Assert.AreEqual(0f, distance, 0.0001f);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var t = new AwardTally();
            Scream(t, 1);
            t.AddDistance(1, 10f);
            t.RecordKnockLure(1);
            t.MarkTaggedOut(1);

            t.Reset();

            Assert.AreEqual(0, t.TrackedPlayers);
            AwardResults r = t.Evaluate();
            Assert.AreEqual(AwardTally.NoWinner, r.LoudestScream);
            Assert.AreEqual(AwardTally.NoWinner, r.SilentSurvivor);
            Assert.AreEqual(AwardTally.NoWinner, r.BestLiar);
        }

        [Test]
        public void AwardResults_ForKindMatchesFields()
        {
            var results = new AwardResults(1, 2, 3);

            Assert.AreEqual(1, results.For(AwardKind.LoudestScream));
            Assert.AreEqual(2, results.For(AwardKind.SilentSurvivor));
            Assert.AreEqual(3, results.For(AwardKind.BestLiar));
        }
    }
}
