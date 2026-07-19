using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Marco.Core.Locomotion;
using Marco.Core.Role;
using Marco.Core.Sound;

namespace Marco.Presentation.Player
{
    /// <summary>
    /// §4 1인칭 조작의 Presentation 계층. 입력 수집(Input System)과
    /// CharacterController 구동·카메라 회전만 담당하고, 이동 상태·속도·발소리
    /// 판정은 전부 Core의 LocomotionSimulator에 위임한다(§15.2 계층 분리).
    ///
    /// - §4.1: 1인칭 시점, FOV 기본 90
    /// - §4.2: CharacterController 기반, Rigidbody 물리 비사용, 콜라이더 반경 0.35
    /// - §4.3: WASD 이동 · 마우스 시점 · Left Shift 질주 · Left Ctrl 잠수
    /// - 발소리는 SoundPulse "이벤트"로만 방출한다 — 차폐 판정(Resolver)은
    ///   서버(Net 레이어)의 책임이므로 여기서 직접 호출하지 않는다(§5.6).
    ///
    /// 입력은 Input System의 Keyboard/Mouse 디바이스를 직접 폴링한다 —
    /// 씬 YAML을 수작업으로 배선하는 현 단계에서 InputActionReference 직렬화
    /// 의존을 만들지 않기 위함. §12.6 키 리바인딩(M4)에서 액션 에셋으로 전환한다.
    ///
    /// 네트워크는 붙이지 않는다(로컬 전용). 후속 태스크에서 FishNet
    /// NetworkTransform(§14.2 10~20Hz)을 덧붙일 때 이 클래스는 수정하지 않는 구조.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class FirstPersonController : MonoBehaviour
    {
        [Header("역할 (역할 배정 시스템 배선 전 로컬 테스트용)")]
        [SerializeField] private RoleType _role = RoleType.Runner;

        [Header("카메라")]
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private float _mouseSensitivity = 0.12f;
        [SerializeField] private float _pitchLimit = 89f;

        private CharacterController _characterController;
        private LocomotionSimulator _simulator;
        private float _pitch;
        private float _verticalVelocity;

        private const float Gravity = -9.81f;
        private const float GroundedStick = -2f;

        /// <summary>
        /// §5.1 발소리 펄스 방출 알림. (type, radius, duration, worldPosition).
        /// Net 레이어가 구독해 SoundPulse를 만들고 서버 판정으로 넘긴다.
        /// </summary>
        public event Action<SoundType, float, float, Vector3> FootstepPulseEmitted;

        public MovementState CurrentState => _simulator?.CurrentState ?? MovementState.Idle;
        public RoleType Role => _role;

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _simulator = new LocomotionSimulator(_role);

            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void Update()
        {
            ApplyLook();
            ApplyMovement();
        }

        private void ApplyLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _cameraTransform == null)
                return;

            Vector2 delta = mouse.delta.ReadValue() * _mouseSensitivity;

            transform.Rotate(0f, delta.x, 0f);

            _pitch = Mathf.Clamp(_pitch - delta.y, -_pitchLimit, _pitchLimit);
            _cameraTransform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        private void ApplyMovement()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            Vector2 moveAxis = Vector2.zero;
            if (keyboard.wKey.isPressed) moveAxis.y += 1f;
            if (keyboard.sKey.isPressed) moveAxis.y -= 1f;
            if (keyboard.dKey.isPressed) moveAxis.x += 1f;
            if (keyboard.aKey.isPressed) moveAxis.x -= 1f;

            var input = new LocomotionInput(
                moveAxis,
                sprintHeld: keyboard.leftShiftKey.isPressed,
                diveHeld: keyboard.leftCtrlKey.isPressed,
                isOnWaterSurface: false); // 수면 존 감지는 후속 태스크(§5.9) — 현재 잠수 진입 불가

            LocomotionTick tick = _simulator.Tick(input, Time.deltaTime);

            // 수평 속도는 시뮬레이터 결과를 월드 방향으로 변환, 중력은 컨트롤러가 관리
            Vector3 worldVelocity = transform.TransformDirection(tick.LocalVelocity);

            if (_characterController.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = GroundedStick;
            _verticalVelocity += Gravity * Time.deltaTime;
            worldVelocity.y = _verticalVelocity;

            _characterController.Move(worldVelocity * Time.deltaTime);

            if (tick.Pulse.HasValue)
            {
                FootstepPulse pulse = tick.Pulse.Value;
                FootstepPulseEmitted?.Invoke(pulse.Type, pulse.Radius, pulse.Duration, transform.position);
            }
        }
    }
}
