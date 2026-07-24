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
            outcome.Evaluate(0, 3, false, 0f); // 시간 초과 → SeekerWin

            bool tagged = outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);

            Assert.IsFalse(tagged);
        }

        // ── §6.3 allRunnersTagged 판정 (GAP-13) ────────────────────────

        // 11) 일부만 태그된 상태에서는 전원 태그가 아니다 → 라운드 계속.
        [Test]
        public void PartiallyTagged_IsNotAllTagged()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);

            Assert.IsFalse(outcome.AreAllRunnersTagged(totalRunners: 3));

            bool decided = outcome.Evaluate(0, 3, outcome.AreAllRunnersTagged(3), 300f);
            Assert.IsFalse(decided);
            Assert.AreEqual(RoundResult.InProgress, outcome.Result);
        }

        // 12) **핵심**: 전원 태그 시 시간이 남아 있어도 즉시 술래 승리.
        [Test]
        public void AllTagged_DecidesSeekerWinImmediately()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerC, RoleType.Runner);

            Assert.IsTrue(outcome.AreAllRunnersTagged(3));

            bool decided = outcome.Evaluate(0, 3, outcome.AreAllRunnersTagged(3), timeRemainingSeconds: 500f);

            Assert.IsTrue(decided);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // 13) 러너가 0명이면 전원 태그로 치지 않는다(공허한 참 방지).
        [Test]
        public void ZeroRunners_IsNotAllTagged()
        {
            var outcome = new RoundOutcomeTracker();

            Assert.IsFalse(outcome.AreAllRunnersTagged(totalRunners: 0));
        }

        // ── 혼재 경계 케이스 (검증 체크리스트 4번) ──────────────────────

        // 14) GAP-13: 1명 탈출 + 2명 태그(총 3명) → 탈출자는 태그된 적이 없으므로
        //     "전원 태그"가 아니다.
        [Test]
        public void OneEscapedTwoTagged_IsNotAllTagged()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerC, RoleType.Runner);

            Assert.IsFalse(outcome.AreAllRunnersTagged(totalRunners: 3));
        }

        // 15) **가장 중요한 경계**: 혼재 상황에서도 §6.3의 if/else 순서가 지배한다.
        //     밸브 완료 + 1명 탈출이면, 나머지가 전부 태그돼도 러너 승리다.
        [Test]
        public void EscapeWithValvesComplete_BeatsRemainingTags()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerC, RoleType.Runner);

            outcome.Evaluate(valvesOpened: 3, totalValves: 3,
                allRunnersTagged: outcome.AreAllRunnersTagged(3), timeRemainingSeconds: 200f);

            Assert.AreEqual(RoundResult.RunnersWin, outcome.Result,
                "탈출은 §6.3 첫 분기라 태그보다 우선한다");
        }

        // 16) 탈출 전에 전원 태그되면 술래 승리로 확정되고, 이후 탈출은 무의미하다
        //     (라운드가 이미 끝났으므로 등록 자체가 거부된다).
        [Test]
        public void AllTaggedBeforeEscape_LatchesSeekerWin()
        {
            var outcome = new RoundOutcomeTracker();
            outcome.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerB, RoleType.Runner);
            outcome.TryRegisterTag(RoleType.Seeker, RunnerC, RoleType.Runner);
            outcome.Evaluate(3, 3, outcome.AreAllRunnersTagged(3), 400f);

            bool escaped = outcome.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);

            Assert.IsFalse(escaped);
            Assert.AreEqual(RoundResult.SeekerWin, outcome.Result);
        }

        // 17) §6.3 세 분기가 모두 도달 가능해졌는지 최종 확인 —
        //     이번 스프린트의 목적 자체를 고정하는 테스트.
        [Test]
        public void AllThreeVerdictPaths_AreReachable()
        {
            // (a) 러너 승리 — 밸브 완료 + 탈출
            var a = new RoundOutcomeTracker();
            a.TryRegisterEscape(RunnerA, RoleType.Runner, gateOpen: true);
            a.Evaluate(3, 3, false, 300f);
            Assert.AreEqual(RoundResult.RunnersWin, a.Result);

            // (b) 술래 승리 — 전원 태그(시간 남음) ← 이번 스프린트로 열린 경로
            var b = new RoundOutcomeTracker();
            b.TryRegisterTag(RoleType.Seeker, RunnerA, RoleType.Runner);
            b.Evaluate(0, 3, b.AreAllRunnersTagged(1), 300f);
            Assert.AreEqual(RoundResult.SeekerWin, b.Result);

            // (c) 술래 승리 — 시간 초과
            var c = new RoundOutcomeTracker();
            c.Evaluate(0, 3, false, 0f);
            Assert.AreEqual(RoundResult.SeekerWin, c.Result);
        }
    }
}
