namespace Marco.Core.GameFlow
{
    /// <summary>§15.4 GameFlowManager 상태기계.</summary>
    public enum GameFlowState
    {
        Boot,
        MainMenu,
        Lobby,
        RoleAssign,
        InGame,
        RoundEnd
    }
}
