using System.Collections.Generic;
using Marco.Core.GameFlow;
using Marco.Core.Objectives;
using Marco.Core.Role;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 승리 경로 끝에서 끝까지(1-4, Core 수준) — 서버가 쓰는 실제 규칙만 엮는다: 밸브 구동기(<see cref="ServerValveDriver"/>)로
    /// 요구 수만큼 열고 → 서버 게이트 래치(<see cref="RoundObjective"/>) → 출구 트리거 규칙(<see cref="EscapeAttemptScheduler"/>)이
    /// 요청 → 서버 탈출 등록(<see cref="ServerRoundDriver"/>) → 판정.
    /// </summary>
    public class VictoryFlowTests
    {
        // Game 씬의 두 출구(중심 · 판정 반경 2m) — ExitValveSceneTests가 씬에서 확인한다.
        private static readonly Vector3 FrontDoor = new Vector3(7f, 1f, 40f);
        private static readonly Vector3 DrainExit = new Vector3(46f, 1f, 2f);
        private const float EscapeRadius = EscapeRules.ExitRadiusMeters;
        private const float Dt = 0.25f;

        private sealed class Round
        {
            public readonly RoundObjective Objective = new RoundObjective();
            public readonly ServerRoundDriver Driver = new ServerRoundDriver(600f);
            public readonly List<ServerValveDriver> Valves = new List<ServerValveDriver>();
            public readonly List<ValveId> Active;
            public float Now;

            public Round(int players)
            {
                Active = ValveRoster.SelectActive(players, seed: 7);
                Objective.BeginRound(players, Active.Count);
                foreach (ValveId id in Active)
                {
                    var valve = new Valve(ValveOccupancy.RotateSeconds(id));
                    valve.Configure(id);
                    var driver = new ServerValveDriver(valve);
                    driver.SetActive(true);
                    Valves.Add(driver);
                }
            }

            public int OpenCount()
            {
                int n = 0;
                foreach (ServerValveDriver v in Valves)
                    if (v.State == ValveState.Open)
                        n++;
                return n;
            }

            /// <summary>서버 틱 — 밸브 · 게이트 래치를 <paramref name="seconds"/> 동안 굴린다.</summary>
            public void Run(float seconds)
            {
                for (float t = 0f; t < seconds; t += Dt)
                {
                    foreach (ServerValveDriver v in Valves)
                        v.Tick(Dt);
                    Objective.Tick(OpenCount());
                    Now += Dt;
                }
            }

            /// <summary>러너들이 활성 밸브 [<paramref name="from"/>, <paramref name="to"/>)를 하나씩 맡아 끝까지 돌린다(동시 개방).</summary>
            public void OpenValves(int from, int to)
            {
                for (int i = from; i < to; i++)
                    Assert.AreEqual(ValveInteractionRejection.None, Valves[i].BeginHold((ulong)(10 + i), RoleType.Runner));
                Run(12f); // 가장 긴 회전(8초) 이상
                for (int i = from; i < to; i++)
                    Valves[i].EndHold((ulong)(10 + i));
            }

            /// <summary>러너가 출구 근처에 <b>서 있기만</b> 한다 — 출구 트리거 규칙이 요청하면 서버가 등록한다.</summary>
            public bool StandAtExit(ulong runnerId, Vector3 feet, Vector3 exit, float seconds)
            {
                var scheduler = new EscapeAttemptScheduler();
                bool inside = Vector3.Distance(feet, exit) <= EscapeRadius;
                for (float t = 0f; t < seconds; t += Dt)
                {
                    bool escaped = Driver.HasEscaped(runnerId);
                    if (scheduler.Tick(inside, Objective.GateOpen, escaped, !Driver.IsDecided, Now) == EscapeAttemptDecision.Request)
                        Driver.TryRegisterEscape(runnerId, RoleType.Runner, Objective.GateOpen);
                    Now += Dt;
                }

                return Driver.HasEscaped(runnerId);
            }
        }

        [Test]
        public void ThreePlayers_OpenTwo_GateLatch_FrontDoor_RunnersWin()
        {
            var round = new Round(players: 3);
            Assert.AreEqual(2, round.Objective.RequiredOpen);

            // 러너는 스폰 슬롯 2 (7, 0.1, 39) — 정문 1.34m, 스폰부터 반경 안. 게이트가 닫힌 동안은 탈출하지 못한다.
            var slot2 = new Vector3(7f, 0.1f, 39f);
            Assert.IsFalse(round.StandAtExit(11, slot2, FrontDoor, 2f), "게이트 닫힘 — 탈출 불가");

            round.OpenValves(0, 2);
            Assert.IsTrue(round.Objective.GateOpen, "요구 2개 동시 개방 → 서버 게이트 래치");

            Assert.IsTrue(round.StandAtExit(11, slot2, FrontDoor, 1f), "움직이지 않고 정문으로 탈출");
            Assert.IsTrue(round.Driver.Evaluate(round.Driver.Census(totalRunners: 2, taggedRunners: 0)));
            Assert.AreEqual(RoundResult.RunnersWin, round.Driver.Result, "도망자 2명 — 탈출 요구 1명");
        }

        [Test]
        public void ThreePlayers_SameFlow_ThroughDrainExit()
        {
            var round = new Round(players: 3);
            round.OpenValves(0, 2);
            Assert.IsTrue(round.Objective.GateOpen);

            var nearDrain = new Vector3(46f, 0.05f, 2.25f); // 직원통로 — 배수로 출구 1.03m
            Assert.IsTrue(round.StandAtExit(12, nearDrain, DrainExit, 1f), "배수로 출구로 탈출");
            Assert.IsTrue(round.Driver.Evaluate(round.Driver.Census(2, 0)));
            Assert.AreEqual(RoundResult.RunnersWin, round.Driver.Result);
        }

        [Test]
        public void GateOpened_ThenReflowClosesValves_EscapeStillValid()
        {
            var round = new Round(players: 3);
            round.OpenValves(0, 2);
            Assert.IsTrue(round.Objective.GateOpen);

            round.Run(Valve.OpenHoldSeconds + Valve.ReflowSeconds + 5f); // 역류로 전부 닫힌다
            Assert.AreEqual(0, round.OpenCount(), "역류 — 밸브가 닫혔다");
            Assert.IsTrue(round.Objective.GateOpen, "§6.1-2 래치 — 게이트는 열린 채");

            Assert.IsTrue(round.StandAtExit(11, new Vector3(7f, 0.1f, 39f), FrontDoor, 1f), "역류 뒤에도 탈출 유효");
            Assert.IsTrue(round.Driver.Evaluate(round.Driver.Census(2, 0)));
            Assert.AreEqual(RoundResult.RunnersWin, round.Driver.Result);
        }

        [Test]
        public void FourPlayers_RequiresThree_AndTwoEscapes()
        {
            var round = new Round(players: 4);
            Assert.AreEqual(3, round.Objective.RequiredOpen, "4인 — 요구 3");
            Assert.AreEqual(2, ValveRoster.EscapeRequirement(3), "도망자 3명 — 탈출 요구 2");

            round.OpenValves(0, 2);
            Assert.IsFalse(round.Objective.GateOpen, "2개로는 열리지 않는다");
            round.OpenValves(2, 3);
            Assert.IsTrue(round.Objective.GateOpen);

            Assert.IsTrue(round.StandAtExit(11, new Vector3(7f, 0.1f, 39f), FrontDoor, 1f));
            Assert.IsFalse(round.Driver.Evaluate(round.Driver.Census(3, 0)), "1명 탈출 — 아직");
            Assert.IsTrue(round.StandAtExit(12, new Vector3(46f, 0.05f, 2.25f), DrainExit, 1f));
            Assert.IsTrue(round.Driver.Evaluate(round.Driver.Census(3, 0)));
            Assert.AreEqual(RoundResult.RunnersWin, round.Driver.Result);
        }
    }
}
