using Marco.Core.Locomotion;
using Marco.Core.Sound;

namespace Marco.Core.Role
{
    /// <summary>
    /// 탈출자는 월드에서 제외된다(09-29) — 한 곳의 규칙. 서버(태그 · 밸브 · 배수구 · 파문 · 캠핑 판정)와 모든 피어(몸 · 충돌 ·
    /// 입력 · 음성)가 이것을 부른다. 입력은 <see cref="PawnEffect.Escaped"/> — 서버가 SyncVar 하나로 공개한 값이다.
    /// 관전 카메라는 두지 않는다(시점 회전만 남는다).
    /// </summary>
    public static class WorldPresence
    {
        /// <summary>몸을 그리는가.</summary>
        public static bool BodyVisible(bool escaped) => !escaped;

        /// <summary>캡슐 충돌이 있는가(메아리 비행과 같은 스위치 — 둘 중 하나면 끈다).</summary>
        public static bool HasCollision(RoleType role, bool escaped) => !escaped && !GhostFlight.IsFlying(role);

        /// <summary>이동 입력을 받는가(시점 회전은 별개 — 관전 중에도 둘러본다).</summary>
        public static bool AcceptsMovement(bool escaped) => !escaped;

        /// <summary>월드 상호작용(밸브 · 배수구 · 줍기 · 능력 RPC)을 할 수 있는가.</summary>
        public static bool CanAct(bool escaped) => !escaped;

        /// <summary>자기 파문(말 · 발소리)을 낼 수 있는가. 메아리의 소리는 노크뿐이다(§3.2).</summary>
        public static bool CanEmitPulses(RoleType role, bool escaped) => !escaped && role != RoleType.Echo;

        /// <summary>§3.6 캠핑 방지(장시간 정지 호흡음)가 적용되는가.</summary>
        public static bool CampingApplies(RoleType role, bool escaped) => !escaped && CampingConfig.AppliesTo(role);
    }
}
