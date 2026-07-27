using System.Collections.Generic;
using NUnit.Framework;
using Marco.Core.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 18: §12.5 리매치 투표(<see cref="RematchVoteDriver"/>)의 계약을 고정한다 —
    /// GAP-27(즉시 재시작 골격)을 대체한 정식 사양: "15초 카운트다운, 과반 찬성 시 즉시 재시작".
    /// </summary>
    public class RematchVoteDriverTests
    {
        private static List<ulong> Players(params ulong[] ids) => new List<ulong>(ids);

        [Test]
        public void VoteWindow_IsFifteenSeconds_PerDesignDoc()
        {
            Assert.AreEqual(15f, RematchVoteDriver.VoteWindowSeconds); // §12.5
        }

        [TestCase(2, 2)] // 2인: 1표는 50%라 과반 아님 → 2표 필요
        [TestCase(3, 2)]
        [TestCase(4, 3)]
        [TestCase(5, 3)]
        [TestCase(6, 4)]
        public void RequiredVotes_IsStrictMajority(int playerCount, int expected)
        {
            Assert.AreEqual(expected, RematchVoteDriver.RequiredVotes(playerCount));
        }

        [Test]
        public void MajorityReached_PassesImmediately()
        {
            // §12.5 "과반 찬성 시 즉시 재시작" — 15초를 기다리지 않는다.
            var v = new RematchVoteDriver();
            v.TryVote(1);
            v.TryVote(2);
            Assert.AreEqual(RematchVoteResult.Passed, v.Tick(Players(1, 2, 3), 0.1f));
        }

        [Test]
        public void BelowMajority_StaysPending()
        {
            var v = new RematchVoteDriver();
            v.TryVote(1);
            Assert.AreEqual(RematchVoteResult.Pending, v.Tick(Players(1, 2, 3), 0.1f));
        }

        [Test]
        public void WindowExpires_Fails()
        {
            var v = new RematchVoteDriver();
            v.TryVote(1);
            Assert.AreEqual(RematchVoteResult.Failed, v.Tick(Players(1, 2, 3), 15.1f));
            Assert.AreEqual(0f, v.SecondsRemaining);
        }

        [Test]
        public void DuplicateVote_IsIdempotent()
        {
            var v = new RematchVoteDriver();
            Assert.IsTrue(v.TryVote(1));
            Assert.IsFalse(v.TryVote(1), "같은 플레이어의 재투표는 새로 집계되지 않는다");
            Assert.AreEqual(1, v.CountVotes(Players(1, 2)));
        }

        [Test]
        public void VoteAfterDecision_IsIgnored()
        {
            var v = new RematchVoteDriver();
            v.Tick(Players(1, 2), 15.1f); // Failed 확정
            Assert.IsFalse(v.TryVote(1));
        }

        [Test]
        public void ResultLatches_TickAfterPassKeepsPassed()
        {
            var v = new RematchVoteDriver();
            v.TryVote(1);
            v.TryVote(2);
            v.Tick(Players(1, 2), 0.1f); // Passed
            Assert.AreEqual(RematchVoteResult.Passed, v.Tick(Players(1, 2), 20f),
                "한 번 가결된 투표는 시간이 지나도 부결로 뒤집히지 않는다");
        }

        [Test]
        public void DisconnectedVoterDoesNotCount_ButShrunkMajorityApplies()
        {
            // 3인 중 1·2가 찬성(2/3 → 필요 2표 충족 직전에 2번이 이탈했다고 하자).
            // 이탈자는 분자·분모 양쪽에서 빠진다: 남은 {1,3} 중 찬성 1표, 필요 2표 → Pending.
            var v = new RematchVoteDriver();
            v.TryVote(1);
            v.TryVote(2);
            Assert.AreEqual(1, v.CountVotes(Players(1, 3)));
            Assert.AreEqual(RematchVoteResult.Pending, v.Tick(Players(1, 3), 0.1f));

            // 반대로 미투표자(3)가 이탈하면 남은 {1} 전원 찬성(1/1, 필요 1) → Passed.
            Assert.AreEqual(RematchVoteResult.Passed, v.Tick(Players(1), 0.1f));
        }

        [Test]
        public void NoPlayers_NeverPasses_RunsOutInstead()
        {
            // 전원 이탈: 과반이 성립할 수 없고, 창이 만료되면 부결(→ 로비)이다.
            var v = new RematchVoteDriver();
            v.TryVote(1);
            Assert.AreEqual(RematchVoteResult.Pending, v.Tick(Players(), 0.1f));
            Assert.AreEqual(RematchVoteResult.Failed, v.Tick(Players(), 15f));
        }

        [Test]
        public void TwoPlayers_BothMustAgree()
        {
            // 실기 2인 구성의 핵심 규칙: 한 명만 찬성하면 재시작되지 않는다(50% ≠ 과반).
            var v = new RematchVoteDriver();
            v.TryVote(1);
            Assert.AreEqual(RematchVoteResult.Pending, v.Tick(Players(1, 2), 0.1f));
            v.TryVote(2);
            Assert.AreEqual(RematchVoteResult.Passed, v.Tick(Players(1, 2), 0.1f));
        }
    }
}
