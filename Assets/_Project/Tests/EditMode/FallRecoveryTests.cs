using Marco.Core.GameFlow;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 20 낙하 복구 판정. 순수 로직이라 Unity 런타임 없이 그대로 돈다
    /// (Vector3의 순수 생성자만 사용 — 네이티브 호출 금지 제약은 스프린트 18b에서 확인).
    /// </summary>
    public class FallRecoveryTests
    {
        private static readonly Vector3 Ground = new Vector3(12f, 0.05f, 27f);

        [Test]
        public void FirstGroundedSample_IsRecordedImmediately()
        {
            var recovery = new FallRecovery();

            // 주기(0.5초)를 기다리지 않고도 첫 접지는 바로 기록돼야 한다 —
            // 스폰 직후 떨어지면 되돌릴 지점이 없기 때문이다.
            recovery.Sample(0.016f, Ground, isGrounded: true, hasFloorBelow: true);

            Assert.IsTrue(recovery.HasSafePoint);
            Assert.AreEqual(Ground, recovery.SafePoint);
        }

        [Test]
        public void AirbornePositions_AreNeverRecorded()
        {
            var recovery = new FallRecovery();
            var falling = new Vector3(12f, -3f, 27f);

            recovery.Sample(1f, falling, isGrounded: false, hasFloorBelow: true);

            Assert.IsFalse(recovery.HasSafePoint,
                "공중 좌표를 기록하면 다시 떨어지는 자리로 복구된다.");
        }

        [Test]
        public void GroundedSample_RespectsInterval()
        {
            var recovery = new FallRecovery(sampleIntervalSeconds: 0.5f);
            recovery.Sample(0.1f, Ground, isGrounded: true, hasFloorBelow: true); // 첫 기록

            var moved = new Vector3(20f, 0.05f, 20f);
            recovery.Sample(0.1f, moved, isGrounded: true, hasFloorBelow: true);  // 주기 미달 → 갱신 안 됨

            Assert.AreEqual(Ground, recovery.SafePoint);

            recovery.Sample(0.5f, moved, isGrounded: true, hasFloorBelow: true);  // 누적 0.6초 → 갱신
            Assert.AreEqual(moved, recovery.SafePoint);
        }

        [Test]
        public void GroundedBelowKillPlane_IsNotRecorded()
        {
            var recovery = new FallRecovery(killPlaneY: -10f);
            var belowMap = new Vector3(5f, -50f, 5f);

            // 맵 밖 지오메트리 위에 착지해도 그 자리는 안전 지점이 아니다.
            recovery.Sample(1f, belowMap, isGrounded: true, hasFloorBelow: true);

            Assert.IsFalse(recovery.HasSafePoint);
        }

        [Test]
        public void HasFallen_UsesKillPlaneThreshold()
        {
            var recovery = new FallRecovery(killPlaneY: -10f);

            Assert.IsFalse(recovery.HasFallen(new Vector3(0f, -9.99f, 0f)), "경계 위는 낙하가 아니다.");
            Assert.IsFalse(recovery.HasFallen(new Vector3(0f, -10f, 0f)), "경계값 자체는 낙하가 아니다.");
            Assert.IsTrue(recovery.HasFallen(new Vector3(0f, -10.01f, 0f)));
        }

        [Test]
        public void KillPlane_IsFarBelowPoolFloor()
        {
            // §10.1 실내수영장 바닥은 y=0 부근이고 물은 그 바로 위 얕은 층이다.
            // 잠수(§4.2 Diving)가 낙하로 오인되지 않을 만큼 아래여야 한다.
            Assert.Less(FallRecovery.DefaultKillPlaneY, -5f);
        }

        [Test]
        public void RecoveryPoint_FallsBackWhenNoSafePointRecorded()
        {
            var recovery = new FallRecovery();
            var spawn = new Vector3(12f, 0.05f, 27f);

            Assert.AreEqual(spawn, recovery.GetRecoveryPoint(spawn));
        }

        [Test]
        public void RecoveryPoint_PrefersRecordedSafePoint()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);

            Assert.AreEqual(Ground, recovery.GetRecoveryPoint(new Vector3(99f, 99f, 99f)));
        }

        [Test]
        public void Reset_ClearsSafePoint()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);

            recovery.Reset();

            Assert.IsFalse(recovery.HasSafePoint, "맵이 바뀌면 이전 맵 좌표를 물고 있으면 안 된다.");
        }

        [Test]
        public void ResetThenGround_RecordsImmediatelyAgain()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);
            recovery.Reset();

            var newMap = new Vector3(-4f, 1f, 8f);
            recovery.Sample(0.016f, newMap, isGrounded: true, hasFloorBelow: true);

            Assert.AreEqual(newMap, recovery.SafePoint);
        }

        [Test]
        public void NonPositiveInterval_FallsBackToDefault()
        {
            var recovery = new FallRecovery(sampleIntervalSeconds: 0f);
            recovery.Sample(0.1f, Ground, isGrounded: true, hasFloorBelow: true);

            var moved = new Vector3(1f, 0f, 1f);
            recovery.Sample(0.1f, moved, isGrounded: true, hasFloorBelow: true);

            Assert.AreEqual(Ground, recovery.SafePoint, "주기 0은 기본값으로 대체돼야 한다.");
        }

        [Test]
        public void NegativeDeltaTime_DoesNotAdvanceInterval()
        {
            var recovery = new FallRecovery(sampleIntervalSeconds: 0.5f);
            recovery.Sample(0.1f, Ground, isGrounded: true, hasFloorBelow: true);

            var moved = new Vector3(30f, 0.05f, 30f);
            recovery.Sample(-100f, moved, isGrounded: true, hasFloorBelow: true);

            Assert.AreEqual(Ground, recovery.SafePoint);
        }

        // ── 09-29: 안전 지점 기록 조건 · 반복 낙하 루프 차단 ──────────────────

        private static readonly Vector3 Spawn = new Vector3(9f, 0.10f, 38f);

        [Test]
        public void Constants_MatchDecision()
        {
            Assert.AreEqual(-10f, FallRecovery.DefaultKillPlaneY, "임계값은 기존 −10 유지(09-29 결정)");
            Assert.AreEqual(10f, FallRecovery.DefaultRepeatWindowSeconds, "반복 낙하 창 10초(09-29 결정)");
        }

        [Test]
        public void GroundedWithoutFloorBelow_IsNotRecorded()
        {
            // 벽 모서리 · 틈 가장자리에 걸친 순간(접지로 보고되지만 발밑 레이가 바닥을 못 맞힘)은 안전 지점이 아니다.
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: false);
            Assert.IsFalse(recovery.HasSafePoint);

            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);
            Assert.IsTrue(recovery.HasSafePoint);
        }

        [Test]
        public void FirstFall_GoesToSafePoint()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);

            Vector3 target = recovery.Recover(100f, Spawn, out FallRecoveryKind kind);
            Assert.AreEqual(FallRecoveryKind.SafePoint, kind);
            Assert.AreEqual(Ground, target);
        }

        [Test]
        public void SecondFallWithinWindow_GoesToAssignedSpawn_AndStaysThereWhileRepeating()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);

            recovery.Recover(100f, Spawn, out _);                                   // 1회차 → 안전 지점
            Vector3 second = recovery.Recover(109.9f, Spawn, out FallRecoveryKind k2); // 9.9초 뒤 또 낙하
            Assert.AreEqual(FallRecoveryKind.AssignedSpawnRepeatedFall, k2, "10초 안 2회차 → 배정 스폰(루프 차단)");
            Assert.AreEqual(Spawn, second);

            recovery.Recover(115f, Spawn, out FallRecoveryKind k3);                 // 직전 복구(109.9) 기준 5.1초
            Assert.AreEqual(FallRecoveryKind.AssignedSpawnRepeatedFall, k3, "창은 직전 복구 기준 — 계속 떨어지면 계속 스폰");
        }

        [Test]
        public void FallAfterWindow_GoesBackToSafePoint()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);

            recovery.Recover(100f, Spawn, out _);
            Vector3 later = recovery.Recover(110f, Spawn, out FallRecoveryKind kind);   // 정확히 10초 = 창 밖
            Assert.AreEqual(FallRecoveryKind.SafePoint, kind, "10초가 지나면 다시 안전 지점(악용 방지 — 매번 스폰으로 보내지 않는다)");
            Assert.AreEqual(Ground, later);
        }

        [Test]
        public void NoSafePoint_GoesToAssignedSpawn()
        {
            var recovery = new FallRecovery();
            Vector3 target = recovery.Recover(5f, Spawn, out FallRecoveryKind kind);
            Assert.AreEqual(FallRecoveryKind.AssignedSpawnNoSafePoint, kind);
            Assert.AreEqual(Spawn, target);
        }

        [Test]
        public void Reset_OnMapChange_ForgetsSafePointAndRepeatHistory()
        {
            // 09-28 기계실 루프: 맵이 내려간 뒤에도 맵 위 안전 지점으로 되돌려 매초 다시 떨어졌다. 맵 전환 = Reset.
            var recovery = new FallRecovery();
            recovery.Sample(1f, new Vector3(48.47f, 0.13f, 11.52f), isGrounded: true, hasFloorBelow: true);
            recovery.Recover(100f, Spawn, out _);

            recovery.Reset();

            Vector3 target = recovery.Recover(101f, Spawn, out FallRecoveryKind kind);
            Assert.AreEqual(FallRecoveryKind.AssignedSpawnNoSafePoint, kind, "사라진 맵의 안전 지점을 쓰지 않는다");
            Assert.AreEqual(Spawn, target);

            // 반복 기록도 지워졌다 — 새 맵에서 안전 지점이 생기고 떨어지면 1회차로 본다.
            var fresh = new FallRecovery();
            fresh.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);
            fresh.Recover(100f, Spawn, out _);
            fresh.Reset();
            fresh.Sample(1f, Ground, isGrounded: true, hasFloorBelow: true);
            fresh.Recover(101f, Spawn, out FallRecoveryKind afterReset);
            Assert.AreEqual(FallRecoveryKind.SafePoint, afterReset);
        }
    }
}
