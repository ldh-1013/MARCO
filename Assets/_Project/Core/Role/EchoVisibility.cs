namespace Marco.Core.Role
{
    /// <summary>
    /// §3.2 메아리를 생존자에게 숨기는 규칙(GAP-63 해소).
    ///
    /// **왜 숨기는가**: 메아리는 §1상 "유령 상태로 노크 능력을 통해 계속 참여"하는 활성 역할이고,
    /// §3.2 노크의 의의는 **은밀한 유인**이다. 생존자에게 메아리 몸체가 보이면 술래가 노크 발신자를
    /// 눈으로 찾아버려 그 설계가 무너진다("도망자를 도와 술래를 유인하거나 … 정보를 흘릴 수도 있음"이
    /// 성립하려면 발신자가 보이지 않아야 한다).
    ///
    /// **순수 로컬 판정이다.** "내가 저 pawn을 그려야 하는가"는 각 피어가 자기 화면에 대해 독립적으로
    /// 결정하므로 네트워크 동기화가 필요 없다 — 역할은 이미 <c>RoleNetworkSync</c>/<c>TagNetworkSync</c>가
    /// 전 피어에 전파하고 있고, 이 규칙은 그 결과를 읽기만 한다.
    ///
    /// **메아리 상호 가시성은 가정이다**: 기획서가 "메아리끼리 서로 보이는가"를 명시하지 않는다.
    /// §3.2가 메아리끼리는 별도 사망자 채널로 자유 대화한다고 규정하므로 서로를 인지하는 관계로
    /// 보고 **보이도록** 구현했다(GAP-63에 가정으로 기록).
    /// </summary>
    public static class EchoVisibility
    {
        /// <summary>
        /// 이 뷰어가 대상 pawn의 몸체를 렌더링해야 하는가.
        /// </summary>
        /// <param name="viewerRole">화면 주인(로컬 플레이어)의 현재 역할.</param>
        /// <param name="targetRole">그려질지 판정할 pawn의 현재 역할.</param>
        /// <param name="isSelf">대상이 뷰어 자신인가(자기 pawn은 이 규칙이 건드리지 않는다).</param>
        public static bool ShouldRender(RoleType viewerRole, RoleType targetRole, bool isSelf)
        {
            // 자기 자신은 언제나 원래 상태 그대로 둔다 — 1인칭 카메라가 캡슐 안에 있어
            // 어차피 보이지 않고, 여기서 끄면 그림자·3인칭 전환 같은 후속 작업이 꼬인다.
            if (isSelf)
                return true;

            // 생존자(러너·술래)는 평소대로 서로 보인다.
            if (targetRole != RoleType.Echo)
                return true;

            // 메아리끼리는 보인다(위 가정 — §3.2 사망자 채널로 함께 움직이는 관계).
            if (viewerRole == RoleType.Echo)
                return true;

            // 생존자 시점에서 본 메아리 — 숨긴다.
            return false;
        }
    }
}
