using System.Collections.Generic;
using Marco.Core.Objectives;
using Marco.Core.Util;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §6.1-0 · §6.2 활성 밸브 조합과 요구 개방 수를 고정한다.
    ///
    /// <para>
    /// <b>이 파일의 중심은 불변 조건 "활성 = 요구 + 1"</b>이다. §6.2가 *"절대 깨지 않는다"*
    /// 고 못박았고 §9.3-1의 봉쇄 무력화 논리가 성립하는 유일한 조건이다.
    /// </para>
    /// </summary>
    public class ValveRosterTests
    {
        [Test]
        public void PlacedCount_IsFiveRegardlessOfPlayers()
        {
            // §6.1-0 "배치는 5개 고정이며, 인원에 따라 활성 개수만 3~4로 조절한다."
            Assert.AreEqual(5, ValveRoster.PlacedCount);
            Assert.AreEqual(5, ValveOccupancy.All.Length);
        }

        // §6.2 표: 총원 2→활성3/요구2 | 3→3/2 | 4→4/3 | 5→4/3 | 6→4/3
        [TestCase(2, 3, 2)]
        [TestCase(3, 3, 2)]
        [TestCase(4, 4, 3)]
        [TestCase(5, 4, 3)]
        [TestCase(6, 4, 3)]
        public void ActiveAndRequired_MatchDesignDocTable(int totalPlayers, int active, int required)
        {
            Assert.AreEqual(required, ValveRoster.RequiredOpenCount(totalPlayers), "요구 개방");
            Assert.AreEqual(active, ValveRoster.ActiveCount(totalPlayers), "활성 밸브");
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Invariant_ActiveEqualsRequiredPlusOne(int totalPlayers)
        {
            // §6.2 "유지되는 불변 조건" — 여유가 0이면 술래가 하나만 지켜도 클리어가 막히고,
            // 여유가 2 이상이면 압박이 소멸한다.
            Assert.AreEqual(ValveRoster.RequiredOpenCount(totalPlayers) + 1,
                ValveRoster.ActiveCount(totalPlayers));

            Assert.DoesNotThrow(() => ValveRoster.AssertInvariant(totalPlayers));
        }

        // §6.2 [v0.4] 탈출 요구 = ⌈도망자 ÷ 2⌉ — 2→1 / 3→2 / 4→2 / 5→3
        [TestCase(1, 1)]
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        public void EscapeRequirement_IsCeilingOfHalf(int runners, int required)
        {
            Assert.AreEqual(required, ValveRoster.EscapeRequirement(runners));
        }

        [Test]
        public void EscapeRequirement_HardcodedTwoWouldBreakSixPlayerGame()
        {
            // §6.2 "하드코딩 2를 남기면 6인 게임(요구 3)에서 틀린다" — 그 사실 자체를 고정한다.
            Assert.AreEqual(3, ValveRoster.EscapeRequirement(5), "6인 = 도망자 5 → 탈출 요구 3");
            Assert.AreNotEqual(2, ValveRoster.EscapeRequirement(5));
        }

        // ── 무작위 활성 선택 ────────────────────────────────────────────

        [Test]
        public void SelectActive_IsDeterministicForSameSeed()
        {
            List<ValveId> a = ValveRoster.SelectActive(5, seed: 1234);
            List<ValveId> b = ValveRoster.SelectActive(5, seed: 1234);

            Assert.AreEqual(a, b, "같은 시드면 같은 조합이어야 테스트에서 고정할 수 있다.");
        }

        [Test]
        public void SelectActive_ReturnsActiveCountWithoutDuplicates()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                List<ValveId> chosen = ValveRoster.SelectActive(5, seed);

                Assert.AreEqual(4, chosen.Count, $"seed={seed}");
                CollectionAssert.AllItemsAreUnique(chosen, $"seed={seed}");
            }
        }

        [Test]
        public void SelectActive_EventuallyCoversAllFiveCombinations()
        {
            // §6.1-0 "5개 중 서버가 무작위 선택, 조합 5가지" — 한 밸브도 영구 제외되지 않는다.
            var seen = new HashSet<ValveId>();
            for (int seed = 1; seed <= 200; seed++)
            {
                foreach (ValveId id in ValveRoster.SelectActive(5, seed))
                    seen.Add(id);
            }

            Assert.AreEqual(5, seen.Count, "다섯 밸브 전부가 활성이 될 수 있어야 한다.");
        }

        [Test]
        public void SelectActive_ForThreePlayers_PicksThree()
        {
            Assert.AreEqual(3, ValveRoster.SelectActive(3, seed: 7).Count);
        }

        [Test]
        public void DeterministicRandom_ZeroSeedDoesNotDegenerate()
        {
            // xorshift는 상태 0이 고정점이라 영원히 0을 낸다. 0이 들어와도 살아야 한다.
            var random = new DeterministicRandom(0);
            var values = new HashSet<uint>();
            for (int i = 0; i < 20; i++)
                values.Add(random.NextUInt());

            Assert.Greater(values.Count, 1, "시드 0에서 같은 값만 나오면 안 된다.");
        }

        [Test]
        public void DeterministicRandom_ChooseDoesNotMutateSource()
        {
            var source = new List<ValveId>(ValveOccupancy.All);
            var random = new DeterministicRandom(99);

            random.Choose(source, 3);

            Assert.AreEqual(5, source.Count, "원본을 건드리면 안 된다.");
        }
    }
}
