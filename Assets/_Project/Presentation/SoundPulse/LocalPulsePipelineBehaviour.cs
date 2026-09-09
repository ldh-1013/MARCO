using UnityEngine;
using UnityEngine.InputSystem;
using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// LocalPulsePipeline의 Unity 수명주기 어댑터(얇은 껍데기 — 로직 없음).
    /// FirstPersonController의 발소리 이벤트를 구독해 파이프라인에 넘기고,
    /// 매 프레임 Time.time으로 재판정 틱을 구동하며, 델리버리를 Console 로그로만 출력한다.
    ///
    /// [임시 스모크 리그] 청취 기준점은 Start 시점의 플레이어 위치에 고정된 가상
    /// 청취자(별도 ID)다 — 실제 게임플레이 기능이 아니며, 걸어서 멀어지거나 벽 뒤로
    /// 돌아가면 Appeared/Updated/Disappeared 변화로 §5.6 차폐가 실물 검증된다.
    ///
    /// T8부터 델리버리 소비자는 <see cref="PulseVisualRenderer"/>다. Debug.Log는
    /// 디버깅 편의로 남겨두되 기본은 꺼져 있다(_logDeliveries).
    /// </summary>
    public sealed class LocalPulsePipelineBehaviour : MonoBehaviour
    {
        /// <summary>GAP-1(본인 제외) 때문에 발생원과 달라야 델리버리가 관측되는 디버그 청취자 ID.</summary>
        private const ulong DebugListenerId = 999;

        [SerializeField] private FirstPersonController _player;
        [SerializeField] private PulseVisualRenderer _visuals;

        [Header("디버그")]
        [SerializeField] private bool _logDeliveries;

        [Header("진단 (스프린트 14 실기 디버그 — 확인 끝나면 꺼도 됨)")]
        [Tooltip("어느 경로(서버 권위 / 로컬 스모크 리그)로 동작 중인지, 브릿지·싱크·프로브가 " +
                 "연결됐는지를 이정표 1회 + 2초 주기로 로그한다.")]
        [SerializeField] private bool _logDiagnostics = true;

        private float _nextDiagTime;
        private bool _loggedBridgeState;
        private bool? _lastLoggedNetworkActive;

        [Tooltip("§15.5 성능 목표(동시 30개) 체감 확인용. 누르면 청취점 주변에 파문을 한꺼번에 생성한다.")]
        [SerializeField] private Key _stressTestKey = Key.P;
        [SerializeField, Range(1, 100)] private int _stressTestPulseCount = 30;

        [Tooltip("§19 색맹 모드 토글. 인스펙터를 건드리지 않고 Play 중에 색상 전환을 확인한다.")]
        [SerializeField] private Key _colorblindToggleKey = Key.C;

        private LocalPulsePipeline _pipeline;
        private Vector3 _listenerAnchor;
        private IPulseNetworkBridge _bridge;
        private PhysicsOcclusionProbe _probe;
        private PhysicsFootstepMaterialProbe _materialProbe;

        /// <summary>
        /// 서버 권위 파문 경로가 활성인가(스프린트 14). true면 로컬에서 §5.6을 판정하지 않고
        /// 서버에 소리 발생만 알린다 — 판정·전송은 서버가 청취자별로 수행한다.
        /// </summary>
        public bool IsNetworkActive => _bridge != null && _bridge.NetworkActive;

        private void Awake()
        {
            if (_visuals == null)
                _visuals = FindAnyObjectByType<PulseVisualRenderer>();

            // 같은 오브젝트의 Net 브릿지(없으면 null — 로컬 전용). Core 인터페이스로만 잡는다.
            _bridge = GetComponent<IPulseNetworkBridge>();

            // [진단 ①] 브릿지 부착 여부. 이게 없으면 네트워크 경로로 절대 전환되지 않으므로
            // 발소리가 로컬 스모크 리그(고정 청취자)로만 돌아 "자기 화면에만 보이는" 증상이 난다.
            // 컴포넌트는 씬 저장 시점에 확정되므로, 미부착이면 자산 문제(툴 미실행/씬 미저장)다.
            if (_logDiagnostics && !_loggedBridgeState)
            {
                _loggedBridgeState = true;
                if (_bridge == null)
                {
                    Debug.LogWarning($"[Pulse:Diag] '{name}'에 IPulseNetworkBridge(PulseNetworkSync)가 없습니다 — " +
                        "발소리는 항상 로컬 스모크 리그로만 처리됩니다(원격 전달 불가). " +
                        "Tools/MARCO/Setup Network Pulse 실행 → 씬 저장(Ctrl+S) → Reserialize NetworkObjects 후 재빌드가 필요합니다.");
                }
                else
                {
                    Debug.Log($"[Pulse:Diag] '{name}'에서 PulseNetworkSync 브릿지를 찾았습니다 — " +
                              "네트워크 스폰이 완료되면 서버 권위 경로로 전환됩니다.");
                }
            }

            // 발생원 ID는 플레이어 바인딩 시점에 실제 값으로 덮어쓴다(BindPlayer).
            // 그 전까지는 로컬 폴백값(1)으로 시작한다.
            _probe = new PhysicsOcclusionProbe();
            _pipeline = new LocalPulsePipeline(_probe, FirstPersonController.LocalFallbackPlayerId);
            _pipeline.DeliveryEmitted += OnDelivery;

            // 스프린트 14: 서버(Net)가 §5.6 차폐 판정에 쓸 구현체를 Core 레지스트리에 올린다.
            // 호스트에서는 이 프로브가 서버 판정 경로에 그대로 쓰인다(§15.2 경계 유지).
            PulseNetworkRegistry.RegisterProbe(_probe);

            // §5.9 재질 배율(4단계). 차폐 프로브와 같은 자리에 같은 방식으로 올린다 —
            // 서버가 발소리 발생 반경을 정할 때 이 구현체로 바닥을 조회한다.
            _materialProbe = new PhysicsFootstepMaterialProbe();
            PulseNetworkRegistry.RegisterFootstepMaterialProbe(_materialProbe);

            // 플레이어는 네트워크로 스폰될 수 있어 이 시점에 없을 수 있다.
            // 준비되면 알려달라고 등록해 둔다(이미 있으면 즉시 콜백).
            LocalPlayerRegistry.WhenReady(BindPlayer);
        }

        private void OnDestroy()
        {
            LocalPlayerRegistry.StopWaiting(BindPlayer);
            PulseNetworkRegistry.UnregisterProbe(_probe);
            PulseNetworkRegistry.UnregisterFootstepMaterialProbe(_materialProbe);
        }

        /// <summary>
        /// 발소리 이벤트 구독 대상을 <paramref name="player"/>로 맞춘다.
        ///
        /// <b>대상이 바뀔 때만 구독을 갈아탄다</b> — 같은 pawn이면 즉시 반환하므로 매 프레임
        /// 호출해도 구독/해제가 반복되지 않는다. <see cref="Update"/>가 매 프레임
        /// <c>LocalPlayerRegistry.Current</c>로 이 메서드를 부르는 이유는 GAP-61이다:
        /// 모든 pawn이 <c>IsLocallyControlled</c> 기본값 true로 자기를 등록하므로 순수
        /// 클라이언트에서는 **원격 pawn이 먼저 도착해** 여기 물릴 수 있는데, 원격 pawn은
        /// <c>Update</c>가 조기 반환해 <c>FootstepPulseEmitted</c>를 영영 내지 않는다
        /// (= 내 발소리가 파문이 되지 않는다). 1회성 바인딩이면 그 상태가 고착된다.
        /// </summary>
        private void BindPlayer(FirstPersonController player)
        {
            if (_player == player)
                return;

            if (_player != null)
                _player.FootstepPulseEmitted -= OnFootstepPulse;

            _player = player;

            if (_player == null)
                return; // 로컬 pawn이 사라진 구간(디스폰·씬 전환) — 다음 프레임에 다시 잡는다.

            _player.FootstepPulseEmitted += OnFootstepPulse;

            // 발생원 ID를 실제 플레이어 신원으로 맞춘다(로컬이면 폴백 1, 네트워크면 OwnerId).
            _pipeline.SourcePlayerId = _player.PlayerId;

            // 임시 청취점: 플레이어가 등장한 위치에 고정(러너 역할 = §5.7 배율 ×1.0이라
            // 표시 수치가 §5.1 원본값 그대로 나와 눈으로 검증하기 쉽다).
            _listenerAnchor = _player.transform.position;
            _pipeline.SetDebugListener(DebugListenerId, _listenerAnchor, RoleType.Runner);

            // 주의: 이 로그는 네트워크 여부와 무관하게 항상 찍힌다(청취점을 미리 준비만 해두는 것).
            // 실제로 이 스모크 리그가 "사용되는지"는 Update의 [Pulse:Diag] 경로 로그로 판단해야 한다 —
            // 네트워크 경로에서는 로컬 틱을 돌리지 않으므로 이 청취자는 쓰이지 않는다.
            Debug.Log($"[Pulse] 디버그 청취점 준비: {_listenerAnchor} (id={DebugListenerId}, 임시 스모크 리그 — " +
                      "실제 사용 여부는 [Pulse:Diag] 경로 로그 확인)");
        }

        private void OnDisable()
        {
            if (_player != null)
                _player.FootstepPulseEmitted -= OnFootstepPulse;
        }

        private void Update()
        {
            float now = Time.time;

            // GAP-61: 로컬 pawn이 바뀌었으면 구독을 갈아탄다(같으면 즉시 반환 — 무비용).
            // 캐시된 참조를 그대로 믿으면 원격 pawn에 물린 채 고착된다.
            BindPlayer(LocalPlayerRegistry.Current);

            LogPathDiagnostics(now);

            // 스프린트 14: 네트워크 활성 시 §5.6 판정은 서버가 청취자별로 수행한다.
            // 로컬 트래커를 돌리면 이중 판정이 되고, 스모크 리그의 고정 청취자(id=999) 결과가
            // 실제 원격 청취자 결과와 섞여 화면에 뜬다 — 그래서 네트워크면 로컬 틱을 멈춘다.
            if (!IsNetworkActive)
                _pipeline.Tick(now);

            // 렌더러의 자체 만료 타이머는 델리버리와 무관하게 매 프레임 돌아야 한다 —
            // 자연 만료 시 Disappeared가 오지 않기 때문(T7 설계, 스프린트 3 조사 결론).
            // 네트워크 경로에서도 수신한 델리버리를 이 타이머가 소멸시키므로 항상 돌린다.
            if (_visuals != null)
                _visuals.Tick(now);

            HandleDebugInput(now);
        }

        /// <summary>
        /// [진단 ②] 현재 어느 경로로 동작 중인지(서버 권위 / 로컬 스모크 리그)와, 그 판단의 근거가
        /// 되는 값들을 남긴다. 경로가 바뀌는 순간은 즉시 1회, 그 외에는 2초 주기로 찍는다.
        ///
        /// **판단 조건은 <see cref="IsNetworkActive"/> 하나뿐이며 매 프레임 새로 평가된다** —
        /// Awake에서 캐시하는 것은 컴포넌트 참조(<c>_bridge</c>)뿐이고 네트워크 상태는 캐시하지
        /// 않는다. 따라서 스폰이 늦어도(BindPlayer가 먼저 실행돼도) 스폰 완료 시점에 자동 전환된다.
        /// 이 로그로 "전환이 실제로 일어났는지"를 시각으로 확인할 수 있다.
        /// </summary>
        private void LogPathDiagnostics(float now)
        {
            if (!_logDiagnostics)
                return;

            bool networkActive = IsNetworkActive;
            bool pathChanged = _lastLoggedNetworkActive != networkActive;

            if (!pathChanged && now < _nextDiagTime)
                return;

            _lastLoggedNetworkActive = networkActive;
            _nextDiagTime = now + 2f;

            string bridgeState = _bridge == null
                ? "없음(미부착)"
                : (_bridge.NetworkActive ? "스폰됨" : "미스폰(연결 전이거나 스폰 대기)");

            Debug.Log($"[Pulse:Diag] 경로={(networkActive ? "서버 권위(원격 전달)" : "로컬 스모크 리그(자기 화면만)")} " +
                      $"| 브릿지={bridgeState} | 차폐프로브={(PulseNetworkRegistry.OcclusionProbe != null ? "등록됨" : "없음")} " +
                      $"| 렌더싱크={(PulseNetworkRegistry.DeliverySink != null ? "등록됨" : "없음")}" +
                      (pathChanged ? "  ← 경로 전환" : string.Empty));
        }

        private void OnFootstepPulse(SoundType type, float radius, float duration, Vector3 position)
        {
            // 네트워크면 "소리가 났다"만 알리고(서버가 §5.1 표로 반경·지속을, 서버 측 위치로
            // 발생 지점을, §5.9 재질로 반경 배율을 결정 — GAP-24), 로컬이면 직접 판정한다.
            if (IsNetworkActive)
            {
                _bridge.SubmitPulse(type);
                return;
            }

            // §5.9 재질 배율은 로컬 경로에서도 같은 Core 규칙으로 적용한다 —
            // 두 경로가 다른 반경을 내면 "호스트에서만 다르게 들린다"가 된다.
            FootstepMaterial material = PulseNetworkRegistry.SampleMaterial(type, position);
            if (!FootstepMaterialRules.EmitsPulse(material))
                return; // §5.9 물(수면 아래) — 파문 발생 안 함

            float materialRadius = FootstepMaterialRules.ApplyToRadius(radius, material);
            _pipeline.OnFootstepPulse(type, materialRadius, duration, position, Time.time);
        }

        /// <summary>
        /// 발소리 외 소리원(§5.1 밸브 회전 등)이 파문을 발행하는 진입점.
        /// 네트워크면 서버로 알리고(§5.1 밸브 12m 소음이 원격에도 들려야 §6.1 유인 설계가
        /// 성립한다), 로컬이면 기존 파이프라인이 차폐·재판정을 담당한다.
        /// </summary>
        public void EmitPulse(SoundType type, float radius, float duration, Vector3 position)
        {
            if (IsNetworkActive)
            {
                _bridge.SubmitPulse(type);
                return;
            }

            _pipeline?.EmitPulse(type, radius, duration, position, Time.time);
        }

        private void OnDelivery(PulseDelivery delivery)
        {
            if (_visuals != null)
                _visuals.Apply(delivery, Time.time);

            if (_logDeliveries)
                LogDelivery(delivery);
        }

        /// <summary>
        /// 수동 검증용 디버그 키. 절차는 docs/수동검증_절차.md 참조.
        /// - 스트레스 키(기본 P): §15.5 성능 목표(동시 30개 @60fps) 체감 확인. 정밀 실측은 T6 스코프
        /// - 색맹 토글 키(기본 C): §19 색맹 팔레트 전환이 실제로 반영되는지 확인
        /// </summary>
        private void HandleDebugInput(float now)
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_stressTestKey].wasPressedThisFrame)
            {
                for (int i = 0; i < _stressTestPulseCount; i++)
                {
                    if (IsNetworkActive)
                    {
                        // 네트워크에서는 발생 위치를 서버가 정하므로(GAP-24) 전부 내 위치에서 난다.
                        // 동시 파문 개수 부하는 그대로 재현된다(§15.5 목표 확인 목적은 유지).
                        _bridge.SubmitPulse(SoundType.Sprint);
                        continue;
                    }

                    Vector2 offset = Random.insideUnitCircle * 4f;
                    Vector3 position = _listenerAnchor + new Vector3(offset.x, 0f, offset.y);
                    _pipeline.OnFootstepPulse(SoundType.Sprint, 6f, 0.8f, position, now);
                }

                Debug.Log($"[Pulse] 스트레스 테스트: {_stressTestPulseCount}개 생성 " +
                          $"(활성 시각 오브젝트 {(_visuals != null ? _visuals.ActiveVisualCount : 0)}, " +
                          $"프레임 {Time.unscaledDeltaTime * 1000f:0.0}ms)");
            }

            if (keyboard[_colorblindToggleKey].wasPressedThisFrame && _visuals != null)
            {
                bool enabled = _visuals.ToggleColorblindMode();
                Debug.Log($"[Pulse] 색맹 모드 {(enabled ? "켜짐" : "꺼짐")} (§19)");
            }
        }

        private static void LogDelivery(PulseDelivery delivery)
        {
            if (delivery.Perceived.HasValue)
            {
                PerceivedPulse p = delivery.Perceived.Value;
                Debug.Log($"[Pulse] {delivery.Kind} pulse={delivery.PulseId} radius={p.PerceivedRadius:0.##} duration={p.PerceivedDuration:0.##} octant={p.Direction} ringVisible={p.WorldSpaceRingVisible}");
            }
            else
            {
                Debug.Log($"[Pulse] {delivery.Kind} pulse={delivery.PulseId}");
            }
        }
    }
}
