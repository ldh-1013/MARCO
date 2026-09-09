using System.Collections.Generic;
using Marco.Core.Breath;
using Marco.Core.Role;
using Marco.Core.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §3.5 술래 외침(5단계).
    ///
    /// 핵심은 **§3.5가 못박은 "세 가지 22m"를 코드가 섞지 않는가**다 —
    /// A(외침 소리 22m, 차폐 적용) · B(공포 반경 22m, 차폐 미적용) · C(비명 청취 10.8m,
    /// §5.7 역할 배율의 결과). 이 클래스가 다루는 것은 B 하나뿐이며,
    /// C는 새 상수 없이 9m × 1.2로 유도된다는 것을 함께 고정한다.
    /// </summary>
    public class ServerShoutDriverTests
    {
        private const ulong Seeker = 1;
        private const ulong RunnerA = 2;
        private const ulong RunnerB = 3;

        private static readonly Vector3 Origin = new Vector3(0f, 0f, 0f);

        private static ShoutTarget Runner(ulong id, float distance, BreathGauge gauge,
            BreathZone zone = BreathZone.OutOfWater)
        {
            return new ShoutTarget(id, new Vector3(distance, 0f, 0f), RoleType.Runner, zone, gauge);
        }

        /// <summary>선딜레이를 넘겨 실제로 발동시킨다. 발동 시각을 돌려준다.</summary>
        private static float Fire(ServerShoutDriver d, float requestedAt = 0f)
        {
            Assert.AreEqual(ShoutRequestResult.Accepted,
                d.TryRequest(Seeker, RoleType.Seeker, Origin, requestedAt));

            float firedAt = requestedAt + SeekerShoutConfig.WindupSeconds;
            Assert.AreEqual(1, d.Tick(firedAt).Count);
            return firedAt;
        }

        // ── §3.5 상수 ────────────────────────────────────────────────────

        [Test]
        public void Config_MatchesDesignDoc()
        {
            Assert.AreEqual(22f, SeekerShoutConfig.ShoutPulseRadiusMeters); // A
            Assert.AreEqual(22f, SeekerShoutConfig.FearRadiusMeters);       // B
            Assert.AreEqual(1f, SeekerShoutConfig.WindupSeconds);
            Assert.AreEqual(45f, SeekerShoutConfig.CooldownSeconds);
        }

        [Test]
        public void ShoutPulse_IsShoutGrade_NotItsOwnNumber()
        {
            // §3.5 "술래가 발생시키는 소리 = 고함 등급 SoundPulse(§5.1)".
            // §5.1 고함이 조정되면 외침도 함께 따라가야 하므로 같은 상수를 참조한다.
            Assert.AreEqual(Voice.VoiceConfig.ShoutRadiusMeters, SeekerShoutConfig.ShoutPulseRadiusMeters);
            Assert.AreEqual(Voice.VoiceConfig.ShoutDurationSeconds, SeekerShoutConfig.ShoutPulseDurationSeconds);
        }

        [Test]
        public void FearRadiusAndShoutRadius_AreSeparateConstants()
        {
            // §3.5 "A와 B가 우연히 같은 22m일 뿐 서로 다른 판정이다."
            // 값이 같다고 한쪽을 다른 쪽의 별칭으로 만들면 안 된다 — 따로 조정할 수 있어야 한다.
            Assert.AreEqual(SeekerShoutConfig.ShoutPulseRadiusMeters, SeekerShoutConfig.FearRadiusMeters,
                "지금은 두 값이 같다(§3.5 표).");

            // B는 고함 등급을 참조하지 않는 독립 상수다 — 이 관계가 깨지면 A만 바꿔도 B가 끌려간다.
            Assert.AreEqual(22f, SeekerShoutConfig.FearRadiusMeters);
        }

        // ── §5.1 비명 규격 + C(청취 10.8m) ──────────────────────────────

        [Test]
        public void Scream_MatchesDesignDoc()
        {
            Assert.AreEqual(9f, ScreamConfig.RadiusMeters);
            Assert.AreEqual(1.0f, ScreamConfig.DurationSeconds);
        }

        [Test]
        public void Scream_IsNotTheSameTypeAsShout()
        {
            // §5.1 "Shout(고함)과 Scream(비명)은 완전히 별개의 SoundType이다."
            Assert.AreNotEqual(SoundType.Shout, SoundType.Scream);
            Assert.AreNotEqual(Voice.VoiceConfig.ShoutRadiusMeters, ScreamConfig.RadiusMeters);
            Assert.AreNotEqual(Voice.VoiceConfig.TalkDurationSeconds, ScreamConfig.DurationSeconds);
        }

        [Test]
        public void ScreamListeningRadius_IsDerivedFromExistingMultiplier()
        {
            // §3.5 C: "비명 청취 반경 10.8m = 9m × 1.2". **새 상수를 만들지 않는다** —
            // ×1.2는 §5.7 역할 배율이며 SoundPulseResolver가 이미 전 파문에 적용하고 있다.
            float listening = ScreamConfig.RadiusMeters * SoundPulseResolver.RoleRadiusMultiplier(RoleType.Seeker);

            Assert.AreEqual(10.8f, listening, 0.0001f);
            Assert.AreEqual(DirectionGaugeRules.RangeMultiplier,
                SoundPulseResolver.RoleRadiusMultiplier(RoleType.Seeker), 0.0001f,
                "§5.0 불변 조건 — 방향 표시 반경과 청취 반경이 같은 ×1.2를 쓴다.");
        }

        [Test]
        public void Scream_IsRegisteredInServerPulseSpec()
        {
            Assert.IsTrue(ServerPulseDriver.TryGetPulseSpec(SoundType.Scream, out float radius, out float duration));
            Assert.AreEqual(ScreamConfig.RadiusMeters, radius, 0.0001f);
            Assert.AreEqual(ScreamConfig.DurationSeconds, duration, 0.0001f);
        }

        // ── 역할·쿨다운·선딜레이 ────────────────────────────────────────

        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Echo)]
        public void Request_ByNonSeeker_IsRejected(RoleType role)
        {
            var d = new ServerShoutDriver();

            Assert.AreEqual(ShoutRequestResult.NotSeeker, d.TryRequest(Seeker, role, Origin, 0f));
            Assert.AreEqual(0, d.PendingCount);
        }

        [Test]
        public void Request_BySeeker_StartsWindup()
        {
            var d = new ServerShoutDriver();

            Assert.AreEqual(ShoutRequestResult.Accepted, d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f));
            Assert.IsTrue(d.IsWindingUp(Seeker));
            Assert.AreEqual(0, d.Tick(0.99f).Count, "선딜레이 1초 전에 발동하면 안 된다.");
        }

        [Test]
        public void Tick_AfterWindup_Activates()
        {
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);

            List<ShoutActivation> fired = d.Tick(SeekerShoutConfig.WindupSeconds);

            Assert.AreEqual(1, fired.Count);
            Assert.AreEqual(Seeker, fired[0].SeekerId);
            Assert.AreEqual(Origin, fired[0].Origin);
            Assert.IsFalse(d.IsWindingUp(Seeker));
        }

        [Test]
        public void Request_WhileWindingUp_IsRejected()
        {
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);

            Assert.AreEqual(ShoutRequestResult.AlreadyWindingUp,
                d.TryRequest(Seeker, RoleType.Seeker, Origin, 0.5f));
            Assert.AreEqual(1, d.PendingCount);
        }

        [Test]
        public void Cooldown_StartsAtWindupCompletion_NotRequest()
        {
            // §3.5 "쿨다운 45초 (선딜레이 완료 시점부터 계산)".
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            Assert.AreEqual(SeekerShoutConfig.CooldownSeconds, d.CooldownRemaining(Seeker, firedAt), 0.0001f);
            Assert.AreEqual(ShoutRequestResult.OnCooldown,
                d.TryRequest(Seeker, RoleType.Seeker, Origin, firedAt + SeekerShoutConfig.CooldownSeconds - 0.01f));
            Assert.AreEqual(ShoutRequestResult.Accepted,
                d.TryRequest(Seeker, RoleType.Seeker, Origin, firedAt + SeekerShoutConfig.CooldownSeconds));
        }

        [Test]
        public void CancelOnMove_RemovesWindup_AndCostsNoCooldown()
        {
            // §3.5 "선딜레이 중 이동하면 취소, **쿨다운 소모 없음**".
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);

            Assert.IsTrue(d.CancelOnMove(Seeker));
            Assert.AreEqual(0, d.Tick(5f).Count, "취소된 외침은 발동하지 않는다.");
            Assert.AreEqual(0f, d.CooldownRemaining(Seeker, 5f), 0.0001f);
            Assert.AreEqual(ShoutRequestResult.Accepted, d.TryRequest(Seeker, RoleType.Seeker, Origin, 5f));
        }

        [Test]
        public void CancelOnMove_WithNothingPending_IsNoOp()
        {
            var d = new ServerShoutDriver();
            Assert.IsFalse(d.CancelOnMove(Seeker));
        }

        // ── §3.5 B — 공포 반경 ──────────────────────────────────────────

        [Test]
        public void FearRadius_BoundaryIsInclusive()
        {
            Assert.IsTrue(ServerShoutDriver.IsInFearRadius(Origin, new Vector3(22f, 0f, 0f)));
            Assert.IsFalse(ServerShoutDriver.IsInFearRadius(Origin, new Vector3(22.01f, 0f, 0f)));
        }

        [Test]
        public void ResolveFear_OutsideRadius_IsNotAffected()
        {
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            var targets = new List<ShoutTarget> { Runner(RunnerA, 30f, new BreathGauge()) };

            Assert.AreEqual(0, d.ResolveFear(Origin, targets, firedAt).Count,
                "§3.5 '22m 초과 — 비명 없음'.");
        }

        [Test]
        public void ResolveFear_InsideRadius_NoInput_Screams()
        {
            // §3.5 "아무것도 안 함 → 비명 발생(9m / 1.0초)".
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            var gauge = new BreathGauge();
            var targets = new List<ShoutTarget> { Runner(RunnerA, 10f, gauge) };

            List<ScreamReaction> reactions = d.ResolveFear(Origin, targets, firedAt);

            Assert.AreEqual(1, reactions.Count);
            Assert.AreEqual(ScreamOutcome.Screamed, reactions[0].Outcome);
            Assert.IsTrue(reactions[0].EmitsScream);
            Assert.AreEqual(BreathConfig.TotalSeconds, gauge.Current, 0.0001f,
                "억제를 시도하지 않았으면 게이지가 줄면 안 된다.");
        }

        [Test]
        public void ResolveFear_SeekerAndEcho_AreNotTargets()
        {
            // §3.5 "22m 안의 **살아있는 도망자**".
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            var targets = new List<ShoutTarget>
            {
                new ShoutTarget(Seeker, new Vector3(1f, 0f, 0f), RoleType.Seeker, BreathZone.OutOfWater, new BreathGauge()),
                new ShoutTarget(RunnerB, new Vector3(2f, 0f, 0f), RoleType.Echo, BreathZone.OutOfWater, new BreathGauge()),
            };

            Assert.AreEqual(0, d.ResolveFear(Origin, targets, firedAt).Count);
        }

        [Test]
        public void ResolveFear_IgnoresOcclusion_ByConstruction()
        {
            // §3.5 "B는 능력 판정이라 벽 뒤 도망자도 놀란다" —
            // 판정에 차폐 프로브가 아예 들어오지 않는다는 것이 그 보장이다.
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            var targets = new List<ShoutTarget> { Runner(RunnerA, 21.9f, new BreathGauge()) };

            Assert.AreEqual(1, d.ResolveFear(Origin, targets, firedAt).Count);
        }

        // ── §3.5 억제 ────────────────────────────────────────────────────

        [Test]
        public void Suppress_WithinWindup_SucceedsAndCostsThree()
        {
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);
            d.NotifySuppressAttempt(RunnerA, 0.5f); // 선딜레이 1초 안

            float firedAt = SeekerShoutConfig.WindupSeconds;
            Assert.AreEqual(1, d.Tick(firedAt).Count);

            var gauge = new BreathGauge();
            List<ScreamReaction> reactions = d.ResolveFear(Origin, new List<ShoutTarget> { Runner(RunnerA, 5f, gauge) }, firedAt);

            Assert.AreEqual(ScreamOutcome.SuppressedByHeldBreath, reactions[0].Outcome);
            Assert.IsFalse(reactions[0].EmitsScream, "억제 성공이면 Scream 자체가 발생하지 않는다.");
            Assert.AreEqual(BreathConfig.TotalSeconds - BreathConfig.SuppressionCost, gauge.Current, 0.0001f);
        }

        [Test]
        public void Suppress_BeforeWindupWindow_DoesNotCount()
        {
            // 외침이 시작되기 한참 전에 눌러 둔 입력으로 억제되면 "1초 안에 입력"이 무의미해진다.
            var d = new ServerShoutDriver();
            d.NotifySuppressAttempt(RunnerA, 0f);
            float firedAt = Fire(d, requestedAt: 10f); // 발동 = 11초

            var gauge = new BreathGauge();
            List<ScreamReaction> reactions = d.ResolveFear(Origin, new List<ShoutTarget> { Runner(RunnerA, 5f, gauge) }, firedAt);

            Assert.AreEqual(ScreamOutcome.Screamed, reactions[0].Outcome);
            Assert.AreEqual(BreathConfig.TotalSeconds, gauge.Current, 0.0001f);
        }

        [Test]
        public void Suppress_BelowThree_FailsAndScreams()
        {
            // §3.5 "게이지 3초 미만 — 억제 불가. 비명 강제 발생" + 게이지가 음수로 안 간다.
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);
            d.NotifySuppressAttempt(RunnerA, 0.5f);
            float firedAt = SeekerShoutConfig.WindupSeconds;
            d.Tick(firedAt);

            var gauge = new BreathGauge();
            for (int i = 0; i < 300; i++)
                gauge.Tick(BreathZone.Submerged, 0.02f); // 6초 잠수 → 잔여 2

            float before = gauge.Current;
            List<ScreamReaction> reactions = d.ResolveFear(Origin, new List<ShoutTarget> { Runner(RunnerA, 5f, gauge) }, firedAt);

            Assert.AreEqual(ScreamOutcome.SuppressionFailed, reactions[0].Outcome);
            Assert.IsTrue(reactions[0].EmitsScream);
            Assert.AreEqual(before, gauge.Current, 0.0001f, "실패한 억제는 게이지를 건드리지 않는다.");
            Assert.GreaterOrEqual(gauge.Current, 0f);
        }

        [Test]
        public void Suppress_WhileSubmerged_IsAutomaticWithoutInput()
        {
            // §3.5 "잠수 중 — 자동 억제(물속이라 비명 못 지름)". 입력이 없어도 억제된다.
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            var gauge = new BreathGauge();
            var targets = new List<ShoutTarget> { Runner(RunnerA, 5f, gauge, BreathZone.Submerged) };

            List<ScreamReaction> reactions = d.ResolveFear(Origin, targets, firedAt);

            Assert.AreEqual(ScreamOutcome.SuppressedByDive, reactions[0].Outcome);
            Assert.IsFalse(reactions[0].EmitsScream);
            Assert.AreEqual(BreathConfig.TotalSeconds, gauge.Current, 0.0001f,
                "잠수 자동 억제에는 -3이 붙지 않는다 — 잠수 소모가 이미 대가다(§3.5).");
        }

        [Test]
        public void Suppress_WhileSubmergedWithInput_StillCostsNothing()
        {
            // 잠수 중에 Ctrl까지 눌러도 이중 차감이 없어야 한다.
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);
            d.NotifySuppressAttempt(RunnerA, 0.5f);
            float firedAt = SeekerShoutConfig.WindupSeconds;
            d.Tick(firedAt);

            var gauge = new BreathGauge();
            for (int i = 0; i < 100; i++)
                gauge.Tick(BreathZone.Submerged, 0.02f); // 2초 잠수 → 6
            float before = gauge.Current;

            List<ScreamReaction> reactions = d.ResolveFear(
                Origin, new List<ShoutTarget> { Runner(RunnerA, 5f, gauge, BreathZone.Submerged) }, firedAt);

            Assert.AreEqual(ScreamOutcome.SuppressedByDive, reactions[0].Outcome);
            Assert.AreEqual(before, gauge.Current, 0.0001f);
        }

        [Test]
        public void Suppress_OnlyChargesTargetsInsideFearRadius()
        {
            // 22m 밖에서 Ctrl을 눌러도 게이지가 빠지면 안 된다 — 그러면 "괜히 눌러 손해"가 된다.
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);
            d.NotifySuppressAttempt(RunnerA, 0.5f);
            float firedAt = SeekerShoutConfig.WindupSeconds;
            d.Tick(firedAt);

            var gauge = new BreathGauge();
            d.ResolveFear(Origin, new List<ShoutTarget> { Runner(RunnerA, 30f, gauge) }, firedAt);

            Assert.AreEqual(BreathConfig.TotalSeconds, gauge.Current, 0.0001f);
        }

        [Test]
        public void ResolveFear_MixedGroup_EachRunnerJudgedSeparately()
        {
            var d = new ServerShoutDriver();
            d.TryRequest(Seeker, RoleType.Seeker, Origin, 0f);
            d.NotifySuppressAttempt(RunnerA, 0.5f); // A만 숨을 참는다
            float firedAt = SeekerShoutConfig.WindupSeconds;
            d.Tick(firedAt);

            var gaugeA = new BreathGauge();
            var gaugeB = new BreathGauge();
            var targets = new List<ShoutTarget>
            {
                Runner(RunnerA, 5f, gaugeA),
                Runner(RunnerB, 20f, gaugeB),
            };

            List<ScreamReaction> reactions = d.ResolveFear(Origin, targets, firedAt);

            Assert.AreEqual(2, reactions.Count);
            Assert.AreEqual(ScreamOutcome.SuppressedByHeldBreath, reactions[0].Outcome);
            Assert.AreEqual(ScreamOutcome.Screamed, reactions[1].Outcome);
            Assert.AreEqual(5f, gaugeA.Current, 0.0001f);
            Assert.AreEqual(8f, gaugeB.Current, 0.0001f);
        }

        // ── §3.5 정보 비대칭 (거리별 반응표) ────────────────────────────

        [TestCase(5f, true, true)]      // 0~10.8m: 비명 + 술래 인지
        [TestCase(10.8f, true, true)]   // 경계
        [TestCase(15f, true, false)]    // 10.8~22m: 비명은 나지만 술래는 못 듣는다
        [TestCase(22f, true, false)]    // 공포 반경 경계
        [TestCase(25f, false, false)]   // 22m 초과: 비명 없음
        public void DocTable_DistanceReaction(float distance, bool screams, bool seekerHears)
        {
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);

            List<ScreamReaction> reactions = d.ResolveFear(
                Origin, new List<ShoutTarget> { Runner(RunnerA, distance, new BreathGauge()) }, firedAt);

            Assert.AreEqual(screams, reactions.Count == 1 && reactions[0].EmitsScream,
                $"{distance}m에서의 비명 발생 여부가 §3.5 표와 다르다.");

            // C — 술래 청취 반경 10.8m. 새 상수 없이 9 × 1.2로 계산한다.
            float listening = ScreamConfig.RadiusMeters * SoundPulseResolver.RoleRadiusMultiplier(RoleType.Seeker);
            Assert.AreEqual(seekerHears, screams && distance <= listening + 0.0001f,
                $"{distance}m에서의 술래 인지 여부가 §3.5 표와 다르다.");
        }

        // ── 라운드 초기화 ────────────────────────────────────────────────

        [Test]
        public void Reset_ClearsCooldownWindupAndAttempts()
        {
            var d = new ServerShoutDriver();
            float firedAt = Fire(d);
            d.NotifySuppressAttempt(RunnerA, firedAt);

            d.Reset();

            Assert.AreEqual(0, d.PendingCount);
            Assert.AreEqual(0f, d.CooldownRemaining(Seeker, firedAt), 0.0001f);
            Assert.AreEqual(ShoutRequestResult.Accepted, d.TryRequest(Seeker, RoleType.Seeker, Origin, firedAt));
        }
    }
}
