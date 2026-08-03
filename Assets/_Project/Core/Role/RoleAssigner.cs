namespace Marco.Core.Role
{
    /// <summary>
    /// §6.2 인원별 역할 배정의 순수 규칙(스프린트 13). 기획서에 명시된 표를 그대로 옮긴 것이며
    /// 임의로 만든 규칙이 없다. FishNet도 UnityEngine도 모른다 — EditMode 테스트 가능.
    ///
    /// **기획서 근거(§6.2 인원별 밸런스 표 + 용어집)**:
    /// <code>
    /// | 인원 | 도망자 |          용어집: "리스너(Seeker) | 술래 역할. 1인"
    /// |  2   |   1    |                   "도망자(Runner) | ... 3~5인"
    /// |  3   |   2    |          §1 게임 개요: 인원 최소 2 / 권장 4~5 / 최대 6
    /// |  4   |   3    | ← MVP              (v1.x: 8~10인일 때만 술래 2인)
    /// |  5   |   4    |
    /// |  6   |   5    |
    /// </code>
    /// 전 행에서 <c>인원 - 도망자 = 1</c>이 성립하므로, 규칙은 <b>"술래 1인 고정, 나머지 전원
    /// 도망자"</b>로 표에서 직접 도출된다(<see cref="SeekerCount"/>). 술래 2인은 v1.x 8~10인
    /// 구간 전용이라 MVP 범위 밖이다.
    ///
    /// **메아리(Echo)는 초기 배정 대상이 아니다** — §3.1상 Echo는 "태그당해 탈락한 도망자"이므로
    /// 게임플레이 이벤트(태그, 스프린트 11)로만 도달한다. 이 클래스는 초기 배정만 담당한다.
    /// </summary>
    public static class RoleAssigner
    {
        /// <summary>
        /// 술래 수. 용어집 "리스너(Seeker) | 술래 역할. 1인" + §6.2 표 전 행의 (인원 - 도망자 = 1).
        /// v1.x 8~10인 구간의 술래 2인은 MVP 범위 밖이다(§1).
        /// </summary>
        public const int SeekerCount = 1;

        /// <summary>§1 "인원 최소 2". 이보다 적으면 라운드가 성립하지 않아 배정하지 않는다(GAP-21).</summary>
        public const int MinimumPlayers = 2;

        /// <summary>§1 "최대 6"(v1.x 8~10인은 범위 밖). 초과 인원도 술래 1인 규칙을 그대로 적용한다.</summary>
        public const int MaximumPlayers = 6;

        /// <summary>
        /// 역할을 배정할 수 있는 인원인가. §1 최소 2인 미만이면 false —
        /// 이때 호출자는 배정을 건너뛰고 로컬 기본값(러너)을 유지해야 한다(로컬 단독 실행 폴백).
        /// </summary>
        public static bool CanAssign(int playerCount) => playerCount >= MinimumPlayers;

        /// <summary>배정될 술래 수. 배정 불가 인원이면 0.</summary>
        public static int SeekersFor(int playerCount) => CanAssign(playerCount) ? SeekerCount : 0;

        /// <summary>
        /// 배정될 도망자 수 = 인원 - 술래 수(§6.2 표). 배정 불가 인원이면 전원 도망자로 남는다
        /// (로컬 단독 실행에서 1명이 러너 기본값을 유지하는 것과 일치).
        /// </summary>
        public static int RunnersFor(int playerCount) =>
            CanAssign(playerCount) ? playerCount - SeekerCount : playerCount;

        /// <summary>
        /// 배정 순서상 <paramref name="orderIndex"/>번째(0-기반) 플레이어의 역할.
        ///
        /// GAP-22 정책: <b>순서 기준 앞쪽 <see cref="SeekerCount"/>명이 술래</b>. 기획서가 "누구를
        /// 술래로 뽑는가"를 명시하지 않으므로 결정론적 순서(호출자가 OwnerId 오름차순으로 넘긴다)를
        /// 택했다 — 서버 권위 검증·테스트에 유리하고, 새 플레이어가 더 큰 ID로 들어와도 기존
        /// 술래가 바뀌지 않는다(재배정 안정성).
        ///
        /// 배정 불가 인원이면 항상 <see cref="RoleType.Runner"/>다.
        /// </summary>
        public static RoleType RoleForOrder(int orderIndex, int playerCount) =>
            RoleForOrder(orderIndex, playerCount, seekerOrderIndex: 0);

        /// <summary>
        /// 술래 자리를 <paramref name="seekerOrderIndex"/>로 지정한 배정(스프린트 21 로테이션).
        ///
        /// 기존 규칙(§6.2 "술래 1인 고정, 나머지 전원 도망자")은 그대로이고, **누가 그 1인인가만**
        /// 호출자가 정한다 — <see cref="SeekerRotation.SeekerOrderIndex"/>가 라운드마다 다른 값을
        /// 준다. 이 최소 확장으로 GAP-22("항상 가장 낮은 OwnerId = 호스트")가 해소된다.
        ///
        /// 범위를 벗어난 값은 0으로 접는다 — 인원이 줄어든 뒤 옛 인덱스가 들어와도 술래가
        /// 사라지지 않게 하려는 것이다(술래 0명이면 라운드가 성립하지 않는다).
        /// </summary>
        public static RoleType RoleForOrder(int orderIndex, int playerCount, int seekerOrderIndex)
        {
            if (!CanAssign(playerCount))
                return RoleType.Runner;

            if (seekerOrderIndex < 0 || seekerOrderIndex >= playerCount)
                seekerOrderIndex = 0;

            return orderIndex == seekerOrderIndex ? RoleType.Seeker : RoleType.Runner;
        }
    }
}
