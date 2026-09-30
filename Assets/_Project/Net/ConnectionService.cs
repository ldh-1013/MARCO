using System;
using System.Collections.Generic;
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

        public string LastFailure { get; private set; }

        public JoinAddress RetryAddress =>
            _hasTarget ? _target
            : JoinAddressParser.TryParse(_defaultAddress, out JoinAddress parsed, out _) ? parsed
            : new JoinAddress("localhost", JoinAddressParser.DefaultPort);

        public string AddressLine =>
            _startedAsHost ? ConnectionMessages.HostAddressLine(_lanAddress, _hostPort)
            : _hasTarget ? ConnectionMessages.ClientAddressLine(_target)
            : $"방코드: {(string.IsNullOrWhiteSpace(_defaultAddress) ? "-" : _defaultAddress)}";

        /// <summary>호스트로 시작했는가(취소 시 서버까지 내려야 하는지 판단).</summary>
        private bool _startedAsHost;

        // 09-30 — 직전 참가 주소 · 이번 시도가 한 번이라도 연결됐는가 · 호스트 포트 · 호스트 LAN IP.
        private JoinAddress _target;
        private bool _hasTarget;
        private bool _reachedStarted;
        private ushort _hostPort = JoinAddressParser.DefaultPort;
        private string _lanAddress;

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
            if (args.ConnectionState == LocalConnectionState.Started)
                _reachedStarted = true;

            if (args.ConnectionState != LocalConnectionState.Stopped || !HasStarted)
                return;

            // 09-30 — 원인 후보를 화면에 남긴다(주소 오타 / 호스트 미실행 / 방화벽 · 터널 미연결). 재시도 경로는 그대로.
            if (_startedAsHost)
                LastFailure = _reachedStarted ? null : $"방을 열지 못했습니다 — 포트 {_hostPort}가 이미 쓰이는 중일 수 있습니다(-hostport로 바꿀 수 있습니다)";
            else if (_hasTarget)
                LastFailure = _reachedStarted ? ConnectionMessages.Disconnected(_target) : ConnectionMessages.JoinFailed(_target);

            if (LastFailure != null)
                Debug.LogWarning("[Connection] " + LastFailure);

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

            // 09-30 — 실행 인자 -hostport(터널 도구 설정용). 없으면 7770, 틀리면 7770으로 열고 알린다.
            if (!LaunchArguments.TryGetHostPort(Environment.GetCommandLineArgs(), out _hostPort, out JoinAddressError portError))
                Debug.LogWarning($"[Connection] -hostport 무시 — {JoinAddressParser.Describe(portError)}. 기본 포트 {_hostPort}로 연다.");

            _lanAddress = LanAddress.PickPreferred(LocalIpv4Candidates());
            LastFailure = null;
            _reachedStarted = false;

            HasStarted = true;
            _startedAsHost = true;
            InstanceFinder.ServerManager.StartConnection(_hostPort);
            InstanceFinder.ClientManager.StartConnection("localhost", _hostPort);
            Debug.Log($"[Connection] 호스트로 시작 — 서버+클라이언트 동시 시작(§12.2 방 만들기). 포트 {_hostPort} · " +
                      ConnectionMessages.HostAddressLine(_lanAddress, _hostPort));
        }

        public void StartClient(JoinAddress address)
        {
            if (HasStarted)
                return;

            if (InstanceFinder.ClientManager == null)
            {
                Debug.LogError("[Connection] NetworkManager를 찾지 못했습니다 — 씬 구성을 확인하세요(Diagnose Network Setup).");
                return;
            }

            if (string.IsNullOrWhiteSpace(address.Host))
                address = RetryAddress;

            TrySubscribe();

            _target = address;
            _hasTarget = true;
            LastFailure = null;
            _reachedStarted = false;

            HasStarted = true;
            _startedAsHost = false;
            InstanceFinder.ClientManager.StartConnection(address.Host, address.Port);
            Debug.Log($"[Connection] 클라이언트로 참가 — {address} (§12.2 코드 입장, 직결 GAP-28 · 호스트 이름 · 포트 지정 09-30).");
        }

        /// <summary>
        /// 이 PC의 IPv4 후보(09-30 — 호스트 로비에 LAN IP 표시). 켜진 인터페이스의 유니캐스트 IPv4. 실패하면 빈 목록(표시만 빠진다).
        /// </summary>
        private static List<string> LocalIpv4Candidates()
        {
            var result = new List<string>();
            try
            {
                foreach (System.Net.NetworkInformation.NetworkInterface ni in
                         System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up)
                        continue;

                    foreach (System.Net.NetworkInformation.UnicastIPAddressInformation info in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (info.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            result.Add(info.Address.ToString());
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Connection] LAN IP 조회 실패 — 주소 줄에 포트만 보인다: {e.Message}");
            }

            return result;
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
            LastFailure = null; // 사용자가 취소했다 — 실패 안내를 남기지 않는다
            Debug.Log("[Connection] 접속을 취소했습니다 — 다시 시도할 수 있습니다(§12.2).");
        }
    }
}
