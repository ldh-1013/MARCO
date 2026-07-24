using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 밸브 하나를 서버 권위로 동기화한다(스프린트 10, 네트워크 2단계 파일럿).
    ///
    /// **신뢰 모델**: 클라이언트는 <b>홀드 의사만</b> 서버에 요청하고(<see cref="ServerSubmitHold"/>),
    /// 밸브를 실제로 돌렸는지는 <b>서버만</b> 결정한다. 서버는 같은 오브젝트의
    /// <see cref="IValveHost"/>가 들고 있는 Core <see cref="Valve"/>를 <see cref="ServerValveDriver"/>로
    /// 구동하며(역할 GAP-5·상태·회전 시간 전부 Core 로직 그대로 재사용), 확정된 상태를
    /// <see cref="SyncVar{T}"/>로 전 클라이언트에 전파한다. 클라이언트는 "완료됐다"를
    /// 보낼 수단이 없으므로 <b>밸브를 즉시 열 수 없다</b> — 이것이 이 파일럿이 확립하는
    /// 서버 권위 패턴이며, 이후 태그·탈출·발소리에 같은 골격을 반복 적용한다.
    ///
    /// **어셈블리 경계**: Net은 Presentation을 참조하지 않는다. 밸브의 Core 상태기계는
    /// <c>GetComponent&lt;IValveHost&gt;()</c>(Core 인터페이스)로 찾고, 입력 계층과의
    /// 연결도 <see cref="IValveNetworkBridge"/>(Core)를 통해서만 한다.
    ///
    /// **로컬 폴백**: 네트워크가 시작되지 않으면 이 컴포넌트는 스폰되지 않아
    /// <see cref="NetworkActive"/>가 false다. 그때 Presentation은 이 브릿지를 무시하고
    /// 스프린트 5의 클라이언트 권위 경로를 그대로 쓴다(회귀 없음).
    /// </summary>
    public sealed class ValveNetworkSync : NetworkBehaviour, IValveNetworkBridge
    {
        // 서버가 확정해 전 클라이언트에 전파하는 권위 상태.
        private readonly SyncVar<ValveState> _state = new();
        private readonly SyncVar<float> _progress = new();

        private IValveHost _host;
        private ServerValveDriver _driver; // 권위 구동기 — 서버에서만 생성된다.

        // 클라이언트 송신 디듀프: 의사가 바뀔 때만 ServerRpc를 보낸다.
        private bool _hasSent;
        private bool _lastSentHeld;
        private ulong _lastSentPlayer;

        // ── IValveNetworkBridge ──────────────────────────────────────────

        /// <summary>
        /// NetworkObject가 스폰된 뒤에만 true. 로컬 단독 실행이나, 연결 전(Play는 됐지만
        /// 아직 H/J로 접속하지 않은 상태)에는 false다.
        ///
        /// <see cref="NetworkBehaviour.IsSpawned"/>는 내부적으로 <c>NetworkObject</c> 캐시
        /// 필드를 그대로 역참조한다. 그 캐시는 FishNet이 이 컴포넌트를 실제로
        /// 초기화(프리스폰 준비)할 때만 채워지므로, 그 전에 <c>IsSpawned</c>를 호출하면
        /// NullReferenceException이 난다. <c>ValveObjectiveTracker.Update</c>가 매 프레임
        /// <c>ValveBehaviour.IsOpen</c> → 이 프로퍼티를 거치는데, 연결 전에는 그 초기화가
        /// 아직 안 된 상태라 매 프레임 예외가 터졌다 — 여기서 캐시가 채워졌는지
        /// (<see cref="NetworkBehaviour.NetworkObject"/> null 여부)를 먼저 확인해 막는다.
        /// </summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        public ValveState State => _state.Value;
        public float Progress01 => _progress.Value;

        public void SubmitHoldIntent(ulong playerId, RoleType role, bool held)
        {
            // IsSpawned 직접 호출 금지 이유는 NetworkActive의 문서 참고 — 같은 NRE를 막는다.
            if (!NetworkActive)
                return;

            // 값이 바뀔 때만 서버로 보낸다(프레임마다 홀드 신호가 와도 1회만 전송).
            if (_hasSent && _lastSentHeld == held && _lastSentPlayer == playerId)
                return;

            _hasSent = true;
            _lastSentHeld = held;
            _lastSentPlayer = playerId;

            ServerSubmitHold(playerId, role, held);
        }

        // ── 서버: 검증·타이밍 ────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            EnsureHost();
            _driver = new ServerValveDriver(_host.Valve);
            // 스폰 시점의 권위 상태로 SyncVar를 맞춘다(씬 리셋/재접속 대비).
            _state.Value = _driver.State;
            _progress.Value = _driver.Progress01;
        }

        /// <summary>
        /// 밸브는 특정 플레이어가 소유하지 않으므로 소유권 검사를 끈다
        /// (<see cref="RpcAttribute"/> 기본값은 소유자만 호출 허용).
        /// <paramref name="caller"/>는 FishNet이 주입하는 실제 호출자 — 스푸핑 대응(GAP-16)에서 쓴다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitHold(ulong playerId, RoleType role, bool held, NetworkConnection caller = null)
        {
            if (_driver == null)
                return;

            if (held)
                _driver.BeginHold(playerId, role);
            else
                _driver.EndHold(playerId);

            PushState();
        }

        private void Update()
        {
            // 권위 타이머는 서버에서만 돈다. 클라이언트는 SyncVar 값만 표시한다.
            // IsServerStarted도 IsSpawned와 같은 이유로 NetworkObject 캐시가 찰 때까지는
            // 직접 호출하면 안 된다(NetworkActive 문서 참고) — Update는 매 프레임 도는
            // 경로라 이 가드가 없으면 연결 전 내내 예외가 반복된다.
            if (_driver == null || !NetworkActive || !IsServerStarted)
                return;

            if (!_driver.IsRotating)
                return;

            bool opened = _driver.Tick(Time.deltaTime);
            PushState();

            if (opened)
                Debug.Log($"[ValveNet:Server] {name} 개방 완료 — 서버가 회전 시간을 모두 확정 (§6.1)");
        }

        /// <summary>권위 상태를 SyncVar에 반영한다. 서버 전용.</summary>
        private void PushState()
        {
            if (_state.Value != _driver.State)
            {
                ValveState prev = _state.Value;
                _state.Value = _driver.State;
                Debug.Log($"[ValveNet:Server] {name} 상태 {prev} → {_driver.State} (홀더={FormatHolder(_driver.HolderId)})");
            }

            _progress.Value = _driver.Progress01;
        }

        private static string FormatHolder(ulong? holder) => holder.HasValue ? holder.Value.ToString() : "-";

        // ── 클라이언트: 수신 상태 로깅(양쪽 창에서 반영 확인용) ───────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _state.OnChange += OnStateChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _state.OnChange -= OnStateChanged;
        }

        private void OnStateChanged(ValveState prev, ValveState next, bool asServer)
        {
            // 서버 측 로그는 PushState가 담당하므로, 여기서는 클라이언트 수신만 찍는다.
            if (asServer)
                return;

            Debug.Log($"[ValveNet:Client] {name} 상태 {prev} → {next} — 서버로부터 수신, 화면 반영");
        }

        // ── 헬퍼 ─────────────────────────────────────────────────────────

        private void EnsureHost()
        {
            if (_host == null)
                _host = GetComponent<IValveHost>();

            if (_host == null)
                Debug.LogError($"[ValveNet] {name}에 IValveHost(ValveBehaviour)가 없습니다 — 서버 권위 밸브를 구동할 수 없습니다.");
        }
    }
}
