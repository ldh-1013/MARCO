using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 버그 조사(마르코_버그_펄스만료_prompt.md): 에디터 Play 세션에서 Disappeared
    /// 델리버리가 한 번도 관측되지 않은 증상을 파이프라인 계층에서 재현한다.
    ///
    /// 결론(테스트로 확정): 코드 버그가 아니다. 두 가지가 겹쳐서 증상을 만든다.
    /// 1) ActivePulseTracker의 "자연 만료는 통지 없이 조용히 끝난다" 설계(T7, 기존
    ///    ActivePulseTrackerTests.AfterListenerDuration_NoMoreDeliveriesForRunner에서
    ///    이미 검증된 의도적 동작) — Disappeared는 지속시간 중 차폐/이탈로 "일찍" 사라질
    ///    때만 쓴다.
    /// 2) LocalPulsePipelineBehaviour의 디버그 청취자가 Start() 시점 스폰 위치에 고정
    ///    되는 임시 스모크 리그라, 발생원(플레이어)이 걸어서 펄스 반경(2~6m) 밖으로
    ///    나가면 그 뒤로 추가되는 펄스는 애초에 Appeared 자체가 나지 않는다(청취자 목록에
    ///    닿지 않으므로). 로그에서 "ID가 건너뛴 것처럼" 보이는 이유가 이것이다 —
    ///    AddPulse는 ID를 스킵하지 않는다(테스트 4로 확정).
    /// </summary>
    public class LocalPulsePipelineExpiryTests
    {
        private const ulong ListenerId = 999;

        private class FakeProbe : IOcclusionProbe
        {
            public OcclusionResult Result = new OcclusionResult(false, 0);
            public OcclusionResult Probe(Vector3 from, Vector3 to) => Result;
        }

        private static LocalPulsePipeline MakePipeline(out FakeProbe probe, out List<PulseDelivery> received,
            Vector3 listenerPos, RoleType listenerRole = RoleType.Runner)
        {
            probe = new FakeProbe();
            var pipeline = new LocalPulsePipeline(probe, sourcePlayerId: 1);
            pipeline.SetDebugListener(ListenerId, listenerPos, listenerRole);
            received = new List<PulseDelivery>();
            var list = received;
            pipeline.DeliveryEmitted += list.Add;
            return pipeline;
        }

        // 1) 기본 만료: 청취자가 계속 반경 안에 있고 차폐도 안 바뀐 채로 duration을
        //    넘기면 — 보고서의 기대("Updated 또는 Disappeared 중 하나는 나와야 함")와
        //    달리 — 아무 델리버리도 나오지 않는다. 이게 버그가 아니라 설계다.
        [Test]
        public void NaturalExpiry_WithoutOcclusionOrRangeChange_EmitsNoDisappeared()
        {
            var pipeline = MakePipeline(out _, out var received, listenerPos: new Vector3(1f, 0, 0));

            pipeline.OnFootstepPulse(SoundType.Sprint, 6f, 0.8f, Vector3.zero, timestamp: 0f);
            pipeline.Tick(0f);     // Appeared
            pipeline.Tick(0.25f);  // 재판정, 변화 없음
            pipeline.Tick(0.5f);   // 재판정, 변화 없음
            pipeline.Tick(0.75f);  // 아직 duration(0.8) 이내
            pipeline.Tick(1.0f);   // duration 초과 → 이 리스너는 조용히 제외
            pipeline.Tick(1.5f);   // MaxDurationMultiplier(1.2) 초과 → 트래커에서 완전 제거

            Assert.AreEqual(1, received.Count, "Appeared 1건만 있어야 하며 Disappeared는 없어야 한다");
            Assert.AreEqual(PulseDeliveryKind.Appeared, received[0].Kind);
        }

        // 2) 역할 배율(×1.5)이 적용된 청취자도 같은 방식으로 "조용히" 만료된다 —
        //    배율은 만료 시점을 늦출 뿐, 만료 시 델리버리를 발생시키지는 않는다.
        [Test]
        public void NaturalExpiry_WithRoleDurationMultiplier_StillSilent()
        {
            var pipeline = MakePipeline(out _, out var received, listenerPos: new Vector3(1f, 0, 0), listenerRole: RoleType.Seeker);

            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, timestamp: 0f);
            pipeline.Tick(0f);     // Appeared (술래 ×1.5 → 유효 0.6초까지)
            pipeline.Tick(0.5f);   // 아직 유효(0.5 < 0.4*1.5=0.6)
            pipeline.Tick(0.75f);  // 유효시간 초과 → 조용히 제외
            pipeline.Tick(1.0f);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, received[0].Kind);
        }

        // 3) 진짜 Disappeared: duration이 남은 상태에서 하드블로커가 등장하면(=이른 소실)
        //    정상적으로 방출된다. Core의 Disappeared 자체는 살아있다는 대조군 테스트.
        [Test]
        public void EarlyOcclusionLoss_WithinDuration_EmitsDisappeared()
        {
            var pipeline = MakePipeline(out var probe, out var received, listenerPos: new Vector3(1f, 0, 0));

            pipeline.OnFootstepPulse(SoundType.Sprint, 6f, 0.8f, Vector3.zero, timestamp: 0f);
            pipeline.Tick(0f);                                   // Appeared

            probe.Result = new OcclusionResult(true, 0);         // 하드블로커 등장(이른 소실)
            pipeline.Tick(0.25f);                                // 아직 duration(0.8) 이내 → Disappeared 정상 발생

            Assert.AreEqual(2, received.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, received[0].Kind);
            Assert.AreEqual(PulseDeliveryKind.Disappeared, received[1].Kind);
        }

        // 4) 연속 추가 상황: 걷기 주기(0.4초)로 펄스를 계속 추가해도 ActivePulseCount가
        //    무한히 쌓이지 않는다(오래된 펄스는 MaxDurationMultiplier 시점에 조용히 제거).
        [Test]
        public void ContinuousAddition_OldPulsesEvictedWithoutUnboundedGrowth()
        {
            var pipeline = MakePipeline(out _, out _, listenerPos: new Vector3(100f, 0, 0)); // 반경 밖 — Appeared 자체를 배제하고 개수만 검증

            for (int i = 0; i < 20; i++)
            {
                float t = i * 0.4f;
                pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, timestamp: t);
                pipeline.Tick(t);
            }

            // 0.4초 간격으로 20개(총 7.6초) 추가, 각 펄스의 절대 보관 상한은 0.4*1.5=0.6초.
            // 마지막 틱(7.6초) 기준으로 살아있을 수 있는 펄스는 최근 1~2개뿐이어야 한다.
            Assert.LessOrEqual(pipeline.ActivePulseCount, 2,
                "오래된 펄스가 계속 쌓이면 안 된다 — 조용한 만료가 실제로 제거까지 하는지 확인");
        }

        // 5) ID 연속성: AddPulse는 호출 순서대로 빈틈없는 ID를 발급한다.
        //    (로그에서 "2,3,4가 빠진 것처럼" 보인 게 ID 스킵이 아님을 확정)
        [Test]
        public void SequentialAddPulse_ReturnsGaplessIds()
        {
            var pipeline = MakePipeline(out _, out _, listenerPos: new Vector3(1f, 0, 0));

            var ids = new List<int>();
            for (int i = 0; i < 6; i++)
                ids.Add(pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, timestamp: i * 0.4f));

            CollectionAssert.AreEqual(new[] { 0, 1, 2, 3, 4, 5 }, ids);
        }

        // 6) 스모크 리그 재현: 고정 청취자 근처에서 펄스가 시작되고, 발생원이 점점
        //    멀어지며 계속 펄스를 낸다(플레이어가 스폰에서 걸어 나가는 상황과 동일).
        //    반경을 벗어난 이후의 펄스는 ID는 정상 발급되지만 Appeared가 전혀 나지
        //    않는다 — "pulse=2,3,4가 로그에 안 보인 것"의 정체.
        [Test]
        public void SourceWalkingAwayFromFixedListener_LaterPulsesNeverAppear()
        {
            var pipeline = MakePipeline(out _, out var received, listenerPos: Vector3.zero);

            // 0,1번째 펄스는 청취자(원점) 반경 2m 이내에서 발생 → Appeared
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, new Vector3(0.5f, 0, 0), timestamp: 0f);
            pipeline.Tick(0f);
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, new Vector3(1.0f, 0, 0), timestamp: 0.4f);
            pipeline.Tick(0.4f);

            // 2,3,4번째 펄스는 플레이어가 이미 반경 밖으로 나간 뒤 발생 → 조용히 무시
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, new Vector3(3.0f, 0, 0), timestamp: 0.8f);
            pipeline.Tick(0.8f);
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, new Vector3(3.5f, 0, 0), timestamp: 1.2f);
            pipeline.Tick(1.2f);
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, new Vector3(4.0f, 0, 0), timestamp: 1.6f);
            pipeline.Tick(1.6f);

            var appearedPulseIds = received.Where(d => d.Kind == PulseDeliveryKind.Appeared).Select(d => d.PulseId).ToList();
            CollectionAssert.AreEqual(new[] { 0, 1 }, appearedPulseIds,
                "펄스 2,3,4는 ID는 발급되지만(테스트 5) 반경 밖이라 Appeared를 내지 않는다");
            Assert.IsFalse(received.Any(d => d.Kind == PulseDeliveryKind.Disappeared),
                "펄스 0,1도 반경 안에서 자연 만료하므로 Disappeared는 없다");
        }
    }
}
