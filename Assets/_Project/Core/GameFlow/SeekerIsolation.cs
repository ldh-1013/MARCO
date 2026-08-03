using Marco.Core.Role;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// §10.1 "술래는 격리 공간에서 3초 후 별도 진입"의 순수 규칙(스프린트 24).
    ///
    /// **기획서 원문은 구역 구성표의 한 셀뿐이다**:
    /// <c>| 입구 로비 | 8×6m | 도망자 스폰 지점(술래는 격리 공간에서 3초 후 별도 진입) | 표준 | … |</c>
    /// 전 문서에서 "격리"는 이 한 곳에만 나온다.
    ///
    /// 여기서 확정적으로 도출되는 것은 **대상(술래)**과 **대기 시간(3초)** 둘뿐이다.
    /// 격리 공간의 위치·크기(구역표에 행 자체가 없음 — GAP-45), 카운트다운 표시 여부(GAP-46),
    /// "별도 진입"의 방식(GAP-47)은 명시가 없어 만들어내지 않았다.
    ///
    /// **3초의 기산점**: §15.4 RoleAssign의 3초 카운트다운(역할 추첨 연출)과는 **별개**로 본다 —
    /// 그 3초는 맵 로드 전 로비에서 흐르고, 이 3초는 맵에 배치된 뒤 흘러야 "격리 공간에서
    /// 3초 후 진입"이라는 문장이 성립하기 때문이다(GAP-48).
    /// </summary>
    public sealed class SeekerIsolation
    {
        /// <summary>§10.1 "3초 후 별도 진입".</summary>
        public const float IsolationSeconds = 3f;

        private float _remaining;
        private bool _active;

        /// <summary>이 역할이 격리 대상인가. §10.1은 술래만 언급한다.</summary>
        public static bool AppliesTo(RoleType role) => role == RoleType.Seeker;

        /// <summary>격리 대기 중인가(이동을 막아야 하는 상태).</summary>
        public bool IsHolding => _active && _remaining > 0f;

        /// <summary>남은 대기 시간(초). 대기 중이 아니면 0.</summary>
        public float Remaining => _active && _remaining > 0f ? _remaining : 0f;

        /// <summary>
        /// 라운드 시작 시 호출한다. 술래면 대기를 시작하고, 그 외 역할은 즉시 자유롭다.
        /// 리매치로 새 라운드가 시작될 때마다 다시 호출된다.
        /// </summary>
        public void Begin(RoleType role)
        {
            _active = AppliesTo(role);
            _remaining = _active ? IsolationSeconds : 0f;
        }

        /// <summary>대기 시간을 흘린다. 0 이하가 되면 <see cref="IsHolding"/>이 false가 된다.</summary>
        public void Tick(float deltaTime)
        {
            if (!_active || deltaTime <= 0f)
                return;

            _remaining -= deltaTime;
            if (_remaining < 0f)
                _remaining = 0f;
        }

        /// <summary>대기를 즉시 끝낸다(라운드 종료·맵 언로드 등).</summary>
        public void Clear()
        {
            _active = false;
            _remaining = 0f;
        }
    }
}
