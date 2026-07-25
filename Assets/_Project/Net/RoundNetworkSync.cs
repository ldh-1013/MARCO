using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using Marco.Core.GameFlow;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 라운드 전체(타이머·탈출·최종 판정)를 서버 권위로 동기화한다(스프린트 12,
    /// 밸브·태그 패턴 재사용). GAP-18에서 이월됐던 "클라이언트마다 다른 결과" 위험을 해소한다.
    ///
    /// **신뢰 모델**: 지금(스프린트 6)은 각 클라이언트가 로컬 타이머를 돌리고 §6.3을 독립
    /// 계산했다. 이제 <b>서버 하나</b>가 <see cref="ServerRoundDriver"/>로 타이머를 굴리고,
    /// 탈출을 재검증하고(§5.3), <see cref="WinConditionEvaluator"/>를 서버에서만 호출해
    /// 확정한 <see cref="SyncVar{T}"/>(남은 시간·탈출 수·최종 결과)를 전 클라이언트에 전파한다.
    /// 클라이언트(<c>RoundCoordinator</c>)는 자체 판정을 멈추고 이 값만 반영한다.
    ///
    /// **판정 입력의 출처**:
    /// - 밸브 게이트(개방 수·전체·게이트 개방): <see cref="EscapeGateRegistry"/>(Core) — 이미
    ///   서버 권위인 밸브 SyncVar를 <c>ValveObjectiveTracker</c>가 집계한 값이다.
    /// - 전원 태그: <see cref="TagTargetRegistry"/>(Core) — 이미 서버 권위인 태그 SyncVar 집합.
    /// - 탈출: 이 컴포넌트가 <see cref="ServerSubmitEscape"/>로 직접 재검증·집계.
    /// - 타이머: 이 컴포넌트가 소유.
    ///
    /// **어셈블리 경계**: Net은 Presentation을 참조하지 않는다. 밸브 게이트·태그·라운드 소비자와의
    /// 연결은 전부 Core 인터페이스/레지스트리를 통해서만 한다.
    ///
    /// **로컬 폴백 + NRE 가드**: 네트워크 미시작 시 스폰되지 않아 <see cref="NetworkActive"/>가
    /// false다. FishNet의 <c>IsSpawned</c>/<c>IsServerStarted</c>는 초기화 전 내부 캐시가 null이라
    /// 직접 호출하면 NRE가 나므로(스프린트 10 교훈), <see cref="NetworkBehaviour.NetworkObject"/>
    /// null 여부를 먼저 확인한다.
    /// </summary>
    public sealed class RoundNetworkSync : NetworkBehaviour, IRoundNetworkBridge
    {
        [Tooltip("§6.2 제한시간. 4인 MVP=600초. 로컬 RoundTimer.FourPlayerSeconds와 같은 값을 유지할 것.")]
        [SerializeField] private float _roundDurationSeconds = 600f;

        // 서버가 확정해 전 클라이언트에 전파하는 권위 상태.
        private readonly SyncVar<float> _remaining = new();
        private readonly SyncVar<int> _escaped = new();
        private readonly SyncVar<RoundResult> _result = new(); // 기본값 = RoundResult.InProgress(0)

        private ServerRoundDriver _driver; // 서버에서만 생성된다.

        // 역할 배정 정렬용 재사용 버퍼(매 프레임 할당 방지 — T8 성능 조사의 무할당 원칙).
        private readonly List<RoleNetworkSync> _assignBuffer = new List<RoleNetworkSync>();

        // ── IRoundNetworkBridge ───────────────────────────────────────────

        /// <summary>NetworkObject가 스폰된 뒤에만 true(연결 전 NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        public float RemainingSeconds => _remaining.Value;
        public int EscapedCount => _escaped.Value;
        public RoundResult Result => _result.Value;

        public void SubmitEscapeIntent(ulong playerId, RoleType role)
        {
            if (!NetworkActive)
                return;

            ServerSubmitEscape(playerId, role);
        }

        // ── 서버: 타이머·판정 ────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            _driver = new ServerRoundDriver(_roundDurationSeconds);
            _remaining.Value = _driver.RemainingSeconds;
            _escaped.Value = 0;
            _result.Value = RoundResult.InProgress;
            Debug.Log($"[RoundNet:Server] 라운드 시작 — 서버 권위 타이머 {_roundDurationSeconds:0}초 (§6.2)");
        }

        /// <summary>
        /// 탈출은 특정 플레이어가 소유하지 않는 라운드 오브젝트에서 처리되므로 소유권 검사를 끈다.
        /// <paramref name="caller"/>는 FishNet이 주입하는 탈출 요청자의 커넥션(향후 위치 스푸핑
        /// 방지에 쓸 수 있으나, 현재는 밸브 GAP-17과 동종으로 이월 — GAP-20).
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitEscape(ulong playerId, RoleType role, NetworkConnection caller = null)
        {
            if (_driver == null || _driver.IsDecided)
                return;

            bool gateOpen = CurrentGateOpen();
            if (!_driver.TryRegisterEscape(playerId, role, gateOpen))
            {
                Debug.Log($"[RoundNet:Server] 탈출 거부 — playerId={playerId} 서버 재검증 실패 " +
                          $"(gateOpen={gateOpen}, role={role}) §5.3");
                return;
            }

            _escaped.Value = _driver.EscapedCount;
            Debug.Log($"[RoundNet:Server] 탈출 확정 — playerId={playerId} (누적 {_driver.EscapedCount}명)");
            EvaluateAndPush();
        }

        private void Update()
        {
            // 권위 타이머·판정은 서버에서만. IsServerStarted도 IsSpawned와 같은 이유로
            // NetworkObject 캐시가 찰 때까지 직접 호출하면 안 된다(NetworkActive 문서 참고).
            if (_driver == null || !NetworkActive || !IsServerStarted)
                return;

            // 스프린트 13: 라운드를 관장하는 서버가 역할 배정도 소유한다(§14.3 RoleAssigned).
            // 판정보다 먼저 호출해 이번 프레임의 판정이 최신 역할을 보게 한다.
            EnsureRolesAssigned();

            if (_driver.IsDecided)
                return;

            _driver.Tick(Time.deltaTime);
            if (_remaining.Value != _driver.RemainingSeconds)
                _remaining.Value = _driver.RemainingSeconds;

            EvaluateAndPush();
        }

        /// <summary>서버 판정을 1회 수행하고, 새로 결정되면 결과 SyncVar에 반영한다. 서버 전용.</summary>
        private void EvaluateAndPush()
        {
            IEscapeGateState gate = EscapeGateRegistry.Current;
            int opened = gate != null ? gate.OpenedValves : 0;
            int total = gate != null ? gate.TotalValves : 0;
            bool allTagged = ServerRoundDriver.AllRunnersTagged(TagTargetRegistry.Targets);

            if (_driver.Evaluate(opened, total, allTagged))
            {
                _result.Value = _driver.Result;
                Debug.Log($"[RoundNet:Server] 라운드 종료 판정 = {_driver.Result} — 전 피어 전파 " +
                          $"(밸브 {opened}/{total}, 탈출 {_driver.EscapedCount}, 전원태그={allTagged}, " +
                          $"남은 {_driver.RemainingSeconds:0.0}초)");
            }
        }

        private bool CurrentGateOpen()
        {
            IEscapeGateState gate = EscapeGateRegistry.Current;
            return gate != null && gate.IsGateOpen;
        }

        // ── 서버: 역할 배정 (스프린트 13, §6.2/§14.3 RoleAssigned) ────────

        /// <summary>
        /// 미배정 플레이어가 있으면 접속 인원 전체에 §6.2 배정을 (재)수행한다. 서버 전용.
        ///
        /// **왜 "미배정이 있을 때만"인가**: MVP에는 로비 준비완료 개념이 없어 "라운드 시작"의
        /// 명확한 트리거가 없다(GAP-23). 그래서 플레이어가 스폰되어 인원이 확정되는 대로 배정하고,
        /// 이후 새 플레이어가 들어오면 그 사람만 미배정이므로 다시 이 경로를 탄다.
        ///
        /// **재배정이 기존 술래를 바꾸지 않는 이유**: 정렬 키가 OwnerId 오름차순이고(GAP-22)
        /// FishNet의 OwnerId는 접속마다 증가하므로, 나중에 들어온 사람은 항상 뒤쪽 인덱스가 되어
        /// 러너로 배정된다. <see cref="RoleNetworkSync.ServerAssign"/>도 같은 값이면 SyncVar를
        /// 건드리지 않아 멱등하다.
        ///
        /// 이미 태그돼 메아리가 된 플레이어는 배정 대상에서 제외한다 — 태그 전환(스프린트 11)
        /// 결과를 초기 배정이 되돌리지 않게 한다(인원 수 계산에는 포함: 라운드에 참가한 사람이므로).
        /// </summary>
        private void EnsureRolesAssigned()
        {
            _assignBuffer.Clear();
            List<RoleNetworkSync> spawned = RoleNetworkSync.Spawned;
            for (int i = 0; i < spawned.Count; i++)
            {
                RoleNetworkSync p = spawned[i];
                if (p != null && p.OrderKey >= 0) // 소유권이 확정된 플레이어만
                    _assignBuffer.Add(p);
            }

            if (!RoleAssigner.CanAssign(_assignBuffer.Count))
                return; // §1 최소 2인 미만 — 배정하지 않고 로컬 기본값(러너)을 유지한다(GAP-21).

            bool anyUnassigned = false;
            for (int i = 0; i < _assignBuffer.Count; i++)
            {
                if (!_assignBuffer[i].HasAssignment)
                {
                    anyUnassigned = true;
                    break;
                }
            }

            if (!anyUnassigned)
                return; // 전원 배정 완료 — 매 프레임 정렬 비용을 피한다.

            _assignBuffer.Sort(CompareByOrderKey);

            int playerCount = _assignBuffer.Count;
            for (int i = 0; i < playerCount; i++)
            {
                RoleNetworkSync p = _assignBuffer[i];
                if (p.IsTaggedOut)
                    continue; // 메아리는 배정 대상 제외(태그 결과 보존)

                p.ServerAssign(RoleAssigner.RoleForOrder(i, playerCount));
            }

            Debug.Log($"[RoleNet:Server] 역할 배정 완료 — 인원 {playerCount}명 " +
                      $"(술래 {RoleAssigner.SeekersFor(playerCount)} / 러너 {RoleAssigner.RunnersFor(playerCount)}, §6.2 표)");
        }

        private static int CompareByOrderKey(RoleNetworkSync a, RoleNetworkSync b) =>
            a.OrderKey.CompareTo(b.OrderKey);

        // ── 전 피어: 확정 결과 수신 로깅(양쪽 창에서 반영 확인용) ───────────

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            _result.OnChange += OnResultChanged;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            _result.OnChange -= OnResultChanged;
        }

        private void OnResultChanged(RoundResult prev, RoundResult next, bool asServer)
        {
            // 서버 측 로그는 EvaluateAndPush가 담당하므로, 여기서는 클라이언트 수신만 찍는다.
            if (asServer)
                return;

            if (next != RoundResult.InProgress)
                Debug.Log($"[RoundNet:Client] 라운드 결과 수신 = {next} — 서버 확정, 화면 반영");
        }
    }
}
