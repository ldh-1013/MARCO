using System.Collections.Generic;
using Marco.Core.Items;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Sound;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [블록 6] §3.2-1 메아리 소나 · §16.4 잔상 · §7 찰칵이.
    /// </summary>
    public class SonarAfterglowClickerTests
    {
        private const float Eps = 1e-4f;

        private sealed class FixedProbe : IOcclusionProbe
        {
            private readonly OcclusionResult _result;
            public int Calls;
            public FixedProbe(bool hard, int walls) => _result = new OcclusionResult(hard, walls);
            public OcclusionResult Probe(Vector3 from, Vector3 to) { Calls++; return _result; }
        }

        private static SoundPulse Talk(Vector3 at, ulong source = 1, float t = 0f) =>
            new SoundPulse(source, at, 9f, 1.2f, SoundType.Talk, t);

        // ── 6-A 메아리 소나 — 서버 판정 ─────────────────────────────────

        [Test]
        public void Echo_SeesPulse_RegardlessOfDistance()
        {
            var probe = new FixedProbe(false, 0);
            PerceivedPulse? p = SoundPulseResolver.Resolve(Talk(Vector3.zero), 9, new Vector3(100f, 0f, 0f), RoleType.Echo, probe);

            Assert.IsTrue(p.HasValue, "§3.2-1 거리 무시");
            Assert.AreEqual(9f, p.Value.PerceivedRadius, Eps, "원본 반경 — 역할 배율 없음");
            Assert.AreEqual(1.2f, p.Value.PerceivedDuration, Eps, "§3.2-1 표시 시간 = 본래 지속");
            Assert.IsTrue(p.Value.WorldSpaceRingVisible);
            Assert.AreEqual(Vector3.zero, p.Value.SourcePos.Value);
            Assert.AreEqual(0, probe.Calls, "차폐 레이도 쏘지 않는다");
        }

        [Test]
        public void Echo_SeesPulse_ThroughHardBlocker()
        {
            PerceivedPulse? p = SoundPulseResolver.Resolve(Talk(Vector3.zero), 9, new Vector3(3f, 0f, 0f),
                RoleType.Echo, new FixedProbe(true, 5));
            Assert.IsTrue(p.HasValue, "§3.2-1 차폐 무시");
        }

        [Test]
        public void NonEcho_Unchanged_OutOfRangeIsNull()
        {
            var probe = new FixedProbe(false, 0);
            Assert.IsNull(SoundPulseResolver.Resolve(Talk(Vector3.zero), 9, new Vector3(100f, 0f, 0f), RoleType.Runner, probe));
            Assert.IsNull(SoundPulseResolver.Resolve(Talk(Vector3.zero), 9, new Vector3(100f, 0f, 0f), RoleType.Seeker, probe));
        }

        [Test]
        public void Echo_StillExcludesOwnPulse_Gap1()
        {
            Assert.IsNull(SoundPulseResolver.Resolve(Talk(Vector3.zero, source: 9), 9, Vector3.zero, RoleType.Echo,
                new FixedProbe(false, 0)));
        }

        [Test]
        public void Echo_ThroughTracker_OneAppeared_NoGauge()
        {
            var tracker = new ActivePulseTracker();
            tracker.AddPulse(new SoundPulse(1, Vector3.zero, 22f, 2.5f, SoundType.Shout, 0f));
            var echo = new ListenerSnapshot(9, new Vector3(80f, 0f, 0f), RoleType.Echo);

            List<PulseDelivery> first = tracker.Tick(0f, new[] { echo }, new FixedProbe(true, 3));
            Assert.AreEqual(1, first.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, first[0].Kind);
            Assert.IsFalse(first[0].GaugeLit, "§3.4 게이지는 술래 전용");

            Assert.AreEqual(0, tracker.Tick(0.3f, new[] { echo }, new FixedProbe(true, 3)).Count, "결과가 변하지 않아 재전송 없음");
        }

        // ── 6-A 메아리 소나 — 정보량 제한 ───────────────────────────────

        private static PulseVisualState Ring(int id, float start, float duration = 1f) =>
            new PulseVisualState(id, PulseVisualKind.WorldRing, new Vector3(id, 0f, 0f), DirectionOctant.N, 9f, duration, start);

        [Test]
        public void SonarConstants_MatchDesignDoc()
        {
            Assert.AreEqual(3f, EchoSonarConfig.ResidualSeconds, Eps);
            Assert.AreEqual(8, EchoSonarConfig.MaxVisible);
        }

        [Test]
        public void Sonar_KeepsNewestEight_DropsOldest()
        {
            var view = new EchoSonarView();
            var active = new List<PulseVisualState>();
            for (int i = 0; i < 12; i++)
                active.Add(Ring(i, start: i * 0.1f, duration: 10f));

            var output = new List<EchoSonarView.Item>();
            view.Select(active, 1.5f, output);

            Assert.AreEqual(8, output.Count);
            Assert.AreEqual(11, output[0].PulseId, "최신이 먼저");
            Assert.AreEqual(4, output[7].PulseId, "0~3(가장 오래된 4개)은 버려진다");
        }

        [Test]
        public void Sonar_Residual_FadesOverThreeSeconds()
        {
            var view = new EchoSonarView();
            view.NotifyExpired(Ring(1, start: 0f), now: 1f);

            var output = new List<EchoSonarView.Item>();
            view.Select(new List<PulseVisualState>(), 1f, output);
            Assert.AreEqual(1f, output[0].Alpha, Eps, "소멸 순간 1.0");
            Assert.IsTrue(output[0].Residual);

            view.Select(new List<PulseVisualState>(), 2.5f, output);
            Assert.AreEqual(0.5f, output[0].Alpha, Eps, "1.5초 — 절반");

            view.Tick(3.99f);
            view.Select(new List<PulseVisualState>(), 3.99f, output);
            Assert.AreEqual(1, output.Count, "2.99초 — 아직 남아 있다");

            view.Tick(4f);
            view.Select(new List<PulseVisualState>(), 4f, output);
            Assert.AreEqual(0, output.Count, "3초 — 사라짐");
        }

        [Test]
        public void Sonar_ResidualsCountTowardCap_ByPulseTime()
        {
            var view = new EchoSonarView();
            for (int i = 0; i < 5; i++)
                view.NotifyExpired(Ring(100 + i, start: i), now: 10f);

            var active = new List<PulseVisualState>();
            for (int i = 0; i < 5; i++)
                active.Add(Ring(i, start: 5f + i, duration: 10f));

            var output = new List<EchoSonarView.Item>();
            view.Select(active, 10.5f, output);
            Assert.AreEqual(8, output.Count);
            Assert.IsFalse(output[0].Residual, "살아 있는 최신 파문이 먼저");
            Assert.AreEqual(102, output[7].PulseId, "잔류도 발생 시각 순으로 줄을 선다 — 100·101이 밀려난다");
        }

        [Test]
        public void Sonar_IgnoresDirectionOnly()
        {
            var view = new EchoSonarView();
            var d = new PulseVisualState(1, PulseVisualKind.DirectionOnly, Vector3.zero, DirectionOctant.E, 9f, 1f, 0f);
            view.NotifyExpired(d, 1f);
            Assert.AreEqual(0, view.ResidualCount);
        }

        // ── 6-B 잔상 ─────────────────────────────────────────────────────

        [Test]
        public void Afterglow_UsesFallback_8PercentFor8Seconds()
        {
            Assert.AreEqual(0.15f, AfterglowConfig.EdgeBrightness, Eps, "§16.4 본안(미사용)");
            Assert.AreEqual(12f, AfterglowConfig.EdgeSeconds, Eps);
            Assert.AreEqual(0.08f, AfterglowConfig.Brightness, Eps, "§16.4 차선책 — 사용자 확정");
            Assert.AreEqual(8f, AfterglowConfig.Seconds, Eps);
        }

        [TestCase(0f, 0.08f)]
        [TestCase(4f, 0.04f)]
        [TestCase(7.99f, 0.0001f)]
        [TestCase(8f, 0f)]
        [TestCase(20f, 0f)]
        public void Afterglow_LinearDecay(float age, float alpha)
        {
            Assert.AreEqual(alpha, AfterglowConfig.AlphaAt(age), 2e-4f);
        }

        [Test]
        public void AfterglowTracker_RemovesAtEightSeconds()
        {
            var t = new AfterglowTracker();
            var removed = new List<int>();
            t.Removed += removed.Add;

            int id = t.Add(Vector3.zero, 6f, 0f);
            t.Tick(7.99f);
            Assert.AreEqual(1, t.Count);
            t.Tick(8f);
            Assert.AreEqual(0, t.Count);
            CollectionAssert.AreEqual(new[] { id }, removed);
        }

        [Test]
        public void Registry_VisualExpired_OnlyOnNaturalExpiry()
        {
            var reg = new PulseVisualRegistry();
            var expired = new List<int>();
            reg.VisualExpired += s => expired.Add(s.PulseId);

            var pp = new PerceivedPulse(2f, 1f, Vector3.zero, DirectionOctant.N, true);
            reg.Apply(new PulseDelivery(0, 1, PulseDeliveryKind.Appeared, pp), 0f);
            reg.Apply(new PulseDelivery(0, 2, PulseDeliveryKind.Appeared, pp), 0f);
            reg.Apply(new PulseDelivery(0, 2, PulseDeliveryKind.Disappeared, null), 0.5f);

            reg.Tick(1f);
            CollectionAssert.AreEqual(new[] { 1 }, expired, "조기 소실(2)은 잔상·잔류를 남기지 않는다");
        }

        // ── 6-C 찰칵이 ───────────────────────────────────────────────────

        [Test]
        public void ClickerConstants_MatchDesignDoc()
        {
            Assert.AreEqual(6f, ClickerConfig.RadiusMeters, Eps);
            Assert.AreEqual(0.3f, ClickerConfig.FlashSeconds, Eps);
            Assert.AreEqual(120f, ClickerConfig.RespawnSeconds, Eps);
            Assert.AreEqual(4, ClickerConfig.SpawnCount);
        }

        [TestCase(RoleType.Runner, ClickerPickupResult.Accepted)]
        [TestCase(RoleType.Seeker, ClickerPickupResult.NotAllowedRole)] // GAP-98 잠정
        [TestCase(RoleType.Echo, ClickerPickupResult.NotAllowedRole)]   // GAP-5
        public void PickUp_ByRole(RoleType role, ClickerPickupResult expected)
        {
            Assert.AreEqual(expected, new ServerClickerDriver().TryPickUp(1, role, 0));
        }

        [Test]
        public void PickUp_HoldOne_UntilUsed()
        {
            // §7 "동시 소지 1개(쓰기 전에는 새로 주울 수 없음)".
            var d = new ServerClickerDriver();
            Assert.AreEqual(ClickerPickupResult.Accepted, d.TryPickUp(1, RoleType.Runner, 0));
            Assert.AreEqual(ClickerPickupResult.AlreadyHolding, d.TryPickUp(1, RoleType.Runner, 1));
            Assert.IsTrue(d.TryUse(1, RoleType.Runner), "1회용");
            Assert.IsFalse(d.TryUse(1, RoleType.Runner), "이미 소진");
            Assert.AreEqual(ClickerPickupResult.Accepted, d.TryPickUp(1, RoleType.Runner, 1));
        }

        [Test]
        public void Respawn_120Seconds_Boundary()
        {
            var d = new ServerClickerDriver();
            d.TryPickUp(1, RoleType.Runner, 2);
            Assert.IsFalse(d.IsAvailable(2));
            Assert.AreEqual(ClickerPickupResult.NotAvailable, d.TryPickUp(2, RoleType.Runner, 2));
            Assert.AreEqual(0b1011, d.AvailabilityMask);

            d.Tick(119.9f);
            Assert.IsFalse(d.IsAvailable(2), "119.9초 — 아직");
            Assert.IsTrue(d.Tick(0.1f), "120초 — 리스폰");
            Assert.IsTrue(d.IsAvailable(2));
            Assert.AreEqual(0b1111, d.AvailabilityMask);
        }

        [Test]
        public void TaggedHolder_LosesClicker()
        {
            var d = new ServerClickerDriver();
            d.TryPickUp(1, RoleType.Runner, 0);
            Assert.IsFalse(d.TryUse(1, RoleType.Echo), "메아리는 쓸 수 없다(GAP-5)");
            Assert.IsFalse(d.IsHolding(1), "소지도 사라진다");
        }

        [Test]
        public void Reset_NewRound()
        {
            var d = new ServerClickerDriver();
            d.TryPickUp(1, RoleType.Runner, 0);
            d.Reset();
            Assert.IsTrue(d.IsAvailable(0));
            Assert.IsFalse(d.IsHolding(1));
        }

        [Test]
        public void FlashRange_6Meters_Inclusive()
        {
            Assert.IsTrue(ServerClickerDriver.WithinFlashRange(Vector3.zero, new Vector3(6f, 0f, 0f)));
            Assert.IsFalse(ServerClickerDriver.WithinFlashRange(Vector3.zero, new Vector3(6.01f, 0f, 0f)));
        }

        [Test]
        public void InvalidSpawnIndex_Rejected()
        {
            var d = new ServerClickerDriver();
            Assert.AreEqual(ClickerPickupResult.InvalidSpawn, d.TryPickUp(1, RoleType.Runner, 4));
            Assert.AreEqual(ClickerPickupResult.InvalidSpawn, d.TryPickUp(1, RoleType.Runner, -1));
        }

        // ── HUD ─────────────────────────────────────────────────────────

        [Test]
        public void Hud_Stamina()
        {
            Assert.AreEqual("질주 ■■■■■□□□□□", HudFormatter.FormatStamina(0.5f, false));
            Assert.AreEqual("질주 ■■■■■■■■■□", HudFormatter.FormatStamina(0.99f, false), "내림");
            Assert.AreEqual("질주 □□□□□□□□□□ 소진", HudFormatter.FormatStamina(0f, true));
        }
    }
}
