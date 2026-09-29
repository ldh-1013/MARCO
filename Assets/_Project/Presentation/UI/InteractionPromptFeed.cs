using UnityEngine;

namespace Marco.Presentation.UI
{
    /// <summary>
    /// 상호작용 안내 한 줄(09-29)의 모음 — 밸브(<c>ValveInteractor</c>) · 배수구(<c>DrainPoint</c>) · 출구(<c>EscapePointTrigger</c>)가
    /// 매 프레임 후보를 내고, HUD는 <b>직전 프레임의 가장 가까운 후보</b> 하나를 그린다. 컴포넌트 Update 순서와 무관하게
    /// 한 줄만 보이게 하려고 한 프레임 늦게 보여 준다.
    /// </summary>
    public static class InteractionPromptFeed
    {
        private static int _frame = -1;
        private static string _best;
        private static float _bestDistance;
        private static string _shown = string.Empty;

        /// <summary>이번 프레임의 후보. 빈 문자열은 무시한다. 여럿이면 <paramref name="distance"/>가 가장 작은 것.</summary>
        public static void Offer(string text, float distance)
        {
            Roll();
            if (string.IsNullOrEmpty(text))
                return;

            if (_best == null || distance < _bestDistance)
            {
                _best = text;
                _bestDistance = distance;
            }
        }

        /// <summary>HUD가 그릴 한 줄 — 직전 프레임에서 고른 것. 없으면 빈 문자열.</summary>
        public static string Current
        {
            get
            {
                Roll();
                return _shown;
            }
        }

        private static void Roll()
        {
            int now = Time.frameCount;
            if (now == _frame)
                return;

            _shown = _frame == now - 1 && _best != null ? _best : string.Empty;
            _frame = now;
            _best = null;
            _bestDistance = float.MaxValue;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession()
        {
            _frame = -1;
            _best = null;
            _shown = string.Empty;
        }
    }
}
