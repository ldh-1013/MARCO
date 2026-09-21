namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.1 밸브 상태기계.
    ///
    /// <para>
    /// <b>Closed는 두 가지를 겸한다</b> — 손댄 적 없는 상태와, 중단돼 진행도가 남은 채
    /// 감쇠 중인 상태. 둘을 상태로 쪼개지 않은 이유는 상호작용 규칙이 완전히 같기 때문이다
    /// (§6.1 "다른 도망자가 즉시 이어받을 수 있다"). 감쇠 중인지는
    /// <c>Valve.IsDecaying</c>이 답한다 — HUD는 그 플래그로 색을 달리한다(§12.4).
    /// </para>
    /// </summary>
    public enum ValveState
    {
        Closed,
        Rotating,
        Open,

        /// <summary>
        /// §6.1-2 [v0.4 신설] 역류 — 개방 유지 180초가 지나 30초에 걸쳐 닫히는 중.
        /// 이 상태에서도 재상호작용이 가능하며, 회전을 완료하면 Open으로 복귀한다.
        /// </summary>
        Reflowing,
    }
}
