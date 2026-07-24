using System;
using Marco.Core.Role;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// 서버 권위 밸브 구동기(스프린트 10). 서버에서만 존재하며, 클라이언트가 보낸
    /// 홀드/해제 의사를 받아 <b>서버가 직접 검증·타이밍</b>한다.
    ///
    /// **핵심 불변식(서버 권위)**: 밸브 개방은 오직 이 구동기의 <see cref="Tick"/>가
    /// 회전 시간 전체를 진전시켜야만 일어난다. 클라이언트는 "완료됐다"는 메시지를 보낼
    /// 수단이 없고 홀드 의사만 전달하므로, <b>어떤 클라이언트도 밸브를 즉시 열 수 없다</b>.
    /// 이 성질을 <c>ServerValveDriverTests</c>가 고정한다.
    ///
    /// **기존 판정 재사용**: 새 규칙을 만들지 않는다. 역할 제약(GAP-5 메아리 거부)·상태 전이·
    /// 진행도 리셋은 전부 Core <see cref="Valve"/>가 이미 구현한 것을 그대로 호출한다.
    /// 여기서 추가되는 것은 "현재 누가 홀드 중인가"의 서버 측 추적뿐이다.
    ///
    /// FishNet도 UnityEngine도 모른다 — MonoBehaviour 없이 EditMode 테스트가 가능하다.
    /// </summary>
    public sealed class ServerValveDriver
    {
        private readonly Valve _valve;

        public ServerValveDriver(Valve valve)
        {
            _valve = valve ?? throw new ArgumentNullException(nameof(valve));
        }

        public ValveState State => _valve.State;
        public float Progress01 => _valve.Progress01;

        /// <summary>현재 회전 중인 홀더. 회전 중이 아니면 null.</summary>
        public ulong? HolderId => _valve.InteractorId;

        /// <summary>서버가 시간을 진전시켜야 하는 상태인가(누군가 홀드 중).</summary>
        public bool IsRotating => _valve.State == ValveState.Rotating;

        /// <summary>
        /// 클라이언트의 홀드 요청을 처리한다. 이 플레이어가 홀드에 성공(또는 이미 홀드 중)이면
        /// true. 역할·상태 검증은 <see cref="Valve.TryBeginRotation"/>에 위임한다.
        ///
        /// 이미 이 플레이어가 회전 중이면 재요청은 그대로 유지(true)한다 —
        /// 프레임마다 오는 홀드 신호가 회전을 리셋하지 않게 한다.
        /// </summary>
        public bool BeginHold(ulong playerId, RoleType role)
        {
            if (_valve.State == ValveState.Rotating)
                return _valve.InteractorId == playerId;

            if (_valve.State == ValveState.Open)
                return false;

            // Closed → 역할(GAP-5)·상태 검증은 Core Valve가 강제한다.
            return _valve.TryBeginRotation(playerId, role);
        }

        /// <summary>
        /// 홀드 해제(§6.1 이탈 / §6.4 연결 끊김 동일 처리). 회전 중인 본인만 해제할 수 있으며,
        /// 성공 시 진행도가 0으로 리셋된다(Core <see cref="Valve.Interrupt"/> 그대로).
        /// </summary>
        public void EndHold(ulong playerId) => _valve.Interrupt(playerId);

        /// <summary>
        /// 서버의 권위 타이머를 진전시킨다. 회전 중일 때만 유효하다.
        /// 이번 틱에 개방이 완료(→ Open)되면 true를 반환한다(개방 알림 1회 발행용).
        /// </summary>
        public bool Tick(float deltaSeconds)
        {
            if (_valve.State != ValveState.Rotating)
                return false;

            _valve.Tick(deltaSeconds);
            return _valve.State == ValveState.Open;
        }
    }
}
