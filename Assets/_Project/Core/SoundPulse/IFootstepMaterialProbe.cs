using UnityEngine;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §5.9 "지금 이 지점의 바닥이 무슨 재질인가"에 답하는 계약.
    ///
    /// <see cref="IOcclusionProbe"/>와 정확히 같은 역할 분담이다 — Unity Physics를 쓰는 구현체는
    /// Presentation에 두고(<c>PhysicsFootstepMaterialProbe</c>), 판정 규칙
    /// (<see cref="FootstepMaterialRules"/>)은 Core에 남겨 EditMode로 고정한다.
    /// 등록은 <see cref="PulseNetworkRegistry"/>가 중계한다(§15.2상 Net은 Presentation을 못 본다).
    ///
    /// **새 시스템이 아니다.** 발생 시점에 "밟고 있는 바닥"만 알면 되므로 상태도 수명주기도 없다.
    /// </summary>
    public interface IFootstepMaterialProbe
    {
        /// <summary>
        /// 이 월드 좌표에서 발을 딛는 바닥의 재질. 판정할 수 없으면
        /// <see cref="FootstepMaterialRules.Default"/>(콘크리트 ×1.0)를 돌려준다 —
        /// 미배선 맵에서 발소리가 사라지거나 증폭되지 않도록.
        /// </summary>
        FootstepMaterial Sample(Vector3 worldPosition);
    }
}
