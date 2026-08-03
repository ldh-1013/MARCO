using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Marco.Core.Locomotion;
using Marco.Core.Net;
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
    public sealed class FirstPersonController : MonoBehaviour, ILocalControlGate, IPlayerIdentity, IRoleState
    {
        /// <summary>
        /// 네트워크가 없는 로컬 단독 실행에서의 발생원 ID.
        /// 스프린트 3~7이 각 컴포넌트에 상수 1로 박아뒀던 값과 동일하게 유지해,
        /// Net 배선이 없을 때 기존 스모크 리그가 그대로 동작하게 한다(GAP-15).
        /// </summary>
        public const ulong LocalFallbackPlayerId = 1;

        [Header("역할 (역할 배정 시스템 배선 전 로컬 테스트용)")]
        [SerializeField] private RoleType _role = RoleType.Runner;

        [Header("카메라")]
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private float _mouseSensitivity = 0.12f;

        [Tooltip("§12.6 조작 탭 'Y축 반전'. 설정 화면이 런타임에 바꾼다.")]
        [SerializeField] private bool _invertY;
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

        /// <summary>
        /// 네트워크(또는 로컬 판정)가 확정한 역할을 적용한다(<see cref="IRoleState"/>).
        /// 스프린트 11: 서버가 태그를 확정하면 <c>TagNetworkSync</c>가 이 플레이어를 Echo로 바꾼다.
        /// 로컬 단독 실행에서는 아무도 호출하지 않아 인스펙터의 <see cref="_role"/>이 유지된다.
        ///
        /// 이동 시뮬레이터는 Awake에서 초기 역할로 만들어지며, 역할 배율의 런타임 재적용은
        /// 이번 스코프가 아니다(태그된 메아리의 이동 특성 변경은 후속 과제) — 여기서는
        /// 태그 상태·밸브 역할 게이팅에 쓰이는 <see cref="Role"/> 값만 갱신한다.
        /// </summary>
        public void ApplyRole(RoleType role) => _role = role;

        /// <summary>
        /// 이 플레이어가 발생시키는 이벤트의 발생원 ID(§5.5/§6.1/§3.1).
        /// 기본값은 로컬 폴백이고, Net 레이어가 소유권 확정 시 실제 OwnerId로 덮어쓴다.
        /// </summary>
        public ulong PlayerId { get; private set; } = LocalFallbackPlayerId;

        /// <summary>Net 레이어(소유권 게이트)가 실제 네트워크 소유자 ID를 전달한다.</summary>
        public void SetPlayerId(ulong playerId) => PlayerId = playerId;

        /// <summary>
        /// 누적된 낙하 속도를 지운다(스프린트 20 리스폰 전용).
        ///
        /// **왜 필요한가**: 중력은 상한 없이 누적되므로 오래 떨어지면 초당 수백 m에 이른다.
        /// 그 상태로 순간이동시키면 다음 프레임에 한 번의 <c>Move</c>로 바닥을 뚫고 다시
        /// 떨어진다(접지 판정 전에 이동량이 바닥 두께를 넘는다). 이동 규칙 자체는 바꾸지 않고
        /// 리스폰 시점에만 속도를 0으로 되돌린다.
        /// </summary>
        public void ResetVerticalVelocity() => _verticalVelocity = 0f;

        /// <summary>
        /// §12.6 조작 탭 설정을 적용한다(스프린트 23). 설정 화면과 저장소가 부르며,
        /// 이동·태그 등 게임플레이 규칙에는 관여하지 않는다.
        /// </summary>
        /// <summary>
        /// §10.1 격리 대기처럼 **이동만** 잠글 때 쓴다(스프린트 24).
        ///
        /// <c>ILocalControlGate.SetLocalControl(false)</c>를 재사용하지 않는 이유: 그쪽은
        /// 소유권용이라 카메라·커서를 끄고 <c>LocalPlayerRegistry</c> 등록까지 해제한다.
        /// 격리에 그것을 쓰면 로컬 플레이어 참조가 사라져 HUD·스폰 배치가 전부 어긋난다
        /// (스프린트 22에서 실제로 겪은 등록 유실과 같은 유형의 사고가 된다).
        /// </summary>
        public bool MovementLocked { get; private set; }

        public void SetMovementLocked(bool locked) => MovementLocked = locked;

        public void ApplyLookSettings(float mouseSensitivity, bool invertY)
        {
            _mouseSensitivity = mouseSensitivity;
            _invertY = invertY;
        }

        /// <summary>
        /// 로컬 플레이어가 조종하는가. 네트워크가 없는 로컬 단독 실행에서는 아무도
        /// <see cref="SetLocalControl"/>을 호출하지 않으므로 true로 남아, 스프린트 3~7의
        /// 로컬 스모크 리그가 그대로 동작한다.
        /// </summary>
        public bool IsLocallyControlled { get; private set; } = true;

        /// <summary>
        /// Net 레이어(소유권 게이트)가 호출한다. false면 입력·카메라·커서 잠금을 모두
        /// 놓고, 이 캐릭터는 네트워크로 받은 Transform 값으로만 움직인다.
        /// </summary>
        public void SetLocalControl(bool isLocallyControlled)
        {
            if (IsLocallyControlled == isLocallyControlled)
            {
                // 값이 같아도 **등록 상태는 다시 확정한다**. 원격 pawn이 스폰되며 레지스트리를
                // 덮어썼다가 해제해 Current가 비어 있을 수 있는데, 여기서 조기 반환해 버리면
                // 이미 true인 로컬 pawn이 영영 재등록되지 않는다(실기에서 확정된 버그).
                // Register는 이미 등록된 대상이면 즉시 반환하므로 중복 부작용이 없다.
                PublishRegistration();
                return;
            }

            IsLocallyControlled = isLocallyControlled;
            ApplyLocalControlState();
            PublishRegistration();
        }

        /// <summary>
        /// 로컬 조종 캐릭터만 씬 컴포넌트들(파문 파이프라인·탈출 지점 등)의 기준이 된다.
        /// 네트워크 스폰이라 씬에서 미리 참조를 걸 수 없으므로 여기서 등록한다.
        /// </summary>
        private void PublishRegistration()
        {
            if (IsLocallyControlled)
                LocalPlayerRegistry.Register(this);
            else
                LocalPlayerRegistry.Unregister(this);
        }

        /// <summary>
        /// 원격 캐릭터는 카메라·오디오리스너를 꺼야 한다 — 안 그러면 클라이언트마다
        /// 카메라가 여러 개 활성화되어 화면이 섞이고 AudioListener 중복 경고가 난다.
        /// </summary>
        private void ApplyLocalControlState()
        {
            if (_cameraTransform != null)
            {
                var camera = _cameraTransform.GetComponent<Camera>();
                if (camera != null)
                    camera.enabled = IsLocallyControlled;

                var listener = _cameraTransform.GetComponent<AudioListener>();
                if (listener != null)
                    listener.enabled = IsLocallyControlled;
            }

            ApplyCursorLock(IsLocallyControlled);
        }

        private static void ApplyCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _simulator = new LocomotionSimulator(_role);

            if (_cameraTransform == null && Camera.main != null)
                _cameraTransform = Camera.main.transform;
        }

        private void OnEnable()
        {
            ApplyLocalControlState();
            // 네트워크가 없으면 SetLocalControl이 호출되지 않으므로 여기서 등록해야
            // 로컬 단독 실행(스프린트 3~7 스모크 리그)이 그대로 동작한다.
            PublishRegistration();
        }

        private void OnDisable()
        {
            ApplyCursorLock(false);
            LocalPlayerRegistry.Unregister(this);
        }

        private void Update()
        {
            // 원격 캐릭터는 입력을 일절 받지 않는다 — 위치·회전은 네트워크 동기화가
            // 전담하므로, 여기서 CharacterController를 건드리면 서로 싸운다.
            if (!IsLocallyControlled)
                return;

            // 시점 회전은 항상 허용한다 — §10.1 격리 중에도 술래가 주변을 볼 수 있어야
            // "격리 공간에서 대기"가 성립한다(눈까지 막으라는 규칙은 없다).
            ApplyLook();

            if (MovementLocked)
                return;

            ApplyMovement();
        }

        private void ApplyLook()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _cameraTransform == null)
                return;

            Vector2 delta = mouse.delta.ReadValue() * _mouseSensitivity;

            transform.Rotate(0f, delta.x, 0f);

            // §12.6 조작 탭 "Y축 반전" — 설정에서만 바뀌며 기본값은 종전과 동일(끔)이다.
            float pitchDelta = _invertY ? delta.y : -delta.y;
            _pitch = Mathf.Clamp(_pitch + pitchDelta, -_pitchLimit, _pitchLimit);
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
