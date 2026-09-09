using NUnit.Framework;
using UnityEngine;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Tagging;
using Marco.Presentation.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 7: §3.1 태그 판정 배선을 고정한다.
    /// §6.3 판정식(11케이스)은 Core가 이미 담당하므로, 여기서는
    /// **거리 규칙 · 역할 규칙 · 집계 · 혼재 경계 케이스**만 검증한다.
    ///
    /// 기획서 §3.1: "접촉 트리거, 반경 1.2m, 1회 접촉 즉시 확정",
    ///              "태그 1회 → 메아리로 즉시 전환", 메아리는 "태그 불가".
    /// </summary>
    public class TaggingTests
    {
        private const ulong RunnerA = 101;
        private const ulong RunnerB = 102;
        private const ulong RunnerC = 103;

        // ── §3.1 거리 규칙 ────────────────────────────────────────────

        // 1) 태그 반경은 기획서 명시값 1.2m다(임의값 아님).
        [Test]
        public void TagRadius_MatchesDesignDoc()
        {
            Assert.AreEqual(1.2f, TagRules.TagRadiusMeters, 0.001f);
        }

        [Test]
        public void WithinRange_IsTaggable()
        {
            Assert.IsTrue(TagRules.IsWithinTagRange(Vector3.zero, new Vector3(1f, 0f, 0f)));
        }

        [Test]
        public void BeyondRange_IsNotTaggable()
        {
            Assert.IsFalse(TagRules.IsWithinTagRange(Vector3.zero, new Vector3(2f, 0f, 0f)));
        }

        // 2) 경계값 1.2m 정확히는 포함(<= 비교).
        [Test]
        public void ExactlyAtRadius_IsTaggable()
        {
            Assert.IsTrue(TagRules.IsWithinTagRange(Vector3.zero, new Vector3(1.2f, 0f, 0f)));
        }

        // ── §3.1 역할 규칙 ────────────────────────────────────────────

        // 3) 술래만 태그할 수 있고, 도망자만 대상이 된다.
        [Test]
        public void SeekerTagsRunner_IsAllowed()
        {
            Assert.IsTrue(TagRules.CanTag(RoleType.Seeker, RoleType.Runner));
        }

        // 4) §3.1 메아리 열: "태그 불가".
        [Test]
        public void EchoTarget_CannotBeTagged()
        {
            Assert.IsFalse(TagRules.CanTag(RoleType.Seeker, RoleType.Echo));
        }

        // 5) 술래는 태그 대상이 아니고, 러너·메아리는 태그 주체가 될 수 없다.
        [TestCase(RoleType.Seeker, RoleType.Seeker)]
        [TestCase(RoleType.Runner, RoleType.Runner)]
        [TestCase(RoleType.Echo, RoleType.Runner)]
        public void InvalidRoleCombinations_AreRejected(RoleType taggerRole, RoleType targetRole)
        {
            Assert.IsFalse(TagRules.CanTag(taggerRole, targetRole));
        }

        // ── 태그 집계 ──────────────────────────────────────────────────

        // 6) 태그가 집계된다.
        [Test]
        public void Tag_IsCounted()
        {
            var outcome = new RoundOutcomeTracker();

            bool tagged = outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            Assert.IsTrue(tagged);
            Assert.AreEqual(1, outcome.TaggedCount);
            Assert.IsTrue(outcome.IsTagged(RunnerA));
        }

        // 7) §3.1: 태그된 대상은 메아리가 되므로 재태그가 구조적으로 불가능하다.
        //    (역할이 Echo로 넘어오면 거부된다 — 별도 쿨다운이 필요 없는 이유)
        [Test]
        public void RetagAsEcho_IsRejected()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            bool second = outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Echo);

            Assert.IsFalse(second);
            Assert.AreEqual(1, outcome.TaggedCount);
        }

        // 8) 같은 ID를 Runner로 다시 보내도 중복 집계되지 않는다(방어).
        [Test]
        public void SameRunnerTwice_CountsOnce()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            bool second = outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            Assert.IsFalse(second);
            Assert.AreEqual(1, outcome.TaggedCount);
        }

        // 9) 탈출한 러너는 태그할 수 없다(이미 맵을 벗어났다).
        [Test]
        public void EscapedRunner_CannotBeTagged()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            bool tagged = outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            Assert.IsFalse(tagged);
            Assert.AreEqual(0, outcome.TaggedCount);
        }

        // 10) 라운드가 끝난 뒤에는 태그가 집계되지 않는다.
        [Test]
        public void TagAfterRoundDecided_IsIgnored()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.Evaluate(0, 3, 0, 0f); // 시간 초과 → SeekerWin

            bool tagged = outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            Assert.IsFalse(tagged);
        }

        // ── §6.3 태그 2명 종료 조건 (GAP-13 소멸) ──────────────────────

        // 11) 태그 1명에서는 라운드가 끝나지 않는다.
        [Test]
        public void OneTagged_DoesNotEndRound()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            Assert.AreEqual(1, outcome.TaggedCount);

            bool decided = outcome.Evaluate(0, 3, outcome.TaggedCount, 300f);
            Assert.IsFalse(decided);
            Assert.AreEqual(RoundResult.InProgress, outcome.Result);
        }

        // 12) **핵심(B2)**: 태그 2명 도달 시 시간이 남아 있어도, 도망자가 1명 남아 있어도
        //     즉시 술래 승리로 확정된다(§6.3 "술래는 전부 태그할 필요가 없다").
        [Test]
        public void TwoTagged_DecidesSeekerWinImmediately()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);

            Assert.AreEqual(2, outcome.TaggedCount);

            bool decided = outcome.Evaluate(0, 3, outcome.TaggedCount, timeRemainingSeconds: 500f);

            Assert.IsTrue(decided);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // 13) RunnerC는 태그되지 않은 채 남아 있어도 결과가 확정된다 —
        //     남은 1명이 탈출해도 탈출 1 < 2라 도망자 승리가 산술적으로 불가능하기 때문이다.
        [Test]
        public void TwoTagged_ThirdRunnerStillFree_RoundStillEnds()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.Evaluate(3, 3, outcome.TaggedCount, 500f);

            Assert.IsFalse(outcome.IsTagged(RunnerC), "세 번째 도망자는 아직 자유롭다");
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // ── 혼재 경계 케이스 ───────────────────────────────────────────

        // 14) §6.3 경계표: "태그 2명과 탈출이 동일 프레임 → 탈출1·태그2 = 술래 승".
        [Test]
        public void OneEscapedTwoTagged_SeekerWins()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerC, RoleType.Runner);

            outcome.Evaluate(valvesOpened: 3, totalValves: 3,
                taggedRunners: outcome.TaggedCount, timeRemainingSeconds: 200f);

            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result,
                "탈출을 먼저 확정해도 1명뿐이라 도망자 조건에 못 미친다");
        }

        // 15) **가장 중요한 경계**: 혼재 상황에서도 §6.3의 우선순위(탈출 → 태그)가 지배한다.
        //     밸브 완료 + 2명 탈출이면, 나머지가 태그돼도 러너 승리다.
        [Test]
        public void TwoEscapesWithValvesComplete_BeatRemainingTags()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            outcome.TryRegisterEscape(RunnerB, RoleType.Runner, gateOpen: true);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerC, RoleType.Runner);

            outcome.Evaluate(valvesOpened: 3, totalValves: 3,
                taggedRunners: outcome.TaggedCount, timeRemainingSeconds: 200f);

            Assert.AreEqual(RoundResult.RunnersWin, outcome.Result,
                "탈출은 §6.3 우선순위 1이라 태그보다 앞선다");
        }

        // 16) 탈출 전에 태그 2명이 되면 술래 승리로 확정되고, 이후 탈출은 무의미하다
        //     (라운드가 이미 끝났으므로 등록 자체가 거부된다).
        [Test]
        public void TwoTaggedBeforeEscape_LatchesSeekerWin()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.Evaluate(3, 3, outcome.TaggedCount, 400f);

            bool escaped = outcome.TryRegisterEscape(RunnerC, RoleType.Runner, gateOpen: true);

            Assert.IsFalse(escaped);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // 17) §6.3 세 종료 경로가 모두 도달 가능한지 최종 확인.
        [Test]
        public void AllThreeVerdictPaths_AreReachable()
        {
            // (a) 러너 승리 — 밸브 완료 + 탈출 2명
            var a = new RoundOutcomeTracker();
            a.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            a.TryRegisterEscape(RunnerB, RoleType.Runner, gateOpen: true);
            a.Evaluate(3, 3, 0, 300f);
            Assert.AreEqual(RoundResult.RunnersWin, a.Result);

            // (b) 술래 승리 — 태그 2명(시간 남음)
            var b = new RoundOutcomeTracker();
            b.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            b.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            b.Evaluate(0, 3, b.TaggedCount, 300f);
            Assert.AreEqual(RoundResult.SeekerWin, b.Result);

            // (c) 술래 승리 — 시간 초과
            var c = new RoundOutcomeTracker();
            c.Evaluate(0, 3, 0, 0f);
            Assert.AreEqual(RoundResult.SeekerWin, c.Result);
        }
    }
}
