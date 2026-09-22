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

        /// <summary>
        /// §6.1 감쇠 중인가. <b>HUD가 색을 달리해야 하므로 플래그로 보낸다</b>(§12.4) —
        /// 진행도 숫자만으로는 "돌고 있다"와 "깎이고 있다"를 구분할 수 없고,
        /// 구분이 안 되면 "지금 뺄까 더 돌릴까" 판단 자체가 불가능해진다.
        /// </summary>
        private readonly SyncVar<bool> _decaying = new();

        /// <summary>
        /// §6.1-2 역류 시작 시점의 잔여 시간(초). <b>매 프레임 동기화하지 않는다</b>(§14.3) —
        /// 시작할 때 1회 보내고 클라이언트가 카운트다운한다. 0이면 역류 중이 아니다.
        /// </summary>
        private readonly SyncVar<float> _reflowRemainingAtStart = new();

        /// <summary>§6.1-0 이번 라운드 활성 여부. 비활성 밸브는 잠금 표시된다.</summary>
        private readonly SyncVar<bool> _active = new();

        private IValveHost _host;
        private ServerValveDriver _driver; // 권위 구동기 — 서버에서만 생성된다.

        /// <summary>
        /// §6.1 [v0.4] 수중 밸브(B·E) 조작 중 2.5초 주기 강제 파문. 서버 전용.
        /// 배수구(§6.5-3)와 <b>같은 클래스</b>를 쓴다 — 규칙이 하나다.
        /// </summary>
        private readonly UnderwaterWorkPulse _underwaterPulse = new UnderwaterWorkPulse();

        /// <summary>
        /// §6.1 [v0.4] 수중 밸브 작업 세션(플레이어별) — 진입 → 회전 → 부상. 서버 전용.
        /// 세션이 살아 있는 동안 그 플레이어는 잠수 의도를 가진 것으로 본다(<see cref="ServerIsInUnderwaterWork"/>)
        /// — 숨 게이지가 회전 5.0초가 아니라 <b>진입 시작부터 부상 완료까지</b> 소모된다(GAP-91 해소).
        /// </summary>
        private readonly Dictionary<ulong, UnderwaterWorkSession> _underwaterSessions =
            new Dictionary<ulong, UnderwaterWorkSession>();

        /// <summary>수중 밸브에 대해 "누르고 있다"고 주장한 플레이어(서버 전용).</summary>
        private readonly HashSet<ulong> _underwaterHolding = new HashSet<ulong>();

        private readonly List<ulong> _sessionScratch = new List<ulong>();

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

        /// <summary>§6.1 감쇠 중인가(§12.4 HUD 색 구분용).</summary>
        public bool IsDecaying => _decaying.Value;

        /// <summary>§6.1-2 역류 시작 시점의 잔여 시간(초). 클라이언트가 여기서 카운트다운한다.</summary>
        public float ReflowRemainingAtStart => _reflowRemainingAtStart.Value;

        /// <summary>§6.1-0 이번 라운드 활성 여부.</summary>
        public bool IsActiveThisRound => _active.Value;

        /// <summary>
        /// §10.2 이 밸브의 식별자. 호스트(<c>ValveBehaviour</c>)가 들고 있는 Core 밸브에서 읽는다 —
        /// <c>RoundNetworkSync</c>가 §6.1-0 활성 조합을 배정할 때 쓴다.
        /// </summary>
        internal ValveId? ValveId
        {
            get
            {
                EnsureHost();
                return _host?.Valve?.Id;
            }
        }

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

            // §6.1 [v0.4] 수중 밸브(B·E)는 진입(하강)을 먼저 거친다 — 손을 대는 것은 진입이 끝난 뒤다.
            ValveId? valveId = _host.Valve.Id;
            if (valveId.HasValue && ValveOccupancy.IsUnderwater(valveId.Value))
            {
                SubmitUnderwaterHold(playerId, role, held, valveId.Value);
                PushState();
                return;
            }

            if (held)
            {
                bool wasHolding = _host.Valve.IsInteracting(playerId);
                ValveInteractionRejection rejection = _driver.BeginHold(playerId, role);
                if (rejection == ValveInteractionRejection.None && !wasHolding)
                    EmitRotationPulse(playerId);

                if (rejection != ValveInteractionRejection.None)
                {
                    // §6.1-0 사유를 구분해 남긴다 — HUD가 "비활성"과 "이미 열림"을
                    // 다르게 말해야 하고, 로그만으로도 어느 규칙이 막았는지 알 수 있어야 한다.
                    Debug.Log($"[ValveNet:Server] {name} 홀드 거부 — {rejection} (playerId={playerId})");
                }
            }
            else
            {
                _driver.EndHold(playerId);
            }

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

            // ★ v0.4: **회전 중이 아닐 때도 반드시 Tick한다.**
            //   이전에는 `if (!_driver.IsRotating) return;` 으로 막혀 있었는데,
            //   그 상태로 §6.1 진행도 감쇠와 §6.1-2 역류를 넣으면 둘 다 영영 돌지 않는다
            //   (과거 ServerKnockDriver.Reset() 호출부 부재와 같은 유형 — 더블체크 1).
            ValveTickResult tick = _driver.Tick(Time.deltaTime);
            TickUnderwaterSessions(Time.deltaTime);
            PushState();

            // §6.1 [v0.4] 수중 작업 중 강제 파문 — "그대로 두면 수중 밸브(B·E)가 무음 안전지대가
            //   되어 '밸브 작업은 술래를 부를지 말지 재는 도박' 설계가 수중에서만 무효화된다."
            //   밸브 회전음과 같은 12m 등급이며 새 SoundType을 만들지 않는다(§3.3).
            //   수면 ×0.5를 지나 술래 청취 7.2m로 도달한다(블록 1-A②에서 테스트로 고정).
            ValveId? id = _host?.Valve?.Id;
            if (id.HasValue && ValveOccupancy.IsUnderwater(id.Value))
            {
                int pulses = _underwaterPulse.Tick(_driver.IsRotating, Time.deltaTime);
                // 지속 = 강제 주기 2.5초(GAP-103) — 주기마다 하나씩 이어져 "작업 중 내내"가 된다.
                for (int i = 0; i < pulses; i++)
                    PulseNetworkSync.ServerEmitWorldPulse(Core.Sound.SoundType.Valve, transform.position,
                        UnderwaterWorkPulse.IntervalSeconds);
            }

            if (tick.Opened)
            {
                Debug.Log($"[ValveNet:Server] {name} 개방 완료 — 서버가 회전 시간을 모두 확정. " +
                          $"§6.1-2 역류 타이머 {Valve.OpenHoldSeconds:0}초 시작");
            }

            if (tick.ReflowStarted)
            {
                Debug.Log($"[ValveNet:Server] {name} 역류 시작 — {Valve.ReflowSeconds:0}초 후 완전 폐쇄 (§6.1-2)");
            }

            if (tick.Closed)
            {
                Debug.Log($"[ValveNet:Server] {name} 완전 폐쇄 — 진행도 0 (§6.1-2)");
            }

            // §6.1-2 "역류 구간 중 10초마다 1회, 총 3회" 물소리 파문.
            // **새 SoundType을 만들지 않는다** — 기존 Valve 등급(12m) 재사용(§3.3 금지).
            for (int i = 0; i < tick.ReflowPulses; i++)
                EmitReflowPulse();
        }

        /// <summary>권위 상태를 SyncVar에 반영한다. 서버 전용.</summary>
        private void PushState()
        {
            if (_state.Value != _driver.State)
            {
                ValveState prev = _state.Value;
                _state.Value = _driver.State;

                // §14.3 역류 잔여는 **시작 시 1회만** 보낸다. 클라이언트가 카운트다운한다.
                _reflowRemainingAtStart.Value =
                    _driver.State == ValveState.Reflowing ? _driver.ReflowRemaining : 0f;

                Debug.Log($"[ValveNet:Server] {name} 상태 {prev} → {_driver.State} " +
                          $"(홀더={FormatHolder(_driver.HolderId)} ×{_driver.HolderCount})");
            }

            _progress.Value = _driver.Progress01;

            // 감쇠 플래그는 바뀔 때만 쓴다 — SyncVar 대입은 값이 같으면 전송하지 않지만,
            // 의도를 코드에 남겨 둔다(§14.3 "진행도도 매 프레임 보내지 마라").
            if (_decaying.Value != _driver.IsDecaying)
                _decaying.Value = _driver.IsDecaying;

            if (_active.Value != _driver.IsActive)
                _active.Value = _driver.IsActive;
        }

        /// <summary>
        /// 수중 밸브 홀드 의사. 누르면 <b>진입</b>을 시작하고(밸브에는 아직 손대지 않는다), 떼면 의사만 지운다 —
        /// 구간 전이는 <see cref="TickUnderwaterSessions"/>가 한다.
        /// </summary>
        private void SubmitUnderwaterHold(ulong playerId, RoleType role, bool held, ValveId id)
        {
            if (!held)
            {
                _underwaterHolding.Remove(playerId);
                return;
            }

            // 어차피 거부될 작업이면 의사도 받지 않는다(비활성·이미 열림·메아리).
            ValveInteractionRejection pre = _host.Valve.CheckInteract(role);
            if (pre != ValveInteractionRejection.None)
            {
                Debug.Log($"[ValveNet:Server] {name} 홀드 거부 — {pre} (playerId={playerId})");
                return;
            }

            // 의사만 보관한다 — 진입(세션 시작)은 자격(범위 · 실제로 잠길 수 있는 자리)이 되는 틱에
            // TickUnderwaterSessions가 연다. 덱 위에서 누른 채 물로 들어가도 다시 누를 필요가 없다.
            _underwaterHolding.Add(playerId);
        }

        /// <summary>
        /// 수중 밸브 작업 자격 — 도망자 · 수평 2.5m · <b>이 자리에서 실제로 잠길 수 있음</b>(GAP-88 해소).
        /// 배수구와 같은 식(<see cref="UnderwaterWorkSession.CanWork"/>). 위치는 서버가 아는 플레이어 위치다.
        /// </summary>
        private bool IsEligibleForUnderwaterWork(ulong playerId, out RoleType role)
        {
            role = RoleType.Echo;
            RoleNetworkSync player = null;
            for (int i = 0; i < RoleNetworkSync.Spawned.Count; i++)
            {
                RoleNetworkSync p = RoleNetworkSync.Spawned[i];
                if (p != null && p.OrderKey >= 0 && (ulong)p.OrderKey == playerId)
                {
                    player = p;
                    break;
                }
            }

            if (player == null)
                return false; // 나갔다

            role = player.EffectiveRole;
            Vector3 feet = player.transform.position;
            bool inRange = InteractionRules.InRange(feet, transform.position, underwaterTarget: true);
            return UnderwaterWorkSession.CanWork(role, inRange, Core.Water.WaterVolumeRegistry.Sample(feet), feet.y,
                PulseNetworkSync.ServerCanSubmerge(playerId));
        }

        /// <summary>
        /// 수중 작업 세션을 진전시킨다. 진입이 끝나면 밸브에 손을 대고(<c>BeginHold</c> + 회전 파문),
        /// 끊기면 뗀다(<c>EndHold</c>). 숨이 다하면 §5.9-1 강제 부상으로 세션이 끝난다.
        /// </summary>
        private void TickUnderwaterSessions(float dt)
        {
            // ① 시작 — 누르고 있고 자격이 되면 진입을 연다(이미 세션이 있으면 그 세션이 끝난 뒤).
            ValveId? valveId = _host?.Valve?.Id;
            if (valveId.HasValue && _underwaterHolding.Count > 0)
            {
                foreach (ulong playerId in _underwaterHolding)
                {
                    if (_underwaterSessions.ContainsKey(playerId))
                        continue;

                    if (_host.Valve.CheckInteract(RoleType.Runner) != ValveInteractionRejection.None)
                        break; // 열렸거나 잠겼다 — 아무도 내려갈 이유가 없다

                    if (!IsEligibleForUnderwaterWork(playerId, out _))
                        continue;

                    _underwaterSessions[playerId] = new UnderwaterWorkSession(valveId.Value);
                    Debug.Log($"[ValveNet:Server] {name} 수중 진입 시작 — playerId={playerId}, " +
                              $"진입 {ValveOccupancy.EntrySeconds(valveId.Value):0.0}초 후 회전 " +
                              $"(§6.1 총 점유 {ValveOccupancy.TotalSeconds(valveId.Value):0.0}초 전 구간 잠수)");
                }
            }

            if (_underwaterSessions.Count == 0)
                return;

            // ② 진행
            _sessionScratch.Clear();
            _sessionScratch.AddRange(_underwaterSessions.Keys);

            for (int i = 0; i < _sessionScratch.Count; i++)
            {
                ulong playerId = _sessionScratch[i];
                UnderwaterWorkSession session = _underwaterSessions[playerId];

                bool eligible = IsEligibleForUnderwaterWork(playerId, out RoleType role);
                bool holding = _underwaterHolding.Contains(playerId) && eligible;
                bool finished = _driver.State == ValveState.Open;
                bool canSubmerge = PulseNetworkSync.ServerCanSubmerge(playerId);

                UnderwaterWorkTick step = session.Tick(dt, holding, finished, canSubmerge);

                if (step.BeginRotation)
                {
                    ValveInteractionRejection rejection = _driver.BeginHold(playerId, role);
                    if (rejection == ValveInteractionRejection.None)
                    {
                        EmitRotationPulse(playerId);
                    }
                    else
                    {
                        session.ForceSurface(); // 내려간 사이 다른 사람이 열었다 등
                        Debug.Log($"[ValveNet:Server] {name} 진입 후 회전 거부 — {rejection} (playerId={playerId}) → 부상");
                    }
                }

                if (step.StopRotation)
                    _driver.EndHold(playerId);

                if (session.EndedByForce)
                {
                    _underwaterHolding.Remove(playerId); // 강제 부상 — 다시 누르게 한다(자동 재진입 반복 방지)
                    Debug.Log($"[ValveNet:Server] {name} 강제 부상 — playerId={playerId} 숨 0 (§5.9-1)");
                }

                if (session.Phase == UnderwaterWorkPhase.Done)
                    _underwaterSessions.Remove(playerId);
            }
        }

        /// <summary>
        /// 이 플레이어가 지금 어느 수중 밸브의 작업 구간(진입·회전·부상) 안에 있는가. 서버 전용, 라운드 중에만.
        /// <c>PulseNetworkSync.ZoneOf</c>가 잠수 의도로 합친다 — 실제로 머리가 잠기는지는 지오메트리가 정한다.
        /// </summary>
        internal static bool ServerIsInUnderwaterWork(ulong playerId)
        {
            if (RoundNetworkSync.ServerPhase != Core.GameFlow.GameFlowState.InGame)
                return false;

            for (int i = 0; i < Spawned.Count; i++)
            {
                ValveNetworkSync v = Spawned[i];
                if (v != null && v._underwaterSessions.TryGetValue(playerId, out UnderwaterWorkSession s) && s.KeepsSubmerged)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// §6.1 밸브 회전 소음 — <b>서버가</b> 홀드를 수락한 순간 1회 낸다(블록 7).
        ///
        /// <para>
        /// <b>결함 수정.</b> 네트워크 경로(<c>ValveInteractor.TickNetworked</c>)는 홀드 의사만 보내고 소리를
        /// 내지 않아, 멀티플레이에서 밸브가 <b>무음</b>이었다 — §6.1 "밸브 작업은 술래를 부를지 말지 재는
        /// 도박"이 통째로 빠져 있었다. 게다가 서버 표 값은 v0.3의 3초였고 밸브 A의 ×0.5도 어느 경로에도
        /// 없었다. 이제 반경 = <see cref="ValveOccupancy.SoundRadiusMeters"/>(A 6m / 나머지 12m),
        /// 지속 = <see cref="ValveOccupancy.PulseDurationSeconds"/>(§5.1 "각 밸브 회전 시간"),
        /// 위치 = 밸브, 발생원 = 돌리는 사람.
        /// </para>
        /// </summary>
        private void EmitRotationPulse(ulong playerId)
        {
            ValveId? id = _host?.Valve?.Id;
            if (!id.HasValue)
                return;

            PulseNetworkSync.ServerEmitPlayerPulse(playerId, Core.Sound.SoundType.Valve, transform.position,
                ValveOccupancy.SoundRadiusMeters(id.Value), ValveOccupancy.PulseDurationSeconds(id.Value));
        }

        /// <summary>
        /// §6.1-2 역류 물소리 파문. 발생 반경은 §5.1 Valve 등급(12m)이며
        /// <b>밸브 A의 ×0.5 기계 앰비언스 보정은 적용하지 않는다</b> —
        /// §6.1의 ×0.5는 <i>회전음</i>에 붙은 환경 규칙이고, 역류는 물소리라
        /// 같은 환경 보정을 받는다는 근거가 기획서에 없다(GAP-83).
        ///
        /// <para>
        /// 파문 자체는 <c>PulseNetworkSync</c>의 서버 경로를 쓴다 — 밸브 회전음과 완전히
        /// 같은 경로라 새 채널을 만들지 않는다.
        /// </para>
        /// </summary>
        private void EmitReflowPulse()
        {
            // 지속은 이 밸브의 회전 시간(§5.1 Valve 등급 "각 밸브 회전 시간") — GAP-103.
            ValveId? id = _host?.Valve?.Id;
            PulseNetworkSync.ServerEmitWorldPulse(
                Core.Sound.SoundType.Valve, transform.position,
                id.HasValue ? ValveOccupancy.PulseDurationSeconds(id.Value) : 0f);

            Debug.Log($"[ValveNet:Server] {name} 역류 물소리 파문 (§6.1-2, Valve 등급 {Valve.SoundRadiusMeters:0}m)");
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
        /// §6.1-0 이번 라운드 활성 여부를 서버가 지정한다. 서버 전용.
        /// <c>RoundNetworkSync</c>가 라운드 시작 시 <c>ValveRoster.SelectActive</c> 결과로 호출한다.
        /// </summary>
        internal void ServerSetActive(bool active)
        {
            if (_driver == null)
                return;

            _driver.SetActive(active);
            PushState();
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

            // ★ v0.4: Valve 인스턴스를 **교체하지 않는다.** 교체하면 구동기가 구독한
            //   Opened/ReflowStarted/ReflowPulse/Closed 이벤트가 끊어져 감쇠·역류 전파가
            //   조용히 사라진다. §6.1 [v0.4]가 Valve.ResetForNewRound를 제공하므로
            //   같은 인스턴스를 제자리에서 되돌린다.
            _driver.ResetForNewRound(active: true);
            _underwaterPulse.Reset();
            _underwaterSessions.Clear();
            _underwaterHolding.Clear();

            _state.Value = _driver.State;
            _progress.Value = _driver.Progress01;
            _decaying.Value = false;
            _reflowRemainingAtStart.Value = 0f;
            _active.Value = true;

            // 송신 디듀프 상태도 지워, 새 라운드의 첫 홀드 의사가 반드시 서버로 전달되게 한다.
            _hasSent = false;
            _lastSentHeld = false;
            _lastSentPlayer = 0;
        }

        /// <summary>
        /// §3.6 "밸브 상호작용 중" — 이 플레이어가 지금 어느 밸브든 돌리고 있는가. 서버 전용.
        /// 캠핑 방지 일시중단 판정이 쓴다(정지가 강제되는 행동이고 이미 12m 파문을 내고 있다).
        /// </summary>
        internal static bool ServerIsInteracting(ulong playerId)
        {
            for (int i = 0; i < Spawned.Count; i++)
            {
                ValveNetworkSync v = Spawned[i];
                if (v != null && v._driver != null && v._host?.Valve != null && v._host.Valve.IsInteracting(playerId))
                    return true;
            }

            return false;
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
