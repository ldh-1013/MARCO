using System.Collections.Generic;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §5.6 [v0.4] 차폐 태그별 가중치와, 그 위에 올라앉은 §6.5-3 전제를 고정한다.
    ///
    /// <para>
    /// 이 파일이 지키는 것은 두 가지다.
    /// ① <b>태그 → Wall 환산 가중치</b> (수면 1장 ×0.5 / 층간 바닥 2장 ×0.25)
    /// ② <b>수중 밸브 강제 파문 12m가 수면을 지나 술래에게 7.2m로 도달한다</b>는
    ///    §6.5-3 마지막 대치의 수치 전제.
    /// </para>
    ///
    /// <para>
    /// <c>Physics</c>는 부르지 않는다 — <c>PhysicsOcclusionProbe.Classify</c>(태그 규칙)와
    /// <c>SoundPulseResolver.Resolve</c>(감쇠 규칙)가 둘 다 순수 함수로 갈라져 있어
    /// 에디터 없이 검증된다.
    /// </para>
    /// </summary>
    public class OcclusionTagWeightTests
    {
        private const ulong SourceId = 1;
        private const ulong ListenerId = 2;

        private class FakeOcclusionProbe : IOcclusionProbe
        {
            private readonly OcclusionResult _result;
            public FakeOcclusionProbe(OcclusionResult result) => _result = result;
            public OcclusionResult Probe(Vector3 from, Vector3 to) => _result;
        }

        private static OcclusionResult Classify(params string[] tags)
        {
            return PhysicsOcclusionProbe.Classify(new List<string>(tags));
        }

        // ── ① 태그 → 가중치 ─────────────────────────────────────────────

        [Test]
        public void Wall_CountsAsOne()
        {
            Assert.AreEqual(1, Classify(PhysicsOcclusionProbe.WallTag).WallCount);
        }

        [Test]
        public void WaterSurface_UsesWallTag_SoItCountsAsOne()
        {
            // §5.6 [v0.4] 표: 수면(물 표면) | `Wall` | 통과 시 반경 ×0.5.
            // 수면 전용 태그를 만들지 않았다 — 효과가 벽 1장과 정확히 같고
            // 문서가 태그를 `Wall`로 지정했다. 따라서 코드 분기가 0개다.
            Assert.AreEqual(1, Classify(PhysicsOcclusionProbe.WallTag).WallCount,
                "수면 차폐판은 `Wall` 태그를 그대로 쓴다(§5.6 표).");
        }

        [Test]
        public void FloorSlab_CountsAsTwo()
        {
            // §5.6 [v0.4] 표: 층간 바닥(2층↔지상) | Wall 2장 상당 | 통과 시 반경 ×0.25.
            Assert.AreEqual(PhysicsOcclusionProbe.FloorSlabWeight,
                Classify(PhysicsOcclusionProbe.FloorSlabTag).WallCount);
            Assert.AreEqual(2, Classify(PhysicsOcclusionProbe.FloorSlabTag).WallCount);
        }

        [Test]
        public void FloorSlab_AndWall_Accumulate()
        {
            // 2층에서 벽 하나 건너 지상으로 — 3장 상당(×0.125).
            Assert.AreEqual(3, Classify(
                PhysicsOcclusionProbe.FloorSlabTag, PhysicsOcclusionProbe.WallTag).WallCount);
        }

        [Test]
        public void UntaggedCollider_DoesNotOcclude()
        {
            // SoundBlocking 레이어에 있으면서 차폐 태그가 없으면 세지 않는다.
            // (맵 생성 시점 태그 부착으로 이 경우가 생기지 않게 막는 것이 블록 1-B)
            Assert.AreEqual(0, Classify("SomeUntaggedThing").WallCount);
        }

        [Test]
        public void HardBlocker_WinsOverEverything()
        {
            OcclusionResult r = Classify(
                PhysicsOcclusionProbe.FloorSlabTag, PhysicsOcclusionProbe.HardBlockerTag);

            Assert.IsTrue(r.HasHardBlocker);
        }

        // ── ② §6.5-3 전제: 수중 밸브 12m → 술래 7.2m ───────────────────

        /// <summary>§6.1 밸브 회전 소음. §5.1 표의 "밸브 회전" 등급 반경 12m.</summary>
        private static SoundPulse ValvePulse(Vector3 position)
        {
            return new SoundPulse(SourceId, position,
                Objectives.Valve.SoundRadiusMeters, 1f, SoundType.Valve, timestamp: 0f);
        }

        [Test]
        public void UnderwaterValve_ThroughWaterSurface_ReachesSeekerAt7Point2()
        {
            // §5.6 [v0.4]: "수중 밸브 강제 파문(12m, 6.1절)은 수면을 통과하며 6m로 줄어
            //               술래 청취 기준 7.2m로 도달한다."
            //
            // §5.6 의사코드 순서: ①역할 배율 12 × 1.2 = 14.4 → ②감쇠 ×0.5 → 7.2.
            var pulse = ValvePulse(Vector3.zero);
            var probe = new FakeOcclusionProbe(Classify(PhysicsOcclusionProbe.WallTag));

            // 7.2m 안쪽에 있는 술래(수면 위)에게는 도달한다.
            PerceivedPulse? inside = SoundPulseResolver.Resolve(
                pulse, ListenerId, new Vector3(7f, 0f, 0f), RoleType.Seeker, probe);

            Assert.IsNotNull(inside, "수면 1장이면 술래 청취 반경은 7.2m다.");
            Assert.AreEqual(7.2f, inside.Value.PerceivedRadius, 0.0001f,
                "§6.5-3 마지막 대치의 수치 전제 — 12 × 1.2 × 0.5 = 7.2m.");
        }

        [Test]
        public void UnderwaterValve_ThroughWaterSurface_MissesSeekerJustOutside()
        {
            // 경계값: 7.2m를 넘으면 탈락한다. 이 경계가 "가까이 오면 알고 멀면 모른다"다.
            var pulse = ValvePulse(Vector3.zero);
            var probe = new FakeOcclusionProbe(Classify(PhysicsOcclusionProbe.WallTag));

            Assert.IsNotNull(SoundPulseResolver.Resolve(
                pulse, ListenerId, new Vector3(7.2f, 0f, 0f), RoleType.Seeker, probe),
                "정확히 7.2m는 들리는 쪽이다(§5.6 'straightDist > perceivedRadius'일 때만 탈락).");

            Assert.IsNull(SoundPulseResolver.Resolve(
                pulse, ListenerId, new Vector3(7.21f, 0f, 0f), RoleType.Seeker, probe));
        }

        [Test]
        public void UnderwaterValve_WithoutWaterSurface_WouldReachTwiceAsFar()
        {
            // 수면 차폐가 빠지면 술래 청취 반경이 14.4m가 되어 §6.5-3이 무너진다.
            // "수면을 정식 차폐물로 올린다"는 v0.4 결정이 실제로 값을 바꾸는지 확인한다.
            var pulse = ValvePulse(Vector3.zero);
            var noOcclusion = new FakeOcclusionProbe(new OcclusionResult(false, 0));

            PerceivedPulse? r = SoundPulseResolver.Resolve(
                pulse, ListenerId, new Vector3(7f, 0f, 0f), RoleType.Seeker, noOcclusion);

            Assert.IsNotNull(r);
            Assert.AreEqual(14.4f, r.Value.PerceivedRadius, 0.0001f);
            Assert.AreEqual(2f, r.Value.PerceivedRadius / 7.2f, 0.0001f,
                "수면 1장이 정확히 절반으로 깎는다.");
        }

        [Test]
        public void InterFloorValve_ReachesSeekerAtThreePointSix()
        {
            // §5.6 [v0.4] 층간 바닥 ×0.25 — 2층 물탱크실 밸브 C의 회전음이
            // 지상층 술래에게 도달하는 반경. 14.4 × 0.25 = 3.6m.
            // 이 값이 §10.2-1의 C↔나머지 동시 감시 판정을 결정한다.
            var pulse = ValvePulse(Vector3.zero);
            var probe = new FakeOcclusionProbe(Classify(PhysicsOcclusionProbe.FloorSlabTag));

            PerceivedPulse? r = SoundPulseResolver.Resolve(
                pulse, ListenerId, new Vector3(3.5f, 0f, 0f), RoleType.Seeker, probe);

            Assert.IsNotNull(r);
            Assert.AreEqual(3.6f, r.Value.PerceivedRadius, 0.0001f);
        }

        // ── GAP-71: 수직 성분을 넣는다는 결정이 실제로 적용되는가 ────────

        [Test]
        public void PerceivedDistance_IncludesVerticalComponent()
        {
            // GAP-71 결정(DistanceMetric): 3D. 2층(Y +3.5m) 리스너가 수평으로는
            // 반경 안이지만 3D로는 밖인 경우가 실제로 탈락해야 한다.
            Assert.AreEqual(5f, Spatial.DistanceMetric.Perceived(
                Vector3.zero, new Vector3(3f, 4f, 0f)), 0.0001f,
                "수직 성분이 빠지면 3이 나온다 — 3D여야 5다.");

            var pulse = ValvePulse(Vector3.zero);
            var probe = new FakeOcclusionProbe(Classify(PhysicsOcclusionProbe.FloorSlabTag));

            // 수평 3.5m + 높이 3.5m = 3D 4.95m > 3.6m → 탈락.
            // 2D로 재면 3.5m ≤ 3.6m라 통과해 버린다.
            Assert.IsNull(SoundPulseResolver.Resolve(
                pulse, ListenerId, new Vector3(3.5f, 3.5f, 0f), RoleType.Seeker, probe),
                "2D로 재면 여기서 통과해 버린다 — 층간 ×0.25와 이중으로 어긋난다.");
        }
    }
}
