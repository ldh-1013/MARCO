using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// E 홀드 상호작용(밸브·배수구)의 공통 규칙. <b>한 곳에만 둔다</b> — 밸브는 Presentation,
    /// 배수구 재검증은 Net에 있어 각자 상수를 두면 값이 갈라진다(더블체크 8).
    /// </summary>
    public static class InteractionRules
    {
        /// <summary>
        /// 상호작용 유효 거리(m). <b>기획서에 수치가 없다 — GAP-10(밸브)에서 정한 잠정값을
        /// GAP-73(E 홀드 전반)으로 넓혀 배수구에도 그대로 쓴다.</b>
        ///
        /// <para>
        /// GAP-10 근거 — §14.1 태그 판정 1.2m(접촉급)보다는 넉넉해야 밸브 앞에 서서 누를 수 있고,
        /// 캐릭터 콜라이더 0.35m + 밸브 큐브 0.6m를 감안하면 2.5m면 "바로 옆"에 해당한다.
        /// 플레이테스트 조정 대상이다.
        /// </para>
        /// </summary>
        public const float RangeMeters = 2.5f;

        /// <summary>
        /// 상호작용 거리. <b>수중 대상(§6.1 밸브 B·E, §6.5-2 배수구)은 수평 거리로 잰다 — GAP-88.</b>
        ///
        /// <para>
        /// 잠수 모델(블록 1-A③ <c>DiveRules</c>)은 발이 아니라 <b>머리를 내린다</b> — 플레이어는
        /// 수직으로 헤엄쳐 내려가지 않는다. 그런데 배수구는 수심 3.5m 바닥에 있어 3D 거리로 재면
        /// 2.5m 안에 영영 들어오지 못한다. 기획서는 그 하강·상승을 <b>진입·부상 시간(초)</b>으로
        /// 추상화해 두었으므로(§6.1-1 · §6.5-2 총 점유 표), 거리에서는 수직 성분을 빼는 것이
        /// 그 추상화와 맞다. 대신 "물속에서 한다"는 조건은 <b>잠수 중 여부</b>로 따로 건다
        /// (<see cref="DrainHatch.CanWork"/>).
        /// </para>
        /// </summary>
        public static float DistanceTo(Vector3 actor, Vector3 target, bool underwaterTarget)
        {
            if (!underwaterTarget)
                return Vector3.Distance(actor, target);

            float dx = actor.x - target.x;
            float dz = actor.z - target.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary><see cref="DistanceTo"/> ≤ <see cref="RangeMeters"/>. 경계값은 포함한다.</summary>
        public static bool InRange(Vector3 actor, Vector3 target, bool underwaterTarget)
        {
            return DistanceTo(actor, target, underwaterTarget) <= RangeMeters;
        }
    }
}
