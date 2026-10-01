using System.Collections.Generic;
using Marco.Core.GameFlow;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Tagging;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 10-01 확정 규칙 — 활성 밸브 3개(수영장 B · E 고정 + 나머지 1개 무작위) · 필요 개방 = 활성 수(3, 인원 무관) ·
    /// 게이트는 "탈출 가능"일 뿐 승리가 아니다 · 라운드는 도망자 전원이 탈출 또는 포획됐을 때(또는 시간 종료)만 끝나고,
    /// 그때 탈출 인원 ⌈도망자 ÷ 2⌉로 승패를 판정한다.
    ///
    /// <para>
    /// 수정 전 동작: 총원 3인 이하는 필요 개방 2개(<c>ValveRoster.RequiredOpenCount</c>), 활성은 필요 + 1개를 5개 전체에서 무작위,
    /// 판정은 탈출 수가 요구치에 닿는 순간 도망자 승리(<c>WinConditionEvaluator.Evaluate</c>) — 3인 판(도망자 2)이면
    /// 밸브 2개 → 게이트 → 1명 탈출로 다른 도망자가 남아 있어도 즉시 승리 화면이었다.
    /// </para>
    /// </summary>
    public class DirectEscapeRuleTests
    {
        private static readonly Vector3 FrontDoor = new Vector3(7f, 1f, 40f);
        private static readonly Vector3 DrainExit = new Vector3(46f, 1f, 2f);
        private static readonly List<Vector3> Exits = new List<Vector3> { FrontDoor, DrainExit };

        // ── 게이트: 필요 개방 = 활성 3개 (인원 무관) ─────────────────────────

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Gate_TwoOfThreeOpen_StaysClosed_ThreeOpensIt(int totalPlayers)
        {
            var objective = new RoundObjective();
            objective.BeginRound(totalPlayers, activeValves: 3);

            Assert.AreEqual(3, objective.RequiredOpen, "필요 개방 = 활성 밸브 수(3) — 인원과 무관(수정 전: 3인 이하 2)");
            Assert.IsFalse(objective.Tick(2), "활성 3개 중 2개 — 게이트 닫힘");
            Assert.IsFalse(objective.GateOpen);
            Assert.IsTrue(objective.Tick(3), "3개 모두 — 게이트 열림");
            Assert.IsTrue(objective.GateOpen);
        }

        [Test]
        public void RequiredOpen_IsThree_ForEveryHeadcount()
        {
            // 인원 입력이 없다 — 필요 개방은 활성 수(3) 하나다(수정 전 API: RequiredOpenCount(총원), 3인 이하 2).
            Assert.AreEqual(3, ValveRoster.RequiredOpenCount);
            Assert.AreEqual(ValveRoster.ActiveCount, ValveRoster.RequiredOpenCount);
        }

        // ── 활성 밸브: 수영장 B · E 고정 + 나머지 1개 ─────────────────────

        [Test]
        public void ActiveValves_AlwaysThree_PoolValvesAlwaysIncluded_ManySeeds()
        {
            for (int players = 2; players <= 6; players++)
            for (int seed = 0; seed < 500; seed++)
            {
                List<ValveId> active = ValveRoster.SelectActive(unchecked(seed * 31 + players)); // 수정 전 API: SelectActive(총원, seed)
                Assert.AreEqual(3, active.Count, $"총원 {players} · seed {seed}");
                CollectionAssert.Contains(active, ValveId.B, $"메인풀 B — 총원 {players} · seed {seed}");
                CollectionAssert.Contains(active, ValveId.E, $"유아풀 E — 총원 {players} · seed {seed}");
                CollectionAssert.AllItemsAreUnique(active);
            }
        }

        [Test]
        public void RemainingSlot_IsSpreadOverNonPoolValves()
        {
            // 서버 시드 식(RoundNetworkSync.ServerSelectActiveValves)으로 라운드 1~600 · 총원 2~6을 돌린다 — 실제 분포.
            var counts = new Dictionary<ValveId, int> { { ValveId.A, 0 }, { ValveId.C, 0 }, { ValveId.D, 0 } };
            int total = 0;
            for (int round = 1; round <= 600; round++)
            for (int players = 2; players <= 6; players++)
            {
                int seed = unchecked(round * 73856093 + players * 19349663 + 1);
                foreach (ValveId id in ValveRoster.SelectActive(seed)) // 수정 전 API: SelectActive(총원, seed)
                {
                    if (id == ValveId.B || id == ValveId.E)
                        continue;
                    Assert.IsTrue(counts.ContainsKey(id), $"나머지 자리는 A · C · D 중 하나 — {id}");
                    counts[id]++;
                    total++;
                }
            }

            Assert.AreEqual(3000, total, "라운드마다 나머지 자리는 정확히 1개");
            foreach (KeyValuePair<ValveId, int> kv in counts)
            {
                float share = kv.Value / (float)total;
                TestContext.WriteLine($"{kv.Key}: {kv.Value} ({share:P1})");
                Assert.That(share, Is.InRange(0.28f, 0.39f), $"{kv.Key} 비율 {share:P1} — 대략 1/3");
            }
        }

        // ── 종료: 도망자 전원 탈출 또는 포획 ──────────────────────────────

        [Test]
        public void GateOpen_NobodyEscaped_RoundContinues()
        {
            var d = new ServerRoundDriver(600f);
            var objective = new RoundObjective();
            objective.BeginRound(4, activeValves: 3);
            objective.Tick(3);
            Assert.IsTrue(objective.GateOpen);

            ServerRoundDriver.RoundStep step = d.Step(d.Census(totalRunners: 3, taggedRunners: 0));

            Assert.IsFalse(step.Decided, "게이트가 열려도 아무도 나가지 않았다 — 승리 아님");
            Assert.AreEqual(RoundResult.InProgress, d.Result);
        }

        [Test]
        public void TwoRunners_OneEscaped_OtherStillIn_NoVictoryYet()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsTrue(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));

            ServerRoundDriver.RoundStep step = d.Step(d.Census(totalRunners: 2, taggedRunners: 0));

            Assert.IsFalse(step.Decided, "다른 도망자가 아직 맵에 있다 — 승리 화면이 뜨면 안 된다(수정 전: 즉시 RunnersWin)");
            Assert.AreEqual(RoundResult.InProgress, d.Result);
            Assert.IsTrue(step.EnteredLastSurvivorPhase, "남은 1명 — 최후 생존자 페이즈(직접 탈출해야 끝난다)");
        }

        [Test]
        public void TwoRunners_BothEscape_RoundEnds_RunnersWin()
        {
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            Assert.IsFalse(d.Step(d.Census(2, 0)).Decided);

            Assert.IsTrue(d.TryRegisterEscape(2, RoleType.Runner, gateOpen: true));
            Assert.IsTrue(d.Step(d.Census(2, 0)).Decided, "전원 탈출 — 끝");
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void TwoRunners_OneEscapes_OtherTagged_RoundEnds_ByRequirement()
        {
            // 도망자 2 → 탈출 요구 ⌈2/2⌉ = 1. 1명 탈출 · 1명 포획 → 남은 도망자 0 → 판정 → 도망자 승.
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            Assert.IsFalse(d.Step(d.Census(2, 0)).Decided);

            Assert.IsTrue(d.Step(d.Census(2, 1)).Decided, "남은 도망자 0(탈출 1 · 포획 1)");
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void ThreeRunners_TwoEscaped_ThirdStillIn_NoVictoryYet_ThenThirdTagged_RunnersWin()
        {
            // 도망자 3 → 요구 2. 2명 탈출로 요구는 채웠지만 3번째가 남아 있으면 끝나지 않는다.
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            d.TryRegisterEscape(2, RoleType.Runner, gateOpen: true);
            Assert.IsFalse(d.Step(d.Census(3, 0)).Decided, "수정 전: 2번째 탈출에서 즉시 RunnersWin");

            Assert.IsTrue(d.Step(d.Census(3, 1)).Decided);
            Assert.AreEqual(RoundResult.RunnersWin, d.Result, "탈출 2 ≥ 요구 2");
        }

        [Test]
        public void FourRunners_OneEscaped_RestTagged_SeekerWin()
        {
            // 도망자 4 → 요구 2. 탈출 1 · 포획 3. 마지막 이탈이 포획이면 술래 승(최후 생존자 탈출 아님).
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            Assert.IsFalse(d.Step(d.Census(4, 0)).Decided);
            Assert.IsFalse(d.Step(d.Census(4, 2)).Decided, "1명 남음 — 최후 생존자 페이즈");

            Assert.IsTrue(d.Step(d.Census(4, 3)).Decided);
            Assert.AreEqual(RoundResult.SeekerWin, d.Result, "탈출 1 < 요구 2");
        }

        [Test]
        public void TimeUp_JudgesByEscapedCount()
        {
            // 시간 종료는 그대로 종료 조건이다 — 남은 도망자는 미탈출로 집계하고 탈출 인원으로 판정한다(§6.3).
            Assert.AreEqual(RoundResult.RunnersWin, WinConditionEvaluator.Evaluate(3, 2, 1, false, 0f), "탈출 2 ≥ 요구 2");
            Assert.AreEqual(RoundResult.SeekerWin, WinConditionEvaluator.Evaluate(3, 1, 2, false, 0f), "탈출 1 < 요구 2");
            Assert.AreEqual(RoundResult.InProgress, WinConditionEvaluator.Evaluate(3, 2, 1, false, 30f), "시간이 남았고 1명이 남았다 — 진행");
        }

        // ── 최후 생존자: 자동 승리 없음, 직접 탈출(배수구) ────────────────

        [Test]
        public void LastSurvivor_GateOpen_NoAutoWin_MustEscapeDirectly()
        {
            // 2인 판(도망자 1) — 라운드 시작부터 최후 생존자. 밸브 3개로 게이트가 열려도 끝나지 않는다.
            var d = new ServerRoundDriver(600f);
            ServerRoundDriver.RoundStep first = d.Step(d.Census(1, 0));
            Assert.IsFalse(first.Decided);
            Assert.IsTrue(first.EnteredLastSurvivorPhase);

            Assert.IsFalse(d.Step(d.Census(1, 0)).Decided, "게이트 개방만으로는 승리 아님");
            Assert.IsTrue(d.TryRegisterEscape(9, RoleType.Runner, gateOpen: true), "출구로 직접 나간다");
            Assert.IsTrue(d.Step(d.Census(1, 0)).Decided);
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void LastSurvivor_DrainEscape_EndsRound()
        {
            var d = new ServerRoundDriver(600f);
            d.Step(d.Census(1, 0));
            Assert.IsFalse(d.Step(d.Census(1, 0)).Decided, "배수구를 열기 전 — 진행");

            Assert.IsTrue(d.TryRegisterDrainEscape(9, RoleType.Runner), "배수구 직접 탈출");
            Assert.IsTrue(d.Step(d.Census(1, 0)).Decided);
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        // ── 탈출 우선 · 탈출자 재집계 없음 ────────────────────────────────

        [Test]
        public void TagInsideExitRadius_BecomesEscape_ButRoundContinuesIfOthersRemain()
        {
            var d = new ServerRoundDriver(600f);
            var feet = new Vector3(7f, 0.1f, 39f); // 정문 1.34m

            ServerRoundDriver.TagResolution r = d.ResolveTagRequest(11, RoleType.Runner, feet, gateOpen: true, Exits);

            Assert.AreEqual(ServerRoundDriver.TagResolution.EscapeInstead, r, "탈출 우선(09-30) 유지");
            Assert.AreEqual(1, d.EscapedCount);
            Assert.IsFalse(d.Step(d.Census(2, 0)).Decided, "남은 도망자가 있다 — 끝나지 않는다(수정 전: 즉시 RunnersWin)");
        }

        [Test]
        public void EscapedRunner_CannotBeTaggedOrCountedAgain()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsTrue(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));
            Assert.IsFalse(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true), "같은 도망자 중복 집계 없음");
            Assert.AreEqual(1, d.EscapedCount);

            Assert.IsFalse(ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner, targetAlreadyTagged: false, targetEscaped: true,
                Vector3.zero, Vector3.zero), "탈출자는 태그 대상이 아니다 — 포획으로 다시 세지 않는다");
        }
    }
}
