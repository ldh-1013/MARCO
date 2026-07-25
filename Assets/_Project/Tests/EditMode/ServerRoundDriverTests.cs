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
    /// 그리고 고정 분모 없이 태그 대상 집합에서 전원 태그를 계산하는 GAP-19 규칙이다.
    ///
    /// §6.3 판정식 자체는 <see cref="WinConditionEvaluator"/>(11케이스로 이미 고정)에 위임하므로
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
            Assert.IsTrue(d.Evaluate(0, TotalValves, false)); // → SeekerWin(시간 초과)
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
            Assert.IsTrue(d.Evaluate(0, TotalValves, false)); // SeekerWin(시간)
            Assert.IsFalse(d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true));
            Assert.AreEqual(0, d.EscapedCount);
        }

        // ── §6.3 판정 위임 + 래치 ────────────────────────────────────────

        [Test]
        public void Evaluate_AllValvesOpen_OneEscaped_RunnersWin()
        {
            var d = new ServerRoundDriver(600f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            Assert.IsTrue(d.Evaluate(TotalValves, TotalValves, false));
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        [Test]
        public void Evaluate_TimeExpired_SeekerWin()
        {
            var d = new ServerRoundDriver(0f);
            Assert.IsTrue(d.Evaluate(0, TotalValves, false));
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);
        }

        [Test]
        public void Evaluate_AllRunnersTagged_SeekerWin_EvenWithTimeLeft()
        {
            var d = new ServerRoundDriver(600f); // 시간 충분
            Assert.IsTrue(d.Evaluate(0, TotalValves, allRunnersTagged: true));
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);
        }

        [Test]
        public void Evaluate_NothingMet_StaysInProgress()
        {
            var d = new ServerRoundDriver(600f);
            Assert.IsFalse(d.Evaluate(1, TotalValves, false));
            Assert.AreEqual(RoundResult.InProgress, d.Result);
            Assert.IsFalse(d.IsDecided);
        }

        [Test]
        public void Evaluate_Latches_SecondCallReturnsFalse_ResultUnchanged()
        {
            var d = new ServerRoundDriver(0f);
            Assert.IsTrue(d.Evaluate(0, TotalValves, false));  // SeekerWin(시간)
            Assert.IsFalse(d.Evaluate(TotalValves, TotalValves, false)); // 이미 결정됨 — 무시
            Assert.AreEqual(RoundResult.SeekerWin, d.Result);  // 러너 승리로 바뀌지 않는다
        }

        [Test]
        public void Evaluate_EscapePriorityOverTimeout_WhenBothTrue()
        {
            // 시간도 만료(0)이고 탈출도 성립 → §6.3은 탈출(RunnersWin)을 우선한다.
            var d = new ServerRoundDriver(0f);
            d.TryRegisterEscape(1, RoleType.Runner, gateOpen: true);
            Assert.IsTrue(d.Evaluate(TotalValves, TotalValves, false));
            Assert.AreEqual(RoundResult.RunnersWin, d.Result);
        }

        // ── GAP-19: 고정 분모 없는 전원 태그 판정 ─────────────────────────

        [Test]
        public void AllRunnersTagged_NullList_False()
        {
            Assert.IsFalse(ServerRoundDriver.AllRunnersTagged(null));
        }

        [Test]
        public void AllRunnersTagged_NoTargets_False()
        {
            Assert.IsFalse(ServerRoundDriver.AllRunnersTagged(new List<ITagTarget>()));
        }

        [Test]
        public void AllRunnersTagged_UntaggedRunnerRemains_False()
        {
            var list = new List<ITagTarget> { TaggedEcho(1), Runner(2) };
            Assert.IsFalse(ServerRoundDriver.AllRunnersTagged(list));
        }

        [Test]
        public void AllRunnersTagged_AllRunnersTurnedEcho_True()
        {
            var list = new List<ITagTarget> { TaggedEcho(1), TaggedEcho(2) };
            Assert.IsTrue(ServerRoundDriver.AllRunnersTagged(list));
        }

        [Test]
        public void AllRunnersTagged_OnlySeekers_False()
        {
            // 러너가 애초에 없으면 공허한 참을 방지한다(태그된 대상 0).
            var list = new List<ITagTarget> { Seeker(1) };
            Assert.IsFalse(ServerRoundDriver.AllRunnersTagged(list));
        }

        [Test]
        public void AllRunnersTagged_IgnoresNullEntries()
        {
            var list = new List<ITagTarget> { null, TaggedEcho(1) };
            Assert.IsTrue(ServerRoundDriver.AllRunnersTagged(list));
        }
    }
}
