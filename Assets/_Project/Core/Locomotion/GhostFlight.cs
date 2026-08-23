using Marco.Core.Role;
using UnityEngine;

namespace Marco.Core.Locomotion
{
    /// <summary>
    /// §3.2 메아리의 자유 비행 이동 규칙(스프린트 27 후속).
    ///
    /// **§3.2 원문**: <c>이동: 자유 비행형 유령 카메라, 충돌 없음(벽 통과), 이동속도 8.0 m/s</c>
    ///
    /// 순수 계산만 한다 — 카메라 방향과 입력 축을 받아 월드 속도를 돌려줄 뿐,
    /// 충돌을 끄거나 <c>Transform</c>을 움직이는 것은 Presentation의 몫이다
    /// (<see cref="LocomotionSimulator"/>가 속도·발소리만 정하고 <c>CharacterController</c>
    /// 조작은 Presentation이 하는 것과 같은 분담).
    ///
    /// **지상 이동과 다른 점**: 지상은 수평(XZ)만 시뮬레이터가 정하고 중력을 컨트롤러가 얹지만,
    /// 비행은 **바라보는 방향 그대로 3축**으로 움직이고 중력이 없다. 그래서 별도 규칙으로 뒀다.
    /// </summary>
    public static class GhostFlight
    {
        /// <summary>이 역할이 §3.2 자유 비행 대상인가. 메아리만 해당한다.</summary>
        public static bool IsFlying(RoleType role) => role == RoleType.Echo;

        /// <summary>
        /// §3.2 비행 속도(8.0 m/s). <see cref="LocomotionConfig.EchoSpeed"/>를 그대로 쓴다 —
        /// 지상 이동과 같은 상수를 공유해 수치가 두 곳으로 갈라지지 않게 한다.
        /// </summary>
        public static float SpeedMetersPerSecond => LocomotionConfig.EchoSpeed;

        /// <summary>
        /// 비행 속도 벡터를 구한다.
        ///
        /// <paramref name="moveAxis"/>는 WASD(x=좌우, y=전후), <paramref name="verticalAxis"/>는
        /// 상승(+1)/하강(−1)이다. 방향은 **카메라 기준**이라 바라보는 쪽으로 날아간다.
        ///
        /// 입력이 대각으로 겹쳐도 <see cref="SpeedMetersPerSecond"/>를 넘지 않도록 정규화한다
        /// (지상 이동의 대각 정규화와 같은 규칙 — §4.2 "대각 입력이 직선보다 빨라지지 않도록").
        /// </summary>
        /// <param name="cameraForward">카메라 정면(정규화되지 않아도 된다).</param>
        /// <param name="cameraRight">카메라 오른쪽(정규화되지 않아도 된다).</param>
        public static Vector3 Velocity(Vector2 moveAxis, float verticalAxis,
            Vector3 cameraForward, Vector3 cameraRight)
        {
            Vector3 direction =
                cameraForward.normalized * moveAxis.y +
                cameraRight.normalized * moveAxis.x +
                Vector3.up * verticalAxis;

            if (direction.sqrMagnitude <= 0.0001f)
                return Vector3.zero;

            return direction.normalized * SpeedMetersPerSecond;
        }
    }
}
