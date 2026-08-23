using System.Collections.Generic;
using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Sound;
using Marco.Presentation.Palette;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// T8 파문 렌더러. <see cref="PulseVisualRegistry"/>가 관리하는 시각 상태를 실제
    /// 화면에 그린다 — Debug.Log를 대체하는 소비자다.
    ///
    /// GAP-2 분기를 그대로 표현한다(판단을 다시 하지 않는다):
    /// - WorldRing    : 발생 지점에서 PerceivedRadius까지 확장하는 월드스페이스 링(§5.4)
    /// - DirectionOnly: 좌표 없이 화면 가장자리 8방위 인디케이터만(§3.4)
    ///
    /// 이 구분 덕분에 "차폐돼서 방향만 보이는 것"과 "반경 밖이라 아예 안 보이는 것"이
    /// 처음으로 눈으로 구별된다(스프린트 3 스모크 리그의 한계 해소).
    ///
    /// 색상은 §16.2 ColorPalette에서 가져오며 색맹 모드 토글을 지원한다(§19).
    /// 링 재질은 URP Unlit을 런타임 생성하되, 인스펙터로 덮어쓸 수 있다.
    ///
    /// 방향 인디케이터는 UI 시스템이 아직 없어 IMGUI(OnGUI)로 그린다 — 기능 확인용
    /// 최소 구현이며 §12 HUD 작업 때 정식 UI로 교체한다.
    ///
    /// 스프린트 14(파문 네트워크화): <see cref="IPulseDeliverySink"/>를 구현해
    /// <see cref="PulseNetworkRegistry"/>에 등록한다. 서버가 개별 전송(§14.3 TargetRpc)한
    /// 델리버리가 <see cref="Apply"/>로 들어오는데, 이는 로컬 판정 결과가 들어오던 것과
    /// **완전히 같은 경로**다 — 시각화 코드는 하나도 새로 만들지 않았다.
    /// </summary>
    public sealed class PulseVisualRenderer : MonoBehaviour, IPulseDeliverySink
    {
        [Header("팔레트 (§16.2)")]
        [SerializeField] private ColorPalette _palette;
        [SerializeField] private bool _colorblindMode;

        [Header("링 표현")]
        [SerializeField] private Material _ringMaterialOverride;
        [SerializeField, Range(8, 128)] private int _ringSegments = 48;
        [SerializeField] private float _ringWidth = 0.06f;
        [SerializeField] private float _ringHeightOffset = 0.05f;

        [Tooltip("스트레스 상황(동시 30개)에서 첫 프레임에 GameObject를 한꺼번에 만들지 않도록 미리 확보한다.")]
        [SerializeField, Range(0, 64)] private int _prewarmRingCount = 32;

        [Header("방향 힌트 (§5.4 — 차폐 시 방위만)")]
        [SerializeField] private Camera _viewCamera;
        [SerializeField] private bool _showDirectionIndicators = true;
        [SerializeField, Range(0.5f, 1f)] private float _indicatorScreenExtent = 0.82f;

        [Header("방향 게이지 (§3.4 — 술래 전용)")]
        [Tooltip("§3.4 대화·고함에 대한 술래 전용 정보 우위. 서버가 역할·등급·사거리를 판정해 " +
                 "GaugeLit로 보내므로, 여기서는 표시만 켜고 끈다.")]
        [SerializeField] private bool _showDirectionGauge = true;

        [Tooltip("게이지는 §5.4 방향 힌트보다 화면 안쪽에 그려 두 표시가 겹치지 않게 한다.")]
        [SerializeField, Range(0.3f, 1f)] private float _gaugeScreenExtent = 0.62f;

        private readonly PulseVisualRegistry _registry = new PulseVisualRegistry();
        private readonly Dictionary<int, LineRenderer> _activeRings = new Dictionary<int, LineRenderer>();
        private readonly Stack<LineRenderer> _ringPool = new Stack<LineRenderer>();

        // 매 프레임 재사용하는 버퍼들 — 힙 할당을 프레임 루프에서 완전히 없애기 위함.
        private readonly List<PulseVisualState> _visualBuffer = new List<PulseVisualState>();
        private readonly List<PulseVisualState> _directionScratch = new List<PulseVisualState>();
        private readonly List<PulseVisualState> _gaugeScratch = new List<PulseVisualState>();

        private Material _ringMaterial;
        private GUIStyle _indicatorStyle;

        /// <summary>인스펙터에서 카메라를 직접 지정했는가(그러면 레지스트리로 덮어쓰지 않는다).</summary>
        private bool _viewCameraOverridden;

        // 진단: 첫 시각 오브젝트 생성 시 1회만 표현·색상을 로그한다(스프린트 14 실기 디버그).
        private bool _logFirstVisual = true;

        // OnGUI는 프레임마다 여러 번(Layout·Repaint·입력 이벤트) 호출되므로,
        // 프레임당 한 번만 계산하면 되는 값은 Tick에서 미리 구해 둔다.
        private Color _frameColor = Color.white;
        private Color _frameGaugeColor = Color.white;
        private float _frameCameraYaw;

        public PulseVisualRegistry Registry => _registry;
        public int ActiveVisualCount => _registry.ActiveVisualCount;
        public bool ColorblindMode => _colorblindMode;

        /// <summary>색맹 모드 토글(§19). 설정 화면이 생기면 그쪽에서 호출한다.</summary>
        public void SetColorblindMode(bool enabled) => _colorblindMode = enabled;

        /// <summary>디버그 키에서 쓰는 토글(§19 검증용).</summary>
        public bool ToggleColorblindMode()
        {
            _colorblindMode = !_colorblindMode;
            return _colorblindMode;
        }

        private void Awake()
        {
            // 카메라는 네트워크 스폰 플레이어의 자식일 수 있어 이 시점에 없을 수 있다.
            // 실제 확보는 Tick의 RefreshViewCamera가 매 프레임 담당한다(GAP-61).
            _viewCameraOverridden = _viewCamera != null;
            if (_viewCamera == null)
                _viewCamera = Camera.main;

            _ringMaterial = _ringMaterialOverride != null ? _ringMaterialOverride : CreateDefaultRingMaterial();

            _registry.VisualAdded += OnVisualAdded;
            _registry.VisualUpdated += OnVisualUpdated;
            _registry.VisualRemoved += OnVisualRemoved;

            // 30개가 동시에 뜨는 순간 GameObject를 한꺼번에 만들면 그 프레임만 튄다.
            for (int i = 0; i < _prewarmRingCount; i++)
                ReturnRing(CreateRing());
        }

        private void OnDestroy()
        {
            _registry.VisualAdded -= OnVisualAdded;
            _registry.VisualUpdated -= OnVisualUpdated;
            _registry.VisualRemoved -= OnVisualRemoved;
        }

        // 스프린트 14: 서버가 개별 전송(TargetRpc)한 델리버리를 받을 소비자로 자신을 등록한다.
        // Net은 Presentation을 참조할 수 없으므로 Core 레지스트리를 경유한다(§15.2).
        private void OnEnable() => PulseNetworkRegistry.RegisterSink(this);
        private void OnDisable() => PulseNetworkRegistry.UnregisterSink(this);

        /// <summary>파이프라인이 방출한 델리버리를 시각 상태에 반영한다.</summary>
        public void Apply(in PulseDelivery delivery, float now)
        {
            _registry.Apply(delivery, now);

            // [진단] 이 렌더러가 실제로 무엇을 그리는지 확정한다. 실기에서 관측된 "노란색 효과"가
            // T8 파문인지(색맹 모드면 러너색이 #F2E205 노랑) 다른 시스템인지(밸브 회전 표시가
            // (1,0.9,0.1) 노랑) 구별하기 위한 것이다. 첫 1회만 찍는다.
            if (_logFirstVisual && delivery.Perceived.HasValue)
            {
                _logFirstVisual = false;
                PerceivedPulse p = delivery.Perceived.Value;
                Color c = ResolvePulseColor();
                Debug.Log($"[PulseVis] ★ 첫 시각 오브젝트 생성 — pulse={delivery.PulseId} " +
                          $"표현={(p.WorldSpaceRingVisible ? "월드스페이스 링" : "화면 가장자리 방위 인디케이터")} " +
                          $"반경={p.PerceivedRadius:0.00}m 지속={p.PerceivedDuration:0.00}s 방위={p.Direction} | " +
                          $"색맹모드={_colorblindMode} 색상={c} (#{ColorUtility.ToHtmlStringRGB(c)}) | " +
                          "이 색과 화면에서 본 색이 다르면 다른 시스템(예: 밸브 노랑 (1,0.9,0.1))을 본 것이다.");
            }
        }

        /// <summary>매 프레임 호출: 자체 타이머 만료 처리 + 링 애니메이션 갱신.</summary>
        public void Tick(float now)
        {
            _registry.Tick(now);

            // 프레임당 1회만 계산해 OnGUI가 매 이벤트마다 다시 구하지 않게 한다.
            _frameColor = ResolvePulseColor();
            _frameGaugeColor = ResolveGaugeColor();

            RefreshViewCamera();
            if (_viewCamera != null)
                _frameCameraYaw = _viewCamera.transform.eulerAngles.y;

            _registry.CopyTo(_visualBuffer);
            UpdateRings(now);
        }

        /// <summary>
        /// 방위 인디케이터 기준 카메라를 **로컬 pawn의 것으로 유지**한다(매 프레임, GAP-61).
        ///
        /// 8방위 표시는 카메라 yaw를 빼서 상대 각도를 만들므로, 남의 카메라를 잡고 있으면
        /// **방향이 통째로 어긋난다**. 예전에는 1회성 <c>WhenReady</c>로 잡아, 순수 클라이언트에서
        /// 원격 pawn이 먼저 등록되면 그 카메라에 고착됐다.
        ///
        /// 인스펙터로 카메라를 직접 지정한 구성은 존중한다 — 그때는 레지스트리를 보지 않는다.
        /// </summary>
        private void RefreshViewCamera()
        {
            if (_viewCameraOverridden)
                return;

            Marco.Presentation.Player.FirstPersonController player =
                Marco.Presentation.Player.LocalPlayerRegistry.Current;
            if (player == null)
                return;

            Camera playerCamera = player.GetComponentInChildren<Camera>(includeInactive: true);
            if (playerCamera != null && !ReferenceEquals(playerCamera, _viewCamera))
                _viewCamera = playerCamera;
        }

        private void UpdateRings(float now)
        {
            _directionScratch.Clear();
            _gaugeScratch.Clear();

            for (int i = 0; i < _visualBuffer.Count; i++)
            {
                PulseVisualState state = _visualBuffer[i];

                // §3.4 게이지는 차폐 여부와 무관하게 뜬다 — 월드 링이 함께 보이는 상황
                // (벽 0개)에서도 술래는 게이지를 받는다. 그래서 Kind 분기 밖에서 모은다.
                if (state.GaugeLit)
                    _gaugeScratch.Add(state);

                if (state.Kind == PulseVisualKind.DirectionOnly)
                    _directionScratch.Add(state); // OnGUI가 쓸 목록을 여기서 미리 확정

                if (!_activeRings.TryGetValue(state.PulseId, out LineRenderer ring))
                    continue;

                if (state.Kind != PulseVisualKind.WorldRing)
                {
                    ring.enabled = false;
                    continue;
                }

                float progress = state.Progress01(now);
                float radius = Mathf.Max(state.Radius * progress, 0.01f);

                ring.enabled = true;
                ring.transform.position = state.SourcePos + Vector3.up * _ringHeightOffset;
                ring.transform.localScale = new Vector3(radius, 1f, radius);

                Color color = _frameColor;
                color.a = 1f - progress; // 확장하면서 옅어진다(§16.1 "투명 감쇠")
                ring.startColor = color;
                ring.endColor = color;
            }
        }

        private void OnVisualAdded(PulseVisualState state)
        {
            LineRenderer ring = RentRing();
            _activeRings[state.PulseId] = ring;
            ring.enabled = state.Kind == PulseVisualKind.WorldRing;
        }

        private void OnVisualUpdated(PulseVisualState state)
        {
            // 표현 종류가 바뀌면(차폐 발생/해소) 링 표시 여부만 토글하면 된다.
            if (_activeRings.TryGetValue(state.PulseId, out LineRenderer ring))
                ring.enabled = state.Kind == PulseVisualKind.WorldRing;
        }

        private void OnVisualRemoved(int pulseId)
        {
            if (!_activeRings.TryGetValue(pulseId, out LineRenderer ring))
                return;

            _activeRings.Remove(pulseId);
            ReturnRing(ring);
        }

        private Color ResolvePulseColor()
        {
            if (_palette == null)
                return Color.white;

            // 현재 발생원은 로컬 플레이어(러너)뿐이다. 발생원 역할에 따라 술래 색을
            // 쓰는 분기는 역할이 네트워크로 전달되는 시점에 배선한다(§16.2).
            return _palette.GetRunner(_colorblindMode);
        }

        /// <summary>
        /// §3.4 게이지 색. 술래에게만 보이는 표시이므로 §16.2 술래 색을 써서 §5.4 방향
        /// 힌트(러너 색)와 한눈에 구별되게 한다 — 색맹 모드도 팔레트가 함께 처리한다.
        /// </summary>
        private Color ResolveGaugeColor()
        {
            if (_palette == null)
                return Color.white;

            return _palette.GetSeeker(_colorblindMode);
        }

        private LineRenderer RentRing()
        {
            if (_ringPool.Count > 0)
            {
                LineRenderer pooled = _ringPool.Pop();
                pooled.gameObject.SetActive(true);
                return pooled;
            }
            return CreateRing();
        }

        private void ReturnRing(LineRenderer ring)
        {
            ring.enabled = false;
            ring.gameObject.SetActive(false);
            _ringPool.Push(ring);
        }

        private LineRenderer CreateRing()
        {
            var go = new GameObject("PulseRing");
            go.transform.SetParent(transform, worldPositionStays: false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            // Renderer.material은 머티리얼 사본을 인스턴스화한다(링 개수만큼 사본 + 배칭 불가).
            // 모든 링이 같은 재질을 공유해야 하므로 sharedMaterial로 대입한다.
            line.sharedMaterial = _ringMaterial;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.TransformZ;
            line.textureMode = LineTextureMode.Stretch;
            line.positionCount = _ringSegments;
            line.widthMultiplier = _ringWidth; // 매 프레임 재설정할 필요 없는 값

            // 단위원을 한 번만 굽고, 이후에는 transform.localScale로만 확장한다.
            for (int i = 0; i < _ringSegments; i++)
            {
                float angle = i / (float)_ringSegments * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)));
            }

            return line;
        }

        private static Material CreateDefaultRingMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Sprites/Default"); // URP 미탑재 환경 대비

            var material = new Material(shader) { name = "PulseRing (runtime)" };
            // 반투명 가산 합성 — 어두운 맵(§16.1)에서 파문만 떠오르게 한다.
            material.SetFloat("_Surface", 1f);       // Transparent
            material.SetFloat("_Blend", 1f);         // Additive
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private void OnGUI()
        {
            // OnGUI는 프레임마다 여러 번(Layout·Repaint·입력 이벤트) 호출된다.
            // 실제로 그려지는 것은 Repaint뿐이라 나머지 이벤트에서는 즉시 빠져나간다.
            if (Event.current.type != EventType.Repaint)
                return;

            bool drawHints = _showDirectionIndicators && _directionScratch.Count > 0;
            bool drawGauge = _showDirectionGauge && _gaugeScratch.Count > 0;
            if (!drawHints && !drawGauge)
                return;

            // 목록은 Tick에서 이미 확정됐다 — 여기서 레지스트리를 다시 순회하지 않는다.
            _indicatorStyle ??= new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 22,
                fontStyle = FontStyle.Bold
            };

            float now = Time.time;
            Color previous = GUI.color;

            if (drawHints)
                DrawOctantMarkers(_directionScratch, _frameColor, _indicatorScreenExtent, IndicatorGlyph, now);

            // §3.4 게이지는 §5.4 힌트 위에 그린다 — 같은 파문이 둘 다 해당될 때
            // 술래 전용 정보가 가려지지 않게 하기 위함이다.
            if (drawGauge)
                DrawOctantMarkers(_gaugeScratch, _frameGaugeColor, _gaugeScreenExtent, GaugeGlyph, now);

            GUI.color = previous;
        }

        /// <summary>
        /// 8방위 마커 한 벌을 화면 가장자리 원주에 그린다(§5.4 힌트·§3.4 게이지 공용).
        /// 표시 지속시간은 두 경우 모두 <c>perceivedDuration</c>과 같다 — §3.4가 명시한 규칙이고,
        /// <see cref="PulseVisualState.Progress01"/>이 그 값으로 페이드를 계산한다.
        /// </summary>
        private void DrawOctantMarkers(List<PulseVisualState> states, Color baseColor, float screenExtent,
            string glyph, float now)
        {
            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            float radiusX = centerX * screenExtent;
            float radiusY = centerY * screenExtent;

            for (int i = 0; i < states.Count; i++)
            {
                PulseVisualState state = states[i];

                // DirectionOctant는 월드 기준(N=+z, 시계방향). 화면 인디케이터는 카메라
                // 정면이 위쪽이어야 하므로 카메라 yaw를 빼서 상대 각도로 바꾼다.
                float worldAngle = (int)state.Direction * 45f;
                float relative = (worldAngle - _frameCameraYaw) * Mathf.Deg2Rad;

                float x = centerX + Mathf.Sin(relative) * radiusX;
                float y = centerY - Mathf.Cos(relative) * radiusY;

                Color color = baseColor;
                color.a = 1f - state.Progress01(now);
                GUI.color = color;
                GUI.Label(new Rect(x - 24f, y - 18f, 48f, 36f), glyph, _indicatorStyle);
            }
        }

        /// <summary>매 프레임 문자열을 새로 만들지 않도록 상수로 고정.</summary>
        private const string IndicatorGlyph = "◆";

        /// <summary>§3.4 게이지 마커. §5.4 힌트(◆)와 모양으로도 구별된다(GAP-53 "최소 표시").</summary>
        private const string GaugeGlyph = "▲";
    }
}
