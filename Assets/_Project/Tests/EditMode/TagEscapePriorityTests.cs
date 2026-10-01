using System.Collections.Generic;
using Marco.Core.GameFlow;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.Objectives;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// §6.3 "탈출과 태그가 동일 프레임(같은 도망자) — 탈출 우선. 출구에 닿았다면 탈출로 확정"(09-30). 서버(<c>TagNetworkSync</c> →
    /// <c>RoundNetworkSync.ServerEscapeTakesPriorityOverTag</c>)가 태그 확정 직전에 <see cref="ServerRoundDriver.ResolveTagRequest"/>를 부른다.
    /// 반경은 출구 트리거와 같은 Core 상수(<see cref="EscapeRules.ExitRadiusMeters"/>).
    /// </summary>
    public class TagEscapePriorityTests
    {
        private static readonly Vector3 FrontDoor = new Vector3(7f, 1f, 40f);
        private static readonly Vector3 DrainExit = new Vector3(46f, 1f, 2f);
        private static readonly List<Vector3> Exits = new List<Vector3> { FrontDoor, DrainExit };

        [Test]
        public void GateOpen_TargetInsideExitRadius_TagRequest_ConfirmsEscape_RejectsTag()
        {
            var d = new ServerRoundDriver(600f);
            var feet = new Vector3(7f, 0.1f, 39f); // 정문 1.34m

            ServerRoundDriver.TagResolution r = d.ResolveTagRequest(11, RoleType.Runner, feet, gateOpen: true, Exits);

            Assert.AreEqual(ServerRoundDriver.TagResolution.EscapeInstead, r, "출구에 닿았다면 탈출로 확정 — 태그는 거부(수정 전: 태그 확정)");
            Assert.IsTrue(d.HasEscaped(11));
            Assert.AreEqual(1, d.EscapedCount);
        }

        [Test]
        public void EscapeInstead_CountsTowardVictory()
        {
            var d = new ServerRoundDriver(600f);
            d.ResolveTagRequest(12, RoleType.Runner, new Vector3(46f, 0.05f, 2.25f), gateOpen: true, Exits); // 배수로 1.03m

            ServerRoundDriver.RoundStep step = d.Step(d.Census(totalRunners: 2, taggedRunners: 0));
            Assert.IsFalse(step.Decided, "다른 도망자가 남았다 — 10-01: 끝나지 않는다");

            // 남은 1명이 포획돼 전원 확정 — 태그 대신 확정된 탈출 1명이 요구 1을 채운다.
            Assert.IsTrue(d.Step(d.Census(2, 1)).Decided);
            Assert.AreEqual(RoundResult.RunnersWin, d.Result, "도망자 2 · 탈출 요구 1 — 태그 대신 탈출이 승리로 이어진다");
        }

        [Test]
        public void GateClosed_InsideExitRadius_StillTagged()
        {
            var d = new ServerRoundDriver(600f);
            ServerRoundDriver.TagResolution r = d.ResolveTagRequest(11, RoleType.Runner, new Vector3(7f, 0.1f, 39f), gateOpen: false, Exits);

            Assert.AreEqual(ServerRoundDriver.TagResolution.Tag, r, "게이트가 닫혀 있으면 탈출이 성립하지 않는다 — 태그");
            Assert.IsFalse(d.HasEscaped(11));
        }

        [Test]
        public void GateOpen_OutsideExitRadius_StillTagged()
        {
            var d = new ServerRoundDriver(600f);
            var feet = new Vector3(7f, 1f, 40f - EscapeRules.ExitRadiusMeters - 0.05f); // 반경 + 0.05m

            Assert.AreEqual(ServerRoundDriver.TagResolution.Tag, d.ResolveTagRequest(11, RoleType.Runner, feet, true, Exits));
            Assert.IsFalse(d.HasEscaped(11));
        }

        [Test]
        public void GateOpen_ExactlyOnRadius_EscapeWins()
        {
            var d = new ServerRoundDriver(600f);
            var feet = new Vector3(7f, 1f, 40f - EscapeRules.ExitRadiusMeters); // 경계 — 트리거와 같은 ≤

            Assert.AreEqual(ServerRoundDriver.TagResolution.EscapeInstead, d.ResolveTagRequest(11, RoleType.Runner, feet, true, Exits));
        }

        [Test]
        public void NonRunnerTarget_NeverEscapes()
        {
            var d = new ServerRoundDriver(600f);
            Assert.AreEqual(ServerRoundDriver.TagResolution.Tag,
                d.ResolveTagRequest(11, RoleType.Echo, new Vector3(7f, 0.1f, 39f), true, Exits), "탈출 집계는 도망자만(GAP-11)");
        }

        [Test]
        public void ExitTrigger_AndServer_ShareOneRadiusConstant()
        {
            Assert.AreEqual(2f, EscapeRules.ExitRadiusMeters, "GAP-12 잠정값 2m");

            var go = new GameObject("Exit");
            try
            {
                var trigger = go.AddComponent<EscapePointTrigger>();
                Assert.AreEqual(EscapeRules.ExitRadiusMeters, trigger.EscapeRadius, "트리거는 Core 상수를 쓴다(인스펙터 값 없음)");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
