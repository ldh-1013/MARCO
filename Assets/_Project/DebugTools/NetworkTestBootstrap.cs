using FishNet;
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
    /// 스프린트 15(정리): 역할 수동 지정 키(K/R)를 제거했다 — 스프린트 13에서 서버가
    /// §6.2 표대로 역할을 자동 배정하고(<c>RoleAssigner</c>/<c>RoleNetworkSync</c>) 전 피어가
    /// 일관되게 인지하게 됐으므로, 로컬만 바꾸던 임시 키는 오히려 실제 배정과 어긋나 혼란을 준다.
    /// 접속(H/J)은 정식 로비 UI가 없어 여전히 필요하므로 남긴다.
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

        [Tooltip("연결 전 화면이 완전히 비지 않도록 임시로 켜 둘 카메라. 비워두면 Camera.main을 쓴다.")]
        [SerializeField] private GameObject _fallbackCameraObject;

        private bool _connectionStarted;

        private void Awake()
        {
            // 스프린트 18b 후속: 예전에는 여기서 Camera.main을 자동으로 집어 폴백 카메라로 삼았다.
            // 씬 분리 후에는 이 컴포넌트(맵 씬)가 **플레이어가 이미 스폰된 뒤**에 깨어나므로,
            // Camera.main이 플레이어 자신의 카메라를 가리킬 수 있다. 그 상태로 OnLocalPlayerReady가
            // 즉시 호출되면 **플레이어 카메라를 꺼 버려** 화면에 하늘만 남는다(실기에서 확인된 증상).
            // 인스펙터로 명시 지정된 경우에만 폴백 카메라를 다루도록 자동 탐색을 제거했다.
            if (_fallbackCameraObject == null)
                Debug.Log("[NetworkTestBootstrap] 폴백 카메라가 지정되지 않아 카메라를 건드리지 않습니다 " +
                          "(씬 분리 배치에서는 시스템 씬의 카메라가 그 역할을 합니다).");
        }

        private void OnEnable()
        {
            if (_fallbackCameraObject != null)
                _fallbackCameraObject.SetActive(true);

            LocalPlayerRegistry.WhenReady(OnLocalPlayerReady);

            Debug.Log($"[NetworkTestBootstrap] 연결 대기 중 — {_hostKey} 키: 호스트로 시작(서버+클라이언트), " +
                $"{_joinKey} 키: 클라이언트로 참가({_joinAddress}). " +
                $"역할은 접속 후 서버가 자동 배정한다(§6.2, 스프린트 13) — 수동 지정 키는 없다. " +
                $"이 창(Game 뷰)에 포커스가 있어야 키 입력이 들어간다.");
        }

        private void OnDisable()
        {
            LocalPlayerRegistry.StopWaiting(OnLocalPlayerReady);
        }

        private void Update()
        {
            // 스프린트 18: 정식 접속 경로(LobbyScreen + ConnectionService)가 씬에 있으면 키 처리를
            // 전부 양보한다 — 같은 H/J 키를 두 컴포넌트가 받아 접속이 이중 시작되는 것을 막는다.
            // 이 부트스트랩은 로비 실기 검증이 끝날 때까지의 폴백으로만 남으며(지시서 §1.7),
            // 검증 완료 후 DebugTools 어셈블리 전체와 함께 삭제된다.
            if (Marco.Core.Net.ConnectionServiceRegistry.Current != null)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (_connectionStarted)
                return;

            if (keyboard[_hostKey].wasPressedThisFrame)
                StartHost();
            else if (keyboard[_joinKey].wasPressedThisFrame)
                StartClient();
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
