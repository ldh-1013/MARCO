using Marco.Core.Awards;
using Marco.Core.Sound;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 22 어워드 집계(§8). 순수 로직이라 Unity 없이 그대로 돈다.
    /// </summary>
    public class AwardTallyTests
    {
        [Test]
        public void NoData_YieldsNoWinners()
        {
            AwardResults results = new AwardTally().Evaluate();

            Assert.AreEqual(AwardTally.NoWinner, results.LoudestScream);
            Assert.AreEqual(AwardTally.NoWinner, results.SilentSurvivor);
            Assert.AreEqual(AwardTally.NoWinner, results.BestLiar);
        }

        // ── 최다 비명상 (§8 "SoundType.Shout 발생 횟수") ──────────────────

        [Test]
        public void LoudestScream_GoesToMostShouts()
        {
            var tally = new AwardTally();
            tally.RecordPulse(1, SoundType.Shout);
            tally.RecordPulse(2, SoundType.Shout);
            tally.RecordPulse(2, SoundType.Shout);

            Assert.AreEqual(2, tally.Evaluate().LoudestScream);
        }

        [Test]
        public void LoudestScream_IgnoresNonShoutPulses()
        {
            var tally = new AwardTally();
            tally.RecordPulse(1, SoundType.Walk);
            tally.RecordPulse(1, SoundType.Sprint);
            tally.RecordPulse(1, SoundType.Valve);

            Assert.AreEqual(AwardTally.NoWinner, tally.Evaluate().LoudestScream,
                "비명이 0회면 수상자가 없어야 한다.");
        }

        [Test]
        public void LoudestScream_TieGoesToLowerId()
        {
            var tally = new AwardTally();
            tally.RecordPulse(5, SoundType.Shout);
            tally.RecordPulse(3, SoundType.Shout);

            // GAP-38: 기획서에 동점 규칙이 없어 결정론적으로 낮은 id를 택한다.
            Assert.AreEqual(3, tally.Evaluate().LoudestScream);
        }

        // ── 무성 생존상 (§8 "이동거리 대비 파문 발생 0회 여부") ────────────

        [Test]
        public void SilentSurvivor_RequiresZeroPulsesAndSomeMovement()
        {
            var tally = new AwardTally();
            tally.AddDistance(1, 40f); // 움직였고 파문 없음 → 자격 있음

            tally.AddDistance(2, 90f);
            tally.RecordPulse(2, SoundType.Walk); // 파문을 냈으므로 실격

            Assert.AreEqual(1, tally.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_ExcludesPlayersWhoNeverMoved()
        {
            var tally = new AwardTally();
            tally.Track(1); // 접속만 하고 가만히 있음 — 파문 0회지만 이동도 0

            Assert.AreEqual(AwardTally.NoWinner, tally.Evaluate().SilentSurvivor,
                "가만히 서 있던 플레이어가 '무성 생존'으로 수상하면 안 된다.");
        }

        [Test]
        public void SilentSurvivor_PrefersGreaterDistance()
        {
            var tally = new AwardTally();
            tally.AddDistance(1, 10f);
            tally.AddDistance(2, 120f);

            Assert.AreEqual(2, tally.Evaluate().SilentSurvivor);
        }

        [Test]
        public void SilentSurvivor_ShoutAlsoDisqualifies()
        {
            var tally = new AwardTally();
            tally.AddDistance(1, 50f);
            tally.RecordPulse(1, SoundType.Shout);

            Assert.AreEqual(AwardTally.NoWinner, tally.Evaluate().SilentSurvivor,
                "비명도 파문이므로 무성 조건을 깬다.");
        }

        // ── 최고의 거짓말상 (§8 "메아리 노크 성공 유인 횟수") ──────────────

        [Test]
        public void BestLiar_GoesToMostKnockLures()
        {
            var tally = new AwardTally();
            tally.RecordKnockLure(4);
            tally.RecordKnockLure(7);
            tally.RecordKnockLure(7);

            Assert.AreEqual(7, tally.Evaluate().BestLiar);
        }

        [Test]
        public void BestLiar_HasNoWinnerWhileKnockIsUnimplemented()
        {
            // GAP-37: 노크(§3.2)가 미구현이라 RecordKnockLure 호출자가 없다.
            // 파문·이동만 기록된 세션에서는 수상자가 없어야 한다.
            var tally = new AwardTally();
            tally.RecordPulse(1, SoundType.Shout);
            tally.AddDistance(1, 30f);

            Assert.AreEqual(AwardTally.NoWinner, tally.Evaluate().BestLiar);
        }

        // ── 누적·초기화 ──────────────────────────────────────────────────

        [Test]
        public void Tally_AccumulatesAcrossRounds()
        {
            // §8이 "세션 로그"라고 하므로 라운드가 아니라 세션 누적이다.
            var tally = new AwardTally();
            tally.RecordPulse(1, SoundType.Shout); // 1라운드
            tally.RecordPulse(1, SoundType.Shout); // 2라운드
            tally.RecordPulse(2, SoundType.Shout);

            tally.Peek(1, out int shouts, out _, out _, out _);
            Assert.AreEqual(2, shouts);
            Assert.AreEqual(1, tally.Evaluate().LoudestScream);
        }

        [Test]
        public void AddDistance_IgnoresNonPositiveValues()
        {
            var tally = new AwardTally();
            tally.AddDistance(1, -5f);
            tally.AddDistance(1, 0f);

            tally.Peek(1, out _, out _, out float distance, out _);
            Assert.AreEqual(0f, distance, 0.0001f);
            Assert.AreEqual(AwardTally.NoWinner, tally.Evaluate().SilentSurvivor);
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var tally = new AwardTally();
            tally.RecordPulse(1, SoundType.Shout);
            tally.AddDistance(2, 10f);

            tally.Reset();

            Assert.AreEqual(0, tally.TrackedPlayers);
            Assert.AreEqual(AwardTally.NoWinner, tally.Evaluate().LoudestScream);
        }

        [Test]
        public void Peek_ReturnsZeroesForUnknownPlayer()
        {
            new AwardTally().Peek(99, out int shouts, out int pulses, out float distance, out int lures);

            Assert.AreEqual(0, shouts);
            Assert.AreEqual(0, pulses);
            Assert.AreEqual(0f, distance, 0.0001f);
            Assert.AreEqual(0, lures);
        }

        [Test]
        public void Results_ForKind_MatchesFields()
        {
            var results = new AwardResults(1, 2, 3);

            Assert.AreEqual(1, results.For(AwardKind.LoudestScream));
            Assert.AreEqual(2, results.For(AwardKind.SilentSurvivor));
            Assert.AreEqual(3, results.For(AwardKind.BestLiar));
        }
    }
}
