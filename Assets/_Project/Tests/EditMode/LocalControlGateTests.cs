using NUnit.Framework;
using UnityEngine;
using Marco.Core.Locomotion;
using Marco.Core.Net;
using Marco.Core.Role;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 8: 네트워크 소유권 게이팅 계약을 고정한다.
    ///
    /// `FirstPersonController`는 MonoBehaviour라 EditMode에서 Update 루프를 돌릴 수
    /// 없으므로, **인터페이스 계약과 상태 전이 규칙**만 검증한다. 실제 입력 차단은
    /// 에디터 Play(멀티 클라이언트) 검증 항목이다.
    ///
    /// 가장 중요한 계약: **기본값이 "로컬 조종"이어야 한다.** 네트워크가 없는
    /// 로컬 단독 실행(스프린트 3~7 스모크 리그)에서는 아무도 SetLocalControl을
    /// 호출하지 않으므로, 기본값이 false면 기존 워크플로우가 통째로 깨진다.
    /// </summary>
    public class LocalControlGateTests
    {
        /// <summary>
        /// `FirstPersonController`의 게이팅 규칙만 떼어낸 대역.
        /// Unity 수명주기 없이 계약을 검증하기 위한 것이다.
        /// </summary>
        private sealed class FakeControlGate : ILocalControlGate
        {
            public bool IsLocallyControlled { get; private set; } = true; // 기본값 = 로컬
            public int ChangeCount { get; private set; }

            public void SetLocalControl(bool isLocallyControlled)
            {
                if (IsLocallyControlled == isLocallyControlled)
                    return; // 같은 값 재통보는 무시(불필요한 카메라 토글 방지)

                IsLocallyControlled = isLocallyControlled;
                ChangeCount++;
            }
        }

        // 1) **핵심**: 아무도 호출하지 않으면 로컬 조종 상태여야 한다.
        //    (네트워크 없는 로컬 단독 실행 = 스프린트 3~7 워크플로우 보존)
        [Test]
        public void DefaultState_IsLocallyControlled()
        {
            var gate = new FakeControlGate();

            Assert.IsTrue(gate.IsLocallyControlled,
                "기본값이 false면 네트워크 없이 실행할 때 조작이 통째로 막힌다");
        }

        // 2) 원격으로 통보되면 조종권을 놓는다.
        [Test]
        public void SetRemote_ReleasesControl()
        {
            var gate = new FakeControlGate();

            gate.SetLocalControl(false);

            Assert.IsFalse(gate.IsLocallyControlled);
            Assert.AreEqual(1, gate.ChangeCount);
        }

        // 3) 소유권 이전으로 다시 로컬이 되면 조종권을 되찾는다.
        [Test]
        public void RegainOwnership_RestoresControl()
        {
            var gate = new FakeControlGate();
            gate.SetLocalControl(false);

            gate.SetLocalControl(true);

            Assert.IsTrue(gate.IsLocallyControlled);
            Assert.AreEqual(2, gate.ChangeCount);
        }

        // 4) 같은 값이 반복 통보돼도 상태 변경이 일어나지 않는다.
        //    (OnStartClient·OnOwnershipClient가 둘 다 불릴 수 있어 중복 통보가 정상이다)
        [Test]
        public void RedundantNotification_DoesNotToggle()
        {
            var gate = new FakeControlGate();

            gate.SetLocalControl(true);   // 이미 true
            gate.SetLocalControl(true);

            Assert.IsTrue(gate.IsLocallyControlled);
            Assert.AreEqual(0, gate.ChangeCount, "같은 값 재통보로 카메라를 껐다 켜면 안 된다");
        }

        // 5) 인터페이스로만 다뤄도 동작한다 — Net 레이어가 구체 타입을 모르는 채
        //    GetComponent<ILocalControlGate>()로 호출하는 실제 경로를 모사한다.
        [Test]
        public void WorksThroughInterfaceReference()
        {
            ILocalControlGate gate = new FakeControlGate();

            gate.SetLocalControl(false);

            Assert.IsFalse(((FakeControlGate)gate).IsLocallyControlled);
        }

        // 6) 원격 캐릭터라도 이동 시뮬레이션 자체는 순수 로직이라 영향받지 않는다 —
        //    게이팅은 "입력을 넣지 않는 것"이지 시뮬레이터를 부수는 것이 아니다.
        //    (원격 캐릭터의 위치는 네트워크 Transform이 담당한다)
        [Test]
        public void GatingDoesNotAffectSimulatorItself()
        {
            var simulator = new LocomotionSimulator(RoleType.Runner);

            var tick = simulator.Tick(
                new LocomotionInput(new Vector2(0, 1), false, false, false), 0.02f);

            Assert.AreEqual(MovementState.Walk, tick.State);
            Assert.AreEqual(5.0f, tick.LocalVelocity.magnitude, 0.001f);
        }
    }
}
