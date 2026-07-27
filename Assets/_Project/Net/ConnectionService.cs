using FishNet;
using Marco.Core.Net;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 접속 시작(호스트/참가)의 정식 경로(스프린트 18, §12.2 방 만들기/코드 입장).
    /// <see cref="IConnectionService"/>(Core) 구현체 — 로비 UI(Presentation)가 §15.2를 넘어
    /// FishNet 접속을 시작할 수 있게 한다.
    ///
    /// 지금까지 이 역할은 <c>DebugTools/NetworkTestBootstrap</c>의 H/J 키가 임시로 맡았다.
    /// 이 서비스가 등록되면 부트스트랩은 키 처리를 양보하고(중복 시작 방지), 로비 실기 검증이
    /// 끝나면 부트스트랩과 DebugTools 어셈블리 전체를 제거한다(스프린트 18 지시서 §1.7).
    ///
    /// NetworkBehaviour가 아니다 — 접속 시작은 스폰 전에 가능해야 하므로 평범한 MonoBehaviour로
    /// 두고 `InstanceFinder`(FishNet 정적 진입점)를 쓴다. 씬 YAML로 안전하게 배선 가능
    /// (에디터 생성값 없음 — <c>ValveVisualIndicator</c>·<c>InGameHud</c> 선례).
    /// </summary>
    public sealed class ConnectionService : MonoBehaviour, IConnectionService
    {
        [Tooltip("참가 기본 주소. Tugboat LAN 직결(MVP) — 방코드 매칭은 Steam 단계(GAP-28).")]
        [SerializeField] private string _defaultAddress = "localhost";

        public bool HasStarted { get; private set; }

        public string DefaultAddress => _defaultAddress;

        private void OnEnable() => ConnectionServiceRegistry.Register(this);
        private void OnDisable() => ConnectionServiceRegistry.Unregister(this);

        public void StartHost()
        {
            if (HasStarted)
                return;

            if (InstanceFinder.ServerManager == null || InstanceFinder.ClientManager == null)
            {
                Debug.LogError("[Connection] NetworkManager를 찾지 못했습니다 — 씬 구성을 확인하세요(Diagnose Network Setup).");
                return;
            }

            HasStarted = true;
            InstanceFinder.ServerManager.StartConnection();
            InstanceFinder.ClientManager.StartConnection();
            Debug.Log("[Connection] 호스트로 시작 — 서버+클라이언트 동시 시작(§12.2 방 만들기).");
        }

        public void StartClient(string address)
        {
            if (HasStarted)
                return;

            if (InstanceFinder.ClientManager == null)
            {
                Debug.LogError("[Connection] NetworkManager를 찾지 못했습니다 — 씬 구성을 확인하세요(Diagnose Network Setup).");
                return;
            }

            if (string.IsNullOrWhiteSpace(address))
                address = _defaultAddress;

            HasStarted = true;
            InstanceFinder.ClientManager.StartConnection(address);
            Debug.Log($"[Connection] 클라이언트로 참가 — {address} (§12.2 코드 입장, MVP 직결 GAP-28).");
        }
    }
}
