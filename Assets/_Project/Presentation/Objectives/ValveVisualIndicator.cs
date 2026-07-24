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
    /// - 열림(Open): <see cref="_openColor"/>(기본 초록) 고정
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
        [SerializeField] private Color _progressColor = new Color(1f, 0.9f, 0.1f, 1f);
        [SerializeField] private Color _openColor = new Color(0.2f, 0.8f, 0.3f, 1f);

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
            switch (_valve.State)
            {
                case ValveState.Open:
                    return _openColor;
                case ValveState.Rotating:
                    return Color.Lerp(_closedColor, _progressColor, Mathf.Clamp01(_valve.Progress01));
                default:
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
