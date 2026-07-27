using NUnit.Framework;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.UI;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 16: §12.4 HUD 표시 문자열 규칙을 고정한다.
    ///
    /// HUD 작업 대부분은 Unity 컴포넌트 배선이라 시각 확인이 중심이지만, **표기 규칙**은 순수
    /// 로직이라 여기서 고정한다 — 특히 §12.4 도식의 `⚙ 0/3` 형식과 타이머 표기 경계(마지막 1초·음수).
    /// </summary>
    public class HudFormatterTests
    {
        // ── 타이머 표기 (§6.2 제한시간) ───────────────────────────────────

        [TestCase(600f, "10:00")] // §6.2 4인 MVP 제한시간
        [TestCase(599f, "9:59")]
        [TestCase(60f, "1:00")]
        [TestCase(59f, "0:59")]
        [TestCase(9f, "0:09")]
        [TestCase(0f, "0:00")]
        public void FormatRemainingTime_FormatsAsMinutesSeconds(float seconds, string expected)
        {
            Assert.AreEqual(expected, HudFormatter.FormatRemainingTime(seconds));
        }

        [Test]
        public void FormatRemainingTime_Negative_ClampsToZero()
        {
            // 서버 타이머는 0에서 멈추지만, 표시가 음수를 그리지 않는 것을 별도로 보장한다.
            Assert.AreEqual("0:00", HudFormatter.FormatRemainingTime(-5f));
        }

        [Test]
        public void FormatRemainingTime_LastSecond_ShowsOneNotZero()
        {
            // 내림으로 하면 남은 0.4초가 "0:00"이 되어 이미 끝난 것처럼 보인다 → 올림.
            Assert.AreEqual("0:01", HudFormatter.FormatRemainingTime(0.4f));
            Assert.AreEqual("0:01", HudFormatter.FormatRemainingTime(1f));
        }

        [Test]
        public void FormatRemainingTime_RoundsUp_NotDown()
        {
            Assert.AreEqual("1:00", HudFormatter.FormatRemainingTime(59.5f));
        }

        // ── 밸브 카운트 (§12.4 도식 `⚙ 0/3`) ──────────────────────────────

        [TestCase(0, 3, "⚙ 0/3")]
        [TestCase(1, 3, "⚙ 1/3")]
        [TestCase(3, 3, "⚙ 3/3")]
        public void FormatValveCount_MatchesDesignDocGlyphFormat(int opened, int total, string expected)
        {
            Assert.AreEqual(expected, HudFormatter.FormatValveCount(opened, total));
        }

        [Test]
        public void FormatValveCount_NegativeInputs_ClampToZero()
        {
            Assert.AreEqual("⚙ 0/0", HudFormatter.FormatValveCount(-1, -1));
        }

        [Test]
        public void ValveGlyph_IsDesignDocSymbol()
        {
            // §12.4 도식이 쓴 기호를 그대로 유지한다(바꾸면 이 테스트가 먼저 깨진다).
            Assert.AreEqual("⚙", HudFormatter.ValveGlyph);
        }

        // ── 역할 표기 (GAP-26: §12.4 미명시) ──────────────────────────────

        [TestCase(RoleType.Seeker, "술래")]
        [TestCase(RoleType.Runner, "도망자")]
        [TestCase(RoleType.Echo, "메아리")]
        public void FormatRole_UsesDesignDocKoreanNames(RoleType role, string expected)
        {
            // §3 용어집의 한국어 명칭(술래/도망자/메아리)을 그대로 쓴다.
            Assert.AreEqual(expected, HudFormatter.FormatRole(role));
        }

        // ── 게이트 안내 (§6.1) ────────────────────────────────────────────

        [Test]
        public void FormatGateHint_GateOpen_ShowsEscapeAvailable()
        {
            Assert.IsNotEmpty(HudFormatter.FormatGateHint(true));
        }

        [Test]
        public void FormatGateHint_GateClosed_ShowsNothing()
        {
            // 아직 안 열렸으면 화면에 아무것도 그리지 않는다(§16.1 미니멀 원칙).
            Assert.IsEmpty(HudFormatter.FormatGateHint(false));
        }

        // ── 결과 배너 (§6.3) ──────────────────────────────────────────────

        [TestCase(RoundResult.RunnersWin, "도망자 승리")]
        [TestCase(RoundResult.SeekerWin, "술래 승리")]
        public void FormatRoundResult_DecidedRounds_ShowBanner(RoundResult result, string expected)
        {
            Assert.AreEqual(expected, HudFormatter.FormatRoundResult(result));
        }

        [Test]
        public void FormatRoundResult_InProgress_ShowsNothing()
        {
            // 진행 중에는 배너를 그리지 않아야 한다 — 라운드 종료 순간에만 나타난다.
            Assert.IsEmpty(HudFormatter.FormatRoundResult(RoundResult.InProgress));
        }

        // ── 승패 사유 유도 (스프린트 17, §12.5 결과 화면) ──────────────────

        [Test]
        public void FormatResultReason_RunnersWin_IsEscape()
        {
            // §6.3 첫 분기는 "밸브 전부 + 1인 이상 탈출"뿐이라 사유가 하나로 확정된다.
            // 남은 시간이 얼마든 결과는 같아야 한다.
            string atFullTime = HudFormatter.FormatResultReason(RoundResult.RunnersWin, 300f);
            string atZero = HudFormatter.FormatResultReason(RoundResult.RunnersWin, 0f);

            Assert.IsNotEmpty(atFullTime);
            Assert.AreEqual(atFullTime, atZero, "러너 승리 사유는 남은 시간과 무관하게 탈출이다");
            StringAssert.Contains("탈출", atFullTime);
        }

        [Test]
        public void FormatResultReason_SeekerWin_TimeExpired_IsTimeout()
        {
            // §6.3 둘째 분기에서 timeRemaining <= 0이면 시간 초과다.
            StringAssert.Contains("제한시간", HudFormatter.FormatResultReason(RoundResult.SeekerWin, 0f));
        }

        [Test]
        public void FormatResultReason_SeekerWin_TimeRemaining_IsAllTagged()
        {
            // 시간이 남은 채 술래가 이기는 경로는 전원 태그뿐이다(서버가 확정 시 타이머를 멈추므로
            // 남은 시간 값이 그대로 보존된다 — ServerRoundDriver.Tick).
            StringAssert.Contains("붙잡", HudFormatter.FormatResultReason(RoundResult.SeekerWin, 120f));
        }

        [Test]
        public void FormatResultReason_SeekerWin_NegativeTime_IsTimeout()
        {
            // 음수도 시간 초과로 읽어야 한다(표시 클램프와 별개로 판정 사유는 동일).
            StringAssert.Contains("제한시간", HudFormatter.FormatResultReason(RoundResult.SeekerWin, -1f));
        }

        [Test]
        public void FormatResultReason_InProgress_ShowsNothing()
        {
            Assert.IsEmpty(HudFormatter.FormatResultReason(RoundResult.InProgress, 100f));
        }
    }
}
