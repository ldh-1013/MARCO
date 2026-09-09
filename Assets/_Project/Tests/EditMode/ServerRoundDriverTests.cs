using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Marco.Core.GameFlow;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 12: 서버 권위 라운드 판정기(<see cref="ServerRoundDriver"/>)의 계약을 고정한다.
    /// 핵심은 "서버 한 곳만 판정한다"의 성질 — 타이머 소유·탈출 재검증·§6.3 판정 위임·1회 래치,
    /// 그리고 태그 대상 집합에서 <b>태그 인원 수</b>를 세는 규칙이다(§6.3 "태그 2명 도달").
    ///
    /// §6.3 판정식 자체는 <see cref="WinConditionEvaluator"/>(3 Runner 전수검증 포함)에 위임하므로
    /// 여기서는 <b>서버 드라이버가 그 식에 올바른 입력을 만들어 넘기고 결과를 래치하는지</b>만 본다.
    /// </summary>
    public class ServerRoundDriverTests
    {
        private const int TotalValves = 3;

        private sealed class FakeTarget : ITagTarget
        {
            public ulong PlayerId { get; set; }
            public RoleType Role { get; set; }
            public bool IsTagged { get; set; }
            public Vector3 WorldPosition { get; set; }
            public bool NetworkActive { get; set; }
            public void RequestTag(ulong seekerId, RoleType seekerRole) { }
        }

        private static FakeTarget Runner(ulong id) => new FakeTarget { PlayerId = id, Role = RoleType.Runner, IsTagged = false };
        private static FakeTarget TaggedEcho(ulong id) => new FakeTarget { PlayerId = id, Role = RoleType.Echo, IsTagged = true };
        private static FakeTarget Seeker(ulong id) => new FakeTarget { PlayerId = id, Role = RoleType.Seeker, IsTagged = false };

        // ── 타이머 소유 ──────────────────────────────────────────────────

        [Test]
        public void NewDriver_RemainingEqualsDuration()
        {
            var d = new ServerRoundDriver(600f);
            Assert.AreEqual(600f, d.RemainingSeconds);
            Assert.IsFalse(d.IsDecided);
            Assert.AreEqual(RoundResult.InProgress, d.Result);
        }

        [Test]
        public void NewDriver_NegativeDuration_ClampsToZero()
        {
            var d = new ServerRoundDriver(-5f);
            Assert.AreEqual(0f, d.RemainingSeconds);
        }

        [Test]
        public void Tick_DecrementsRemaining()
        {
            var d = new ServerRoundDriver(10f);
            d.Tick(3f);
            Assert.AreEqual(7f, d.RemainingSeconds, 1e-4f);
        }

        [Test]
        public void Tick_ClampsAtZero_NeverNegative()
        {
            var d = new ServerRoundDriver(2f);
            d.Tick(5f);
            Assert.AreEqual(0f, d.RemainingSeconds);
        }

        [Test]
        public void Tick_AfterDecided_DoesNotAdvance()
        {
            var d = new ServerRoundDriver(0f); // 즉시 시간 만료 상태
            Assert.IsTrue(d.Evaluate(0, TotalValves, 0)); // → SeekerWin(시간 초과)
            d.Tick(1f);
            Assert.AreEqual(0f, d.RemainingSeconds);
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);
        }

        // ── 탈출 재검증(서버 권위, §5.3) ─────────────────────────────────

        [Test]
        public void Escape_GateClosed_IsRejectedByServer()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsFalse(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: false));
            Assert.AreEqual(0, d.EscapedCount);
        }

        [Test]
        public void Escape_NonRunner_IsRejected()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsFalse(d.TryRegisterEscape(1, RoleType.Seeker, gateOpen: true));
            Assert.IsFalse(d.TryRegisterEscape(2, RoleType.Echo, gateOpen: true));
            Assert.AreEqual(0, d.EscapedCount);
        }

        [Test]
        public void Escape_RunnerWithGateOpen_IsCounted()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsTrue(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));
            Assert.AreEqual(1, d.EscapedCount);
        }

        [Test]
        public void Escape_SamePlayerTwice_CountedOnce()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsTrue(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));
            Assert.IsFalse(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));
            Assert.AreEqual(1, d.EscapedCount);
        }

        [Test]
        public void Escape_AfterRoundDecided_IsIgnored()
        {
            var d = new ServerRoundDriver(0f);
            Assert.IsTrue(d.Evaluate(0, TotalValves, 0)); // SeekerWin(시간)
            Assert.IsFalse(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));
            Assert.AreEqual(0, d.EscapedCount);
        }

        // ── §6.3 판정 위임 + 래치 ────────────────────────────────────────

        [Test]
        public void Evaluate_AllValvesOpen_TwoEscaped_RunnersWin()
        {
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            d.TryRegisterEscape(2, RoleType.Runner, gateOpen: true);
            Assert.IsTrue(d.Evaluate(TotalValves, TotalValves, 0));
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void Evaluate_AllValvesOpen_OnlyOneEscaped_StaysInProgress()
        {
            // §6.3 갱신: 탈출 1명으로는 부족하다(탈출 2명이 목표).
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            Assert.IsFalse(d.Evaluate(TotalValves, TotalValves, 0));
            Assert.AreEqual(RoundResult.InProgress, d.Result);
        }

        [Test]
        public void Evaluate_TimeExpired_SeekerWin()
        {
            var d = new ServerRoundDriver(0f);
            Assert.IsTrue(d.Evaluate(0, TotalValves, 0));
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);
        }

        [Test]
        public void Evaluate_TwoTagged_SeekerWin_EvenWithTimeLeft()
        {
            // §6.3 "태그 2명 도달 → 즉시 술래 승리 확정, 라운드 종료".
            var d = new ServerRoundDriver(600f); // 시간 충분
            Assert.IsTrue(d.Evaluate(0, TotalValves, taggedRunners: 2));
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);
        }

        [Test]
        public void Evaluate_OneTagged_DoesNotEndRound()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsFalse(d.Evaluate(0, TotalValves, taggedRunners: 1));
            Assert.AreEqual(RoundResult.InProgress, d.Result);
        }

        [Test]
        public void Evaluate_NothingMet_StaysInProgress()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsFalse(d.Evaluate(1, TotalValves, 0));
            Assert.AreEqual(RoundResult.InProgress, d.Result);
            Assert.IsFalse(d.IsDecided);
        }

        [Test]
        public void Evaluate_Latches_SecondCallReturnsFalse_ResultUnchanged()
        {
            var d = new ServerRoundDriver(0f);
            Assert.IsTrue(d.Evaluate(0, TotalValves, 0));  // SeekerWin(시간)
            Assert.IsFalse(d.Evaluate(TotalValves, TotalValves, 0)); // 이미 결정됨 — 무시
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);  // 러너 승리로 바뀌지 않는다
        }

        [Test]
        public void Evaluate_EscapePriorityOverTimeout_WhenBothTrue()
        {
            // 시간도 만료(0)이고 탈출 2명도 성립 → §6.3은 탈출(RunnersWin)을 우선한다.
            var d = new ServerRoundDriver(0f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            d.TryRegisterEscape(2, RoleType.Runner, gateOpen: true);
            Assert.IsTrue(d.Evaluate(TotalValves, TotalValves, 0));
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void Evaluate_EscapePriorityOverTwoTagged()
        {
            // §6.3 우선순위 1(탈출) → 2(태그). 탈출 2명이 이미 성립하면 태그 2명이 겹쳐도 도망자 승.
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            d.TryRegisterEscape(2, RoleType.Runner, gateOpen: true);
            Assert.IsTrue(d.Evaluate(TotalValves, TotalValves, taggedRunners: 2));
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        // ── §6.3 태그 인원 집계 (GAP-19 소멸) ────────────────────────────

        [Test]
        public void TaggedCount_NullList_Zero()
        {
            Assert.AreEqual(0, ServerRoundDriver.TaggedCount(null));
        }

        [Test]
        public void TaggedCount_NoTargets_Zero()
        {
            Assert.AreEqual(0, ServerRoundDriver.TaggedCount(new List<ITagTarget>()));
        }

        [Test]
        public void TaggedCount_CountsOnlyTagged()
        {
            var list = new List<ITagTarget> { TaggedEcho(1), Runner(2), Runner(3) };
            Assert.AreEqual(1, ServerRoundDriver.TaggedCount(list));
        }

        [Test]
        public void TaggedCount_IgnoresNullEntries()
        {
            var list = new List<ITagTarget> { null, TaggedEcho(1), null, TaggedEcho(2) };
            Assert.AreEqual(2, ServerRoundDriver.TaggedCount(list));
        }

        [Test]
        public void TaggedCount_SeekerIsNotCounted()
        {
            // 술래는 태그 대상이 아니다(§3.1) — IsTagged가 설 일이 없지만 집계에도 안 들어간다.
            var list = new List<ITagTarget> { Seeker(0), Runner(1) };
            Assert.AreEqual(0, ServerRoundDriver.TaggedCount(list));
        }

        /// <summary>
        /// **GAP-19가 왜 소멸했는가**: 예전 <c>AllRunnersTagged</c>는 "미태그 러너가 0인가"를
        /// 봤기 때문에 <b>등록된 모집단 전체</b>가 판정에 영향을 줬다 — 씬의 대역 러너가 살아
        /// 있으면 실제 플레이어를 전부 태그해도 종료되지 않았고(스프린트 13·15), 그 분모 문제가
        /// GAP-19의 본체였다. §6.3이 "태그 2명 도달"로 바뀌면서 판정이 **절대 인원**만 보게 돼,
        /// 미태그 대상이 몇 명 등록돼 있든 결과가 달라지지 않는다.
        /// </summary>
        [Test]
        public void TaggedCount_UnaffectedByUntaggedPopulation()
        {
            var withStandIns = new List<ITagTarget>
            {
                Seeker(0), TaggedEcho(1), TaggedEcho(2),   // 실제 플레이어: 태그 2명
                Runner(101), Runner(102), Runner(103)      // 대역 3명은 미태그
            };

            Assert.AreEqual(2, ServerRoundDriver.TaggedCount(withStandIns),
                "대역이 등록돼 있어도 태그 인원 수는 달라지지 않는다(GAP-19 분모 문제 소멸).");

            var d = new ServerRoundDriver(600f);
            Assert.IsTrue(d.Evaluate(0, TotalValves, ServerRoundDriver.TaggedCount(withStandIns)));
            Assert.AreEqual(RoundResult.SeekerWin, d.Result,
                "대역이 남아 있어도 태그 2명이면 §6.3대로 라운드가 끝나야 한다.");
        }

        [Test]
        public void TaggedCount_TwoRealPlayers_OneTagged_DoesNotEndRound()
        {
            // 실기 2인 구성(술래 + 러너 1)에서는 태그 1명이 최대다 — §6.3 기준으로는
            // 라운드가 끝나지 않는다. **도망자 3명 전제(§1)를 벗어난 구성의 귀결**이며,
            // 4인 미만 테스트 플레이에서 "태그해도 안 끝난다"로 보이는 것이 정상이다.
            var list = new List<ITagTarget> { Seeker(0), TaggedEcho(1) };
            Assert.AreEqual(1, ServerRoundDriver.TaggedCount(list));

            var d = new ServerRoundDriver(600f);
            Assert.IsFalse(d.Evaluate(0, TotalValves, ServerRoundDriver.TaggedCount(list)));
            Assert.AreEqual(RoundResult.InProgress, d.Result);
        }
    }
}
