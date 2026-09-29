using System.Collections.Generic;
using UnityEngine;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// 씬에 켜져 있는 밸브 목록 — <see cref="ValveBehaviour"/>가 <c>OnEnable</c>/<c>OnDisable</c>에서 스스로 등록한다
    /// (<c>EscapePointRegistry</c>와 같은 패턴, 09-29).
    ///
    /// <para>
    /// <b>왜 필요한가</b>: 캐릭터는 로비에서 먼저 생성되고 맵(Game 씬)은 나중에 추가 로드된다. 예전의 <c>ValveInteractor</c>는
    /// <c>Awake</c>에서 <c>FindObjectsByType</c>로 목록을 한 번만 캐시해 영구히 비어 있었고, E가 밸브에 닿지 않았다
    /// (호스트 로그 "[Valve] 씬에 ValveBehaviour가 없습니다"). 소비자는 매번 이 목록을 읽는다 — 맵이 오가도 그대로 맞는다.
    /// </para>
    /// </summary>
    public static class ValveRegistry
    {
        private static readonly List<ValveBehaviour> Valves = new List<ValveBehaviour>();
        private static ValveBehaviour[] _snapshot = System.Array.Empty<ValveBehaviour>();
        private static bool _dirty;

        /// <summary>지금 켜져 있는 밸브(등록 순서).</summary>
        public static IReadOnlyList<ValveBehaviour> All => Valves;

        /// <summary>배열 사본 — 목록이 바뀔 때만 새로 만든다(HUD가 매 프레임 읽는다).</summary>
        public static ValveBehaviour[] Snapshot
        {
            get
            {
                if (_dirty)
                {
                    _snapshot = Valves.ToArray();
                    _dirty = false;
                }

                return _snapshot;
            }
        }

        public static void Register(ValveBehaviour valve)
        {
            if (valve != null && !Valves.Contains(valve))
            {
                Valves.Add(valve);
                _dirty = true;
            }
        }

        public static void Unregister(ValveBehaviour valve)
        {
            if (Valves.Remove(valve))
                _dirty = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            Valves.Clear();
            _snapshot = System.Array.Empty<ValveBehaviour>();
            _dirty = false;
        }
    }
}
