using System.Collections.Generic;
using UnityEngine;

namespace Marco.Core.Net
{
    /// <summary>
    /// 접속 플레이어들의 준비 상태(<see cref="IReadyState"/>) 중앙 등록소(스프린트 18).
    ///
    /// 로비 UI(Presentation)가 전원 목록을 그리고, 진단이 인원을 세는 데 쓴다.
    /// 서버 게이팅은 이 레지스트리가 아니라 Net 내부 목록(<c>ReadyNetworkSync.Spawned</c>)을
    /// 쓴다 — 서버 판정 입력은 Net이 직접 소유한 실측이어야 하기 때문(스프린트 13 원칙).
    /// <c>TagTargetRegistry</c>와 같은 지연 바인딩 패턴이다.
    /// </summary>
    public static class ReadyStateRegistry
    {
        private static readonly List<IReadyState> _states = new List<IReadyState>();

        /// <summary>현재 등록된 준비 상태들(읽기 전용, UI 순회용).</summary>
        public static IReadOnlyList<IReadyState> All => _states;

        public static void Register(IReadyState state)
        {
            if (state != null && !_states.Contains(state))
                _states.Add(state);
        }

        public static void Unregister(IReadyState state)
        {
            if (state != null)
                _states.Remove(state);
        }

        /// <summary>도메인 리로드 꺼짐 대비 세션 진입 초기화(다른 레지스트리와 동일한 안전장치).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => _states.Clear();
    }
}
