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
    }
}
