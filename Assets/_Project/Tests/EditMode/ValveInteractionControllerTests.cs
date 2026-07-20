using NUnit.Framework;
using UnityEngine;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.Objectives;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 5: E 홀드 입력 → §6.1 밸브 상태기계 배선 규칙을 고정한다.
    /// Core `Valve` 자체의 상태 전이는 ValveTests(17케이스)가 이미 담당하므로
    /// 여기서는 **입력→Core 호출 매핑과 취소 조건**만 검증한다.
    ///
    /// GAP-9 결정: §6.1의 "이탈"은 (a) E를 뗌, (b) 상호작용 범위 이탈 두 가지다.
    ///             범위 안에서의 단순 이동은 취소하지 않는다.
    /// GAP-10 결정: 상호작용 거리 2.5m(기획서 미명시).
    /// </summary>
    public class ValveInteractionControllerTests
    {
        private const ulong PlayerId = 1;
        private static readonly Vector3 ValvePos = new Vector3(10f, 0f, 10f);

        private static ValveInteractionInput Input(Valve valve, Vector3 playerPos, bool held,
            RoleType role = RoleType.Runner)
        {
            return new ValveInteractionInput(PlayerId, role, playerPos, held, valve, ValvePos);
        }

        /// <summary>밸브 바로 앞(1m).</summary>
        private static Vector3 NearValve => ValvePos + new Vector3(1f, 0f, 0f);

        /// <summary>범위(2.5m) 밖(5m).</summary>
        private static Vector3 FarFromValve => ValvePos + new Vector3(5f, 0f, 0f);

        // 1) 범위 안에서 E 홀드 → 회전 시작, Core 상태가 Rotating으로 전이.
        [Test]
        public void HoldInRange_StartsRotation()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            var result = controller.Tick(Input(valve, NearValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.Started, result);
            Assert.AreEqual(ValveState.Rotating, valve.State);
            Assert.AreSame(valve, controller.ActiveValve);
        }

        // 2) GAP-5: 메아리는 거부된다. **Core가 거부하는 것을 그대로 전달**하는지 확인
        //    (Presentation이 역할을 중복 검사하지 않는다).
        [Test]
        public void EchoRole_IsRejectedByCore()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            var result = controller.Tick(Input(valve, NearValve, held: true, role: RoleType.Echo), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.Rejected, result);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.IsNull(controller.ActiveValve);
        }

        // 3) 술래는 밸브를 돌릴 수 있다 — 기획서가 금지한 것은 메아리뿐이다.
        [Test]
        public void SeekerRole_IsAllowed()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            var result = controller.Tick(Input(valve, NearValve, held: true, role: RoleType.Seeker), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.Started, result);
        }

        // 4) §6.2 3초를 채우면 완료되고 Open이 된다.
        [Test]
        public void HoldingForFullDuration_Completes()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, NearValve, held: true), 0.02f); // Started
            var result = controller.Tick(Input(valve, NearValve, held: true), Valve.DefaultRotationSeconds);

            Assert.AreEqual(ValveInteractionEvent.Completed, result);
            Assert.AreEqual(ValveState.Open, valve.State);
            Assert.IsNull(controller.ActiveValve, "완료 후에는 활성 상호작용이 없어야 한다");
        }

        // 5) GAP-9(a): E를 떼면 취소되고 §6.1대로 진행도가 0으로 리셋된다.
        [Test]
        public void ReleasingKey_CancelsAndResetsProgress()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, NearValve, held: true), 0.02f);
            controller.Tick(Input(valve, NearValve, held: true), 2.0f); // 2초 진행
            Assert.Greater(valve.Progress01, 0.5f);

            var result = controller.Tick(Input(valve, NearValve, held: false), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.CancelledByRelease, result);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.001f, "부분 진행 저장 없음(§6.1)");
        }

        // 6) GAP-9(b): 범위를 벗어나면 홀드 중이어도 취소된다.
        [Test]
        public void LeavingRange_CancelsEvenWhileHolding()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, NearValve, held: true), 0.02f);
            controller.Tick(Input(valve, NearValve, held: true), 1.5f);

            var result = controller.Tick(Input(valve, FarFromValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.CancelledByRangeExit, result);
            Assert.AreEqual(ValveState.Closed, valve.State);
            Assert.AreEqual(0f, valve.Progress01, 0.001f);
        }

        // 7) GAP-9 결정의 반대편: 범위 안에서의 이동은 취소하지 않는다.
        //    (기획서에 "움직이면 취소"라는 문구가 없으므로 규칙을 만들어내지 않는다.)
        [Test]
        public void MovingWithinRange_DoesNotCancel()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, ValvePos + new Vector3(0.5f, 0, 0), held: true), 0.02f);
            var result = controller.Tick(Input(valve, ValvePos + new Vector3(2.0f, 0, 0), held: true), 0.5f);

            Assert.AreEqual(ValveInteractionEvent.Progressing, result);
            Assert.AreEqual(ValveState.Rotating, valve.State);
        }

        // 8) 범위 밖에서는 E를 눌러도 시작되지 않는다.
        [Test]
        public void HoldOutOfRange_DoesNotStart()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            var result = controller.Tick(Input(valve, FarFromValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.None, result);
            Assert.AreEqual(ValveState.Closed, valve.State);
        }

        // 9) 근처에 밸브가 없으면 아무 일도 없다.
        [Test]
        public void NoCandidate_DoesNothing()
        {
            var controller = new ValveInteractionController();

            var result = controller.Tick(Input(null, NearValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.None, result);
        }

        // 10) 이미 열린 밸브는 조용히 무시한다(Rejected가 아니라 None —
        //     Rejected는 역할 제약처럼 알려줄 가치가 있는 거부 전용).
        [Test]
        public void AlreadyOpenValve_IsSilentlyIgnored()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();
            valve.TryBeginRotation(PlayerId, RoleType.Runner);
            valve.Tick(Valve.DefaultRotationSeconds);
            Assert.AreEqual(ValveState.Open, valve.State);

            var result = controller.Tick(Input(valve, NearValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.None, result);
        }

        // 11) 완료 후 계속 홀드해도 다시 시작되지 않는다.
        [Test]
        public void HoldingAfterCompletion_DoesNotRestart()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, NearValve, held: true), 0.02f);
            controller.Tick(Input(valve, NearValve, held: true), Valve.DefaultRotationSeconds); // Completed

            var result = controller.Tick(Input(valve, NearValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.None, result);
            Assert.AreEqual(ValveState.Open, valve.State);
        }

        // 12) 취소 후 다시 홀드하면 처음부터 새로 시작된다(§6.1 부분 진행 저장 없음).
        [Test]
        public void RestartAfterCancel_BeginsFromZero()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, NearValve, held: true), 0.02f);
            controller.Tick(Input(valve, NearValve, held: true), 2.0f);
            controller.Tick(Input(valve, NearValve, held: false), 0.02f); // 취소

            var result = controller.Tick(Input(valve, NearValve, held: true), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.Started, result);
            Assert.Less(valve.Progress01, 0.1f, "이어받기가 아니라 0부터 다시 시작");
        }

        // 13) 진행률이 홀드 시간에 비례해 올라간다(디버그 표시의 입력값).
        [Test]
        public void Progress01_TracksHoldDuration()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            controller.Tick(Input(valve, NearValve, held: true), 0.02f);
            Assert.AreEqual(0f, controller.Progress01, 0.05f);

            controller.Tick(Input(valve, NearValve, held: true), 1.5f); // 3초 중 절반
            Assert.AreEqual(0.5f, controller.Progress01, 0.05f);
        }

        // 14) 홀드하지 않으면 아무 일도 없다.
        [Test]
        public void NotHolding_DoesNothing()
        {
            var controller = new ValveInteractionController();
            var valve = new Valve();

            var result = controller.Tick(Input(valve, NearValve, held: false), 0.02f);

            Assert.AreEqual(ValveInteractionEvent.None, result);
            Assert.AreEqual(ValveState.Closed, valve.State);
        }

        // 15) §6.3 연결: 밸브 3개를 이 컨트롤러로 전부 열면 러너 승리 조건이 성립한다
        //     (탈출 1인은 탈출 시스템이 배선되면 채워질 입력).
        [Test]
        public void OpeningAllValves_FeedsWinCondition()
        {
            var controller = new ValveInteractionController();
            var valves = new[] { new Valve(), new Valve(), new Valve() };

            foreach (Valve valve in valves)
            {
                var input = new ValveInteractionInput(PlayerId, RoleType.Runner, NearValve, true, valve, ValvePos);
                controller.Tick(input, 0.02f);
                controller.Tick(input, Valve.DefaultRotationSeconds);
            }

            int opened = 0;
            foreach (Valve valve in valves)
            {
                if (valve.State == ValveState.Open)
                    opened++;
            }

            RoundResult result = WinConditionEvaluator.Evaluate(
                valvesOpened: opened, totalValves: valves.Length,
                runnersEscaped: 1, allRunnersTagged: false, timeRemainingSeconds: 600f);

            Assert.AreEqual(3, opened);
            Assert.AreEqual(RoundResult.RunnersWin, result);
        }
    }
}
