using System.Reflection;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.Objectives;
using Marco.Presentation.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 2-3 상호작용 안내 한 줄 — "[E] 밸브 A 돌리기", 누르는 동안 진행 %, 못 하면 이유(메아리 불가 · 이번 판 비활성 · 이미 열림).
    /// 배수구도 같은 형식. 규칙은 <see cref="InteractionPrompt"/>, 거부 사유는 서버와 같은 <see cref="Valve.CheckInteract(RoleType, bool, ValveState)"/>.
    /// </summary>
    public class InteractionPromptTests
    {
        private static void Lifecycle(MonoBehaviour component, string method)
        {
            MethodInfo m = component.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            m?.Invoke(component, null);
        }

        // ── 밸브 ─────────────────────────────────────────────────────────

        [Test]
        public void Valve_InRange_Idle_ShowsKeyAndAction()
        {
            Assert.AreEqual("[E] 밸브 A 돌리기", InteractionPrompt.Valve(ValveId.A, true, ValveInteractionRejection.None, false, 0f));
        }

        [Test]
        public void Valve_OutOfRange_ShowsNothing()
        {
            Assert.IsEmpty(InteractionPrompt.Valve(ValveId.A, false, ValveInteractionRejection.None, false, 0f));
            Assert.IsEmpty(InteractionPrompt.Valve(ValveId.A, false, ValveInteractionRejection.AlreadyOpen, false, 0f),
                "범위 밖이면 이유도 말하지 않는다");
        }

        [Test]
        public void Valve_Holding_ShowsProgressPercent_RoundedDown()
        {
            Assert.AreEqual("밸브 B 돌리는 중 45%", InteractionPrompt.Valve(ValveId.B, true, ValveInteractionRejection.None, true, 0.459f));
            Assert.AreEqual("밸브 B 돌리는 중 99%", InteractionPrompt.Valve(ValveId.B, true, ValveInteractionRejection.None, true, 0.996f),
                "99.6%를 100%로 올려 보이면 '다 됐다'로 읽힌다");
        }

        [TestCase(ValveInteractionRejection.EchoCannotInteract, "밸브 C — 메아리 불가")]
        [TestCase(ValveInteractionRejection.NotActiveThisRound, "밸브 C — 이번 판 비활성")]
        [TestCase(ValveInteractionRejection.AlreadyOpen, "밸브 C — 이미 열림")]
        public void Valve_Rejected_ShowsReason(ValveInteractionRejection rejection, string expected)
        {
            Assert.AreEqual(expected, InteractionPrompt.Valve(ValveId.C, true, rejection, false, 0f));
        }

        [Test]
        public void Valve_Seeker_IsRejected_AndGetsNoPrompt()
        {
            Assert.AreEqual(ValveInteractionRejection.SeekerCannotInteract, Valve.CheckInteract(RoleType.Seeker, true, ValveState.Closed),
                "술래는 밸브를 돌릴 수 없다(09-30 결정)");
            Assert.AreEqual(ValveInteractionRejection.SeekerCannotInteract, Valve.CheckInteract(RoleType.Seeker, false, ValveState.Open),
                "역할 사유가 비활성 · 이미 열림보다 먼저다(메아리와 같은 순서)");
            Assert.IsEmpty(InteractionPrompt.Valve(ValveId.A, true, ValveInteractionRejection.SeekerCannotInteract, false, 0f),
                "술래에게는 안내를 띄우지 않는다");
        }

        [Test]
        public void Drain_Seeker_GetsNoPrompt_AndServerRejects()
        {
            Assert.IsEmpty(InteractionPrompt.Drain("메인 풀", true, InteractionPrompt.CheckDrain(RoleType.Seeker, true), false, 0f),
                "술래에게는 배수구 안내도 띄우지 않는다");

            var hatch = new DrainHatch(DrainId.MainPool, workSeconds: 8f);
            Assert.AreEqual(ValveInteractionRejection.SeekerCannotInteract, hatch.TryWork(1, RoleType.Seeker));
            Assert.AreEqual(ValveInteractionRejection.EchoCannotInteract, hatch.TryWork(2, RoleType.Echo));
        }

        [Test]
        public void Valve_ReasonOrder_MatchesServerRule()
        {
            Assert.AreEqual(ValveInteractionRejection.EchoCannotInteract, Valve.CheckInteract(RoleType.Echo, false, ValveState.Open));
            Assert.AreEqual(ValveInteractionRejection.NotActiveThisRound, Valve.CheckInteract(RoleType.Runner, false, ValveState.Open));
            Assert.AreEqual(ValveInteractionRejection.AlreadyOpen, Valve.CheckInteract(RoleType.Runner, true, ValveState.Open));
            Assert.AreEqual(ValveInteractionRejection.None, Valve.CheckInteract(RoleType.Runner, true, ValveState.Reflowing));
        }

        // ── 배수구 ───────────────────────────────────────────────────────

        [Test]
        public void Drain_SameFormat()
        {
            Assert.AreEqual("[E] 배수구 메인 풀 열기",
                InteractionPrompt.Drain("메인 풀", true, InteractionPrompt.DrainBlock.None, false, 0f));
            Assert.AreEqual("배수구 메인 풀 여는 중 30%",
                InteractionPrompt.Drain("메인 풀", true, InteractionPrompt.DrainBlock.None, true, 0.305f));
            Assert.AreEqual("배수구 유아풀 — 메아리 불가",
                InteractionPrompt.Drain("유아풀", true, InteractionPrompt.CheckDrain(RoleType.Echo, true), false, 0f));
            Assert.AreEqual("배수구 유아풀 — 이번 판 비활성",
                InteractionPrompt.Drain("유아풀", true, InteractionPrompt.CheckDrain(RoleType.Runner, false), false, 0f));
            Assert.IsEmpty(InteractionPrompt.Drain("유아풀", false, InteractionPrompt.DrainBlock.None, false, 0f));
        }

        // ── 실제 컴포넌트 ────────────────────────────────────────────────

        [Test]
        public void ValveInteractor_InRange_PublishesPrompt_EscapedShowsNothing()
        {
            var pawnGo = new GameObject("Pawn");
            var player = pawnGo.AddComponent<FirstPersonController>();
            var interactor = pawnGo.AddComponent<ValveInteractor>();
            Lifecycle(interactor, "Awake");

            var valveGo = new GameObject("Valve_A");
            valveGo.transform.position = new Vector3(1f, 0f, 0f);
            var valve = valveGo.AddComponent<ValveBehaviour>();
            var so = new SerializedObject(valve);
            so.FindProperty("_valveId").enumValueIndex = (int)ValveId.A;
            so.ApplyModifiedPropertiesWithoutUndo();
            Lifecycle(valve, "Awake");
            Lifecycle(valve, "OnEnable");

            try
            {
                Lifecycle(interactor, "Update");
                Assert.AreEqual("[E] 밸브 A 돌리기", interactor.Prompt, "밸브 1m 앞 — 실제 인터랙터가 안내를 낸다");

                player.ApplyEscaped(true);
                Lifecycle(interactor, "Update");
                Assert.IsEmpty(interactor.Prompt, "탈출자는 월드에서 빠졌다 — 안내도 없다");

                player.ApplyEscaped(false);
                player.ApplyRole(RoleType.Seeker);
                Lifecycle(interactor, "Update");
                Assert.IsEmpty(interactor.Prompt, "술래 화면에는 '[E] 밸브 돌리기'가 뜨지 않는다(수정 전: 떴다)");
            }
            finally
            {
                Lifecycle(valve, "OnDisable");
                Object.DestroyImmediate(valveGo);
                Object.DestroyImmediate(pawnGo);
            }
        }
    }
}
