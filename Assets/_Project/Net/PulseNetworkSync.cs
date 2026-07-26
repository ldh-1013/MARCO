using System.Collections.Generic;
using System.Text;
using FishNet.Connection;
using FishNet.Object;
using Marco.Core.Net;
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

        private ServerPulseDriver _driver; // 서버에서만 생성된다.

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

        // ── 서버: 수집 ────────────────────────────────────────────────────

        public override void OnStartServer()
        {
            base.OnStartServer();
            _driver = new ServerPulseDriver();
            Debug.Log("[PulseNet:Server] 서버 권위 파문 판정 시작 — 청취자별 개별 전송(§14.3)");
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

            int pulseId = _driver.AddPulse(sourceId, type, serverPosition, Time.time);
            if (pulseId < 0)
            {
                // §5.1 표에 없는 종류 — 서버가 파문을 만들지 않는다.
                if (_logDiagnostics)
                    Debug.LogWarning($"[PulseNet:Server] 파문 거부 — type={type}는 §5.1 표에 없는 종류(GAP-24).");
                return;
            }

            if (_logServerDeliveries)
                Debug.Log($"[PulseNet:Server] 파문 등록 pulse={pulseId} type={type} sourceId={sourceId} (서버 위치 기준)");

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

            if (probe == null)
                return; // 차폐 판정기가 없으면 판정하지 않는다(좌표를 흘리지 않기 위함).

            if (_listeners.Count == 0)
                return;

            List<PulseDelivery> deliveries = _driver.Tick(Time.time, _listeners, probe);
            for (int i = 0; i < deliveries.Count; i++)
                SendDelivery(deliveries[i]);
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

                TargetPulseDelivery(target, delivery.PulseId, (byte)delivery.Kind,
                    p.PerceivedRadius, p.PerceivedDuration, hasSourcePos, sourcePos,
                    (byte)p.Direction, p.WorldSpaceRingVisible);
            }
            else
            {
                // Disappeared: 인지값이 없다(§5.6 소실). 좌표·반경은 보낼 것이 없다.
                TargetPulseDelivery(target, delivery.PulseId, (byte)delivery.Kind,
                    0f, 0f, false, Vector3.zero, 0, false);
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
            byte direction, bool worldSpaceRingVisible)
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
            var delivery = new PulseDelivery(0UL, pulseId, deliveryKind, perceived);
            sink.Apply(delivery, Time.time);
        }
    }
}
