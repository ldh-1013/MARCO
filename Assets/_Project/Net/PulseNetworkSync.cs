using System.Collections.Generic;
using System.Text;
using FishNet.Connection;
using FishNet.Object;
using Marco.Core.Net;
using Marco.Core.Breath;
using Marco.Core.Role;
using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Net
{
    /// <summary>
    /// 파문(발소리·밸브 소음)을 서버 권위로 판정해 **청취자별로 개별 전송**한다
    /// (스프린트 14, §14.3 `SoundPulse` + `PerceivedPulse`).
    ///
    /// **왜 지금까지의 패턴과 다른가**: 밸브·태그·라운드·역할은 "상태 하나를 전원에게 똑같이"
    /// 전파하는 문제라 <c>SyncVar</c> 하나로 충분했다. 파문은 GAP-2(벽 0개면 좌표, 아니면 방위만)와
    /// GAP-3(청취자별 재판정) 때문에 <b>같은 소리라도 청취자마다 다른 내용</b>이 나가야 한다.
    /// 그래서 SyncVar/ObserversRpc(전체 브로드캐스트)가 아니라 <see cref="TargetRpcAttribute"/>로
    /// 청취자 한 명에게만 보낸다 — §14.3의 "Server → 해당 리스너만"이 이 구조를 명시한다.
    ///
    /// **흐름**:
    /// <code>
    /// [클라] 발소리 발생 → SubmitPulse(type)        ← 종류만 보낸다(GAP-24)
    ///   → [서버] ServerSubmitPulse: 호출자에서 발생원 ID·서버 측 위치를 얻고,
    ///            §5.1 표에서 반경·지속을 재계산해 ServerPulseDriver에 등록
    ///   → [서버] Update: 접속자 전원의 (ID·위치·배정된 역할) 스냅샷으로 §5.6 재판정
    ///   → [서버] 델리버리마다 TargetPulseDelivery(해당 청취자의 커넥션에게만)
    ///   → [클라] 수신 → PulseDelivery 재구성 → T8 렌더러(IPulseDeliverySink)에 그대로 전달
    /// </code>
    ///
    /// **차폐로 걸러진 청취자는 아무것도 받지 않는다** — <c>SoundPulseResolver</c>가 null을
    /// 반환하면 델리버리 자체가 생기지 않으므로, §14.3 주석의 "차폐로 걸러진 리스너는 아예
    /// 수신하지 않음"이 그대로 성립한다(트래픽 분산 + 정보 누출 차단).
    ///
    /// **GAP-1(본인 제외)**: 발생원 자신은 자기 파문을 받지 않는다 —
    /// <c>SoundPulseResolver</c>가 listenerId == sourceId면 null을 반환한다(기존 결정 재사용).
    ///
    /// **로컬 폴백 + NRE 가드**: 네트워크 미시작 시 스폰되지 않아 <see cref="NetworkActive"/>가
    /// false이고, Presentation은 스프린트 3~9의 로컬 스모크 리그를 그대로 쓴다. FishNet의
    /// <c>IsSpawned</c>/<c>IsServerStarted</c>는 초기화 전 내부 캐시가 null이라 직접 호출하면
    /// NRE가 나므로(스프린트 10 교훈), <see cref="NetworkBehaviour.NetworkObject"/> null 여부를
    /// 먼저 확인한다.
    /// </summary>
    public sealed class PulseNetworkSync : NetworkBehaviour, IPulseNetworkBridge
    {
        [Tooltip("서버 판정·전송 로그를 남길지. 초당 여러 번 도는 경로라 기본은 꺼둔다(델리버리 전량 로그).")]
        [SerializeField] private bool _logServerDeliveries;

        [Header("진단 (스프린트 14 실기 디버그 — 확인 끝나면 꺼도 됨)")]
        [Tooltip("스폰·ServerRpc 첫 수신·TargetRpc 첫 수신을 각각 1회만 찍고, 2초 주기로 " +
                 "청취자 수·누계 카운터를 요약한다. 어느 단계에서 끊기는지 짚는 용도.")]
        [SerializeField] private bool _logDiagnostics = true;

        private ServerPulseDriver _driver;      // 서버에서만 생성된다.

        /// <summary>
        /// §8.3 감시 종료 조건 "술래가 도망자를 태그"를 서버에서 관측하기 위한 구독.
        /// <c>TagTargetRegistry.TargetTagged</c>는 서버가 태그를 확정할 때 발화한다.
        /// </summary>
        private bool _subscribedToTags;
        private ServerKnockDriver _knockDriver; // §3.2 메아리 노크(스프린트 27). 서버 전용.
        private ServerShoutDriver _shoutDriver; // §3.5 술래 외침(5단계). 서버 전용.

        /// <summary>
        /// §5.9-1 플레이어별 숨 게이지. **게이지는 하나뿐**이라는 규칙을 서버가 소유한다 —
        /// 잠수 소모도 비명 억제 비용도 여기서 빠진다. 클라이언트 표시는 파생값이다.
        /// </summary>
        private readonly Dictionary<ulong, BreathGauge> _breath = new Dictionary<ulong, BreathGauge>();

        /// <summary>
        /// §3.5 "선딜레이 중 이동하면 취소"를 판정하기 위한 직전 프레임 위치(서버 관측값).
        /// 선딜레이 중인 술래에 대해서만 채워진다.
        /// </summary>
        private readonly Dictionary<ulong, Vector3> _shoutAnchor = new Dictionary<ulong, Vector3>();

        /// <summary>
        /// §3.5 선딜레이 취소 판정의 이동 허용치(m). NetworkTransform의 미세 떨림으로
        /// 외침이 취소되면 능력 자체를 쓸 수 없게 되므로 완전 0으로 두지 않는다.
        /// </summary>
        private const float ShoutMoveToleranceMeters = 0.15f;

        private readonly List<ShoutTarget> _shoutTargets = new List<ShoutTarget>();
        private readonly List<ulong> _movedSeekers = new List<ulong>();

        /// <summary>
        /// 노크 상태를 라운드 경계에서 비우기 위해 직전 틱의 페이즈를 기억한다(서버 전용).
        ///
        /// §3.2의 "라운드당 5회"는 라운드 경계에서만 회복되므로 <see cref="ServerKnockDriver.Reset"/>을
        /// 부를 지점이 반드시 필요하다. <c>RoundNetworkSync</c>에서 이쪽을 호출하게 만들지 않고
        /// **여기서 페이즈 전이를 관측**하는 쪽을 골랐다 — 이 컴포넌트는 이미 매 틱
        /// <c>RoundNetworkSync.ServerPhase</c>를 읽고 있어(노크 페이즈 게이트) 새 결합이 생기지 않는다.
        /// </summary>
        private Core.GameFlow.GameFlowState _lastKnockPhase = Core.GameFlow.GameFlowState.Boot;

        // 청취자 스냅샷 재사용 버퍼(매 프레임 할당 방지 — T8 성능 조사의 무할당 원칙).
        private readonly List<ListenerSnapshot> _listeners = new List<ListenerSnapshot>();

        // ── 진단 카운터(관측 전용 — 판정 로직에 영향 없음) ────────────────
        private bool _loggedFirstServerRpc;
        private bool _loggedFirstTargetRpc;
        private int _serverRpcCount;     // 서버가 받은 소리 발생 요청 수
        private int _serverSentCount;    // 서버가 개별 전송한 델리버리 수
        private int _clientReceivedCount; // 이 클라이언트가 받은 델리버리 수
        private float _nextServerDiagTime;
        private float _nextJudgeDiagTime;
        private readonly StringBuilder _judgeLog = new StringBuilder();

        // ── IPulseNetworkBridge ──────────────────────────────────────────

        /// <summary>NetworkObject가 스폰된 뒤에만 true(연결 전 NRE 가드).</summary>
        public bool NetworkActive => NetworkObject != null && IsSpawned;

        public void SubmitPulse(SoundType type)
        {
            if (!NetworkActive)
                return;

            ServerSubmitPulse(type);
        }

        public void SubmitKnock()
        {
            if (!NetworkActive)
                return;

            ServerSubmitKnock();
        }

        public void SubmitShout()
        {
            if (!NetworkActive)
                return;

            ServerSubmitShout();
        }

        public void SubmitHoldBreath()
        {
            if (!NetworkActive)
                return;

            ServerSubmitHoldBreath();
        }

        // ── 서버: 수집 ────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();

            if (!_subscribedToTags)
            {
                Core.Net.TagTargetRegistry.TargetTagged += OnServerTargetTagged;
                _subscribedToTags = true;
            }

            _driver = new ServerPulseDriver();
            _knockDriver = new ServerKnockDriver();
            _shoutDriver = new ServerShoutDriver();
            _breath.Clear();
            _shoutAnchor.Clear();
            Debug.Log("[PulseNet:Server] 서버 권위 파문 판정 시작 — 청취자별 개별 전송(§14.3)");
        }

        /// <summary>
        /// §3.2 노크 요청(§14.3 `EchoKnock`). 씬 오브젝트라 소유권 검사를 끄고, **역할·플레이어 ID·
        /// 위치를 전부 호출자에서 서버가 읽는다**(<see cref="RoleNetworkSync.TryGetCallerIdentity"/>
        /// + <c>caller.FirstObject</c>, GAP-24).
        ///
        /// <b>페이로드가 비어 있다</b>(기획서 갱신). §3.2의 발생 위치가 "메아리의 현재 위치"가 되면서
        /// 노크만 예외적으로 지점을 받던 이유가 사라졌고, 이제 <see cref="ServerSubmitPulse"/>와
        /// 완전히 같은 규칙이다 — 클라이언트가 정할 수 있는 것은 "지금 쓴다"뿐이다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitKnock(NetworkConnection caller = null)
        {
            if (_knockDriver == null)
                return;

            // 스프린트 18의 페이즈 게이트와 같은 규칙 — 노크는 라운드 중에만 성립한다.
            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.InGame)
                return;

            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
            {
                Debug.LogWarning("[KnockNet:Server] 노크 폐기 — 호출자의 플레이어 오브젝트/역할을 찾을 수 없습니다.");
                return;
            }

            // TryGetCallerIdentity가 caller·FirstObject를 이미 검증했다(§3.2 발생 위치의 근거).
            Vector3 echoPosition = caller.FirstObject.transform.position;

            KnockRequestResult result = _knockDriver.TryRequest(playerId, role, echoPosition, Time.time);
            if (result != KnockRequestResult.Accepted)
            {
                Debug.Log($"[KnockNet:Server] 노크 거부 — playerId={playerId} 사유={result} " +
                          $"(§3.2 메아리 전용·라운드 {KnockConfig.MaxUsesPerRound}회·" +
                          $"전환 후 {KnockConfig.EchoLockoutSeconds:0}초 잠금·쿨다운 {KnockConfig.CooldownSeconds:0}초)");
                return;
            }

            Debug.Log($"[KnockNet:Server] 노크 접수 — playerId={playerId} 지점={echoPosition.ToString("0.0")}(메아리 현재 위치), " +
                      $"{KnockConfig.ActivationDelaySeconds:0.#}초 뒤 발생. " +
                      $"남은 횟수={_knockDriver.UsesRemaining(playerId)}/{KnockConfig.MaxUsesPerRound} (§3.2)");
        }

        /// <summary>
        /// §3.5 외침 요청. 씬 오브젝트라 소유권 검사를 끄고, **역할·플레이어 ID·위치를 전부
        /// 호출자에서 서버가 읽는다**(GAP-24). 페이로드가 비어 있다 — 클라이언트가 정할 수 있는
        /// 것은 "지금 쓴다"뿐이다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitShout(NetworkConnection caller = null)
        {
            if (_shoutDriver == null)
                return;

            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.InGame)
                return;

            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType role, out ulong playerId))
            {
                Debug.LogWarning("[ShoutNet:Server] 외침 폐기 — 호출자의 플레이어 오브젝트/역할을 찾을 수 없습니다.");
                return;
            }

            Vector3 origin = caller.FirstObject.transform.position;
            ShoutRequestResult result = _shoutDriver.TryRequest(playerId, role, origin, Time.time);
            if (result != ShoutRequestResult.Accepted)
            {
                Debug.Log($"[ShoutNet:Server] 외침 거부 — playerId={playerId} 사유={result} " +
                          $"(§3.5 술래 전용·쿨다운 {SeekerShoutConfig.CooldownSeconds:0}초)");
                return;
            }

            // §3.5 "선딜레이 중 이동하면 취소" 판정의 기준점.
            _shoutAnchor[playerId] = origin;

            Debug.Log($"[ShoutNet:Server] 외침 접수 — playerId={playerId}, " +
                      $"{SeekerShoutConfig.WindupSeconds:0.#}초 정지 선딜레이 시작(§3.5). 이동하면 취소된다.");
        }

        /// <summary>
        /// §3.5 "숨 참기(선딜레이 1초 안에 입력)". 의사표시만 기록하며 **게이지는 여기서 깎지 않는다** —
        /// 실제 -3은 외침이 발동해 공포 반경 안에 있다고 판정될 때 일어난다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitHoldBreath(NetworkConnection caller = null)
        {
            if (_shoutDriver == null)
                return;

            if (!RoleNetworkSync.TryGetCallerIdentity(caller, out RoleType _, out ulong playerId))
                return;

            _shoutDriver.NotifySuppressAttempt(playerId, Time.time);
        }

        /// <summary>
        /// [진단 ③] 이 컴포넌트가 실제로 네트워크 스폰됐는지. 이 로그가 없으면 씬 오브젝트에
        /// 컴포넌트가 붙지 않았거나(씬 미저장) NetworkObject가 스폰되지 않은 것이므로,
        /// Presentation의 <c>IsNetworkActive</c>도 영구히 false가 된다.
        /// </summary>
        public override void OnStartNetwork()
        {
            base.OnStartNetwork();

            if (_logDiagnostics)
                Debug.Log($"[PulseNet:Diag] 스폰 완료 — objectId={(NetworkObject != null ? NetworkObject.ObjectId : -1)}, " +
                          $"서버컨텍스트={IsServerInitialized}, 클라컨텍스트={IsClientInitialized}. " +
                          "이제 발소리가 서버 권위 경로로 전환돼야 한다([Pulse:Diag] 경로 로그 확인).");
        }

        /// <summary>
        /// 파문은 특정 플레이어가 소유하지 않는 씬 오브젝트에서 처리하므로 소유권 검사를 끈다.
        /// <paramref name="caller"/>는 FishNet이 주입하는 호출자 — **발생원 신원과 위치를 여기서
        /// 얻는다**(클라 주장값을 쓰지 않는다, GAP-24). 그래서 클라이언트는 남의 발소리를
        /// 위장하거나 엉뚱한 위치에서 소리를 낼 수 없다.
        /// </summary>
        [ServerRpc(RequireOwnership = false)]
        private void ServerSubmitPulse(SoundType type, NetworkConnection caller = null)
        {
            if (_driver == null)
                return;

            _serverRpcCount++;

            // [진단 ④] ServerRpc가 서버에 실제로 도달했는가 — 첫 수신만 1회 찍는다.
            // 이 로그가 없으면 클라이언트가 아직 로컬 경로로 동작 중이거나(브릿지 미부착),
            // RPC 위빙이 안 된 것이다.
            if (_logDiagnostics && !_loggedFirstServerRpc)
            {
                _loggedFirstServerRpc = true;
                Debug.Log($"[PulseNet:Server] ★ 첫 ServerRpc 수신 — type={type}, " +
                          $"callerId={(caller != null ? caller.ClientId : -1)}. 클라이언트→서버 경로 정상.");
            }

            if (caller == null || caller.FirstObject == null)
            {
                // 발생원 오브젝트를 못 찾으면 위치를 신뢰할 수 없다 — 폐기.
                // (진단: 이 경고가 계속 나오면 PlayerSpawner가 FirstObject를 세팅하지 못한 것)
                if (_logDiagnostics)
                    Debug.LogWarning("[PulseNet:Server] 파문 폐기 — 호출자의 플레이어 오브젝트(FirstObject)를 찾을 수 없어 " +
                                     "서버 측 발생 위치를 확정할 수 없습니다.");
                return;
            }

            ulong sourceId = (ulong)caller.ClientId;
            Vector3 serverPosition = caller.FirstObject.transform.position;

            // §5.9 재질 배율(4단계). **서버가 서버 측 위치에서 바닥을 조회한다** — 클라이언트가
            // "나는 카펫 위다"라고 주장할 수 없다(GAP-24와 같은 원칙). 발소리가 아닌 종류는
            // 항상 기본값이 나오므로 여기서 분기하지 않는다.
            FootstepMaterial material = PulseNetworkRegistry.SampleMaterial(type, serverPosition);

            int pulseId = _driver.AddPulse(sourceId, type, serverPosition, Time.time, material);
            if (pulseId < 0)
            {
                // §5.1 표에 없는 종류이거나, §5.9 물(수면 아래)이라 파문이 발생하지 않는다.
                if (_logDiagnostics)
                    Debug.LogWarning($"[PulseNet:Server] 파문 거부 — type={type} 재질={material} " +
                                     "(§5.1 표에 없는 종류이거나 §5.9 '물 = 파문 발생 안 함').");
                return;
            }

            // §8 어워드 집계: 서버가 실제로 등록한 파문만 센다 — 클라이언트 보고를 믿지 않는다.
            // §8.2가 "발생 반경은 **재질 배율 적용 후**의 값"이라고 못박았으므로,
            // AddPulse가 쓴 것과 **같은 계산**(TryGetAppliedSpec)의 결과를 넘긴다.
            ServerPulseDriver.TryGetAppliedSpec(type, material, out float appliedRadius, out float appliedDuration);
            RoundNetworkSync.ServerRecordPulse(caller.ClientId, type, appliedRadius, appliedDuration);

            if (_logServerDeliveries)
                Debug.Log($"[PulseNet:Server] 파문 등록 pulse={pulseId} type={type} sourceId={sourceId} " +
                          $"재질={material}(×{FootstepMaterialRules.RadiusMultiplier(material):0.##}) (서버 위치 기준)");

            LogJudgmentDiagnostics(type, sourceId, serverPosition);
        }

        /// <summary>
        /// [진단 ⑦] 파문 등록 직후, 각 청취자에 대해 §5.6 판정에 쓰이는 **실측값**을 한 줄로 남긴다.
        /// 1초에 1회로 스로틀한다(초당 2~4개 발생하는 경로라 전량 로그는 무의미하게 시끄럽다).
        ///
        /// 형식: <c>거리=3.42m 역할배율후반경=6.00 벽=2 감쇠=0.25 인지반경=1.50 → 탈락(거리>인지반경)</c>
        ///
        /// **주의(관측 전용)**: 이 계산은 <see cref="SoundPulseResolver"/>의 판정식을 로그 목적으로
        /// **거울처럼 재현**한 것이다(Core의 공개 API를 그대로 호출한다). 실제 판정은 여전히 Core가
        /// 수행하며 이 코드는 아무 상태도 바꾸지 않는다. 만약 이 로그와 실제 전송 여부가 어긋나면
        /// Core가 아니라 **이 거울 코드**를 먼저 의심해야 한다.
        /// </summary>
        private void LogJudgmentDiagnostics(SoundType type, ulong sourceId, Vector3 sourcePos)
        {
            if (!_logDiagnostics || Time.time < _nextJudgeDiagTime)
                return;

            _nextJudgeDiagTime = Time.time + 1f;

            if (!ServerPulseDriver.TryGetPulseSpec(type, out float radius, out float duration))
                return;

            IOcclusionProbe probe = PulseNetworkRegistry.OcclusionProbe;
            BuildListenerSnapshots();

            _judgeLog.Clear();
            _judgeLog.Append($"[PulseNet:Judge] type={type} 원본반경={radius:0.##}m 지속={duration:0.##}s source={sourceId} " +
                             $"위치={sourcePos.ToString("0.0")} | 청취자 {_listeners.Count}명");

            for (int i = 0; i < _listeners.Count; i++)
            {
                ListenerSnapshot listener = _listeners[i];
                _judgeLog.AppendLine();
                _judgeLog.Append($"    listener={listener.PlayerId}({listener.Role}) ");

                if (listener.PlayerId == sourceId)
                {
                    _judgeLog.Append("→ 본인이라 제외(GAP-1)");
                    continue;
                }

                float dist = Vector3.Distance(sourcePos, listener.Position);
                float baseRadius = radius * SoundPulseResolver.RoleRadiusMultiplier(listener.Role);
                _judgeLog.Append($"거리={dist:0.00}m 역할배율후반경={baseRadius:0.00}m ");

                if (dist > baseRadius)
                {
                    _judgeLog.Append("→ 탈락(1차 컷: 거리>반경)");
                    continue;
                }

                if (probe == null)
                {
                    _judgeLog.Append("→ 판정 불가(차폐 프로브 미등록)");
                    continue;
                }

                OcclusionResult occ = probe.Probe(sourcePos, listener.Position);
                if (occ.HasHardBlocker)
                {
                    _judgeLog.Append("→ 탈락(하드블로커)");
                    continue;
                }

                float attenuation = Mathf.Pow(0.5f, occ.WallCount);
                float perceivedRadius = baseRadius * attenuation;
                _judgeLog.Append($"벽={occ.WallCount} 감쇠={attenuation:0.###} 인지반경={perceivedRadius:0.00}m ");

                _judgeLog.Append(dist > perceivedRadius
                    ? "→ 탈락(차폐 감쇠 후 거리>인지반경)"
                    : $"→ **통과** 좌표공개={(occ.WallCount == 0 ? "예(월드링)" : "아니오(방위만)")}");
            }

            Debug.Log(_judgeLog.ToString());
        }

        // ── 서버: 청취자별 판정 + 개별 전송 ───────────────────────────────

        private void Update()
        {
            // 권위 판정은 서버에서만. IsServerStarted도 IsSpawned와 같은 이유로 NetworkObject
            // 캐시가 찰 때까지 직접 호출하면 안 된다(NetworkActive 문서 참고).
            if (_driver == null || !NetworkActive || !IsServerStarted)
                return;

            IOcclusionProbe probe = PulseNetworkRegistry.OcclusionProbe;

            BuildListenerSnapshots();

            // [진단 ⑤] 서버 판정의 입력 상태 요약(2초 주기). 여기서 청취자 수가 0이면
            // RoleNetworkSync가 Player 프리팹에 없거나 소유권이 확정되지 않은 것이고,
            // 차폐프로브가 없으면 Presentation 등록이 안 된 것이다 — 둘 다 전달이 0이 되는 원인.
            if (_logDiagnostics && Time.time >= _nextServerDiagTime)
            {
                _nextServerDiagTime = Time.time + 2f;
                Debug.Log($"[PulseNet:Diag] 서버 상태 — 청취자={_listeners.Count}명, 추적 파문={_driver.ActivePulseCount}개, " +
                          $"차폐프로브={(probe != null ? "등록됨" : "없음(판정 중단)")}, " +
                          $"누계: 수신 RPC={_serverRpcCount} / 개별 전송={_serverSentCount}");
            }

            // §5.9-1 숨 게이지: 소모·회복은 서버가 소유한다(§3.5 억제 비용이 여기서 빠진다).
            TickBreath(Time.deltaTime);

            // §3.2 노크: 지연이 끝난 것을 파문화하고, 술래 위치로 §8 유인을 판정한다.
            // 청취자 스냅샷을 이미 만들어 둔 이 자리가 술래 위치를 얻기 가장 싼 지점이다.
            TickKnocks();

            // §3.5 외침: 선딜레이 취소 판정 → 발동 → 공포 반경 판정 → 비명 파문.
            TickShouts();

            if (probe == null)
                return; // 차폐 판정기가 없으면 판정하지 않는다(좌표를 흘리지 않기 위함).

            if (_listeners.Count == 0)
                return;

            List<PulseDelivery> deliveries = _driver.Tick(Time.time, _listeners, probe);
            for (int i = 0; i < deliveries.Count; i++)
                SendDelivery(deliveries[i]);
        }

        /// <summary>
        /// §3.2 노크의 서버 측 진행(스프린트 27). 서버 전용, 매 프레임.
        ///
        /// ① 1.5초 지연이 끝난 노크를 <b>기존 파문 경로에 그대로 태운다</b> —
        /// <see cref="ServerPulseDriver.AddPulse"/>가 §5.1 표에서 반경·지속을 재계산하고,
        /// 이후 차폐·청취자별 전달·시각화는 발소리·음성과 **완전히 같은 코드**를 탄다.
        /// ② 술래 위치로 §8 "노크 성공 유인"을 판정해 어워드에 넘긴다.
        /// </summary>
        private void TickKnocks()
        {
            if (_knockDriver == null)
                return;

            float now = Time.time;

            // 라운드 시작 전이(→ InGame)에서 노크 상태를 전부 비운다 — §3.2 "라운드당 정확히
            // 5회(재충전 없음)"는 **라운드 경계에서만** 회복되고, 전환 후 20초 잠금도 새 라운드에서
            // 다시 세야 한다. 이 전이를 놓치면 5회 카운터가 세션 내내 누적돼 2라운드부터 노크가
            // 아예 불가능해진다.
            Core.GameFlow.GameFlowState phase = RoundNetworkSync.ServerPhase;
            if (phase != _lastKnockPhase)
            {
                if (phase == Core.GameFlow.GameFlowState.InGame)
                {
                    _knockDriver.Reset();
                    _shoutDriver?.Reset();      // §3.5 쿨다운·선딜레이도 라운드 경계에서 비운다.
                    _shoutAnchor.Clear();
                    foreach (BreathGauge gauge in _breath.Values)
                        gauge.Reset();          // §5.9-1 새 라운드는 만충으로 시작한다.
                    Debug.Log($"[KnockNet:Server] 라운드 시작 — 노크 상태 초기화(§3.2 라운드당 {KnockConfig.MaxUsesPerRound}회 재충전).");
                }

                _lastKnockPhase = phase;
            }

            // §3.2 "메아리 전환 후 20초간 사용 불가"의 기준점. 태그 직후부터 세야 하므로
            // 요청을 기다리지 않고 매 틱 관측한다(ObserveEcho는 최초 1회만 기록한다).
            ObserveEchoes(now);

            List<KnockActivation> activations = _knockDriver.Tick(now);
            for (int i = 0; i < activations.Count; i++)
            {
                KnockActivation knock = activations[i];

                // 발생원 ID는 노크를 지정한 메아리다 — 그래야 GAP-1(본인 제외)이
                // "자기 노크는 자기에게 안 보인다"로 자연스럽게 성립한다.
                int pulseId = _driver.AddPulse(knock.PlayerId, SoundType.Knock, knock.Point, now);
                if (pulseId < 0)
                    continue;

                // 서버가 실제로 등록한 파문만 §8에 센다(발소리·음성과 같은 규칙).
                // 노크는 재질 배율 대상이 아니라 §5.1 표 값이 그대로 들어간다.
                RoundNetworkSync.ServerRecordPulse((int)knock.PlayerId, SoundType.Knock,
                    KnockConfig.RadiusMeters, KnockConfig.DurationSeconds);

                Debug.Log($"[KnockNet:Server] 노크 발생 — pulse={pulseId} playerId={knock.PlayerId} " +
                          $"지점={knock.Point.ToString("0.0")} 반경={KnockConfig.RadiusMeters:0.#}m (§3.2)");
            }

            // 유인 판정에는 술래 위치가 필요하다. MVP는 술래 1인(§6.2)이라 첫 술래만 본다.
            if (!TryGetSeekerPosition(out Vector3 seekerPosition))
                return;

            List<ulong> lures = _knockDriver.ResolveLures(seekerPosition, now);
            for (int i = 0; i < lures.Count; i++)
            {
                RoundNetworkSync.ServerRecordKnockLure((int)lures[i]);
                Debug.Log($"[KnockNet:Server] ★ 유인 성공 — playerId={lures[i]}의 노크 지점으로 술래가 진입했다 " +
                          $"(§8 최고의 거짓말상 +1, GAP-59 기준: 발생 시 {ServerKnockDriver.LureRadiusMeters:0.#}m 밖 → " +
                          $"{ServerKnockDriver.LureWindowSeconds:0}초 내 진입)");
            }
        }

        /// <summary>
        /// 현재 메아리인 플레이어 전원을 <see cref="ServerKnockDriver.ObserveEcho"/>에 알린다.
        ///
        /// 청취자 스냅샷의 <c>Role</c>이 아니라 <see cref="RoleNetworkSync.EffectiveRole"/>을 쓴다 —
        /// 태그 아웃 직후에는 역할 SyncVar가 아직 Runner일 수 있고, 잠금은 **태그당한 순간**부터
        /// 세야 하기 때문이다(<c>TryGetCallerIdentity</c>가 쓰는 판정과 같은 기준).
        /// </summary>
        private void ObserveEchoes(float now)
        {
            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0)
                    continue;

                if (p.EffectiveRole == RoleType.Echo)
                    _knockDriver.ObserveEcho((ulong)p.OrderKey, now);
            }
        }

        /// <summary>
        /// §8.3 감시 종료 조건 "**술래가 도망자를 태그**" — 그 순간 활성 유인 감시를 전부 버린다.
        /// 태그 직후의 이동을 유인으로 세면 "잡고 나서 우연히 노크 지점을 지났다"가 거짓말상이 된다.
        /// </summary>
        private void OnServerTargetTagged(Core.Net.ITagTarget target)
        {
            _knockDriver?.NotifySeekerTagged();
        }

        /// <summary>
        /// §5.9-1 이 플레이어의 숨 상태.
        ///
        /// **현재는 항상 <see cref="BreathZone.OutOfWater"/>다** — 물 볼륨(수면 존) 감지가 아직
        /// 없어서 서버가 잠수/수면을 관측할 방법이 없기 때문이다. <c>FirstPersonController</c>도
        /// 같은 이유로 <c>isOnWaterSurface: false</c>를 하드코딩하고 있다(§5.9 후속 태스크).
        /// 물 볼륨이 들어오면 <b>이 메서드 한 곳만</b> 고치면 된다 —
        /// 규칙 자체는 <see cref="BreathConfig.ZoneOf"/>가 이미 전부 들고 있다.
        /// </summary>
        private static BreathZone ZoneOf(RoleNetworkSync player)
        {
            return BreathZone.OutOfWater;
        }

        /// <summary>이 플레이어의 숨 게이지(없으면 만충으로 새로 만든다). 서버 전용.</summary>
        private BreathGauge BreathOf(ulong playerId)
        {
            if (!_breath.TryGetValue(playerId, out BreathGauge gauge))
            {
                gauge = new BreathGauge();
                _breath[playerId] = gauge;
            }

            return gauge;
        }

        /// <summary>
        /// §5.9-1 숨 게이지를 전원에 대해 진전시킨다. 서버 전용, 매 프레임.
        ///
        /// 질식(게이지 0)이 발생하면 §5.9-1대로 **고함급(22m) 파문 1회**를 강제 발생시킨다.
        /// 강제 부상과 이동속도 -20%는 게이지 자신이 들고 있고(<c>CanSubmerge</c>·<c>SpeedMultiplier</c>),
        /// 이동 시뮬레이터가 그것을 읽는다.
        /// </summary>
        private void TickBreath(float deltaSeconds)
        {
            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0)
                    continue;

                ulong id = (ulong)p.OrderKey;
                BreathTick tick = BreathOf(id).Tick(ZoneOf(p), deltaSeconds);
                if (!tick.Choked)
                    continue;

                // §5.9-1 "고함급(발생 반경 22m) 파문 1회 강제 발생"(기침·헐떡임 연출).
                _driver.AddPulse(id, SoundType.Shout, p.transform.position, Time.time);
                RoundNetworkSync.ServerRecordPulse((int)id, SoundType.Shout,
                    Core.Voice.VoiceConfig.ShoutRadiusMeters, Core.Voice.VoiceConfig.ShoutDurationSeconds);
                Debug.Log($"[Breath:Server] 질식 — playerId={id} 강제 부상 + 이동속도 " +
                          $"{(1f - BreathConfig.ChokeSpeedMultiplier) * 100f:0}% 감소 " +
                          $"{BreathConfig.ChokePenaltySeconds:0}초 + 고함급 파문(§5.9-1)");
            }
        }

        /// <summary>
        /// §3.5 외침의 서버 측 진행(5단계). 서버 전용, 매 프레임.
        ///
        /// ① 선딜레이 중 이동했으면 취소한다(**쿨다운 소모 없음**).
        /// ② 선딜레이가 끝난 외침을 발동시켜 <b>A — 고함 등급 파문(22m)</b>을 등록한다.
        /// ③ <b>B — 공포 반경(22m, 차폐 미적용)</b>을 판정해, 억제하지 못한 러너마다
        ///    <b>비명 파문(9m/1.0초)</b>을 등록한다.
        ///
        /// A·B·C 세 22m를 섞지 않는다는 §3.5 규칙이 여기서 코드로 드러난다 —
        /// ②는 파문 경로(차폐 있음), ③은 능력 판정(차폐 없음), C는 §5.7 역할 배율의 결과다.
        /// </summary>
        private void TickShouts()
        {
            if (_shoutDriver == null)
                return;

            float now = Time.time;

            CancelMovedShouts();

            List<ShoutActivation> activations = _shoutDriver.Tick(now);
            if (activations.Count == 0)
                return;

            // Tick의 반환은 내부 재사용 버퍼다 — ResolveFear를 부르기 전에 복사해 둔다.
            var fired = new List<ShoutActivation>(activations);

            for (int i = 0; i < fired.Count; i++)
            {
                ShoutActivation shout = fired[i];
                _shoutAnchor.Remove(shout.SeekerId);

                // ① A — 외침 자체의 소리(고함 등급 22m). 차폐가 적용되는 일반 파문이다.
                _driver.AddPulse(shout.SeekerId, SoundType.Shout, shout.Origin, now);

                // §8.2 소음량에는 들어간다(밸브만 제외 대상이다). §8.1 최다 비명상에는
                // 영향이 없다 — 그쪽은 Scream만 세고 Shout는 일절 반영하지 않는다.
                RoundNetworkSync.ServerRecordPulse((int)shout.SeekerId, SoundType.Shout,
                    SeekerShoutConfig.ShoutPulseRadiusMeters, SeekerShoutConfig.ShoutPulseDurationSeconds);

                // ② B — 공포 반경. 차폐를 보지 않는 순수 직선거리 판정이다.
                BuildShoutTargets();
                List<ScreamReaction> reactions = _shoutDriver.ResolveFear(shout.Origin, _shoutTargets, now);

                int screamed = 0;
                for (int r = 0; r < reactions.Count; r++)
                {
                    ScreamReaction reaction = reactions[r];
                    if (!reaction.EmitsScream)
                        continue;

                    // ③ 비명 파문(9m/1.0초). 발생원은 **비명을 지른 러너**라
                    // GAP-1(본인 제외)이 "자기 비명은 자기 화면에 안 뜬다"로 성립한다.
                    _driver.AddPulse(reaction.PlayerId, SoundType.Scream, reaction.Position, now);
                    screamed++;

                    // §8.1 최다 비명상(6단계에서 개통). **청취 여부와 무관하게** 이벤트가
                    // 발생했으므로 센다 — 술래가 10.8m 밖이라 못 들었어도 집계 대상이다.
                    // 억제에 성공한 러너는 여기 도달하지 않으므로 자동으로 빠진다.
                    RoundNetworkSync.ServerRecordPulse((int)reaction.PlayerId, SoundType.Scream,
                        ScreamConfig.RadiusMeters, ScreamConfig.DurationSeconds);
                }

                Debug.Log($"[ShoutNet:Server] 외침 발동 — seeker={shout.SeekerId} " +
                          $"지점={shout.Origin.ToString("0.0")} | 공포 반경 {SeekerShoutConfig.FearRadiusMeters:0}m 안 " +
                          $"{reactions.Count}명 중 비명 {screamed}명 (§3.5). 쿨다운 {SeekerShoutConfig.CooldownSeconds:0}초 시작");
            }
        }

        /// <summary>§3.5 "선딜레이 중 이동하면 취소, 쿨다운 소모 없음".</summary>
        private void CancelMovedShouts()
        {
            if (_shoutAnchor.Count == 0)
                return;

            _movedSeekers.Clear();

            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0)
                    continue;

                ulong id = (ulong)p.OrderKey;
                if (!_shoutAnchor.TryGetValue(id, out Vector3 anchor))
                    continue;

                if (Vector3.Distance(anchor, p.transform.position) > ShoutMoveToleranceMeters)
                    _movedSeekers.Add(id);
            }

            for (int i = 0; i < _movedSeekers.Count; i++)
            {
                ulong id = _movedSeekers[i];
                _shoutAnchor.Remove(id);

                if (_shoutDriver.CancelOnMove(id))
                    Debug.Log($"[ShoutNet:Server] 외침 취소 — playerId={id}가 선딜레이 중 이동했다(§3.5, 쿨다운 소모 없음).");
            }
        }

        /// <summary>§3.5 공포 반경 판정에 넘길 대상 목록을 서버 값으로 구성한다.</summary>
        private void BuildShoutTargets()
        {
            _shoutTargets.Clear();

            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0)
                    continue;

                ulong id = (ulong)p.OrderKey;
                _shoutTargets.Add(new ShoutTarget(id, p.transform.position, p.EffectiveRole, ZoneOf(p), BreathOf(id)));
            }
        }

        /// <summary>이미 만들어 둔 청취자 스냅샷에서 술래 위치를 찾는다(§6.2 MVP는 술래 1인).</summary>
        private bool TryGetSeekerPosition(out Vector3 position)
        {
            for (int i = 0; i < _listeners.Count; i++)
            {
                if (_listeners[i].Role != RoleType.Seeker)
                    continue;

                position = _listeners[i].Position;
                return true;
            }

            position = default;
            return false;
        }

        /// <summary>
        /// 접속한 플레이어 전원의 청취자 스냅샷을 구성한다(§5.6 입력).
        ///
        /// 역할은 **스프린트 13에서 서버가 배정한 실제 역할**(<c>RoleNetworkSync.CurrentRole</c>)을
        /// 쓴다 — 그래서 §5.7 인지 배율(술래 반경 ×1.2·지속 ×1.5)이 로컬 하드코딩이 아니라
        /// 진짜 배정 결과로 계산된다. 위치는 서버가 아는 NetworkTransform 위치다(스프린트 8).
        ///
        /// 발생원 제외(GAP-1)는 여기서 하지 않는다 — <c>SoundPulseResolver</c>가 펄스별로
        /// listenerId == sourceId를 판정해 null을 반환하므로, 전원을 넣어도 자기 소리는 안 간다.
        /// </summary>
        private void BuildListenerSnapshots()
        {
            _listeners.Clear();

            List<RoleNetworkSync> players = RoleNetworkSync.Spawned;
            for (int i = 0; i < players.Count; i++)
            {
                RoleNetworkSync p = players[i];
                if (p == null || p.OrderKey < 0)
                    continue; // 소유권 미확정 — 아직 청취자로 셀 수 없다.

                _listeners.Add(new ListenerSnapshot((ulong)p.OrderKey, p.transform.position, p.CurrentRole));
            }
        }

        /// <summary>델리버리 하나를 해당 청취자에게만 보낸다(§14.3 "Server → 해당 리스너만").</summary>
        private void SendDelivery(in PulseDelivery delivery)
        {
            NetworkConnection target = FindConnection(delivery.ListenerId);
            if (target == null)
            {
                // 진단: 판정은 통과했는데 커넥션을 못 찾는 경우(청취자 ID ↔ ClientId 불일치 등).
                if (_logDiagnostics)
                    Debug.LogWarning($"[PulseNet:Server] 전송 실패 — listener={delivery.ListenerId}의 커넥션을 찾을 수 없음 " +
                                     "(청취자 ID는 OwnerId, 커넥션 키는 ClientId여야 일치).");
                return;
            }

            _serverSentCount++;

            if (delivery.Perceived.HasValue)
            {
                PerceivedPulse p = delivery.Perceived.Value;

                // GAP-2를 **와이어 레벨에서 강제**한다: 좌표를 공개하지 않는 판정이면
                // 위치 인자를 아예 보내지 않는다(hasSourcePos=false + Vector3.zero).
                // 구조체를 통째로 보내면 비공개 좌표가 페이로드에 실릴 수 있어, 필드를
                // 원시값으로 분해해 전송한다 — PerceivedPulse 문서의 의도(정밀 좌표를
                // "아예 보내지 않기 위함")를 전송 계층까지 관철하는 것이다.
                bool hasSourcePos = p.SourcePos.HasValue;
                Vector3 sourcePos = hasSourcePos ? p.SourcePos.Value : Vector3.zero;

                // [진단 ⑧] 판정에 쓰인 값과 실제로 전송하는 값이 같은지(전송 중 축소·손실 여부).
                // 첫 전송 1회만 찍는다 — 두 값이 같은 변수에서 나오므로 불일치는 구조적으로 불가하지만,
                // "반경이 작아 보이는" 증상의 원인이 전송 단계인지 판정 단계인지 못박기 위해 남긴다.
                if (_logDiagnostics && _serverSentCount == 1)
                    Debug.Log($"[PulseNet:Send] 첫 전송 값 확인 — pulse={delivery.PulseId} listener={delivery.ListenerId} " +
                              $"판정 인지반경={p.PerceivedRadius:0.00}m → 전송 반경={p.PerceivedRadius:0.00}m (동일 변수), " +
                              $"지속={p.PerceivedDuration:0.00}s, 좌표공개={hasSourcePos}, 방위={p.Direction}");

                // §3.4 게이지 판정도 **서버 결론**을 그대로 보낸다 — 술래 전용 정보라
                // 클라이언트가 자기 역할을 보고 스스로 켜게 두면 안 된다(GAP-4).
                TargetPulseDelivery(target, delivery.PulseId, (byte)delivery.Kind,
                    p.PerceivedRadius, p.PerceivedDuration, hasSourcePos, sourcePos,
                    (byte)p.Direction, p.WorldSpaceRingVisible, delivery.GaugeLit);
            }
            else
            {
                // Disappeared: 인지값이 없다(§5.6 소실). 좌표·반경은 보낼 것이 없다.
                TargetPulseDelivery(target, delivery.PulseId, (byte)delivery.Kind,
                    0f, 0f, false, Vector3.zero, 0, false, false);
            }

            if (_logServerDeliveries)
                Debug.Log($"[PulseNet:Server] {delivery.Kind} pulse={delivery.PulseId} → listener={delivery.ListenerId} 개별 전송");
        }

        private NetworkConnection FindConnection(ulong listenerId)
        {
            if (ServerManager == null)
                return null;

            return ServerManager.Clients.TryGetValue((int)listenerId, out NetworkConnection conn) ? conn : null;
        }

        // ── 청취자: 수신 → T8 렌더러 ──────────────────────────────────────

        /// <summary>
        /// 서버가 이 클라이언트에게만 보낸 §5.6 판정 결과. 필드를 원시값으로 받아
        /// <see cref="PerceivedPulse"/>를 재구성한 뒤 T8 렌더러에 그대로 넘긴다.
        ///
        /// 시각화 코드는 새로 만들지 않는다 — 로컬 판정 결과와 완전히 같은 경로
        /// (<see cref="IPulseDeliverySink.Apply"/> = 렌더러의 기존 <c>Apply</c>)로 들어간다.
        /// 렌더러가 <c>PerceivedDuration</c>으로 자체 만료 타이머를 돌리므로(스프린트 4 결론)
        /// 여기서 넘기는 시각은 **수신 측 로컬 <c>Time.time</c>** 이면 충분하다(시계 동기화 불필요).
        /// </summary>
        [TargetRpc]
        private void TargetPulseDelivery(NetworkConnection conn, int pulseId, byte kind,
            float perceivedRadius, float perceivedDuration, bool hasSourcePos, Vector3 sourcePos,
            byte direction, bool worldSpaceRingVisible, bool gaugeLit)
        {
            _clientReceivedCount++;

            // [진단 ⑥] TargetRpc가 이 클라이언트에 실제로 도달했는가 — 첫 수신만 1회 찍는다.
            // 여기까지 왔는데 화면에 아무것도 없으면 남은 원인은 렌더러(싱크 미등록/카메라·팔레트)다.
            if (_logDiagnostics && !_loggedFirstTargetRpc)
            {
                _loggedFirstTargetRpc = true;
                Debug.Log($"[PulseNet:Client] ★ 첫 TargetRpc 수신 — pulse={pulseId} kind={(PulseDeliveryKind)kind} " +
                          $"radius={perceivedRadius:0.##} 좌표공개={hasSourcePos}. 서버→이 클라이언트 경로 정상.");
            }

            IPulseDeliverySink sink = PulseNetworkRegistry.DeliverySink;
            if (sink == null)
            {
                if (_logDiagnostics)
                    Debug.LogWarning($"[PulseNet:Client] 델리버리를 그릴 수 없음 — PulseVisualRenderer(IPulseDeliverySink)가 " +
                                     $"레지스트리에 등록되지 않았습니다(누계 수신 {_clientReceivedCount}건 폐기).");
                return;
            }

            var deliveryKind = (PulseDeliveryKind)kind;
            PerceivedPulse? perceived = deliveryKind == PulseDeliveryKind.Disappeared
                ? (PerceivedPulse?)null
                : new PerceivedPulse(
                    perceivedRadius,
                    perceivedDuration,
                    hasSourcePos ? sourcePos : (Vector3?)null,
                    (DirectionOctant)direction,
                    worldSpaceRingVisible);

            // ListenerId는 수신 측에서 의미가 없다(자기 자신) — 렌더러는 PulseId로만 관리한다.
            var delivery = new PulseDelivery(0UL, pulseId, deliveryKind, perceived, gaugeLit);
            sink.Apply(delivery, Time.time);
        }
    }
}
