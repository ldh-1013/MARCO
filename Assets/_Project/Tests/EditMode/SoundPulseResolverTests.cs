using NUnit.Framework;
using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// docs/phase-1-분석.md T5 수용 기준: 벽 0/1/2개, 하드블로커, 역할 배율 조합을
    /// Physics 없이 검증한다. §9 밸런스 수치가 여기서 코드로 고정된다.
    /// </summary>
    public class SoundPulseResolverTests
    {
        private const ulong SourceId = 1;
        private const ulong ListenerId = 2;

        private static SoundPulse MakeShout(Vector3 position)
        {
            // §5.1 "고함" 등급: 반경 22m, 지속 2.5초
            return new SoundPulse(SourceId, position, 22f, 2.5f, SoundType.Shout, timestamp: 0f);
        }

        private class FakeOcclusionProbe : IOcclusionProbe
        {
            private readonly OcclusionResult _result;
            public FakeOcclusionProbe(OcclusionResult result) => _result = result;
            public OcclusionResult Probe(Vector3 from, Vector3 to) => _result;
        }

        // 1) GAP-1: 본인 발생 펄스는 리스너 ID가 같으면 무조건 null.
        [Test]
        public void Resolve_SelfListener_ReturnsNull()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 0));

            var result = SoundPulseResolver.Resolve(pulse, SourceId, new Vector3(1, 0, 0), RoleType.Runner, probe);

            Assert.IsNull(result);
        }

        // 2) 벽 0개, 러너, 반경 내 → 통과, 배율 없음, 좌표 공개.
        [Test]
        public void Resolve_NoWalls_Runner_WithinRadius_Passes()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 0));
            var listenerPos = new Vector3(10f, 0, 0); // 22m 반경 이내

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Runner, probe);

            Assert.IsNotNull(result);
            Assert.AreEqual(22f, result.Value.PerceivedRadius, 0.001f);
            Assert.AreEqual(2.5f, result.Value.PerceivedDuration, 0.001f);
            Assert.IsTrue(result.Value.WorldSpaceRingVisible);
            Assert.IsTrue(result.Value.SourcePos.HasValue);
        }

        // 3) 벽 0개, 술래 → 반경×1.2, 지속×1.5 (§5.7).
        [Test]
        public void Resolve_NoWalls_Seeker_AppliesPerceptionMultiplier()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 0));
            var listenerPos = new Vector3(25f, 0, 0); // 22m은 넘지만 22*1.2=26.4m 이내

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Seeker, probe);

            Assert.IsNotNull(result);
            Assert.AreEqual(26.4f, result.Value.PerceivedRadius, 0.001f);
            Assert.AreEqual(3.75f, result.Value.PerceivedDuration, 0.001f);
        }

        // 4) 벽 0개, 메아리 → 러너와 동일(×1.0), §5.7 "관전 목적, 동일 적용".
        [Test]
        public void Resolve_NoWalls_Echo_UsesDefaultMultiplier()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 0));
            var listenerPos = new Vector3(10f, 0, 0);

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Echo, probe);

            Assert.IsNotNull(result);
            Assert.AreEqual(22f, result.Value.PerceivedRadius, 0.001f);
            Assert.AreEqual(2.5f, result.Value.PerceivedDuration, 0.001f);
        }

        // 5) 배율 적용 전부터 반경 밖 → 1차 컷으로 즉시 null (레이캐스트 자체가 불필요).
        [Test]
        public void Resolve_BeyondBaseRadius_ReturnsNullWithoutOcclusionCheck()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 0));
            var listenerPos = new Vector3(100f, 0, 0); // 22*1.2=26.4m도 훌쩍 넘음

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Seeker, probe);

            Assert.IsNull(result);
        }

        // 6) 벽 1개, 반경 내 → 반경×0.5, 지속×max(0.5,0.5)=0.5, 좌표 비공개(방향만).
        [Test]
        public void Resolve_OneWall_AttenuatesRadiusAndHidesPosition()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 1));
            var listenerPos = new Vector3(10f, 0, 0); // 22*0.5=11m 이내

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Runner, probe);

            Assert.IsNotNull(result);
            Assert.AreEqual(11f, result.Value.PerceivedRadius, 0.001f);
            Assert.AreEqual(1.25f, result.Value.PerceivedDuration, 0.001f);
            Assert.IsFalse(result.Value.WorldSpaceRingVisible);
            Assert.IsFalse(result.Value.SourcePos.HasValue);
        }

        // 7) 벽 1개, 감쇠된 반경 밖(원본 반경 안) → 2차 컷으로 null.
        [Test]
        public void Resolve_OneWall_BeyondAttenuatedRadius_ReturnsNull()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 1));
            var listenerPos = new Vector3(15f, 0, 0); // 22m 이내지만 22*0.5=11m는 넘음

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Runner, probe);

            Assert.IsNull(result);
        }

        // 8) 벽 2개 → 반경×0.25(중첩 감쇠), 단 지속시간은 최소 배율 0.5로 바닥(§5.6 "최대 50%까지만 깎임").
        [Test]
        public void Resolve_TwoWalls_DurationFlooredAtHalf()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 2));
            var listenerPos = new Vector3(5f, 0, 0); // 22*0.25=5.5m 이내

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Runner, probe);

            Assert.IsNotNull(result);
            Assert.AreEqual(5.5f, result.Value.PerceivedRadius, 0.001f);
            Assert.AreEqual(1.25f, result.Value.PerceivedDuration, 0.001f); // 2.5 * max(0.25, 0.5) = 1.25, 0.625 아님
        }

        // 9) 벽 2개, 감쇠 반경 밖 → null.
        [Test]
        public void Resolve_TwoWalls_BeyondAttenuatedRadius_ReturnsNull()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 2));
            var listenerPos = new Vector3(6f, 0, 0); // 22*0.25=5.5m를 넘음

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Runner, probe);

            Assert.IsNull(result);
        }

        // 10) 하드블로커(무대 커퍼 등) → 벽 개수·거리와 무관하게 항상 null.
        [Test]
        public void Resolve_HardBlocker_AlwaysReturnsNull()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(true, 0));
            var listenerPos = new Vector3(1f, 0, 0); // 코앞이어도 차단

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Seeker, probe);

            Assert.IsNull(result);
        }

        // 11) 경계값: straightDist == perceivedRadius → 통과(> 비교이므로 경계 포함).
        [Test]
        public void Resolve_ExactlyAtPerceivedRadius_Passes()
        {
            var pulse = MakeShout(Vector3.zero);
            var probe = new FakeOcclusionProbe(new OcclusionResult(false, 0));
            var listenerPos = new Vector3(22f, 0, 0); // 정확히 22m

            var result = SoundPulseResolver.Resolve(pulse, ListenerId, listenerPos, RoleType.Runner, probe);

            Assert.IsNotNull(result);
        }

        // 12) 방향 8방위 스냅: N/E/S/W 및 대각선이 올바르게 계산되는가 (§3.4).
        [TestCase(0f, 1f, DirectionOctant.N)]
        [TestCase(1f, 1f, DirectionOctant.NE)]
        [TestCase(1f, 0f, DirectionOctant.E)]
        [TestCase(1f, -1f, DirectionOctant.SE)]
        [TestCase(0f, -1f, DirectionOctant.S)]
        [TestCase(-1f, -1f, DirectionOctant.SW)]
        [TestCase(-1f, 0f, DirectionOctant.W)]
        [TestCase(-1f, 1f, DirectionOctant.NW)]
        public void ComputeOctant_SnapsToNearestEighth(float dx, float dz, DirectionOctant expected)
        {
            var sourcePos = new Vector3(dx, 0, dz);
            var listenerPos = Vector3.zero;

            var actual = SoundPulseResolver.ComputeOctant(sourcePos, listenerPos);

            Assert.AreEqual(expected, actual);
        }
    }
}
