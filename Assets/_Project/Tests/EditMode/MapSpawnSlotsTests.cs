using System.Collections.Generic;
using Marco.Core.GameFlow;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 맵 도망자 스폰 슬롯 격자(10-01) — 4열 × 2행, 앵커 중심. 출구 거리 · 바닥 · 벽은 SpawnPointSceneTests가 실제 씬으로 본다.
    /// </summary>
    public class MapSpawnSlotsTests
    {
        private const float Eps = 1e-4f;
        private static readonly SpawnPose Anchor = new SpawnPose(new Vector3(7f, 0.05f, 34f), Quaternion.Euler(0f, 180f, 0f));

        [Test]
        public void EightSlots_FourColumnsTwoRows_CenteredOnAnchor()
        {
            var expected = new[]
            {
                new Vector3(3.85f, 0.05f, 34.75f), new Vector3(5.95f, 0.05f, 34.75f), new Vector3(8.05f, 0.05f, 34.75f), new Vector3(10.15f, 0.05f, 34.75f),
                new Vector3(3.85f, 0.05f, 33.25f), new Vector3(5.95f, 0.05f, 33.25f), new Vector3(8.05f, 0.05f, 33.25f), new Vector3(10.15f, 0.05f, 33.25f),
            };

            var center = Vector3.zero;
            for (int i = 0; i < 8; i++)
            {
                SpawnPose p = MapSpawnSlots.GetPose(Anchor, i, 8);
                Assert.AreEqual(expected[i].x, p.Position.x, Eps, $"슬롯 {i} x");
                Assert.AreEqual(expected[i].y, p.Position.y, Eps, $"슬롯 {i} 높이 = 앵커 높이");
                Assert.AreEqual(expected[i].z, p.Position.z, Eps, $"슬롯 {i} z");
                Assert.AreEqual(Anchor.Rotation, p.Rotation, "회전 = 앵커 회전");
                center += p.Position;
            }

            center /= 8f;
            Assert.AreEqual(Anchor.Position.x, center.x, Eps, "격자 중심 = 앵커");
            Assert.AreEqual(Anchor.Position.z, center.z, Eps);
        }

        [Test]
        public void SlotNumbers_WrapLikePlayerIdModulo()
        {
            Assert.AreEqual(MapSpawnSlots.GetPose(Anchor, 2, 8).Position, MapSpawnSlots.GetPose(Anchor, 10, 8).Position, "PlayerId 10 → 슬롯 2");
            Assert.AreEqual(MapSpawnSlots.GetPose(Anchor, 7, 8).Position, MapSpawnSlots.GetPose(Anchor, -1, 8).Position, "음수도 되돌아온다");
        }

        [Test]
        public void Slots_AreDistinct_AndAtLeastMinSpacingApart()
        {
            var points = new List<Vector3>();
            for (int i = 0; i < 8; i++)
                points.Add(MapSpawnSlots.GetPose(Anchor, i, 8).Position);

            float min = float.MaxValue;
            for (int i = 0; i < points.Count; i++)
            for (int j = i + 1; j < points.Count; j++)
                min = Mathf.Min(min, Vector3.Distance(points[i], points[j]));

            Assert.AreEqual(MapSpawnSlots.MinSpacing(8), min, Eps, "가장 가까운 두 슬롯 = 줄 간격");
            Assert.Greater(min, 1.2f, "태그 반경(1.2m)보다 넓다");
        }

        [Test]
        public void OneSlot_IsTheAnchor()
        {
            Assert.AreEqual(Anchor.Position, MapSpawnSlots.GetPose(Anchor, 5, 1).Position);
        }
    }
}
