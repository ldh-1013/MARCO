using System;
using System.Collections.Generic;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// §15.4 상태기계의 순수 로직. MonoBehaviour나 FishNet에 의존하지 않는다 —
    /// 실제 네트워크 동기화(전원에게 상태 브로드캐스트)는 Net 레이어가
    /// 이 클래스를 감싸서 수행한다(§15.3 "Core가 FishNet API를 직접 참조하지 않음").
    ///
    /// RoundEnd에서 두 갈래로 분기하는 이유(§15.4):
    /// - Lobby: 리매치 투표 부결
    /// - RoleAssign: 리매치 투표 가결(즉시 재시작, Lobby를 다시 거치지 않음)
    /// </summary>
    public sealed class GameFlowManager
    {
        public GameFlowState CurrentState { get; private set; } = GameFlowState.Boot;

        /// <summary>(이전 상태, 다음 상태) 순서로 전달된다.</summary>
        public event Action<GameFlowState, GameFlowState> StateChanged;

        private static readonly Dictionary<GameFlowState, GameFlowState[]> AllowedTransitions =
            new Dictionary<GameFlowState, GameFlowState[]>
            {
                { GameFlowState.Boot,       new[] { GameFlowState.MainMenu } },
                { GameFlowState.MainMenu,   new[] { GameFlowState.Lobby } },
                { GameFlowState.Lobby,      new[] { GameFlowState.RoleAssign } },
                { GameFlowState.RoleAssign, new[] { GameFlowState.InGame } },
                { GameFlowState.InGame,     new[] { GameFlowState.RoundEnd } },
                { GameFlowState.RoundEnd,   new[] { GameFlowState.Lobby, GameFlowState.RoleAssign } },
            };

        /// <summary>정의된 전이표에 없는 전이는 거부하고 false를 반환한다.</summary>
        public bool TryTransition(GameFlowState next)
        {
            if (!AllowedTransitions.TryGetValue(CurrentState, out GameFlowState[] allowed))
                return false;

            if (Array.IndexOf(allowed, next) < 0)
                return false;

            GameFlowState previous = CurrentState;
            CurrentState = next;
            StateChanged?.Invoke(previous, next);
            return true;
        }
    }
}
