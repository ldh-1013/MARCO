using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 14: 서버 권위 파문 구동기(<see cref="ServerPulseDriver"/>)의 계약을 고정한다.
    ///
    /// 핵심은 두 가지다:
    /// 1. **서버가 §5.1 표에서 반경·지속을 재계산한다**(클라이언트는 소리 종류만 주장 — GAP-24).
    ///    표에 없는 종류는 파문이 아예 생기지 않는다.
    /// 2. **청취자별로 다른 결과가 나온다**(§14.3 "해당 리스너만 개별 전송"의 전제) —
    ///    같은 파문 하나에서 청취자마다 좌표/방위/미전달이 갈린다.
    ///
    /// §5.6 판정식 자체는 <c>SoundPulseResolverTests</c>(19케이스)·<c>ActivePulseTrackerTests</c>
    /// (12케이스)가 이미 고정하므로, 여기서는 <b>서버 드라이버가 그 로직에 올바른 입력을 만들어
    /// 넘기고 청취자별 전송 지시를 산출하는지</b>만 본다.
    /// </summary>
    public class ServerPulseDriverTests
    {
        private const ulong SourceId = 1;
        private const ulong ListenerA = 2;
        private const ulong ListenerB = 3;

        /// <summary>차폐 없음(벽 0개) — GAP-2상 좌표가 공개되는 조건.</summary>
        private sealed class ClearProbe : IOcclusionProbe
        {
            public OcclusionResult Probe(Vector3 from, Vector3 to) => new OcclusionResult(false, 0);
        }

        /// <summary>벽 n개 — 감쇠 + 좌표 비공개(방위만).</summary>
        private sealed class WallProbe : IOcclusionProbe
        {
            private readonly int _walls;
            public WallProbe(int walls) => _walls = walls;
            public OcclusionResult Probe(Vector3 from, Vector3 to) => new OcclusionResult(false, _walls);
        }

        /// <summary>하드블로커 — 완전 차단(전달 없음).</summary>
        private sealed class BlockedProbe : IOcclusionProbe
        {
            public OcclusionResult Probe(Vector3 from, Vector3 to) => new OcclusionResult(true, 0);
        }

        private static List<ListenerSnapshot> Listeners(params ListenerSnapshot[] snapshots) =>
            new List<ListenerSnapshot>(snapshots);

        // ── §5.1 표 서버 재계산 (GAP-24) ─────────────────────────────────

        [Test]
        public void TryGetPulseSpec_Walk_MatchesDesignDocTable()
        {
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Walk, out float r, out float d));
            Assert.AreEqual(LocomotionConfig.WalkPulseRadius, r);   // §5.1 2m
            Assert.AreEqual(LocomotionConfig.WalkPulseDuration, d); // §5.1 0.4s
            Assert.AreEqual(2f, r);
            Assert.AreEqual(0.4f, d);
        }

        [Test]
        public void TryGetPulseSpec_Sprint_MatchesDesignDocTable()
        {
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Sprint, out float r, out float d));
            Assert.AreEqual(6f, r);   // §5.1 6m
            Assert.AreEqual(0.8f, d); // §5.1 0.8s
        }

        [Test]
        public void TryGetPulseSpec_Valve_MatchesDesignDocTable()
        {
            // §5.1 밸브 회전: 12m, "회전 내내"(4인 MVP 3초). §6.1 의도된 유인 장치.
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Valve, out float r, out float d));
            Assert.AreEqual(Valve.SoundRadiusMeters, r);
            Assert.AreEqual(12f, r);
            Assert.AreEqual(Valve.DefaultRotationSeconds, d);
        }

        [Test]
        public void TryGetPulseSpec_Knock_MatchesDesignDocTable()
        {
            // §3.2 "발생 소음: '대화' 등급과 동일 취급 (반경 9m, 지속 1.2초)" + §5.1 노크 행.
            // 스프린트 27에서 메아리 능력이 들어오며 거부 목록에서 빠졌다.
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Knock, out float r, out float d));
            Assert.AreEqual(9f, r);
            Assert.AreEqual(1.2f, d);
        }

        [Test]
        public void TryGetPulseSpec_Knock_MatchesTalkRadiusButKeepsOwnType()
        {
            // §3.2가 "대화와 동일 취급"이라 수치는 같아야 하고, §3.4가 노크를 게이지 트리거에서
            // 제외하므로 **종류는 달라야 한다** — Talk로 뭉개면 메아리가 술래 게이지를 띄운다.
            ServerPulseDriver.TryGetPulseSpec(SoundType.Knock, out float knockR, out float knockD);
            ServerPulseDriver.TryGetPulseSpec(SoundType.Talk, out float talkR, out float talkD);

            Assert.AreEqual(talkR, knockR);
            Assert.AreEqual(talkD, knockD);
            Assert.IsFalse(DirectionGaugeRules.TriggersGauge(SoundType.Knock));
            Assert.IsTrue(DirectionGaugeRules.TriggersGauge(SoundType.Talk));
        }

        [Test]
        public void AddPulse_Knock_IsTracked()
        {
            var d = new ServerPulseDriver();
            Assert.GreaterOrEqual(d.AddPulse(SourceId, SoundType.Knock, Vector3.zero, 0f), 0);
            Assert.AreEqual(1, d.ActivePulseCount);
        }

        // ── §5.2 음성 3등급 (스프린트 26b) ────────────────────────────────

        [Test]
        public void TryGetPulseSpec_Whisper_MatchesDesignDoc()
        {
            // §5.1 "속삭임 | 4m | 0.6초"
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Whisper, out float r, out float d));
            Assert.AreEqual(4f, r, 0.001f);
            Assert.AreEqual(0.6f, d, 0.001f);
        }

        [Test]
        public void TryGetPulseSpec_Talk_MatchesDesignDoc()
        {
            // §5.1 "대화 | 9m | 1.2초"
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Talk, out float r, out float d));
            Assert.AreEqual(9f, r, 0.001f);
            Assert.AreEqual(1.2f, d, 0.001f);
        }

        [Test]
        public void TryGetPulseSpec_Shout_MatchesDesignDoc()
        {
            // §5.1 "고함 | 22m | 2.5초"
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Shout, out float r, out float d));
            Assert.AreEqual(22f, r, 0.001f);
            Assert.AreEqual(2.5f, d, 0.001f);
        }

        [Test]
        public void VoiceRadii_AreOrderedByLoudness()
        {
            // 속삭임 < 대화 < 고함. 순서가 뒤집히면 "크게 말할수록 안전"해져 §2.1 훅이 무너진다.
            ServerPulseDriver.TryGetPulseSpec(SoundType.Whisper, out float whisper, out _);
            ServerPulseDriver.TryGetPulseSpec(SoundType.Talk, out float talk, out _);
            ServerPulseDriver.TryGetPulseSpec(SoundType.Shout, out float shout, out _);

            Assert.Less(whisper, talk);
            Assert.Less(talk, shout);
        }

        [Test]
        public void AddPulse_Shout_IsAcceptedAndTracked()
        {
            var d = new ServerPulseDriver();

            int id = d.AddPulse(SourceId, SoundType.Shout, Vector3.zero, 0f);

            Assert.GreaterOrEqual(id, 0, "고함은 §5.1 표에 있으므로 서버가 파문을 만들어야 한다.");
            Assert.AreEqual(1, d.ActivePulseCount);
        }

        [Test]
        public void AddPulse_SupportedType_IsTracked()
        {
            var d = new ServerPulseDriver();
            Assert.GreaterOrEqual(d.AddPulse(SourceId, SoundType.Walk, Vector3.zero, 0f), 0);
            Assert.AreEqual(1, d.ActivePulseCount);
        }

        [Test]
        public void AddPulse_ServerRecalculatesRadius_ClientCannotInflate()
        {
            // 클라이언트가 넘기는 값은 종류뿐이므로, 걷기로 주장하면 반경은 항상 2m다.
            // 6m 밖 청취자는 절대 듣지 못한다 — 클라가 반경을 부풀릴 수단이 없음을 고정한다.
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Walk, Vector3.zero, 0f);

            var far = new ListenerSnapshot(ListenerA, new Vector3(5f, 0f, 0f), RoleType.Runner);
            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(far), new ClearProbe());

            Assert.AreEqual(0, deliveries.Count, "걷기(2m)인데 5m 청취자에게 전달됐다");
        }

        // ── 청취자별로 결과가 갈린다 (§14.3 개별 전송의 전제) ─────────────

        [Test]
        public void Tick_NearListenerGetsAppeared_FarListenerGetsNothing()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, Vector3.zero, 0f); // 6m

            var near = new ListenerSnapshot(ListenerA, new Vector3(3f, 0f, 0f), RoleType.Runner);
            var far = new ListenerSnapshot(ListenerB, new Vector3(50f, 0f, 0f), RoleType.Runner);

            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(near, far), new ClearProbe());

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(ListenerA, deliveries[0].ListenerId);
            Assert.AreEqual(PulseDeliveryKind.Appeared, deliveries[0].Kind);
        }

        [Test]
        public void Tick_SourceItself_ReceivesNothing_Gap1()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, Vector3.zero, 0f);

            // 발생원이 청취자 목록에 있어도(서버는 전원을 넣는다) 자기 소리는 받지 않는다.
            var self = new ListenerSnapshot(SourceId, Vector3.zero, RoleType.Runner);
            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(self), new ClearProbe());

            Assert.AreEqual(0, deliveries.Count);
        }

        [Test]
        public void Tick_ClearLineOfSight_DisclosesCoordinates_Gap2()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, new Vector3(1f, 0f, 0f), 0f);

            var listener = new ListenerSnapshot(ListenerA, Vector3.zero, RoleType.Runner);
            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(listener), new ClearProbe());

            Assert.AreEqual(1, deliveries.Count);
            PerceivedPulse p = deliveries[0].Perceived.Value;
            Assert.IsTrue(p.WorldSpaceRingVisible);
            Assert.IsTrue(p.SourcePos.HasValue, "벽 0개면 좌표가 공개돼야 한다(GAP-2)");
        }

        [Test]
        public void Tick_OccludedByWall_HidesCoordinates_DirectionOnly_Gap2()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, new Vector3(1f, 0f, 0f), 0f);

            var listener = new ListenerSnapshot(ListenerA, Vector3.zero, RoleType.Runner);
            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(listener), new WallProbe(1));

            Assert.AreEqual(1, deliveries.Count);
            PerceivedPulse p = deliveries[0].Perceived.Value;
            Assert.IsFalse(p.WorldSpaceRingVisible);
            Assert.IsFalse(p.SourcePos.HasValue, "벽이 있으면 좌표를 보내지 않는다(GAP-2)");
        }

        [Test]
        public void Tick_HardBlocker_NoDelivery()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, new Vector3(1f, 0f, 0f), 0f);

            var listener = new ListenerSnapshot(ListenerA, Vector3.zero, RoleType.Runner);
            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(listener), new BlockedProbe());

            Assert.AreEqual(0, deliveries.Count, "하드블로커면 아예 전달되지 않아야 한다(§5.6)");
        }

        [Test]
        public void Tick_SameePulse_DifferentResultsPerListener_CoordinateVsDirection()
        {
            // 같은 파문 하나에서 청취자마다 다른 내용이 나가는지 — 이번 스프린트의 존재 이유.
            // 벽 개수가 청취자마다 다른 상황을 프로브로 흉내낼 수 없으므로(프로브는 공용),
            // 여기서는 "거리 차이로 한쪽만 받는 것"과 별개로 역할 차이를 확인한다.
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Walk, Vector3.zero, 0f); // 2m — 러너는 못 듣는 거리

            // §5.7: 술래는 반경 ×1.2 → 2.4m까지 인지. 2.2m 지점은 술래만 듣는다.
            var seeker = new ListenerSnapshot(ListenerA, new Vector3(2.2f, 0f, 0f), RoleType.Seeker);
            var runner = new ListenerSnapshot(ListenerB, new Vector3(2.2f, 0f, 0f), RoleType.Runner);

            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(seeker, runner), new ClearProbe());

            Assert.AreEqual(1, deliveries.Count, "술래만 받아야 한다(§5.7 인지 배율)");
            Assert.AreEqual(ListenerA, deliveries[0].ListenerId);
        }

        [Test]
        public void Tick_SeekerPerceivesLargerRadius_Than_Runner_Gap57()
        {
            // §5.7 인지 배율이 실제 배정 역할(스프린트 13) 기준으로 적용되는지 —
            // 같은 파문을 두 역할이 다른 반경으로 인지한다.
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, Vector3.zero, 0f);

            var seeker = new ListenerSnapshot(ListenerA, new Vector3(1f, 0f, 0f), RoleType.Seeker);
            var runner = new ListenerSnapshot(ListenerB, new Vector3(1f, 0f, 0f), RoleType.Runner);

            List<PulseDelivery> deliveries = d.Tick(0f, Listeners(seeker, runner), new ClearProbe());

            Assert.AreEqual(2, deliveries.Count);

            float seekerRadius = 0f, runnerRadius = 0f;
            foreach (PulseDelivery delivery in deliveries)
            {
                if (delivery.ListenerId == ListenerA) seekerRadius = delivery.Perceived.Value.PerceivedRadius;
                if (delivery.ListenerId == ListenerB) runnerRadius = delivery.Perceived.Value.PerceivedRadius;
            }

            Assert.Greater(seekerRadius, runnerRadius, "술래(×1.2)가 러너보다 크게 인지해야 한다");
        }

        [Test]
        public void Tick_NoListeners_NoDeliveries_NoException()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, Vector3.zero, 0f);

            List<PulseDelivery> deliveries = d.Tick(0f, new List<ListenerSnapshot>(), new ClearProbe());
            Assert.AreEqual(0, deliveries.Count);
        }

        [Test]
        public void Tick_NaturalExpiry_RemovesPulseWithoutDisappeared()
        {
            // T7 설계: 자연 만료는 통지 없이 조용히 끝난다(클라가 자체 타이머로 소멸).
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, Vector3.zero, 0f); // duration 0.8s

            var listener = new ListenerSnapshot(ListenerA, new Vector3(1f, 0f, 0f), RoleType.Runner);
            d.Tick(0f, Listeners(listener), new ClearProbe()); // Appeared

            // 최대 보관 상한(duration × 1.5)을 넘긴 시점
            List<PulseDelivery> later = d.Tick(5f, Listeners(listener), new ClearProbe());

            Assert.AreEqual(0, later.Count, "자연 만료에는 Disappeared를 보내지 않는다");
            Assert.AreEqual(0, d.ActivePulseCount, "만료된 파문은 서버에서 제거돼야 한다");
        }

        [Test]
        public void Tick_OcclusionAppears_EmitsDisappeared_WhenStillWithinDuration()
        {
            var d = new ServerPulseDriver();
            d.AddPulse(SourceId, SoundType.Sprint, new Vector3(1f, 0f, 0f), 0f);

            var listener = new ListenerSnapshot(ListenerA, Vector3.zero, RoleType.Runner);
            d.Tick(0f, Listeners(listener), new ClearProbe()); // Appeared

            // 재판정 주기(0.25s) 경과 후 하드블로커 등장 → 지속시간 내이므로 Disappeared
            List<PulseDelivery> deliveries = d.Tick(0.3f, Listeners(listener), new BlockedProbe());

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(PulseDeliveryKind.Disappeared, deliveries[0].Kind);
            Assert.IsFalse(deliveries[0].Perceived.HasValue);
        }
    }
}
