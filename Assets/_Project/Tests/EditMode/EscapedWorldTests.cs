using System.Collections.Generic;
using System.Reflection;
using Marco.Core.Role;
using Marco.Core.Tagging;
using Marco.Presentation.Player;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 2-2 탈출자는 월드에서 제외된다 — 탈출은 서버 SyncVar 하나(<c>TagNetworkSync</c>)로 공개되고, 각 피어의
    /// <see cref="PawnRoleSync"/>가 태그와 같은 경로(<see cref="RoleEffect.Resolve(RoleAssignment, bool, bool, RoleType)"/> →
    /// 직전 적용값과 다를 때만)로 <c>IEscapeState</c>에 적용한다. 우선순위 탈출 &gt; 태그 &gt; 배정 역할(§6.3 "동일 프레임이면 탈출 우선"),
    /// 미배정(로비)이면 로비 기본값.
    /// </summary>
    public class EscapedWorldTests
    {
        private static void Lifecycle(MonoBehaviour component, string method)
        {
            MethodInfo m = component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(component, null);
        }

        private static RoleSyncUpdate Assigned(RoleType role) => RoleSyncUpdate.Of(RoleAssignment.Of(role));

        // ── 규칙 ──────────────────────────────────────────────────────────

        [Test]
        public void Resolve_Escaped_LeavesWorld_KeepsAssignedRole()
        {
            PawnEffect e = RoleEffect.Resolve(RoleAssignment.Of(RoleType.Runner), tagged: false, escaped: true, RoleType.Runner);
            Assert.AreEqual(new PawnEffect(RoleType.Runner, true), e);
        }

        [Test]
        public void Resolve_EscapeBeatsTag()
        {
            PawnEffect e = RoleEffect.Resolve(RoleAssignment.Of(RoleType.Runner), tagged: true, escaped: true, RoleType.Runner);
            Assert.AreEqual(new PawnEffect(RoleType.Runner, true), e, "§6.3 탈출과 태그가 겹치면 탈출 우선 — 메아리가 되지 않는다");
        }

        [Test]
        public void Resolve_Unassigned_IsLobbyDefault_EvenIfEscapedFlagRemains()
        {
            PawnEffect e = RoleEffect.Resolve(RoleAssignment.Unassigned, tagged: true, escaped: true, RoleType.Runner);
            Assert.AreEqual(PawnEffect.InWorld(RoleType.Runner), e, "로비로 돌아오면 처음 입장과 같다");
        }

        [Test]
        public void WorldPresence_Escaped_IsOutOfEverything()
        {
            Assert.IsFalse(WorldPresence.BodyVisible(true), "몸 숨김");
            Assert.IsFalse(WorldPresence.HasCollision(RoleType.Runner, true), "충돌 없음");
            Assert.IsFalse(WorldPresence.AcceptsMovement(true), "이동 입력 정지");
            Assert.IsFalse(WorldPresence.CanAct(true), "밸브 · 배수구 · 줍기 제외");
            Assert.IsFalse(WorldPresence.CanEmitPulses(RoleType.Runner, true), "파문 제외");
            Assert.IsFalse(WorldPresence.CampingApplies(RoleType.Runner, true), "정지 호흡음 제외");

            Assert.IsTrue(WorldPresence.BodyVisible(false));
            Assert.IsTrue(WorldPresence.HasCollision(RoleType.Runner, false));
            Assert.IsTrue(WorldPresence.AcceptsMovement(false));
            Assert.IsTrue(WorldPresence.CanAct(false));
            Assert.IsTrue(WorldPresence.CanEmitPulses(RoleType.Runner, false));
            Assert.IsTrue(WorldPresence.CampingApplies(RoleType.Runner, false));
            Assert.IsFalse(WorldPresence.CanEmitPulses(RoleType.Echo, false), "메아리 소리는 노크뿐(§3.2) — 기존 규칙 유지");
        }

        [Test]
        public void ServerTag_RejectsEscapedTarget()
        {
            Vector3 at = Vector3.zero;
            Assert.IsTrue(ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner, false, false, at, at));
            Assert.IsFalse(ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner, false, true, at, at),
                "탈출자는 월드에서 빠졌다 — 서버가 태그를 거부한다");
        }

        // ── 한 경로(직전 적용값과 다를 때만) ─────────────────────────────

        [Test]
        public void PawnRoleSync_EscapeAppliedOnce_ThenTagHasNoEchoEffect_NewRoundRestores()
        {
            var peer = new PawnRoleSync(RoleType.Runner);
            var escapes = new List<bool>();
            var roles = new List<RoleType>();
            peer.EscapeApplied += escapes.Add;
            peer.RoleApplied += roles.Add;

            peer.Receive(Assigned(RoleType.Runner));
            peer.ReceiveEscaped(true);
            peer.ReceiveEscaped(true); // 호스트: 서버 · 클라 양쪽 통지 — 두 번째는 같은 값
            peer.ReceiveTagged(true);  // 탈출 뒤 태그 통지가 와도 메아리가 되지 않는다

            CollectionAssert.AreEqual(new[] { true }, escapes);
            Assert.IsTrue(peer.AppliedEffect.Escaped);
            Assert.AreEqual(0, peer.EchoEffectCount, "탈출자는 태그 집계에 들어가지 않는다");
            CollectionAssert.IsEmpty(roles, "역할은 도망자 그대로");

            // 새 판 — 서버가 태그 · 탈출을 내린다.
            peer.ReceiveTagged(false);
            peer.ReceiveEscaped(false);
            CollectionAssert.AreEqual(new[] { true, false }, escapes);
            Assert.AreEqual(PawnEffect.InWorld(RoleType.Runner), peer.AppliedEffect);
        }

        [Test]
        public void PawnRoleSync_LobbyReturn_AssignmentClearedFirst_ReturnsToWorldOnce()
        {
            var peer = new PawnRoleSync(RoleType.Runner);
            var escapes = new List<bool>();
            peer.EscapeApplied += escapes.Add;

            peer.Receive(Assigned(RoleType.Runner));
            peer.ReceiveEscaped(true);
            peer.Receive(RoleSyncUpdate.Of(RoleAssignment.Unassigned)); // 로비 복귀 — 배정 해제가 먼저 온다
            peer.ReceiveEscaped(false);                                 // 뒤늦은 탈출 해제는 같은 값

            CollectionAssert.AreEqual(new[] { true, false }, escapes);
        }

        [Test]
        public void PawnRoleSync_LateJoinSnapshot_EscapedRunner_NoIntermediateState()
        {
            var peer = new PawnRoleSync(RoleType.Runner);
            var escapes = new List<bool>();
            peer.EscapeApplied += escapes.Add;

            peer.ReceiveSnapshot(new RoleSyncState(RoleAssignment.Of(RoleType.Runner)), tagged: false, escaped: true);

            CollectionAssert.AreEqual(new[] { true }, escapes);
            Assert.AreEqual(new PawnEffect(RoleType.Runner, true), peer.AppliedEffect);
        }

        // ── 실제 컴포넌트 ────────────────────────────────────────────────

        [Test]
        public void FirstPersonController_ApplyEscaped_HidesBody_DisablesCollision()
        {
            var go = new GameObject("EscapedRunner");
            var cc = go.AddComponent<CharacterController>();
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.transform.SetParent(go.transform, false);
            Object.DestroyImmediate(body.GetComponent<Collider>());
            var renderer = body.GetComponent<Renderer>();
            var fpc = go.AddComponent<FirstPersonController>();
            Lifecycle(fpc, "Awake");
            Lifecycle(fpc, "OnEnable");

            try
            {
                fpc.ApplyEscaped(true);
                Lifecycle(fpc, "Update");
                Assert.IsTrue(fpc.IsEscaped);
                Assert.IsFalse(cc.enabled, "탈출자는 충돌이 없다(모든 피어)");
                Assert.IsFalse(renderer.enabled, "탈출자의 몸은 그리지 않는다(모든 피어)");

                fpc.ApplyEscaped(false);
                Lifecycle(fpc, "Update");
                Assert.IsTrue(cc.enabled, "새 판 — 충돌 복귀");
                Assert.IsTrue(renderer.enabled, "새 판 — 몸 복귀");
            }
            finally
            {
                Lifecycle(fpc, "OnDisable");
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void EscapedOverlay_SaysSpectating()
        {
            Assert.AreEqual("탈출 성공 — 관전 중", HudFormatter.FormatEscapedOverlay(true));
            Assert.IsEmpty(HudFormatter.FormatEscapedOverlay(false));
        }
    }
}
