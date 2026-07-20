using UnityEngine;
using Marco.Core.Role;

namespace Marco.Presentation.Tagging
{
    /// <summary>
    /// §3.1 태그 판정 규칙의 순수 구현. 기획서에 명시된 것만 담는다 —
    /// 쿨다운·재태그 방지 같은 규칙은 만들지 않았다(아래 설명 참조).
    ///
    /// §3.1 원문: "접촉 트리거, 반경 1.2m, 1회 접촉 즉시 확정"
    /// §3.1 사망/탈락 처리: "태그 1회 → 메아리로 즉시 전환"
    /// §3.1 메아리 열: "태그 불가(비활성 콜라이더)"
    ///
    /// **재태그 쿨다운이 필요 없는 이유**: 태그당한 즉시 메아리가 되고, 메아리는
    /// 태그 불가다. 즉 같은 대상을 두 번 태그하는 상황이 구조적으로 성립하지 않는다.
    /// 기획서에 쿨다운 언급이 없는 것도 이 때문으로 보이며, 없는 규칙을 만들지 않았다.
    /// </summary>
    public static class TagRules
    {
        /// <summary>§3.1 접촉 반경. 캐릭터 콜라이더 0.35m와는 별개 수치(§8 명시).</summary>
        public const float TagRadiusMeters = 1.2f;

        /// <summary>
        /// §3.1 "접촉 트리거, 반경 1.2m". 반경 1.2m 구체 트리거와 기하적으로 동일한
        /// 거리 판정으로 구현했다 — 스프린트 5·6의 근접 판정 방식과 일관되고,
        /// "1회 접촉 즉시 확정" 의미론도 그대로 유지된다.
        /// </summary>
        public static bool IsWithinTagRange(Vector3 seekerPosition, Vector3 targetPosition)
        {
            return Vector3.Distance(seekerPosition, targetPosition) <= TagRadiusMeters;
        }

        /// <summary>
        /// 역할 조건만 본다(거리·라운드 상태는 호출자 책임).
        /// 술래만 태그할 수 있고, 도망자만 태그 대상이다 — 메아리는 §3.1상 태그 불가.
        /// </summary>
        public static bool CanTag(RoleType taggerRole, RoleType targetRole)
        {
            return taggerRole == RoleType.Seeker && targetRole == RoleType.Runner;
        }
    }
}
