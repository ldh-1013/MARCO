using NUnit.Framework;
using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;
using Marco.Presentation.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 9: 발생원 ID 하드코딩 제거의 계약을 고정한다.
    ///
    /// **가장 중요한 것은 폴백값 보존**이다. 네트워크가 없는 로컬 단독 실행에서는
    /// 아무도 `SetPlayerId`를 호출하지 않으므로, 기본값이 스프린트 3~7이 박아둔
    /// 값(1)과 정확히 같아야 기존 스모크 리그가 그대로 동작한다(GAP-15).
    /// </summary>
    public class PlayerIdentityTests
    {
        /// <summary>
        /// `FirstPersonController`의 신원 규칙만 떼어낸 대역.
        /// MonoBehaviour 없이 계약을 검증하기 위한 것이며, 기본값은 실제 상수와 묶는다.
        /// </summary>
        private sealed class FakePlayerIdentity : IPlayerIdentity
        {
            public ulong PlayerId { get; private set; } = FirstPersonController.LocalFallbackPlayerId;
            public void SetPlayerId(ulong playerId) => PlayerId = playerId;
        }

        // 1) **핵심**: 로컬 폴백값은 정확히 1이어야 한다.
        //    (스프린트 3~7이 발소리·밸브·탈출에 박아둔 값과 동일 — 회귀 방지의 근간)
        [Test]
        public void LocalFallbackId_IsExactlyOne()
        {
            Assert.AreEqual(1UL, FirstPersonController.LocalFallbackPlayerId,
                "폴백값이 1이 아니면 기존 로컬 스모크 리그의 발생원 식별이 어긋난다");
        }

        // 2) 아무도 호출하지 않으면 폴백값이 유지된다.
        [Test]
        public void DefaultPlayerId_IsFallback()
        {
            var identity = new FakePlayerIdentity();

            Assert.AreEqual(FirstPersonController.LocalFallbackPlayerId, identity.PlayerId);
        }

        // 3) Net이 실제 OwnerId를 전달하면 반영된다.
        [Test]
        public void SetPlayerId_OverridesFallback()
        {
            var identity = new FakePlayerIdentity();

            identity.SetPlayerId(7);

            Assert.AreEqual(7UL, identity.PlayerId);
        }

        // 4) 인터페이스 참조로만 다뤄도 동작한다 — Net이 구체 타입을 모르는 채
        //    GetComponentsInChildren<IPlayerIdentity>()로 호출하는 실제 경로를 모사.
        [Test]
        public void WorksThroughInterfaceReference()
        {
            IPlayerIdentity identity = new FakePlayerIdentity();

            identity.SetPlayerId(42);

            Assert.AreEqual(42UL, identity.PlayerId);
        }

        // 5) FishNet OwnerId(int)를 ulong으로 변환한 값이 그대로 들어간다.
        //    (PlayerOwnershipGate가 (ulong)OwnerId로 캐스팅하는 경로의 값 보존)
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(255)]
        public void OwnerIdCast_PreservesValue(int ownerId)
        {
            var identity = new FakePlayerIdentity();

            identity.SetPlayerId((ulong)ownerId);

            Assert.AreEqual((ulong)ownerId, identity.PlayerId);
        }

        // ── 파이프라인 통합: 발생원 ID가 실제로 SoundPulse에 반영되는가 ──

        private class FakeProbe : IOcclusionProbe
        {
            public OcclusionResult Probe(Vector3 from, Vector3 to) => new OcclusionResult(false, 0);
        }

        // 6) 발소리 파이프라인 기본값도 폴백과 같다(하드코딩 제거 후에도 로컬 동작 보존).
        [Test]
        public void PulsePipeline_DefaultSourceId_MatchesFallback()
        {
            var pipeline = new LocalPulsePipeline(new FakeProbe(), FirstPersonController.LocalFallbackPlayerId);

            Assert.AreEqual(FirstPersonController.LocalFallbackPlayerId, pipeline.SourcePlayerId);
        }

        // 7) 파이프라인의 발생원 ID를 바꾸면 이후 발행되는 펄스가 GAP-1 판정에서
        //    그 ID를 기준으로 본인 여부를 가른다.
        [Test]
        public void PulsePipeline_SourceIdChange_AffectsSelfListenerFilter()
        {
            var pipeline = new LocalPulsePipeline(new FakeProbe(), sourcePlayerId: 1);
            var received = new System.Collections.Generic.List<PulseDelivery>();
            pipeline.DeliveryEmitted += received.Add;

            // 발생원을 7로 바꾸고, 청취자도 7로 두면 GAP-1(본인 제외)로 아무것도 안 나온다.
            pipeline.SourcePlayerId = 7;
            pipeline.SetDebugListener(listenerId: 7, position: new Vector3(1, 0, 0), role: RoleType.Runner);
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, timestamp: 0f);
            pipeline.Tick(0f);

            Assert.AreEqual(0, received.Count, "발생원과 청취자가 같으면(둘 다 7) GAP-1로 제외돼야 한다");
        }

        // 8) 반대로 발생원(7)과 청취자(8)가 다르면 정상 전달된다 —
        //    ID가 실제로 발생원 식별에 쓰이고 있음을 확인.
        [Test]
        public void PulsePipeline_DistinctSourceAndListener_Delivers()
        {
            var pipeline = new LocalPulsePipeline(new FakeProbe(), sourcePlayerId: 7);
            var received = new System.Collections.Generic.List<PulseDelivery>();
            pipeline.DeliveryEmitted += received.Add;

            pipeline.SetDebugListener(listenerId: 8, position: new Vector3(1, 0, 0), role: RoleType.Runner);
            pipeline.OnFootstepPulse(SoundType.Walk, 2f, 0.4f, Vector3.zero, timestamp: 0f);
            pipeline.Tick(0f);

            Assert.AreEqual(1, received.Count);
            Assert.AreEqual(PulseDeliveryKind.Appeared, received[0].Kind);
        }
    }
}
