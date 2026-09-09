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
    /// 핵심 목적은 **§3.2 원문 수치(쿨다운 25초·라운드당 5회·전환 후 20초 잠금·지연 1.5초·
    /// 반경 9m·지속 1.2초)를 코드가 그대로 재현하는지**와, 클라이언트가 정할 수 없는 것
    /// (역할·횟수·잠금·쿨다운·지연·발생 위치)을 서버가 강제하는지다.
    /// </summary>
    public class ServerKnockDriverTests
    {
        private const ulong Echo = 7;
        private const ulong Other = 8;

        /// <summary>서버가 관측한 메아리의 현재 위치(§3.2 — 클라이언트가 고른 지점이 아니다).</summary>
        private static readonly Vector3 Point = new Vector3(10f, 0f, 10f);

        /// <summary>
        /// §3.2 전환 후 20초 잠금이 **이미 풀린** 드라이버. 잠금 자체를 검증하는 테스트가 아니면
        /// 전부 이걸 쓴다 — 잠금은 별도 절에서 따로 고정한다.
        /// </summary>
        private static ServerKnockDriver UnlockedDriver()
        {
            var d = new ServerKnockDriver();
            d.ObserveEcho(Echo, -KnockConfig.EchoLockoutSeconds);
            d.ObserveEcho(Other, -KnockConfig.EchoLockoutSeconds);
            return d;
        }

        // ── §3.2 상수 ────────────────────────────────────────────────────

        [Test]
        public void Config_MatchesDesignDoc()
        {
            // §3.2: "쿨다운 25초"(갱신 30 → 25), "라운드당 정확히 5회(재충전 없음)",
            //       "메아리 전환 후 20초간 사용 불가", "1.5초 지연", "반경 9m, 지속 1.2초"
            Assert.AreEqual(25f, KnockConfig.CooldownSeconds);
            Assert.AreEqual(5, KnockConfig.MaxUsesPerRound);
            Assert.AreEqual(20f, KnockConfig.EchoLockoutSeconds);
            Assert.AreEqual(1.5f, KnockConfig.ActivationDelaySeconds);
            Assert.AreEqual(9f, KnockConfig.RadiusMeters);
            Assert.AreEqual(1.2f, KnockConfig.DurationSeconds);
        }

        // ── 역할 제약 (§3.2 메아리 전용) ──────────────────────────────────

        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Seeker)]
        public void Request_ByNonEcho_IsRejected(RoleType role)
        {
            ServerKnockDriver d = UnlockedDriver();

            Assert.AreEqual(KnockRequestResult.NotEcho, d.TryRequest(Echo, role, Point, 0f));
            Assert.AreEqual(0, d.PendingCount, "거부된 요청이 대기열에 남으면 안 된다.");
        }

        [Test]
        public void Request_ByEcho_IsAccepted()
        {
            ServerKnockDriver d = UnlockedDriver();

            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Echo, RoleType.Echo, Point, 0f));
            Assert.AreEqual(1, d.PendingCount);
        }

        [Test]
        public void Request_ByNonEcho_DoesNotConsumeUses()
        {
            // 거부된 요청은 §3.2의 5회를 깎지 않는다.
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Runner, Point, 0f);

            Assert.AreEqual(KnockConfig.MaxUsesPerRound, d.UsesRemaining(Echo));
        }

        // ── §3.2 전환 후 20초 잠금 ───────────────────────────────────────

        [Test]
        public void Request_BeforeLockoutElapses_IsRejected()
        {
            var d = new ServerKnockDriver();
            d.ObserveEcho(Echo, 0f); // 이 순간 태그당해 메아리가 됐다.

            Assert.AreEqual(KnockRequestResult.Locked,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.EchoLockoutSeconds - 0.01f));
            Assert.AreEqual(0, d.PendingCount);
        }

        [Test]
        public void Request_AfterLockoutElapses_IsAccepted()
        {
            var d = new ServerKnockDriver();
            d.ObserveEcho(Echo, 0f);

            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.EchoLockoutSeconds));
        }

        [Test]
        public void Request_LockedByLockout_DoesNotConsumeUses()
        {
            var d = new ServerKnockDriver();
            d.ObserveEcho(Echo, 0f);
            d.TryRequest(Echo, RoleType.Echo, Point, 1f);

            Assert.AreEqual(KnockConfig.MaxUsesPerRound, d.UsesRemaining(Echo));
        }

        [Test]
        public void ObserveEcho_KeepsFirstObservationOnly()
        {
            // 매 틱 호출해도 잠금이 계속 뒤로 밀리면 안 된다(Net 계층은 매 프레임 부른다).
            var d = new ServerKnockDriver();
            d.ObserveEcho(Echo, 0f);
            for (float t = 0f; t < KnockConfig.EchoLockoutSeconds; t += 1f)
                d.ObserveEcho(Echo, t);

            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.EchoLockoutSeconds));
        }

        [Test]
        public void Request_WithoutObservation_IsLockedAndStartsTheClock()
        {
            // 관측이 없는 상태에서 요청이 먼저 오면(관측 틱보다 RPC가 빠른 경우) 통과시키지 않는다.
            var d = new ServerKnockDriver();

            Assert.AreEqual(KnockRequestResult.Locked, d.TryRequest(Echo, RoleType.Echo, Point, 100f));

            // 그 요청 시점이 기준이 돼 20초 뒤에 풀린다.
            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, 100f + KnockConfig.EchoLockoutSeconds));
        }

        [Test]
        public void LockoutRemaining_CountsDown()
        {
            var d = new ServerKnockDriver();
            Assert.AreEqual(KnockConfig.EchoLockoutSeconds, d.LockoutRemaining(Echo, 0f), 0.001f,
                "관측 전에는 잠금이 전부 남은 것으로 봐야 한다.");

            d.ObserveEcho(Echo, 0f);

            Assert.AreEqual(20f, d.LockoutRemaining(Echo, 0f), 0.001f);
            Assert.AreEqual(5f, d.LockoutRemaining(Echo, 15f), 0.001f);
            Assert.AreEqual(0f, d.LockoutRemaining(Echo, 30f), 0.001f);
        }

        [Test]
        public void Lockout_IsPerPlayer()
        {
            // 먼저 태그당한 메아리의 잠금이 나중에 태그당한 메아리에게 옮겨붙으면 안 된다.
            var d = new ServerKnockDriver();
            d.ObserveEcho(Echo, 0f);
            d.ObserveEcho(Other, 100f);

            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Echo, RoleType.Echo, Point, 100f));
            Assert.AreEqual(KnockRequestResult.Locked, d.TryRequest(Other, RoleType.Echo, Point, 100f));
        }

        // ── §3.2 라운드당 5회 하드캡 ─────────────────────────────────────

        /// <summary>쿨다운을 정확히 지켜 5회를 소진한다. 마지막 요청 시각을 돌려준다.</summary>
        private static float ExhaustUses(ServerKnockDriver d)
        {
            float t = 0f;
            for (int i = 0; i < KnockConfig.MaxUsesPerRound; i++)
            {
                t = i * KnockConfig.CooldownSeconds;
                Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Echo, RoleType.Echo, Point, t),
                    $"{i + 1}번째 노크는 §3.2 5회 안이므로 통과해야 한다.");
            }

            return t;
        }

        [Test]
        public void Request_FiveTimes_AllAccepted()
        {
            ServerKnockDriver d = UnlockedDriver();
            ExhaustUses(d);

            Assert.AreEqual(0, d.UsesRemaining(Echo));
        }

        [Test]
        public void Request_SixthTime_IsRejectedEvenAfterCooldown()
        {
            // §3.2 "재충전 없음" — 쿨다운을 아무리 기다려도 6번째는 없다.
            ServerKnockDriver d = UnlockedDriver();
            float last = ExhaustUses(d);

            Assert.AreEqual(KnockRequestResult.NoUsesLeft,
                d.TryRequest(Echo, RoleType.Echo, Point, last + KnockConfig.CooldownSeconds));
            Assert.AreEqual(KnockRequestResult.NoUsesLeft,
                d.TryRequest(Echo, RoleType.Echo, Point, last + 10000f));
        }

        [Test]
        public void NoUsesLeft_TakesPrecedenceOverCooldown()
        {
            // 5회를 다 쓴 사람에게 "쿨다운 n초"라고 알리면 기다리면 된다는 잘못된 기대를 준다.
            ServerKnockDriver d = UnlockedDriver();
            float last = ExhaustUses(d);

            Assert.AreEqual(KnockRequestResult.NoUsesLeft,
                d.TryRequest(Echo, RoleType.Echo, Point, last + 0.5f));
        }

        [Test]
        public void UsesRemaining_CountsDown()
        {
            ServerKnockDriver d = UnlockedDriver();
            Assert.AreEqual(KnockConfig.MaxUsesPerRound, d.UsesRemaining(Echo));

            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            Assert.AreEqual(KnockConfig.MaxUsesPerRound - 1, d.UsesRemaining(Echo));

            d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.CooldownSeconds);
            Assert.AreEqual(KnockConfig.MaxUsesPerRound - 2, d.UsesRemaining(Echo));
        }

        [Test]
        public void Uses_ArePerPlayer()
        {
            ServerKnockDriver d = UnlockedDriver();
            ExhaustUses(d);

            Assert.AreEqual(KnockRequestResult.NoUsesLeft, d.TryRequest(Echo, RoleType.Echo, Point, 200f));
            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Other, RoleType.Echo, Point, 200f),
                "한 메아리가 소진해도 다른 메아리의 5회는 그대로다.");
        }

        [Test]
        public void RejectedRequest_DoesNotConsumeUses()
        {
            // 쿨다운으로 거부된 요청이 횟수를 깎으면, 연타만으로 5회가 증발한다.
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            for (int i = 0; i < 10; i++)
                Assert.AreEqual(KnockRequestResult.OnCooldown, d.TryRequest(Echo, RoleType.Echo, Point, 1f + i));

            Assert.AreEqual(KnockConfig.MaxUsesPerRound - 1, d.UsesRemaining(Echo));
        }

        // ── §3.2 쿨다운 25초 ─────────────────────────────────────────────

        [Test]
        public void Request_WithinCooldown_IsRejected()
        {
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(KnockRequestResult.OnCooldown,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.CooldownSeconds - 0.01f));
        }

        [Test]
        public void Request_AfterCooldown_IsAccepted()
        {
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.CooldownSeconds));
        }

        [Test]
        public void Cooldown_IsPerPlayer()
        {
            // 메아리가 여럿이면 서로의 쿨다운을 공유하지 않는다.
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Other, RoleType.Echo, Point, 0f));
        }

        [Test]
        public void CooldownRemaining_CountsDown()
        {
            ServerKnockDriver d = UnlockedDriver();
            Assert.AreEqual(0f, d.CooldownRemaining(Echo, 0f), 0.001f);

            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(25f, d.CooldownRemaining(Echo, 0f), 0.001f);
            Assert.AreEqual(5f, d.CooldownRemaining(Echo, 20f), 0.001f);
            Assert.AreEqual(0f, d.CooldownRemaining(Echo, 40f), 0.001f);
        }

        // ── §3.2 1.5초 지연 ──────────────────────────────────────────────

        [Test]
        public void Tick_BeforeDelay_DoesNotActivate()
        {
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);

            Assert.AreEqual(0, d.Tick(KnockConfig.ActivationDelaySeconds - 0.01f).Count,
                "1.5초 전에 소음이 나면 §3.2 지연이 무의미해진다.");
            Assert.AreEqual(1, d.PendingCount);
        }

        [Test]
        public void Tick_AfterDelay_ActivatesAtRequestTimePosition()
        {
            // GAP-66: 발생 지점은 **요청 시점**의 메아리 위치로 고정된다.
            // 지연 중 메아리가 이동해도 소리는 누른 자리에서 난다(그 자리에 유인하는 것이 §3.2의 목적).
            ServerKnockDriver d = UnlockedDriver();
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
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            d.Tick(KnockConfig.ActivationDelaySeconds);

            Assert.AreEqual(0, d.Tick(KnockConfig.ActivationDelaySeconds + 1f).Count);
        }

        // ── §8.3 최고의 거짓말상 — 4조건 인과관계 판정 ───────────────────

        private const float Sample = ServerKnockDriver.ApproachSampleSeconds;   // 0.5초

        /// <summary>노크를 발동시켜 감시를 만든다. 발동 시각(t0)을 돌려준다.</summary>
        private static ServerKnockDriver ActivatedDriver(out float t0)
        {
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            t0 = KnockConfig.ActivationDelaySeconds;
            d.Tick(t0);
            return d;
        }

        private static Vector3 At(float distance) => Point + new Vector3(distance, 0f, 0f);

        /// <summary>
        /// 술래를 <paramref name="from"/>m에서 <paramref name="to"/>m로 0.5초 샘플마다
        /// 일정하게 접근시킨다. 마지막 호출의 유인 결과 수를 돌려준다.
        /// </summary>
        private static int Approach(ServerKnockDriver d, ref float now, float from, float to, int samples)
        {
            int lures = 0;
            for (int i = 1; i <= samples; i++)
            {
                now += Sample;
                float distance = from + (to - from) * i / samples;
                lures = d.ResolveLures(At(distance), now).Count;
                if (lures > 0)
                    break;
            }

            return lures;
        }

        [Test]
        public void Rules_AnchorToDesignDocNumbers()
        {
            // §8.3 상수표. 진입 거리만 §3.2 파문 반경에 묶여 있고 나머지는 §8.3 고유값이다.
            Assert.AreEqual(KnockConfig.RadiusMeters, ServerKnockDriver.LureRadiusMeters);
            Assert.AreEqual(9f, ServerKnockDriver.LureRadiusMeters);
            Assert.AreEqual(30f, ServerKnockDriver.LureWindowSeconds);
            Assert.AreEqual(2f, ServerKnockDriver.PreApproachWindowSeconds);
            Assert.AreEqual(0.5f, ServerKnockDriver.ApproachSampleSeconds);
            Assert.AreEqual(3f, ServerKnockDriver.ApproachRequiredSeconds);
        }

        [Test]
        public void WatchWindow_IsLongerThanCooldown()
        {
            // 감시 30초 > 쿨다운 25초 — 같은 메아리의 감시가 겹칠 수 있다는 사실 자체를 고정한다.
            Assert.Greater(ServerKnockDriver.LureWindowSeconds, KnockConfig.CooldownSeconds);
        }

        // ── ① d(t0) > 9m ────────────────────────────────────────────────

        [Test]
        public void Condition1_SeekerAlreadyInside_WatchIsNotStarted()
        {
            // §8.3 ① "d(t0) > 9m — 아니면 감시를 시작하지 않는다".
            ServerKnockDriver d = ActivatedDriver(out float t0);

            Assert.AreEqual(0, d.ResolveLures(At(1f), t0).Count);
            Assert.AreEqual(0, d.WatchCount, "개시 조건 불만족 감시는 즉시 버려야 한다.");
        }

        [Test]
        public void Condition1_ExactlyAtRadius_IsNotStarted()
        {
            // 경계: d(t0) = 9m는 "> 9m"이 아니므로 감시가 시작되지 않는다.
            ServerKnockDriver d = ActivatedDriver(out float t0);

            d.ResolveLures(At(ServerKnockDriver.LureRadiusMeters), t0);
            Assert.AreEqual(0, d.WatchCount);
        }

        [Test]
        public void Condition1_JustOutsideRadius_StartsWatch()
        {
            ServerKnockDriver d = ActivatedDriver(out float t0);

            d.ResolveLures(At(ServerKnockDriver.LureRadiusMeters + 0.01f), t0);
            Assert.AreEqual(1, d.WatchCount);
        }

        // ── ② d(t0) >= d(t0 - 2초) ──────────────────────────────────────

        /// <summary>
        /// 노크 **전에** 술래 위치 이력을 쌓는다(실기에서는 서버가 매 프레임 ResolveLures를
        /// 부르므로 노크 시점에 이미 2초 이력이 있다). 마지막 샘플 시각을 돌려준다.
        /// </summary>
        private static float SeedHistory(ServerKnockDriver d, params float[] distances)
        {
            float now = 0f;
            for (int i = 0; i < distances.Length; i++)
            {
                d.ResolveLures(At(distances[i]), now);
                if (i < distances.Length - 1)
                    now += Sample;
            }

            return now;
        }

        /// <summary>이력을 쌓아 둔 드라이버에서 노크를 발동시킨다.</summary>
        private static float FireAfterHistory(ServerKnockDriver d, float now)
        {
            d.TryRequest(Echo, RoleType.Echo, Point, now);
            float fire = now + KnockConfig.ActivationDelaySeconds;
            d.Tick(fire);
            return fire;
        }

        [Test]
        public void Condition2_AlreadyApproaching_WatchIsNotStarted()
        {
            // §8.3 ② "직전 2초간 접근 중이 아니었을 것" / 표 2 "이미 그쪽으로 이동 중이었음 → 실패".
            ServerKnockDriver d = UnlockedDriver();

            // 노크 전 2초간 30m → 20m로 꾸준히 접근해 왔다.
            float now = SeedHistory(d, 30f, 27f, 24f, 22f, 20f);
            float fire = FireAfterHistory(d, now);

            d.ResolveLures(At(20f), fire); // d(t0)=20m, d(t0-2초)=22m → 20 >= 22 불성립

            Assert.AreEqual(0, d.WatchCount,
                "직전 2초간 접근 중이었다면 감시를 시작하지 않는다(§8.3 ②).");
        }

        [Test]
        public void Condition2_MovingAway_StartsWatch()
        {
            // 반대로 멀어지는 중이었다면 ②를 통과한다.
            ServerKnockDriver d = UnlockedDriver();

            float now = SeedHistory(d, 20f, 22f, 24f, 27f, 30f);
            float fire = FireAfterHistory(d, now);

            d.ResolveLures(At(30f), fire); // d(t0)=30m >= d(t0-2초)=24m

            Assert.AreEqual(1, d.WatchCount);
        }

        [Test]
        public void Condition2_StandingStill_StartsWatch()
        {
            // 제자리에 있었다면 "접근 중"이 아니므로 d(t0) >= d(t0-2)가 등호로 성립한다.
            ServerKnockDriver d = UnlockedDriver();

            float now = SeedHistory(d, 25f, 25f, 25f, 25f, 25f);
            float fire = FireAfterHistory(d, now);

            d.ResolveLures(At(25f), fire);

            Assert.AreEqual(1, d.WatchCount);
        }

        [Test]
        public void Condition2_NoHistory_FailsOpen()
        {
            // 이력이 없으면 d(t0 - 2초)를 알 수 없다. 확인 불가를 이유로 정당한 노크를
            // 탈락시키지 않는 쪽(폴백 = 현재 거리)을 택했다는 판단을 고정한다.
            ServerKnockDriver d = ActivatedDriver(out float t0);

            Assert.AreEqual(0, d.ResolveLures(At(30f), t0).Count);
            Assert.AreEqual(1, d.WatchCount);
        }

        // ── ③④ 접근누적 3.0초 ───────────────────────────────────────────

        [Test]
        public void Condition4_SustainedApproach_IsCounted()
        {
            // 30m → 5m로 8샘플(4초) 접근: 누적 4.0초 ≥ 3.0초, 마지막에 9m 안 → 성공.
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now); // 감시 개시

            Assert.AreEqual(1, Approach(d, ref now, 30f, 5f, 8));
        }

        [Test]
        public void Condition4_PassingBy_IsNotCounted()
        {
            // §8.3 표 1 "우연히 지나감 → 실패(접근누적 3.0초 미달)".
            // 30m → 5m를 4샘플(2.0초)만에 지나가면 누적 2.0 < 3.0이라 실패한다.
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now);

            Assert.AreEqual(0, Approach(d, ref now, 30f, 5f, 4));
        }

        [Test]
        public void Condition4_ExactlyThreeSeconds_IsCounted()
        {
            // 경계: 누적이 정확히 3.0초(6샘플)면 성공이다("접근누적 >= 3.0초").
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now);

            Assert.AreEqual(1, Approach(d, ref now, 30f, 5f, 6));
        }

        [Test]
        public void Condition3_OnlyDecreasingSamplesAccumulate()
        {
            // §8.3 ③ "직전 샘플보다 거리가 줄었을 때만" — 제자리에 서 있으면 누적되지 않는다.
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now);

            // 20m에서 10샘플(5초)간 정지 — 누적 0.
            for (int i = 0; i < 10; i++)
            {
                now += Sample;
                d.ResolveLures(At(20f), now);
            }

            // 그 뒤 4샘플(2.0초)만 접근해 9m 안으로 들어간다 → 누적 2.0 < 3.0이라 실패.
            Assert.AreEqual(0, Approach(d, ref now, 20f, 5f, 4));
        }

        [Test]
        public void Table3_AwayThenReturn_IsCounted()
        {
            // §8.3 표 3 "Knock 직후 반대로 갔다가 복귀 → 성공(복귀 구간에서 조건 충족 시)".
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(20f), now);

            // 멀어진다(누적 0)
            for (int i = 1; i <= 4; i++)
            {
                now += Sample;
                d.ResolveLures(At(20f + i * 2f), now);
            }

            // 복귀하며 6샘플(3.0초) 접근 → 성공
            Assert.AreEqual(1, Approach(d, ref now, 28f, 5f, 6));
        }

        [Test]
        public void InsideRadiusWithoutEnoughApproach_KeepsWatching()
        {
            // §8.3 종료 조건은 30초·태그·라운드 종료뿐이다 — 반경 안인데 누적이 모자라도
            // 감시를 끝내지 않는다(다시 다가오면 그때 성립할 수 있어야 한다).
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now);

            Assert.AreEqual(0, Approach(d, ref now, 30f, 5f, 4));
            Assert.AreEqual(1, d.WatchCount, "누적 미달로 감시가 사라지면 표 3이 성립하지 않는다.");
        }

        // ── 감시 종료 조건 ───────────────────────────────────────────────

        [Test]
        public void Watch_ExpiresAfterThirtySeconds()
        {
            ServerKnockDriver d = ActivatedDriver(out float t0);
            d.ResolveLures(At(30f), t0);

            Assert.AreEqual(0, d.ResolveLures(At(1f), t0 + ServerKnockDriver.LureWindowSeconds).Count);
            Assert.AreEqual(0, d.WatchCount, "만료된 감시는 정리돼야 한다.");
        }

        [Test]
        public void Watch_EndsWhenSeekerTags()
        {
            // §8.3 "[감시 종료 — 전부 실패] … 술래가 도망자를 태그".
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now);
            Assert.AreEqual(1, d.WatchCount);

            d.NotifySeekerTagged();

            Assert.AreEqual(0, d.WatchCount);
            Assert.AreEqual(0, Approach(d, ref now, 30f, 5f, 8), "태그 이후에는 유인이 성립하지 않는다.");
        }

        [Test]
        public void Watch_EndsOnRoundReset()
        {
            ServerKnockDriver d = ActivatedDriver(out float now);
            d.ResolveLures(At(30f), now);

            d.Reset();

            Assert.AreEqual(0, d.WatchCount);
        }

        // ── §8.3 표 4·5 — 감시 겹침(30초 > 25초) ────────────────────────

        [Test]
        public void Table5_NewKnockReplacesOwnActiveWatch()
        {
            // 감시 30초 > 쿨다운 25초라 겹칠 수 있다. 새 노크가 나가면 이전 감시를 버린다.
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            float first = KnockConfig.ActivationDelaySeconds;
            d.Tick(first);
            d.ResolveLures(At(30f), first);
            Assert.AreEqual(1, d.WatchCount);

            // 25초 뒤 두 번째 노크(쿨다운 만료) — 첫 감시는 아직 30초가 안 지났다.
            float secondRequest = KnockConfig.CooldownSeconds;
            Assert.AreEqual(KnockRequestResult.Accepted, d.TryRequest(Echo, RoleType.Echo, Point, secondRequest));
            float second = secondRequest + KnockConfig.ActivationDelaySeconds;
            Assert.Less(second, first + ServerKnockDriver.LureWindowSeconds, "두 감시가 겹치는 구간이어야 한다.");

            d.Tick(second);

            Assert.AreEqual(1, d.WatchCount, "§8.3 표 5 — 활성 감시는 최신 1개로 갱신된다.");
        }

        [Test]
        public void Table4_OverlappingKnocks_CountLureOnlyOnce()
        {
            // 겹친 감시를 그대로 두면 술래의 한 번의 접근이 2회로 세어진다.
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            float first = KnockConfig.ActivationDelaySeconds;
            d.Tick(first);
            d.ResolveLures(At(30f), first);

            float secondRequest = KnockConfig.CooldownSeconds;
            d.TryRequest(Echo, RoleType.Echo, Point, secondRequest);
            float now = secondRequest + KnockConfig.ActivationDelaySeconds;
            d.Tick(now);
            d.ResolveLures(At(30f), now); // 새 감시 개시

            Assert.AreEqual(1, Approach(d, ref now, 30f, 5f, 8), "유인은 1회만 집계돼야 한다.");
        }

        [Test]
        public void OtherEchoWatch_IsNotDropped()
        {
            // 최신 1개 갱신은 **같은 플레이어**에 한한다 — 다른 메아리의 감시를 건드리면 안 된다.
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            d.TryRequest(Other, RoleType.Echo, Point, 0f);
            float t0 = KnockConfig.ActivationDelaySeconds;
            d.Tick(t0);
            d.ResolveLures(At(30f), t0);

            Assert.AreEqual(2, d.WatchCount);
        }

        // ── 라운드 초기화 ────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsCooldownPendingAndWatches()
        {
            ServerKnockDriver d = UnlockedDriver();
            d.TryRequest(Echo, RoleType.Echo, Point, 0f);
            d.Tick(KnockConfig.ActivationDelaySeconds);
            d.ResolveLures(Point + new Vector3(30f, 0f, 0f), KnockConfig.ActivationDelaySeconds);
            d.TryRequest(Other, RoleType.Echo, Point, 0f);

            d.Reset();
            d.ObserveEcho(Echo, 0f); // 새 라운드에서 다시 메아리로 관측된다.

            Assert.AreEqual(0, d.PendingCount);
            Assert.AreEqual(0, d.WatchCount);
            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.EchoLockoutSeconds),
                "새 라운드에서는 쿨다운이 남아 있으면 안 된다.");
        }

        [Test]
        public void Reset_RestoresRoundUses()
        {
            // §3.2 "재충전 없음"은 라운드 안에서의 이야기다 — 라운드 경계에서는 5회가 돌아온다.
            ServerKnockDriver d = UnlockedDriver();
            ExhaustUses(d);
            Assert.AreEqual(0, d.UsesRemaining(Echo));

            d.Reset();

            Assert.AreEqual(KnockConfig.MaxUsesPerRound, d.UsesRemaining(Echo));
            d.ObserveEcho(Echo, 0f);
            Assert.AreEqual(KnockRequestResult.Accepted,
                d.TryRequest(Echo, RoleType.Echo, Point, KnockConfig.EchoLockoutSeconds));
        }

        [Test]
        public void Reset_RestartsLockout()
        {
            // 새 라운드에서 다시 메아리가 되면 20초를 처음부터 센다(이전 라운드의 관측을 물려받지 않는다).
            ServerKnockDriver d = UnlockedDriver();
            d.Reset();

            Assert.AreEqual(KnockRequestResult.Locked, d.TryRequest(Echo, RoleType.Echo, Point, 1000f));
        }
    }
}
