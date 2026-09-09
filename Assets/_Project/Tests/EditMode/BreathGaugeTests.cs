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
            Assert.AreEqual(8f, BreathConfig.TotalSeconds);
            Assert.AreEqual(1f, BreathConfig.DivePerSecond);
            Assert.AreEqual(3f, BreathConfig.SuppressionCost);
            Assert.AreEqual(2f, BreathConfig.SurfaceRecoveryPerSecond);
            Assert.AreEqual(4f, BreathConfig.OutOfWaterRecoveryPerSecond);
            Assert.AreEqual(2f, BreathConfig.RecoveryDelaySeconds);
            Assert.AreEqual(3f, BreathConfig.ChokePenaltySeconds);
            Assert.AreEqual(0.8f, BreathConfig.ChokeSpeedMultiplier);
        }

        [Test]
        public void MaxConsecutiveSuppressions_IsDerivedNotHardcoded()
        {
            // §5.9-1 "연속 억제 최대 2회 — 게이지 8초 ÷ 억제 3초".
            Assert.AreEqual(2, BreathConfig.MaxConsecutiveSuppressions);
            Assert.AreEqual((int)(BreathConfig.TotalSeconds / BreathConfig.SuppressionCost),
                BreathConfig.MaxConsecutiveSuppressions);
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

            Assert.AreEqual(5f, g.Current, 0.01f);
        }

        [Test]
        public void Dive_StopsAtZero_NeverNegative()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 20f); // 총량의 2배 넘게 잠수

            Assert.AreEqual(0f, g.Current, 0.0001f);
        }

        [Test]
        public void Suppression_CostsThreeImmediately()
        {
            var g = new BreathGauge();

            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(5f, g.Current, 0.0001f);
        }

        // ── 회복 ─────────────────────────────────────────────────────────

        [Test]
        public void Recovery_DoesNotStartDuringDelay()
        {
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.OutOfWater); // 5 남음, 대기 2초

            Advance(g, BreathZone.OutOfWater, 1.9f);

            Assert.IsTrue(g.IsRecoveryDelayed);
            Assert.AreEqual(5f, g.Current, 0.01f, "회복 대기 중에는 1도 회복하면 안 된다(§5.9-1).");
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
        public void DocTable_LandSuppression_TakesTwoPointSevenFive()
        {
            // §5.9-1: 육상 비명 억제 1회 → -3 / 대기 2초 / 회복 0.75초 / 총 2.75초
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.OutOfWater);

            Assert.AreEqual(2.75f, SecondsToFull(g, BreathZone.OutOfWater), 0.03f);
        }

        [Test]
        public void DocTable_SurfaceSuppression_TakesThreePointFive()
        {
            // §5.9-1: 수면에서 비명 억제 1회 → -3 / 대기 2초 / 회복 1.5초 / 총 3.5초
            var g = new BreathGauge();
            g.TrySuppressScream(BreathZone.Surface);

            Assert.AreEqual(3.5f, SecondsToFull(g, BreathZone.Surface), 0.03f);
        }

        [Test]
        public void DocTable_SevenSecondDive_TakesThreePointSevenFive()
        {
            // §5.9-1: 7초 잠수 후 물 밖 → -7 / 대기 2초 / 회복 1.75초 / 총 3.75초
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 7f);
            Assert.AreEqual(1f, g.Current, 0.02f);

            Assert.AreEqual(3.75f, SecondsToFull(g, BreathZone.OutOfWater), 0.03f);
        }

        [Test]
        public void DocTable_FullDepletion_TakesFour()
        {
            // §5.9-1: 전체 고갈(8초) 후 물 밖 → -8 / 대기 2초 / 회복 2.0초 / 총 4.0초
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 8f);
            Assert.AreEqual(0f, g.Current, 0.02f);

            Assert.AreEqual(4.0f, SecondsToFull(g, BreathZone.OutOfWater), 0.03f);
        }

        // ── 질식 (§5.9-1 고갈 페널티) ────────────────────────────────────

        [Test]
        public void Choke_FiresExactlyOnceWhenGaugeHitsZero()
        {
            var g = new BreathGauge();
            int chokes = 0;

            for (int i = 0; i < 600; i++) // 12초 잠수 — 8초에 고갈된다
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
        public void Suppress_ExactlyThreeRemaining_Succeeds()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 5f); // 8 - 5 = 3
            Assert.AreEqual(3f, g.Current, 0.02f);

            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(0f, g.Current, 0.03f);
        }

        [Test]
        public void Suppress_BelowThree_FailsAndLeavesGaugeUntouched()
        {
            // §3.5 "게이지 3초 미만 — 억제 불가. 비명 강제 발생". 부분 차감도 음수도 없다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 6f); // 잔여 2
            float before = g.Current;

            Assert.AreEqual(SuppressionResult.NotEnoughBreath, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(before, g.Current, 0.0001f);
            Assert.GreaterOrEqual(g.Current, 0f);
        }

        [Test]
        public void Suppress_AtZero_FailsAndStaysAtZero()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 8f);

            Assert.AreEqual(SuppressionResult.NotEnoughBreath, g.TrySuppressScream(BreathZone.OutOfWater));
            Assert.AreEqual(0f, g.Current, 0.0001f);
        }

        // ── §5.9-1 "연속 억제 최대 2회" ─────────────────────────────────

        [Test]
        public void ConsecutiveSuppressions_MaxTwoOnFullGauge()
        {
            var g = new BreathGauge();

            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater)); // 8 → 5
            Assert.AreEqual(SuppressionResult.Suppressed, g.TrySuppressScream(BreathZone.OutOfWater)); // 5 → 2
            Assert.AreEqual(SuppressionResult.NotEnoughBreath, g.TrySuppressScream(BreathZone.OutOfWater));

            Assert.AreEqual(BreathConfig.MaxConsecutiveSuppressions, 2);
            Assert.AreEqual(2f, g.Current, 0.0001f);
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

        [TestCase(5f, true)]   // §5.9-1 표: 5초 잠수(-5, 잔여 3) 직후 → 억제 가능하나 게이지 0
        [TestCase(6f, false)]  // §5.9-1 표: 6초 잠수(-6, 잔여 2) 직후 → 억제 불가, 비명 강제
        public void DocTable_DiveThenShout(float diveSeconds, bool canSuppress)
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, diveSeconds);

            Assert.AreEqual(canSuppress, g.CanSuppress,
                "§5.9-1 '이 제약이 실제로 작동하는 것은 잠수와 겹칠 때뿐'이라는 표와 어긋난다.");
        }

        // ── 잠수 중 억제 (경계 조건) ─────────────────────────────────────

        [Test]
        public void Suppress_WhileSubmerged_IsAutomaticAndFree()
        {
            // §3.5 "잠수 중 | 이미 게이지 소모 중 | 자동 억제(물속이라 비명 못 지름)".
            // 억제 -3과 잠수 -1/초가 같은 프레임에 이중으로 빠지지 않는다는 것이 핵심이다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 2f); // 잔여 6
            float before = g.Current;

            Assert.AreEqual(SuppressionResult.SuppressedByDive, g.TrySuppressScream(BreathZone.Submerged));
            Assert.AreEqual(before, g.Current, 0.0001f, "잠수 자동 억제에는 추가 비용이 없다(§3.5).");
        }

        [Test]
        public void Suppress_WhileSubmergedBelowThree_StillSucceeds()
        {
            // 잔여 2에서도 잠수 중이면 억제된다 — 비용이 0이라 "3 미만" 규칙이 적용되지 않는다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 6f);
            Assert.IsFalse(g.CanSuppress);

            Assert.AreEqual(SuppressionResult.SuppressedByDive, g.TrySuppressScream(BreathZone.Submerged));
        }

        [Test]
        public void Suppress_ThenSameFrameDiveTick_DoesNotDoubleSpend()
        {
            // 같은 프레임에 억제와 잠수 소모가 겹치는 경우. 잠수 중이면 억제는 무료이므로
            // 그 프레임의 감소분은 잠수 소모(-1 × dt)뿐이어야 한다.
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 1f); // 잔여 7
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

            Assert.AreEqual(5f, g.Current, 0.0001f);
        }

        // ── 라운드 초기화 ────────────────────────────────────────────────

        [Test]
        public void Reset_RestoresFullGaugeAndClearsFlags()
        {
            var g = new BreathGauge();
            Advance(g, BreathZone.Submerged, 8f);

            g.Reset();

            Assert.AreEqual(BreathConfig.TotalSeconds, g.Current, 0.0001f);
            Assert.IsFalse(g.IsRecoveryDelayed);
            Assert.IsFalse(g.IsChokePenaltyActive);
            Assert.IsTrue(g.CanSubmerge);
        }
    }
}
