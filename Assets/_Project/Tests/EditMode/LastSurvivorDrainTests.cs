using System.Collections.Generic;
using Marco.Core.Breath;
using Marco.Core.GameFlow;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Tagging;
using Marco.Core.Water;
using Marco.Presentation.Objectives;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [블록 4] §6.5 최후 생존자 페이즈 · 배수구. 수치는 전부 §6.5-1 · §6.5-2 표에서 옮겼다.
    /// </summary>
    public class LastSurvivorDrainTests
    {
        private const float Eps = 1e-4f;

        // ── §6.5-2 작업 시간 · 총 점유 · 잠수 횟수 표 ─────────────────────

        // 동시 개방 | T | 총 점유(진입 1 + T + 부상 1) | 게이지 12초 기준
        //   0개     | 14 | 16 | 2회
        //   1개     | 11 | 13 | 2회
        //   2개     |  8 | 10 | 1회(잔여 2초)
        [TestCase(0, 14f, 16f, 2)]
        [TestCase(1, 11f, 13f, 2)]
        [TestCase(2, 8f, 10f, 1)]
        public void DrainTable_MatchesDesignDoc(int openValves, float t, float occupancy, int dives)
        {
            Assert.AreEqual(t, DrainConfig.WorkSeconds(openValves), Eps);
            Assert.AreEqual(occupancy, DrainConfig.TotalOccupancySeconds(openValves), Eps);
            Assert.AreEqual(dives, DrainConfig.RequiredDives(openValves, BreathConfig.TotalSeconds));
        }

        [Test]
        public void TwoOpenValves_OneDive_LeavesTwoSecondsOfBreath()
        {
            // §6.5-2 "1회 잠수(잔여 2초)" — 게이지 값은 BreathConfig 한 곳이 소유한다.
            Assert.AreEqual(2f, BreathConfig.TotalSeconds - DrainConfig.TotalOccupancySeconds(2), Eps);
        }

        [Test]
        public void RequiredDives_Boundary_EqualOccupancyIsOneDive()
        {
            // 경계: 총 점유 = 게이지면 1회(≤). 0.01초라도 모자라면 2회.
            Assert.AreEqual(1, DrainConfig.RequiredDives(2, 10f));
            Assert.AreEqual(2, DrainConfig.RequiredDives(2, 9.99f));
        }

        [Test]
        public void WorkSeconds_NeverNegative()
        {
            // 방어 하한 — 5개 개방은 게이트가 열려 배수구가 활성화되지 않지만 공식은 음수가 되면 안 된다.
            Assert.AreEqual(0f, DrainConfig.WorkSeconds(5), Eps);
            Assert.AreEqual(14f, DrainConfig.WorkSeconds(-1), Eps);
        }

        [Test]
        public void PhaseConstants_MatchDesignDoc()
        {
            Assert.AreEqual(90f, DrainConfig.PhaseSeconds, Eps);   // §6.5-1
            Assert.AreEqual(1.5f, DrainConfig.TransitSeconds, Eps); // §6.5-2
            Assert.AreEqual(2, DrainConfig.PlacedCount);            // §6.5-2
            Assert.AreEqual(2, DrainSelection.All.Length);
        }

        // ── 선택 · 활성 ──────────────────────────────────────────────────

        [Test]
        public void DrainSelection_IsDeterministicAndReachesBoth()
        {
            var seen = new HashSet<DrainId>();
            for (int seed = 0; seed < 100; seed++)
            {
                DrainId a = DrainSelection.Choose(seed);
                Assert.AreEqual(a, DrainSelection.Choose(seed), "같은 시드는 같은 배수구");
                seen.Add(a);
            }

            Assert.AreEqual(2, seen.Count, "100개 시드 안에 두 배수구가 모두 나와야 한다(§10.5 선점 방지)");
        }

        [Test]
        public void GateOpen_DoesNotActivateDrain()
        {
            // §6.5-2 "게이트 개방 상태 — 활성화하지 않는다(기존 출구를 쓰면 된다)".
            Assert.IsFalse(DrainSelection.ShouldActivate(gateOpen: true));
            Assert.IsTrue(DrainSelection.ShouldActivate(gateOpen: false));
        }

        // ── 수중 강제 파문(§6.1 · §6.5-3) ─────────────────────────────────

        [Test]
        public void UnderwaterPulse_FiresOnStart_ThenEvery2Point5()
        {
            Assert.AreEqual(2.5f, UnderwaterWorkPulse.IntervalSeconds, Eps);

            var p = new UnderwaterWorkPulse();
            Assert.AreEqual(0, p.Tick(false, 1f), "작업 전에는 없다");
            Assert.AreEqual(1, p.Tick(true, 0.02f), "시작 순간 1회");
            Assert.AreEqual(0, p.Tick(true, 2.49f), "2.49초 — 아직");
            Assert.AreEqual(1, p.Tick(true, 0.01f), "2.5초 경계에서 1회");
            Assert.AreEqual(2, p.Tick(true, 5f), "한 틱이 길면 몰아서 센다");
        }

        [Test]
        public void UnderwaterPulse_StopAndRestart_FiresImmediatelyAgain()
        {
            // 짧게 끊어 치는 작업이 무음이 되면 안 된다("침묵 탈출은 불가능하다").
            var p = new UnderwaterWorkPulse();
            p.Tick(true, 0f);
            p.Tick(true, 1f);
            Assert.AreEqual(0, p.Tick(false, 0.1f));
            Assert.AreEqual(1, p.Tick(true, 0.1f));
        }

        [Test]
        public void UnderwaterValves_AreBAndEOnly()
        {
            // 강제 파문을 거는 대상 — ValveNetworkSync가 이 판정으로 고른다.
            Assert.IsTrue(ValveOccupancy.IsUnderwater(ValveId.B));
            Assert.IsTrue(ValveOccupancy.IsUnderwater(ValveId.E));
            Assert.IsFalse(ValveOccupancy.IsUnderwater(ValveId.A));
            Assert.IsFalse(ValveOccupancy.IsUnderwater(ValveId.C));
            Assert.IsFalse(ValveOccupancy.IsUnderwater(ValveId.D));
        }

        // ── DrainHatch — 진행도는 Valve 규칙 재사용 ──────────────────────

        [Test]
        public void DrainHatch_OnlyRunnerCanWork()
        {
            var d = new DrainHatch(DrainId.MainPool, 8f);
            Assert.AreNotEqual(ValveInteractionRejection.None, d.TryWork(1, RoleType.Seeker));
            Assert.AreNotEqual(ValveInteractionRejection.None, d.TryWork(2, RoleType.Echo));
            Assert.AreEqual(ValveInteractionRejection.None, d.TryWork(3, RoleType.Runner));
            Assert.IsTrue(d.IsWorking);
        }

        [Test]
        public void DrainHatch_CompletesAfterT_ThenTransit1Point5_ThenEscape()
        {
            var d = new DrainHatch(DrainId.KiddiePool, 8f);
            d.TryWork(7, RoleType.Runner);

            DrainTickResult r = d.Tick(8f);
            Assert.IsTrue(r.TransitStarted, "T=8초 경과 → 통과 시작");
            Assert.IsTrue(d.IsInTransit);
            Assert.AreEqual(7UL, d.TransitPlayer);

            Assert.IsFalse(d.Tick(1.4f).EscapedPlayer.HasValue, "1.4초 — 아직 통과 중(태그 가능 창)");
            DrainTickResult done = d.Tick(0.1f + Eps);
            Assert.AreEqual(7UL, done.EscapedPlayer, "1.5초 경과 → 탈출");
            Assert.IsFalse(d.IsInTransit);
        }

        [Test]
        public void DrainHatch_NotCompleteJustBeforeT()
        {
            var d = new DrainHatch(DrainId.MainPool, 14f);
            d.TryWork(1, RoleType.Runner);
            Assert.IsFalse(d.Tick(13.9f).TransitStarted);
            Assert.AreEqual(13.9f / 14f, d.Progress01, 1e-3f);
        }

        [Test]
        public void DrainHatch_TaggedDuringTransit_NoEscape()
        {
            var d = new DrainHatch(DrainId.MainPool, 8f);
            d.TryWork(4, RoleType.Runner);
            d.Tick(8f);

            d.CancelTransit(4);
            Assert.IsFalse(d.IsInTransit);
            Assert.IsFalse(d.Tick(2f).EscapedPlayer.HasValue);
        }

        [Test]
        public void DrainHatch_Interrupted_GraceThenDecay_SameAsValve()
        {
            // §6.5-2 "진행도 — 밸브와 동일(유예 3초 + 감쇠 0.10/s)".
            var d = new DrainHatch(DrainId.MainPool, 10f);
            d.TryWork(1, RoleType.Runner);
            d.Tick(5f);
            Assert.AreEqual(0.5f, d.Progress01, Eps);

            d.StopWork(1);
            d.Tick(3f);
            Assert.AreEqual(0.5f, d.Progress01, Eps, "유예 3초 동안 손실 없음");

            d.Tick(1f);
            Assert.AreEqual(0.4f, d.Progress01, 1e-3f, "이후 초당 -0.10");
            Assert.IsTrue(d.IsDecaying, "HUD 감쇠 색의 근거");
        }

        [TestCase(RoleType.Runner, true, true, true)]
        [TestCase(RoleType.Runner, false, true, false)]
        [TestCase(RoleType.Runner, true, false, false)]
        [TestCase(RoleType.Seeker, true, true, false)]
        [TestCase(RoleType.Echo, true, true, false)]
        public void DrainHatch_CanWork_TruthTable(RoleType role, bool inRange, bool submerged, bool expected)
        {
            Assert.AreEqual(expected, DrainHatch.CanWork(role, inRange, submerged));
        }

        // ── 상호작용 거리(GAP-10/73/88) ──────────────────────────────────

        [Test]
        public void InteractionRange_IsSingleSource()
        {
            // 밸브(Presentation)와 배수구(Net 재검증)가 같은 값을 쓴다 — 더블체크 8.
            Assert.AreEqual(2.5f, InteractionRules.RangeMeters, Eps);
            Assert.AreEqual(InteractionRules.RangeMeters, ValveInteractionController.DefaultInteractionRange, Eps);
        }

        [Test]
        public void UnderwaterTarget_UsesHorizontalDistance()
        {
            // 수면 근처의 발(-0.9)과 3.5m 바닥의 배수구: 3D는 2.6m > 2.5m라 영영 닿지 않는다.
            var feet = new Vector3(31f, -0.9f, 21f);
            var drain = new Vector3(31f, -3.5f, 21f);

            Assert.IsFalse(InteractionRules.InRange(feet, drain, underwaterTarget: false));
            Assert.IsTrue(InteractionRules.InRange(feet, drain, underwaterTarget: true));

            // 수평 경계 2.5m 포함 / 2.51m 제외.
            Assert.IsTrue(InteractionRules.InRange(new Vector3(33.5f, -0.9f, 21f), drain, true));
            Assert.IsFalse(InteractionRules.InRange(new Vector3(33.51f, -0.9f, 21f), drain, true));
        }

        // ── §6.5-3 잠수 중 접촉 위치(GAP-89) ─────────────────────────────

        [Test]
        public void SubmergedRunner_AtDrain_CannotBeTaggedFromSurface()
        {
            // 메인 풀 배수구 3.5m. 술래는 바로 위 수면 높이에 있다.
            var water = new WaterSample(true, 0f, -3.5f);
            var runnerFeet = new Vector3(31f, -0.9f, 21f);
            var seeker = new Vector3(31f, -0.9f, 21f);

            Vector3 submerged = DiveRules.ContactPosition(runnerFeet, water, submerged: true);
            Assert.AreEqual(-3.5f, submerged.y, Eps);
            Assert.IsFalse(TagRules.IsWithinTagRange(seeker, submerged),
                "§6.5-3 수심 3.5m > 태그 1.2m — 수면에서 닿지 않는다");

            Vector3 surfaced = DiveRules.ContactPosition(runnerFeet, water, submerged: false);
            Assert.IsTrue(TagRules.IsWithinTagRange(seeker, surfaced),
                "§6.5-3 부상하는 순간이 유일한 태그 기회");
        }

        [Test]
        public void ContactPosition_ShallowPool_Unchanged()
        {
            // 유아풀 본체(0.9m 잠정, GAP-75)에서는 서 있는 발이 이미 바닥 — 결과가 같다.
            var water = new WaterSample(true, 0f, -0.9f);
            var feet = new Vector3(8f, -0.9f, 9.5f);
            Assert.AreEqual(feet, DiveRules.ContactPosition(feet, water, submerged: true));
        }

        [Test]
        public void ContactPosition_OutOfWater_Unchanged()
        {
            var feet = new Vector3(1f, 0f, 1f);
            Assert.AreEqual(feet, DiveRules.ContactPosition(feet, WaterSample.OutOfWater, submerged: true));
        }

        // ── 페이즈 타이머(§6.5-1 "라운드 잔여가 더 짧으면 그쪽 우선") ─────

        [Test]
        public void PhaseTimer_RoundShorter_89sWins()
        {
            var driver = new ServerRoundDriver(600f);
            driver.Tick(511f);
            Assert.AreEqual(89f, driver.RemainingSeconds, Eps);

            Assert.IsTrue(driver.TryEnterLastSurvivorPhase());
            Assert.AreEqual(90f, driver.PhaseRemainingSeconds, Eps);
            Assert.AreEqual(89f, driver.EffectiveRemainingSeconds(driver.PhaseRemainingSeconds), Eps,
                "페이즈가 라운드를 연장하지 않는다");
        }

        [Test]
        public void PhaseTimer_PhaseShorter_ExpiresAt90_SeekerWins()
        {
            var driver = new ServerRoundDriver(600f);
            driver.Tick(300f);
            driver.TryEnterLastSurvivorPhase();

            driver.Tick(89.9f);
            Assert.IsFalse(driver.Evaluate(driver.Census(3, 2)), "89.9초 — 진행 중");

            driver.Tick(0.1f + Eps);
            Assert.IsTrue(driver.Evaluate(driver.Census(3, 2)));
            Assert.AreEqual(RoundResult.SeekerWin, driver.Result, "§6.3 페이즈 90초 경과 → 판정");
            Assert.Greater(driver.RemainingSeconds, 200f, "라운드 잔여는 남아 있었다");
        }

        [Test]
        public void PhaseTimer_EnterOnlyOnce()
        {
            var driver = new ServerRoundDriver(600f);
            Assert.IsTrue(driver.TryEnterLastSurvivorPhase());
            driver.Tick(10f);
            Assert.IsFalse(driver.TryEnterLastSurvivorPhase(), "1회성 — 재진입이 타이머를 90으로 되돌리면 안 된다");
            Assert.AreEqual(80f, driver.PhaseRemainingSeconds, Eps);
        }

        [Test]
        public void DrainEscape_LastSurvivor_TeamWins()
        {
            // 3인 도망자 중 2명 태그 → 최후 생존자 배수구 탈출 → 탈출 1 < ⌈3/2⌉=2 이지만 팀 승리(§6.3).
            var driver = new ServerRoundDriver(600f);
            driver.TryEnterLastSurvivorPhase();

            Assert.IsTrue(driver.TryRegisterDrainEscape(9, RoleType.Runner));
            Assert.IsTrue(driver.Evaluate(driver.Census(3, 2)));
            Assert.AreEqual(RoundResult.RunnersWin, driver.Result);
        }

        [Test]
        public void DrainEscape_OutsidePhase_Rejected()
        {
            var driver = new ServerRoundDriver(600f);
            Assert.IsFalse(driver.TryRegisterDrainEscape(9, RoleType.Runner), "배수구는 페이즈 전용");
        }

        [Test]
        public void DrainEscape_TaggedDuringTransit_EchoRejected()
        {
            // 통과 1.5초 중 태그되면 서버가 역할을 다시 읽어 Echo로 넘긴다.
            var driver = new ServerRoundDriver(600f);
            driver.TryEnterLastSurvivorPhase();
            Assert.IsFalse(driver.TryRegisterDrainEscape(9, RoleType.Echo));
            Assert.IsFalse(driver.LastSurvivorEscaped);
        }

        // ── HUD 문구 ────────────────────────────────────────────────────

        [Test]
        public void Hud_PhaseLine()
        {
            Assert.AreEqual(string.Empty, HudFormatter.FormatLastSurvivorPhase(false, 50f, 1, 0.5f));
            Assert.AreEqual("최후 생존자 1:30 — 배수구 메인 풀 42%",
                HudFormatter.FormatLastSurvivorPhase(true, 89.2f, 1, 0.426f));
            Assert.AreEqual("최후 생존자 0:30 — 배수구 유아풀 99%",
                HudFormatter.FormatLastSurvivorPhase(true, 30f, 2, 0.996f));
            Assert.AreEqual("최후 생존자 0:30 — 출구 개방",
                HudFormatter.FormatLastSurvivorPhase(true, 30f, 0, 0f));
        }
    }
}
