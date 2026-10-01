using Marco.Core.Breath;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Water;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [커밋 전 수정 1] §6.1 [v0.4] 수중 밸브 B·E — 잠수 구간 = 진입 시작 ~ 부상 완료(GAP-91 해소).
    /// 실제 <see cref="Valve"/> · <see cref="BreathGauge"/> · <see cref="UnderwaterWorkSession"/>을 서버와 같은 순서로 엮는다.
    /// </summary>
    public class UnderwaterWorkSessionTests
    {
        private const float Eps = 1e-3f;
        private const float Dt = 0.5f; // 2진 정확값 — B 10틱 · E 14틱에 정확히 열린다
        private const ulong Me = 7;

        private struct JobResult
        {
            public float SubmergedSeconds;
            public bool Opened;
            public BreathGauge Gauge;
        }

        /// <summary>
        /// 끊김 없는 작업 1회를 서버 순서대로 돌린다: 숨 판정(세션이 살아 있으면 잠수) → 밸브 틱 → 세션 틱 → 전이 반영.
        /// </summary>
        private static JobResult RunFullJob(ValveId id, BreathGauge gauge = null)
        {
            var valve = new Valve(ValveOccupancy.RotateSeconds(id));
            valve.Configure(id);
            gauge ??= new BreathGauge();
            var session = new UnderwaterWorkSession(id);
            float submerged = 0f;

            for (int step = 0; step < 100 && session.KeepsSubmerged; step++)
            {
                gauge.Tick(BreathZone.Submerged, Dt);   // 세션이 살아 있는 동안 = 잠수(머리가 잠기는 수심 전제)
                submerged += Dt;

                if (session.Phase == UnderwaterWorkPhase.Rotate)
                    valve.Tick(Dt);

                UnderwaterWorkTick t = session.Tick(Dt, holding: true, rotationFinished: valve.State == ValveState.Open,
                    canSubmerge: gauge.CanSubmerge);
                if (t.BeginRotation)
                    Assert.AreEqual(ValveInteractionRejection.None, valve.TryInteract(Me, RoleType.Runner));
                if (t.StopRotation)
                    valve.Interrupt(Me);
            }

            return new JobResult { SubmergedSeconds = submerged, Opened = valve.State == ValveState.Open, Gauge = gauge };
        }

        [TestCase(ValveId.B)]
        [TestCase(ValveId.E)]
        public void FullJob_SubmergedForTotalOccupancy_8Seconds(ValveId id)
        {
            // 새 상수 없음 — 블록 2의 "총 점유 8.0"(진입 + 회전 + 부상)을 그대로 쓴다.
            JobResult r = RunFullJob(id);
            Assert.IsTrue(r.Opened);
            Assert.AreEqual(ValveOccupancy.TotalSeconds(id), r.SubmergedSeconds, Eps);
            Assert.AreEqual(8f, r.SubmergedSeconds, Eps);
        }

        [TestCase(ValveId.B)]
        [TestCase(ValveId.E)]
        public void ShoutRightAfterJob_Residual6_CanSuppressOnlyOnce(ValveId id)
        {
            // §6.1 "숨 게이지 8.0초를 소모하고 부상". 10-02 총량 14초 → 잔여 6.0(v0.4 12초 4.0 · 10-01 20초 12.0).
            // 억제 4.5는 한 번 가능하고 두 번째는 안 된다(6.0 − 4.5 = 1.5 < 4.5).
            JobResult r = RunFullJob(id);
            Assert.AreEqual(6f, r.Gauge.Current, Eps, "14 − 8.0 = 6.0");
            Assert.AreEqual(SuppressionResult.Suppressed, r.Gauge.TrySuppressScream(BreathZone.Surface),
                "부상 직후 외침 → 억제 가능(6.0 ≥ 4.5)");
            Assert.AreEqual(SuppressionResult.NotEnoughBreath, r.Gauge.TrySuppressScream(BreathZone.Surface),
                "두 번째 외침 → 억제 불가(1.5 < 4.5)");
        }

        [Test]
        public void AfterValveB_Residual6_CannotOpenEBackToBack_ButRecoversOnTheWay()
        {
            // 10-02: 총량 14초에서 B(8.0) 뒤 잔여 6.0 — E(진입 0.5 + 회전 7.0 = 7.5초 잠수가 필요)를 쉬지 않고 이어서 열 수 없다.
            var gauge = new BreathGauge();
            JobResult b = RunFullJob(ValveId.B, gauge);
            Assert.IsTrue(b.Opened);
            Assert.AreEqual(6f, gauge.Current, Eps, "B 한 번 뒤 잔여 6.0");

            JobResult e = RunFullJob(ValveId.E, gauge);
            Assert.IsFalse(e.Opened, "쉬지 않고 E — 회전 도중 숨이 다해 강제 부상(10-01 20초에서는 잔여 12로 열렸다)");
            Assert.AreEqual(0f, gauge.Current, Eps);

            // B(메인 풀)와 E(유아풀)는 다른 풀이다 — 물 밖으로 걸어가는 동안(대기 2초 + 초당 4) 숨이 차서 도착하면 열 수 있다.
            var walked = new BreathGauge();
            RunFullJob(ValveId.B, walked);
            for (int i = 0; i < 10; i++)
                walked.Tick(BreathZone.OutOfWater, 0.25f); // 2.5초: 대기 2초 + 0.5초 × 4 = +2 → 8.0
            Assert.AreEqual(8f, walked.Current, Eps);
            Assert.IsTrue(RunFullJob(ValveId.E, walked).Opened, "물 밖 2.5초 회복 뒤에는 E를 연다");
        }

        [Test]
        public void RotationOnly_WouldHaveLeftEntryPlusSurfaceMore_WhichIsWhyThisExists()
        {
            // 수정 전 모델(회전 5.0초만 잠수)은 진입 + 부상(3.0초)만큼 숨을 덜 썼다 — 수중 작업의 대가가 줄어든다.
            float rotationOnlyResidual = BreathConfig.TotalSeconds - ValveOccupancy.RotateSeconds(ValveId.B);
            float fullJobResidual = BreathConfig.TotalSeconds - ValveOccupancy.TotalSeconds(ValveId.B);
            Assert.AreEqual(ValveOccupancy.EntrySeconds(ValveId.B) + ValveOccupancy.SurfaceSeconds(ValveId.B),
                rotationOnlyResidual - fullJobResidual, Eps);
            Assert.AreEqual(3f, rotationOnlyResidual - fullJobResidual, Eps);
        }

        [Test]
        public void Entry_ThenRotate_ThenSurface_Boundaries()
        {
            var s = new UnderwaterWorkSession(ValveId.B);
            Assert.IsFalse(s.Tick(1.49f, true, false, true).BeginRotation, "1.49초 — 아직 하강");
            Assert.AreEqual(UnderwaterWorkPhase.Entry, s.Phase);
            Assert.IsTrue(s.Tick(0.02f, true, false, true).BeginRotation, "1.51초 — 진입 끝, 회전 시작");
            Assert.AreEqual(UnderwaterWorkPhase.Rotate, s.Phase);

            s.Tick(Dt, true, rotationFinished: true, canSubmerge: true);
            Assert.AreEqual(UnderwaterWorkPhase.Surface, s.Phase, "열리면 부상");
            s.Tick(1.49f, false, true, true);
            Assert.IsTrue(s.KeepsSubmerged, "1.49초 — 아직 수면 아래");
            s.Tick(0.02f, false, true, true);
            Assert.AreEqual(UnderwaterWorkPhase.Done, s.Phase, "1.51초 — 부상 완료");
            Assert.IsFalse(s.KeepsSubmerged);
        }

        [Test]
        public void ReleaseDuringRotation_StopsRotation_ThenSurfaces()
        {
            var s = new UnderwaterWorkSession(ValveId.B);
            s.Tick(1.5f, true, false, true);
            UnderwaterWorkTick t = s.Tick(Dt, holding: false, rotationFinished: false, canSubmerge: true);
            Assert.IsTrue(t.StopRotation, "손을 뗐다 — 밸브 감쇠로 넘어간다(§6.1)");
            Assert.AreEqual(UnderwaterWorkPhase.Surface, s.Phase, "뗀 뒤에도 부상 1.5초는 잠수다");
        }

        [Test]
        public void ReleaseDuringEntry_NeverTouchesValve()
        {
            var s = new UnderwaterWorkSession(ValveId.B);
            s.Tick(1f, true, false, true);
            UnderwaterWorkTick t = s.Tick(Dt, holding: false, rotationFinished: false, canSubmerge: true);
            Assert.IsFalse(t.BeginRotation);
            Assert.IsFalse(t.StopRotation);
            Assert.AreEqual(UnderwaterWorkPhase.Surface, s.Phase);
        }

        [Test]
        public void BreathExhausted_ForcedSurface_EndsImmediately()
        {
            // §5.9-1 강제 부상 — 숨이 0이면 어느 구간이든 끝난다.
            var s = new UnderwaterWorkSession(ValveId.E);
            s.Tick(0.5f, true, false, true);
            Assert.AreEqual(UnderwaterWorkPhase.Rotate, s.Phase);
            UnderwaterWorkTick t = s.Tick(Dt, true, false, canSubmerge: false);
            Assert.IsTrue(t.StopRotation);
            Assert.AreEqual(UnderwaterWorkPhase.Done, s.Phase);
        }

        [Test]
        public void ForceSurface_WhenRotationRejectedAfterEntry()
        {
            var s = new UnderwaterWorkSession(ValveId.B);
            s.Tick(1.5f, true, false, true);
            s.ForceSurface();
            Assert.AreEqual(UnderwaterWorkPhase.Surface, s.Phase);
        }

        // ── E vs GAP-75: 얕은 물에서 잠수가 실제로 성립하는가 ─────────────

        [Test]
        public void ValveE_ShallowPool_DivePostureSubmerges_StandingDoesNot()
        {
            // 유아풀 본체 수심 0.9(GAP-75 잠정 — 성립 구간 0.5 < 수심 < 1.62).
            // 서 있으면 머리 −0.9 + 1.62 = +0.72(수면 위), 잠수 자세면 −0.9 + 0.5 = −0.4(수면 아래).
            // 세션은 "잠수 키"와 같은 의도만 주므로 E에서 잠수가 **정상 발동**한다 — 강제로 잠그는 것이 아니다.
            var water = new WaterSample(true, 0f, -0.9f);
            Assert.AreEqual(BreathZone.Submerged, DiveRules.ZoneOf(water, -0.9f, diving: true));
            Assert.AreEqual(BreathZone.Surface, DiveRules.ZoneOf(water, -0.9f, diving: false));
            Assert.Greater(0.9f, DiveRules.MinDivableDepth, "0.9 > 0.5 — 잠수 가능 수심");
        }

        [Test]
        public void ValveB_MainPool_SwimFloor_DivePostureSubmerges()
        {
            // 메인풀: 발은 수영 바닥 −0.9(GAP-90), 바닥은 −3.5.
            var water = new WaterSample(true, 0f, -3.5f);
            Assert.AreEqual(BreathZone.Submerged, DiveRules.ZoneOf(water, -0.9f, diving: true));
        }

        [Test]
        public void ShallowSpot_NoForcedSubmersion()
        {
            // 경사로 위처럼 발이 −0.3이면 잠수 자세여도 머리 +0.2 — 잠기지 않는다(강제하지 않는다).
            var water = new WaterSample(true, 0f, -0.3f);
            Assert.AreEqual(BreathZone.Surface, DiveRules.ZoneOf(water, -0.3f, diving: true));
        }

        // ── Valve.CheckInteract ─────────────────────────────────────────

        [Test]
        public void CheckInteract_MatchesTryInteract_WithoutSideEffects()
        {
            var v = new Valve(ValveOccupancy.RotateSeconds(ValveId.B));
            v.Configure(ValveId.B);
            Assert.AreEqual(ValveInteractionRejection.EchoCannotInteract, v.CheckInteract(RoleType.Echo));
            Assert.AreEqual(ValveInteractionRejection.None, v.CheckInteract(RoleType.Runner));
            Assert.AreEqual(ValveState.Closed, v.State, "판정만 — 상태 불변");

            v.SetActive(false);
            Assert.AreEqual(ValveInteractionRejection.NotActiveThisRound, v.CheckInteract(RoleType.Runner));
            Assert.AreEqual(v.CheckInteract(RoleType.Runner), v.TryInteract(Me, RoleType.Runner));
        }
    }
}
