using Marco.Core.Locomotion;
using Marco.Core.Role;
using Marco.Core.Water;

namespace Marco.Core.Objectives
{
    /// <summary>수중 밸브(B·E) 작업 1회의 구간. §6.1-1 총 점유 표의 진입 · 회전 · 부상.</summary>
    public enum UnderwaterWorkPhase
    {
        /// <summary>하강 중 — 머리는 이미 수면 아래지만 밸브는 아직 돌지 않는다.</summary>
        Entry,

        /// <summary>회전 중 — 밸브 진행도가 오른다.</summary>
        Rotate,

        /// <summary>상승 중 — 손은 뗐지만 아직 수면 위로 나오지 않았다.</summary>
        Surface,

        /// <summary>수면 위로 나왔다(또는 숨이 다해 강제 부상했다). 세션 종료.</summary>
        Done,
    }

    /// <summary>한 틱에 일어난 전이. 호출부(서버)가 밸브 구동기에 반영한다.</summary>
    public readonly struct UnderwaterWorkTick
    {
        /// <summary>진입이 끝났다 — 이제 밸브에 손을 댄다(<c>BeginHold</c>).</summary>
        public readonly bool BeginRotation;

        /// <summary>회전이 끊겼다(뗐거나 숨이 다함) — 밸브에서 손을 뗀다(<c>EndHold</c>). 개방 완료는 해당 없음.</summary>
        public readonly bool StopRotation;

        public UnderwaterWorkTick(bool beginRotation, bool stopRotation)
        {
            BeginRotation = beginRotation;
            StopRotation = stopRotation;
        }
    }

    /// <summary>
    /// §6.1 [v0.4] 수중 밸브 작업의 <b>잠수 구간</b> — 진입 시작부터 부상 완료까지.
    ///
    /// <para>
    /// <b>왜 필요한가(GAP-91 해소).</b> 이전에는 밸브가 회전만 알았다. 잠수 모델(<c>DiveRules</c>)은 머리만
    /// 내리므로 하강·상승이 없고, 숨 게이지는 회전 5.0초(B)만 소모돼 잔여 7.0 — §6.1이 설계한
    /// <i>"숨 게이지 8.0초 소모(잔여 4.0) → 비명 억제(4.5) 불가"</i> 리스크가 실기에서 사라졌다.
    /// 이제 작업 1회 = <b>진입 → 회전 → 부상</b>이고 그 전 구간 동안 서버가 이 플레이어를 잠수 중으로 본다.
    /// </para>
    ///
    /// <para>
    /// <b>새 상수가 없다.</b> 진입·부상 초는 <see cref="ValveOccupancy.EntrySeconds"/>·<see cref="ValveOccupancy.SurfaceSeconds"/>,
    /// 끊김 없는 작업 1회의 잠수 시간은 <see cref="ValveOccupancy.TotalSeconds"/>(블록 2의 "총 점유 8.0")와 같다.
    /// </para>
    ///
    /// <para>
    /// <b>배수구도 같은 세션이다</b>(§6.5-2 "총 점유 = 진입 1 + T + 부상 1"). 다른 것은 진입·부상 초뿐이라
    /// 그것만 주입받는다 — 구간 규칙은 한 곳이다(더블체크 8).
    /// </para>
    ///
    /// <para>
    /// <b>강제로 잠기게 하지 않는다.</b> 이 세션은 "잠수 키를 누른 것"과 같은 <i>의도</i>만 만든다. 실제로 머리가
    /// 수면 아래인지는 여전히 서버 지오메트리(<c>DiveRules.ZoneOf</c>)가 정한다 — 물이 얕아 머리가 안 잠기는
    /// 자리면 숨은 소모되지 않는다.
    /// </para>
    /// </summary>
    public sealed class UnderwaterWorkSession
    {
        /// <summary>수중 밸브(B·E) — 진입·부상은 §6.1-1 배분(<see cref="ValveOccupancy"/>).</summary>
        public UnderwaterWorkSession(ValveId id)
            : this(ValveOccupancy.EntrySeconds(id), ValveOccupancy.SurfaceSeconds(id))
        {
        }

        /// <summary>§6.5-2 배수구 — 진입 1 · 부상 1(<see cref="DrainConfig"/>).</summary>
        public static UnderwaterWorkSession ForDrain() =>
            new UnderwaterWorkSession(DrainConfig.EntrySeconds, DrainConfig.SurfaceSeconds);

        private UnderwaterWorkSession(float entrySeconds, float surfaceSeconds)
        {
            EntrySeconds = entrySeconds;
            SurfaceSeconds = surfaceSeconds;
            Phase = UnderwaterWorkPhase.Entry;
        }

        public float EntrySeconds { get; }
        public float SurfaceSeconds { get; }

        /// <summary>
        /// 수중 작업을 시작·유지할 자격 — <b>도망자 · 범위 안 · 이 자리에서 실제로 잠길 수 있음</b>.
        /// 밸브 B·E와 배수구가 같은 식을 쓴다(<see cref="DrainHatch.CanWork"/>는 이것을 부른다).
        /// </summary>
        public static bool CanWork(RoleType role, bool inRange, bool canSubmergeHere) =>
            role == RoleType.Runner && inRange && canSubmergeHere;

        /// <summary>서버가 쓰는 형태 — 지오메트리에서 <see cref="DiveRules.CanSubmergeHere"/>를 계산해 넘긴다.</summary>
        public static bool CanWork(RoleType role, bool inRange, in WaterSample water, float feetY, bool canSubmerge) =>
            CanWork(role, inRange, DiveRules.CanSubmergeHere(role, water, feetY, canSubmerge));

        public UnderwaterWorkPhase Phase { get; private set; }

        /// <summary>현재 구간에서 흐른 시간(초).</summary>
        public float PhaseElapsed { get; private set; }

        /// <summary>이 세션이 잠수 의도를 유지하는가(진입·회전·부상 전 구간).</summary>
        public bool KeepsSubmerged => Phase != UnderwaterWorkPhase.Done;

        /// <summary>
        /// 숨이 다해 강제 부상으로 끝났는가(§5.9-1). 부상 구간을 끝까지 마친 정상 종료와 구분한다 —
        /// 배수구 통과(§6.5-2)는 정상 부상 뒤에만 열린다.
        /// </summary>
        public bool EndedByForce { get; private set; }

        /// <summary>
        /// 이번 틱(<paramref name="deltaSeconds"/>) 안에 부상 구간이 끝나는가 — §5.9-1 "게이지 0초 경계" 판정.
        /// 서버는 숨 게이지를 이 세션보다 <b>먼저</b> 진전시키므로(<c>ServerTickOrder</c>), 게이지가 이 값을 보고
        /// 같은 틱의 고갈을 질식으로 치지 않는다(<see cref="Marco.Core.Breath.BreathGauge.Tick(Marco.Core.Breath.BreathZone, float, bool)"/>).
        /// </summary>
        public bool SurfaceCompletesWithin(float deltaSeconds) =>
            Phase == UnderwaterWorkPhase.Surface && PhaseElapsed + (deltaSeconds > 0f ? deltaSeconds : 0f) >= SurfaceSeconds;

        /// <summary>
        /// 시간을 진전시킨다.
        /// </summary>
        /// <param name="holding">플레이어가 아직 E를 누르고 있다(도망자 한정 — 호출부가 역할을 확인한다).</param>
        /// <param name="rotationFinished">밸브가 열렸다(이 플레이어든 동시 작업자든).</param>
        /// <param name="canSubmerge">
        /// §5.9-1 숨이 남아 있다. 0이면 강제 부상 — 세션이 즉시 끝난다. <b>단 이번 틱에 부상이 끝나면 부상 완료가 이긴다</b>
        /// (§5.9-1 "게이지 0초 경계": 순위 1 부상 완료 · 순위 2 게이지 고갈 — 부상 완료가 확정되면 질식은 무시).
        /// </param>
        public UnderwaterWorkTick Tick(float deltaSeconds, bool holding, bool rotationFinished, bool canSubmerge)
        {
            if (Phase == UnderwaterWorkPhase.Done)
                return default;

            if (deltaSeconds < 0f)
                deltaSeconds = 0f;

            // §5.9-1 강제 부상 — 숨이 다하면 어느 구간이든 그 자리에서 끝난다(더 소모할 숨이 없다).
            // 예외는 하나 — 같은 틱에 부상이 끝나면 아래 Surface 분기가 정상 종료로 처리한다(0초 경계, 순위 1).
            if (!canSubmerge && !SurfaceCompletesWithin(deltaSeconds))
            {
                bool wasRotating = Phase == UnderwaterWorkPhase.Rotate;
                EndedByForce = true;
                Enter(UnderwaterWorkPhase.Done);
                return new UnderwaterWorkTick(false, wasRotating);
            }

            switch (Phase)
            {
                case UnderwaterWorkPhase.Entry:
                    if (!holding)
                    {
                        Enter(UnderwaterWorkPhase.Surface); // 내려가다 그만뒀다 — 올라와야 한다
                        return default;
                    }

                    PhaseElapsed += deltaSeconds;
                    if (PhaseElapsed < EntrySeconds)
                        return default;

                    Enter(UnderwaterWorkPhase.Rotate);
                    return new UnderwaterWorkTick(true, false);

                case UnderwaterWorkPhase.Rotate:
                    if (rotationFinished)
                    {
                        Enter(UnderwaterWorkPhase.Surface);
                        return default;
                    }

                    if (!holding)
                    {
                        Enter(UnderwaterWorkPhase.Surface);
                        return new UnderwaterWorkTick(false, true);
                    }

                    PhaseElapsed += deltaSeconds;
                    return default;

                case UnderwaterWorkPhase.Surface:
                    PhaseElapsed += deltaSeconds;
                    if (PhaseElapsed >= SurfaceSeconds)
                        Enter(UnderwaterWorkPhase.Done);
                    return default;
            }

            return default;
        }

        /// <summary>회전을 시작하지 못했다(그 사이 다른 사람이 열었거나 잠겼다) — 곧장 올라온다.</summary>
        public void ForceSurface()
        {
            if (Phase != UnderwaterWorkPhase.Done)
                Enter(UnderwaterWorkPhase.Surface);
        }

        private void Enter(UnderwaterWorkPhase phase)
        {
            Phase = phase;
            PhaseElapsed = 0f;
        }
    }
}
