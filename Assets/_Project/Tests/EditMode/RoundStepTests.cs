using Marco.Core.GameFlow;
using Marco.Core.Objectives;
using Marco.Core.Role;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 판정과 최후 생존자 페이즈 진입의 순서(09-30). 탈출로 승리가 확정되는 같은 처리에서 "페이즈 진입"이 먼저 찍혀
    /// 결과 화면에 "최후 생존자 1:30"이 남았다 — §6.3 "페이즈 진입과 탈출이 동일 프레임: 탈출 먼저 확정. 도망자가 0명이 되면 페이즈 미진입".
    /// 서버(<c>RoundNetworkSync.EvaluateAndPush</c>)가 <see cref="ServerRoundDriver.Step"/>을 그대로 호출한다.
    /// </summary>
    public class RoundStepTests
    {
        private static ServerRoundDriver NewDriver() => new ServerRoundDriver(600f);

        [Test]
        public void TwoRunners_OneEscapes_NotDecided_LastOneEntersPhase_ThenEscapes()
        {
            ServerRoundDriver d = NewDriver();
            Assert.IsTrue(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));

            // 도망자 2 · 1명 탈출 → 탈출 요구 ⌈2/2⌉=1은 채웠지만 1명이 남았다 — 10-01: 끝나지 않고 최후 생존자 페이즈로.
            //   (09-30 결함 "승리 확정 처리에서 페이즈 진입"은 이제 생길 수 없다 — 승리는 남은 도망자 0명일 때만 확정된다.)
            ServerRoundDriver.RoundStep step = d.Step(d.Census(totalRunners: 2, taggedRunners: 0));

            Assert.IsFalse(step.Decided);
            Assert.IsTrue(step.EnteredLastSurvivorPhase);

            Assert.IsTrue(d.TryRegisterEscape(2, RoleType.Runner, gateOpen: true), "남은 1명도 직접 탈출");
            ServerRoundDriver.RoundStep end = d.Step(d.Census(2, 0));
            Assert.IsTrue(end.Decided);
            Assert.IsFalse(end.EnteredLastSurvivorPhase, "판정이 난 처리에서는 페이즈에 다시 들어가지 않는다");
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void ThreeRunners_TwoEscape_LastAlive_EntersPhase_ThenTagged_RunnersWin()
        {
            ServerRoundDriver d = NewDriver();
            d.TryRegisterEscape(1, RoleType.Runner, true);
            d.TryRegisterEscape(2, RoleType.Runner, true);

            ServerRoundDriver.RoundStep step = d.Step(d.Census(3, 0)); // 요구 ⌈3/2⌉=2 달성, 생존 1 — 아직 끝나지 않는다

            Assert.IsFalse(step.Decided);
            Assert.IsTrue(step.EnteredLastSurvivorPhase);

            Assert.IsTrue(d.Step(d.Census(3, 1)).Decided, "마지막 1명 포획 — 전원 확정");
            Assert.AreEqual(RoundResult.RunnersWin, d.Result, "탈출 2 ≥ 요구 2");
        }

        [Test]
        public void OneAliveWithoutDecision_EntersPhaseExactlyOnce()
        {
            ServerRoundDriver d = NewDriver();

            // 도망자 3 · 1명 태그 · 1명 탈출(요구 2 미달) · 생존 1 → 승패 미정, 페이즈 진입.
            d.TryRegisterEscape(1, RoleType.Runner, true);
            RunnerCensus census = d.Census(3, taggedRunners: 1);

            ServerRoundDriver.RoundStep first = d.Step(census);
            Assert.IsFalse(first.Decided);
            Assert.IsTrue(first.EnteredLastSurvivorPhase);
            Assert.IsTrue(d.LastSurvivorPhase);

            ServerRoundDriver.RoundStep second = d.Step(census);
            Assert.IsFalse(second.Decided);
            Assert.IsFalse(second.EnteredLastSurvivorPhase, "1회성 — 재진입이 90초 타이머를 되돌리면 안 된다");
        }

        [Test]
        public void AllRunnersTagged_SeekerWins_WithoutPhase()
        {
            ServerRoundDriver d = NewDriver();

            ServerRoundDriver.RoundStep step = d.Step(d.Census(2, taggedRunners: 2)); // 생존 0

            Assert.IsTrue(step.Decided);
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);
            Assert.IsFalse(step.EnteredLastSurvivorPhase, "'도망자가 0명이 되면 페이즈 미진입'");
        }

        [Test]
        public void TimeExpired_WithOneAlive_SeekerWins_WithoutPhase()
        {
            ServerRoundDriver d = NewDriver();
            d.Tick(600f);

            ServerRoundDriver.RoundStep step = d.Step(d.Census(3, taggedRunners: 1)); // 생존 2 → 어차피 시간 종료
            Assert.IsTrue(step.Decided);
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);

            ServerRoundDriver e = NewDriver();
            e.Tick(600f);
            ServerRoundDriver.RoundStep step2 = e.Step(e.Census(2, taggedRunners: 1)); // 생존 1 + 시간 종료
            Assert.IsTrue(step2.Decided);
            Assert.IsFalse(step2.EnteredLastSurvivorPhase, "시간이 이미 끝났으면 페이즈에 들어가지 않는다");
        }

        [Test]
        public void LastSurvivorEscapesInPhase_StillWins()
        {
            ServerRoundDriver d = NewDriver();
            d.Step(d.Census(2, taggedRunners: 1)); // 페이즈 진입
            Assert.IsTrue(d.LastSurvivorPhase);

            Assert.IsTrue(d.TryRegisterDrainEscape(2, RoleType.Runner));
            ServerRoundDriver.RoundStep step = d.Step(d.Census(2, taggedRunners: 1));

            Assert.IsTrue(step.Decided);
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }
    }
}
