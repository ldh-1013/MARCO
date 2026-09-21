using Marco.Core.Breath;
using Marco.Core.Locomotion;
using Marco.Core.Water;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §10.1 수면 영역 조회와 §4.3·§5.9-1 잠수/숨 상태 판정을 고정한다.
    ///
    /// <para>
    /// <c>WaterVolumeBehaviour</c>(MonoBehaviour)는 여기서 쓰지 않는다 — 대신
    /// <see cref="IWaterVolume"/> 테스트 대역을 쓴다. 인터페이스를 둔 이유가 이것이고,
    /// 좌표 판정 규칙이 Unity 수명주기 없이 검증된다.
    /// </para>
    /// </summary>
    public class WaterVolumeTests
    {
        /// <summary>§10.1 메인 풀 (21,17)~(35,25), 수면 Y=0, 깊은쪽 3.5m.</summary>
        private sealed class FakeVolume : IWaterVolume
        {
            private readonly Vector2 _min, _max;
            private readonly float _depth, _sumpDepth, _sumpRadius;
            private readonly Vector2 _sumpCenter;

            public FakeVolume(Vector2 min, Vector2 max, float surfaceY, float depth,
                Vector2 sumpCenter = default, float sumpRadius = 0f, float sumpDepth = 0f)
            {
                _min = min;
                _max = max;
                SurfaceY = surfaceY;
                _depth = depth;
                _sumpCenter = sumpCenter;
                _sumpRadius = sumpRadius;
                _sumpDepth = sumpDepth;
            }

            public float SurfaceY { get; }

            public bool ContainsHorizontally(Vector3 p) =>
                p.x >= _min.x && p.x <= _max.x && p.z >= _min.y && p.z <= _max.y;

            public float BedYAt(Vector3 p)
            {
                if (_sumpRadius > 0f)
                {
                    float dx = p.x - _sumpCenter.x, dz = p.z - _sumpCenter.y;
                    if (dx * dx + dz * dz <= _sumpRadius * _sumpRadius)
                        return SurfaceY - Mathf.Max(_depth, _sumpDepth);
                }

                return SurfaceY - _depth;
            }
        }

        private static FakeVolume MainPool() =>
            new FakeVolume(new Vector2(21f, 17f), new Vector2(35f, 25f), surfaceY: 0f, depth: 3.5f);

        /// <summary>§10.1 유아풀 (3.5,6.5)~(12.5,12.5) + §6.5-2 배수구 (8, 9.5) 침강부 3.5m.</summary>
        private static FakeVolume KiddiePool(float shallowDepth) =>
            new FakeVolume(new Vector2(3.5f, 6.5f), new Vector2(12.5f, 12.5f), surfaceY: 0f,
                depth: shallowDepth, sumpCenter: new Vector2(8f, 9.5f), sumpRadius: 1f, sumpDepth: 3.5f);

        [SetUp]
        public void ClearRegistry()
        {
            WaterVolumeRegistry.ResetForNewSession();
        }

        // ── 수면 조회 ────────────────────────────────────────────────────

        [Test]
        public void Sample_OutsideHorizontally_IsOutOfWater()
        {
            WaterVolumeRegistry.Register(MainPool());

            // 덱 위 (19, 21) — 구역 안이지만 수면 밖(§10.1 서쪽 덱 3m).
            Assert.IsFalse(WaterVolumeRegistry.Sample(new Vector3(19f, -3.5f, 21f)).BodyInWater);
        }

        [Test]
        public void Sample_AboveSurface_IsOutOfWater()
        {
            WaterVolumeRegistry.Register(MainPool());

            // 수면 위에 떠 있는(=덱 높이) 발 — 물 안이 아니다.
            Assert.IsFalse(WaterVolumeRegistry.Sample(new Vector3(28f, 0f, 21f)).BodyInWater,
                "발이 정확히 수면 높이면 물 밖이다(§5.9-1 '콜라이더가 물 볼륨과 완전 분리').");
        }

        [Test]
        public void Sample_InsidePool_ReportsSurfaceAndBed()
        {
            WaterVolumeRegistry.Register(MainPool());

            WaterSample s = WaterVolumeRegistry.Sample(new Vector3(28f, -3.5f, 21f));

            Assert.IsTrue(s.BodyInWater);
            Assert.AreEqual(0f, s.SurfaceY, 0.0001f);
            Assert.AreEqual(-3.5f, s.BedY, 0.0001f);
            Assert.AreEqual(3.5f, s.Depth, 0.0001f);
        }

        [Test]
        public void Sample_AtKiddieSump_ReportsThreePointFive()
        {
            // §6.5-2 "유아풀은 얕은 풀이므로 배수구 지점만 국소 침강부(sump)로 판다".
            WaterVolumeRegistry.Register(KiddiePool(shallowDepth: 0.9f));

            Assert.AreEqual(3.5f, WaterVolumeRegistry.Sample(new Vector3(8f, -3.5f, 9.5f)).Depth, 0.0001f,
                "배수구 2 지점은 3.5m여야 §6.5-3의 공정성 논거가 성립한다.");
            Assert.AreEqual(0.9f, WaterVolumeRegistry.Sample(new Vector3(11f, -0.5f, 11f)).Depth, 0.0001f,
                "침강부 밖은 얕은 수심 그대로다.");
        }

        [Test]
        public void Sample_TwoPools_DoNotLeakIntoEachOther()
        {
            WaterVolumeRegistry.Register(MainPool());
            WaterVolumeRegistry.Register(KiddiePool(0.9f));

            Assert.AreEqual(3.5f, WaterVolumeRegistry.Sample(new Vector3(28f, -1f, 21f)).Depth, 0.0001f);
            Assert.AreEqual(0.9f, WaterVolumeRegistry.Sample(new Vector3(11f, -0.5f, 11f)).Depth, 0.0001f);
        }

        [Test]
        public void ResetForNewSession_DropsStaleVolumes()
        {
            // 도메인 리로드를 끈 채 Play를 반복하면 파괴된 볼륨이 남는다.
            WaterVolumeRegistry.Register(MainPool());
            Assert.AreEqual(1, WaterVolumeRegistry.Count);

            WaterVolumeRegistry.ResetForNewSession();

            Assert.AreEqual(0, WaterVolumeRegistry.Count);
            Assert.IsFalse(WaterVolumeRegistry.Sample(new Vector3(28f, -1f, 21f)).BodyInWater);
        }

        // ── §4.3 잠수 진입 조건 (판정이 한 곳인지) ───────────────────────

        [TestCase(true, true, true, true)]     // 누름 + 물속 + 숨 있음 → 잠수
        [TestCase(false, true, true, false)]   // 안 누름
        [TestCase(true, false, true, false)]   // §4.3 "수면 위에서만" — 마른 땅에서는 불가
        [TestCase(true, true, false, false)]   // §5.9-1 "강제 부상" — 숨 0
        public void IsDiving_MatchesDesignDoc(bool held, bool inWater, bool canSubmerge, bool expected)
        {
            Assert.AreEqual(expected, DiveRules.IsDiving(held, inWater, canSubmerge));
        }

        [Test]
        public void LocomotionSimulator_UsesTheSameDivePredicate()
        {
            // 판정이 두 곳에 있지 않다는 실증 — 시뮬레이터의 Diving 진입이
            // DiveRules.IsDiving과 모든 조합에서 일치해야 한다.
            foreach (bool held in new[] { false, true })
            foreach (bool inWater in new[] { false, true })
            foreach (bool canSubmerge in new[] { false, true })
            {
                var sim = new LocomotionSimulator(Role.RoleType.Runner);
                var input = new LocomotionInput(new Vector2(0f, 1f),
                    sprintHeld: false, diveHeld: held, isOnWaterSurface: inWater, canSubmerge: canSubmerge);

                bool diving = sim.Tick(input, 0.02f).State == MovementState.Diving;

                Assert.AreEqual(DiveRules.IsDiving(held, inWater, canSubmerge), diving,
                    $"held={held} inWater={inWater} canSubmerge={canSubmerge}");
            }
        }

        // ── §5.9-1 숨 상태 판정 (서버가 쓰는 지오메트리 형태) ────────────

        [Test]
        public void ZoneOf_OutOfWater_WhenNotInAnyVolume()
        {
            var dry = WaterSample.OutOfWater;
            Assert.AreEqual(BreathZone.OutOfWater, DiveRules.ZoneOf(dry, 0f, diving: true),
                "잠수 키를 눌러도 물이 없으면 물 밖이다.");
        }

        [Test]
        public void ZoneOf_Surface_WhenHeadAboveWater()
        {
            WaterVolumeRegistry.Register(MainPool());
            WaterSample s = WaterVolumeRegistry.Sample(new Vector3(28f, -3.5f, 21f));

            // 바닥에 서 있음: 발 -3.5 + 머리 1.62 = -1.88 < 수면 0 → 사실은 이미 잠긴다.
            // 그래서 "수면"을 재려면 발이 수면에 가까워야 한다(깊은 물에서 헤엄치는 상태).
            Assert.AreEqual(BreathZone.Surface,
                DiveRules.ZoneOf(s, feetY: -1f, diving: false),
                "발 -1 + 머리 1.62 = 0.62 > 수면 0 → 머리는 물 위.");
        }

        [Test]
        public void ZoneOf_Submerged_WhenDivingLowersTheHead()
        {
            WaterVolumeRegistry.Register(MainPool());
            WaterSample s = WaterVolumeRegistry.Sample(new Vector3(28f, -3.5f, 21f));

            // 같은 발 높이에서 잠수만 켠다 — 머리 1.62 → 0.5로 내려가 수면 아래가 된다.
            Assert.AreEqual(BreathZone.Surface, DiveRules.ZoneOf(s, feetY: -1f, diving: false));
            Assert.AreEqual(BreathZone.Submerged, DiveRules.ZoneOf(s, feetY: -1f, diving: true),
                "§5.9-1 '카메라(머리)가 수면 아래' — 발이 아니라 머리가 내려간다.");
        }

        [Test]
        public void MinDivableDepth_IsDerivedFromSubmergedHeadHeight()
        {
            // 바닥에 선 잠수자의 머리는 (수면 - 수심 + 0.5)이므로 필요 수심은 0.5m 초과다.
            // 상수로 따로 적지 않고 머리 높이를 가리킨다(더블체크 8).
            Assert.AreEqual(DiveRules.SubmergedHeadHeight, DiveRules.MinDivableDepth, 0.0001f);
            Assert.AreEqual(0.5f, DiveRules.MinDivableDepth, 0.0001f);

            // 반대쪽 경계 — 이 이상 깊으면 서 있기만 해도 잠긴다.
            Assert.Less(DiveRules.MinDivableDepth, DiveRules.StandingHeadHeight,
                "'서서 잠수로만 잠기는' 구간이 존재해야 유아풀이 성립한다(GAP-75).");
        }

        [Test]
        public void ShallowWater_CannotSubmerge_EvenWhileHoldingDive()
        {
            // 잠수가 가능한 최소 수심(0.5m)보다 얕으면, 잠수 키를 눌러도 머리가 수면 위에
            // 남아 Surface가 된다. 이것이 "얕은 물에서는 잠길 수 없다"의 올바른 표현이며,
            // 유아풀 본체 수심이 미확정인 한 밸브 E의 잠수 성립 여부도 미확정이다(GAP-75).
            float tooShallow = DiveRules.MinDivableDepth - 0.1f;
            WaterVolumeRegistry.Register(KiddiePool(shallowDepth: tooShallow));

            var feet = new Vector3(11f, -tooShallow, 11f); // 바닥에 서 있다
            WaterSample s = WaterVolumeRegistry.Sample(feet);

            Assert.IsTrue(s.BodyInWater);
            Assert.AreEqual(BreathZone.Surface, DiveRules.ZoneOf(s, feet.y, diving: true));
        }

        [Test]
        public void DivableDepth_Boundary_JustDeepEnough()
        {
            // 경계값: 정확히 0.5m면 머리가 수면과 같은 높이라 아직 잠기지 않는다.
            // 조금이라도 깊으면 잠긴다.
            WaterVolumeRegistry.Register(KiddiePool(shallowDepth: DiveRules.MinDivableDepth));
            var atBoundary = new Vector3(11f, -DiveRules.MinDivableDepth, 11f);
            Assert.AreEqual(BreathZone.Surface,
                DiveRules.ZoneOf(WaterVolumeRegistry.Sample(atBoundary), atBoundary.y, diving: true),
                "머리가 정확히 수면 높이면 아직 물 위다('< SurfaceY'가 조건).");

            WaterVolumeRegistry.ResetForNewSession();
            WaterVolumeRegistry.Register(KiddiePool(shallowDepth: DiveRules.MinDivableDepth + 0.01f));
            var justDeeper = new Vector3(11f, -(DiveRules.MinDivableDepth + 0.01f), 11f);
            Assert.AreEqual(BreathZone.Submerged,
                DiveRules.ZoneOf(WaterVolumeRegistry.Sample(justDeeper), justDeeper.y, diving: true));
        }

        [Test]
        public void ZoneOf_GeometricForm_AgreesWithStateForm()
        {
            // 상태표가 두 벌이 아니라는 실증 — 지오메트리 형태와 MovementState 형태가
            // 모든 조합에서 같은 답을 내야 한다.
            WaterVolumeRegistry.Register(MainPool());
            WaterSample wet = WaterVolumeRegistry.Sample(new Vector3(28f, -3.5f, 21f));

            Assert.AreEqual(BreathConfig.ZoneOf(MovementState.Diving, true),
                DiveRules.ZoneOf(wet, -1f, diving: true));
            Assert.AreEqual(BreathConfig.ZoneOf(MovementState.Walk, true),
                DiveRules.ZoneOf(wet, -1f, diving: false));
            Assert.AreEqual(BreathConfig.ZoneOf(MovementState.Idle, false),
                DiveRules.ZoneOf(WaterSample.OutOfWater, 0f, diving: false));
        }
    }
}
