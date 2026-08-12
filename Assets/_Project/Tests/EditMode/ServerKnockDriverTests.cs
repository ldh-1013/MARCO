using System.Collections.Generic;
using Marco.Core.Role;
using Marco.Core.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 27: §3.2 메아리 노크의 서버 권위 규칙을 고정한다.
    ///
    /// 핵심 목적은 **§3.2 원문 수치(쿨다운 30초·지연 1.5초·반경 9m·지속 1.2초)를 코드가 그대로
    /// 재현하는지**와, 클라이언트가 정할 수 없는 것(역할·쿨다운·지연)을 서버가 강제하는지다.
    /// </summary>
    public class ServerKnockDriverTests
    {
        private const ulong Echo = 7;
        private const ulong Other = 8;
        private static readonly Vector3 Point = new Vector3(10f, 0f, 10f);

        // ── §3.2 상수 ────────────────────────────────────────────────────

        [Test]
        public void Config_MatchesDesignDoc()
        {
            // §3.2: "쿨다운 30초", "1.5초 지연", "반경 9m, 지속 1.2초"
            Assert.AreEqual(30f, KnockConfig.CooldownSeconds);
            Assert.AreEqual(1.5f, KnockConfig.ActivationDelaySeconds);
            Assert.AreEqual(9f, KnockConfig.RadiusMeters);
            Assert.AreEqual(1.2f, KnockConfig.DurationSeconds);
        }

        // ── 역할 제약 (§3.2 메아리 전용) ──────────────────────────────────

        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Seeker)]
        public void Request_ByNonEcho_IsRejected(RoleType role)
        {
            var d = new ServerKnockDriver();

            Assert.AreEqual(KnockRequestResult.NotEcho, d.TryRequest(Echo, role, Point, 0f));
            Assert.AreEqual(0, d.PendingCount, "거부된 요청이 대기열에 남으면 안 된다.");
        }

        [Test]
        public void Request_ByEcho_IsAccepted()
        {
            var d = new ServerKnockDriver();

            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Echo, RoleType.Echo, Point, 0f));
            Assert.AreEqual(1, d.PendingCount);
        }

        // ── §3.2 쿨다운 30초 ─────────────────────────────────────────────

        [Test]
        public void Request_WithinCooldown_IsRejected()
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(KnockRequestResult.OnCooldown,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.CooldownSeconds - 0.01f));
        }

        [Test]
        public void Request_AfterCooldown_IsAccepted()
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.CooldownSeconds));
        }

        [Test]
        public void Cooldown_IsPerPlayer()
        {
            // 메아리가 여럿이면 서로의 쿨다운을 공유하지 않는다.
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Other, RoleType.Echo, Point, 0f));
        }

        [Test]
        public void CooldownRemaining_CountsDown()
        {
            var d = new ServerKnockDriver();
            Assert.AreEqual(0f, d.CooldownRemaining(Echo, 0f), 0.001f);

            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(30f, d.CooldownRemaining(Echo, 0f), 0.001f);
            Assert.AreEqual(10f, d.CooldownRemaining(Echo, 20f), 0.001f);
            Assert.AreEqual(0f, d.CooldownRemaining(Echo, 40f), 0.001f);
        }

        // ── §3.2 1.5초 지연 ──────────────────────────────────────────────

        [Test]
        public void Tick_BeforeDelay_DoesNotActivate()
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(0, d.Tick(KnockConfig.ActivationDelaySeconds - 0.01f).Count,
                "1.5초 전에 소음이 나면 §3.2 지연이 무의미해진다.");
            Assert.AreEqual(1, d.PendingCount);
        }

        [Test]
        public void Tick_AfterDelay_ActivatesAtRequestedPoint()
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            List<KnockActivation> fired = d.Tick(KnockConfig.ActivationDelaySeconds);

            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(Echo, fired[0].PlayerId);
            Assert.AreEqual(Point, fired[0].Point);
            Assert.AreEqual(0, d.PendingCount);
        }

        [Test]
        public void Tick_ActivatesOnlyOnce()
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            d.Tick(KnockConfig.ActivationDelaySeconds);

            Assert.AreEqual(0, d.Tick(KnockConfig.ActivationDelaySeconds + 1f).Count);
        }

        // ── §8 유인 판정 (GAP-59) ────────────────────────────────────────

        private static ServerKnockDriver ActivatedDriver(out float activatedAt)
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            activatedAt = KnockConfig.ActivationDelaySeconds;
            d.Tick(activatedAt);
            return d;
        }

        [Test]
        public void Lure_SeekerApproachesFromOutside_IsCounted()
        {
            ServerKnockDriver d = ActivatedDriver(out float t);

            // 발생 시점: 반경 밖(멀리) → 감시 무장
            Vector3 far = Point + new Vector3(30f, 0f, 0f);
            Assert.AreEqual(0, d.ResolveLures(far, t).Count);

            // 그 뒤 반경 안으로 진입 → 유인 성공
            Vector3 near = Point + new Vector3(ServerKnockDriver.LureRadiusMeters - 1f, 0f, 0f);
            List<ulong> lures = d.ResolveLures(near, t + 5f);

            Assert.AreEqual(1, lures.Count);
            Assert.AreEqual(Echo, lures[0]);
        }

        [Test]
        public void Lure_SeekerAlreadyInside_IsNotCounted()
        {
            // "유인"은 이동을 만들어낸 경우다 — 이미 그 자리에 있던 술래는 세지 않는다.
            ServerKnockDriver d = ActivatedDriver(out float t);

            Vector3 inside = Point + new Vector3(1f, 0f, 0f);
            Assert.AreEqual(0, d.ResolveLures(inside, t).Count);
            Assert.AreEqual(0, d.WatchCount, "무장되지 않은 감시는 즉시 버려야 한다.");

            // 나갔다 다시 들어와도 그 노크로는 집계되지 않는다.
            Assert.AreEqual(0, d.ResolveLures(Point + new Vector3(30f, 0f, 0f), t + 1f).Count);
            Assert.AreEqual(0, d.ResolveLures(inside, t + 2f).Count);
        }

        [Test]
        public void Lure_AfterWindow_IsNotCounted()
        {
            ServerKnockDriver d = ActivatedDriver(out float t);

            Vector3 far = Point + new Vector3(30f, 0f, 0f);
            d.ResolveLures(far, t);

            // 창(= §3.2 쿨다운 30초)이 지난 뒤 도착 → 유인 아님
            Assert.AreEqual(0, d.ResolveLures(Point, t + ServerKnockDriver.LureWindowSeconds + 0.01f).Count);
            Assert.AreEqual(0, d.WatchCount, "만료된 감시는 정리돼야 한다.");
        }

        [Test]
        public void Lure_CountedOncePerKnock()
        {
            ServerKnockDriver d = ActivatedDriver(out float t);

            d.ResolveLures(Point + new Vector3(30f, 0f, 0f), t);
            Assert.AreEqual(1, d.ResolveLures(Point, t + 1f).Count);

            // 술래가 계속 그 자리에 있어도 같은 노크로 또 받지 않는다.
            Assert.AreEqual(0, d.ResolveLures(Point, t + 2f).Count);
        }

        [Test]
        public void Lure_BoundaryIsInclusive()
        {
            // 정확히 반경 위는 "들을 수 있는 거리"로 본다(§5.6 판정과 같은 경계 규칙).
            ServerKnockDriver d = ActivatedDriver(out float t);

            d.ResolveLures(Point + new Vector3(30f, 0f, 0f), t);
            Vector3 onEdge = Point + new Vector3(ServerKnockDriver.LureRadiusMeters, 0f, 0f);

            Assert.AreEqual(1, d.ResolveLures(onEdge, t + 1f).Count);
        }

        [Test]
        public void LureRules_AnchorToDesignDocNumbers()
        {
            // GAP-59: §3.2에 유인 정의가 없어 이 프로젝트가 정한 해석이다.
            // 임의 상수를 만들지 않고 §3.2의 두 수치(파문 반경·쿨다운)에 묶었다는 것을 고정한다.
            Assert.AreEqual(KnockConfig.RadiusMeters, ServerKnockDriver.LureRadiusMeters);
            Assert.AreEqual(KnockConfig.CooldownSeconds, ServerKnockDriver.LureWindowSeconds);
        }

        // ── 라운드 초기화 ────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsCooldownPendingAndWatches()
        {
            var d = new ServerKnockDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            d.Tick(KnockConfig.ActivationDelaySeconds);
            d.ResolveLures(Point + new Vector3(30f, 0f, 0f), KnockConfig.ActivationDelaySeconds);
            d.TryRequest(Other, RoleType.Echo, Point, 0f);

            d.Reset();

            Assert.AreEqual(0, d.PendingCount);
            Assert.AreEqual(0, d.WatchCount);
            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Echo, RoleType.Echo, Point, 0f),
                "새 라운드에서는 쿨다운이 남아 있으면 안 된다.");
        }
    }
}
