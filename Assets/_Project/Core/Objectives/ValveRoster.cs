using System;
using System.Collections.Generic;
using Marco.Core.Util;
using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.1-0 · §6.2 매 라운드 <b>활성 밸브 조합</b>과 <b>요구 개방 수</b>.
    ///
    /// <para>
    /// <b>불변 조건: 활성 = 요구 + 1.</b> §9.3-1의 봉쇄 무력화 논리가 성립하는 유일한
    /// 조건이며 §6.2가 *"절대 깨지 않는다"* 고 못박았다. 여유가 0이면 술래가 하나만
    /// 지켜도 클리어가 막히고, 여유가 2 이상이면 압박이 소멸한다.
    /// 그래서 <b>활성 개수를 표에 적지 않고 요구 개수에서 유도한다</b> — 두 값을 따로
    /// 적으면 불변 조건이 깨진 채 남을 수 있다.
    /// </para>
    ///
    /// <para>
    /// 배치는 5개 고정이고 활성만 3~4로 조절한다. 위치를 숨기지 않고 로비 브리핑에서
    /// 공개하는 이유는 §6.1-0에 있다 — <i>"이 게임의 정보 비대칭은 '소리'에서 나와야지
    /// '위치 암기'에서 나오면 안 되기 때문이다."</i>
    /// </para>
    /// </summary>
    public static class ValveRoster
    {
        /// <summary>§6.1-0 맵에 배치된 밸브 수. 인원과 무관하게 고정이다.</summary>
        public static int PlacedCount => ValveOccupancy.All.Length;

        /// <summary>
        /// §6.2 요구 개방 수. <b>2인 구간만 2개, 그 외는 3개 고정</b>이다 —
        /// §6.2 *"요구 개방 3 고정(2인 구간 제외) — 병렬 작업 상한을 묶어 인원이 늘수록
        /// 클리어가 과도하게 쉬워지는 것을 막는다."*
        ///
        /// <para>
        /// 표(총원 2→2 / 3→2 / 4→3 / 5→3 / 6→3)를 그대로 옮긴 것이며, 경계는 총원 4다.
        /// </para>
        /// </summary>
        public static int RequiredOpenCount(int totalPlayers)
        {
            return totalPlayers <= 3 ? 2 : 3;
        }

        /// <summary>
        /// §6.2 활성 밸브 수. <b>요구 + 1</b>로 유도한다(위 불변 조건).
        /// 배치 수(5)를 넘지 않는다.
        /// </summary>
        public static int ActiveCount(int totalPlayers)
        {
            return Mathf.Min(RequiredOpenCount(totalPlayers) + 1, PlacedCount);
        }

        /// <summary>
        /// §6.2 [v0.4] 탈출 요구 인원 = <c>⌈도망자 수 ÷ 2⌉</c>.
        /// 도망자 2→1 / 3→2 / 4→2 / 5→3.
        ///
        /// <para>
        /// v0.3의 "탈출 2명"을 일반화한 것이며 도망자 3명 구간에서는 값이 같다.
        /// <b>2를 하드코딩하면 6인 게임(도망자 5, 요구 3)에서 틀린다.</b>
        /// </para>
        /// </summary>
        public static int EscapeRequirement(int runnerCount)
        {
            if (runnerCount <= 0)
                return 0;

            return (runnerCount + 1) / 2; // 정수 천장 나눗셈
        }

        /// <summary>
        /// §6.2 불변 조건을 단언한다. 표를 고치다 활성·요구가 어긋나면 <b>여기서 터진다</b> —
        /// 조용히 밸런스가 무너지는 것보다 낫다.
        /// </summary>
        public static void AssertInvariant(int totalPlayers)
        {
            int required = RequiredOpenCount(totalPlayers);
            int active = ActiveCount(totalPlayers);

            if (active != required + 1)
            {
                throw new InvalidOperationException(
                    $"§6.2 불변 조건 위반 — 총원 {totalPlayers}: 활성 {active} ≠ 요구 {required} + 1. " +
                    "§9.3-1 봉쇄 무력화 논리가 성립하는 유일한 조건이다.");
            }

            if (active > PlacedCount)
            {
                throw new InvalidOperationException(
                    $"§6.1-0 위반 — 총원 {totalPlayers}: 활성 {active} > 배치 {PlacedCount}.");
            }
        }

        /// <summary>
        /// 이번 라운드 활성 밸브를 고른다. <b>서버만 호출하고 결과를 네트워크로 알린다</b> —
        /// 클라이언트가 같은 시드로 다시 굴리게 만들지 않는다(서버 권위, GAP-24).
        ///
        /// <para>
        /// 시드를 주입받는 이유는 <c>UnityEngine.Random</c>이 Core에서 금지이고(§15.2),
        /// 테스트에서 조합을 고정할 수 없기 때문이다.
        /// </para>
        ///
        /// <para>
        /// 반환 순서는 난수 순서 그대로다 — 호출부가 정렬을 필요로 하면 직접 정렬한다.
        /// </para>
        /// </summary>
        public static List<ValveId> SelectActive(int totalPlayers, int seed)
        {
            AssertInvariant(totalPlayers);

            var random = new DeterministicRandom(seed);
            return random.Choose(ValveOccupancy.All, ActiveCount(totalPlayers));
        }
    }
}
