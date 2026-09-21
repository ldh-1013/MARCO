using System;
using System.Collections.Generic;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.1 [v0.4 전면 교체] 밸브 상태기계의 순수 로직. MonoBehaviour도 FishNet도 모른다 —
    /// 소리 발생(§5.1 Valve 등급 12m)과 네트워크 동기화는 이 클래스가 발행하는
    /// 이벤트를 Net 레이어가 받아서 처리한다.
    ///
    /// <code>
    /// [Closed] --interact--> [Rotating] --진행도 1.0--> [Open] --180초--> [Reflowing] --30초--> [Closed]
    ///    ▲  ▲                    │                                            ▲
    ///    │  └─중단(유예 3초 → -0.10/s)─┘  (0.0 도달 시에만 Closed)              └─재회전 완료
    /// </code>
    ///
    /// <para>
    /// <b>v0.3에서 바뀐 것 세 가지.</b>
    /// ① 중단 시 진행도가 <b>0으로 리셋되지 않는다</b> — 3초 유예 후 초당 0.10씩 감쇠한다.
    ///    만충에서 유예 포함 13초 만에 전손된다. 그래서 다른 도망자가 이어받을 수 있다.
    /// ② 개방된 밸브는 180초 뒤 <b>스스로 다시 닫힌다</b>(역류, §6.1-2).
    /// ③ 진행도를 <b>0.0~1.0 정규화</b>로 다룬다. 초 단위로 다루면 밸브마다 배분이 달라
    ///    이어받기 계산이 틀어진다.
    /// </para>
    ///
    /// <para>
    /// <b>★ 감쇠와 역류는 절대 한 타이머로 합치지 않는다.</b> 감쇠는 <i>회전 중이던</i>
    /// 밸브의 진행도에만 적용되고, 역류는 <i>이미 열린</i> 밸브의 유지 시간에만 적용된다.
    /// 둘은 상태가 배타적이라(Rotating vs Open) 동시에 돌 수 없으며, 한 필드로 합치면
    /// "개방 직후 감쇠가 시작되는" 버그가 조용히 생긴다.
    /// </para>
    ///
    /// <para>
    /// 설계 결정:
    /// GAP-5 메아리는 밸브를 돌릴 수 없다(유령 상태, 물리 상호작용 불가) /
    /// §6.1 회전 중 소음은 시작 시점에 1회 발행되고 중간에 멈춰도 취소되지 않는다 /
    /// §6.4 상호작용자 이탈·연결 끊김은 동일하게 중단으로 처리한다.
    /// </para>
    /// </summary>
    public sealed class Valve
    {
        /// <summary>
        /// §6.2 밸브별 값이 없을 때의 폴백 회전 시간. <b>v0.3의 값(3초)을 그대로 남긴다</b> —
        /// 기존 테스트와 씬 프리팹의 기본값이 이것을 참조하고 있으며, v0.4 밸브는
        /// <see cref="ValveOccupancy.RotateSeconds"/>로 자기 시간을 갖는다.
        /// </summary>
        public const float DefaultRotationSeconds = 3f;

        // [블록 7 · 더블체크 8] 밸브별 회전 시간 상수 5개(A 8 / B 5 / C 8 / D 8 / E 7)와 v0.3의
        //   6인 보정(3.75)을 지웠다. 값은 ValveOccupancy.RotateSeconds 한 곳이 소유하며, 이 상수들은
        //   호출부가 0곳이었다(주석 1곳뿐) — 두 곳에 적힌 같은 값은 한쪽만 고쳐지는 순간 어긋난다.

        /// <summary>§5.1 밸브 회전 소음 반경. 밸브 A는 §6.1대로 ×0.5가 적용된다.</summary>
        public const float SoundRadiusMeters = 12f;

        // ── §6.1 진행도 감쇠 ────────────────────────────────────────────

        /// <summary>§6.1 "작업 중단 3초 유예".</summary>
        public const float DecayGraceSeconds = 3f;

        /// <summary>
        /// §6.1 "이후 -0.10/sec". <b>정규화 진행도 기준</b>이라 밸브 배분과 무관하게 같다.
        /// 만충에서 10초, 유예를 포함해 13초에 전손된다.
        /// </summary>
        public const float DecayPerSecond = 0.10f;

        /// <summary>만충에서 전손까지 걸리는 시간(초). 유도값 — 3 + 1/0.10 = 13.0.</summary>
        public static float FullDecaySeconds => DecayGraceSeconds + 1f / DecayPerSecond;

        // ── §6.1-2 역류 ─────────────────────────────────────────────────

        /// <summary>§6.1-2 "개방 유지(유예) 180초". 테스트 범위 160~210초.</summary>
        public const float OpenHoldSeconds = 180f;

        /// <summary>§6.1-2 "역류 진행 30초". 되돌릴 수 있는 경고 창이다.</summary>
        public const float ReflowSeconds = 30f;

        /// <summary>§6.1-2 역류 물소리 파문 간격. "10초마다 1회, 총 3회".</summary>
        public const float ReflowPulseIntervalSeconds = 10f;

        /// <summary>
        /// 역류 구간 물소리 파문 횟수. <b>유도값</b> — 30초 ÷ 10초 = 3회.
        /// 역류 시작(0초)·10초·20초에 발행한다. 30초는 폐쇄 시점이라 쓰지 않는다.
        /// </summary>
        public static int ReflowPulseCount => (int)(ReflowSeconds / ReflowPulseIntervalSeconds);

        private readonly float _rotateSeconds;
        private readonly HashSet<ulong> _interactors = new HashSet<ulong>();

        private float _progress01;
        private float _sinceInterrupt;   // 중단 후 경과(유예 판정)
        private float _sinceOpen;        // Open 진입 후 경과(역류 판정)
        private float _sinceReflow;      // Reflowing 진입 후 경과
        private int _reflowPulsesFired;

        public Valve(float rotationSeconds = DefaultRotationSeconds)
        {
            _rotateSeconds = rotationSeconds;
        }

        /// <summary>§10.2 밸브 식별자. 씬 배선 전에는 null일 수 있다(폴백 생성자 경로).</summary>
        public ValveId? Id { get; private set; }

        /// <summary>§6.1-0 이번 라운드에 활성인가. 비활성 밸브는 상호작용을 거부한다.</summary>
        public bool IsActive { get; private set; } = true;

        public ValveState State { get; private set; } = ValveState.Closed;

        /// <summary>회전 시간(초). 진행도의 분모다.</summary>
        public float RotationSeconds => _rotateSeconds;

        /// <summary>§6.1 정규화 진행도 0.0~1.0. 회전·감쇠·역류 전부 이 값을 쓴다.</summary>
        public float Progress01 => _progress01;

        /// <summary>
        /// §6.1 지금 감쇠 중인가 — 중단됐고 유예가 끝났고 진행도가 남아 있다.
        /// <b>HUD가 색을 달리해야 하므로(§12.4) 플래그로 공개한다</b> — 이건 연출이 아니라
        /// 정보다. 구분이 안 되면 "지금 뺄까 더 돌릴까" 판단 자체가 불가능해진다.
        /// </summary>
        public bool IsDecaying => State == ValveState.Closed
                                  && _progress01 > 0f
                                  && _sinceInterrupt >= DecayGraceSeconds;

        /// <summary>중단된 진행도가 남아 있는가(유예 중 포함). 이어받기가 가능한 상태다.</summary>
        public bool HasPartialProgress => State == ValveState.Closed && _progress01 > 0f;

        /// <summary>감쇠 시작까지 남은 유예(초). 감쇠가 이미 시작됐으면 0.</summary>
        public float DecayGraceRemaining =>
            HasPartialProgress ? Mathf.Max(0f, DecayGraceSeconds - _sinceInterrupt) : 0f;

        /// <summary>§6.1-2 역류까지 남은 시간(초). Open이 아니면 0.</summary>
        public float OpenHoldRemaining =>
            State == ValveState.Open ? Mathf.Max(0f, OpenHoldSeconds - _sinceOpen) : 0f;

        /// <summary>
        /// §6.1-2 완전 폐쇄까지 남은 시간(초). Reflowing이 아니면 0.
        /// <b>시작 시 1회만 동기화하고 클라이언트가 카운트다운한다</b>(§14.3).
        /// </summary>
        public float ReflowRemaining =>
            State == ValveState.Reflowing ? Mathf.Max(0f, ReflowSeconds - _sinceReflow) : 0f;

        /// <summary>현재 회전에 참여 중인 인원. §6.1 동시 작업 배율의 입력이다.</summary>
        public int InteractorCount => _interactors.Count;

        /// <summary>
        /// 현재 회전 중인 대표 플레이어. 동시 작업에서는 임의의 한 명이며,
        /// 기존 호출부(로그·HUD)의 호환을 위해 남긴다. 회전 중이 아니면 null.
        /// </summary>
        public ulong? InteractorId
        {
            get
            {
                foreach (ulong id in _interactors)
                    return id;

                return null;
            }
        }

        /// <summary>이 플레이어가 지금 이 밸브를 돌리고 있는가.</summary>
        public bool IsInteracting(ulong playerId) => _interactors.Contains(playerId);

        /// <summary>회전이 시작된 순간. Net 레이어가 여기서 §5.1 Valve 펄스를 발행한다.</summary>
        public event Action<Valve> RotationStarted;

        /// <summary>회전이 완료되어 Open이 된 순간. §6.3 승리 판정과 게이트 집계의 입력이다.</summary>
        public event Action<Valve> Opened;

        /// <summary>§6.1-2 역류가 시작된 순간(Open → Reflowing).</summary>
        public event Action<Valve> ReflowStarted;

        /// <summary>
        /// §6.1-2 역류 구간 물소리 파문 시점(총 3회). Net 레이어가 §5.1 Valve 등급으로 발행한다 —
        /// <b>새 SoundType을 만들지 않는다</b>(§3.3 금지).
        /// </summary>
        public event Action<Valve> ReflowPulse;

        /// <summary>
        /// 완전히 닫힌 순간. 역류 만료(진행도 0) 또는 감쇠 0.0 도달 — <b>같은 종착점이다</b>(§6.1-2).
        /// </summary>
        public event Action<Valve> Closed;

        /// <summary>씬 배선이 밸브 식별자와 활성 여부를 심는다.</summary>
        public void Configure(ValveId id)
        {
            Id = id;
        }

        /// <summary>
        /// §6.1-0 이번 라운드 활성 여부를 설정한다. 서버만 호출한다.
        /// 비활성으로 바뀌면 진행 중이던 회전도 중단된다.
        /// </summary>
        public void SetActive(bool active)
        {
            IsActive = active;
            if (active)
                return;

            _interactors.Clear();
            if (State == ValveState.Rotating)
            {
                State = ValveState.Closed;
                _sinceInterrupt = 0f;
            }
        }

        /// <summary>
        /// 회전 시작을 시도한다. <see cref="ValveInteractionRejection.None"/>이면 성공이다.
        ///
        /// <para>
        /// <b>Reflowing에서도 시작할 수 있다</b>(§6.1-2 "역류 중 재상호작용"). 그때는
        /// 역류 타이머를 버리고 진행도 0에서 새로 돈다 — §6.1-2가 <i>"회전 시간은 최초와
        /// 동일"</i> 이라고 못박았기 때문이다. 감쇠 중(Closed + 진행도 잔존)에서 시작하면
        /// <b>그 시점 값에서 이어진다</b>(0부터가 아니다).
        /// </para>
        /// </summary>
        /// <summary>
        /// <see cref="TryInteract"/>가 지금 거부할지 <b>상태를 바꾸지 않고</b> 판정한다. 수중 밸브는 진입(하강)을
        /// 마친 뒤에야 손을 대므로, 하강을 시작하기 전에 "어차피 거부될 작업"을 걸러야 한다
        /// (<see cref="UnderwaterWorkSession"/>). 거부 조건은 여기 한 곳이고 <see cref="TryInteract"/>도 이것을 쓴다.
        /// </summary>
        public ValveInteractionRejection CheckInteract(RoleType role)
        {
            // GAP-5: 메아리는 물리 상호작용 불가.
            if (role == RoleType.Echo)
                return ValveInteractionRejection.EchoCannotInteract;

            if (!IsActive)
                return ValveInteractionRejection.NotActiveThisRound;

            if (State == ValveState.Open)
                return ValveInteractionRejection.AlreadyOpen;

            return ValveInteractionRejection.None;
        }

        public ValveInteractionRejection TryInteract(ulong playerId, RoleType role)
        {
            ValveInteractionRejection rejection = CheckInteract(role);
            if (rejection != ValveInteractionRejection.None)
                return rejection;

            if (State == ValveState.Rotating)
            {
                // 동시 작업(§6.1). 이미 참여 중이면 그대로 유지한다 —
                // 매 프레임 오는 홀드 신호가 회전을 리셋하지 않게 한다.
                _interactors.Add(playerId);
                return ValveInteractionRejection.None;
            }

            if (State == ValveState.Reflowing)
            {
                // §6.1-2: 처음부터 다시 돈다. 역류 타이머는 버린다.
                _progress01 = 0f;
                _sinceReflow = 0f;
                _reflowPulsesFired = 0;
            }

            State = ValveState.Rotating;
            _sinceInterrupt = 0f;
            _interactors.Add(playerId);

            RotationStarted?.Invoke(this);
            return ValveInteractionRejection.None;
        }

        /// <summary>v0.3 호환 진입점. 성공 여부만 필요한 호출부·테스트가 쓴다.</summary>
        public bool TryBeginRotation(ulong playerId, RoleType role)
        {
            return TryInteract(playerId, role) == ValveInteractionRejection.None;
        }

        /// <summary>
        /// 시간을 진전시킨다. <b>모든 상태에서 호출해야 한다</b> —
        /// Rotating일 때만 부르면 감쇠도 역류도 영영 작동하지 않는다.
        /// </summary>
        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return;

            switch (State)
            {
                case ValveState.Rotating:
                    TickRotating(deltaSeconds);
                    break;

                case ValveState.Closed:
                    TickDecay(deltaSeconds);
                    break;

                case ValveState.Open:
                    TickOpenHold(deltaSeconds);
                    break;

                case ValveState.Reflowing:
                    TickReflow(deltaSeconds);
                    break;
            }
        }

        private void TickRotating(float deltaSeconds)
        {
            if (_rotateSeconds <= 0f)
            {
                CompleteRotation();
                return;
            }

            float rate = ValveOccupancy.ConcurrencyMultiplier(_interactors.Count) / _rotateSeconds;
            _progress01 += rate * deltaSeconds;

            if (_progress01 < 1f)
                return;

            CompleteRotation();
        }

        private void CompleteRotation()
        {
            _progress01 = 1f;
            State = ValveState.Open;
            _interactors.Clear();
            _sinceOpen = 0f;
            _sinceReflow = 0f;
            _reflowPulsesFired = 0;

            Opened?.Invoke(this);
        }

        /// <summary>§6.1 중단된 진행도의 감쇠. 유예 3초가 지난 뒤부터 깎인다.</summary>
        private void TickDecay(float deltaSeconds)
        {
            if (_progress01 <= 0f)
                return;

            _sinceInterrupt += deltaSeconds;

            float overGrace = _sinceInterrupt - DecayGraceSeconds;
            if (overGrace <= 0f)
                return;

            // 유예를 막 넘긴 틱에서는 넘긴 만큼만 깎는다 — 유예 경계가 프레임률에 흔들리지 않게.
            float decaySeconds = Mathf.Min(overGrace, deltaSeconds);
            _progress01 -= DecayPerSecond * decaySeconds;

            if (_progress01 > 0f)
                return;

            _progress01 = 0f;
            Closed?.Invoke(this);
        }

        /// <summary>§6.1-2 개방 유지 180초. 만료되면 역류가 시작된다.</summary>
        private void TickOpenHold(float deltaSeconds)
        {
            _sinceOpen += deltaSeconds;
            if (_sinceOpen < OpenHoldSeconds)
                return;

            State = ValveState.Reflowing;
            _sinceReflow = 0f;
            _reflowPulsesFired = 0;

            // §6.1-2 "역류 중 재상호작용해 회전 완료 → Open 복귀 + 타이머 리셋
            //          (회전 시간은 최초와 동일)" — 그래서 진행도를 0으로 내린다.
            //          역류 잔여 30초는 Progress01이 아니라 ReflowRemaining이 들고 있다.
            _progress01 = 0f;

            ReflowStarted?.Invoke(this);
            FireReflowPulseIfDue();
        }

        /// <summary>§6.1-2 역류 30초. 만료되면 완전 폐쇄(진행도 0).</summary>
        private void TickReflow(float deltaSeconds)
        {
            _sinceReflow += deltaSeconds;
            FireReflowPulseIfDue();

            if (_sinceReflow < ReflowSeconds)
                return;

            State = ValveState.Closed;
            _progress01 = 0f;
            _sinceInterrupt = 0f;
            _sinceReflow = 0f;

            Closed?.Invoke(this);
        }

        /// <summary>
        /// §6.1-2 "10초마다 1회, 총 3회". 역류 시작(0초)·10초·20초에 발행한다 —
        /// 30초는 폐쇄 시점이므로 그때 울리면 경고가 아니라 사후 통보가 된다.
        /// </summary>
        private void FireReflowPulseIfDue()
        {
            while (_reflowPulsesFired < ReflowPulseCount
                   && _sinceReflow >= _reflowPulsesFired * ReflowPulseIntervalSeconds)
            {
                _reflowPulsesFired++;
                ReflowPulse?.Invoke(this);
            }
        }

        /// <summary>
        /// 상호작용을 중단한다(§6.1 이탈 / §6.4 연결 끊김 / §6.1 수중 밸브 부상 모두 동일).
        ///
        /// <para>
        /// <b>진행도는 유지된다</b> — v0.3처럼 0으로 리셋되지 않는다. 마지막 참여자가
        /// 빠지면 Closed로 내려가고 <see cref="DecayGraceSeconds"/> 뒤부터 감쇠가 시작된다.
        /// 동시 작업 중 한 명만 빠지면 회전은 계속되고 배율만 낮아진다.
        /// </para>
        /// </summary>
        public bool Interrupt(ulong playerId)
        {
            if (State != ValveState.Rotating)
                return false;

            if (!_interactors.Remove(playerId))
                return false;

            if (_interactors.Count > 0)
                return true; // 남은 인원이 계속 돈다 — 배율만 떨어진다

            State = ValveState.Closed;
            _sinceInterrupt = 0f;
            return true;
        }

        /// <summary>
        /// 새 라운드를 위한 초기화. <b>Open 진입 시각과 진행도가 전부 리셋된다</b> —
        /// 지난 라운드의 역류 타이머가 살아남으면 새 라운드에서 밸브가 갑자기 닫힌다.
        /// </summary>
        public void ResetForNewRound(bool active = true)
        {
            State = ValveState.Closed;
            IsActive = active;
            _interactors.Clear();
            _progress01 = 0f;
            _sinceInterrupt = 0f;
            _sinceOpen = 0f;
            _sinceReflow = 0f;
            _reflowPulsesFired = 0;
        }
    }

    /// <summary>
    /// §6.1-0 상호작용 거부 사유. <b>사유를 구분해 돌려주는 이유</b>는 HUD가
    /// "이번 라운드 비활성"과 "이미 열림"과 "메아리는 불가"를 다르게 말해야 하기 때문이다.
    /// </summary>
    public enum ValveInteractionRejection
    {
        /// <summary>성공.</summary>
        None,

        /// <summary>GAP-5 메아리는 물리 상호작용 불가.</summary>
        EchoCannotInteract,

        /// <summary>§6.1-0 이번 라운드 비활성 밸브. 잠금 표시된다.</summary>
        NotActiveThisRound,

        /// <summary>이미 개방됨.</summary>
        AlreadyOpen,
    }
}
