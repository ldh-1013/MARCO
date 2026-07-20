using NUnit.Framework;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 6: 탈출 지점 + 라운드 타이머 배선 규칙을 고정한다.
    /// §6.3 판정식(11케이스)과 §15.4 상태 전이(9케이스)는 Core 테스트가 이미 담당하므로,
    /// 여기서는 **타이머 만료 1회성 · 탈출 집계 규칙 · 판정 래치**만 검증한다.
    ///
    /// GAP-11 결정: 탈출로 집계되는 것은 러너뿐(§6.3 변수명 `runnersEscaped`, §3.2 메아리는 유령).
    /// §6.1 명시 규칙: 밸브 전부 개방(게이트 Open) 전에는 탈출 자체가 불가능.
    /// </summary>
    public class RoundFlowTests
    {
        private const ulong RunnerA = 1;
        private const ulong RunnerB = 2;

        // ── RoundTimer ────────────────────────────────────────────────

        // 1) §6.2 4인 MVP 제한시간은 기획서 명시값 10분이다(임의값 아님).
        [Test]
        public void FourPlayerDuration_MatchesDesignDoc()
        {
            Assert.AreEqual(600f, RoundTimer.FourPlayerSeconds, 0.001f);
        }

        // 2) 카운트다운이 실제로 감소한다.
        [Test]
        public void Tick_DecrementsRemaining()
        {
            var timer = new RoundTimer();
            timer.Start(10f);

            timer.Tick(3f);

            Assert.AreEqual(7f, timer.RemainingSeconds, 0.001f);
            Assert.IsFalse(timer.HasExpired);
        }

        // 3) **핵심**: 만료 이벤트는 정확히 1회만 발행된다(계속 Tick해도 재발행 없음).
        [Test]
        public void Expiry_FiresExactlyOnce()
        {
            var timer = new RoundTimer();
            int fired = 0;
            timer.Expired += () => fired++;
            timer.Start(1f);

            timer.Tick(1f);   // 만료
            timer.Tick(1f);   // 추가 Tick
            timer.Tick(5f);

            Assert.AreEqual(1, fired);
            Assert.IsTrue(timer.HasExpired);
        }

        // 4) 남은 시간은 음수로 흘러가지 않는다(§6.3 timeRemaining <= 0 입력 안정성).
        [Test]
        public void Remaining_ClampsAtZero()
        {
            var timer = new RoundTimer();
            timer.Start(1f);

            timer.Tick(100f);

            Assert.AreEqual(0f, timer.RemainingSeconds, 0.001f);
        }

        // 5) Stop 후에는 더 이상 감소하지 않는다(라운드가 먼저 끝난 경우).
        [Test]
        public void Stop_HaltsCountdown()
        {
            var timer = new RoundTimer();
            timer.Start(10f);
            timer.Tick(2f);

            timer.Stop();
            timer.Tick(5f);

            Assert.AreEqual(8f, timer.RemainingSeconds, 0.001f);
        }

        // ── 탈출 집계 ──────────────────────────────────────────────────

        // 6) §6.1: 게이트가 닫혀 있으면(밸브 미완) 탈출 자체가 불가능하다.
        [Test]
        public void Escape_BeforeGateOpens_IsRejected()
        {
            var outcome = new RoundOutcomeTracker();

            bool escaped = outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: false);

            Assert.IsFalse(escaped);
            Assert.AreEqual(0, outcome.EscapedCount);
        }

        // 7) 게이트가 열린 뒤 러너가 도달하면 집계된다.
        [Test]
        public void Escape_AfterGateOpens_IsCounted()
        {
            var outcome = new RoundOutcomeTracker();

            bool escaped = outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            Assert.IsTrue(escaped);
            Assert.AreEqual(1, outcome.EscapedCount);
            Assert.IsTrue(outcome.HasEscaped(RunnerA));
        }

        // 8) GAP-11: 술래·메아리는 탈출로 집계되지 않는다.
        [TestCase(RoleType.Seeker)]
        [TestCase(RoleType.Echo)]
        public void Escape_NonRunnerRoles_AreNotCounted(RoleType role)
        {
            var outcome = new RoundOutcomeTracker();

            bool escaped = outcome.TryRegisterEscape(RunnerA, role, gateOpen: true);

            Assert.IsFalse(escaped);
            Assert.AreEqual(0, outcome.EscapedCount);
        }

        // 9) 같은 러너가 두 번 도달해도 중복 집계되지 않는다.
        [Test]
        public void Escape_SameRunnerTwice_CountsOnce()
        {
            var outcome = new RoundOutcomeTracker();

            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            bool second = outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            Assert.IsFalse(second);
            Assert.AreEqual(1, outcome.EscapedCount);
        }

        // 10) 서로 다른 러너는 각각 집계된다.
        [Test]
        public void Escape_DifferentRunners_CountSeparately()
        {
            var outcome = new RoundOutcomeTracker();

            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            outcome.TryRegisterEscape(RunnerB, RoleType.Runner, gateOpen: true);

            Assert.AreEqual(2, outcome.EscapedCount);
        }

        // ── §6.3 판정 연결 ─────────────────────────────────────────────

        // 11) 밸브 전부 개방 + 1인 탈출 → 러너 승리. **이 스프린트로 처음 성립하는 경로.**
        [Test]
        public void AllValvesOpenAndOneEscaped_DecidesRunnersWin()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            bool decided = outcome.Evaluate(valvesOpened: 3, totalValves: 3,
                allRunnersTagged: false, timeRemainingSeconds: 500f);

            Assert.IsTrue(decided);
            Assert.AreEqual(RoundResult.RunnersWin, outcome.Result);
            Assert.IsTrue(outcome.IsDecided);
        }

        // 12) 시간 초과 + 아무도 탈출 못함 → 술래 승리.
        [Test]
        public void TimeExpiredWithNoEscape_DecidesSeekerWin()
        {
            var outcome = new RoundOutcomeTracker();

            bool decided = outcome.Evaluate(valvesOpened: 2, totalValves: 3,
                allRunnersTagged: false, timeRemainingSeconds: 0f);

            Assert.IsTrue(decided);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // 13) 시간이 남고 아무 조건도 성립하지 않으면 미결이며, 판정도 래치되지 않는다.
        [Test]
        public void NothingAchieved_StaysUndecided()
        {
            var outcome = new RoundOutcomeTracker();

            bool decided = outcome.Evaluate(valvesOpened: 1, totalValves: 3,
                allRunnersTagged: false, timeRemainingSeconds: 400f);

            Assert.IsFalse(decided);
            Assert.AreEqual(RoundResult.InProgress, outcome.Result);
            Assert.IsFalse(outcome.IsDecided);
        }

        // 14) **판정 래치**: 한 번 결정되면 이후 재평가로 뒤집히지 않는다
        //     (라운드 종료는 되돌릴 수 없음). 종료 처리도 1회만 일어나야 한다.
        [Test]
        public void Verdict_IsLatched_AndDecidesOnlyOnce()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            bool first = outcome.Evaluate(3, 3, false, 500f);   // RunnersWin 확정
            bool second = outcome.Evaluate(0, 3, true, 0f);     // 술래 승리 조건으로 재평가 시도

            Assert.IsTrue(first);
            Assert.IsFalse(second, "이미 결정된 라운드는 다시 결정되지 않는다");
            Assert.AreEqual(RoundResult.RunnersWin, outcome.Result);
        }

        // 15) 라운드가 끝난 뒤에는 추가 탈출이 집계되지 않는다.
        [Test]
        public void EscapeAfterRoundDecided_IsIgnored()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.Evaluate(0, 3, false, 0f); // 시간 초과 → SeekerWin

            bool escaped = outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            Assert.IsFalse(escaped);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // 16) 시간 초과 시점에 이미 탈출자가 있었다면 러너 승리가 우선한다
        //     (§6.3 의사코드의 if/else 순서 — 탈출은 취소되지 않는 달성이다).
        [Test]
        public void TimeExpiredButSomeoneEscaped_RunnersWinTakesPrecedence()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            outcome.Evaluate(valvesOpened: 3, totalValves: 3,
                allRunnersTagged: false, timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.RunnersWin, outcome.Result);
        }

        // 17) 타이머 만료 → 판정까지 이어지는 흐름(코디네이터가 하는 일의 순수 부분).
        [Test]
        public void TimerExpiry_LeadsToSeekerWinVerdict()
        {
            var timer = new RoundTimer();
            var outcome = new RoundOutcomeTracker();
            timer.Expired += () => outcome.Evaluate(1, 3, false, timer.RemainingSeconds);

            timer.Start(5f);
            timer.Tick(2f);
            Assert.IsFalse(outcome.IsDecided, "아직 시간이 남았다");

            timer.Tick(3f); // 만료

            Assert.IsTrue(outcome.IsDecided);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }
    }
}
