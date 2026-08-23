using Marco.Core.Net;
using Marco.Core.Role;
using Marco.Core.Sound;
using Marco.Presentation.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.Echo
{
    /// <summary>
    /// §3.2 메아리 노크의 입력 계층(스프린트 27).
    ///
    /// **§4.3 키 배정 원문**: <c>메아리 전용: 미니맵/노크 지정 | Tab(미니맵 토글) + 좌클릭(지점 지정)</c>.
    /// 그래서 이 컴포넌트는 임시 키를 만들지 않는다 — 표에 명시된 조작을 그대로 구현한다.
    ///
    /// **§3.2 조작 원문**: "미니맵/탑다운 뷰에서 지점 클릭 → 1.5초 지연 후 해당 지점에서 소음 발생".
    /// 노크는 <b>벽을 두드리는 근접 행동이 아니라 원격 지점 지정</b>이며("사거리 제한 없음"),
    /// 그래서 미니맵이 연출이 아니라 **입력 장치 자체**다. 이것이 없으면 능력이 성립하지 않는다.
    ///
    /// **서버가 결정하는 것 / 클라이언트가 결정하는 것**:
    /// - 클라이언트: 어느 지점인가(§3.2가 허용한 유일한 자유도)
    /// - 서버: 메아리인가 · 쿨다운 30초 · 1.5초 지연 · 파문 반경/지속 · 차폐 · 유인 판정
    /// 아래 역할·쿨다운 검사는 **표시와 트래픽 절약용 사전 필터**일 뿐이며, 통과 여부의
    /// 진실은 <c>ServerKnockDriver</c>가 쥔다(밸브·태그와 같은 원칙).
    ///
    /// **미니맵 카메라를 런타임에 만드는 이유**: 씬 YAML로 카메라 계층을 편집하는 것은 이
    /// 프로젝트에서 금지된 방식이고(에디터 생성값 손상), 에디터 도구로 만들면 "툴 실행 후
    /// 씬 저장 누락"이 재발할 수 있다 — <c>InGameHud</c>가 uGUI를 코드로 세우는 것과 같은 판단이다.
    /// </summary>
    public sealed class EchoKnockController : MonoBehaviour
    {
        [Header("§4.3 키")]
        [Tooltip("§4.3 '메아리 전용: 미니맵/노크 지정 | Tab(미니맵 토글)'.")]
        [SerializeField] private Key _minimapKey = Key.Tab;

        [Header("§3.2 미니맵(탑다운 뷰)")]
        [Tooltip("맵 중심 월드 좌표. 기획서에 수치가 없어 그레이박스 기준으로 둔 값이다(GAP-60).")]
        [SerializeField] private Vector3 _mapCenter = new Vector3(0f, 0f, 0f);

        [Tooltip("탑다운 카메라의 세로 반경(m). §3.4 주석의 맵 크기 45×35m를 담도록 잡은 값이다(GAP-60).")]
        [SerializeField] private float _orthographicSize = 20f;

        [Tooltip("카메라 높이(m). 맵 지오메트리보다 충분히 위여야 한다.")]
        [SerializeField] private float _cameraHeight = 40f;

        [Tooltip("노크 지점을 투영할 지면 높이(m). 클릭 광선이 이 평면과 만나는 곳이 발생 지점이 된다.")]
        [SerializeField] private float _groundPlaneY;

        [Header("표시")]
        [SerializeField] private bool _showHud = true;

        private Camera _minimapCamera;
        private IPulseNetworkBridge _bridge;

        private bool _minimapOpen;
        private float _clientCooldownUntil; // 표시·트래픽 절약용 사전 필터(진실은 서버).

        /// <summary>미니맵이 열려 있는가(진단·테스트용).</summary>
        public bool MinimapOpen => _minimapOpen;

        /// <summary>표시용 남은 쿨다운(초). 서버 판정과 어긋날 수 있는 근사값이다.</summary>
        public float CooldownRemaining => Mathf.Max(0f, _clientCooldownUntil - Time.time);

        private void OnDisable()
        {
            SetMinimap(false);
        }

        private void Update()
        {
            // 메아리가 아니면 능력 자체가 없다(§3.2). 역할이 바뀌면 미니맵도 닫는다.
            if (!IsLocalEcho())
            {
                if (_minimapOpen)
                    SetMinimap(false);
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard[_minimapKey].wasPressedThisFrame)
                SetMinimap(!_minimapOpen);

            if (!_minimapOpen)
                return;

            HandleDesignationClick();
        }

        /// <summary>
        /// 로컬 플레이어가 살아 있고 메아리인가(§3.2 노크는 메아리 전용).
        ///
        /// <b>레지스트리를 매 프레임 다시 읽는다(캐시 금지).</b> 모든 pawn은 <c>OnEnable</c> 시점에
        /// <c>IsLocallyControlled</c> 기본값이 true라 **원격 pawn도 자기를 등록**하고, 순수
        /// 클라이언트에서는 원격 pawn이 먼저 도착하는 일이 흔하다. 그 시점에 한 번만 바인딩하면
        /// (<c>WhenReady</c>는 1회성이다) 소유권이 확정돼 <c>Unregister</c> 복구가 일어나도
        /// **이 컴포넌트만 원격 pawn을 계속 물고 있어** Tab이 조용히 아무 일도 하지 않는다.
        /// <c>InGameHud</c>·<c>FallRecoveryDriver</c>·<c>PawnPhaseTeleporter</c>·<c>SettingsStore</c>가
        /// 전부 매 프레임 <see cref="LocalPlayerRegistry.Current"/>를 읽는 것과 같은 이유다(GAP-61).
        ///
        /// 소유권 판별 자체는 새로 만들지 않는다 — <c>PlayerOwnershipGate</c>(Net)가
        /// <c>IsOwner</c>를 <see cref="Marco.Core.Net.ILocalControlGate"/>로 밀어 넣은 결과인
        /// <c>IsLocallyControlled</c>를 그대로 쓴다.
        /// </summary>
        private bool IsLocalEcho()
        {
            FirstPersonController player = LocalPlayerRegistry.Current;
            return player != null && player.IsLocallyControlled && player.Role == RoleType.Echo;
        }

        /// <summary>
        /// §4.3 "좌클릭(지점 지정)". 미니맵 카메라 기준 화면 좌표를 지면 평면에 투영해 월드 지점을 얻는다.
        /// </summary>
        private void HandleDesignationClick()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || !mouse.leftButton.wasPressedThisFrame)
                return;

            if (Time.time < _clientCooldownUntil)
            {
                Debug.Log($"[Knock] 아직 쿨다운 중이다 — {CooldownRemaining:0.0}초 남음(§3.2 {KnockConfig.CooldownSeconds:0}초).");
                return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            if (!TryResolveWorldPoint(screenPosition, out Vector3 point))
            {
                // 이 경로는 조용히 실패하면 "클릭이 안 먹는다"로만 보인다 — 원인을 남긴다.
                Debug.LogWarning($"[Knock] 지점을 계산하지 못했다 — 화면좌표={screenPosition}, " +
                                 $"미니맵 카메라={(_minimapCamera != null ? "있음" : "없음")}. " +
                                 "카메라가 지면 평면을 향하고 있는지 확인할 것.");
                return;
            }

            if (!TryGetBridge(out IPulseNetworkBridge bridge))
            {
                Debug.Log("[Knock] 접속 상태가 아니라 노크를 보낼 수 없다(로컬 단독 실행에서는 능력이 비활성이다).");
                return;
            }

            // 사전 필터를 먼저 걸어 둔다 — 서버가 거부하면 실제로는 더 길게 남아 있을 수 있다.
            _clientCooldownUntil = Time.time + KnockConfig.CooldownSeconds;

            bridge.SubmitKnock(point);
            Debug.Log($"[Knock] 지점 지정 — {point.ToString("0.0")} " +
                      $"({KnockConfig.ActivationDelaySeconds:0.#}초 뒤 발생, 반경 {KnockConfig.RadiusMeters:0.#}m — §3.2)");
        }

        /// <summary>미니맵 화면 좌표 → 지면 평면(<see cref="_groundPlaneY"/>) 위의 월드 좌표.</summary>
        private bool TryResolveWorldPoint(Vector2 screenPosition, out Vector3 point)
        {
            point = default;
            if (_minimapCamera == null)
                return false;

            Ray ray = _minimapCamera.ScreenPointToRay(screenPosition);
            var plane = new Plane(Vector3.up, new Vector3(0f, _groundPlaneY, 0f));

            if (!plane.Raycast(ray, out float distance))
                return false; // 광선이 평면과 평행 — 정상 구성에서는 오지 않는다.

            point = ray.GetPoint(distance);
            return true;
        }

        /// <summary>
        /// 발소리·음성이 쓰는 브릿지를 그대로 쓴다 — 노크 전용 네트워크 채널을 만들지 않는다
        /// (26b가 음성에서 내린 판단과 같다). 이 컴포넌트는 <c>SceneFlow</c>에 있고 브릿지는
        /// <c>PulseSystem</c>에 있어 씬에서 찾는다(같은 Lobby 씬).
        /// </summary>
        private bool TryGetBridge(out IPulseNetworkBridge bridge)
        {
            if (_bridge == null)
            {
                foreach (MonoBehaviour candidate in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
                {
                    if (candidate is IPulseNetworkBridge found)
                    {
                        _bridge = found;
                        break;
                    }
                }
            }

            bridge = _bridge;
            return bridge != null && bridge.NetworkActive;
        }

        // ── 미니맵(탑다운 뷰) ────────────────────────────────────────────

        private void SetMinimap(bool open)
        {
            if (_minimapOpen == open)
                return;

            _minimapOpen = open;

            if (open)
                EnsureCamera();

            if (_minimapCamera != null)
                _minimapCamera.enabled = open;

            // §4.3 좌클릭으로 지점을 찍으려면 커서가 풀려 있어야 한다. 1인칭에서는 커서가
            // 화면 중앙에 잠긴 채 숨겨져 있고, 그 상태에서는 Input System의 마우스 위치가
            // 물리 마우스를 따라가지 않아 **어디를 눌러도 같은 좌표**가 읽힌다.
            ApplyCursorRelease(open);

            Debug.Log(open
                ? $"[Knock] 미니맵 열림(§4.3 {_minimapKey}) — 커서 해제됨. 좌클릭으로 노크 지점을 지정한다."
                : $"[Knock] 미니맵 닫힘 — 1인칭 커서 잠금으로 복원한다(§4.1).");
        }

        /// <summary>
        /// 커서 잠금 해제/복원을 <b>플레이어에게 요청</b>한다. 커서 정책은
        /// <see cref="FirstPersonController"/>가 단독 소유하므로 여기서 <c>Cursor</c>를 직접
        /// 만지지 않는다 — 그래야 "여는 쪽과 닫는 쪽이 서로 다른 상태를 쓰는" 어긋남이 없다.
        ///
        /// 플레이어는 <b>호출 시점에 다시 조회한다</b>(캐시 금지 — GAP-61). 열 때와 닫을 때
        /// 사이에 pawn이 바뀌어도 각각 그 시점의 로컬 pawn에 적용된다.
        /// </summary>
        private void ApplyCursorRelease(bool released)
        {
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player != null)
                player.SetCursorReleased(released);
        }

        private void EnsureCamera()
        {
            if (_minimapCamera != null)
                return;

            var go = new GameObject("EchoMinimapCamera");
            go.transform.SetParent(transform, worldPositionStays: false);
            go.transform.position = _mapCenter + Vector3.up * _cameraHeight;
            go.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);

            _minimapCamera = go.AddComponent<Camera>();
            _minimapCamera.orthographic = true;
            _minimapCamera.orthographicSize = _orthographicSize;
            _minimapCamera.nearClipPlane = 0.1f;
            _minimapCamera.farClipPlane = _cameraHeight * 2f;

            // 플레이어 카메라 위에 그린다(§3.2 "탑다운 뷰" — 전환형 표시).
            _minimapCamera.depth = 50f;
            _minimapCamera.enabled = false;
        }

        private void OnGUI()
        {
            if (!_showHud || !_minimapOpen || Event.current.type != EventType.Repaint)
                return;

            float remaining = CooldownRemaining;
            string line = remaining > 0f
                ? $"노크 쿨다운 {remaining:0.0}초 (§3.2 {KnockConfig.CooldownSeconds:0}초)"
                : "좌클릭 — 노크 지점 지정 (§3.2 1.5초 뒤 발생)";

            GUI.Label(new Rect(16f, Screen.height - 40f, 520f, 24f), line);
        }
    }
}
