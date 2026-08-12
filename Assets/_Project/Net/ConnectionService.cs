using FishNet;
using FishNet.Transporting;
using Marco.Core.Net;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 접속 시작(호스트/참가)의 정식 경로(스프린트 18, §12.2 방 만들기/코드 입장).
    /// <see cref="IConnectionService"/>(Core) 구현체 — 로비 UI(Presentation)가 §15.2를 넘어
    /// FishNet 접속을 시작할 수 있게 한다.
    ///
    /// 스프린트 8~18b 동안 이 역할은 임시 도구(<c>DebugTools/NetworkTestBootstrap</c>)의 H/J 키가
    /// 맡았다. 로비 실기 검증이 끝나 **스프린트 19에서 그 도구와 어셈블리를 전부 제거**했고,
    /// 이제 접속 경로는 이 서비스 하나뿐이다(§12.2 MainMenu → LobbyEntry).
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

        /// <summary>호스트로 시작했는가(취소 시 서버까지 내려야 하는지 판단).</summary>
        private bool _startedAsHost;

        private bool _subscribed;

        private void OnEnable()
        {
            ConnectionServiceRegistry.Register(this);
            TrySubscribe();
        }

        private void OnDisable()
        {
            ConnectionServiceRegistry.Unregister(this);
            Unsubscribe();
        }

        // ── 접속 상태 구독 (재시도 경로의 핵심) ───────────────────────────

        /// <summary>
        /// FishNet 클라이언트 접속 상태를 구독한다. <see cref="OnEnable"/> 시점에 NetworkManager가
        /// 아직 준비되지 않았을 수 있어, 접속 시작 직전에도 다시 시도한다(멱등).
        /// </summary>
        private void TrySubscribe()
        {
            if (_subscribed || InstanceFinder.ClientManager == null)
                return;

            InstanceFinder.ClientManager.OnClientConnectionState += OnClientConnectionState;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.OnClientConnectionState -= OnClientConnectionState;

            _subscribed = false;
        }

        /// <summary>
        /// 접속이 완전히 끝나면 <see cref="HasStarted"/>를 되돌려 **다시 시도할 수 있게** 한다.
        ///
        /// <b>왜 필요한가</b>: 이전에는 <see cref="HasStarted"/>가 시도 즉시 true가 되고 실패해도
        /// 돌아오지 않아, 주소를 한 번만 틀려도 <c>LobbyScreen</c>이 "접속 중…"에서 영구 정지했다
        /// (재시도하려면 Play를 다시 시작해야 했다).
        ///
        /// <b>실패한 시도도 여기로 온다</b> — 연결이 성립하지 않으면 전송 계층이
        /// Starting → <see cref="LocalConnectionState.Stopped"/>로 떨어뜨린다. 정상 플레이 중
        /// 호스트가 나가 끊긴 경우도 같은 경로이며, 그때도 입력 상태로 돌아가는 것이 맞다.
        /// </summary>
        private void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Stopped || !HasStarted)
                return;

            HasStarted = false;
            _startedAsHost = false;
            Debug.Log("[Connection] 접속이 종료됐습니다 — 다시 시도할 수 있습니다(§12.2). " +
                      "접속에 실패한 경우에도 이 경로로 돌아옵니다.");
        }

        // ── 접속 시작·취소 ────────────────────────────────────────────────

        public void StartHost()
        {
            if (HasStarted)
                return;

            if (InstanceFinder.ServerManager == null || InstanceFinder.ClientManager == null)
            {
                Debug.LogError("[Connection] NetworkManager를 찾지 못했습니다 — 씬 구성을 확인하세요(Diagnose Network Setup).");
                return;
            }

            TrySubscribe(); // OnEnable이 너무 일렀을 수 있다 — 시작 직전에 확실히 건다.

            HasStarted = true;
            _startedAsHost = true;
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

            TrySubscribe();

            HasStarted = true;
            _startedAsHost = false;
            InstanceFinder.ClientManager.StartConnection(address);
            Debug.Log($"[Connection] 클라이언트로 참가 — {address} (§12.2 코드 입장, MVP 직결 GAP-28).");
        }

        /// <summary>
        /// 진행 중인 접속을 중단한다(§12.2 재시도 경로).
        ///
        /// 전송 계층을 실제로 내린다 — 플래그만 되돌리면 FishNet이 계속 시도하다가 나중에
        /// 접속돼, 화면은 입력 상태인데 뒤에서 세션이 살아 있는 상태가 된다.
        /// <see cref="OnClientConnectionState"/>가 Stopped를 받아 플래그를 되돌리지만,
        /// 콜백이 오지 않는 구성(구독 실패 등)에서도 빠져나올 수 있게 여기서도 내린다.
        /// </summary>
        public void Cancel()
        {
            if (!HasStarted)
                return;

            if (InstanceFinder.ClientManager != null)
                InstanceFinder.ClientManager.StopConnection();

            if (_startedAsHost && InstanceFinder.ServerManager != null)
                InstanceFinder.ServerManager.StopConnection(sendDisconnectMessage: true);

            HasStarted = false;
            _startedAsHost = false;
            Debug.Log("[Connection] 접속을 취소했습니다 — 다시 시도할 수 있습니다(§12.2).");
        }
    }
}
