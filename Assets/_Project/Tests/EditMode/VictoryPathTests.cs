using System.Collections.Generic;
using System.Reflection;
using Marco.Core.Objectives;
using Marco.Presentation.Objectives;
using Marco.Presentation.Player;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 승리 경로 결함(09-29 실기 · 코드 확인) — 1-1 E가 밸브에 닿지 않음, 1-2 요구 개방 수 불일치, 1-3 출구가 진입 순간에만 시도.
    /// 모두 실제 컴포넌트 · 실제 Core 규칙을 호출한다.
    /// </summary>
    public class VictoryPathTests
    {
        /// <summary>EditMode에서는 생명주기가 자동으로 돌지 않는다 — 컴포넌트 자신의 메서드를 실제 순서대로 부른다(없으면 건너뜀).</summary>
        private static void Lifecycle(MonoBehaviour component, string method)
        {
            MethodInfo m = component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(component, null);
        }

        // ── 1-1: 인터랙터가 먼저, 밸브가 나중(로비에서 캐릭터 생성 → 맵 추가 로드) ──

        [Test]
        public void ValveInteractor_ValveLoadedAfterInteractor_IsFoundAsNearest()
        {
            var pawnGo = new GameObject("Pawn");
            pawnGo.AddComponent<FirstPersonController>();
            var interactor = pawnGo.AddComponent<ValveInteractor>();
            Lifecycle(interactor, "Awake");                                   // 로비: 캐릭터가 먼저 생긴다

            var valveGo = new GameObject("Valve_A");
            valveGo.transform.position = new Vector3(1f, 0f, 0f);
            var valve = valveGo.AddComponent<ValveBehaviour>();
            var so = new SerializedObject(valve);
            so.FindProperty("_valveId").enumValueIndex = (int)ValveId.A;
            so.ApplyModifiedPropertiesWithoutUndo();
            Lifecycle(valve, "Awake");                                        // 맵이 나중에 추가 로드된다
            Lifecycle(valve, "OnEnable");

            try
            {
                Assert.AreSame(valve, interactor.FindNearestValve(Vector3.zero),
                    "맵이 나중에 로드돼도 인터랙터가 그 밸브를 최근접으로 잡아야 한다");
            }
            finally
            {
                Lifecycle(valve, "OnDisable");
                Object.DestroyImmediate(valveGo);
                Object.DestroyImmediate(pawnGo);
            }
        }

        // ── 1-2: 서버 판정 · HUD · 브리핑이 같은 수 ─────────────────────────

        [TestCase(3, 3, 2)]
        [TestCase(4, 4, 3)]
        public void ObjectiveNumbers_ServerHudBriefingAgree_AndGateOpensAtRequired(int players, int expectedActive, int expectedRequired)
        {
            // 서버: 활성 밸브를 고를 때와 같은 총원으로 라운드 시작 시 확정(RoundNetworkSync.ServerSelectActiveValves).
            List<ValveId> active = ValveRoster.SelectActive(players, seed: 1);
            var server = new RoundObjective();
            server.BeginRound(players, active.Count);
            Assert.AreEqual(expectedActive, server.ActiveValves, "서버 활성 수");
            Assert.AreEqual(expectedRequired, server.RequiredOpen, "서버 요구 수");

            // 클라이언트: 목표 추적기(ValveObjectiveTracker)가 쓰는 규칙 — HUD · 브리핑 · 출구 사전 필터가 그 값을 읽는다.
            ObjectivePublication published = server.Publication;
            int clientRequired = ObjectiveView.RequiredOpen(published, totalPlayersOverride: 0, out _);
            int clientActive = ObjectiveView.ActiveValves(published, localActiveCount: expectedActive);
            Assert.AreEqual(server.RequiredOpen, clientRequired, $"HUD·게이트 요구 수가 서버와 다르다({players}인)");

            StringAssert.Contains($"0/{expectedRequired}", HudFormatter.FormatValveCount(0, clientRequired), "HUD 밸브 칸");
            StringAssert.Contains($"활성 밸브 {expectedActive}개 · 요구 {expectedRequired}개",
                HudFormatter.FormatBriefingTitle(clientActive, clientRequired, 30f), "브리핑 제목");

            // 게이트: 서버에서만 판정 — 요구 수만큼 동시에 열리면 열린다. 클라이언트는 공개값을 읽는다.
            Assert.IsFalse(server.Tick(expectedRequired - 1), "요구 수 미만 — 닫힘");
            Assert.IsTrue(server.Tick(expectedRequired), "요구 수 도달 — 열림");
            Assert.IsTrue(ObjectiveView.GateOpen(server.Publication, localLatchOpen: false),
                "클라이언트는 서버가 공개한 게이트를 읽어야 한다(피어마다 따로 계산하지 않는다)");
        }

        // ── 1-3: 게이트가 열리기 전부터 범위 안에 서 있던 러너 ──────────────────

        [Test]
        public void Escape_StandingInsideBeforeGateOpens_EscapesWhenGateOpens_WithoutMoving()
        {
            var scheduler = new EscapeAttemptScheduler();
            float t = 0f;

            // 스폰부터 정문 반경 안(러너 슬롯 2 → 정문 1.34m). 게이트는 닫혀 있다.
            for (; t < 2f; t += 0.1f)
                Assert.AreNotEqual(EscapeAttemptDecision.Request,
                    scheduler.Tick(inside: true, gateOpen: false, alreadyEscaped: false, roundInProgress: true, now: t));

            // 게이트가 열린다 — 움직이지 않는다.
            int requests = 0;
            for (; t < 3f; t += 0.1f)
            {
                if (scheduler.Tick(inside: true, gateOpen: true, alreadyEscaped: false, roundInProgress: true, now: t)
                    == EscapeAttemptDecision.Request)
                    requests++;
            }

            Assert.GreaterOrEqual(requests, 1, "게이트가 열리면 제자리에서도 탈출 요청이 나가야 한다");
        }
    }
}
