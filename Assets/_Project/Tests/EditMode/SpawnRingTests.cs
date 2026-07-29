using Marco.Core.GameFlow;
using Marco.Core.Tagging;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 18b 후속 실기 버그("술래와 러너가 같은 위치에 스폰돼 즉시 SeekerWin") 회귀 방지.
    ///
    /// Quaternion/Vector3의 **순수 생성자만** 쓴다 — 이 하네스는 Unity 런타임 없이 도므로
    /// Quaternion.Euler 같은 네이티브 호출은 쓸 수 없다(스프린트 18b에서 확인).
    /// </summary>
    public class SpawnRingTests
    {
        private static readonly SpawnPose Anchor =
            new SpawnPose(new Vector3(12f, 0.05f, 27f), new Quaternion(0f, 1f, 0f, 0f));

        [Test]
        public void AdjacentSlots_AreFartherApartThanTagRadius()
        {
            // 이 테스트가 실기 버그의 핵심 조건이다 — 인접 슬롯이 태그 반경 안에 있으면
            // 시작 즉시 태그가 성립해 라운드가 끝난다.
            float spacing = SpawnRing.AdjacentSpacing();

            Assert.Greater(spacing, TagRules.TagRadiusMeters,
                "인접 슬롯 간격이 태그 반경보다 커야 시작 즉시 태그되지 않는다.");
        }

        [Test]
        public void EveryPairOfSlots_IsOutsideTagRadius()
        {
            for (int a = 0; a < SpawnRing.DefaultSlots; a++)
            {
                for (int b = a + 1; b < SpawnRing.DefaultSlots; b++)
                {
                    Vector3 pa = SpawnRing.GetPose(Anchor, a).Position;
                    Vector3 pb = SpawnRing.GetPose(Anchor, b).Position;

                    Assert.IsFalse(TagRules.IsWithinTagRange(pa, pb),
                        $"슬롯 {a}와 {b}가 태그 반경 안에 있다.");
                }
            }
        }

        [Test]
        public void AllSlots_AreExactlyRadiusFromAnchor()
        {
            for (int i = 0; i < SpawnRing.DefaultSlots; i++)
            {
                Vector3 p = SpawnRing.GetPose(Anchor, i).Position;
                float dx = p.x - Anchor.Position.x;
                float dz = p.z - Anchor.Position.z;
                float horizontal = Mathf.Sqrt(dx * dx + dz * dz);

                Assert.AreEqual(SpawnRing.DefaultRadiusMeters, horizontal, 0.001f);
            }
        }

        [Test]
        public void PreservesAnchorHeightAndRotation()
        {
            SpawnPose pose = SpawnRing.GetPose(Anchor, 3);

            Assert.AreEqual(Anchor.Position.y, pose.Position.y, 0.0001f, "높이는 앵커 값을 유지해야 한다.");
            Assert.AreEqual(Anchor.Rotation, pose.Rotation, "회전은 맵이 정한 방향을 유지해야 한다.");
        }

        [Test]
        public void IndexWrapsAround_AndIsDeterministic()
        {
            Assert.AreEqual(SpawnRing.GetPose(Anchor, 0).Position,
                            SpawnRing.GetPose(Anchor, SpawnRing.DefaultSlots).Position);

            Assert.AreEqual(SpawnRing.GetPose(Anchor, 2).Position,
                            SpawnRing.GetPose(Anchor, 2).Position);
        }

        [Test]
        public void NegativeIndex_IsNormalized()
        {
            Vector3 negative = SpawnRing.GetPose(Anchor, -1).Position;
            Vector3 equivalent = SpawnRing.GetPose(Anchor, SpawnRing.DefaultSlots - 1).Position;

            Assert.AreEqual(equivalent, negative);
        }

        [Test]
        public void ZeroRadius_ReturnsAnchorUnchanged()
        {
            SpawnPose pose = SpawnRing.GetPose(Anchor, 5, radiusMeters: 0f);

            Assert.AreEqual(Anchor.Position, pose.Position);
        }

        [Test]
        public void SingleSlot_ReturnsAnchorUnchanged()
        {
            SpawnPose pose = SpawnRing.GetPose(Anchor, 5, slots: 1);

            Assert.AreEqual(Anchor.Position, pose.Position);
        }

        [Test]
        public void AdjacentSpacing_IsZeroWhenDistributionDisabled()
        {
            Assert.AreEqual(0f, SpawnRing.AdjacentSpacing(radiusMeters: 0f), 0.0001f);
            Assert.AreEqual(0f, SpawnRing.AdjacentSpacing(slots: 1), 0.0001f);
        }
    }
}
