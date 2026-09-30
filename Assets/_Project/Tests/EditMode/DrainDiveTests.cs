using Marco.Core.Breath;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Water;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 배수구 잠수 시뮬레이터: 숨 판정(세션이 살아 있으면 잠수) → 배수구 틱 → 세션 틱 → 전이 반영(작업 시작 · 중단 · 부상 뒤 통과).
    /// <b>실제 서버 순서는 세션 → 배수구</b>(<c>RoundNetworkSync.TickDrain</c> — 밸브의 구동기 → 세션과는 다른 패턴)이며,
    /// 09-23 재검증 결과 두 순서의 산출물은 동일하다(이 시뮬레이터를 서버 순서로 바꿔 돌려도 이 파일 15개 테스트 전부 같은 값,
    /// 파이썬 정책 전수 탐색도 두 순서 결과 동일).
    /// 플레이어 정책은 <see cref="ReleaseAtBreath"/>(이 이하로 떨어지면 E를 뗌)과 <see cref="PressAtBreath"/>(이 이상 차면 다시 누름) 둘뿐이고,
    /// <b>숨은 물 위(수면 +2/s)에서만 채운다</b> — 물 밖(+4/s)으로 나가 채우는 전략은 모델링하지 않는다.
    /// </summary>
    internal sealed class DrainDiveSim
    {
        private const ulong Me = 9;

        public readonly DrainHatch Drain;
        public readonly BreathGauge Gauge = new BreathGauge();
        public UnderwaterWorkSession Session;
        public bool Intent = true;

        /// <summary>숨이 이 값 이하가 되면 스스로 E를 뗀다(부상 구간을 숨으로 덮으려는 정책). 0이면 끝까지 버틴다.</summary>
        public float ReleaseAtBreath;

        /// <summary>수면에서 숨이 이 값 이상 차면 다시 누른다. 기본 만충.</summary>
        public float PressAtBreath = BreathConfig.TotalSeconds;

        public int Dives;
        public int ForcedSurfaces;
        public bool Escaped;
        public float Elapsed;
        public float BreathAtTransit = -1f;

        public DrainDiveSim(int openValves)
        {
            Drain = new DrainHatch(DrainId.MainPool, DrainConfig.WorkSeconds(openValves));
        }

        public void Step(float dt)
        {
            // 누르고 있고 숨이 남아 있으면 진입(서버: IsEligibleForDrain — 물 안 · 범위는 시뮬레이션 전제).
            if (Session == null && Intent && Gauge.CanSubmerge && !Drain.IsInTransit && !Escaped)
            {
                Session = UnderwaterWorkSession.ForDrain();
                Dives++;
            }

            Gauge.Tick(Session != null && Session.KeepsSubmerged ? BreathZone.Submerged : BreathZone.Surface, dt);
            DrainTickResult r = Drain.Tick(dt);

            if (Session != null)
            {
                if (ReleaseAtBreath > 0f && Gauge.Current <= ReleaseAtBreath)
                    Intent = false;

                UnderwaterWorkTick step = Session.Tick(dt, Intent, Drain.IsCompleted, Gauge.CanSubmerge);
                if (step.BeginRotation && Drain.TryWork(Me, RoleType.Runner) != ValveInteractionRejection.None)
                    Session.ForceSurface();
                if (step.StopRotation)
                    Drain.StopWork(Me);

                if (Session.Phase == UnderwaterWorkPhase.Done)
                {
                    if (Session.EndedByForce)
                        ForcedSurfaces++;
                    else if (Drain.IsCompleted && Drain.BeginTransit(Me))
                        BreathAtTransit = Gauge.Current;

                    Session = null;
                    Intent = false;
                }
            }
            else if (!Intent && Gauge.Current >= PressAtBreath - 1e-4f)
            {
                Intent = true; // 숨이 찼다 — 다시 누른다
            }

            if (r.EscapedPlayer.HasValue)
                Escaped = true;
            Elapsed += dt;
        }

        public void RunUntilEscapeOr(float seconds, float dt = 0.5f)
        {
            while (!Escaped && Elapsed < seconds)
                Step(dt);
        }
    }

    /// <summary>
    /// [커밋 전 수정 4-1 · 4-2] 배수구 잠수 구간(진입 → 작업 → 부상, 밸브 B·E와 같은 세션) · 수중 작업 자격.
    /// </summary>
    public class DrainDiveTests
    {
        private const float Eps = 1e-3f;

        // ── 4-1 배수구 잠수 구간 ─────────────────────────────────────────

        [Test]
        public void DrainSession_UsesSameComponent_EntryAndSurfaceFromDrainConfig()
        {
            UnderwaterWorkSession s = UnderwaterWorkSession.ForDrain();
            Assert.AreEqual(DrainConfig.EntrySeconds, s.EntrySeconds, Eps);
            Assert.AreEqual(DrainConfig.SurfaceSeconds, s.SurfaceSeconds, Eps);
        }

        // ── 10-01 플레이 보고 "배수구를 절대 못 연다" — 숨 총량 20초 ──────────────────

        [TestCase(0.5f)]
        [TestCase(1f / 60f)]
        public void NoValvesOpen_HoldFromFullBreath_CompletesInOneDive(float dt)
        {
            // 최후 생존자 페이즈 · 동시 개방 0개 → T 14 → 총 점유(진입 1 + 14 + 부상 1) 16초.
            // 만충에서 E를 끝까지 누른 채 한 번 잠수로 작업 · 부상 · 통과까지 마쳐야 한다.
            // 수정 전(숨 12초): 작업 11초째에 숨이 바닥나 강제 부상 — 진행도 11/14에서 감쇠(유예 3초 뒤 −0.10/s)가
            // 수면 회복(대기 2초 + 초당 2)보다 빨라 다음 잠수로도 따라잡지 못했다(아래 NoValvesOpen_SurfaceRecoveryOnly_… 참조).
            var sim = new DrainDiveSim(openValves: 0);
            while (sim.Dives < 2 && !sim.Escaped && sim.Elapsed < DrainConfig.PhaseSeconds)
                sim.Step(dt);

            Assert.AreEqual(0, sim.ForcedSurfaces, "숨이 모자라 강제 부상(질식)하면 안 된다");
            Assert.IsTrue(sim.Escaped, "첫 잠수로 탈출(작업 완료 → 부상 → 통과 1.5초)");
            Assert.AreEqual(1, sim.Dives);
        }

        [TestCase(0, 14f, 4f)]
        [TestCase(1, 11f, 7f)]
        [TestCase(2, 8f, 10f)]
        [TestCase(3, 5f, 13f)] // 공식 확인용 — 실제로는 요구 수(2 또는 3)를 채워 게이트가 열리면 배수구가 켜지지 않는다(§6.5-2)
        public void OneDive_WorkSecondsAndResidualBreath(int openValves, float workSeconds, float residualBreath)
        {
            Assert.AreEqual(workSeconds, DrainConfig.WorkSeconds(openValves), Eps, "T = 14 − 동시 개방 × 3");

            // 서버에 가까운 1/60초 틱. 진입 · 작업 · 부상 세 구간은 틱마다 dt를 더해 경계를 넘는지 보므로, 부동소수 누적으로
            // 구간마다 최대 한 틱씩 늦을 수 있다 — 허용 오차 세 틱(0.05초). 게임에서도 같다(서버 틱 단위).
            // (0.5초 틱이면 T 14초가 한 틱 늦어 잔여가 3.5로 나온다.)
            const float Dt = 1f / 60f;
            var sim = new DrainDiveSim(openValves);
            sim.RunUntilEscapeOr(DrainConfig.PhaseSeconds, Dt);

            Assert.IsTrue(sim.Escaped);
            Assert.AreEqual(1, sim.Dives, "한 번 잠수");
            Assert.AreEqual(0, sim.ForcedSurfaces);
            Assert.AreEqual(residualBreath, sim.BreathAtTransit, 3f * Dt + Eps, "20 − (진입 1 + T + 부상 1) — 부상을 마친 순간의 숨");
        }

        [TestCase(0)]
        [TestCase(1)]
        public void CautiousPolicy_ReleaseAtOneAndHalf_StillOneDive(int openValves)
        {
            // 부상을 숨으로 덮으려는 조심스러운 정책(숨 1.5에서 E를 뗌)도 한 번에 끝난다 — 잔여가 4 · 7초라 떼는 일이 없다.
            // v0.4(12초)에서는 1개 개방이 2회 잠수, 0개 개방은 수면 회복만으로 질식 없이 90초 안에 나갈 수 없었다
            // (진행도 감쇠 −0.10/s가 수면 회복보다 빨랐다 — 10-01 플레이 보고의 원인).
            var sim = new DrainDiveSim(openValves) { ReleaseAtBreath = 1.5f };
            sim.RunUntilEscapeOr(DrainConfig.PhaseSeconds);

            Assert.IsTrue(sim.Escaped, $"경과 {sim.Elapsed:0.0}초");
            Assert.AreEqual(1, sim.Dives);
            Assert.AreEqual(0, sim.ForcedSurfaces);
        }

        [Test]
        public void Transit_OnlyAfterSurface_ThenTaggableWindow()
        {
            // 작업 완료 = 부상 대기. 통과(태그 가능 1.5초)는 부상을 마친 뒤에 열린다.
            var d = new DrainHatch(DrainId.MainPool, 8f);
            d.TryWork(3, RoleType.Runner);
            Assert.IsTrue(d.Tick(8f).Completed);
            Assert.IsTrue(d.IsCompleted);
            Assert.IsFalse(d.IsInTransit, "아직 통과 아님 — 부상 중(잠수)이라 태그되지 않는다");
            Assert.AreEqual(1f, d.Progress01, Eps);

            Assert.IsTrue(d.BeginTransit(3));
            Assert.IsTrue(d.IsInTransit);
            Assert.IsFalse(d.BeginTransit(3), "한 번만");
        }

        [Test]
        public void ForcedSurfaceAfterCompletion_KeepsCompletion_NextDiveIsEntryPlusSurface()
        {
            var d = new DrainHatch(DrainId.MainPool, 8f);
            d.TryWork(3, RoleType.Runner);
            d.Tick(8f);
            // (강제 부상 — BeginTransit 호출 없음) 완료는 유지된다.
            Assert.IsTrue(d.IsCompleted);
            Assert.AreEqual(ValveInteractionRejection.None, d.TryWork(3, RoleType.Runner), "다음 잠수 — 할 일 없음");
            Assert.IsFalse(d.IsWorking);
        }

        // ── 4-2 수중 작업 자격 — "이 자리에서 실제로 잠길 수 있음" ─────────

        private static readonly WaterSample MainPoolSwim = new WaterSample(true, 0f, -3.5f);

        [Test]
        public void Deck_OutOfWater_Rejected_EvenInHorizontalRange()
        {
            // 밸브 B (34,22) — 메인풀 동쪽 벽(수면 x=35) 바깥 덱 x=35.5에 서면 수평 1.5m: 범위 안.
            var feet = new Vector3(35.5f, 0f, 22f);
            var valveB = new Vector3(34f, -3.0f, 22f);
            Assert.IsTrue(InteractionRules.InRange(feet, valveB, underwaterTarget: true), "수평 거리만으로는 잡힌다");
            Assert.IsFalse(UnderwaterWorkSession.CanWork(RoleType.Runner, true, WaterSample.OutOfWater, feet.y, true),
                "물 밖 — 거부(GAP-88 해소)");
        }

        [TestCase(-0.49f, false)] // 경사로 윗부분 — 잠수 자세 머리 +0.01(수면 위)
        [TestCase(-0.51f, true)]  // 머리 −0.01(수면 아래)
        [TestCase(-0.90f, true)]  // 수영 바닥 / 유아풀 바닥
        public void ShallowBoundary_HeadMustGoUnder(float feetY, bool expected)
        {
            var water = new WaterSample(true, 0f, feetY);
            Assert.AreEqual(expected, DiveRules.CanSubmergeHere(RoleType.Runner, water, feetY, canSubmerge: true));
            Assert.AreEqual(expected, UnderwaterWorkSession.CanWork(RoleType.Runner, true, water, feetY, true));
        }

        [Test]
        public void NoBreath_Seeker_Echo_OutOfRange_Rejected()
        {
            Assert.IsFalse(UnderwaterWorkSession.CanWork(RoleType.Runner, true, MainPoolSwim, -0.9f, canSubmerge: false), "숨 0");
            Assert.IsFalse(UnderwaterWorkSession.CanWork(RoleType.Seeker, true, MainPoolSwim, -0.9f, true), "술래는 잠수 불가");
            Assert.IsFalse(UnderwaterWorkSession.CanWork(RoleType.Echo, true, MainPoolSwim, -0.9f, true));
            Assert.IsFalse(UnderwaterWorkSession.CanWork(RoleType.Runner, false, MainPoolSwim, -0.9f, true), "범위 밖");
            Assert.IsTrue(UnderwaterWorkSession.CanWork(RoleType.Runner, true, MainPoolSwim, -0.9f, true));
        }

        [Test]
        public void DrainCanWork_DelegatesToSameRule()
        {
            foreach (RoleType role in new[] { RoleType.Runner, RoleType.Seeker, RoleType.Echo })
            foreach (bool inRange in new[] { false, true })
            foreach (bool sub in new[] { false, true })
                Assert.AreEqual(UnderwaterWorkSession.CanWork(role, inRange, sub), DrainHatch.CanWork(role, inRange, sub));
        }

        // ── 배치 확인(수평 2.5m 판정 반경과 물가 거리) ─────────────────────

        [Test]
        public void Layout_Drain1_BeyondDeckAndRamp_ValveB_WithinDeck()
        {
            // 메인풀 수면 (21,17)~(35,25), 풀 측벽 두께 0.2, 경사로 수평 1.157(계단 완만 쪽 37.87°에서 유도).
            float wallOuter = 35f + 0.2f;
            float rampRun = 0.9f / Mathf.Tan(37.87f * Mathf.Deg2Rad);

            // 밸브 B (34,22): 덱(벽 바깥)까지 1.2m — 2.5m 안이라 자격 판정이 없으면 덱에서 잡혔다.
            Assert.Less(wallOuter - 34f, InteractionRules.RangeMeters);

            // 배수구 1 (31,21): 가장 가까운 물가까지 4.0m — 덱은 물론 경사로 안쪽 끝(2.84m)도 2.5m 밖.
            float drain1ToEdge = Mathf.Min(35f - 31f, Mathf.Min(21f - 17f, 25f - 21f));
            Assert.AreEqual(4f, drain1ToEdge, Eps);
            Assert.Greater(drain1ToEdge - rampRun, InteractionRules.RangeMeters);
        }
    }
}
