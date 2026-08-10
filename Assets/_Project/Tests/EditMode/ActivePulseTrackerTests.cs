using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// T7 수용 기준(docs/phase-1-분석.md §3): PerceivedPulse 프로토콜의 재판정 루프.
    /// GAP-3 결정("최초 1회 + 0.25초 간격 재판정, 변화분만 방출")을 고정한다.
    /// </summary>
    public class ActivePulseTrackerTests
    {
        private const ulong SourceId = 1;
        private const ulong RunnerId = 2;
        private const ulong SeekerId = 3;

        /// <summary>호출 시점마다 다른 차폐 결과를 돌려줄 수 있는 가짜 프로브.</summary>
        private class MutableProbe : IOcclusionProbe
        {
            public OcclusionResult Result = new OcclusionResult(false, 0);
            public int ProbeCallCount;

            public OcclusionResult Probe(Vector3 from, Vector3 to)
            {
                ProbeCallCount++;
                return Result;
            }
        }

        private static SoundPulse MakeShout(float timestamp = 0f)
        {
            // §5.1 고함: 반경 22m, 지속 2.5초
            return new SoundPulse(SourceId, Vector3.zero, 22f, 2.5f, SoundType.Shout, timestamp);
        }

        private static ListenerSnapshot Runner(Vector3 pos) => new ListenerSnapshot(RunnerId, pos, RoleType.Runner);
        private static ListenerSnapshot Seeker(Vector3 pos) => new ListenerSnapshot(SeekerId, pos, RoleType.Seeker);

        // 1) 등록 후 첫 Tick: 가청 리스너에게 Appeared가 나간다.
        [Test]
        public void FirstTick_AudibleListener_ReceivesAppeared()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            int pulseId = tracker.AddPulse(MakeShout());

            var deliveries = tracker.Tick(0f, new[] { Runner(new Vector3(10, 0, 0)) }, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, deliveries[0].Kind);
            Assert.AreEqual(RunnerId, deliveries[0].ListenerId);
            Assert.AreEqual(pulseId, deliveries[0].PulseId);
            Assert.IsTrue(deliveries[0].Perceived.HasValue);
        }

        // 2) GAP-1: 발생원 본인은 리스너 목록에 있어도 아무것도 받지 않는다.
        [Test]
        public void FirstTick_SourceItself_ReceivesNothing()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());
            var self = new ListenerSnapshot(SourceId, new Vector3(1, 0, 0), RoleType.Runner);

            var deliveries = tracker.Tick(0f, new[] { self }, probe);

            Assert.AreEqual(0, deliveries.Count);
        }

        // 3) 0.25초 미만 재-Tick: 상황이 변해도 재판정하지 않는다(레이캐스트 예산 보호).
        [Test]
        public void TickBeforeInterval_DoesNotReevaluate()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());
            var listener = new[] { Runner(new Vector3(10, 0, 0)) };

            tracker.Tick(0f, listener, probe);
            int callsAfterFirst = probe.ProbeCallCount;
            probe.Result = new OcclusionResult(false, 1); // 벽이 생겼지만
            var deliveries = tracker.Tick(0.1f, listener, probe); // 아직 0.25초 전

            Assert.AreEqual(0, deliveries.Count);
            Assert.AreEqual(callsAfterFirst, probe.ProbeCallCount, "재판정 자체가 없어야 한다");
        }

        // 4) 0.25초 경과 + 차폐 변화(벽 0→1) → Updated (감쇠된 값으로).
        [Test]
        public void AfterInterval_OcclusionChanged_EmitsUpdated()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());
            var listener = new[] { Runner(new Vector3(10, 0, 0)) };

            tracker.Tick(0f, listener, probe);
            probe.Result = new OcclusionResult(false, 1); // 벽 1개 → 반경 11m, 10m는 여전히 가청
            var deliveries = tracker.Tick(0.25f, listener, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(PulseDeliveryKind.Updated, deliveries[0].Kind);
            Assert.AreEqual(11f, deliveries[0].Perceived.Value.PerceivedRadius, 0.001f);
            Assert.IsFalse(deliveries[0].Perceived.Value.WorldSpaceRingVisible);
        }

        // 5) 재판정에서 반경 밖으로 이동 → Disappeared (지속시간이 남았는데 소실).
        [Test]
        public void AfterInterval_ListenerMovedOut_EmitsDisappeared()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());

            tracker.Tick(0f, new[] { Runner(new Vector3(10, 0, 0)) }, probe);
            var deliveries = tracker.Tick(0.25f, new[] { Runner(new Vector3(30, 0, 0)) }, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(PulseDeliveryKind.Disappeared, deliveries[0].Kind);
            Assert.IsFalse(deliveries[0].Perceived.HasValue);
        }

        // 6) Disappeared 후 다시 반경 안으로 → 다시 Appeared (GAP-3 "벽 뒤에서 나오는 케이스").
        [Test]
        public void ReenteringRadius_EmitsAppearedAgain()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());

            tracker.Tick(0f, new[] { Runner(new Vector3(10, 0, 0)) }, probe);
            tracker.Tick(0.25f, new[] { Runner(new Vector3(30, 0, 0)) }, probe);   // Disappeared
            var deliveries = tracker.Tick(0.5f, new[] { Runner(new Vector3(10, 0, 0)) }, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, deliveries[0].Kind);
        }

        // 7) 변화가 없으면 재판정해도 이벤트가 없다(대역폭 보호).
        [Test]
        public void NoChange_EmitsNothing()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());
            var listener = new[] { Runner(new Vector3(10, 0, 0)) };

            tracker.Tick(0f, listener, probe);
            var deliveries = tracker.Tick(0.25f, listener, probe);

            Assert.AreEqual(0, deliveries.Count);
        }

        // 8) 러너 기준 지속시간(2.5초)이 지나면 그 리스너는 재판정 대상에서 빠진다.
        //    자연 만료이므로 Disappeared도 보내지 않는다.
        [Test]
        public void AfterListenerDuration_NoMoreDeliveriesForRunner()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout(timestamp: 0f));
            var listener = new[] { Runner(new Vector3(10, 0, 0)) };

            tracker.Tick(0f, listener, probe);
            var deliveries = tracker.Tick(2.6f, listener, probe); // 2.5초 초과

            Assert.AreEqual(0, deliveries.Count, "자연 만료는 통지 없이 끝난다");
        }

        // 9) §5.7: 같은 시점에 술래(×1.5 → 3.75초)는 아직 유효해 delivery를 받는다.
        [Test]
        public void SeekerDurationExtended_StillReceivesAfterRunnerExpiry()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout(timestamp: 0f));
            var listeners = new[] { Runner(new Vector3(10, 0, 0)), Seeker(new Vector3(10, 0, 0)) };

            tracker.Tick(0f, listeners, probe);
            probe.Result = new OcclusionResult(false, 1); // 변화 유발
            var deliveries = tracker.Tick(2.6f, listeners, probe);

            Assert.AreEqual(1, deliveries.Count, "러너는 만료, 술래만 갱신받는다");
            Assert.AreEqual(SeekerId, deliveries[0].ListenerId);
        }

        // 10) 최대 지속(×1.5) 초과 시 펄스가 트래커에서 제거된다.
        [Test]
        public void AfterMaxDuration_PulseIsRemoved()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout(timestamp: 0f));
            Assert.AreEqual(1, tracker.ActivePulseCount);

            tracker.Tick(3.8f, new List<ListenerSnapshot>(), probe); // 2.5 × 1.5 = 3.75 초과

            Assert.AreEqual(0, tracker.ActivePulseCount);
        }

        // 11) 하드블로커 뒤 리스너: 처음부터 Appeared 자체가 없다.
        [Test]
        public void HardBlocked_NeverAppears()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe { Result = new OcclusionResult(true, 0) };
            tracker.AddPulse(MakeShout());

            var deliveries = tracker.Tick(0f, new[] { Runner(new Vector3(5, 0, 0)) }, probe);

            Assert.AreEqual(0, deliveries.Count);
        }

        // 12) 여러 펄스 동시 추적: 각 펄스가 독립적으로 delivery를 낸다.
        [Test]
        public void MultiplePulses_TrackedIndependently()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            int idA = tracker.AddPulse(MakeShout());
            int idB = tracker.AddPulse(new SoundPulse(SourceId, new Vector3(5, 0, 0), 9f, 1.2f, SoundType.Talk, 0f));

            var deliveries = tracker.Tick(0f, new[] { Runner(new Vector3(8, 0, 0)) }, probe);

            Assert.AreEqual(2, deliveries.Count);
            var pulseIds = deliveries.Select(d => d.PulseId).ToList();
            CollectionAssert.AreEquivalent(new[] { idA, idB }, pulseIds);
        }

        // ── §3.4 방향 게이지 배선 (스프린트 26 더블체크 — 규칙만 있고 소비자가 없던 문제) ──
        //
        // DirectionGaugeRules는 26c에서 만들어졌지만 어떤 프로덕션 코드도 호출하지 않아
        // 술래 화면에 게이지가 실제로 뜨지 않았다. 아래 테스트는 트래커가 판정 결과를
        // delivery에 실어 보낸다는 것을 고정한다 — 다시 끊기면 여기가 빨간불이 된다.

        // 13) 술래는 고함에 대해 게이지를 받는다(§3.4 "술래 전용 정보 우위").
        [Test]
        public void Gauge_SeekerHearingShout_IsLit()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());

            var deliveries = tracker.Tick(0f, new[] { Seeker(new Vector3(10, 0, 0)) }, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.IsTrue(deliveries[0].GaugeLit, "§3.4 게이지가 delivery에 실리지 않으면 화면에 뜰 수 없다.");
        }

        // 14) 러너는 같은 고함에도 게이지를 받지 않는다(술래 전용).
        [Test]
        public void Gauge_RunnerHearingShout_IsNotLit()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());

            var deliveries = tracker.Tick(0f, new[] { Runner(new Vector3(10, 0, 0)) }, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.IsFalse(deliveries[0].GaugeLit, "게이지가 러너에게도 뜨면 §3.4 정보 우위가 무너진다.");
        }

        // 15) 발소리·밸브·속삭임은 술래에게도 게이지를 트리거하지 않는다(§3.4 제외 목록).
        [TestCase(SoundType.Walk, 2f, 0.4f)]
        [TestCase(SoundType.Sprint, 6f, 0.8f)]
        [TestCase(SoundType.Valve, 12f, 3f)]
        [TestCase(SoundType.Whisper, 4f, 0.6f)]
        public void Gauge_ExcludedTypes_AreNotLitEvenForSeeker(SoundType type, float radius, float duration)
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(new SoundPulse(SourceId, Vector3.zero, radius, duration, type, 0f));

            var deliveries = tracker.Tick(0f, new[] { Seeker(new Vector3(1, 0, 0)) }, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.IsFalse(deliveries[0].GaugeLit, $"{type}은 §3.4 게이지 트리거 대상이 아니다.");
        }

        // 16) 소실 통지에는 게이지가 실리지 않는다(그릴 대상 자체가 사라진 신호).
        [Test]
        public void Gauge_DisappearedDelivery_IsNotLit()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe();
            tracker.AddPulse(MakeShout());
            var listeners = new[] { Seeker(new Vector3(10, 0, 0)) };

            tracker.Tick(0f, listeners, probe);

            // 차폐가 생겨 조기 소실.
            probe.Result = new OcclusionResult(true, 0);
            var deliveries = tracker.Tick(ActivePulseTracker.ReevaluationInterval, listeners, probe);

            Assert.AreEqual(1, deliveries.Count);
            Assert.AreEqual(PulseDeliveryKind.Disappeared, deliveries[0].Kind);
            Assert.IsFalse(deliveries[0].GaugeLit);
        }

        // 17) GAP-4 결정("§5.6을 통과한 펄스만 트리거") — 판정에서 탈락하면 게이지도 없다.
        //     §3.4 사거리(33m)가 물리 반경(22m)보다 넓다는 이유로 탈락한 파문에까지
        //     게이지가 뜨면, 차폐를 뚫고 위치가 새어나간다.
        [Test]
        public void Gauge_PulseRejectedByOcclusion_IsNotDeliveredAtAll()
        {
            var tracker = new ActivePulseTracker();
            var probe = new MutableProbe { Result = new OcclusionResult(true, 0) };
            tracker.AddPulse(MakeShout());

            var deliveries = tracker.Tick(0f, new[] { Seeker(new Vector3(10, 0, 0)) }, probe);

            Assert.AreEqual(0, deliveries.Count, "§5.6 탈락 파문은 게이지 경로로도 새어나가면 안 된다.");
        }
    }
}
