using UnityEngine;

namespace Marco.Core.Net
{
    /// <summary>
    /// 서버가 확정한 <b>라운드 전역 상태</b>를 Presentation이 어셈블리 경계를 넘어 읽게 해주는
    /// 지연 바인딩 지점. <c>EscapeGateRegistry</c>·<c>WaterVolumeRegistry</c>와 같은 패턴이다.
    ///
    /// <para>
    /// <b>왜 필요한가</b>: §6.2 인원별 술래 이속과 §6.2-1 종반 압박(+5%)은 이동 시뮬레이터
    /// (Core)가 계산하는데, 그 입력인 "총원"과 "종반 여부"는 라운드 상태기계
    /// (<c>RoundNetworkSync</c>, Net)가 소유한다. Presentation은 Net을 참조하지 않으므로
    /// (§15.2) 공통 조상 Core에 값만 둔다.
    /// </para>
    ///
    /// <para>
    /// <b>여기 담기는 값은 전부 서버가 쓴 것이다</b>(GAP-24). 클라이언트는 읽기만 하며,
    /// 네트워크가 없으면 기본값(총원 0 → 시뮬레이터가 5인으로 폴백, 종반 false)이다.
    /// </para>
    /// </summary>
    public static class RoundStateRegistry
    {
        /// <summary>
        /// §6.2 이번 라운드 총원. 술래 이속 보정의 입력이다.
        /// 0이면 미정이며 시뮬레이터가 §6.2 권장 구성(5인)으로 폴백한다.
        /// </summary>
        public static int TotalPlayers { get; private set; }

        /// <summary>§6.2-1 종반 압박(잔여 1분 술래 +5%)이 걸렸는가.</summary>
        public static bool EndgamePressure { get; private set; }

        /// <summary>
        /// §6.5-1 최후 생존자 페이즈 중인가. 블록 4의 HUD·배수구 표시가 읽는다.
        /// </summary>
        public static bool LastSurvivorPhase { get; private set; }

        /// <summary>서버가 값을 밀어 넣는다. 다른 곳에서 쓰지 않는다.</summary>
        public static void PublishFromServer(int totalPlayers, bool endgamePressure,
            bool lastSurvivorPhase)
        {
            TotalPlayers = totalPlayers;
            EndgamePressure = endgamePressure;
            LastSurvivorPhase = lastSurvivorPhase;
        }

        /// <summary>
        /// 도메인 리로드를 끈 채 Play를 반복하면 지난 판의 종반 압박이 살아남아
        /// <b>새 라운드가 시작부터 +5% 상태</b>가 된다. 진입 시 초기화한다(더블체크 2).
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            TotalPlayers = 0;
            EndgamePressure = false;
            LastSurvivorPhase = false;
        }
    }
}
