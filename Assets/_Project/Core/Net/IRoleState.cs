using Marco.Core.Role;

namespace Marco.Core.Net
{
    /// <summary>
    /// 플레이어의 역할을 읽고, 네트워크가 확정한 역할 변경을 적용하는 최소 계약.
    ///
    /// **왜 필요한가**: 스프린트 11 서버 권위 태그에서, 태그가 확정되면 대상 러너의
    /// 역할이 Echo로 바뀌어야 한다(§3.1). 역할은 <c>FirstPersonController</c>(Presentation)가
    /// 들고 있고, 그 전환을 지시하는 쪽은 <c>TagNetworkSync</c>(Net)다. §15.2상 Net은
    /// Presentation을 참조하지 않으므로, 공통 조상 Core에 계약만 두어 연결한다 —
    /// <see cref="ILocalControlGate"/>·<see cref="IPlayerIdentity"/>와 같은 패턴.
    ///
    /// <see cref="ApplyRole"/>는 "네트워크가 확정한 결과의 반영"이라는 의미를 분명히 하려고
    /// 세터가 아니라 명령형 이름을 쓴다. 로컬 단독 실행에서는 아무도 호출하지 않으므로
    /// 구현체의 초기 역할(인스펙터 값)이 그대로 유지된다.
    /// </summary>
    public interface IRoleState
    {
        /// <summary>현재 역할.</summary>
        RoleType Role { get; }

        /// <summary>네트워크(또는 로컬 판정)가 확정한 역할을 적용한다. 예: 태그당하면 Echo.</summary>
        void ApplyRole(RoleType role);
    }
}
