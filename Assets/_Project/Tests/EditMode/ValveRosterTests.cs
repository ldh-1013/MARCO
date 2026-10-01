using System.Collections.Generic;
using Marco.Core.Objectives;
using Marco.Core.Util;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §6.1-0 · §6.2 활성 밸브 조합과 필요 개방 수를 고정한다.
    ///
    /// <para>
    /// <b>10-01 확정 규칙</b>: 활성 3개(수영장 B · E 고정 + A · C · D 중 1개) · 필요 개방 = 활성 수(3, 인원 무관).
    /// 예전 불변 조건 "활성 = 요구 + 1"(§9.3-1 봉쇄 무력화)은 이 규칙으로 폐기됐다 — 활성 3개를 전부 열어야 하므로 술래가 하나를
    /// 지키면 게이트가 막힌다(PrototypeScenarioTests.Scenario4).
    /// </para>
    /// </summary>
    public class ValveRosterTests
    {
        [Test]
        public void PlacedCount_IsFiveRegardlessOfPlayers()
        {
            // §6.1-0 "배치는 5개 고정" — 매 라운드 활성 3개(10-01).
            Assert.AreEqual(5, ValveRoster.PlacedCount);
            Assert.AreEqual(5, ValveOccupancy.All.Length);
        }

        [Test]
        public void ActiveAndRequired_AreThree_RegardlessOfPlayers()
        {
            // 10-01: 인원 입력이 없다. 예전 표(총원 2·3 → 활성 3 / 요구 2, 4~6 → 활성 4 / 요구 3)는 폐기.
            Assert.AreEqual(3, ValveRoster.ActiveCount, "활성 밸브");
            Assert.AreEqual(3, ValveRoster.RequiredOpenCount, "필요 개방");
        }

        [Test]
        public void Invariant_RequiredEqualsActive_PoolValvesAreBAndE()
        {
            Assert.AreEqual(ValveRoster.ActiveCount, ValveRoster.RequiredOpenCount, "필요 개방 = 활성 수");
            CollectionAssert.AreEqual(new[] { ValveId.B, ValveId.E }, ValveRoster.PoolValves, "수영장 밸브 = 수중 밸브 B · E");
            Assert.DoesNotThrow(ValveRoster.AssertInvariant);
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
            List<ValveId> a = ValveRoster.SelectActive(seed: 1234);
            List<ValveId> b = ValveRoster.SelectActive(seed: 1234);

            Assert.AreEqual(a, b, "같은 시드면 같은 조합이어야 테스트에서 고정할 수 있다.");
        }

        [Test]
        public void SelectActive_ReturnsActiveCountWithoutDuplicates()
        {
            for (int seed = 1; seed <= 50; seed++)
            {
                List<ValveId> chosen = ValveRoster.SelectActive(seed);

                Assert.AreEqual(3, chosen.Count, $"seed={seed}");
                CollectionAssert.AllItemsAreUnique(chosen, $"seed={seed}");
            }
        }

        [Test]
        public void SelectActive_EventuallyCoversAllFiveCombinations()
        {
            // 10-01: B · E 고정 + 나머지 1자리 → 조합 3가지. 다섯 밸브 모두 활성이 될 수 있다(A · C · D는 번갈아).
            var seen = new HashSet<ValveId>();
            for (int seed = 1; seed <= 200; seed++)
            {
                foreach (ValveId id in ValveRoster.SelectActive(seed))
                    seen.Add(id);
            }

            Assert.AreEqual(5, seen.Count, "다섯 밸브 전부가 활성이 될 수 있어야 한다.");
        }

        [Test]
        public void SelectActive_PicksThree()
        {
            Assert.AreEqual(3, ValveRoster.SelectActive(seed: 7).Count);
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
