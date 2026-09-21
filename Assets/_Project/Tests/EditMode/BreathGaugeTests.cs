using Marco.Core.Breath;
using Marco.Core.Locomotion;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §5.9-1 숨 게이지(5단계).
    ///
    /// 이 파일의 중심은 §5.9-1이 직접 적어 둔 **"상태별 소요 시간(계산 검증)" 표 4행**을
    /// 그대로 재현하는 것이다 — 소모·대기·회복이 하나라도 어긋나면 그 표에서 잡힌다.
    /// </summary>
    public class BreathGaugeTests
    {
        /// <summary>작은 틱으로 누적 진행한다(프레임률 의존성을 배제하려 0.02초 고정).</summary>
        private static void Advance(BreathGauge gauge, BreathZone zone, float seconds)
        {
            const float Step = 0.02f;
            int steps = (int)(seconds / Step + 0.5f);
            for (int i = 0; i < steps; i++)
                gauge.Tick(zone, Step);
        }

        // ── §5.9-1 상수 ──────────────────────────────────────────────────

        [Test]
        public void Config_MatchesDesignDoc()
        {
            Assert.AreEqual(12f, BreathConfig.TotalSeconds);   // §5.9-1 [v0.4] "총량 12초"
            Assert.AreEqual(1f, BreathConfig.DivePerSecond);
            Assert.AreEqual(4.5f, BreathConfig.SuppressionCost); // §5.9-1 [v0.4] "비명 억제 -4.5"
            Assert.AreEqual(2f, BreathConfig.SurfaceRecoveryPerSecond);
            Assert.AreEqual(4f, BreathConfig.OutOfWaterRecoveryPerSecond);
            Assert.AreEqual(2f, BreathConfig.RecoveryDelaySeconds);
            Assert.AreEqual(3f, BreathConfig.ChokePenaltySeconds);
            Assert.AreEqual(0.8f, BreathConfig.ChokeSpeedMultiplier);
        }

        [Test]
        public void MaxConsecutiveSuppressions_IsDerivedNotHardcoded()
        {
            // §5.9-1 [v0.4] "연속 억제 최대 2회 — 게이지 12초 ÷ 억제 4.5초 = 2.67".
            Assert.AreEqual(2, BreathConfig.MaxConsecutiveSuppressions);
            Assert.AreEqual((int)(BreathConfig.TotalSeconds / BreathConfig.SuppressionCost),
                BreathConfig.MaxConsecutiveSuppressions);

            // §5.9-1 [v0.4]가 명시적으로 막은 결과를 고정한다 — 총량만 8→12로 올리고
            // 비용을 3으로 두면 이 값이 4가 되어 "3.5절 외침의 위력이 약해진다".
            Assert.AreNotEqual(4, BreathConfig.MaxConsecutiveSuppressions,
                "총량 12초에 억제 비용 3초를 쓰면 4회가 된다 — §5.9-1이 4.5초로 비율을 고정한 이유다.");
            Assert.AreEqual(2.666f, BreathConfig.TotalSeconds / BreathConfig.SuppressionCost, 0.001f,
                "§5.9-1 '12 ÷ 4.5 = 2.67회로 비율을 고정'.");
        }

        [Test]
        public void NewGauge_StartsFull()
        {
            Assert.AreEqual(BreathConfig.TotalSeconds, new BreathGauge().Current, 0.0001f);
        }

        // ── 잠수 상태를 새로 만들지 않았다 (MovementState.Diving 재사용) ──

        [TestCase(MovementState.Diving, true, BreathZone.Submerged)]
        [TestCase(MovementState.Diving, false, BreathZone.Submerged)]
        [TestCase(MovementState.Walk, true, BreathZone.Surface)]
        [TestCase(MovementState.Idle, true, BreathZone.Surface)]
        [TestCase(MovementState.Sprint, false, BreathZone.OutOfWater)]
        [TestCase(MovementState.Idle, false, BreathZone.OutOfWater)]
        public void ZoneOf_DerivesFromExistingMovementState(MovementState state, bool onWater, BreathZone expected)
        {
            // §4.3 잠수 진입 판정은 이미 LocomotionSimulator가 소유한다 —
            // 숨 게이지는 그 결과를 읽기만 한다(두 곳에서 판정하면 반드시 어긋난다).
            Assert.AreEqual(expected, BreathConfig.ZoneOf(state, onWater));
        }

        // ── 소모 ─────────────────────────────────────────────────────────

        [Test]
        public void Dive_DrainsOnePerSecond()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 3f);

            Assert.AreEqual(9f, g.Current, 0.01f); // 12 - 1/초 × 3초
        }

        [Test]
        public void Dive_StopsAtZero_NeverNegative()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 20f); // 총량의 2배 넘게 잠수

            Assert.AreEqual(0f, g.Current, 0.0001f);
        }

        [Test]
        public void Suppression_CostsFourPointFiveImmediately()
        {
            var g = new BreathGauge();

            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(7.5f, g.Current, 0.0001f); // 12 - 4.5
        }

        // ── 회복 ─────────────────────────────────────────────────────────

        [Test]
        public void Recovery_DoesNotStartDuringDelay()
        {
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.OutOfWater); // 7.5 남음, 대기 2초

            Advance(g, BreathZone.OutOfWater, 1.9f);

            Assert.IsTrue(g.IsRecoveryDelayed);
            Assert.AreEqual(7.5f, g.Current, 0.01f, "회복 대기 중에는 1도 회복하면 안 된다(§5.9-1).");
        }

        [Test]
        public void Recovery_CapsAtTotal()
        {
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.OutOfWater);
            Advance(g, BreathZone.OutOfWater, 60f);

            Assert.AreEqual(BreathConfig.TotalSeconds, g.Current, 0.0001f);
        }

        [Test]
        public void Submerging_ResetsRecoveryDelay()
        {
            // §5.9-1 "회복 중 다시 잠수하면 즉시 중단되고 초당 -1로 전환(회복 대기도 2초 리셋)".
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.OutOfWater); // 5, 대기 2초
            Advance(g, BreathZone.OutOfWater, 2.5f);    // 대기 소진 + 0.5초 회복(+2) → 7
            Assert.IsFalse(g.IsRecoveryDelayed);

            Advance(g, BreathZone.Submerged, 1f);       // 잠수 1초 → -1, 대기 다시 2초
            Assert.IsTrue(g.IsRecoveryDelayed);

            // 부상 직후 2초는 회복이 없다.
            float afterDive = g.Current;
            Advance(g, BreathZone.OutOfWater, 1.9f);
            Assert.AreEqual(afterDive, g.Current, 0.01f);
        }

        // ── §5.9-1 "상태별 소요 시간(계산 검증)" 표 4행 ──────────────────

        /// <summary>만충까지 걸린 시간을 0.02초 단위로 잰다.</summary>
        private static float SecondsToFull(BreathGauge gauge, BreathZone zone)
        {
            const float Step = 0.02f;
            float elapsed = 0f;

            for (int i = 0; i < 10000; i++)
            {
                if (gauge.Current >= BreathConfig.TotalSeconds - 0.0001f)
                    return elapsed;

                gauge.Tick(zone, Step);
                elapsed += Step;
            }

            return float.PositiveInfinity;
        }

        [Test]
        public void DocTable_LandSuppression_TakesThreePointOneTwoFive()
        {
            // §5.9-1 [v0.4]: 육상 비명 억제 1회 → -4.5 / 대기 2초 / 회복 1.125초 / 총 3.125초
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.OutOfWater);

            // 3.125초는 0.02초 격자에 떨어지지 않아 측정 최소 단위가 0.02초 위로 뜬다
            // (실측 3.16초). 표의 값을 바꾸지 않고 격자 폭만 허용한다.
            Assert.AreEqual(3.125f, SecondsToFull(g, BreathZone.OutOfWater), 0.04f);
        }

        [Test]
        public void DocTable_SurfaceSuppression_TakesFourPointTwoFive()
        {
            // §5.9-1 [v0.4]: 수면에서 비명 억제 1회 → -4.5 / 대기 2초 / 회복 2.25초 / 총 4.25초
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.Surface);

            Assert.AreEqual(4.25f, SecondsToFull(g, BreathZone.Surface), 0.03f);
        }

        [Test]
        public void DocTable_UnderwaterValveDive_TakesFour()
        {
            // §5.9-1 [v0.4]: 수중 밸브 1회(8초) 후 물 밖 → -8 / 대기 2초 / 회복 2.0초 / 총 4.0초.
            // 8초는 §6.1 [v0.4] 수중 밸브 총 점유(B: 1.5+5.0+1.5 / E: 0.5+7.0+0.5)와 같은 값이다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 8f);
            Assert.AreEqual(4f, g.Current, 0.02f);

            Assert.AreEqual(4.0f, SecondsToFull(g, BreathZone.OutOfWater), 0.03f);
        }

        [Test]
        public void DocTable_FullDepletion_TakesFive()
        {
            // §5.9-1 [v0.4]: 전체 고갈(12초) 후 물 밖 → -12 / 대기 2초 / 회복 3.0초 / 총 5.0초
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 12f);
            Assert.AreEqual(0f, g.Current, 0.02f);

            Assert.AreEqual(5.0f, SecondsToFull(g, BreathZone.OutOfWater), 0.03f);
        }

        // ── 질식 (§5.9-1 고갈 페널티) ────────────────────────────────────

        [Test]
        public void Choke_FiresExactlyOnceWhenGaugeHitsZero()
        {
            var g = new BreathGauge();
            int chokes = 0;

            for (int i = 0; i < 900; i++) // 18초 잠수 — 12초에 고갈된다(§5.9-1 [v0.4])
            {
                if (g.Tick(BreathZone.Submerged, 0.02f).Choked)
                    chokes++;
            }

            Assert.AreEqual(1, chokes, "질식은 0으로 떨어지는 그 틱에만 1회여야 한다.");
        }

        [Test]
        public void Choke_AppliesSpeedPenaltyForThreeSeconds()
        {
            var g = new BreathGauge();

            // 8초를 한 틱으로 소모한다 — 0.02초씩 400회 더하면 부동소수 잔차 때문에
            // 게이지가 정확히 0에 닿지 않아 질식 판정이 흔들린다.
            Assert.IsTrue(g.Tick(BreathZone.Submerged, BreathConfig.TotalSeconds).Choked);

            Assert.IsTrue(g.IsChokePenaltyActive);
            Assert.AreEqual(BreathConfig.ChokeSpeedMultiplier, g.SpeedMultiplier, 0.0001f);

            Advance(g, BreathZone.OutOfWater, 2.9f);
            Assert.IsTrue(g.IsChokePenaltyActive, "3초 전에 풀리면 안 된다.");

            Advance(g, BreathZone.OutOfWater, 0.2f);
            Assert.IsFalse(g.IsChokePenaltyActive);
            Assert.AreEqual(1f, g.SpeedMultiplier, 0.0001f);
        }

        [Test]
        public void Choke_ForcesSurfacing_CannotSubmergeAtZero()
        {
            var g = new BreathGauge();
            Assert.IsTrue(g.CanSubmerge);

            g.Tick(BreathZone.Submerged, BreathConfig.TotalSeconds);

            Assert.IsFalse(g.CanSubmerge, "§5.9-1 '강제 부상' — 숨 0으로는 잠수를 유지할 수 없다.");
        }

        [Test]
        public void ForcedSurfacing_IsHonoredByLocomotionSimulator()
        {
            // 잠수 판정을 새로 만들지 않았다는 것의 실증 — 게이지의 CanSubmerge를 넘기면
            // 기존 시뮬레이터가 Diving 진입을 거부한다.
            var sim = new LocomotionSimulator(Role.RoleType.Runner);

            var canDive = new LocomotionInput(new UnityEngine.Vector2(0f, 1f),
                sprintHeld: false, diveHeld: true, isOnWaterSurface: true, canSubmerge: true);
            Assert.AreEqual(MovementState.Diving, sim.Tick(canDive, 0.02f).State);

            var exhausted = new LocomotionInput(new UnityEngine.Vector2(0f, 1f),
                sprintHeld: false, diveHeld: true, isOnWaterSurface: true, canSubmerge: false);
            Assert.AreNotEqual(MovementState.Diving, sim.Tick(exhausted, 0.02f).State);
        }

        [Test]
        public void ChokeSpeedPenalty_SlowsLocomotion()
        {
            // §5.9-1 "이동속도 -20%" — 실제 이동속도가 줄어야 §5.1 발소리 등급에도 반영된다.
            var sim = new LocomotionSimulator(Role.RoleType.Runner);

            var penalized = new LocomotionInput(new UnityEngine.Vector2(0f, 1f),
                sprintHeld: false, diveHeld: false, isOnWaterSurface: false,
                canSubmerge: true, speedMultiplier: BreathConfig.ChokeSpeedMultiplier);

            Assert.AreEqual(LocomotionConfig.RunnerWalkSpeed * 0.8f,
                sim.Tick(penalized, 0.02f).LocalVelocity.magnitude, 0.001f);
        }

        // ── 억제 가능/불가 경계 ──────────────────────────────────────────

        [Test]
        public void Suppress_ExactlyCostRemaining_Succeeds()
        {
            // §3.5 [v0.4] "게이지 4.5초 미만 — 억제 불가" → 정확히 4.5는 가능한 쪽이다.
            // 0.02초씩 375회 누적하면 부동소수 잔차로 4.49999가 되어 경계가 반대로 넘어간다.
            // 경계를 재는 테스트이므로 단일 틱으로 정확히 7.5초를 소모한다
            // (Choke_AppliesSpeedPenaltyForThreeSeconds가 같은 이유로 이미 이 방식을 쓴다).
            var g = new BreathGauge();
            g.Tick(BreathZone.Submerged, 7.5f); // 12 - 7.5 = 4.5 정확히
            Assert.AreEqual(4.5f, g.Current, 0.0001f);

            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(0f, g.Current, 0.03f);
        }

        [Test]
        public void Suppress_BelowCost_FailsAndLeavesGaugeUntouched()
        {
            // §3.5 [v0.4] "게이지 4.5초 미만 — 억제 불가. 비명 강제 발생". 부분 차감도 음수도 없다.
            // 8초는 §5.9-1 [v0.4]가 지목한 실제 상황이다 — 수중 밸브 1회 직후 잔여 4.0.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 8f); // 잔여 4.0 < 4.5
            float before = g.Current;

            Assert.AreEqual(SuppressionResult.NotEnoughBreath, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(before, g.Current, 0.0001f);
            Assert.GreaterOrEqual(g.Current, 0f);
        }

        [Test]
        public void Suppress_AtZero_FailsAndStaysAtZero()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 12f);

            Assert.AreEqual(SuppressionResult.NotEnoughBreath, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(0f, g.Current, 0.0001f);
        }

        // ── §5.9-1 "연속 억제 최대 2회" ─────────────────────────────────

        [Test]
        public void ConsecutiveSuppressions_MaxTwoOnFullGauge()
        {
            var g = new BreathGauge();

            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater)); // 12 → 7.5
            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater)); // 7.5 → 3.0
            Assert.AreEqual(SuppressionResult.NotEnoughBreath, g.TrySuppressScream(BreathZone.OutOfWater));

            // v0.4에서 총량이 8→12로 늘었어도 2회는 그대로다 — 비용이 3→4.5로 함께 올랐다.
            Assert.AreEqual(BreathConfig.MaxConsecutiveSuppressions, 2);
            Assert.AreEqual(3f, g.Current, 0.0001f, "12 - 4.5 - 4.5 = 3.0 (4.5 미만이라 3회는 불가).");
        }

        [Test]
        public void ShoutCooldownAlone_NeverTriggersTheLimit()
        {
            // §5.9-1: "외침 쿨다운이 45초라 외침만으로는 이 한계가 발동하지 않는다
            //          (45초면 어떤 상태에서든 만충)." — 가장 느린 회복(수면)으로도 성립하는가.
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.Surface);

            Advance(g, BreathZone.Surface, Sound.SeekerShoutConfig.CooldownSeconds);

            Assert.AreEqual(BreathConfig.TotalSeconds, g.Current, 0.0001f,
                "45초 뒤에는 어떤 상태에서든 만충이라 육상·수면에서는 억제가 항상 가능하다.");
        }

        [TestCase(7.5f, true)]  // §5.9-1 [v0.4] 표: 7.5초 잠수(-7.5, 잔여 4.5) 직후 → 억제 가능하나 게이지 0
        [TestCase(8f, false)]   // §5.9-1 [v0.4] 표: 8초 잠수(수중 밸브 1회, 잔여 4.0) → 억제 불가, 비명 강제
        public void DocTable_DiveThenShout(float diveSeconds, bool canSuppress)
        {
            // 7.5초 행이 정확히 비용 경계(잔여 4.5)라 누적 오차가 판정을 뒤집는다 — 단일 틱.
            var g = new BreathGauge();
            g.Tick(BreathZone.Submerged, diveSeconds);

            Assert.AreEqual(canSuppress, g.CanSuppress,
                "§5.9-1 '이 제약이 실제로 작동하는 것은 잠수와 겹칠 때뿐'이라는 표와 어긋난다.");
        }

        // ── 잠수 중 억제 (경계 조건) ─────────────────────────────────────

        [Test]
        public void Suppress_WhileSubmerged_IsAutomaticAndFree()
        {
            // §3.5 "잠수 중 | 이미 게이지 소모 중 | 자동 억제(물속이라 비명 못 지름)".
            // 억제 -4.5와 잠수 -1/초가 같은 프레임에 이중으로 빠지지 않는다는 것이 핵심이다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 2f); // 잔여 10
            float before = g.Current;

            Assert.AreEqual(SuppressionResult.SuppressedByDive, g.TrySuppressScream(BreathZone.Submerged));
            Assert.AreEqual(before, g.Current, 0.0001f, "잠수 자동 억제에는 추가 비용이 없다(§3.5).");
        }

        [Test]
        public void Suppress_WhileSubmergedBelowCost_StillSucceeds()
        {
            // 잔여 4.0에서도 잠수 중이면 억제된다 — 비용이 0이라 "4.5 미만" 규칙이 적용되지 않는다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 8f);
            Assert.IsFalse(g.CanSuppress);

            Assert.AreEqual(SuppressionResult.SuppressedByDive, g.TrySuppressScream(BreathZone.Submerged));
        }

        [Test]
        public void Suppress_ThenSameFrameDiveTick_DoesNotDoubleSpend()
        {
            // 같은 프레임에 억제와 잠수 소모가 겹치는 경우. 잠수 중이면 억제는 무료이므로
            // 그 프레임의 감소분은 잠수 소모(-1 × dt)뿐이어야 한다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 1f); // 잔여 11
            float before = g.Current;

            g.TrySuppressScream(BreathZone.Submerged);
            g.Tick(BreathZone.Submerged, 0.02f);

            Assert.AreEqual(before - BreathConfig.DivePerSecond * 0.02f, g.Current, 0.0001f);
        }

        [Test]
        public void Suppress_OnSurface_ThenSameFrameTick_HasNoRecovery()
        {
            // 물 위에서 억제한 프레임에 곧바로 회복이 붙으면 §5.9-1 검증표의 합이 어긋난다.
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.Surface);
            g.Tick(BreathZone.Surface, 0.02f);

            Assert.AreEqual(7.5f, g.Current, 0.0001f); // 12 - 4.5
        }

        // ── 라운드 초기화 ────────────────────────────────────────────────

        [Test]
        public void Reset_RestoresFullGaugeAndClearsFlags()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 12f);

            g.Reset();

            Assert.AreEqual(BreathConfig.TotalSeconds, g.Current, 0.0001f);
            Assert.IsFalse(g.IsRecoveryDelayed);
            Assert.IsFalse(g.IsChokePenaltyActive);
            Assert.IsTrue(g.CanSubmerge);
        }

        // ── §6.5-2 배수구 "2회 잠수 필수" 부등식 [v0.4] ──────────────────

        /// <summary>
        /// §6.5-2 표를 그대로 고정한다 — 총 점유가 게이지 12초를 넘으면 2회 잠수가 강제된다.
        ///
        /// <para>
        /// 여기의 14·3·1은 <b>§6.5-2 표의 값을 테스트 안에서만</b> 쓴 것이다. 배수구 상수의
        /// 소유자는 블록 4(최후 생존자 페이즈)이며, 그때 <c>DrainConfig</c>가 생기면 이 테스트는
        /// 리터럴을 그 상수 참조로 바꿔야 한다. 지금 프로덕션 상수를 만들면 블록 4와 두 곳에
        /// 같은 값이 남는다(더블체크 8).
        /// </para>
        /// </summary>
        [TestCase(0, 14f, 16f, 2)] // §6.5-2: T=14, 총 16초 → 2회 잠수 필수
        [TestCase(1, 11f, 13f, 2)] // §6.5-2: T=11, 총 13초 → 2회 잠수 필수
        [TestCase(2, 8f, 10f, 1)]  // §6.5-2: T= 8, 총 10초 → 1회 잠수(잔여 2초)
        public void DocTable_DrainWork_ForcesDiveCount(int valvesOpen, float expectedT,
            float expectedOccupancy, int expectedDives)
        {
            // §6.5-2 "작업 시간 T = 14 − (동시 개방 밸브 수 × 3)"
            float t = 14f - valvesOpen * 3f;
            Assert.AreEqual(expectedT, t, 0.0001f);

            // §6.5-2 "총 점유(진입 1 + T + 부상 1)"
            float occupancy = 1f + t + 1f;
            Assert.AreEqual(expectedOccupancy, occupancy, 0.0001f);

            // 게이지 12초로 한 번에 덮을 수 있는가 — 이것이 "2회 잠수 필수"의 정의다.
            int dives = occupancy <= BreathConfig.TotalSeconds ? 1 : 2;
            Assert.AreEqual(expectedDives, dives,
                $"§6.5-2 표와 어긋난다 — 총 점유 {occupancy}초 vs 게이지 {BreathConfig.TotalSeconds}초.");
        }

        [Test]
        public void DrainWork_WouldCollapseToOneDive_IfGaugeWereLarger()
        {
            // §6.5-2의 "2회 잠수 필수"는 게이지 12초에 의존한다. 총량을 16초 이상으로 올리면
            // 밸브 0개 상태에서도 1회 잠수로 끝나 "부상하는 순간마다 위치가 노출된다"는
            // 페이즈 설계가 사라진다. 그 경계를 고정해 둔다.
            const float WorstCaseOccupancy = 16f; // 밸브 0개: 1 + 14 + 1

            Assert.Less(BreathConfig.TotalSeconds, WorstCaseOccupancy,
                "게이지가 총 점유 이상이면 §6.5-2가 성립하지 않는다.");
            Assert.Greater(BreathConfig.TotalSeconds, WorstCaseOccupancy / 2f,
                "게이지가 총 점유의 절반 미만이면 2회로도 부족해 3회 잠수가 된다(표에 없는 상태).");
        }
    }
}
