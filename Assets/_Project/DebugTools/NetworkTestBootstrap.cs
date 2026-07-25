using FishNet;
using Marco.Core.Role;
using Marco.Presentation.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.DebugTools
{
    /// <summary>
    /// [임시 디버그 도구 — 정식 로비 UI가 생기면 이 클래스와
    /// <c>Assets/_Project/DebugTools/</c> 전체를 삭제한다]
    ///
    /// 로비 UI(접속 화면) 스프린트 전까지 로컬 2-클라이언트 네트워크 테스트를
    /// 키 입력만으로 시작하기 위한 도구다(docs/수동검증_절차.md §10-6).
    /// H = 호스트(서버+클라이언트 동시 시작), J = 클라이언트로 참가(localhost).
    ///
    /// Net(FishNet `InstanceFinder`)과 Presentation(`LocalPlayerRegistry`)을 동시에
    /// 참조한다 — §15.2가 금지하는 건 Net과 Presentation "서로"의 직접 참조이지,
    /// 이런 임시 조정용 제3의 어셈블리가 둘을 함께 쓰는 것이 아니다. 정식 로비 UI는
    /// 이 역할을 대체할 것이므로 이 클래스는 그 경계를 영구히 흔들지 않는다.
    /// </summary>
    public sealed class NetworkTestBootstrap : MonoBehaviour
    {
        [Header("임시 디버그 — 로비 UI 작업 시 이 컴포넌트를 제거할 것")]
        [SerializeField] private Key _hostKey = Key.H;
        [SerializeField] private Key _joinKey = Key.J;
        [SerializeField] private string _joinAddress = "localhost";

        [Tooltip("스프린트 11 태그 검증용: 역할 배정 네트워크화(다음 스프린트) 전까지, 이 키로 " +
                 "로컬 플레이어를 술래로 만들어 태그를 테스트한다. 로비/역할 배정이 생기면 제거.")]
        [SerializeField] private Key _becomeSeekerKey = Key.K;

        [Tooltip("스프린트 11 태그 검증용: 로컬 플레이어를 러너로 되돌린다(K의 반대). " +
                 "주의 — 역할은 아직 네트워크 동기화되지 않으므로 이 지정은 '이 기기'에서만 유효하다. " +
                 "다른 피어가 보는 이 플레이어 프록시의 역할은 프리팹 기본값(현재 Runner)으로 남는다. " +
                 "로비/역할 배정이 생기면 제거.")]
        [SerializeField] private Key _becomeRunnerKey = Key.R;

        [Tooltip("연결 전 화면이 완전히 비지 않도록 임시로 켜 둘 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private GameObject _fallbackCameraObject;

        private bool _connectionStarted;

        private void Awake()
        {
            if (_fallbackCameraObject == null && Camera.main != null)
                _fallbackCameraObject = Camera.main.gameObject;
        }

        private void OnEnable()
        {
            if (_fallbackCameraObject != null)
                _fallbackCameraObject.SetActive(true);

            LocalPlayerRegistry.WhenReady(OnLocalPlayerReady);

            Debug.Log($"[NetworkTestBootstrap] 연결 대기 중 — {_hostKey} 키: 호스트로 시작(서버+클라이언트), " +
                $"{_joinKey} 키: 클라이언트로 참가({_joinAddress}), {_becomeSeekerKey} 키: 로컬 플레이어를 술래로, " +
                $"{_becomeRunnerKey} 키: 로컬 플레이어를 러너로(둘 다 태그 검증용). " +
                $"이 창(Game 뷰)에 포커스가 있어야 키 입력이 들어간다.");
        }

        private void OnDisable()
        {
            LocalPlayerRegistry.StopWaiting(OnLocalPlayerReady);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            // 태그 검증용 역할 전환은 연결 후에도 눌러야 하므로 _connectionStarted 가드 밖에 둔다.
            if (keyboard[_becomeSeekerKey].wasPressedThisFrame)
                SetLocalRole(RoleType.Seeker);
            if (keyboard[_becomeRunnerKey].wasPressedThisFrame)
                SetLocalRole(RoleType.Runner);

            if (_connectionStarted)
                return;

            if (keyboard[_hostKey].wasPressedThisFrame)
                StartHost();
            else if (keyboard[_joinKey].wasPressedThisFrame)
                StartClient();
        }

        /// <summary>
        /// [임시] 로컬 플레이어의 역할을 수동 지정한다(스프린트 11 태그 검증). 역할 배정이
        /// 아직 네트워크화되지 않아, 태그를 테스트하려면 한 클라이언트를 술래로 지정해야 한다.
        ///
        /// **네트워크 전파 안 됨**: 이 호출은 <see cref="LocalPlayerRegistry.Current"/>(이 기기의
        /// 로컬 플레이어)의 <c>_role</c>만 바꾼다. 역할은 SyncVar가 아니므로 다른 피어가 보는
        /// 이 플레이어 프록시의 역할은 프리팹 기본값(현재 Runner)으로 남는다. 그래서
        /// "술래가 상대를 태그"는 <b>술래 자신의 기기</b>에서 상대(프리팹 기본 Runner)를 보고 판정하는
        /// 방식으로 성립한다 — 상대가 R을 눌러 자기 화면에서 러너로 바꿔야 하는 것이 아니다.
        /// </summary>
        private void SetLocalRole(RoleType role)
        {
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null)
            {
                Debug.LogWarning("[NetworkTestBootstrap] 로컬 플레이어가 아직 없습니다 — 스폰 후 다시 누르세요.");
                return;
            }

            player.ApplyRole(role);
            string hint = role == RoleType.Seeker
                ? "이제 러너에게 1.2m 접근하면 태그 요청을 보냅니다."
                : "이 기기에서 로컬 플레이어를 러너로 되돌렸습니다.";
            Debug.Log($"[NetworkTestBootstrap] 로컬 플레이어 역할 = {role} (태그 검증용, 이 기기 한정). {hint}");
        }

        private void StartHost()
        {
            if (InstanceFinder.ServerManager == null || InstanceFinder.ClientManager == null)
            {
                Debug.LogError("[NetworkTestBootstrap] NetworkManager를 찾지 못했습니다 — 씬에 NetworkManager 오브젝트가 있는지 확인하세요.");
                return;
            }

            _connectionStarted = true;
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
            Debug.Log("[NetworkTestBootstrap] 호스트로 시작 — 서버+클라이언트를 동시에 시작했습니다.");
        }

        private void StartClient()
        {
            if (InstanceFinder.ClientManager == null)
            {
                Debug.LogError("[NetworkTestBootstrap] NetworkManager를 찾지 못했습니다 — 씬에 NetworkManager 오브젝트가 있는지 확인하세요.");
                return;
            }

            _connectionStarted = true;
            InstanceFinder.ClientManager.StartConnection(_joinAddress);
            Debug.Log($"[NetworkTestBootstrap] 클라이언트로 참가를 시도합니다 — {_joinAddress}");
        }

        /// <summary>로컬 플레이어가 스폰·등록되면 임시 카메라를 끈다(플레이어 자신의 카메라가 이미 켜져 있다).</summary>
        private void OnLocalPlayerReady(FirstPersonController _)
        {
            if (_fallbackCameraObject != null)
                _fallbackCameraObject.SetActive(false);

            Debug.Log("[NetworkTestBootstrap] 로컬 플레이어 스폰 확인 — 임시 카메라를 비활성화했습니다.");
        }
    }
}
