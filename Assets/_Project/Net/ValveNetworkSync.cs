using System.Collections.Generic;
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

        /// <summary>
        /// 스폰된 밸브 동기화 컴포넌트들(스프린트 17). 라운드 재시작 시
        /// <c>RoundNetworkSync</c>가 전체를 초기화하려고 열거한다 — 소비자도 Net이라
        /// Core 레지스트리를 거칠 필요가 없다(<c>RoleNetworkSync.Spawned</c>와 같은 패턴).
        /// </summary>
        internal static readonly List<ValveNetworkSync> Spawned = new List<ValveNetworkSync>();

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

            // playerId·role은 **전송하지 않는다** — 서버가 호출자에서 직접 읽는다(아래 ServerSubmitHold).
            // 여기서 받은 값은 송신 디듀프 판단에만 쓴다.
            ServerSubmitHold(held);
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
        ///
        /// <b>클라이언트는 "누르고 있다/뗐다"만 보낸다(GAP-16 해소)</b>. 플레이어 ID와 역할은
        /// FishNet이 주입한 <paramref name="caller"/>에서 서버가 직접 읽는다 —
        /// <see cref="RoleNetworkSync.TryGetCallerIdentity"/>. 페이로드에 주장할 값 자체가 없으므로
        /// 메아리가 <c>role=Runner</c>를 보내 GAP-5(§3.2 물리 상호작용 불가)를 우회하던 경로가 닫힌다.
        /// <c>PulseNetworkSync</c>가 소리 종류만 받고 나머지를 서버가 확정하는 것과 같은 원칙이다(GAP-24).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitHold(bool held, NetworkConnection caller = null)
        {
            if (_driver == null)
                return;

            // 호출자의 플레이어 오브젝트를 못 찾으면 신원을 확정할 수 없다 — 폐기(NRE 가드 겸용).
            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
            {
                Debug.LogWarning($"[ValveNet:Server] {name} 홀드 폐기 — 호출자의 플레이어 오브젝트/역할을 " +
                                 "찾을 수 없어 서버 측 신원을 확정할 수 없습니다.");
                return;
            }

            // 스프린트 18: 밸브는 라운드 중에만 조작 가능하다. 로비(§12.3 튜토리얼 자유 이동)·
            // 카운트다운·결과 화면에서의 조작을 서버가 차단한다 — 파문은 §12.3상 로비에서도
            // 의도된 기능(조작 학습)이라 게이트하지 않는 것과 대조적이다.
            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.InGame)
            {
                Debug.Log($"[ValveNet:Server] {name} 홀드 무시 — 라운드 중이 아님({RoundNetworkSync.ServerPhase})");
                return;
            }

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

            // 스프린트 18: 라운드가 끝나는 순간 회전 중이던 밸브가 계속 돌아 결과 후에 열리는 것을
            // 막는다(페이즈가 InGame을 벗어나면 타이머 동결 — 어차피 재시작 시 리셋된다).
            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.InGame)
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

            if (!Spawned.Contains(this))
                Spawned.Add(this);
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _state.OnChange -= OnStateChanged;
            Spawned.Remove(this);
        }

        /// <summary>
        /// 새 라운드를 위해 서버 권위 밸브 상태를 초기값으로 되돌린다(스프린트 17). 서버 전용.
        ///
        /// <see cref="IValveHost"/>(<c>ValveBehaviour</c>)가 Core <see cref="Valve"/> 인스턴스를
        /// 새로 만들므로, 옛 인스턴스를 감싸고 있던 구동기도 **새로 만들어야** 리셋이 반영된다.
        /// 그 뒤 SyncVar를 초기값으로 밀어 전 클라이언트의 표시(색·카운트)도 함께 되돌린다.
        /// </summary>
        internal void ServerResetForNewRound()
        {
            EnsureHost();
            if (_host == null)
                return;

            _host.ResetValveForNewRound();
            _driver = new ServerValveDriver(_host.Valve);

            _state.Value = _driver.State;
            _progress.Value = _driver.Progress01;

            // 송신 디듀프 상태도 지워, 새 라운드의 첫 홀드 의사가 반드시 서버로 전달되게 한다.
            _hasSent = false;
            _lastSentHeld = false;
            _lastSentPlayer = 0;
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 static 상태가 남는다(스프린트 13과 같은 안전장치).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Spawned.Clear();

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
