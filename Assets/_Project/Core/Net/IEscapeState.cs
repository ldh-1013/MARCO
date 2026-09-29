namespace Marco.Core.Net
{
    /// <summary>
    /// 서버가 확정한 탈출(월드 제외)을 pawn에 적용하는 계약(09-29) — <see cref="IRoleState"/>와 같은 패턴. <c>RoleNetworkSync</c>(Net)가
    /// <c>PawnRoleSync</c>의 판정대로 부르고 <c>FirstPersonController</c>(Presentation)가 구현한다.
    /// </summary>
    public interface IEscapeState
    {
        /// <summary>이 pawn이 탈출해 월드에서 빠졌는가.</summary>
        bool IsEscaped { get; }

        /// <summary>네트워크가 확정한 탈출 상태를 적용한다(바뀔 때만 불린다).</summary>
        void ApplyEscaped(bool escaped);
    }
}
