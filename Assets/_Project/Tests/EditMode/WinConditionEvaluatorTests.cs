using NUnit.Framework;
using Marco.Core.Objectives;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §6.3 승패 판정(갱신본 기준).
    ///
    /// 이 파일의 핵심은 <b>3 Runner 전수검증</b>이다 — §6.3의 정식 판정식
    /// <c>escaped &gt; (tagged + notEscaped)</c>와 코드가 쓰는 <c>escaped ≥ 2</c>가
    /// 10개 조합 전부에서 같은 결과를 내는지 고정한다. 특히 §6.3이 "여기서만 다르다"고
    /// 경고한 <b>탈출1·태그0·미탈출2</b>가 술래 승리로 나오는지 확인한다.
    /// </summary>
    public class WinConditionEvaluatorTests
    {
        private const int MvpTotalValves = 3;      // §6.2 4인 기준
        private const float TenMinutes = 600f;     // §6.2 4인 제한시간

        // ── §6.3 임계값 ──────────────────────────────────────────────────

        [Test]
        public void Thresholds_MatchDesignDoc()
        {
            // §6.3/§8: "도망자는 탈출 2명, 술래는 태그 2명 — 먼저 채우는 쪽이 승리"(§12.3)
            Assert.AreEqual(2, WinConditionEvaluator.EscapeWinThreshold);
            Assert.AreEqual(2, WinConditionEvaluator.TagWinThreshold);
        }

        // ── B1: 탈출 판정 (탈출 2명) ─────────────────────────────────────

        [Test]
        public void AllValvesOpenAndTwoEscaped_RunnersWin()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 2,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        [Test]
        public void AllValvesOpenAndOnlyOneEscaped_StillInProgress()
        {
            // 갱신 전에는 이 조합이 즉시 도망자 승리였다. §6.3 갱신으로 1명은 부족하다.
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        [Test]
        public void AllValvesOpenButNoneEscaped_StillInProgress()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        [Test]
        public void EscapedButValvesIncomplete_NotRunnersWin()
        {
            // §6.3 경계 상황: "밸브 미완료 + 시간 종료 → 탈출 인원과 무관하게 술래 승리".
            // (실전에서는 게이트가 안 열려 발생 불가하지만, 판정식 자체를 고정한다.)
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 2,
                totalValves: MvpTotalValves,
                runnersEscaped: 2,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        [Test]
        public void ValvesIncompleteAndTimeExpired_SeekerWinsRegardlessOfEscapes()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 2,
                totalValves: MvpTotalValves,
                runnersEscaped: 3,
                taggedRunners: 0,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        [Test]
        public void AllThreeRunnersEscaped_RunnersWin()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 3,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        // ── B2: 태그 2명 즉시 종료 ───────────────────────────────────────

        [Test]
        public void TwoRunnersTagged_SeekerWinsImmediately()
        {
            // §6.3 "태그 2명 도달 → 즉시 술래 승리 확정, 라운드 종료".
            // 시간이 한참 남아 있어도, 도망자가 1명 더 남아 있어도 끝난다.
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 2,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        [Test]
        public void OneRunnerTagged_DoesNotEndRound()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 1,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        [Test]
        public void AllThreeTagged_SeekerWins()
        {
            // 2명에서 이미 끝나지만, 3명이 들어와도 같은 결과여야 한다(초과 방어).
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 3,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        // ── 시간 종료 ────────────────────────────────────────────────────

        [Test]
        public void TimeExpired_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 0,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        [Test]
        public void TimeNegative_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 0,
                timeRemainingSeconds: -0.5f);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        [Test]
        public void RoundStart_InProgress()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: MvpTotalValves,
                runnersEscaped: 0,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        // ── §6.3 동일 프레임 우선순위 (탈출 → 태그 → 시간 종료) ──────────

        [Test]
        public void EscapedAndTimeExpiredSameFrame_RunnersWinTakesPrecedence()
        {
            // §6.3 "시간 종료와 탈출이 동일 프레임 → 탈출을 먼저 확정(도망자에게 유리하게)".
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 2,
                taggedRunners: 0,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        [Test]
        public void TwoEscapedAndOneTaggedSameFrame_RunnersWinTakesPrecedence()
        {
            // 탈출 2 + 태그 1 = 3명 확정. 탈출이 먼저이므로 도망자 승.
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 2,
                taggedRunners: 1,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.RunnersWin, result);
        }

        [Test]
        public void OneEscapedAndTwoTagged_SeekerWins()
        {
            // §6.3 경계표: "태그 2명과 탈출이 동일 프레임 → 탈출1·태그2 = 술래 승".
            // 탈출을 먼저 확정해도 1명뿐이라 도망자 조건에 못 미치고, 태그 2명이 성립한다.
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                taggedRunners: 2,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        // ── 이월 C1: totalValves == 0 방어 ───────────────────────────────

        [Test]
        public void ZeroTotalValves_NeverRunnersWin()
        {
            // 밸브 목표가 구성되지 않은 씬에서는 `0 >= 0`이 참이 되어 밸브 조건이 공허하게
            // 성립한다. 목표가 없는 라운드를 "목표를 전부 달성했다"로 읽으면 안 된다.
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: 0,
                runnersEscaped: 3,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        [Test]
        public void ZeroTotalValvesAtTimeout_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: 0,
                runnersEscaped: 3,
                taggedRunners: 0,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.SeekerWin, result);
        }

        [Test]
        public void NegativeTotalValves_NeverRunnersWin()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: 0,
                totalValves: -1,
                runnersEscaped: 3,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(RoundResult.InProgress, result);
        }

        // ── §6.3 3 Runner 전수검증 (10/10) ───────────────────────────────

        /// <summary>
        /// §6.3의 정식 판정식 <c>escaped &gt; (tagged + notEscaped)</c>를 테스트 안에서
        /// 그대로 계산한다 — 코드의 <c>escaped ≥ 2</c>와 비교할 **독립적인 기준**이 필요하다.
        /// </summary>
        private static RoundResult DesignDocFormula(int escaped, int tagged, int notEscaped)
        {
            return escaped > tagged + notEscaped ? RoundResult.RunnersWin : RoundResult.SeekerWin;
        }

        /// <summary>
        /// §6.3 "3 Runner 전수검증" 표 10행. 세 상태의 합은 항상 3이다(도망자 3명 고정, §1).
        ///
        /// 라운드 **종료 시점**의 평가이므로 <c>timeRemaining = 0</c>, 밸브는 전부 개방으로 둔다 —
        /// 그래야 판정이 오직 세 인원 조합에만 좌우된다.
        /// </summary>
        [TestCase(3, 0, 0, RoundResult.RunnersWin)]
        [TestCase(2, 1, 0, RoundResult.RunnersWin)]
        [TestCase(2, 0, 1, RoundResult.RunnersWin)]
        [TestCase(1, 2, 0, RoundResult.SeekerWin)]
        [TestCase(1, 1, 1, RoundResult.SeekerWin)]
        [TestCase(1, 0, 2, RoundResult.SeekerWin)] // ★ §6.3이 "여기서만 다르다"고 경고한 케이스
        [TestCase(0, 3, 0, RoundResult.SeekerWin)]
        [TestCase(0, 2, 1, RoundResult.SeekerWin)]
        [TestCase(0, 1, 2, RoundResult.SeekerWin)]
        [TestCase(0, 0, 3, RoundResult.SeekerWin)]
        public void ThreeRunnerExhaustive_MatchesDesignDocFormula(
            int escaped, int tagged, int notEscaped, RoundResult expected)
        {
            Assert.AreEqual(3, escaped + tagged + notEscaped,
                "§1이 도망자 3명으로 잠갔으므로 세 상태의 합은 항상 3이어야 한다.");

            // ① 기획서 원식과 기대값이 일치하는가(표 자체의 무결성)
            Assert.AreEqual(expected, DesignDocFormula(escaped, tagged, notEscaped),
                "§6.3 원식 escaped > (tagged + notEscaped)와 표의 판정이 어긋난다.");

            // ② 코드(탈출 ≥ 2)가 같은 결과를 내는가
            RoundResult actual = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: escaped,
                taggedRunners: tagged,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(expected, actual,
                $"탈출 {escaped}·태그 {tagged}·미탈출 {notEscaped}에서 코드와 §6.3 원식이 갈렸다.");

            // ③ 등가성의 근거를 직접 고정한다 — 판정은 "탈출 ≥ 2"와 정확히 같다.
            Assert.AreEqual(escaped >= WinConditionEvaluator.EscapeWinThreshold,
                actual == RoundResult.RunnersWin,
                "도망자 승리는 '탈출 2명 이상'과 정확히 일치해야 한다.");
        }

        /// <summary>
        /// 위 전수검증의 ★ 케이스를 단독으로 못박는다 — 갱신 전 코드(<c>escaped &gt;= 1</c>)는
        /// 이 조합을 **도망자 승리**로 판정했다. 회귀하면 여기서 잡힌다.
        /// </summary>
        [Test]
        public void TimeoutWithOneEscapedAndNoneTagged_SeekerWins()
        {
            var result = WinConditionEvaluator.Evaluate(
                valvesOpened: MvpTotalValves,
                totalValves: MvpTotalValves,
                runnersEscaped: 1,
                taggedRunners: 0,
                timeRemainingSeconds: 0f);

            Assert.AreEqual(RoundResult.SeekerWin, result,
                "탈출1·태그0·미탈출2는 §6.3 갱신본에서 술래 승리다(HUD '탈출 2명'과의 불일치 제거).");
        }

        // ── 밸브 상태기계 연동 ───────────────────────────────────────────

        [Test]
        public void IntegratesWithValveStateMachine()
        {
            // T3 연동: Valve 3개를 실제로 열어 판정 입력을 만들어도 러너 승이 나온다.
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
                runnersEscaped: 2,
                taggedRunners: 0,
                timeRemainingSeconds: TenMinutes);

            Assert.AreEqual(3, opened);
            Assert.AreEqual(RoundResult.RunnersWin, result);
        }
    }
}
