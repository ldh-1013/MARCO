using Marco.Core.Sound;
using Marco.Presentation.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// "말하면 보인다" 벽 윤곽 리빌 — 시간 규칙과 상자 모서리. 반경 · 지속은 §5.1(ServerPulseDriver) 값을 그대로 쓴다.
    /// </summary>
    public class PulseWallRevealTests
    {
        private const float Eps = 1e-4f;

        // ── 도달 시각 = 확장 링이 그 거리에 닿는 순간 ─────────────────────

        [Test]
        public void Arrival_MatchesRingExpansion()
        {
            // 대화 9m / 1.2초 — 4.5m 떨어진 벽은 링이 절반 퍼진 0.6초에 닿는다.
            Assert.AreEqual(10.6f, WallRevealTiming.ArrivalTime(10f, 1.2f, 9f, 4.5f), Eps);
            Assert.AreEqual(10f, WallRevealTiming.ArrivalTime(10f, 1.2f, 9f, 0f), Eps, "발생 지점에 붙은 벽은 즉시");
            Assert.AreEqual(11.2f, WallRevealTiming.ArrivalTime(10f, 1.2f, 9f, 9f), Eps, "반경 끝 = 링 소멸 시각");
            Assert.AreEqual(11.2f, WallRevealTiming.ArrivalTime(10f, 1.2f, 9f, 20f), Eps, "반경 밖은 소멸 시각으로 고정");
        }

        [Test]
        public void Arrival_DiffersBySoundType_FromSection51Table()
        {
            // 같은 3m 거리 — §5.1 표의 종류별 반경 · 지속이 그대로 도달 시각 차이가 된다.
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Walk, out float walkR, out float walkT));
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Talk, out float talkR, out float talkT));
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Shout, out float shoutR, out float shoutT));

            float walk = WallRevealTiming.ArrivalTime(0f, walkT, walkR, 3f);   // 2m — 3m 벽은 반경 밖
            float talk = WallRevealTiming.ArrivalTime(0f, talkT, talkR, 3f);   // 9m / 1.2초
            float shout = WallRevealTiming.ArrivalTime(0f, shoutT, shoutR, 3f); // 22m / 2.5초

            Assert.AreEqual(walkT, walk, Eps, "걷기 2m 파문은 3m 벽에 닿지 않는다(소멸 시각 = 밝기 0)");
            Assert.AreEqual(0f, WallRevealTiming.Alpha(walk, 0f, walkT, walk), Eps);
            Assert.AreEqual(1.2f * 3f / 9f, talk, Eps);
            Assert.AreEqual(2.5f * 3f / 22f, shout, Eps);
        }

        // ── 밝기 = 링이 닿은 뒤 링의 밝기 곡선(1 − 진행도) ──────────────────

        [Test]
        public void Alpha_ZeroBeforeArrival_RingAlphaAfter_ZeroAtEnd()
        {
            const float start = 5f, duration = 1.2f;
            float arrival = WallRevealTiming.ArrivalTime(start, duration, 9f, 4.5f); // 5.6

            Assert.AreEqual(0f, WallRevealTiming.Alpha(5.59f, start, duration, arrival), Eps, "링이 닿기 전에는 보이지 않는다");
            Assert.AreEqual(0.5f, WallRevealTiming.Alpha(arrival, start, duration, arrival), Eps, "닿는 순간 = 그때 링의 밝기");
            Assert.AreEqual(0.25f, WallRevealTiming.Alpha(5.9f, start, duration, arrival), Eps, "링과 함께 선형 감쇠");
            Assert.AreEqual(0f, WallRevealTiming.Alpha(start + duration, start, duration, arrival), Eps, "링 소멸과 함께 사라진다");
        }

        [Test]
        public void Alpha_ZeroDuration_NeverVisible()
        {
            Assert.AreEqual(0f, WallRevealTiming.Alpha(1f, 0f, 0f, 0f), Eps);
        }

        // ── 상자 모서리 ───────────────────────────────────────────────

        [Test]
        public void BoxCorners_Identity_FaceOrderAndInflate()
        {
            var corners = new Vector3[8];
            WallRevealTiming.BoxCorners(Matrix4x4.identity, Vector3.zero, new Vector3(2f, 4f, 6f), Vector3.one, 0f, corners);

            Assert.AreEqual(new Vector3(-1f, -2f, -3f), corners[0]);
            Assert.AreEqual(new Vector3(1f, -2f, -3f), corners[1]);
            Assert.AreEqual(new Vector3(1f, 2f, -3f), corners[2]);
            Assert.AreEqual(new Vector3(-1f, 2f, -3f), corners[3]);
            Assert.AreEqual(new Vector3(-1f, -2f, 3f), corners[4]);
            Assert.AreEqual(new Vector3(-1f, 2f, 3f), corners[7]);

            WallRevealTiming.BoxCorners(Matrix4x4.identity, Vector3.zero, new Vector3(2f, 4f, 6f), Vector3.one, 0.03f, corners);
            Assert.AreEqual(-1.03f, corners[0].x, Eps, "선 반폭만큼 바깥으로");
            Assert.AreEqual(3.03f, corners[4].z, Eps);
        }

        [Test]
        public void BoxCorners_ScaledTransform_MatchesMapWall()
        {
            // 맵 v2 벽 차폐 자식: BoxCollider(중심 0, 크기 1) · 월드 크기 = 벽 크기(길이 18 · 높이 3.5 · 두께 0.2).
            var scale = new Vector3(18f, 3.5f, 0.2f);
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(27f, 1.75f, 15f), Quaternion.identity, scale);
            var corners = new Vector3[8];
            WallRevealTiming.BoxCorners(m, Vector3.zero, Vector3.one, scale, 0.03f, corners);

            Assert.AreEqual(27f - 9f - 0.03f, corners[0].x, Eps);
            Assert.AreEqual(0f - 0.03f, corners[0].y, Eps, "바닥 높이 − 반폭");
            Assert.AreEqual(3.5f + 0.03f, corners[2].y, Eps, "벽 윗면 + 반폭");
            Assert.AreEqual(15f + 0.1f + 0.03f, corners[4].z, Eps, "두께 방향도 월드 m 기준으로 부푼다");
        }
    }
}
