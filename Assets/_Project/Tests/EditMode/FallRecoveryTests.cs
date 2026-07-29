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
            recovery.Sample(0.016f, Ground, isGrounded: true);

            Assert.IsTrue(recovery.HasSafePoint);
            Assert.AreEqual(Ground, recovery.SafePoint);
        }

        [Test]
        public void AirbornePositions_AreNeverRecorded()
        {
            var recovery = new FallRecovery();
            var falling = new Vector3(12f, -3f, 27f);

            recovery.Sample(1f, falling, isGrounded: false);

            Assert.IsFalse(recovery.HasSafePoint,
                "공중 좌표를 기록하면 다시 떨어지는 자리로 복구된다.");
        }

        [Test]
        public void GroundedSample_RespectsInterval()
        {
            var recovery = new FallRecovery(sampleIntervalSeconds: 0.5f);
            recovery.Sample(0.1f, Ground, isGrounded: true); // 첫 기록

            var moved = new Vector3(20f, 0.05f, 20f);
            recovery.Sample(0.1f, moved, isGrounded: true);  // 주기 미달 → 갱신 안 됨

            Assert.AreEqual(Ground, recovery.SafePoint);

            recovery.Sample(0.5f, moved, isGrounded: true);  // 누적 0.6초 → 갱신
            Assert.AreEqual(moved, recovery.SafePoint);
        }

        [Test]
        public void GroundedBelowKillPlane_IsNotRecorded()
        {
            var recovery = new FallRecovery(killPlaneY: -10f);
            var belowMap = new Vector3(5f, -50f, 5f);

            // 맵 밖 지오메트리 위에 착지해도 그 자리는 안전 지점이 아니다.
            recovery.Sample(1f, belowMap, isGrounded: true);

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
            recovery.Sample(1f, Ground, isGrounded: true);

            Assert.AreEqual(Ground, recovery.GetRecoveryPoint(new Vector3(99f, 99f, 99f)));
        }

        [Test]
        public void Reset_ClearsSafePoint()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true);

            recovery.Reset();

            Assert.IsFalse(recovery.HasSafePoint, "맵이 바뀌면 이전 맵 좌표를 물고 있으면 안 된다.");
        }

        [Test]
        public void ResetThenGround_RecordsImmediatelyAgain()
        {
            var recovery = new FallRecovery();
            recovery.Sample(1f, Ground, isGrounded: true);
            recovery.Reset();

            var newMap = new Vector3(-4f, 1f, 8f);
            recovery.Sample(0.016f, newMap, isGrounded: true);

            Assert.AreEqual(newMap, recovery.SafePoint);
        }

        [Test]
        public void NonPositiveInterval_FallsBackToDefault()
        {
            var recovery = new FallRecovery(sampleIntervalSeconds: 0f);
            recovery.Sample(0.1f, Ground, isGrounded: true);

            var moved = new Vector3(1f, 0f, 1f);
            recovery.Sample(0.1f, moved, isGrounded: true);

            Assert.AreEqual(Ground, recovery.SafePoint, "주기 0은 기본값으로 대체돼야 한다.");
        }

        [Test]
        public void NegativeDeltaTime_DoesNotAdvanceInterval()
        {
            var recovery = new FallRecovery(sampleIntervalSeconds: 0.5f);
            recovery.Sample(0.1f, Ground, isGrounded: true);

            var moved = new Vector3(30f, 0.05f, 30f);
            recovery.Sample(-100f, moved, isGrounded: true);

            Assert.AreEqual(Ground, recovery.SafePoint);
        }
    }
}
