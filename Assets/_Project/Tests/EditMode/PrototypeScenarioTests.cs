using System.Collections.Generic;
using Marco.Core.Breath;
using Marco.Core.GameFlow;
using Marco.Core.Locomotion;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.GameFlow;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// [블록 7-E] 통합 시나리오 1~5 — 실제 Core 클래스(Valve · EscapeGateLatch · ServerRoundDriver ·
    /// DrainHatch · BreathGauge · CampingMonitor)를 엮어 규칙이 <b>함께</b> 성립하는지 고정한다.
    /// 시나리오 6(전체 루프: 말하기→파문→잔상→게이지→추격→태그→소나→노크)은 네트워크·렌더링이
    /// 필요해 실기 검증 항목이다(docs/수동검증_절차.md).
    /// </summary>
    public class PrototypeScenarioTests
    {
        private const float Eps = 1e-3f;

        private static Valve NewValve(ValveId id)
        {
            var v = new Valve(ValveOccupancy.RotateSeconds(id));
            v.Configure(id);
            return v;
        }

        private static void TickAll(IEnumerable<Valve> valves, float seconds, float step = 0.5f)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += step)
                foreach (Valve v in valves)
                    v.Tick(Mathf.Min(step, seconds - t));
        }

        private static int CountOpen(IEnumerable<Valve> valves)
        {
            int n = 0;
            foreach (Valve v in valves)
                if (v.State == ValveState.Open)
                    n++;
            return n;
        }

        // ── 시나리오 1 — 정상 클리어 (5인, 활성 A·B·C·D, 요구 3) ────────────

        [Test]
        public void Scenario1_NormalClear_FivePlayers()
        {
            const int total = 5;
            Assert.AreEqual(3, ValveRoster.RequiredOpenCount(total));
            Assert.AreEqual(4, ValveRoster.ActiveCount(total));

            Valve a = NewValve(ValveId.A), b = NewValve(ValveId.B), c = NewValve(ValveId.C), d = NewValve(ValveId.D);
            var all = new[] { a, b, c, d };
            var latch = new EscapeGateLatch();
            latch.ResetIfNewRound(1);

            a.TryInteract(1, RoleType.Runner);
            TickAll(all, 8f);                                  // t=8  A 개방(역류 180초 시작)
            c.TryInteract(2, RoleType.Runner);
            TickAll(all, 8f);                                  // t=16 C 개방
            Assert.IsFalse(latch.Update(CountOpen(all), 3));
            b.TryInteract(3, RoleType.Runner);
            TickAll(all, 5f);                                  // t=21 B 개방 → 동시 3
            Assert.AreEqual(3, CountOpen(all));
            Assert.IsTrue(latch.Update(CountOpen(all), 3), "동시 3 → 게이트 Open");

            TickAll(all, 200f);                                // t=221 A 역류 완료(218) → Closed
            Assert.AreEqual(ValveState.Closed, a.State);
            latch.Update(CountOpen(all), 3);
            Assert.IsTrue(latch.IsOpen, "§6.1-2 latch — 역류로 닫혀도 게이트는 Open 유지");

            // 도망자 4 → 탈출 요구 ⌈4/2⌉ = 2.
            int runners = RoleAssigner.RunnersFor(total);
            Assert.AreEqual(2, ValveRoster.EscapeRequirement(runners));
            var round = new ServerRoundDriver(600f);
            Assert.IsTrue(round.TryRegisterEscape(11, RoleType.Runner, latch.IsOpen));
            Assert.IsFalse(round.Evaluate(round.Census(runners, 0)), "1명 탈출 — 아직");
            Assert.IsTrue(round.TryRegisterEscape(12, RoleType.Runner, latch.IsOpen));
            Assert.IsTrue(round.Evaluate(round.Census(runners, 0)));
            Assert.AreEqual(RoundResult.RunnersWin, round.Result);
        }

        // ── 시나리오 2 — 감쇠 (v0.4 핵심) ──────────────────────────────────

        [Test]
        public void Scenario2_Decay_TakeoverResumesFromRemaining()
        {
            Valve v = NewValve(ValveId.A);
            v.TryInteract(1, RoleType.Runner);
            v.Tick(5f);
            Assert.AreEqual(0.625f, v.Progress01, Eps, "5초 회전 = 5/8");

            v.Interrupt(1);
            v.Tick(2.99f);
            Assert.AreEqual(0.625f, v.Progress01, Eps, "유예 중 — 손실 없음");
            Assert.IsFalse(v.IsDecaying, "2.99초 — 아직 유예(3.00초 정각부터 감쇠)");

            v.Tick(5.01f);                                      // 중단 후 8초 = 유예 3 + 감쇠 5
            Assert.AreEqual(0.125f, v.Progress01, Eps, "-0.10/s × 5");
            Assert.IsTrue(v.IsDecaying, "HUD 감쇠 색의 근거");

            Assert.AreEqual(ValveInteractionRejection.None, v.TryInteract(2, RoleType.Runner), "다른 도망자가 이어받기");
            v.Tick(6.99f);
            Assert.AreEqual(ValveState.Rotating, v.State);
            v.Tick(0.02f);
            Assert.AreEqual(ValveState.Open, v.State, "남은 0.125에서 이어져 7초 만에 완료");
        }

        [Test]
        public void Scenario2_Decay_ThirteenSecondsIsTotalLoss()
        {
            // §6.1 "전손 13초" = 유예 3 + 1.0 ÷ 0.10.
            Valve v = NewValve(ValveId.A);
            v.TryInteract(1, RoleType.Runner);
            v.Tick(7.99f);
            v.Interrupt(1);
            v.Tick(13f);
            Assert.AreEqual(0f, v.Progress01, Eps);
            Assert.AreEqual(ValveState.Closed, v.State);
        }

        // ── 시나리오 3 — 역류 실패 ────────────────────────────────────────

        [Test]
        public void Scenario3_ReflowFailure_SeekerWinsOnTime()
        {
            Valve a = NewValve(ValveId.A);
            var all = new[] { a };
            var latch = new EscapeGateLatch();
            latch.ResetIfNewRound(1);

            a.TryInteract(1, RoleType.Runner);
            TickAll(all, 8f);
            Assert.AreEqual(ValveState.Open, a.State);

            TickAll(all, Valve.OpenHoldSeconds);               // t=188
            Assert.AreEqual(ValveState.Reflowing, a.State, "180초 → 역류");
            TickAll(all, Valve.ReflowSeconds);                 // t=218
            Assert.AreEqual(ValveState.Closed, a.State, "30초 → 완전 폐쇄");

            latch.Update(CountOpen(all), 3);
            Assert.IsFalse(latch.IsOpen, "게이트는 한 번도 열리지 않았다");

            var round = new ServerRoundDriver(600f);
            Assert.IsFalse(round.TryRegisterEscape(11, RoleType.Runner, latch.IsOpen), "게이트 닫힘 — 탈출 불가");
            round.Tick(600f);
            Assert.IsTrue(round.Evaluate(round.Census(4, 0)));
            Assert.AreEqual(RoundResult.SeekerWin, round.Result, "시간 종료");
        }

        // ── 시나리오 4 — 봉쇄 무력화(활성 = 요구 + 1) ──────────────────────

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public void Scenario4_GuardingAnyOneValve_OthersStillOpenGate(int totalPlayers)
        {
            int required = ValveRoster.RequiredOpenCount(totalPlayers);
            List<ValveId> active = ValveRoster.SelectActive(totalPlayers, seed: 17);
            Assert.AreEqual(required + 1, active.Count, "§6.2 불변 조건");

            foreach (ValveId guarded in active)
            {
                var latch = new EscapeGateLatch();
                latch.ResetIfNewRound(1);
                var opened = new List<Valve>();
                foreach (ValveId id in active)
                {
                    if (id == guarded)
                        continue;           // 술래가 이 밸브를 계속 지킨다
                    Valve v = NewValve(id);
                    v.TryInteract(1, RoleType.Runner);
                    v.Tick(ValveOccupancy.RotateSeconds(id));
                    opened.Add(v);
                }

                latch.Update(CountOpen(opened), required);
                Assert.IsTrue(latch.IsOpen, $"{totalPlayers}인 — {guarded}를 지켜도 나머지로 게이트가 열린다");
            }
        }

        [Test]
        public void GateLatch_OpenCloseOpenClose_NeverOpens()
        {
            // 누적이 아니라 **동시** 개방 수다 — "A 열고 닫고, B 열고 닫고"로는 열리지 않는다.
            var latch = new EscapeGateLatch();
            latch.ResetIfNewRound(1);
            Assert.IsFalse(latch.Update(1, 2));
            Assert.IsFalse(latch.Update(0, 2));
            Assert.IsFalse(latch.Update(1, 2));
            Assert.IsFalse(latch.IsOpen);
        }

        [Test]
        public void GateLatch_ResetsOnNewRound_RematchKeepsMap()
        {
            // [블록 7 결함 수정] 리매치는 맵을 다시 로드하지 않는다 — 라운드 번호로 풀려야 한다.
            var latch = new EscapeGateLatch();
            latch.ResetIfNewRound(3);
            latch.Update(3, 3);
            Assert.IsTrue(latch.IsOpen);

            latch.ResetIfNewRound(3);
            Assert.IsTrue(latch.IsOpen, "같은 라운드 — 유지");
            latch.ResetIfNewRound(4);
            Assert.IsFalse(latch.IsOpen, "새 라운드 — 해제");
        }

        // ── 시나리오 5 — 최후 생존자 (4인) ─────────────────────────────────

        [Test]
        public void Scenario5_LastSurvivor_DrainDives_TeamWins()
        {
            const int total = 4;
            int runners = RoleAssigner.RunnersFor(total);   // 3
            var census = new RunnerCensus(runners, taggedOut: 2, escaped: 0);
            Assert.IsTrue(census.ShouldEnterLastSurvivorPhase, "도망자 2명 태그 → 1명 남음");

            var round = new ServerRoundDriver(600f);
            round.Tick(200f);
            Assert.IsTrue(round.TryEnterLastSurvivorPhase());

            // 게이트 닫힘 → 배수구 활성. 동시 개방 1개 → T = 11초, 총 점유 13 > 게이지 12 → 2회 잠수.
            Assert.IsTrue(DrainSelection.ShouldActivate(gateOpen: false));
            Assert.AreEqual(2, DrainConfig.RequiredDives(1, BreathConfig.TotalSeconds));

            // [커밋 전 수정 4-1 의미 변경] 잠수 구간 = 진입 1 + 작업 + 부상 1(밸브 B·E와 같은 세션), 부상 뒤 통과.
            //   0개 개방에서 1개 개방으로 옮겼다 — 이 시뮬레이터(숨은 물 위에서만 채움)로는 0개 개방을 질식 없이 90초 안에
            //   나갈 수 없다. 물 밖 회복(+4/s)을 쓰면 0개도 2회로 나간다(09-23 재검증 — DrainDiveTests 주석).
            var sim = new DrainDiveSim(openValves: 1) { ReleaseAtBreath = 1.5f, PressAtBreath = 11f };
            while (!sim.Escaped && sim.Elapsed < DrainConfig.PhaseSeconds)
            {
                sim.Step(0.5f);
                round.Tick(0.5f);
            }

            Assert.IsTrue(sim.Escaped, $"90초 안에 배수구 탈출(경과 {sim.Elapsed:0.0}초)");
            Assert.AreEqual(2, sim.Dives, "§6.5-2 1개 개방 → 2회 잠수");
            Assert.AreEqual(0, sim.ForcedSurfaces, "질식 없이");
            Assert.IsTrue(round.TryRegisterDrainEscape(9, RoleType.Runner));
            Assert.IsTrue(round.Evaluate(round.Census(runners, 2)));
            Assert.AreEqual(RoundResult.RunnersWin, round.Result, "마지막 1인의 탈출 = 팀 승리");
        }

        [Test]
        public void Scenario5_SeekerWaitingAtPool_CampingFiresAfter20s()
        {
            // 술래는 잠수할 수 없다 → "잠수 중 미발동"에 걸리지 않는다 → 20초 뒤 호흡음(§6.5-3).
            Assert.IsFalse(DiveRules.CanDive(RoleType.Seeker));
            var m = new CampingMonitor();
            float radius = 0f;
            for (int i = 0; i < 40 && radius <= 0f; i++)
            {
                bool suspended = DiveRules.CanDive(RoleType.Seeker); // 서버 TickCamping과 같은 조건식
                radius = m.Tick(0.5f, 0f, suspended);
            }

            Assert.AreEqual(3f, radius, Eps, "20초 — 호흡음 3m, 위치 광고 시작");
        }

        [Test]
        public void Scenario5_SubmergedRunnerAtDrain_CampingSuspended()
        {
            // 도망자 잠수 중은 미발동(§3.6) — 배수구 작업 중 호흡음이 겹치지 않는다(강제 파문이 이미 있다).
            var m = new CampingMonitor();
            float radius = 0f;
            for (int i = 0; i < 60; i++)
                radius += m.Tick(0.5f, 0f, suspended: DiveRules.CanDive(RoleType.Runner));
            Assert.AreEqual(0f, radius);
        }

        // ── 블록 7 부속 규칙 ──────────────────────────────────────────────

        [Test]
        public void ValvePulse_PerValve_RadiusAndDuration()
        {
            // §6.1 A ×0.5(12→6m), §5.1 [v0.4] 지속 = 각 밸브 회전 시간.
            Assert.AreEqual(6f, ValveOccupancy.SoundRadiusMeters(ValveId.A), Eps);
            Assert.AreEqual(12f, ValveOccupancy.SoundRadiusMeters(ValveId.B), Eps);
            Assert.AreEqual(8f, ValveOccupancy.PulseDurationSeconds(ValveId.A), Eps);
            Assert.AreEqual(5f, ValveOccupancy.PulseDurationSeconds(ValveId.B), Eps);
            Assert.AreEqual(7f, ValveOccupancy.PulseDurationSeconds(ValveId.E), Eps);
        }

        [Test]
        public void ServerPulse_DurationOverride_Registered()
        {
            var driver = new ServerPulseDriver();
            Assert.GreaterOrEqual(driver.AddPulse(1, SoundType.Valve, Vector3.zero, 0f, radiusOverride: 6f, durationOverride: 8f), 0);
        }

        /// <summary>
        /// §12.4 정본 "맵 평면도를 30초간" — 코드에 남긴 기획서 값(<see cref="BriefingConfig.DesignSeconds"/>, 지금은 미사용).
        /// 정식 밸런스로 복원할 때(<c>Seconds = DesignSeconds</c>) 30초가 맞는지 보장하는 안전장치다.
        /// </summary>
        [Test]
        public void BriefingDesignSeconds_MatchDesignDoc()
        {
            Assert.AreEqual(30f, BriefingConfig.DesignSeconds, Eps); // §12.4 "30초간"
        }

        /// <summary>
        /// <b>기획서 값이 아니다.</b> 09-24 테스트 반복용 임시값(7초)을 고정한다 — 정본은 위 테스트(30초).
        /// 이 테스트가 통과한다고 코드가 기획서를 따른다는 뜻이 아니다. 정식 밸런스로 복원하면 이 테스트는
        /// <b>실패하는 것이 맞다</b> — 그때 이 테스트를 지운다.
        /// </summary>
        [Test]
        public void BriefingSeconds_MatchPrototypeOverride()
        {
            Assert.AreEqual(7f, BriefingConfig.Seconds, Eps,
                "브리핑이 임시값 7초가 아니다 — 정식 밸런스(Seconds = DesignSeconds)로 복원했다면 이 테스트를 지운다.");
        }

        [Test]
        public void OpeningGuideSeconds_MatchDesignDoc()
        {
            Assert.AreEqual(20f, BriefingConfig.OpeningGuideSeconds, Eps); // §12.4 "첫 20초"
        }

        [Test]
        public void StepPhase_ZeroAtFootstep_BobSyncSource()
        {
            // 연출.md §2.1 "발이 닿는 순간 = 파문" — 위상 0이 곧 파문 순간이다.
            var sim = new LocomotionSimulator(RoleType.Runner);
            var walk = new LocomotionInput(new Vector2(0f, 1f), false, false, false);

            LocomotionTick t1 = sim.Tick(walk, 0.2f);                  // 1.0m
            Assert.IsFalse(t1.Pulse.HasValue);
            Assert.AreEqual(0.5f, sim.StepPhase01, Eps);

            LocomotionTick t2 = sim.Tick(walk, 0.2f);                  // 2.0m → 파문
            Assert.IsTrue(t2.Pulse.HasValue);
            Assert.AreEqual(0f, sim.StepPhase01, Eps, "파문 순간 위상 0 = bob 최저점");

            sim.Tick(new LocomotionInput(Vector2.zero, false, false, false), 0.1f);
            Assert.AreEqual(0f, sim.StepPhase01, Eps, "정지 — 0");
        }

        [Test]
        public void BreathClientState_Prediction()
        {
            BreathClientState.ResetForNewSession();
            Assert.IsTrue(BreathClientState.CanSubmerge);
            Assert.AreEqual(1f, BreathClientState.SpeedMultiplier, Eps);

            BreathClientState.Apply(0f, submerged: false, chokePenalty: true);
            Assert.IsFalse(BreathClientState.CanSubmerge, "§5.9-1 강제 부상 예측");
            Assert.AreEqual(0.8f, BreathClientState.SpeedMultiplier, Eps, "§5.9-1 질식 -20%");
            BreathClientState.ResetForNewSession();
        }

        [Test]
        public void MapPlan_Normalize()
        {
            var bounds = new Rect(0f, 0f, 52f, 40f);
            Assert.AreEqual(new Vector2(0.5f, 0.5f), MapPlanData.Normalize(bounds, new Vector2(26f, 20f)));
            Assert.AreEqual(Vector2.zero, MapPlanData.Normalize(bounds, Vector2.zero));
        }

        [Test]
        public void Hud_Block7Formats()
        {
            Assert.AreEqual("탈출 ●○ / 요구 2", HudFormatter.FormatEscapeBoard(1, 2));
            Assert.AreEqual("탈출 ●●● / 요구 2", HudFormatter.FormatEscapeBoard(3, 2), "요구 초과도 표시");
            Assert.AreEqual("숨 ■■■■■■□□□□ 7.5초", HudFormatter.FormatBreath(7.55f));
            Assert.AreEqual("숨 □□□□□□□□□□ 0.0초", HudFormatter.FormatBreath(-1f));
            Assert.AreEqual("↑", HudFormatter.DirectionArrow(0f));
            Assert.AreEqual("↑", HudFormatter.DirectionArrow(22.4f));
            Assert.AreEqual("↗", HudFormatter.DirectionArrow(22.6f));
            Assert.AreEqual("←", HudFormatter.DirectionArrow(-90f));
            Assert.AreEqual("↓", HudFormatter.DirectionArrow(180f));
            StringAssert.Contains("활성 밸브 4개 · 요구 3개 · 0:30", HudFormatter.FormatBriefingTitle(4, 3, 30f));
            Assert.AreNotEqual(HudFormatter.FormatOpeningGuide(0), HudFormatter.FormatOpeningGuide(1));
        }
    }
}
