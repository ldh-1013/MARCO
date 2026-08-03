namespace Marco.Core.Role
{
    /// <summary>
    /// §2.3 "술래 로테이션 3판 1세트"의 순수 계산(스프린트 21, GAP-22 해소).
    ///
    /// **기획서 원문은 한 줄뿐이다**(§2.3 메타 루프): <c>- 술래 로테이션 3판 1세트 (~30분)</c>.
    /// 여기서 확정적으로 도출되는 것은 **세트 = 3판**뿐이고, 아래는 명시가 없어 GAP-34로 기록했다:
    /// 순환 주기(매 판인가 매 세트인가), 순번 결정 방식, 중도 이탈 시 처리.
    ///
    /// **채택한 해석 — 매 라운드 순환**: 이 스프린트의 목적이 "호스트가 항상 술래"(GAP-22) 해소이고,
    /// 세트마다 순환하면 한 사람이 30분 내내 술래라 그 목적을 달성하지 못한다. §20.3 합격 기준의
    /// "술래 승률 40~60%"도 술래가 자주 바뀌는 것을 전제로 읽힌다. 따라서 라운드마다 다음 순번으로
    /// 넘기고, "3판 1세트"는 <see cref="RoundsPerSet"/>로 **표시·집계 단위**로만 쓴다.
    ///
    /// **순번은 결정론적**이다 — 호출자가 OwnerId 오름차순으로 정렬해 넘긴 목록의 인덱스를 쓴다
    /// (스프린트 13 GAP-22 정책의 정렬 규칙을 그대로 유지). 서버·클라이언트 어디서 계산해도 같은
    /// 결과가 나오고 테스트도 쉽다.
    /// </summary>
    public static class SeekerRotation
    {
        /// <summary>§2.3 "3판 1세트". 표시·집계 단위이며 순환 주기가 아니다(위 해석 참고).</summary>
        public const int RoundsPerSet = 3;

        /// <summary>
        /// <paramref name="roundNumber"/>(0-기반 통산 라운드)의 술래가 될 **정렬 순서 인덱스**.
        /// 인원이 유효하지 않으면 0(첫 번째)을 돌려준다 — 호출자가 배정 가능 인원인지 먼저 확인한다.
        /// </summary>
        public static int SeekerOrderIndex(int roundNumber, int playerCount)
        {
            if (playerCount <= 0)
                return 0;

            // 음수 라운드(초기화 전 값)에서도 0..playerCount-1로 정규화한다.
            return ((roundNumber % playerCount) + playerCount) % playerCount;
        }

        /// <summary>통산 라운드 번호(0-기반)가 속한 세트 번호(1-기반, 표시용).</summary>
        public static int SetNumber(int roundNumber)
        {
            if (roundNumber < 0)
                return 1;

            return roundNumber / RoundsPerSet + 1;
        }

        /// <summary>세트 안에서 몇 번째 판인가(1..<see cref="RoundsPerSet"/>, 표시용).</summary>
        public static int RoundInSet(int roundNumber)
        {
            if (roundNumber < 0)
                return 1;

            return roundNumber % RoundsPerSet + 1;
        }

        /// <summary>
        /// 인원 전원이 한 번씩 술래를 맡는 데 필요한 라운드 수(= 인원). 로테이션이 한 바퀴 도는 주기이며,
        /// §2.3의 세트(3판)와 일치하지 않을 수 있다(4인이면 4판이 한 바퀴).
        /// </summary>
        public static int RoundsPerFullCycle(int playerCount) => playerCount > 0 ? playerCount : 1;
    }
}
