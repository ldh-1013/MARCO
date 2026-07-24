using UnityEngine;
using Marco.Core.Role;

namespace Marco.Core.Tagging
{
    /// <summary>
    /// 서버 권위 태그 검증기(스프린트 11). 서버가 클라이언트의 태그 요청을 <b>무조건
    /// 신뢰하지 않고</b> §3.1 규칙으로 재검증하는 순수 로직이다.
    ///
    /// **핵심(서버 권위)**: 클라이언트가 "태그했다"고 주장해도, 서버가 이 검증을 통과하지
    /// 못하면 태그는 확정되지 않는다. 거리(1.2m)·역할(술래→도망자)·이미 태그됨 여부를
    /// 전부 서버 측 값으로 다시 본다. 이 성질을 <c>ServerTagDriverTests</c>가 고정한다.
    ///
    /// **기존 규칙 재사용**: 새 규칙을 만들지 않는다 — 거리·역할 판정은 전부
    /// <see cref="TagRules"/>(스프린트 7에서 Presentation에 있던 것을 스프린트 11에 Core로
    /// 이동)를 그대로 호출한다. 밸브의 <c>ServerValveDriver</c>가 Core <c>Valve</c>를
    /// 재사용한 것과 같은 구조다.
    ///
    /// FishNet도 MonoBehaviour도 모른다 — EditMode 테스트 가능. 상태가 없으므로(태그됨
    /// 여부는 대상의 SyncVar가 소유) 순수 정적 검증만 제공한다.
    /// </summary>
    public static class ServerTagDriver
    {
        /// <summary>
        /// 태그 요청이 유효한가. true면 서버가 대상을 태그 확정(→ Echo)해도 된다.
        /// </summary>
        /// <param name="seekerRole">술래가 주장한 역할(현재는 클라 주장 신뢰 — GAP-18).</param>
        /// <param name="targetRole">대상의 서버 측 역할.</param>
        /// <param name="targetAlreadyTagged">대상이 이미 태그됐는지(서버 SyncVar 기준).</param>
        /// <param name="seekerPosition">술래의 서버 측 위치(네트워크 동기화된 위치).</param>
        /// <param name="targetPosition">대상의 서버 측 위치.</param>
        public static bool Validate(RoleType seekerRole, RoleType targetRole, bool targetAlreadyTagged,
            Vector3 seekerPosition, Vector3 targetPosition)
        {
            // 이미 태그된 대상은 재태그 불가(§3.1 메아리는 태그 불가).
            if (targetAlreadyTagged)
                return false;

            // 술래→도망자만 유효(§3.1). 메아리·술래 대상 등은 여기서 걸린다.
            if (!TagRules.CanTag(seekerRole, targetRole))
                return false;

            // 거리 재검증(§3.1 1.2m). 클라이언트가 멀리서 보낸 요청을 서버가 거른다(§5.3).
            if (!TagRules.IsWithinTagRange(seekerPosition, targetPosition))
                return false;

            return true;
        }
    }
}
