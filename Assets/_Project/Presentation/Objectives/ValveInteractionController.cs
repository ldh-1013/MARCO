using UnityEngine;
using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Presentation.Objectives
{
    /// <summary>이번 틱에 상호작용에서 벌어진 일. 로그·연출이 구독할 수 있게 값으로 돌려준다.</summary>
    public enum ValveInteractionEvent
    {
        /// <summary>아무 일도 없음(범위 밖, 입력 없음, 이미 열린 밸브 등).</summary>
        None,

        /// <summary>회전 시작 — 이 시점에 §5.1 밸브 소음(12m/3초)을 발행해야 한다.</summary>
        Started,

        /// <summary>회전 진행 중.</summary>
        Progressing,

        /// <summary>§6.1 "완료 전 이탈": E를 뗌 → 진행도 0 리셋.</summary>
        CancelledByRelease,

        /// <summary>§6.1 "완료 전 이탈": 상호작용 범위를 벗어남 → 진행도 0 리셋.</summary>
        CancelledByRangeExit,

        /// <summary>회전 완료 → Open.</summary>
        Completed,

        /// <summary>Core가 시작을 거부함(GAP-5 메아리 등). 역할 제약 확인용.</summary>
        Rejected
    }

    /// <summary>한 틱분 입력. 씬 스캔(최근접 밸브 찾기)은 호출자 책임이다.</summary>
    public readonly struct ValveInteractionInput
    {
        public readonly ulong PlayerId;
        public readonly RoleType Role;
        public readonly Vector3 PlayerPosition;

        /// <summary>§4.3 상호작용 키(E) 홀드 상태.</summary>
        public readonly bool InteractHeld;

        /// <summary>가장 가까운 밸브. 없으면 null.</summary>
        public readonly Valve Candidate;
        public readonly Vector3 CandidatePosition;

        public ValveInteractionInput(ulong playerId, RoleType role, Vector3 playerPosition,
            bool interactHeld, Valve candidate, Vector3 candidatePosition)
        {
            PlayerId = playerId;
            Role = role;
            PlayerPosition = playerPosition;
            InteractHeld = interactHeld;
            Candidate = candidate;
            CandidatePosition = candidatePosition;
        }
    }

    /// <summary>
    /// §4.3 E 홀드 입력을 §6.1 밸브 상태기계에 연결하는 순수 배선 로직.
    /// Unity 수명주기에 의존하지 않아 EditMode 테스트로 취소 규칙 전체를 고정할 수 있다.
    ///
    /// **역할 제약은 여기서 검사하지 않는다.** GAP-5(메아리 밸브 조작 불가)는 Core의
    /// <see cref="Valve.TryBeginRotation"/>이 이미 강제하므로, 중복 검사 대신 Core의
    /// 거부를 <see cref="ValveInteractionEvent.Rejected"/>로 그대로 전달한다 —
    /// 규칙이 두 곳에 흩어져 나중에 어긋나는 것을 막기 위함.
    ///
    /// 새 게임플레이 규칙은 추가하지 않는다. 회전 시간·진행도 리셋·잠금 여부는
    /// 전부 Core `Valve`가 이미 구현한 것을 그대로 쓴다.
    /// </summary>
    public sealed class ValveInteractionController
    {
        /// <summary>
        /// GAP-10 결정: 기획서에 밸브 상호작용 거리 수치가 없어 이 값을 둔다.
        /// 근거 — §14.1 태그 판정 1.2m(접촉급)보다는 넉넉해야 밸브 앞에 서서 누를 수 있고,
        /// 캐릭터 콜라이더 0.35m + 밸브 큐브 0.6m를 감안하면 2.5m면 "바로 옆"에 해당한다.
        /// 플레이테스트 조정 대상이라 생성자로 주입 가능하게 열어 둔다.
        /// </summary>
        public const float DefaultInteractionRange = 2.5f;

        private readonly float _interactionRange;
        private Valve _activeValve;
        private Vector3 _activeValvePosition;

        public ValveInteractionController(float interactionRange = DefaultInteractionRange)
        {
            _interactionRange = interactionRange;
        }

        public Valve ActiveValve => _activeValve;

        /// <summary>진행 중인 회전의 진행도(0~1). 없으면 0.</summary>
        public float Progress01 => _activeValve?.Progress01 ?? 0f;

        public ValveInteractionEvent Tick(in ValveInteractionInput input, float deltaSeconds)
        {
            if (_activeValve != null)
                return TickActive(input, deltaSeconds);

            return TryStart(input);
        }

        private ValveInteractionEvent TickActive(in ValveInteractionInput input, float deltaSeconds)
        {
            // §6.1 "완료 전 이탈 시 리셋" — GAP-9 결정에 따라 이탈은 두 가지다.
            if (!input.InteractHeld)
                return Cancel(input.PlayerId, ValveInteractionEvent.CancelledByRelease);

            if (Vector3.Distance(input.PlayerPosition, _activeValvePosition) > _interactionRange)
                return Cancel(input.PlayerId, ValveInteractionEvent.CancelledByRangeExit);

            _activeValve.Tick(deltaSeconds);

            if (_activeValve.State == ValveState.Open)
            {
                _activeValve = null;
                return ValveInteractionEvent.Completed;
            }

            return ValveInteractionEvent.Progressing;
        }

        private ValveInteractionEvent TryStart(in ValveInteractionInput input)
        {
            if (!input.InteractHeld || input.Candidate == null)
                return ValveInteractionEvent.None;

            if (Vector3.Distance(input.PlayerPosition, input.CandidatePosition) > _interactionRange)
                return ValveInteractionEvent.None;

            // 이미 열렸거나 다른 플레이어가 돌리는 중이면 조용히 무시한다.
            // (Rejected는 역할 제약 같은 "알려줄 가치가 있는 거부"에만 쓴다.)
            if (input.Candidate.State != ValveState.Closed)
                return ValveInteractionEvent.None;

            if (input.Candidate.TryBeginRotation(input.PlayerId, input.Role))
            {
                _activeValve = input.Candidate;
                _activeValvePosition = input.CandidatePosition;
                return ValveInteractionEvent.Started;
            }

            return ValveInteractionEvent.Rejected;
        }

        private ValveInteractionEvent Cancel(ulong playerId, ValveInteractionEvent reason)
        {
            _activeValve.Interrupt(playerId); // Core가 진행도를 0으로 리셋한다(§6.1)
            _activeValve = null;
            return reason;
        }
    }
}
