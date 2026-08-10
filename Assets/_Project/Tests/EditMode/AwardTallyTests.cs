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

        // ── 메아리 제외 (GAP-56, 스프린트 27 선행 재검증) ──────────────────
        //
        // §8은 역할을 명시하지 않지만, 메아리는 §3.2상 발소리를 내지 않아 "파문 0회"를
        // **구조적으로** 충족하고 이동속도까지 가장 빠르다(8.0m/s). 태그 아웃 이후의 이동을
        // 세면 먼저 죽은 사람이 "무성 **생존**상"을 가져간다. 그래서 집계 측
        // (RoundNetworkSync.AccumulateDistances)이 메아리 구간을 넣지 않기로 했다.
        //
        // 아래 두 테스트는 그 계약을 **기대 결과로** 고정한다 — 집계 측이 다시 메아리
        // 거리를 흘려보내면 어떤 값이 나오는지까지 함께 박아 둔다.

        [Test]
        public void SilentSurvivor_EchoDistanceExcluded_GoesToActualSurvivor()
        {
            var tally = new AwardTally();

            // 1번: 초반에 태그당한 메아리. 태그 전까지 걸은 12m만 들어오고,
            // 그 구간에서 발소리를 냈으므로 파문도 함께 기록된다.
            tally.AddDistance(1, 12f);
            tally.RecordPulse(1, SoundType.Walk);

            // 2번: 끝까지 살아남아 잠수(§4.2 — 파문 없음)로만 이동한 러너.
            tally.AddDistance(2, 30f);

            Assert.AreEqual(2, tally.Evaluate().SilentSurvivor,
                "메아리 구간이 빠지면 실제 생존자가 수상해야 한다.");
        }

        [Test]
        public void SilentSurvivor_IfEchoDistanceLeaksIn_EchoWinsWrongly()
        {
            // 회귀 감시용 반례: 집계 측이 메아리 이동을 그대로 넣으면 이렇게 된다.
            // 태그당한 뒤 8.0m/s로 날아다니며 파문 없이 거리만 쌓기 때문이다.
            var tally = new AwardTally();

            tally.AddDistance(1, 400f); // 메아리가 라운드 내내 비행한 거리(파문 0회)
            tally.AddDistance(2, 30f);  // 실제 생존자

            Assert.AreEqual(1, tally.Evaluate().SilentSurvivor,
                "이 결과가 나오면 AccumulateDistances의 IsTaggedOut 제외가 빠진 것이다(GAP-56).");
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
