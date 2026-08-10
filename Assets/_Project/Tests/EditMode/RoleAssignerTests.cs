using NUnit.Framework;
using Marco.Core.Role;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 13: §6.2 인원별 역할 배정 규칙을 고정한다.
    ///
    /// 이 테스트의 핵심 목적은 **기획서 §6.2 표의 도망자 수를 코드가 그대로 재현하는지**를
    /// 못박는 것이다(임의 규칙 창작 방지). 표가 바뀌면 이 테스트가 먼저 깨진다.
    ///
    /// | 인원 | 도망자 |   ← §6.2 인원별 밸런스 표
    /// |  2   |   1    |      용어집: "리스너(Seeker) | 술래 역할. 1인"
    /// |  3   |   2    |
    /// |  4   |   3    | MVP
    /// |  5   |   4    |
    /// |  6   |   5    |
    /// </summary>
    public class RoleAssignerTests
    {
        // ── 기획서 §6.2 표 재현 ──────────────────────────────────────────

        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 3)] // MVP 대상 행
        [TestCase(5, 4)]
        [TestCase(6, 5)]
        public void RunnersFor_MatchesDesignDocTable(int playerCount, int expectedRunners)
        {
            Assert.AreEqual(expectedRunners, RoleAssigner.RunnersFor(playerCount));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void SeekersFor_IsAlwaysOne_WithinSupportedRange(int playerCount)
        {
            // 용어집 "술래 역할. 1인" + 표 전 행의 (인원 - 도망자 = 1).
            Assert.AreEqual(1, RoleAssigner.SeekersFor(playerCount));
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void SeekersPlusRunners_EqualsPlayerCount(int playerCount)
        {
            Assert.AreEqual(playerCount,
                RoleAssigner.SeekersFor(playerCount) + RoleAssigner.RunnersFor(playerCount));
        }

        [Test]
        public void SeekerCount_IsExactlyOne_MvpConstant()
        {
            // v1.x 8~10인 구간의 술래 2인은 MVP 범위 밖(§1) — 상수가 1이어야 한다.
            Assert.AreEqual(1, RoleAssigner.SeekerCount);
        }

        [Test]
        public void MinimumPlayers_IsTwo_PerDesignDoc()
        {
            // §1 게임 개요: "인원 최소 2".
            Assert.AreEqual(2, RoleAssigner.MinimumPlayers);
        }

        [Test]
        public void MaximumPlayers_IsSix_PerDesignDoc()
        {
            // §1 게임 개요: "최대 6".
            Assert.AreEqual(6, RoleAssigner.MaximumPlayers);
        }

        // ── GAP-21: 최소 인원 미만은 배정하지 않는다(로컬 폴백 보존) ──────

        [TestCase(0)]
        [TestCase(1)]
        public void CanAssign_BelowMinimum_IsFalse(int playerCount)
        {
            Assert.IsFalse(RoleAssigner.CanAssign(playerCount));
        }

        [TestCase(2)]
        [TestCase(4)]
        [TestCase(6)]
        public void CanAssign_AtOrAboveMinimum_IsTrue(int playerCount)
        {
            Assert.IsTrue(RoleAssigner.CanAssign(playerCount));
        }

        [Test]
        public void SinglePlayer_StaysRunner_LocalFallbackPreserved()
        {
            // 로컬 단독 실행(1명)에서 술래로 바뀌면 스프린트 3~7 워크플로우가 깨진다
            // (탈출은 러너만 가능 — GAP-11). 배정하지 않고 러너로 남아야 한다.
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(0, 1));
            Assert.AreEqual(0, RoleAssigner.SeekersFor(1));
            Assert.AreEqual(1, RoleAssigner.RunnersFor(1));
        }

        [Test]
        public void ZeroPlayers_NoSeeker()
        {
            Assert.AreEqual(0, RoleAssigner.SeekersFor(0));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(0, 0));
        }

        // ── GAP-22: 순서 기준 앞쪽 1명이 술래 ───────────────────────────

        [Test]
        public void RoleForOrder_FirstIsSeeker_RestAreRunners_FourPlayers()
        {
            Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(0, 4));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(1, 4));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(2, 4));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(3, 4));
        }

        [Test]
        public void RoleForOrder_TwoPlayers_OneSeekerOneRunner()
        {
            // 실기 2-클라이언트 테스트 구성(GAP-21: AI 술래 대신 인간 술래 1명).
            Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(0, 2));
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(1, 2));
        }

        [Test]
        public void RoleForOrder_NegativeIndex_IsRunner_NotSeeker()
        {
            // 소유권 미확정(-1) 같은 비정상 입력이 술래로 새지 않게 한다.
            Assert.AreEqual(RoleType.Runner, RoleAssigner.RoleForOrder(-1, 4));
        }

        [Test]
        public void RoleForOrder_NeverAssignsEcho()
        {
            // §3.1: Echo는 "태그당해 탈락한 도망자" — 초기 배정으로 도달할 수 없어야 한다.
            for (int count = 0; count <= RoleAssigner.MaximumPlayers; count++)
            {
                for (int i = 0; i < count; i++)
                    Assert.AreNotEqual(RoleType.Echo, RoleAssigner.RoleForOrder(i, count),
                        $"인원 {count}, 순서 {i}에서 Echo가 배정됐다");
            }
        }

        [Test]
        public void RoleForOrder_ExactlyOneSeekerAcrossAllOrders()
        {
            // 배정 전체를 훑어 술래가 정확히 1명인지 — 표의 (인원 - 도망자 = 1) 불변식.
            for (int count = RoleAssigner.MinimumPlayers; count <= RoleAssigner.MaximumPlayers; count++)
            {
                int seekers = 0;
                for (int i = 0; i < count; i++)
                {
                    if (RoleAssigner.RoleForOrder(i, count) == RoleType.Seeker)
                        seekers++;
                }
                Assert.AreEqual(1, seekers, $"인원 {count}명에서 술래 수가 1이 아니다");
            }
        }

        [Test]
        public void RoleForOrder_NewPlayerJoining_DoesNotChangeExistingSeeker()
        {
            // GAP-22 재배정 안정성: 인원이 늘어도 순서 0번은 계속 술래다
            // (OwnerId 오름차순이라 새 접속자는 항상 뒤쪽 인덱스).
            //
            // **주의**: 이 성질은 seekerOrderIndex가 0으로 고정된 경우에만 성립한다.
            // §2.3 로테이션(스프린트 21)이 들어온 뒤로는 순번이 라운드마다 달라지므로,
            // "재배정해도 술래 불변"은 아래 RotatedSeeker_* 테스트가 규정하는 방식
            // (순번을 라운드 시작에 **고정**)으로만 보장된다.
            for (int count = RoleAssigner.MinimumPlayers; count <= RoleAssigner.MaximumPlayers; count++)
                Assert.AreEqual(RoleType.Seeker, RoleAssigner.RoleForOrder(0, count));
        }

        // ── 로테이션 + 재배정 조합 (2/3 Net 검증 C항목) ─────────────────────
        //
        // 이 조합이 검증 공백이었다. 위 테스트는 2-인자 오버로드(= seekerOrderIndex 0 고정)만
        // 보고 있어, 스프린트 21의 로테이션이 들어오며 깨진 불변식을 잡지 못했다.

        [TestCase(5, 3, 4)] // 5라운드: 3인 → 5%3=2번, 4인 합류 시 5%4=1번으로 **바뀐다**
        [TestCase(3, 2, 3)] // 3라운드: 2인 → 3%2=1번, 3인 합류 시 3%3=0번으로 **바뀐다**
        public void SeekerOrderIndex_ChangesWhenPlayerCountChanges_HenceMustBeFrozen(
            int roundNumber, int beforeCount, int afterCount)
        {
            int before = SeekerRotation.SeekerOrderIndex(roundNumber, beforeCount);
            int after = SeekerRotation.SeekerOrderIndex(roundNumber, afterCount);

            Assert.AreNotEqual(before, after,
                "순번이 인원수에 의존한다는 사실 자체를 고정한다 — 그래서 RoundNetworkSync가 " +
                "라운드 시작 시점 값을 _fixedSeekerOrder에 얼려 두고 재계산하지 않는다.");
        }

        [Test]
        public void RotatedSeeker_StaysSameWhenOrderIndexIsFrozen()
        {
            // 라운드 시작에 확정한 순번을 그대로 쓰면, 인원이 늘어도 같은 사람이 술래로 남는다.
            const int roundNumber = 5;
            const int beforeCount = 3;
            const int afterCount = 4;

            int frozen = SeekerRotation.SeekerOrderIndex(roundNumber, beforeCount); // = 2

            for (int i = 0; i < beforeCount; i++)
            {
                RoleType before = RoleAssigner.RoleForOrder(i, beforeCount, frozen);
                RoleType after = RoleAssigner.RoleForOrder(i, afterCount, frozen);

                Assert.AreEqual(before, after,
                    $"정렬 {i}번의 역할이 합류 전후로 달라지면 안 된다(고정 순번 {frozen}).");
            }
        }

        [Test]
        public void RotatedSeeker_FrozenOrder_StillYieldsExactlyOneSeeker_AfterJoin()
        {
            // 합류 후에도 술래가 정확히 1명이어야 한다 — 0명(증상 2)이 되면 라운드가 성립하지 않는다.
            for (int roundNumber = 0; roundNumber < 12; roundNumber++)
            {
                for (int beforeCount = RoleAssigner.MinimumPlayers; beforeCount < RoleAssigner.MaximumPlayers; beforeCount++)
                {
                    int frozen = SeekerRotation.SeekerOrderIndex(roundNumber, beforeCount);
                    int afterCount = beforeCount + 1;

                    int seekers = 0;
                    for (int i = 0; i < afterCount; i++)
                    {
                        if (RoleAssigner.RoleForOrder(i, afterCount, frozen) == RoleType.Seeker)
                            seekers++;
                    }

                    Assert.AreEqual(1, seekers,
                        $"라운드 {roundNumber}, {beforeCount}→{afterCount}인 합류 후 술래 수가 1이 아니다.");
                }
            }
        }

        [Test]
        public void RotatedSeeker_LateJoinerIsAlwaysRunner()
        {
            // 신규 접속자는 정렬 뒤쪽(OwnerId가 더 큼)에 붙으므로, 라운드 시작에 고정된 순번이
            // 그를 가리키는 일이 없다 — RoundNetworkSync가 러너로만 채우는 것과 일치한다.
            for (int roundNumber = 0; roundNumber < 12; roundNumber++)
            {
                for (int beforeCount = RoleAssigner.MinimumPlayers; beforeCount < RoleAssigner.MaximumPlayers; beforeCount++)
                {
                    int frozen = SeekerRotation.SeekerOrderIndex(roundNumber, beforeCount);
                    int joinerIndex = beforeCount; // 0-기반이라 새 인원의 인덱스 = 기존 인원 수

                    Assert.AreEqual(RoleType.Runner,
                        RoleAssigner.RoleForOrder(joinerIndex, beforeCount + 1, frozen),
                        $"라운드 {roundNumber}, {beforeCount}인에 합류한 사람이 술래가 됐다(고정 순번 {frozen}).");
                }
            }
        }
    }
}
