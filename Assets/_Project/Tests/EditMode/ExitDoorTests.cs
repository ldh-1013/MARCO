using Marco.Core.Objectives;
using Marco.Core.Sound;
using Marco.Presentation.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [10-02] 출구 문 — 발광 규칙(<see cref="EscapeDoorLook"/>)과 콜라이더 없는 문짝의 파문 윤곽(<see cref="RevealOutlineRegistry"/> →
    /// <see cref="PulseWallRevealer"/>).
    /// </summary>
    public class ExitDoorTests
    {
        private const float Eps = 1e-4f;

        // ── 발광 ─────────────────────────────────────────────────────────

        [Test]
        public void ClosedGate_NoEmission()
        {
            Assert.AreEqual(Color.black, EscapeDoorLook.PanelEmission(false), "닫힌 문은 스스로 빛나지 않는다 — 목소리 빛 · 파문으로만 보인다");
        }

        [Test]
        public void OpenGate_WeakGreenEmission()
        {
            Color c = EscapeDoorLook.PanelEmission(true);
            Assert.AreEqual(EscapeDoorLook.OpenEmission, c);
            Assert.Greater(c.g, c.r, "초록 계열");
            Assert.Greater(c.g, c.b, "초록 계열");
            Assert.Greater(c.g, 0.1f, "어둠 속에서 보일 만큼은 밝다");
            Assert.Less(c.maxColorComponent, 0.5f, "약한 발광 — 표면 색이지 조명이 아니다");
        }

        [Test]
        public void DoorSize_FitsUnderWallAndHumanSized()
        {
            Assert.AreEqual(2f, EscapeDoorLook.WidthMeters, Eps);
            Assert.AreEqual(2.5f, EscapeDoorLook.HeightMeters, Eps);
            Assert.Less(EscapeDoorLook.HeightMeters, 3.5f, "벽 높이(층 간격 3.5m)보다 낮다");
        }

        // ── 콜라이더 없는 상자의 최근접점 ─────────────────────────────────

        [Test]
        public void ClosestPointOnUnitBox_RotatedDoorPanel()
        {
            // 배수로 문: 벽 면 x 47.9, 방 안쪽 −x를 바라봄(+z 로컬 = −x 월드). 문짝 폭 2(로컬 x = 월드 z), 높이 2.5, 두께 0.04.
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(47.875f, 1.25f, 2.25f), Quaternion.LookRotation(Vector3.left),
                new Vector3(2f, 2.5f, 0.04f));

            Vector3 front = WallRevealTiming.ClosestPointOnUnitBox(m, new Vector3(44f, 1.25f, 2.25f));
            Assert.AreEqual(47.855f, front.x, Eps, "정면에서는 앞면(방 쪽 면)");
            Assert.AreEqual(2.25f, front.z, Eps);

            Vector3 side = WallRevealTiming.ClosestPointOnUnitBox(m, new Vector3(46f, 4f, 10f));
            Assert.AreEqual(3.25f, side.z, Eps, "폭 방향 끝(z 2.25 + 1)");
            Assert.AreEqual(2.5f, side.y, Eps, "위쪽 끝");

            Vector3 inside = new Vector3(47.87f, 1f, 2f);
            Assert.AreEqual(inside, WallRevealTiming.ClosestPointOnUnitBox(m, inside), "안쪽 점은 그대로");
        }

        // ── 파문 윤곽 ────────────────────────────────────────────────────

        [Test]
        public void Pulse_WithNoWallInRadius_StillOutlinesRegisteredDoor()
        {
            // 콜라이더가 하나도 없는 곳 — 전에는 OverlapSphere 0건이면 바로 끝나 문이 있어도 윤곽이 생기지 않았다.
            var parent = new GameObject("RevealParent");
            var door = new GameObject("DoorPanel");
            door.transform.SetPositionAndRotation(new Vector3(500f, 1.25f, 503f), Quaternion.LookRotation(Vector3.back));
            door.transform.localScale = new Vector3(2f, 2.5f, 0.04f);
            var material = new Material(Shader.Find("Sprites/Default"));
            RevealOutlineRegistry.Register(door.transform);
            try
            {
                var revealer = new PulseWallRevealer(parent.transform, material, 0.05f);
                revealer.Add(new PulseVisualState(1, PulseVisualKind.WorldRing, new Vector3(500f, 0f, 500f),
                    default(DirectionOctant), 9f, 1.2f, 0f));

                Assert.AreEqual(1, revealer.ActiveRevealCount, "반경 9m 안의 문짝(3m 앞)은 윤곽이 생긴다");
                Assert.AreEqual(6, parent.transform.childCount, "상자 윤곽 = 선 6개");

                revealer.Add(new PulseVisualState(2, PulseVisualKind.WorldRing, new Vector3(500f, 0f, 480f),
                    default(DirectionOctant), 9f, 1.2f, 0f));
                Assert.AreEqual(1, revealer.ActiveRevealCount, "반경 밖(23m)의 문짝은 그리지 않는다");

                RevealOutlineRegistry.Unregister(door.transform);
                revealer.Add(new PulseVisualState(3, PulseVisualKind.WorldRing, new Vector3(500f, 0f, 500f),
                    default(DirectionOctant), 9f, 1.2f, 0f));
                Assert.AreEqual(1, revealer.ActiveRevealCount, "등록을 뺀 문짝은 그리지 않는다");
                revealer.Clear();
            }
            finally
            {
                RevealOutlineRegistry.Unregister(door.transform);
                Object.DestroyImmediate(door);
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(material);
            }
        }
    }
}
