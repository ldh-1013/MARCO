using NUnit.Framework;
using Marco.Core.GameFlow;

namespace Marco.Core.Tests
{
    /// <summary>§15.4의 7개 상태 전이(RoundEnd의 두 분기 포함)를 검증한다.</summary>
    public class GameFlowManagerTests
    {
        [TestCase(GameFlowState.Boot, GameFlowState.MainMenu)]
        [TestCase(GameFlowState.MainMenu, GameFlowState.Lobby)]
        [TestCase(GameFlowState.Lobby, GameFlowState.RoleAssign)]
        [TestCase(GameFlowState.RoleAssign, GameFlowState.InGame)]
        [TestCase(GameFlowState.InGame, GameFlowState.RoundEnd)]
        [TestCase(GameFlowState.RoundEnd, GameFlowState.Lobby)]      // 리매치 투표 부결
        [TestCase(GameFlowState.RoundEnd, GameFlowState.RoleAssign)] // 리매치 투표 가결(즉시 재시작)
        public void TryTransition_AllowedPath_Succeeds(GameFlowState from, GameFlowState to)
        {
            var manager = new GameFlowManager();
            AdvanceTo(manager, from);

            bool result = manager.TryTransition(to);

            Assert.IsTrue(result);
            Assert.AreEqual(to, manager.CurrentState);
        }

        [Test]
        public void TryTransition_SkipsState_Fails()
        {
            var manager = new GameFlowManager();

            bool result = manager.TryTransition(GameFlowState.InGame); // Boot에서 바로 InGame으로 건너뛰기 시도

            Assert.IsFalse(result);
            Assert.AreEqual(GameFlowState.Boot, manager.CurrentState);
        }

        [Test]
        public void TryTransition_RaisesStateChangedEvent()
        {
            var manager = new GameFlowManager();
            GameFlowState? observedPrevious = null;
            GameFlowState? observedNext = null;
            manager.StateChanged += (prev, next) => { observedPrevious = prev; observedNext = next; };

            manager.TryTransition(GameFlowState.MainMenu);

            Assert.AreEqual(GameFlowState.Boot, observedPrevious);
            Assert.AreEqual(GameFlowState.MainMenu, observedNext);
        }

        /// <summary>테스트 편의를 위해 Boot부터 목표 상태까지 정상 경로로 이동시킨다.</summary>
        private static void AdvanceTo(GameFlowManager manager, GameFlowState target)
        {
            GameFlowState[] path =
            {
                GameFlowState.Boot,
                GameFlowState.MainMenu,
                GameFlowState.Lobby,
                GameFlowState.RoleAssign,
                GameFlowState.InGame,
                GameFlowState.RoundEnd,
            };

            foreach (GameFlowState state in path)
            {
                if (manager.CurrentState == target)
                    return;

                manager.TryTransition(state);
            }
        }
    }
}
