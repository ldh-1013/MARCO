using UnityEngine;
using Marco.Core.Objectives;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// [검증용 최소 시각 표시 — 정식 UI/셰이더 아님]
    /// 밸브 큐브의 색을 상태에 따라 바꿔, 서버 권위 동기화(스프린트 10)를 눈으로 확인하기 쉽게 한다.
    ///
    /// - 닫힘(Closed): <see cref="_closedColor"/>(기본 회색)
    /// - 회전 중(Rotating): 진행률에 비례해 회색 → 노랑 보간
    /// - <b>감쇠 중(Closed + 진행도 잔존): 회색 → 주황 보간</b> ★
    /// - 열림(Open): <see cref="_openColor"/>(기본 초록) 고정
    /// - 역류 중(Reflowing): 초록 ↔ 적색 점멸
    ///
    /// <para>
    /// <b>★ 감쇠 중 색 구분은 연출이 아니라 정보다</b>(§12.4). 회전 중과 구분되지 않으면
    /// "지금 뺄까, 1초 더 돌릴까"라는 v0.4의 핵심 판단 자체가 불가능해진다.
    /// 생략 금지 항목이다.
    /// </para>
    ///
    /// **새 로직 없음**: 상태·진행률은 <see cref="ValveBehaviour.State"/>·<see cref="ValveBehaviour.Progress01"/>
    /// (네트워크면 서버 확정값, 아니면 로컬값 — <c>ValveBehaviour</c>가 이미 소스를 고른다)를 읽기만 한다.
    /// 그래서 한쪽에서 밸브를 돌리면 SyncVar로 전파된 값이 양쪽 화면에서 그대로 색으로 나타난다.
    ///
    /// 밸브는 3개뿐이라 <see cref="Renderer.material"/>(인스턴스 사본)을 써도 비용 문제가 없다
    /// (T8의 성능 이슈는 동시 30개 링 때문이었고, 밸브와는 무관).
    /// </summary>
    [RequireComponent(typeof(ValveBehaviour), typeof(MeshRenderer))]
    public sealed class ValveVisualIndicator : MonoBehaviour
    {
        [SerializeField] private Color _closedColor = new Color(0.5f, 0.5f, 0.5f, 1f);

        [Tooltip("§12.4 회전 중 진행도 색.")]
        [SerializeField] private Color _progressColor = new Color(1f, 0.9f, 0.1f, 1f);

        [Tooltip("★ §12.4 감쇠 중 진행도 색. 회전 중과 반드시 달라야 한다 — 이건 정보다.")]
        [SerializeField] private Color _decayColor = new Color(1f, 0.45f, 0.1f, 1f);

        [SerializeField] private Color _openColor = new Color(0.2f, 0.8f, 0.3f, 1f);

        [Tooltip("§6.1-2 역류 중 점멸 색.")]
        [SerializeField] private Color _reflowColor = new Color(0.94f, 0.42f, 0.30f, 1f);

        [Tooltip("§6.1-2 역류 점멸 주기(초).")]
        [SerializeField] private float _reflowBlinkSeconds = 0.5f;

        [Tooltip("§6.1-0 비활성 밸브 색(잠금 표시).")]
        [SerializeField] private Color _inactiveColor = new Color(0.22f, 0.22f, 0.26f, 1f);

        private ValveBehaviour _valve;
        private MeshRenderer _renderer;
        private Material _material;    // 인스턴스 사본(밸브별 독립 색)
        private bool _hasLastColor;
        private Color _lastColor;

        private void Awake()
        {
            _valve = GetComponent<ValveBehaviour>();
            _renderer = GetComponent<MeshRenderer>();
            _material = _renderer.material; // 접근 시 인스턴스화 → 다른 밸브와 색이 섞이지 않는다
            Apply(CurrentColor());
        }

        private void Update()
        {
            // 회전 중에는 진행률이 매 프레임 바뀌므로 매 프레임 계산하되, 색이 실제로
            // 바뀔 때만 머티리얼에 반영해 불필요한 세팅을 줄인다.
            Apply(CurrentColor());
        }

        private Color CurrentColor()
        {
            // §6.1-0 비활성 밸브는 잠금 표시 — 상호작용이 거부되므로 진행도를 보일 이유가 없다.
            if (!_valve.IsActiveThisRound)
                return _inactiveColor;

            switch (_valve.State)
            {
                case ValveState.Open:
                    return _openColor;

                case ValveState.Rotating:
                    return Color.Lerp(_closedColor, _progressColor, Mathf.Clamp01(_valve.Progress01));

                case ValveState.Reflowing:
                    // §6.1-2 역류 경고 — 점멸로 "되돌릴 수 있는 창"임을 알린다.
                    return Mathf.Repeat(Time.time, _reflowBlinkSeconds * 2f) < _reflowBlinkSeconds
                        ? _reflowColor
                        : _openColor;

                default:
                    // ★ 감쇠 중이면 **회전 중과 다른 색**으로 같은 진행도를 그린다(§12.4).
                    //   진행도 숫자만으로는 "돌고 있다"와 "깎이고 있다"를 구분할 수 없다.
                    if (_valve.Progress01 > 0f)
                    {
                        Color tint = _valve.IsDecaying ? _decayColor : _progressColor;
                        return Color.Lerp(_closedColor, tint, Mathf.Clamp01(_valve.Progress01));
                    }

                    return _closedColor;
            }
        }

        private void Apply(Color color)
        {
            if (_hasLastColor && _lastColor == color)
                return;

            _material.color = color;
            _lastColor = color;
            _hasLastColor = true;
        }

        private void OnDestroy()
        {
            // material 게터가 만든 인스턴스 사본을 정리한다(에디터 반복 Play 시 누수 방지).
            if (_material != null)
                Destroy(_material);
        }
    }
}
