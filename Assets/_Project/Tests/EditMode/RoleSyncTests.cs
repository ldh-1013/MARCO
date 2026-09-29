using System.Collections.Generic;
using Marco.Core.Locomotion;
using Marco.Core.Role;
using Marco.Core.Sound;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 역할 효과 동기화 — <see cref="PawnRoleSync"/>는 <c>RoleNetworkSync</c> · <c>TagNetworkSync</c>가 실제로 호출하는 규칙이다
    /// (테스트용 복제본이 아니다). 서버 SyncVar 값의 변화는 <see cref="RoleSyncWire.ClientArrival"/>이 정한 순서로 피어에 도착한다.
    /// </summary>
    public class RoleSyncTests
    {
        /// <summary>서버가 SyncVar를 <paramref name="next"/>로 바꿨을 때 피어가 받는 통지를 순서대로 전달한다.</summary>
        private static void Deliver(PawnRoleSync peer, ref RoleSyncState server, RoleSyncState next)
        {
            foreach (RoleSyncUpdate update in RoleSyncWire.ClientArrival(server, next))
                peer.Receive(update);
            server = next;
        }

        /// <summary>서버의 새 판 초기화(ServerResetWorld 순서: 태그 해제 → 배정 해제)를 비호스트 피어가 받는다.</summary>
        private static void ResetWorldOnNonHost(PawnRoleSync peer, ref RoleSyncState server)
        {
            peer.ReceiveTagged(false);
            Deliver(peer, ref server, server.Clear());
        }

        // ── T1: 리매치 태그 ─────────────────────────────────────────────

        [Test]
        public void T1_NonHostPeer_TagThenNewRoundThenTagAgain_AppliesEchoEffectTwice()
        {
            var server = RoleSyncState.Initial;
            var peer = new PawnRoleSync(RoleType.Runner);   // 비호스트 피어 — 서버 전용 초기화 코드를 타지 않고 SyncVar 통지만 받는다

            Deliver(peer, ref server, server.Assign(RoleType.Runner));   // 1판 배정
            peer.ReceiveTagged(true);                                     // 1판 태그
            Assert.AreEqual(1, peer.EchoEffectCount, "1판 태그 — 메아리 효과 1회");

            ResetWorldOnNonHost(peer, ref server);                        // 리매치 가결 → 월드 초기화
            Deliver(peer, ref server, server.Assign(RoleType.Runner));   // 2판 배정
            peer.ReceiveTagged(true);                                     // 2판에서 같은 러너를 다시 태그

            Assert.AreEqual(2, peer.EchoEffectCount,
                $"2판 태그에서도 메아리 효과가 적용돼야 한다 — 적용 {peer.EchoEffectCount}회, 유효 역할 이력 [{string.Join(", ", peer.Timeline)}]");
            Assert.AreEqual(RoleType.Echo, peer.Applied);
        }

        // ── T2: 배정 원자성 ─────────────────────────────────────────────

        [Test]
        public void T2_AssignRunner_InRealArrivalOrder_TimelineIsRunnerOnly()
        {
            var server = RoleSyncState.Initial;
            var peer = new PawnRoleSync(RoleType.Runner);

            Deliver(peer, ref server, server.Assign(RoleType.Runner));

            CollectionAssert.AreEqual(new[] { RoleType.Runner }, peer.Timeline,
                $"중간 역할이 끼면 안 된다 — 유효 역할 이력 [{string.Join(", ", peer.Timeline)}]");
        }

        // ── T3: 로비 = 신규 입장 ─────────────────────────────────────────

        private sealed class NoWalls : IOcclusionProbe
        {
            public OcclusionResult Probe(Vector3 from, Vector3 to) => new OcclusionResult(false, 0);
        }

        [Test]
        public void T3_LobbyAfterRound_EqualsFreshJoin()
        {
            var fresh = new PawnRoleSync(RoleType.Runner);

            var played = new PawnRoleSync(RoleType.Runner);
            var server = RoleSyncState.Initial;
            Deliver(played, ref server, server.Assign(RoleType.Runner));
            played.ReceiveTagged(true);                   // 판 중 태그 → 메아리
            ResetWorldOnNonHost(played, ref server);      // 판 종료 → 로비

            // 역할 효과 · 직전 적용값
            Assert.AreEqual(fresh.Applied, played.Applied,
                $"로비 역할이 신규 입장과 다르다 — 이력 [{string.Join(", ", played.Timeline)}]");

            // CharacterController · 중력 — FirstPersonController.ApplyGhostFlightState가 쓰는 규칙
            Assert.AreEqual(GhostFlight.IsFlying(fresh.Applied), GhostFlight.IsFlying(played.Applied),
                "유령 비행 여부(CharacterController 꺼짐 · 중력 없음)가 신규 입장과 다르다");

            // 몸 표시 — FirstPersonController.RefreshEchoVisibility가 쓰는 규칙(러너 · 술래 시점)
            foreach (RoleType viewer in new[] { RoleType.Runner, RoleType.Seeker })
            {
                Assert.AreEqual(EchoVisibility.ShouldRender(viewer, fresh.Applied, false),
                    EchoVisibility.ShouldRender(viewer, played.Applied, false), $"{viewer} 시점 몸 표시가 신규 입장과 다르다");
            }

            // 청취자 판정 — 서버 PulseNetworkSync는 청취자 역할로 RoleNetworkSync.CurrentRole(= 적용된 역할)을 쓴다.
            // 40m 밖 대화(9m): 메아리는 거리 무관하게 듣고(§3.2-1), 러너는 못 듣는다.
            var far = new SoundPulse(1, Vector3.zero, 9f, 1.2f, SoundType.Talk, 0f);
            var listenerAt = new Vector3(40f, 0f, 0f);
            Assert.AreEqual(
                SoundPulseResolver.Resolve(far, 2, listenerAt, fresh.Applied, new NoWalls()).HasValue,
                SoundPulseResolver.Resolve(far, 2, listenerAt, played.Applied, new NoWalls()).HasValue,
                "청취자 판정이 신규 입장과 다르다(로비에서 listener=Echo)");
            Assert.AreEqual(SoundPulseResolver.RoleRadiusMultiplier(fresh.Applied),
                SoundPulseResolver.RoleRadiusMultiplier(played.Applied));
        }
    }
}
