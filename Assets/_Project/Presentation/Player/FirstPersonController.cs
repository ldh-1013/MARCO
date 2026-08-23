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

        [Header("§3.2 메아리 비행 (상승·하강 키는 §4.3 표에 없다 — GAP-64)")]
        [SerializeField] private Key _ghostAscendKey = Key.Space;
        [SerializeField] private Key _ghostDescendKey = Key.LeftCtrl;

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
        public void ApplyRole(RoleType role)
        {
            if (_role == role)
                return;

            _role = role;

            // 시뮬레이터를 새 역할로 다시 만든다(스프린트 27 후속 — 기존 이월 해소).
            // 이게 없으면 태그로 메아리가 돼도 §3.2 8.0m/s와 "메아리는 발소리 없음"이
            // 적용되지 않는다(시뮬레이터가 Awake 시점의 역할을 그대로 들고 있기 때문).
            _simulator = new LocomotionSimulator(_role);

            // §3.2 "충돌 없음(벽 통과)" — 비행 전환 시 컨트롤러를 놓고, 되돌아오면 다시 잡는다.
            ApplyGhostFlightState();
        }

        /// <summary>
        /// §3.2 유령 이동 상태를 <see cref="_characterController"/>에 반영한다.
        ///
        /// <b>왜 컨트롤러를 끄는가</b>: <c>CharacterController.Move</c>는 컴포넌트가 켜져 있는 한
        /// 캡슐 충돌을 수행한다(<c>detectCollisions</c>는 *남이 나를* 밀 때만 관여한다).
        /// §3.2가 요구하는 "벽 통과"는 컨트롤러를 비활성화하고 <c>Transform</c>을 직접
        /// 움직이는 방법으로만 성립한다. 되돌아올 때 다시 켜므로 지상 이동은 그대로다.
        ///
        /// 누적 낙하 속도도 함께 지운다 — 비행 중에는 중력을 적용하지 않으므로, 남겨 두면
        /// 지상 복귀 첫 프레임에 바닥을 뚫는다(<c>FallRecoveryDriver</c>가 겪은 것과 같은 함정).
        /// </summary>
        /// <summary>
        /// §3.2 메아리 은닉(GAP-63)을 이 pawn의 몸체 렌더러에 반영한다. **매 프레임 호출된다.**
        ///
        /// <b>두 역할이 모두 실시간으로 바뀐다</b>: 대상(이 pawn)이 태그당해 메아리가 되는 순간,
        /// 그리고 <b>뷰어 자신</b>이 나중에 태그당해 메아리가 되는 순간(그 전까지 안 보이던 다른
        /// 메아리들이 그때부터 보여야 한다). 그래서 어느 한쪽 이벤트에 걸지 않고 두 값을 매 프레임
        /// 다시 읽어 비교한다.
        ///
        /// <b>뷰어를 캐시하지 않는다(GAP-61)</b> — <c>LocalPlayerRegistry.Current</c>는 소유권이
        /// 확정되며 바뀔 수 있고, 1회성으로 굳히면 원격 pawn을 기준으로 판정하게 된다.
        ///
        /// 실제 <c>Renderer.enabled</c> 대입은 **값이 바뀔 때만** 한다 — 매 프레임 같은 값을 쓰면
        /// 렌더러가 불필요하게 더티 처리된다.
        /// </summary>
        private void RefreshEchoVisibility()
        {
            if (_bodyRenderers == null || _bodyRenderers.Length == 0)
                return;

            FirstPersonController viewer = LocalPlayerRegistry.Current;
            if (viewer == null)
                return; // 아직 로컬 pawn이 없다 — 다음 프레임에 다시 본다(상태를 흔들지 않는다).

            bool shouldRender = EchoVisibility.ShouldRender(viewer.Role, _role, ReferenceEquals(viewer, this));
            if (shouldRender == _bodyVisible)
                return;

            _bodyVisible = shouldRender;
            for (int i = 0; i < _bodyRenderers.Length; i++)
            {
                if (_bodyRenderers[i] != null)
                    _bodyRenderers[i].enabled = shouldRender;
            }

            Debug.Log($"[EchoVis] '{name}' 몸체 {(shouldRender ? "표시" : "숨김")} — " +
                      $"뷰어={viewer.Role}, 대상={_role} (§3.2 메아리 은닉, GAP-63)");
        }

        private void ApplyGhostFlightState()
        {
            if (_characterController == null)
                return;

            bool flying = GhostFlight.IsFlying(_role);
            _characterController.enabled = !flying;

            if (flying)
                _verticalVelocity = 0f;
        }

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
        /// 마우스를 포인터로 쓰는 모드가 커서 잠금을 잠시 풀어 둔 상태인가
        /// (<see cref="SetCursorReleased"/>). 잠금 계산과 시점 회전 억제에 함께 쓰인다.
        /// </summary>
        private bool _cursorReleased;

        /// <summary>몸체 렌더러(§3.2 메아리 은닉 대상). 자기 오브젝트 컴포넌트라 Awake에서 1회 수집한다.</summary>
        private Renderer[] _bodyRenderers;

        /// <summary>현재 몸체를 그리고 있는가. 값이 바뀔 때만 렌더러를 건드리기 위한 변경 감지용.</summary>
        private bool _bodyVisible = true;

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

            ApplyCursorLock(IsLocallyControlled && !_cursorReleased);
        }

        private static void ApplyCursorLock(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        /// <summary>
        /// 마우스를 **포인터로 쓰는 모드**(§3.2 노크 지점 지정 등)가 커서를 잠시 풀어 달라고 요청한다.
        /// 해제 중에는 시점 회전도 멈춘다 — 안 그러면 화면 밖에서 마우스를 움직이는 동안
        /// 1인칭 시점이 같이 돌아가, 모드를 닫았을 때 엉뚱한 방향을 보고 있게 된다.
        ///
        /// <b>커서 정책은 이 클래스가 단독으로 소유한다.</b> 호출자가 <see cref="Cursor"/>를 직접
        /// 만지면 "연 쪽과 닫는 쪽이 서로 다른 상태를 쓰는" 어긋남이 생기므로, 요청만 받고
        /// 실제 적용은 <see cref="ApplyLocalControlState"/>의 기존 규칙
        /// (<see cref="IsLocallyControlled"/>)과 함께 여기서 계산한다.
        ///
        /// <paramref name="released"/>를 false로 되돌리면 §4.1 1인칭 잠금으로 정확히 복원된다.
        /// </summary>
        public void SetCursorReleased(bool released)
        {
            if (_cursorReleased == released)
                return;

            _cursorReleased = released;
            ApplyLocalControlState();
        }

        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _simulator = new LocomotionSimulator(_role);

            // 몸체 렌더러(스프린트 9 `Body` 캡슐). 자기 오브젝트의 컴포넌트라 캐시해도 안전하다 —
            // GAP-61이 금지한 것은 **다른 pawn(LocalPlayerRegistry.Current)** 참조를 굳히는 것이다.
            _bodyRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);

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
            // §3.2 메아리 은닉(GAP-63)은 **소유권과 무관하다** — "내가 저 pawn을 그려야 하는가"의
            // 문제라 원격 pawn에서도 판정해야 한다. 그래서 아래 소유권 조기 반환보다 앞에 둔다.
            RefreshEchoVisibility();

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
            // 커서를 포인터로 쓰는 동안에는 시점을 돌리지 않는다(§3.2 노크 지정 등) —
            // 잠금이 풀린 상태에서도 delta는 계속 들어오므로, 막지 않으면 모드를 닫았을 때
            // 시점이 엉뚱한 방향으로 돌아가 있다.
            if (_cursorReleased)
                return;

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

            // §3.2 메아리: 자유 비행(충돌 없음·중력 없음). 시뮬레이터는 그대로 돌려 둔다 —
            // 상태 전이와 "메아리는 발소리 없음"(§5.1) 판정이 거기 있기 때문이다.
            if (GhostFlight.IsFlying(_role))
            {
                ApplyGhostMovement(moveAxis, keyboard);
                return; // 비행 중에는 발소리가 나올 수 없다(tick.Pulse는 항상 null이다).
            }

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

        /// <summary>
        /// §3.2 "자유 비행형 유령 카메라, 충돌 없음(벽 통과), 이동속도 8.0 m/s".
        ///
        /// 방향 계산은 Core <see cref="GhostFlight"/>(순수)가 하고, 여기서는 카메라 축을 넘겨주고
        /// <c>Transform</c>을 직접 움직이기만 한다 — <see cref="_characterController"/>는
        /// <see cref="ApplyGhostFlightState"/>가 이미 꺼 뒀으므로 벽을 통과한다.
        ///
        /// 상승·하강 키는 §4.3 표에 없어 임시로 Space/Left Ctrl을 쓴다(GAP-64).
        /// Left Ctrl은 §4.3상 잠수 키지만 메아리는 잠수할 수 없어 충돌하지 않는다.
        /// </summary>
        private void ApplyGhostMovement(Vector2 moveAxis, Keyboard keyboard)
        {
            float verticalAxis = 0f;
            if (keyboard[_ghostAscendKey].isPressed) verticalAxis += 1f;
            if (keyboard[_ghostDescendKey].isPressed) verticalAxis -= 1f;

            Transform view = _cameraTransform != null ? _cameraTransform : transform;

            Vector3 velocity = GhostFlight.Velocity(moveAxis, verticalAxis, view.forward, view.right);
            if (velocity == Vector3.zero)
                return;

            transform.position += velocity * Time.deltaTime;
        }
    }
}
