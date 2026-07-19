using NUnit.Framework;
using Marco.Core.Objectives;

namespace Marco.Core.Tests
{
    /// <summary>
    /// T4 수용 기준(docs/phase-1-분석.md §3):
    /// "밸브 3개 + 1인 탈출 → 러너 승, 전원 태그 → 술래 승".
    /// §6.2의 4인(MVP) 구간은 밸브 3개 · 제한시간 10분이다.
    /// </summary>
    public class WinConditionEvaluatorTests
    {
        private const int MvpTotalValves = 3;      // §6.2 4인 기준
        private const float TenMinutes = 600f;     // §6.2 4인 제한시간

        // 1) 밸브 전부 개방 + 1인 탈출 → 러너 승.
        [Test]
        public void AllValvesOpenAndOneEscaped_RunnersWin()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                allRunnersTagged: false,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        // 2) 밸브는 다 열렸지만 아직 아무도 탈출하지 않음 → 진행 중.
        [Test]
        public void AllValvesOpenButNoneEscaped_StillInProgress()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                allRunnersTagged: false,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        // 3) 밸브가 덜 열린 상태에서는 탈출 카운트가 있어도 러너 승이 아니다.
        //    (실전에서는 게이트가 안 열려 발생 불가하지만, 판정식 자체를 고정한다.)
        [Test]
        public void EscapedButValvesIncomplete_NotRunnersWin()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 2,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                allRunnersTagged: false,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        // 4) 도망자 전원 태그 → 술래 승.
        [Test]
        public void AllRunnersTagged_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 1,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                allRunnersTagged: true,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        // 5) 제한시간 종료 → 술래 승.
        [Test]
        public void TimeExpired_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 2,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                allRunnersTagged: false,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        // 6) 시간이 음수로 넘어가도 술래 승(오버슛 방어).
        [Test]
        public void TimeNegative_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                allRunnersTagged: false,
                timeRemainingSeconds: -0.5f);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        // 7) 판정 순서 검증: 탈출과 시간 초과가 겹치면 러너 승이 우선한다.
        [Test]
        public void EscapedAndTimeExpiredSameFrame_RunnersWinTakesPrecedence()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                allRunnersTagged: false,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        // 8) 판정 순서 검증: 1명이 탈출했고 남은 전원이 태그돼도 러너 승이 우선한다.
        //    탈출한 도망자는 이미 결과를 확정지었으므로 나중에 뒤집히지 않는다.
        [Test]
        public void OneEscapedAndRestTagged_RunnersWinTakesPrecedence()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                allRunnersTagged: true,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        // 9) 아무 조건도 성립하지 않은 라운드 초반 → 진행 중.
        [Test]
        public void RoundStart_InProgress()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                allRunnersTagged: false,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        // 10) 도망자 3명 전원 탈출 → 러너 승.
        [Test]
        public void AllThreeRunnersEscaped_RunnersWin()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 3,
                allRunnersTagged: false,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        // 11) T3 연동: Valve 3개를 실제로 열어 판정 입력을 만들어도 러너 승이 나온다.
        //     (밸브 상태기계와 승패 판정이 같은 수치 위에서 맞물리는지 확인)
        [Test]
        public void IntegratesWithValveStateMachine()
        {
            var valves = new[] { new Valve(), new Valve(), new Valve() };
            foreach (var valve in valves)
            {
                valve.TryBeginRotation(playerId: 1, role: Marco.Core.Role.RoleType.Runner);
                valve.Tick(Valve.DefaultRotationSeconds);
            }

            int opened = 0;
            foreach (var valve in valves)
            {
                if (valve.State == ValveState.Open)
                    opened++;
            }

            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: opened,
                totalValves: valves.Length,
                runnersEscaped: 1,
                allRunnersTagged: false,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(3, opened);
            Assert.AreEqual(RoundResult.RunnersWin, result);
        }
    }
}
