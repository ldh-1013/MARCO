using Marco.Core.Breath;
using Marco.Core.Role;
using Marco.Core.Water;
using UnityEngine;

namespace Marco.Core.Locomotion
{
    /// <summary>
    /// §4.3 잠수 진입 조건과 §5.9-1 숨 상태 판정을 **한 곳에** 모은 규칙.
    ///
    /// <para>
    /// <b>왜 이 파일이 생겼나.</b> 잠수 진입 판정은 <c>LocomotionSimulator.Tick</c> 안에
    /// 인라인으로 박혀 있었다. 물 볼륨이 들어오면서 <b>서버도</b> 같은 판정을 해야 하는데
    /// (§5.9-1 숨 게이지가 서버 권위이므로), 조건식을 복사하면 두 곳이 어긋난다.
    /// 그래서 식을 빼내고 양쪽이 <b>같은 함수</b>를 부른다 — 판정은 여전히 한 곳이다.
    /// </para>
    ///
    /// <para>
    /// <b>서버 권위(GAP-24) 배분.</b>
    /// <list type="bullet">
    /// <item><c>diveHeld</c> — 클라이언트만 아는 "버튼을 누르고 있다"는 사실. 주장으로 받는다.</item>
    /// <item><c>bodyInWater</c> — 서버가 동기화된 위치를 §10.1 수면 지오메트리에 대고 재계산한다.</item>
    /// <item><c>canSubmerge</c> — 서버가 소유한 숨 게이지가 답한다(§5.9-1 강제 부상).</item>
    /// </list>
    /// 즉 클라이언트는 마른 땅에서 잠수할 수도, 게이지 0으로 잠수를 유지할 수도 없다.
    /// </para>
    /// </summary>
    public static class DiveRules
    {
        /// <summary>
        /// 서 있을 때 머리(카메라) 높이. <c>Assets/_Project/Prefabs/Player.prefab</c>의
        /// 카메라 <c>localPosition.y = 1.62</c>와 같은 값이며, 이쪽이 정본이다.
        ///
        /// **기획서에는 눈높이 수치가 없다**(§4.2는 콜라이더 반경 0.35만 정한다).
        /// 프리팹에 이미 들어 있던 값을 그대로 승격시킨 것이므로 창작이 아니지만,
        /// 프리팹과 이 상수가 갈라지면 서버와 클라이언트의 머리 높이가 달라진다 — **GAP-81**.
        /// (블록 1-D에서 GAP-73이 "상호작용 유효 범위 미정의"로 확정되며 번호가 재배정됐다.)
        /// </summary>
        public const float StandingHeadHeight = 1.62f;

        /// <summary>
        /// 잠수 중 머리 높이(발 기준). §5.9-1이 잠수를 *"카메라(머리)가 수면 아래"* 로
        /// 정의하므로, 잠수는 <b>발을 내리는 게 아니라 머리를 내리는 것</b>으로 구현한다.
        ///
        /// <para>
        /// <b>왜 발을 내리지 않았나</b>: 발을 내리면 부력·수중 이동·풀 바닥 콜라이더가
        /// 전부 딸려 오고, 얕은 유아풀에서는 바닥에 막혀 잠수가 <b>조용히 실패</b>한다.
        /// 머리만 내리면 <c>LocomotionSimulator</c>가 이미 잠수 중 속도를 0으로 두는 것과도
        /// 일치한다(제자리 잠수 = 홀드).
        /// </para>
        ///
        /// <para>
        /// <b>이 값은 프로토타입 잠정값이다 — GAP-74.</b> 기획서에 잠수 자세·시야 높이
        /// 수치가 없다. 0.5m는 "엎드려 잠긴 자세"를 가정한 값이며, 이 값이 곧
        /// <b>잠수가 가능한 최소 수심</b>이다 — <see cref="MinDivableDepth"/> 참조.
        /// </para>
        /// </summary>
        public const float SubmergedHeadHeight = 0.5f;

        /// <summary>
        /// 잠수가 성립하는 최소 수심.
        ///
        /// <para>
        /// 바닥에 선 플레이어의 발은 <c>수면 − 수심</c>에 있고, 잠수 중 머리는 거기서
        /// <see cref="SubmergedHeadHeight"/>만큼 위다. 머리가 수면 아래이려면
        /// <c>수면 − 수심 + 0.5 &lt; 수면</c>, 즉 <b>수심 &gt; 0.5m</b>다.
        /// 그래서 이 값은 <see cref="SubmergedHeadHeight"/>와 같으며 따로 적지 않고 가리킨다.
        /// </para>
        ///
        /// <para>
        /// 반대쪽 경계도 여기서 읽힌다 — 수심이 <see cref="StandingHeadHeight"/> 이상이면
        /// <b>서 있기만 해도</b> 머리가 잠긴다. 그런 물(메인 풀 3.5m)에서는 바닥에 서는 것이
        /// 아니라 떠 있는 것이 정상이며, 수면 상태는 발이 수면 근처일 때 성립한다.
        /// 즉 "서서 잠수로 잠긴다"가 의미를 갖는 구간은 <b>0.5m &lt; 수심 &lt; 1.62m</b>이고,
        /// 유아풀이 바로 그 구간에 있어야 한다 — 그 수심이 기획서에 없다(GAP-75).
        /// </para>
        ///
        /// 배치 검증(블록 1-C)이 수중 밸브·배수구 지점의 수심을 이 값으로 판정한다.
        /// </summary>
        public static float MinDivableDepth => SubmergedHeadHeight;

        /// <summary>
        /// §4.3 "잠수(Left Ctrl 홀드, 수면 위에서만)" + §5.9-1 "강제 부상".
        /// <c>LocomotionSimulator</c>와 서버가 같이 쓰는 <b>유일한</b> 잠수 판정.
        /// </summary>
        public static bool IsDiving(RoleType role, bool diveHeld, bool bodyInWater, bool canSubmerge)
        {
            return CanDive(role) && diveHeld && bodyInWater && canSubmerge;
        }

        /// <summary>
        /// §3.1 "특수 능력" 행 — 잠수는 <b>도망자</b>의 능력이다(술래는 외침, 메아리는 노크).
        ///
        /// <para>
        /// <b>[블록 5에서 추가된 역할 조건.]</b> 이전 판정식에는 역할이 없어 술래도 물에서 Ctrl을 누르면
        /// 잠수했다. 그러면 ① §3.6 "잠수 중 미발동"에 걸려 <b>물속 대기 술래의 캠핑 방지가 꺼지고</b>
        /// ② §6.5-3 "술래에게 잠수를 주지 않는다"가 정면으로 깨진다 — 최후 생존자 페이즈가 술래의
        /// 일방적 승리가 된다. 판정식이 한 곳이라 서버·클라이언트가 함께 막힌다.
        /// </para>
        /// </summary>
        public static bool CanDive(RoleType role) => role == RoleType.Runner;

        /// <summary>
        /// §6.5-3 <b>잠수 중인 몸의 접촉 위치</b> — 태그 판정이 쓴다. 잠수 중이면 그 지점의 바닥,
        /// 아니면 발 위치 그대로.
        ///
        /// <para>
        /// <b>왜 필요한가.</b> 이 파일의 잠수 모델은 머리(카메라)만 내리고 발(<c>transform</c>)은
        /// 수면 근처에 남긴다. 그런데 §6.5-3의 공정성 논거 전체가 <i>"수심 3.5m, 태그 반경
        /// 1.2m이므로 술래는 수면에서 배수구에 닿을 수 없다 … 술래가 태그할 수 있는 순간은
        /// (부상할) 그때뿐이다"</i> — 즉 <b>잠수자가 실제로 바닥에 있다</b>는 전제다. 발 위치로
        /// 태그를 재면 술래가 수면에 선 채 바로 위에서 잠수자를 잡을 수 있어 그 절이 통째로 무너진다.
        /// </para>
        ///
        /// <para>
        /// <b>잠수 중 몸의 수직 위치는 기획서에 없다 — GAP-89.</b> 수중 작업(밸브 B·E, 배수구)이
        /// 바닥에서 이뤄지고 기획서가 하강·상승을 진입·부상 시간으로 추상화했으므로(§6.1-1 ·
        /// §6.5-2), "잠수 = 바닥에 있다"로 두는 것이 그 추상화와 맞는 잠정 해석이다.
        /// 유아풀(얕음)에서는 서 있는 발이 이미 바닥이라 결과가 달라지지 않는다.
        /// </para>
        /// </summary>
        public static Vector3 ContactPosition(Vector3 feet, in WaterSample water, bool submerged)
        {
            if (!submerged || !water.BodyInWater)
                return feet;

            return new Vector3(feet.x, Mathf.Min(feet.y, water.BedY), feet.z);
        }

        /// <summary>
        /// 이 자리에서 잠수하면 <b>실제로 머리가 수면 아래로 가는가</b> — 수중 작업(밸브 B·E, 배수구)의 자격(GAP-88 해소).
        ///
        /// <para>
        /// 새 판정이 아니다 — 잠수 상태 판정이 이미 쓰는 두 식의 조합이다: <see cref="IsDiving"/>(도망자 · 물 안 ·
        /// 숨 있음)에 "잠수 키를 눌렀다면"을 넣고, <see cref="ZoneOf"/>로 그 자세의 머리 높이가 수면 아래인지 본다.
        /// 덱 위(물 밖)도, 경사로 윗부분(발 &gt; −0.5)도 여기서 걸린다 — 수중 대상의 거리는 수평으로 재므로(GAP-88)
        /// 이 조건이 없으면 물 밖에서 수중 밸브를 돌려 숨을 한 번도 쓰지 않을 수 있었다.
        /// </para>
        /// </summary>
        public static bool CanSubmergeHere(RoleType role, in WaterSample water, float feetY, bool canSubmerge)
        {
            return IsDiving(role, true, water.BodyInWater, canSubmerge)
                   && ZoneOf(water, feetY, diving: true) == BreathZone.Submerged;
        }

        /// <summary>발 기준 머리 높이. 잠수 중이면 <see cref="SubmergedHeadHeight"/>.</summary>
        public static float HeadHeight(bool diving)
        {
            return diving ? SubmergedHeadHeight : StandingHeadHeight;
        }

        /// <summary>
        /// §5.9-1 숨 상태를 <b>지오메트리 관측</b>으로 판정한다. 서버가 쓰는 형태다.
        ///
        /// <para>
        /// 규칙 자체는 <see cref="BreathConfig.ZoneOf(MovementState, bool)"/>가 소유하고
        /// 여기서는 <c>MovementState</c>로 번역만 한다 — 상태표를 두 벌 쓰지 않는다.
        /// </para>
        ///
        /// <para>
        /// 머리가 수면 아래인지는 <b>수심으로 재지 않고 실제 머리 높이로</b> 잰다.
        /// 물이 <see cref="MinDivableDepth"/>보다 얕으면 잠수 키를 눌러도 머리가 수면 위에
        /// 남아 <c>Surface</c>가 되는데, 그것이 "얕은 물에서는 잠길 수 없다"는 사실의
        /// 올바른 표현이다.
        /// </para>
        /// </summary>
        public static BreathZone ZoneOf(in WaterSample water, float feetY, bool diving)
        {
            if (!water.BodyInWater)
                return BreathZone.OutOfWater;

            bool headBelowSurface = feetY + HeadHeight(diving) < water.SurfaceY;
            return BreathConfig.ZoneOf(
                headBelowSurface ? MovementState.Diving : MovementState.Walk,
                isOnWaterSurface: true);
        }
    }
}
