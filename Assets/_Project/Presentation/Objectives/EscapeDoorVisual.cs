using Marco.Core.Objectives;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Sound;
using UnityEngine;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// [10-02] 출구 문짝 표시 — 출구(<see cref="EscapePointTrigger"/>) 자식인 문짝 큐브에 붙는다. 문은 보이기만 한다(콜라이더 없음,
    /// 판정 위치는 출구 Transform 그대로).
    ///
    /// <list type="bullet">
    /// <item><b>닫힘</b>: 발광 없는 재질(씬에 붙은 그대로). 어둠 속에선 안 보이고 내 목소리 빛(Lit 표면)에 비치거나, 파문 윤곽에
    /// 문 테두리가 드러난다 — 윤곽 상자(<see cref="_outline"/>, 문틀까지 감싸는 빈 Transform)를 <see cref="RevealOutlineRegistry"/>에
    /// 등록해 <see cref="PulseWallRevealer"/>가 벽처럼 그린다. 문짝 상자를 그대로 쓰면 가장자리 선이 앞으로 더 나온 문틀 뒤에
    /// 가려져 보이지 않는다(10-02 실기).</item>
    /// <item><b>열림</b>: 약한 초록 자체 발광 재질(<see cref="_openMaterial"/>, 발광색 = <see cref="EscapeDoorLook.OpenEmission"/>)로 바꾼다.</item>
    /// </list>
    ///
    /// <para>
    /// <b>새 로직 없음</b>: 게이트 상태는 <see cref="RoundCoordinator.IsEscapeGateOpen"/>(네트워크면 서버가 SyncVar로 알린 값 —
    /// 늦게 들어온 클라이언트도 같은 값)를 읽기만 한다. <see cref="ValveVisualIndicator"/>처럼 매 프레임 계산하고 바뀔 때만 반영한다.
    /// </para>
    ///
    /// <para>
    /// <b>재질을 바꾸는 이유</b>(발광색만 바꾸지 않고): URP는 발광색이 검정인 재질의 <c>_EMISSION</c> 키워드를 임포트 때 끈다. 그러면
    /// 빌드에서 발광 변형이 빠져 런타임에 색을 넣어도 빛나지 않는다. 그래서 발광 재질을 따로 둔다(문 2개 · 재질 2개 — 공유, 사본 없음).
    /// </para>
    /// </summary>
    [RequireComponent(typeof(MeshRenderer))]
    public sealed class EscapeDoorVisual : MonoBehaviour
    {
        /// <summary>RoundCoordinator를 못 찾았을 때 다시 찾는 간격(초) — 맵이 시스템 씬보다 먼저 로드될 수 있다(§15.1).</summary>
        private const float FindRetrySeconds = 1f;

        [Tooltip("게이트가 열렸을 때 문짝 재질 — URP Lit, 발광 = EscapeDoorLook.OpenEmission. 닫힘 재질은 렌더러에 붙은 재질이다.")]
        [SerializeField] private Material _openMaterial;

        [Tooltip("파문 윤곽 상자 — 단위 큐브 기준 크기의 빈 Transform(문틀까지 감싼다). 비면 문짝 자신.")]
        [SerializeField] private Transform _outline;

        private MeshRenderer _renderer;
        private Material _closedMaterial;
        private RoundCoordinator _round;
        private float _nextFindTime;
        private bool _hasLast;
        private bool _lastOpen;

        /// <summary>지금 문짝이 보여 주는 발광색(진단용) — <see cref="EscapeDoorLook.PanelEmission"/>.</summary>
        public Color CurrentEmission => EscapeDoorLook.PanelEmission(_hasLast && _lastOpen);

        private void Awake()
        {
            _renderer = GetComponent<MeshRenderer>();
            _closedMaterial = _renderer.sharedMaterial;
            Apply(false);
        }

        private Transform OutlineBox => _outline != null ? _outline : transform;

        private void OnEnable() => RevealOutlineRegistry.Register(OutlineBox);

        private void OnDisable() => RevealOutlineRegistry.Unregister(OutlineBox);

        private void Update() => Apply(IsGateOpen());

        private bool IsGateOpen()
        {
            if (_round == null && Time.unscaledTime >= _nextFindTime)
            {
                _nextFindTime = Time.unscaledTime + FindRetrySeconds;
                _round = FindAnyObjectByType<RoundCoordinator>();
            }

            return _round != null && _round.IsEscapeGateOpen;
        }

        private void Apply(bool open)
        {
            if (_hasLast && _lastOpen == open)
                return;

            _renderer.sharedMaterial = open && _openMaterial != null ? _openMaterial : _closedMaterial;
            _lastOpen = open;
            _hasLast = true;
        }
    }
}
