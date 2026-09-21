using System.Collections.Generic;
using Marco.Core.Objectives;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §6.3 [v0.4] 승패 판정 3분법을 고정한다.
    ///
    /// <para>
    /// <b>이 파일은 v0.4에서 전면 재작성됐다.</b> v0.3 판정은 `(밸브 개방, 탈출, 태그, 시간)`
    /// 네 입력의 함수였고 "태그 2명 = 술래 승"이 핵심이었다. v0.4는 —
    /// </para>
    ///
    /// <list type="number">
    /// <item>게이트가 <b>판정식에서 빠졌다</b>(탈출의 전제 조건으로 옮겨졌다)</item>
    /// <item><c>TagWinThreshold</c>가 <b>삭제됐다</b> — 술래는 전원을 잡아야 이긴다</item>
    /// <item>탈출 요구가 <c>⌈도망자 ÷ 2⌉</c>로 <b>일반화됐다</b></item>
    /// <item><c>lastSurvivorEscaped</c>라는 <b>순서 의존 항</b>이 생겼다</item>
    /// </list>
    ///
    /// <para>
    /// 아래 전수 조합표가 §6.3의 <b>52케이스</b>를 그대로 재현하며, 그 중
    /// <b>순서에 의존하는 4건</b>을 따로 고정한다.
    /// </para>
    /// </summary>
    public class WinConditionEvaluatorTests
    {
        private const float TimeLeft = 300f;
        private const float TimeUp = 0f;

        /// <summary>판정 호출을 §6.3 입력 이름으로 감싼다.</summary>
        private static RoundResult Verdict(int runners, int escaped, int taggedOut,
            bool lastSurvivorEscaped = false, float remaining = TimeLeft)
        {
            var census = new RunnerCensus(runners, taggedOut, escaped);
            return WinConditionEvaluator.Evaluate(
                census.Total, census.Escaped, census.Alive, lastSurvivorEscaped, remaining);
        }

        // ── 폐지된 상수가 실제로 사라졌는가 ─────────────────────────────

        [Test]
        public void RemovedConstants_AreGone()
        {
            // §6.3 [v0.4]가 "태그 2명 도달 시 즉시 종료"를 삭제했고,
            // §6.2 [v0.4]가 탈출 요구를 ⌈n/2⌉로 일반화했다.
            // 두 상수가 코드에 남아 있으면 6인 게임에서 틀린다.
            System.Type t = typeof(WinConditionEvaluator);

            Assert.IsNull(t.GetField("TagWinThreshold"),
                "§6.3 [v0.4] TagWinThreshold는 삭제됐다 — 술래는 전원을 잡아야 이긴다.");
            Assert.IsNull(t.GetField("EscapeWinThreshold"),
                "§6.2 [v0.4] 탈출 요구는 ⌈도망자/2⌉로 유도한다 — 상수 2를 남기면 6인에서 틀린다.");
        }

        // ── §6.2 [v0.4] 탈출 요구 ───────────────────────────────────────

        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        public void EscapeRequirement_IsCeilingOfHalf(int runners, int required)
        {
            Assert.AreEqual(required, WinConditionEvaluator.EscapeRequirement(runners));
        }

        // ── §6.5-1 페이즈 진입 경계 ────────────────────────────────────

        [Test]
        public void PhaseEntry_BoundaryIsExactlyOneAlive()
        {
            // 경계값: 살아있는 2명 → 미진입 / 1명 → 진입 / 0명 → 페이즈가 아니라 SeekerWin
            Assert.IsFalse(WinConditionEvaluator.ShouldEnterLastSurvivorPhase(2));
            Assert.IsTrue(WinConditionEvaluator.ShouldEnterLastSurvivorPhase(1));
            Assert.IsFalse(WinConditionEvaluator.ShouldEnterLastSurvivorPhase(0));

            // 0명은 술래 승리여야 한다 — 지킬 사람이 없다.
            Assert.AreEqual(RoundResult.SeekerWin, Verdict(runners: 3, escaped: 0, taggedOut: 3));
        }

        [Test]
        public void TwoTaggedOfThree_IsPhaseEntry_NotSeekerWin()
        {
            // ★ v0.4의 핵심 변경. v0.3에서는 여기가 술래 승리였다.
            var census = new RunnerCensus(total: 3, taggedOut: 2, escaped: 0);

            Assert.AreEqual(1, census.Alive);
            Assert.IsTrue(census.ShouldEnterLastSurvivorPhase);
            Assert.AreEqual(RoundResult.InProgress, Verdict(3, 0, 2),
                "도망자 1명이 남은 것은 §6.5 페이즈의 시작이지 술래의 승리가 아니다.");
        }

        // ── §6.3 전수 조합표 — 도망자 2~5, 52케이스 ─────────────────────

        /// <summary>
        /// §6.3 전수검증을 코드로 재현한다. (탈출 e, 태그 t, 미탈출 n) 조합에서
        /// <c>e + t + n = 도망자 수</c>이며, <b>시간 종료 시점</b>의 판정을 본다
        /// (미탈출은 §6.3 정의상 시간 종료 시점에만 확정되는 값이다).
        /// </summary>
        [TestCase(2, 1)]
        [TestCase(3, 2)]
        [TestCase(4, 2)]
        [TestCase(5, 3)]
        public void ExhaustiveTable_MatchesDesignDoc(int runners, int required)
        {
            int cases = 0;
            var failures = new List<string>();

            for (int escaped = 0; escaped <= runners; escaped++)
            for (int tagged = 0; tagged + escaped <= runners; tagged++)
            {
                int notEscaped = runners - escaped - tagged;
                cases++;

                // §6.3: 시간 종료 시점 판정. lastSurvivorEscaped는 사건이므로 여기서는 false
                //        (순서 의존 4건은 아래 별도 테스트가 다룬다).
                RoundResult actual = Verdict(runners, escaped, tagged, remaining: TimeUp);
                RoundResult expected = escaped >= required
                    ? RoundResult.RunnersWin
                    : RoundResult.SeekerWin;

                if (actual != expected)
                    failures.Add($"(e{escaped} t{tagged} n{notEscaped}) 기대 {expected} 실제 {actual}");
            }

            // §6.3 표의 케이스 수: 도망자 2 → 6 / 3 → 10 / 4 → 15 / 5 → 21
            int expectedCases = (runners + 1) * (runners + 2) / 2;
            Assert.AreEqual(expectedCases, cases, $"도망자 {runners} 케이스 수");
            CollectionAssert.IsEmpty(failures, string.Join(" / ", failures));
        }

        [Test]
        public void ExhaustiveTable_TotalIsFiftyTwoCases()
        {
            // §6.3 "전수검증 — 도망자 2~5 전 구간 (총 52케이스)"
            int total = 0;
            for (int runners = 2; runners <= 5; runners++)
                total += (runners + 1) * (runners + 2) / 2;

            Assert.AreEqual(52, total);
        }

        // ── ★ 순서 의존 4건 (§6.3 표) ──────────────────────────────────

        /// <summary>
        /// §6.3이 지목한 <b>순서 의존 4건</b>. 같은 최종 집계에서 마지막 이탈이
        /// <b>탈출이면 도망자 승 / 태그면 술래 승</b>으로 갈린다.
        ///
        /// <para>
        /// 그래서 <c>lastSurvivorEscaped</c>는 <b>집계에서 유도할 수 없다</b> —
        /// 최종 숫자만 보고 역산하면 이 4건에서 반드시 틀린다.
        /// </para>
        /// </summary>
        [TestCase(3, 1, 2)]
        [TestCase(4, 1, 3)]
        [TestCase(5, 1, 4)]
        [TestCase(5, 2, 3)]
        public void OrderDependentCases_SplitOnLastDeparture(int runners, int escaped, int tagged)
        {
            Assert.AreEqual(runners, escaped + tagged, "미탈출 0 조합이어야 한다.");

            // 마지막 이탈이 **태그** — 최후 생존자가 잡혔다.
            Assert.AreEqual(RoundResult.SeekerWin,
                Verdict(runners, escaped, tagged, lastSurvivorEscaped: false),
                $"도망자 {runners} e{escaped} t{tagged}: 마지막이 태그면 술래 승");

            // 마지막 이탈이 **탈출** — 최후 생존자가 배수구로 나갔다(§6.5-1 팀 승리).
            Assert.AreEqual(RoundResult.RunnersWin,
                Verdict(runners, escaped, tagged, lastSurvivorEscaped: true),
                $"도망자 {runners} e{escaped} t{tagged}: 마지막이 탈출이면 도망자 승");
        }

        [Test]
        public void OrderDependence_IsExactlyFourCases()
        {
            // §6.3 표: 52케이스 중 순서 무관 48 / 순서 의존 4.
            // 순서가 갈리는 조건은 "미탈출 0 && 탈출 < 요구 && 탈출 ≥ 1"이다 —
            // 탈출 0이면 최후 생존자가 탈출한 적이 없고, 요구를 채우면 이미 도망자 승이다.
            int orderDependent = 0;

            for (int runners = 2; runners <= 5; runners++)
            {
                int required = WinConditionEvaluator.EscapeRequirement(runners);
                for (int escaped = 0; escaped <= runners; escaped++)
                for (int tagged = 0; tagged + escaped <= runners; tagged++)
                {
                    if (runners - escaped - tagged != 0)
                        continue;
                    if (escaped < 1 || escaped >= required)
                        continue;

                    RoundResult withEscape = Verdict(runners, escaped, tagged,
                        lastSurvivorEscaped: true, remaining: TimeUp);
                    RoundResult withTag = Verdict(runners, escaped, tagged,
                        lastSurvivorEscaped: false, remaining: TimeUp);

                    if (withEscape != withTag)
                        orderDependent++;
                }
            }

            Assert.AreEqual(4, orderDependent, "§6.3 표가 센 순서 의존 케이스 수와 같아야 한다.");
        }

        // ── §6.3 v0.3과의 차이 1건 ─────────────────────────────────────

        [Test]
        public void DifferenceFromV03_IsExactlyOneCase()
        {
            // §6.3 "v0.3 10케이스와의 차이 — 1건만 바뀐다": 도망자 3, (탈출 1, 태그 2, 미탈출 0).
            // v0.3은 술래 승, v0.4는 순서 의존이다.
            Assert.AreEqual(RoundResult.SeekerWin,
                Verdict(3, 1, 2, lastSurvivorEscaped: false, remaining: TimeUp));
            Assert.AreEqual(RoundResult.RunnersWin,
                Verdict(3, 1, 2, lastSurvivorEscaped: true, remaining: TimeUp));

            // 나머지 9케이스(도망자 3)는 v0.3과 결과가 같다 — 회귀 테스트로 재사용한다.
            int sameAsV03 = 0;
            for (int escaped = 0; escaped <= 3; escaped++)
            for (int tagged = 0; tagged + escaped <= 3; tagged++)
            {
                if (escaped == 1 && tagged == 2)
                    continue; // 바뀐 1건

                RoundResult v04 = Verdict(3, escaped, tagged, remaining: TimeUp);
                RoundResult v03 = escaped >= 2 ? RoundResult.RunnersWin : RoundResult.SeekerWin;
                if (v04 == v03)
                    sameAsV03++;
            }

            Assert.AreEqual(9, sameAsV03);
        }

        // ── 판정 우선순위 ───────────────────────────────────────────────

        [Test]
        public void EscapeWins_EvenWhenTimeIsUp()
        {
            // §6.3 "탈출을 먼저 확정(도망자에게 유리하게 해석)".
            Assert.AreEqual(RoundResult.RunnersWin,
                Verdict(runners: 4, escaped: 2, taggedOut: 0, remaining: TimeUp));
        }

        [Test]
        public void LastSurvivorEscape_WinsRegardlessOfEverything()
        {
            // §6.3 "마지막 1인의 탈출은 게이트 개방 여부와 무관하게 팀 승리다."
            Assert.AreEqual(RoundResult.RunnersWin,
                Verdict(runners: 5, escaped: 1, taggedOut: 4,
                    lastSurvivorEscaped: true, remaining: TimeUp));
        }

        [Test]
        public void TimeUp_WithoutEnoughEscapes_IsSeekerWin()
        {
            Assert.AreEqual(RoundResult.SeekerWin,
                Verdict(runners: 5, escaped: 2, taggedOut: 0, remaining: TimeUp));
        }

        [Test]
        public void TimeLeft_NothingDecided_StaysInProgress()
        {
            Assert.AreEqual(RoundResult.InProgress,
                Verdict(runners: 4, escaped: 1, taggedOut: 1));
        }

        [Test]
        public void ZeroRunners_DoesNotDecideOnAliveCount()
        {
            // 역할 배정 전(도망자 0)에는 "생존 0"이 참이지만 판정해서는 안 된다 —
            // 로비·카운트다운에서 즉시 술래 승리가 나오면 라운드가 시작되지 않는다.
            Assert.AreEqual(RoundResult.InProgress, Verdict(runners: 0, escaped: 0, taggedOut: 0));
        }

        // ── RunnerCensus (살아있는 셈의 단일 소유자) ────────────────────

        [Test]
        public void Census_AliveIsTotalMinusTaggedMinusEscaped()
        {
            var census = new RunnerCensus(total: 5, taggedOut: 2, escaped: 1);

            Assert.AreEqual(2, census.Alive);
            Assert.AreEqual(3, census.EscapeRequirement);
            Assert.IsFalse(census.ShouldEnterLastSurvivorPhase);
        }

        [Test]
        public void Census_AliveNeverGoesNegative()
        {
            var census = new RunnerCensus(total: 3, taggedOut: 2, escaped: 2);
            Assert.AreEqual(0, census.Alive);
        }
    }
}
