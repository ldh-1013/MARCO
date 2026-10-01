using System;
using System.Collections.Generic;
using Marco.Core.Util;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.1-0 · §6.2 매 라운드 <b>활성 밸브 조합</b>과 <b>필요 개방 수</b>.
    ///
    /// <para>
    /// <b>10-01 확정 규칙.</b> 배치 5개 중 매 라운드 <b>활성 3개</b> — 수영장 밸브(메인풀 B · 유아풀 E, 수중 밸브)는 항상 활성이고
    /// 남은 1자리를 A · C · D에서 서버가 무작위로 고른다. 탈출구(게이트)는 활성 3개가 <b>모두</b> 동시에 열려야 열린다 —
    /// 인원과 무관하다. 그 전 규칙(총원 3인 이하 요구 2 · 4인 이상 요구 3, 활성 = 요구 + 1을 5개 전체에서 무작위)은 폐기했다.
    /// </para>
    ///
    /// <para>
    /// 위치를 숨기지 않고 로비 브리핑에서 공개하는 이유는 §6.1-0에 있다 — <i>"이 게임의 정보 비대칭은 '소리'에서 나와야지
    /// '위치 암기'에서 나오면 안 되기 때문이다."</i>
    /// </para>
    /// </summary>
    public static class ValveRoster
    {
        /// <summary>§6.1-0 맵에 배치된 밸브 수. 인원과 무관하게 고정이다.</summary>
        public static int PlacedCount => ValveOccupancy.All.Length;

        /// <summary>10-01 매 라운드 활성 밸브 수 — 인원과 무관하게 3개.</summary>
        public const int ActiveCount = 3;

        /// <summary>
        /// 10-01 탈출구 개방에 필요한 동시 개방 수 = <b>활성 밸브 수</b>(3). 인원과 무관하다 —
        /// 예전 "총원 3인 이하면 2"는 폐기했다(3인 판에서 밸브 2개 + 1명 탈출로 즉시 승리가 났다).
        /// </summary>
        public const int RequiredOpenCount = ActiveCount;

        /// <summary>
        /// 수영장 밸브 — 항상 활성. 수중 밸브(<see cref="ValveOccupancy.IsUnderwater"/>)에서 유도한다(메인풀 B · 유아풀 E).
        /// 따로 적어 두면 수중 판정과 어긋난 채 남을 수 있다.
        /// </summary>
        public static IReadOnlyList<ValveId> PoolValves => Pool;

        private static readonly ValveId[] Pool = Array.FindAll(ValveOccupancy.All, ValveOccupancy.IsUnderwater);

        private static readonly ValveId[] NonPool = Array.FindAll(ValveOccupancy.All, id => !ValveOccupancy.IsUnderwater(id));

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
        /// 규칙을 단언한다 — 수영장 밸브 수 ≤ 활성 수 ≤ 배치 수, 필요 개방 = 활성 수. 표를 고치다 어긋나면 <b>여기서 터진다</b> —
        /// 조용히 밸런스가 무너지는 것보다 낫다.
        /// </summary>
        public static void AssertInvariant()
        {
            if (RequiredOpenCount != ActiveCount)
                throw new InvalidOperationException($"10-01 규칙 위반 — 필요 개방 {RequiredOpenCount} ≠ 활성 {ActiveCount}.");

            if (Pool.Length > ActiveCount)
                throw new InvalidOperationException($"10-01 규칙 위반 — 수영장 밸브 {Pool.Length}개 > 활성 {ActiveCount}개.");

            if (ActiveCount > PlacedCount)
                throw new InvalidOperationException($"§6.1-0 위반 — 활성 {ActiveCount} > 배치 {PlacedCount}.");
        }

        /// <summary>
        /// 이번 라운드 활성 밸브를 고른다 — <b>수영장 밸브(B · E) 전부 + 나머지 자리를 A · C · D에서 무작위</b>.
        /// <b>서버만 호출하고 결과를 네트워크로 알린다</b> — 클라이언트가 같은 시드로 다시 굴리게 만들지 않는다(서버 권위, GAP-24).
        ///
        /// <para>
        /// 시드를 주입받는 이유는 <c>UnityEngine.Random</c>이 Core에서 금지이고(§15.2), 테스트에서 조합을 고정할 수 없기 때문이다.
        /// 서버는 라운드 번호로 시드를 만들어 매 라운드(재경기 포함) 다시 고른다.
        /// </para>
        ///
        /// <para>반환 순서: 수영장 밸브(배치 순서) 다음 무작위 자리.</para>
        /// </summary>
        public static List<ValveId> SelectActive(int seed)
        {
            AssertInvariant();

            var active = new List<ValveId>(ActiveCount);
            active.AddRange(Pool);
            active.AddRange(new DeterministicRandom(seed).Choose(NonPool, ActiveCount - Pool.Length));
            return active;
        }
    }
}
