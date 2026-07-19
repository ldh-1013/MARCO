using NUnit.Framework;
using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 3 배선 검증: 발소리 이벤트 데이터가 §5.5 SoundPulse로 올바르게 변환되어
    /// T7 ActivePulseTracker 의미론(GAP-1/GAP-3)을 그대로 통과하는지,
    /// 그리고 §5.6 차폐 판정 순수부(Classify)가 태그 규칙을 지키는지 고정한다.
    /// Physics 호출부(Probe)는 에디터 Play 검증 항목(체크리스트 §5)이다.
    /// </summary>
    public class LocalPulsePipelineTests
    {
        private const ulong SourceId = 1;
        private const ulong ListenerId = 999;

        private class FakeProbe : IOcclusionProbe
        {
            public OcclusionResult Result = new OcclusionResult(false, 0);
            public OcclusionResult Probe(Vector3 from, Vector3 to) => Result;
        }

        private static (LocalPulsePipeline pipeline, FakeProbe probe, System.Collections.Generic.List<PulseDelivery> received)
            MakePipeline(Vector3 listenerPos, ulong listenerId = ListenerId, RoleType listenerRole = RoleType.Runner)
        {
            var probe = new FakeProbe();
            var pipeline = new LocalPulsePipeline(probe, SourceId);
            pipeline.SetDebugListener(listenerId, listenerPos, listenerRole);
            var received = new System.Collections.Generic.List<PulseDelivery>();
            pipeline.DeliveryEmitted += received.Add;
            return (pipeline, probe, received);
        }

        // 1) 걷기 발소리 이벤트 → 반경 내 청취자에게 §5.1 원본값 그대로 Appeared.
        [Test]
        public void WalkPulse_DeliveredToDistinctListener_AsAppeared()
        {
            var (pipeline, _, received) = MakePipeline(new Vector3(1.5f, 0, 0));

            int pulseId = pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, timestamp: 0f);
            pipeline.Tick(0f);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, received[0].Kind);
            Assert.AreEqual(pulseId, received[0].PulseId);
            Assert.AreEqual(ListenerId, received[0].ListenerId);
            Assert.AreEqual(2f, received[0].Perceived.Value.PerceivedRadius, 0.001f);
            Assert.AreEqual(0.4f, received[0].Perceived.Value.PerceivedDuration, 0.001f);
        }

        // 2) GAP-1: 청취자 ID가 발생원과 같으면(self-listen) 아무 델리버리도 없다.
        //    — 로컬 스모크 리그에서 디버그 청취자 ID를 반드시 다르게 줘야 하는 이유.
        [Test]
        public void SelfListener_ReceivesNothing()
        {
            var (pipeline, _, received) = MakePipeline(new Vector3(1f, 0, 0), listenerId: SourceId);

            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, 0f);
            pipeline.Tick(0f);

            Assert.AreEqual(0, received.Count);
        }

        // 3) 질주 발소리(6m/0.8s)도 값 왜곡 없이 전달된다.
        [Test]
        public void SprintPulse_ValuesPreserved()
        {
            var (pipeline, _, received) = MakePipeline(new Vector3(5f, 0, 0));

            pipeline.OnFootstepPulse(SoundType.Sprint, 6f, 0.8f, Vector3.zero, 0f);
            pipeline.Tick(0f);

            Assert.AreEqual(1, received.Count);
            var p = received[0].Perceived.Value;
            Assert.AreEqual(6f, p.PerceivedRadius, 0.001f);
            Assert.AreEqual(0.8f, p.PerceivedDuration, 0.001f);
            Assert.IsTrue(p.WorldSpaceRingVisible); // 벽 0개 → §5.4 좌표 공개 조건
        }

        // 4) 반경 밖 청취자는 아무것도 받지 않는다.
        [Test]
        public void ListenerBeyondRadius_NoDelivery()
        {
            var (pipeline, _, received) = MakePipeline(new Vector3(3f, 0, 0)); // 걷기 2m 밖

            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, 0f);
            pipeline.Tick(0f);

            Assert.AreEqual(0, received.Count);
        }

        // 5) GAP-3 재판정이 파이프라인을 통해서도 동작: 차폐 변화 → Updated,
        //    하드블로커 등장 → Disappeared.
        [Test]
        public void OcclusionChanges_FlowThroughAsUpdatedThenDisappeared()
        {
            var (pipeline, probe, received) = MakePipeline(new Vector3(1f, 0, 0));

            pipeline.OnFootstepPulse(SoundType.Sprint, 6f, 0.8f, Vector3.zero, 0f);
            pipeline.Tick(0f);                                    // Appeared (6m)

            probe.Result = new OcclusionResult(false, 1);         // 벽 1개 등장
            pipeline.Tick(0.25f);                                 // Updated (3m)

            probe.Result = new OcclusionResult(true, 0);          // 하드블로커
            pipeline.Tick(0.5f);                                  // Disappeared

            Assert.AreEqual(3, received.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, received[0].Kind);
            Assert.AreEqual(PulseDeliveryKind.Updated, received[1].Kind);
            Assert.AreEqual(3f, received[1].Perceived.Value.PerceivedRadius, 0.001f);
            Assert.IsFalse(received[1].Perceived.Value.WorldSpaceRingVisible);
            Assert.AreEqual(PulseDeliveryKind.Disappeared, received[2].Kind);
        }

        // 6) §5.6 순수 판정부: 태그 목록 → 차폐 결과. Physics 없이 규칙만 고정한다.
        [Test]
        public void Classify_EmptyHits_NoOcclusion()
        {
            var result = PhysicsOcclusionProbe.Classify(new string[0]);
            Assert.IsFalse(result.HasHardBlocker);
            Assert.AreEqual(0, result.WallCount);
        }

        [Test]
        public void Classify_SingleWall_CountsOne()
        {
            var result = PhysicsOcclusionProbe.Classify(new[] { "Wall" });
            Assert.IsFalse(result.HasHardBlocker);
            Assert.AreEqual(1, result.WallCount);
        }

        [Test]
        public void Classify_TwoWalls_CountsTwo()
        {
            var result = PhysicsOcclusionProbe.Classify(new[] { "Wall", "Wall" });
            Assert.IsFalse(result.HasHardBlocker);
            Assert.AreEqual(2, result.WallCount);
        }

        [Test]
        public void Classify_HardBlocker_FlagsHardBlock()
        {
            var result = PhysicsOcclusionProbe.Classify(new[] { "HardBlocker" });
            Assert.IsTrue(result.HasHardBlocker);
            Assert.AreEqual(0, result.WallCount);
        }

        [Test]
        public void Classify_WallAndHardBlocker_ReportsBoth()
        {
            var result = PhysicsOcclusionProbe.Classify(new[] { "Wall", "HardBlocker" });
            Assert.IsTrue(result.HasHardBlocker);
            Assert.AreEqual(1, result.WallCount);
        }
    }
}
