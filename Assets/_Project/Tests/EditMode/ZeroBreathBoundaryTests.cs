using Marco.Core.Breath;
using Marco.Core.Objectives;
using Marco.Core.Role;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §5.9-1 "게이지 0초 경계 처리" — 부상 완료와 게이지 고갈이 <b>같은 틱</b>에 성립하면 부상 완료가 이긴다
    /// (순위 1 부상 완료 · 순위 2 게이지 고갈, "부상 완료 판정이 질식 판정보다 항상 우선한다").
    ///
    /// <para>
    /// 서버 순서(<c>ServerTickOrder</c>)를 그대로 옮긴다: ① 숨 게이지 — 세션이 살아 있으면 잠수, 이번 틱에 부상이 끝나는지를
    /// 세션에 묻는다 → ② 세션 → ③ 대상(밸브 · 배수구). 이전 구현은 세션이 숨 0을 먼저 봐 강제 부상시켰고,
    /// 게이지는 세션과 무관하게 질식(파문 · 감속)을 냈다 — 그리고 두 컴포넌트의 순서가 정해져 있지 않았다.
    /// </para>
    /// </summary>
    public class ZeroBreathBoundaryTests
    {
        private const float Dt = 0.5f; // 2진 정확값 — 누적 오차 없이 정확히 0에 닿는다
        private const float Eps = 1e-4f;
        private const ulong Me = 7;

        // ── 단위: 세션 ───────────────────────────────────────────────────

        private static UnderwaterWorkSession SessionInSurface(float surfaceElapsed)
        {
            var s = UnderwaterWorkSession.ForDrain();
            s.Tick(DrainConfig.EntrySeconds, true, false, true);          // 진입 끝 → 작업
            s.Tick(Dt, true, rotationFinished: true, canSubmerge: true);   // 완료 → 부상
            Assert.AreEqual(UnderwaterWorkPhase.Surface, s.Phase);
            if (surfaceElapsed > 0f)
                s.Tick(surfaceElapsed, false, true, true);
            return s;
        }

        [Test]
        public void Session_SurfaceEndsThisTick_BreathZero_CompletionWins()
        {
            UnderwaterWorkSession s = SessionInSurface(DrainConfig.SurfaceSeconds - Dt);
            Assert.IsTrue(s.SurfaceCompletesWithin(Dt));

            s.Tick(Dt, false, true, canSubmerge: false);

            Assert.AreEqual(UnderwaterWorkPhase.Done, s.Phase);
            Assert.IsFalse(s.EndedByForce, "순위 1 부상 완료 — 강제 부상 아님");
        }

        [Test]
        public void Session_SurfaceNotEndingThisTick_BreathZero_ForcedSurface()
        {
            UnderwaterWorkSession s = SessionInSurface(0f);
            Assert.IsFalse(s.SurfaceCompletesWithin(Dt), "부상 1초 중 0.5초 — 이번 틱에 안 끝난다");

            s.Tick(Dt, false, true, canSubmerge: false);

            Assert.AreEqual(UnderwaterWorkPhase.Done, s.Phase);
            Assert.IsTrue(s.EndedByForce, "경계가 아니면 기존대로 강제 부상");
        }

        [Test]
        public void Session_RotatePhase_BreathZero_StillForced()
        {
            var s = UnderwaterWorkSession.ForDrain();
            s.Tick(DrainConfig.EntrySeconds, true, false, true);
            Assert.IsFalse(s.SurfaceCompletesWithin(100f), "작업 중에는 경계 예외가 없다");
            UnderwaterWorkTick t = s.Tick(Dt, true, false, canSubmerge: false);
            Assert.IsTrue(t.StopRotation);
            Assert.IsTrue(s.EndedByForce);
        }

        // ── 단위: 게이지 ─────────────────────────────────────────────────

        [Test]
        public void Gauge_HitsZero_WhileSurfacingCompletes_NoChoke()
        {
            var g = new BreathGauge();
            g.Tick(BreathZone.Submerged, BreathConfig.TotalSeconds - Dt);   // 잔여 0.5

            BreathTick t = g.Tick(BreathZone.Submerged, Dt, surfacingCompletes: true);

            Assert.IsFalse(t.Choked, "부상 완료가 우선 — 고함급 파문 없음");
            Assert.IsFalse(g.IsChokePenaltyActive, "이동 −20% 없음");
            Assert.AreEqual(0f, g.Current, Eps, "숨은 그대로 0까지 쓴다");
            Assert.IsTrue(g.IsRecoveryDelayed, "회복 대기 2초도 그대로");
        }

        [Test]
        public void Gauge_HitsZero_Otherwise_Chokes()
        {
            var g = new BreathGauge();
            g.Tick(BreathZone.Submerged, BreathConfig.TotalSeconds - Dt);

            BreathTick t = g.Tick(BreathZone.Submerged, Dt);

            Assert.IsTrue(t.Choked);
            Assert.IsTrue(g.IsChokePenaltyActive);
            Assert.AreEqual(BreathConfig.ChokeSpeedMultiplier, g.SpeedMultiplier, Eps);
        }

        // ── 서버 순서 통합 ───────────────────────────────────────────────

        private struct DrainRun
        {
            public bool TransitOpened;
            public bool Forced;
            public int Chokes;
            public float BreathAtEnd;
        }

        /// <summary>배수구 작업 1회 — 서버 순서: ① 숨(부상 완료 여부 전달) → ② 세션 → ③ 배수구.</summary>
        private static DrainRun RunDrainJob(int openValves, float startBreath)
        {
            var gauge = new BreathGauge();
            if (startBreath < BreathConfig.TotalSeconds)
                gauge.Tick(BreathZone.Submerged, BreathConfig.TotalSeconds - startBreath);

            var drain = new DrainHatch(DrainId.MainPool, DrainConfig.WorkSeconds(openValves));
            var session = UnderwaterWorkSession.ForDrain();
            var run = new DrainRun();

            for (int i = 0; i < 200 && session.KeepsSubmerged; i++)
            {
                BreathTick bt = gauge.Tick(BreathZone.Submerged, Dt, session.SurfaceCompletesWithin(Dt));
                if (bt.Choked)
                    run.Chokes++;

                UnderwaterWorkTick st = session.Tick(Dt, holding: true, drain.IsCompleted, gauge.CanSubmerge);
                if (st.BeginRotation)
                    Assert.AreEqual(ValveInteractionRejection.None, drain.TryWork(Me, RoleType.Runner));
                if (st.StopRotation)
                    drain.StopWork(Me);

                drain.Tick(Dt);

                if (session.Phase == UnderwaterWorkPhase.Done)
                {
                    run.Forced = session.EndedByForce;
                    run.TransitOpened = !session.EndedByForce && drain.IsCompleted && drain.BeginTransit(Me);
                }
            }

            run.BreathAtEnd = gauge.Current;
            return run;
        }

        [Test]
        public void Drain_TwoOpen_StartBreathEqualsOccupancy_ExactZero_Escapes()
        {
            // 2개 개방: 진입 1 + 작업 8 + 부상 1 = 10초. 숨 10.0으로 시작하면 부상이 끝나는 틱에 정확히 0이 된다.
            Assert.AreEqual(10f, DrainConfig.TotalOccupancySeconds(2), Eps);
            DrainRun r = RunDrainJob(openValves: 2, startBreath: 10f);

            Assert.IsTrue(r.TransitOpened, "§5.9-1 순위 1 — 부상 완료로 통과가 열린다");
            Assert.IsFalse(r.Forced);
            Assert.AreEqual(0, r.Chokes, "질식(고함급 파문 · 감속) 없음");
            Assert.AreEqual(0f, r.BreathAtEnd, Eps);
        }

        [Test]
        public void Drain_TwoOpen_OneTickShort_ChokesAndNoTransit()
        {
            // 숨 9.5 — 부상 마지막 틱 **전에** 0이 된다. 경계가 아니므로 기존대로 질식 · 강제 부상.
            DrainRun r = RunDrainJob(openValves: 2, startBreath: 10f - Dt);

            Assert.IsFalse(r.TransitOpened);
            Assert.IsTrue(r.Forced);
            Assert.AreEqual(1, r.Chokes);
        }

        [Test]
        public void ValveB_StartBreathEqualsOccupancy_ExactZero_OpensWithoutChoke()
        {
            // 밸브 B 총 점유 8.0 — 숨 8.0으로 시작. 서버 순서: 숨 → 밸브 구동기 → 세션(ValveNetworkSync.Update).
            var valve = new Valve(ValveOccupancy.RotateSeconds(ValveId.B));
            valve.Configure(ValveId.B);
            var gauge = new BreathGauge();
            gauge.Tick(BreathZone.Submerged, BreathConfig.TotalSeconds - ValveOccupancy.TotalSeconds(ValveId.B));
            var session = new UnderwaterWorkSession(ValveId.B);
            int chokes = 0;

            for (int i = 0; i < 100 && session.KeepsSubmerged; i++)
            {
                if (gauge.Tick(BreathZone.Submerged, Dt, session.SurfaceCompletesWithin(Dt)).Choked)
                    chokes++;
                if (session.Phase == UnderwaterWorkPhase.Rotate)
                    valve.Tick(Dt);

                UnderwaterWorkTick t = session.Tick(Dt, true, valve.State == ValveState.Open, gauge.CanSubmerge);
                if (t.BeginRotation)
                    valve.TryInteract(Me, RoleType.Runner);
                if (t.StopRotation)
                    valve.Interrupt(Me);
            }

            Assert.AreEqual(ValveState.Open, valve.State);
            Assert.IsFalse(session.EndedByForce, "정확히 게이지 0초에 부상 완료 → 성공(§5.9-1)");
            Assert.AreEqual(0, chokes);
            Assert.AreEqual(0f, gauge.Current, Eps);
        }
    }
}
