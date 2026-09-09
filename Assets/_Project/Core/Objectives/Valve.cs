using System;
using UnityEngine;
using Marco.Core.Role;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §6.1 밸브 상태기계의 순수 로직. MonoBehaviour도 FishNet도 모른다 —
    /// 소리 발생(§5.1 Valve 등급 12m)과 네트워크 동기화는 이 클래스가 발행하는
    /// 이벤트를 Net 레이어가 받아서 처리한다.
    ///
    /// 설계 결정:
    /// - GAP-5: 메아리(Echo)는 밸브를 돌릴 수 없다. 유령 상태이므로 물리 상호작용 불가.
    /// - §6.1: 완료 전 중단 시 진행도는 0으로 리셋된다(부분 진행 저장 없음, MVP 단순화).
    /// - §6.1: 회전 시작 시점에 소음이 1회 발생하며 지속시간 전체를 덮는다.
    ///   중간에 멈춰도 이미 발생한 소음은 취소되지 않으므로, RotationStarted에서만
    ///   펄스를 쏘고 중단 시에는 아무것도 하지 않는다.
    /// - §6.4: 밸브 자체는 잠기지 않는다. 상호작용자가 이탈하면 즉시 Closed로 돌아가
    ///   다른 생존 도망자가 바로 이어서 시작할 수 있다(연결 끊김도 Interrupt로 동일 처리).
    /// </summary>
    public sealed class Valve
    {
        /// <summary>§6.2 4인(MVP) 기준 회전 시간. 밸브별 값이 없을 때의 폴백이며 §6.1 밸브 A와 같다.</summary>
        public const float DefaultRotationSeconds = 3f;

        // ── §6.1 밸브별 회전 시간 차등 [기획서 갱신] ──────────────────────
        //
        // **미니게임을 추가하지 않는다.** 세 밸브의 차이는 회전 시간과 환경 규칙으로만 만든다.
        //
        // | 밸브 | 위치        | 회전 | 위험                    |
        // | A    | 기계실      | 3.0초 | 퇴로 없음(출입구 1개)   |
        // | B    | 풀 수중 3.5m | 2.0초 | 한 숨에 완료(숨 게이지) |
        // | C    | 물탱크실 2층 | 4.0초 | 이동 비용(금속 계단)    |
        //
        // 같은 12m 소음에 서로 다른 리스크를 붙인 것이 차등화의 근거다(§9.3).

        /// <summary>§6.1 밸브 A(기계실) 회전 시간. 기존 값과 동일하다.</summary>
        public const float ValveARotationSeconds = 3f;

        /// <summary>§6.1 밸브 B(메인 풀 수중 3.5m) 회전 시간. 잠수 4.0초 타임라인의 중간 단계다(§6.1-1).</summary>
        public const float ValveBRotationSeconds = 2f;

        /// <summary>§6.1 밸브 C(물탱크실, 2층) 회전 시간.</summary>
        public const float ValveCRotationSeconds = 4f;

        /// <summary>§6.2 6인 구간 보정(+25%). v1.x 대비 상수로만 남겨둔다.</summary>
        public const float SixPlayerRotationSeconds = 3.75f;

        /// <summary>§5.1 밸브 회전 소음 반경.</summary>
        public const float SoundRadiusMeters = 12f;

        private readonly float _rotationSeconds;
        private float _elapsedSeconds;

        public ValveState State { get; private set; } = ValveState.Closed;

        /// <summary>현재 회전 중인 플레이어. Rotating 상태가 아니면 null.</summary>
        public ulong? InteractorId { get; private set; }

        public float RotationSeconds => _rotationSeconds;

        public float Progress01 => _rotationSeconds <= 0f ? 1f : Mathf.Clamp01(_elapsedSeconds / _rotationSeconds);

        /// <summary>회전이 시작된 순간. Net 레이어가 여기서 §5.1 Valve 펄스를 발행한다.</summary>
        public event Action<Valve> RotationStarted;

        /// <summary>회전이 완료되어 Open이 된 순간. 라운드 승패 판정(§6.3)의 입력이 갱신된다.</summary>
        public event Action<Valve> Opened;

        public Valve(float rotationSeconds = DefaultRotationSeconds)
        {
            _rotationSeconds = rotationSeconds;
        }

        /// <summary>
        /// 회전 시작을 시도한다. 이미 열렸거나, 다른 플레이어가 회전 중이거나,
        /// 메아리가 시도하면 거부하고 false를 반환한다.
        /// </summary>
        public bool TryBeginRotation(ulong playerId, RoleType role)
        {
            // GAP-5: 메아리는 물리 상호작용 불가.
            if (role == RoleType.Echo)
                return false;

            if (State != ValveState.Closed)
                return false;

            State = ValveState.Rotating;
            InteractorId = playerId;
            _elapsedSeconds = 0f;

            RotationStarted?.Invoke(this);
            return true;
        }

        /// <summary>회전 중일 때만 진행도를 전진시킨다. 완료되면 Open으로 전이한다.</summary>
        public void Tick(float deltaSeconds)
        {
            if (State != ValveState.Rotating)
                return;

            if (deltaSeconds <= 0f)
                return;

            _elapsedSeconds += deltaSeconds;

            if (_elapsedSeconds < _rotationSeconds)
                return;

            _elapsedSeconds = _rotationSeconds;
            State = ValveState.Open;
            InteractorId = null;

            Opened?.Invoke(this);
        }

        /// <summary>
        /// 상호작용을 중단한다(§6.1 이탈 / §6.4 연결 끊김 모두 동일 처리).
        /// 회전 중인 본인만 중단할 수 있으며, 성공 시 진행도가 0으로 리셋된다.
        /// </summary>
        public bool Interrupt(ulong playerId)
        {
            if (State != ValveState.Rotating)
                return false;

            if (InteractorId != playerId)
                return false;

            State = ValveState.Closed;
            InteractorId = null;
            _elapsedSeconds = 0f;
            return true;
        }
    }
}
