using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// 서버가 공개하는 이번 판의 목표 수치(SyncVar로 나가는 값) — 활성 밸브 수 · 요구 동시 개방 수 · 게이트 래치.
    /// <see cref="Published"/>가 false면 아직 서버 값이 없다(오프라인 스모크 리그이거나 네트워크 라운드 객체가 없음).
    /// </summary>
    public readonly struct ObjectivePublication
    {
        public readonly bool Published;
        public readonly int ActiveValves;
        public readonly int RequiredOpen;
        public readonly bool GateOpen;

        public ObjectivePublication(bool published, int activeValves, int requiredOpen, bool gateOpen)
        {
            Published = published;
            ActiveValves = activeValves;
            RequiredOpen = requiredOpen;
            GateOpen = gateOpen;
        }

        public static readonly ObjectivePublication None = default;

        public override string ToString() =>
            Published ? $"활성 {ActiveValves} · 요구 {RequiredOpen} · 게이트 {(GateOpen ? "열림" : "닫힘")}" : "미공개";
    }

    /// <summary>
    /// 서버의 목표 판정 — 요구 개방 수를 <b>라운드 시작 때 한 번</b> 확정하고(활성 밸브를 고를 때와 같은 총원),
    /// 게이트 래치를 서버에서만 판정한다. <c>RoundNetworkSync</c>가 쓰고 결과를 SyncVar로 공개한다.
    /// Unity와 무관 — EditMode 테스트 가능.
    /// </summary>
    public sealed class RoundObjective
    {
        private readonly EscapeGateLatch _latch = new EscapeGateLatch();

        public int TotalPlayers { get; private set; }
        public int ActiveValves { get; private set; }
        public int RequiredOpen { get; private set; }
        public bool GateOpen => _latch.IsOpen;

        /// <summary>
        /// 라운드 시작(브리핑에서 활성 밸브를 고르는 순간) — 그 총원으로 요구 수를 확정한다. 라운드 중 인원이 바뀌어도
        /// 이 판에서는 변하지 않는다(§6.2 · 09-29 결정).
        /// </summary>
        public void BeginRound(int totalPlayers, int activeValves)
        {
            TotalPlayers = totalPlayers;
            ActiveValves = activeValves;
            RequiredOpen = ValveRoster.RequiredOpenCount(totalPlayers);
            _latch.Reset();
        }

        /// <summary>서버 틱 — 지금 동시에 열린 밸브 수. 게이트가 이번 틱에 열렸으면 true(래치 — 역류로 줄어도 닫히지 않는다).</summary>
        public bool Tick(int openedNow) => RequiredOpen > 0 && _latch.Update(openedNow, RequiredOpen);

        /// <summary>라운드 경계(리매치 · 로비) — 다음 판의 확정 전까지 비운다.</summary>
        public void Reset()
        {
            TotalPlayers = 0;
            ActiveValves = 0;
            RequiredOpen = 0;
            _latch.Reset();
        }

        public ObjectivePublication Publication => new ObjectivePublication(true, ActiveValves, RequiredOpen, GateOpen);
    }

    /// <summary>
    /// 서버 공개값의 거울 — 서버(호스트)는 판정할 때, 순수 클라이언트는 SyncVar 변경 때 채운다
    /// (<c>RoundStateRegistry</c>와 같은 패턴). Presentation(목표 추적기 · HUD · 브리핑 · 출구 사전 필터)은 여기만 읽는다.
    /// </summary>
    public static class ObjectiveRegistry
    {
        public static ObjectivePublication Current { get; private set; }

        public static void Publish(ObjectivePublication publication) => Current = publication;

        public static void Clear() => Current = ObjectivePublication.None;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => Clear();
    }

    /// <summary>
    /// 클라이언트 쪽 목표 수치 규칙 — <c>ValveObjectiveTracker</c>가 부른다(HUD · 브리핑 · 출구 사전 필터가 그 값을 읽는다).
    /// <b>서버가 공개한 값이 있으면 그것만 쓴다</b>(09-29) — 피어마다 게이트를 따로 계산하지 않는다. 예전에는 공개값을 보지 않고
    /// 5인 폴백(요구 3)과 로컬 래치를 써서, 3인 판에서 서버는 요구 2 · HUD · 게이트 · 브리핑은 요구 3이었다.
    /// 공개값이 없을 때(오프라인 스모크 리그)만 총원 덮어쓰기 · 5인 폴백 · 로컬 래치를 쓴다.
    /// </summary>
    public static class ObjectiveView
    {
        /// <summary>총원을 모를 때 쓰는 인원(오프라인 스모크 리그 전용이어야 한다).</summary>
        public const int OfflineFallbackPlayers = 5;

        public static int RequiredOpen(in ObjectivePublication publication, int totalPlayersOverride, out bool usedFallback)
        {
            if (publication.Published)
            {
                usedFallback = false;
                return publication.RequiredOpen;
            }

            usedFallback = totalPlayersOverride <= 0;
            return ValveRoster.RequiredOpenCount(usedFallback ? OfflineFallbackPlayers : totalPlayersOverride);
        }

        public static int ActiveValves(in ObjectivePublication publication, int localActiveCount) =>
            publication.Published ? publication.ActiveValves : localActiveCount;

        public static bool GateOpen(in ObjectivePublication publication, bool localLatchOpen) =>
            publication.Published ? publication.GateOpen : localLatchOpen;
    }
}
