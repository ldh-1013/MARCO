using System;
using Marco.Core.Role;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// 서버 권위 밸브 구동기(스프린트 10, v0.4 갱신). 서버에서만 존재하며, 클라이언트가 보낸
    /// 홀드/해제 의사를 받아 <b>서버가 직접 검증·타이밍</b>한다.
    ///
    /// <para>
    /// <b>핵심 불변식(서버 권위)</b>: 밸브 개방은 오직 이 구동기의 <see cref="Tick"/>가
    /// 회전 시간 전체를 진전시켜야만 일어난다. 클라이언트는 "완료됐다"는 메시지를 보낼
    /// 수단이 없고 홀드 의사만 전달하므로, <b>어떤 클라이언트도 밸브를 즉시 열 수 없다</b>.
    /// </para>
    ///
    /// <para>
    /// <b>★ v0.4에서 Tick의 계약이 바뀌었다.</b> 이전에는 "회전 중일 때만 유효"했지만
    /// 이제는 <b>모든 상태에서 매 프레임 호출해야 한다</b> — §6.1 진행도 감쇠와
    /// §6.1-2 역류가 Rotating이 아닌 상태에서 돌기 때문이다. 호출부가 이전처럼
    /// <c>if (!IsRotating) return;</c> 으로 막고 있으면 <b>감쇠도 역류도 영영 작동하지 않는다.</b>
    /// 그래서 <see cref="Tick"/>은 이제 개방 여부가 아니라 <see cref="ValveTickResult"/>를
    /// 돌려준다 — 호출부가 무엇을 전파해야 하는지 구조체가 말해 준다.
    /// </para>
    ///
    /// <para>
    /// <b>기존 판정 재사용</b>: 새 규칙을 만들지 않는다. 역할 제약(GAP-5)·상태 전이·
    /// 진행도·감쇠·역류는 전부 Core <see cref="Valve"/>가 구현한 것을 그대로 호출한다.
    /// </para>
    ///
    /// FishNet도 UnityEngine도 모른다 — MonoBehaviour 없이 EditMode 테스트가 가능하다.
    /// </summary>
    public sealed class ServerValveDriver
    {
        private readonly Valve _valve;

        private bool _sawOpened;
        private bool _sawReflowStarted;
        private bool _sawClosed;
        private int _reflowPulses;

        public ServerValveDriver(Valve valve)
        {
            _valve = valve ?? throw new ArgumentNullException(nameof(valve));

            _valve.Opened += OnOpened;
            _valve.ReflowStarted += OnReflowStarted;
            _valve.ReflowPulse += OnReflowPulse;
            _valve.Closed += OnClosed;
        }

        public ValveState State => _valve.State;
        public float Progress01 => _valve.Progress01;

        /// <summary>§6.1 감쇠 중인가. HUD가 색을 달리해야 하므로 네트워크로 나간다(§12.4).</summary>
        public bool IsDecaying => _valve.IsDecaying;

        /// <summary>§6.1-2 역류 잔여(초). 시작 시 1회만 보내고 클라이언트가 카운트다운한다.</summary>
        public float ReflowRemaining => _valve.ReflowRemaining;

        /// <summary>현재 회전 중인 대표 홀더. 회전 중이 아니면 null.</summary>
        public ulong? HolderId => _valve.InteractorId;

        /// <summary>§6.1 동시 작업 인원. 배율의 입력이다.</summary>
        public int HolderCount => _valve.InteractorCount;

        /// <summary>서버가 회전 타이머를 진전시켜야 하는 상태인가(누군가 홀드 중).</summary>
        public bool IsRotating => _valve.State == ValveState.Rotating;

        /// <summary>§6.1-0 이번 라운드 활성 여부.</summary>
        public bool IsActive => _valve.IsActive;

        public void SetActive(bool active) => _valve.SetActive(active);

        /// <summary>
        /// 클라이언트의 홀드 요청을 처리한다. 거부 사유를 그대로 돌려준다 —
        /// §6.1-0 비활성 밸브와 이미 열린 밸브를 HUD가 다르게 말해야 하기 때문이다.
        ///
        /// <para>
        /// 이미 이 플레이어가 회전 중이면 재요청은 그대로 유지한다 —
        /// 프레임마다 오는 홀드 신호가 회전을 리셋하지 않게 한다.
        /// </para>
        /// </summary>
        public ValveInteractionRejection BeginHold(ulong playerId, RoleType role)
        {
            return _valve.TryInteract(playerId, role);
        }

        /// <summary>
        /// 홀드 해제(§6.1 이탈 / §6.4 연결 끊김 / 수중 밸브 부상 동일 처리).
        /// <b>진행도는 리셋되지 않고 감쇠로 넘어간다</b>(§6.1 [v0.4]).
        /// </summary>
        public void EndHold(ulong playerId) => _valve.Interrupt(playerId);

        /// <summary>
        /// 서버의 권위 타이머를 진전시킨다. <b>상태와 무관하게 매 프레임 호출한다.</b>
        /// 이번 틱에 일어난 전이를 구조체로 돌려주며, 각 플래그는 <b>1회성</b>이다.
        /// </summary>
        public ValveTickResult Tick(float deltaSeconds)
        {
            _sawOpened = false;
            _sawReflowStarted = false;
            _sawClosed = false;
            _reflowPulses = 0;

            _valve.Tick(deltaSeconds);

            return new ValveTickResult(_sawOpened, _sawReflowStarted, _sawClosed, _reflowPulses);
        }

        /// <summary>
        /// 새 라운드 초기화. <b>Open 진입 시각·역류 타이머·진행도가 전부 리셋된다.</b>
        /// <c>IValveHost.ResetValveForNewRound</c>처럼 인스턴스를 새로 만들지 않아도 되도록
        /// §6.1 [v0.4]의 <see cref="Valve.ResetForNewRound"/>를 그대로 위임한다 —
        /// 인스턴스를 교체하면 구독한 이벤트가 끊어져 감쇠·역류 전파가 조용히 사라진다.
        /// </summary>
        public void ResetForNewRound(bool active = true) => _valve.ResetForNewRound(active);

        private void OnOpened(Valve _) => _sawOpened = true;
        private void OnReflowStarted(Valve _) => _sawReflowStarted = true;
        private void OnReflowPulse(Valve _) => _reflowPulses++;
        private void OnClosed(Valve _) => _sawClosed = true;
    }

    /// <summary>
    /// 한 틱에 일어난 §6.1 밸브 전이. <b>전부 1회성 신호</b>이며 호출부는 이것만 보고
    /// 네트워크 이벤트를 발행한다 — 상태를 매 프레임 비교하는 코드를 없애기 위함이다.
    /// </summary>
    public readonly struct ValveTickResult
    {
        /// <summary>회전이 완료돼 Open이 됐다.</summary>
        public readonly bool Opened;

        /// <summary>§6.1-2 개방 유지 180초가 끝나 역류가 시작됐다.</summary>
        public readonly bool ReflowStarted;

        /// <summary>완전히 닫혔다 — 역류 만료 또는 감쇠 0.0 도달(§6.1-2 같은 종착점).</summary>
        public readonly bool Closed;

        /// <summary>이번 틱에 발행할 역류 물소리 파문 횟수(보통 0 또는 1).</summary>
        public readonly int ReflowPulses;

        public ValveTickResult(bool opened, bool reflowStarted, bool closed, int reflowPulses)
        {
            Opened = opened;
            ReflowStarted = reflowStarted;
            Closed = closed;
            ReflowPulses = reflowPulses;
        }

        /// <summary>전파할 것이 하나라도 있는가.</summary>
        public bool HasAny => Opened || ReflowStarted || Closed || ReflowPulses > 0;
    }
}
