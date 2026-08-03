using System.Collections.Generic;
using Marco.Core.Role;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 21 술래 로테이션(§2.3, GAP-22 해소). 순수 계산이라 Unity 없이 그대로 돈다.
    /// </summary>
    public class SeekerRotationTests
    {
        [Test]
        public void RoundsPerSet_MatchesDesignDoc()
        {
            // §2.3 "술래 로테이션 3판 1세트".
            Assert.AreEqual(3, SeekerRotation.RoundsPerSet);
        }

        [Test]
        public void FirstRound_SeekerIsFirstInOrder()
        {
            // 스프린트 13의 기존 동작(가장 낮은 OwnerId가 술래)이 첫 판에서는 그대로 유지된다.
            Assert.AreEqual(0, SeekerRotation.SeekerOrderIndex(0, playerCount: 4));
        }

        [Test]
        public void TwoPlayers_AlternatesEveryRound()
        {
            // GAP-22의 핵심 증상(호스트가 항상 술래)이 2인에서 사라지는지.
            Assert.AreEqual(0, SeekerRotation.SeekerOrderIndex(0, 2));
            Assert.AreEqual(1, SeekerRotation.SeekerOrderIndex(1, 2));
            Assert.AreEqual(0, SeekerRotation.SeekerOrderIndex(2, 2));
            Assert.AreEqual(1, SeekerRotation.SeekerOrderIndex(3, 2));
        }

        [Test]
        public void ThreePlayers_CycleCoversEveryoneOnce()
        {
            var seen = new HashSet<int>();
            for (int round = 0; round < 3; round++)
                seen.Add(SeekerRotation.SeekerOrderIndex(round, 3));

            Assert.AreEqual(3, seen.Count, "3판이면 3인 전원이 한 번씩 술래를 맡아야 한다.");
        }

        [Test]
        public void FourPlayers_FullCycleTakesFourRounds()
        {
            var seen = new HashSet<int>();
            for (int round = 0; round < 4; round++)
                seen.Add(SeekerRotation.SeekerOrderIndex(round, 4));

            Assert.AreEqual(4, seen.Count);

            // 한 바퀴 = 인원 수이며, §2.3의 세트(3판)와 일치하지 않을 수 있다.
            Assert.AreEqual(4, SeekerRotation.RoundsPerFullCycle(4));
            Assert.AreNotEqual(SeekerRotation.RoundsPerSet, SeekerRotation.RoundsPerFullCycle(4));
        }

        [Test]
        public void NoPlayerIsSeekerTwiceBeforeEveryoneHasBeen()
        {
            // 로테이션의 공정성 조건: 한 바퀴 안에서 중복이 없어야 한다.
            for (int playerCount = 2; playerCount <= RoleAssigner.MaximumPlayers; playerCount++)
            {
                var seen = new HashSet<int>();
                for (int round = 0; round < playerCount; round++)
                {
                    int index = SeekerRotation.SeekerOrderIndex(round, playerCount);
                    Assert.IsTrue(seen.Add(index),
                        $"{playerCount}인에서 {round}번째 판에 술래 순번 {index}가 중복됐다.");
                }
            }
        }

        [Test]
        public void SeekerIndex_IsAlwaysInRange()
        {
            for (int playerCount = 1; playerCount <= 6; playerCount++)
            {
                for (int round = -5; round < 20; round++)
                {
                    int index = SeekerRotation.SeekerOrderIndex(round, playerCount);
                    Assert.GreaterOrEqual(index, 0);
                    Assert.Less(index, playerCount);
                }
            }
        }

        [Test]
        public void SeekerIndex_HandlesInvalidPlayerCount()
        {
            Assert.AreEqual(0, SeekerRotation.SeekerOrderIndex(7, playerCount: 0));
            Assert.AreEqual(0, SeekerRotation.SeekerOrderIndex(7, playerCount: -3));
        }

        [Test]
        public void SetAndRoundNumbering_IsOneBasedForDisplay()
        {
            Assert.AreEqual(1, SeekerRotation.SetNumber(0));
            Assert.AreEqual(1, SeekerRotation.RoundInSet(0));

            Assert.AreEqual(1, SeekerRotation.SetNumber(2));
            Assert.AreEqual(3, SeekerRotation.RoundInSet(2));

            // 4번째 판(0-기반 3)부터 2세트.
            Assert.AreEqual(2, SeekerRotation.SetNumber(3));
            Assert.AreEqual(1, SeekerRotation.RoundInSet(3));
        }

        [Test]
        public void NumberingIsSafeBeforeFirstRound()
        {
            // OnStartServer가 -1로 초기화한 뒤 첫 배정 전에 표시될 수 있다.
            Assert.AreEqual(1, SeekerRotation.SetNumber(-1));
            Assert.AreEqual(1, SeekerRotation.RoundInSet(-1));
        }

        // ── RoleAssigner 연동 ────────────────────────────────────────────

        [Test]
        public void RotatedAssignment_YieldsExactlyOneSeeker()
        {
            for (int playerCount = 2; playerCount <= RoleAssigner.MaximumPlayers; playerCount++)
            {
                for (int round = 0; round < playerCount * 2; round++)
                {
                    int seekerOrder = SeekerRotation.SeekerOrderIndex(round, playerCount);
                    int seekers = 0;

                    for (int i = 0; i < playerCount; i++)
                    {
                        if (RoleAssigner.RoleForOrder(i, playerCount, seekerOrder) == RoleType.Seeker)
                            seekers++;
                    }

                    Assert.AreEqual(RoleAssigner.SeekerCount, seekers,
                        $"{playerCount}인 {round}판에서 술래가 {seekers}명이다(§6.2는 1명).");
                }
            }
        }

        [Test]
        public void RotatedAssignment_PutsSeekerAtRotatedSlot()
        {
            // 4인 2번째 판(0-기반 1): 정렬 1번이 술래여야 한다 — 호스트(0번)가 아니다.
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(0, 4, seekerOrderIndex: 1));
            Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(1, 4, seekerOrderIndex: 1));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(2, 4, seekerOrderIndex: 1));
        }

        [Test]
        public void OutOfRangeSeekerIndex_FallsBackToFirstSlot()
        {
            // 인원이 줄어든 뒤 옛 인덱스가 들어와도 술래가 사라지면 안 된다.
            Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(0, 2, seekerOrderIndex: 5));
            Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(0, 2, seekerOrderIndex: -1));
        }

        [Test]
        public void LegacyOverload_StillAssignsFirstSlot()
        {
            // 스프린트 13 호출부 호환(기본 술래 순번 0).
            Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(0, 4));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(1, 4));
        }

        [Test]
        public void BelowMinimumPlayers_NeverAssignsSeeker()
        {
            // §1 최소 2인 미만은 배정하지 않는다(GAP-21 유지) — 로테이션도 이를 바꾸지 않는다.
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(0, 1, seekerOrderIndex: 0));
        }
    }
}
