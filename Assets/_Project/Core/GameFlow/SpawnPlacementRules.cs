using Marco.Core.Role;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// 새 판 배치 규칙 — <c>PawnPhaseTeleporter</c>가 부른다. 모든 새 판(맵 새로 로드 · 리매치 가결로 맵이 유지된 채 재시작)은
    /// 전원이 스폰 슬롯에서 시작하고, 술래는 격리 지점에서 시작한다. Unity와 무관 — EditMode 테스트 가능.
    /// </summary>
    public static class SpawnPlacementRules
    {
        /// <summary>
        /// 지금 맵 위 배치를 해야 하는가. 맵이 있고, 이 맵에서 아직 배치하지 않았거나 <b>라운드 번호가 바뀌었으면</b>(리매치 —
        /// 맵이 내려가지 않으므로 맵 로드 신호만 보면 직전 판 자리에서 재시작한다, 스프린트 22).
        /// </summary>
        public static bool ShouldPlaceOnMap(bool mapReady, bool placedForCurrentMap, int placedForRound, int currentRound) =>
            mapReady && (!placedForCurrentMap || currentRound != placedForRound);

        /// <summary>격리 지점에 둘지 — 술래이고 격리 앵커가 있을 때. 그 밖(러너 · 새 판 직후의 전원)은 스폰 링 슬롯.</summary>
        public static bool UsesIsolationAnchor(RoleType role, bool hasIsolationAnchor) =>
            SeekerIsolation.AppliesTo(role) && hasIsolationAnchor;
    }
}
