using System;
using System.Collections.Generic;
using Marco.Core.Role;
using Marco.Core.Util;
using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.5-2 배수구 수치. 전부 기획서 원문에서 옮기거나 유도한 값이다.
    /// </summary>
    public static class DrainConfig
    {
        /// <summary>§6.5-2 "배치 — 2개소". 메인풀 (31,21) · 유아풀 (8,9.5).</summary>
        public const int PlacedCount = 2;

        /// <summary>§6.5-1 "페이즈 제한 90초(라운드 잔여가 더 짧으면 그쪽 우선)".</summary>
        public const float PhaseSeconds = 90f;

        /// <summary>§6.5-2 작업 시간 공식 <c>T = 14 − (동시 개방 밸브 수 × 3)</c>의 기본항.</summary>
        public const float BaseWorkSeconds = 14f;

        /// <summary>§6.5-2 작업 시간 공식의 밸브 1개당 감소량.</summary>
        public const float WorkSecondsPerOpenValve = 3f;

        /// <summary>§6.5-2 총 점유 표의 "진입 1".</summary>
        public const float EntrySeconds = 1f;

        /// <summary>§6.5-2 총 점유 표의 "부상 1".</summary>
        public const float SurfaceSeconds = 1f;

        /// <summary>§6.5-2 "통과 1.5초, 그 동안 태그 가능".</summary>
        public const float TransitSeconds = 1.5f;

        /// <summary>
        /// §6.5-2 작업 시간 <c>T = 14 − (동시 개방 밸브 수 × 3)</c>.
        ///
        /// <para>
        /// <b>기여도에 반비례한다 — 던지기 차단.</b> §6.5-2 원문: *"밸브 2개를 이미 열어놨어야
        /// 단번에 나갈 수 있는데, 2개가 열린 상태라면 나머지 1개를 여는 쪽이 훨씬 쉽다.
        /// 던지기가 항상 손해가 되어 역인센티브가 수치로 자동 차단된다."*
        /// </para>
        ///
        /// <para>
        /// 공식이 음수가 되지 않게 하한 0을 둔다 — 밸브 5개가 동시에 열려 있을 수는 없지만
        /// (요구 수를 채우면 게이트가 열리고 배수구는 활성화되지 않는다) 방어적으로 막는다.
        /// </para>
        /// </summary>
        public static float WorkSeconds(int openValves)
        {
            return Mathf.Max(0f, BaseWorkSeconds - Mathf.Max(0, openValves) * WorkSecondsPerOpenValve);
        }

        /// <summary>
        /// §6.5-2 총 점유 = 진입 1 + T + 부상 1. <b>하드코딩하지 않고 합으로 유도한다</b>
        /// (0개 → 16초 / 1개 → 13초 / 2개 → 10초).
        /// </summary>
        public static float TotalOccupancySeconds(int openValves)
        {
            return EntrySeconds + WorkSeconds(openValves) + SurfaceSeconds;
        }

        /// <summary>
        /// 숨 게이지로 한 번에 덮을 수 있는가. 못 덮으면 §6.5-2 "2회 잠수 필수".
        /// 게이지 값은 <c>BreathConfig.TotalSeconds</c> 한 곳이 소유한다 — 10-01 총량 20초부터는 동시 개방 0~3개 모두 1회다.
        /// </summary>
        public static int RequiredDives(int openValves, float breathTotalSeconds)
        {
            float occupancy = TotalOccupancySeconds(openValves);
            if (breathTotalSeconds <= 0f)
                return int.MaxValue;

            return occupancy <= breathTotalSeconds ? 1 : Mathf.CeilToInt(occupancy / breathTotalSeconds);
        }
    }

    /// <summary>
    /// §6.5-2 배수구 식별자. 메인풀 = 1, 유아풀 = 2.
    /// </summary>
    public enum DrainId
    {
        MainPool = 1,
        KiddiePool = 2,
    }

    /// <summary>
    /// §6.5-2 배수구 하나의 서버 권위 상태. <b>진행도 규칙은 <see cref="Valve"/>를 그대로 재사용한다</b> —
    /// §6.5-2 "진행도 — 밸브와 동일(유예 3초 + 감쇠 0.10/s)". 새 진행도 시스템을 만들면
    /// 감쇠 상수가 두 곳에 생긴다(더블체크 8).
    ///
    /// <para>
    /// <b>작업 완료 → 부상 → 통과 → 탈출.</b> [커밋 전 수정 4-1] 작업이 끝나면 "완료" 상태로 기다리고, 작업자가
    /// <b>부상을 마친 뒤</b> 서버가 <see cref="BeginTransit"/>로 통과(1.5초)를 연다. §6.5-2 "통과 1.5초, 그 동안
    /// 태그 가능"과 §6.5-3 "술래가 태그할 수 있는 순간은 부상할 그때뿐"을 함께 만족하려면 통과가 수면에서
    /// 일어나야 한다 — 부상 구간(잠수)과 겹치면 통과 앞부분이 태그 불가가 된다. 이 순서 덕에 총 점유
    /// (진입 1 + T + 부상 1)가 숨 총량(<c>BreathConfig.TotalSeconds</c>) 안에 들어와야 한 번에 나갈 수 있다(§6.5-2).
    /// </para>
    ///
    /// <para>
    /// 부상 도중 숨이 다해 강제 부상하면(§5.9-1) 완료는 <b>유지</b>된다 — 다음 잠수에서 진입 1 + 부상 1만 하면 나간다.
    /// 역류(§6.1-2)는 배수구에 해당하지 않는다(완료 상태는 Open 타이머를 쓰지 않는다).
    /// </para>
    /// </summary>
    public sealed class DrainHatch
    {
        private readonly Valve _progress;
        private ulong? _transitPlayer;
        private float _transitElapsed;
        private bool _completed;

        public DrainHatch(DrainId id, float workSeconds)
        {
            Id = id;
            WorkSeconds = workSeconds;
            _progress = new Valve(workSeconds);
        }

        public DrainId Id { get; }

        /// <summary>
        /// 이 배수구의 작업 시간 T(초). <b>페이즈 진입 시점에 확정된다</b> — GAP-87 참조.
        /// </summary>
        public float WorkSeconds { get; }

        /// <summary>§6.5-2 진행도 0.0~1.0. 밸브와 같은 정규화다. 완료 후 부상 대기 중이면 1.</summary>
        public float Progress01 => _completed ? 1f : _progress.Progress01;

        /// <summary>§6.1 규칙 그대로의 감쇠 중 여부. HUD가 색을 달리해야 한다(§12.4).</summary>
        public bool IsDecaying => !_completed && _progress.IsDecaying;

        /// <summary>작업이 끝나 작업자의 부상을 기다리는 중(통과는 아직).</summary>
        public bool IsCompleted => _completed;

        /// <summary>누군가 작업 중인가.</summary>
        public bool IsWorking => _progress.State == ValveState.Rotating;

        /// <summary>작업이 끝나 통과 중인가(이 1.5초 동안 태그될 수 있다).</summary>
        public bool IsInTransit => _transitPlayer.HasValue;

        /// <summary>통과 잔여(초).</summary>
        public float TransitRemaining =>
            IsInTransit ? Mathf.Max(0f, DrainConfig.TransitSeconds - _transitElapsed) : 0f;

        /// <summary>통과 중인 플레이어.</summary>
        public ulong? TransitPlayer => _transitPlayer;

        /// <summary>
        /// 작업을 시작한다. <b>도망자만</b> 가능하다 — 술래는 배수구를 조작할 이유도 권한도 없고,
        /// 메아리는 GAP-5로 물리 상호작용이 불가다.
        /// </summary>
        public ValveInteractionRejection TryWork(ulong playerId, RoleType role)
        {
            if (role == RoleType.Echo)
                return ValveInteractionRejection.EchoCannotInteract;
            if (role != RoleType.Runner)
                return ValveInteractionRejection.SeekerCannotInteract;

            if (IsInTransit)
                return ValveInteractionRejection.AlreadyOpen;

            // 이미 끝난 배수구 — 할 일이 없다(바로 부상하면 된다).
            if (_completed)
                return ValveInteractionRejection.None;

            return _progress.TryInteract(playerId, role);
        }

        /// <summary>
        /// 작업자가 부상을 마쳤다 — 통과를 연다(§6.5-2 1.5초, 그 동안 태그 가능). 완료 상태가 아니면 false.
        /// </summary>
        public bool BeginTransit(ulong playerId)
        {
            if (!_completed || IsInTransit)
                return false;

            _completed = false;
            _transitPlayer = playerId;
            _transitElapsed = 0f;
            return true;
        }

        /// <summary>
        /// 이 플레이어가 지금 배수구 작업 자격이 있는가. 서버가 <b>매 틱</b> 재평가한다 —
        /// 클라이언트는 "누르고 있다"만 주장하고 나머지는 전부 서버 관측이다(GAP-24).
        ///
        /// <list type="bullet">
        /// <item><b>도망자</b> — 태그되면 메아리가 되어 즉시 자격을 잃는다(GAP-5).</item>
        /// <item><b>범위 안</b> — <see cref="InteractionRules.InRange"/>, 수중 대상이므로 수평 거리(GAP-88).</item>
        /// <item><b>이 자리에서 실제로 잠길 수 있음</b>(<see cref="Marco.Core.Locomotion.DiveRules.CanSubmergeHere"/> — 물 안 · 잠수 자세의 머리가
        ///   수면 아래 · 숨 있음, GAP-88). "지금 잠수 키를 누르고 있는가"가 아니다 — 잠수 상태는 작업 세션
        ///   (<see cref="UnderwaterWorkSession"/>, 진입 → 작업 → 부상 전 구간)이 유지한다. §6.5-2 "2회 잠수 필수"는
        ///   작업이 숨을 쓴다는 뜻이라, 덱이나 얕은 경사로에서 작업할 수 있으면 규칙이 통째로 무너진다.</item>
        /// </list>
        /// </summary>
        public static bool CanWork(RoleType role, bool inRange, bool submerged) =>
            UnderwaterWorkSession.CanWork(role, inRange, submerged); // 밸브 B·E와 같은 식(한 곳)

        /// <summary>작업 중단(부상·이탈·연결 끊김). 진행도는 감쇠로 넘어간다(§6.1과 동일).</summary>
        public void StopWork(ulong playerId) => _progress.Interrupt(playerId);

        /// <summary>
        /// 시간을 진전시킨다. 작업이 끝나면 <b>완료(부상 대기)</b>로 멈추고 <see cref="DrainTickResult.Completed"/>를
        /// 한 번 알린다 — 통과는 아직이다. 서버가 작업자의 정상 부상을 확인한 뒤 <see cref="BeginTransit"/>을
        /// 불러야 통과(1.5초)가 시작되고, 통과가 끝나는 틱에 <see cref="DrainTickResult.EscapedPlayer"/>가
        /// 그 플레이어를 담는다. 부상 도중 강제 부상(숨 0)이면 통과는 열리지 않고 완료만 유지된다.
        /// </summary>
        public DrainTickResult Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return default;

            if (IsInTransit)
            {
                _transitElapsed += deltaSeconds;
                if (_transitElapsed < DrainConfig.TransitSeconds)
                    return default;

                ulong escaped = _transitPlayer.Value;
                _transitPlayer = null;
                return new DrainTickResult(completed: false, escapedPlayer: escaped);
            }

            if (_completed)
                return default; // 부상 대기 — 통과는 BeginTransit이 연다

            _progress.Tick(deltaSeconds);

            if (_progress.State != ValveState.Open)
                return default;

            // 작업 완료 → 부상 대기. 역류 타이머는 쓰지 않는다(Open에 머물지 않는다).
            _completed = true;
            _progress.ResetForNewRound();

            return new DrainTickResult(completed: true, escapedPlayer: null);
        }

        /// <summary>
        /// 통과 중인 플레이어가 태그됐다(§6.5-2 "그 동안 태그 가능"). 통과가 취소된다 —
        /// 태그된 도망자는 메아리가 되므로 작업을 이어받을 사람이 없어진다.
        /// </summary>
        public void CancelTransit(ulong playerId)
        {
            if (_transitPlayer == playerId)
                _transitPlayer = null;
        }
    }

    /// <summary>한 틱에 일어난 배수구 전이. 1회성 신호다.</summary>
    public readonly struct DrainTickResult
    {
        /// <summary>이번 틱에 작업이 끝났다(부상 대기로 들어감). 통과는 아직이다.</summary>
        public readonly bool Completed;
        public readonly ulong? EscapedPlayer;

        public DrainTickResult(bool completed, ulong? escapedPlayer)
        {
            Completed = completed;
            EscapedPlayer = escapedPlayer;
        }
    }

    /// <summary>
    /// §6.5-2 활성 배수구 선택. <b>서버만</b> 굴리고 결과를 양 진영에 공개한다.
    /// <c>UnityEngine.Random</c>은 Core 계층 위반이므로 시드를 주입받는다.
    /// </summary>
    public static class DrainSelection
    {
        public static readonly DrainId[] All = { DrainId.MainPool, DrainId.KiddiePool };

        /// <summary>
        /// §6.5-2 "페이즈 진입 시 서버가 <b>무작위 1개</b> 선택". 2개소를 두는 이유는
        /// §10.5에 있다 — 술래가 페이즈 진입 전에 미리 자리를 잡지 못하게 하기 위함이다.
        /// </summary>
        public static DrainId Choose(int seed)
        {
            List<DrainId> picked = new DeterministicRandom(seed).Choose(All, 1);
            return picked[0];
        }

        /// <summary>
        /// §6.5-2 "게이트 개방 상태 — 활성화하지 않는다(기존 출구를 쓰면 된다)".
        /// </summary>
        public static bool ShouldActivate(bool gateOpen) => !gateOpen;
    }

    /// <summary>
    /// §6.5-2 배수구 위치를 Net이 어셈블리 경계를 넘어 읽게 해주는 지연 바인딩 지점.
    /// Presentation의 배수구 마커가 스스로 등록한다(<c>EscapePointRegistry</c>와 같은 패턴).
    ///
    /// <para>
    /// 서버는 이 좌표로 ① 페이즈 진입 알림 파문을 활성 배수구에서 발행하고 ② 작업 요청자가
    /// 배수구 근처에 있는지 재검증한다(GAP-24 — 클라이언트는 "작업한다"만 주장한다).
    /// </para>
    /// </summary>
    public static class DrainRegistry
    {
        private static readonly Dictionary<DrainId, Transform> Points = new Dictionary<DrainId, Transform>();

        public static int Count => Points.Count;

        public static void Register(DrainId id, Transform point)
        {
            if (point != null)
                Points[id] = point;
        }

        public static void Unregister(DrainId id, Transform point)
        {
            if (Points.TryGetValue(id, out Transform current) && current == point)
                Points.Remove(id);
        }

        public static bool TryGetPosition(DrainId id, out Vector3 position)
        {
            if (Points.TryGetValue(id, out Transform t) && t != null)
            {
                position = t.position;
                return true;
            }

            position = default;
            return false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession() => Points.Clear();
    }

    /// <summary>
    /// §6.1 [v0.4] 수중 작업 중 강제 파문. <b>2.5초 주기, 밸브 회전음과 같은 12m 등급</b>이며
    /// 새 SoundType을 만들지 않는다. §6.5-3이 배수구에도 적용한다 — *"침묵 탈출은 불가능하다"*.
    ///
    /// <para>
    /// 수면 차폐(×0.5, §5.6)를 지나 수면 위 6m, 술래 청취 기준 7.2m로 도달한다
    /// (블록 1-A②의 <c>UnderwaterValve_ThroughWaterSurface_ReachesSeekerAt7Point2</c>).
    /// </para>
    /// </summary>
    public sealed class UnderwaterWorkPulse
    {
        /// <summary>§6.1 "수중 밸브 조작 중에는 2.5초 주기로 파문이 강제 발생한다".</summary>
        public const float IntervalSeconds = 2.5f;

        private float _elapsed;
        private bool _working;

        /// <summary>
        /// 작업 여부를 알리고 시간을 진전시킨다. 이번 틱에 발행할 파문 수를 돌려준다.
        /// <b>작업을 시작한 순간 1회</b> 발행하고, 이후 2.5초마다 발행한다 —
        /// 시작 직후 2.5초간 침묵하면 짧게 끊어 치는 작업이 무음이 된다.
        /// </summary>
        public int Tick(bool working, float deltaSeconds)
        {
            if (!working)
            {
                _working = false;
                _elapsed = 0f;
                return 0;
            }

            if (!_working)
            {
                _working = true;
                _elapsed = 0f;
                return 1;
            }

            _elapsed += Mathf.Max(0f, deltaSeconds);
            int pulses = 0;
            while (_elapsed >= IntervalSeconds)
            {
                _elapsed -= IntervalSeconds;
                pulses++;
            }

            return pulses;
        }

        public void Reset()
        {
            _elapsed = 0f;
            _working = false;
        }
    }
}
