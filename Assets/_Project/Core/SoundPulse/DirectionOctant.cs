namespace Marco.Core.Sound
{
    /// <summary>
    /// §3.4 방향 게이지용 8방위 스냅. 정확한 좌표 대신 사용해
    /// 리스너가 발생원의 정밀 위치를 알 수 없도록 한다(GAP-2 결정).
    /// </summary>
    public enum DirectionOctant
    {
        N,
        NE,
        E,
        SE,
        S,
        SW,
        W,
        NW
    }
}
