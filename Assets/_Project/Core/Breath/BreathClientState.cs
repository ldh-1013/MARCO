using UnityEngine;

namespace Marco.Core.Breath
{
    /// <summary>
    /// 서버 권위 숨 게이지(§5.9-1)의 <b>소유자 사본</b>. 서버가 소유자에게만 보내고(TargetRpc)
    /// HUD·이동 예측이 읽는다 — GAP-76("숨 게이지가 소유자 클라이언트에 내려오지 않는다") 해소.
    ///
    /// <para>
    /// 표시·예측 전용이다. 강제 부상·질식의 권위는 여전히 서버에 있다. 매 프레임 보내지 않는다
    /// (§14.3) — 서버가 변화가 있을 때 초당 최대 몇 회만 보낸다.
    /// </para>
    /// </summary>
    public static class BreathClientState
    {
        public static float Remaining { get; private set; } = BreathConfig.TotalSeconds;

        public static bool Submerged { get; private set; }

        /// <summary>§5.9-1 질식 페널티(이동속도 -20% 3초)가 걸려 있는가 — 소유자 이동 예측이 쓴다.</summary>
        public static bool ChokePenaltyActive { get; private set; }

        /// <summary>§5.9-1 질식 페널티 배율(평소 1.0). <c>LocomotionInput.SpeedMultiplier</c>로 넘긴다.</summary>
        public static float SpeedMultiplier => ChokePenaltyActive ? BreathConfig.ChokeSpeedMultiplier : 1f;

        public static float Normalized => Mathf.Clamp01(Remaining / BreathConfig.TotalSeconds);

        /// <summary>숨이 남아 잠수를 유지할 수 있는가(§5.9-1 강제 부상의 소유자 예측).</summary>
        public static bool CanSubmerge => Remaining > 0f;

        public static void Apply(float remaining, bool submerged, bool chokePenalty)
        {
            Remaining = Mathf.Clamp(remaining, 0f, BreathConfig.TotalSeconds);
            Submerged = submerged;
            ChokePenaltyActive = chokePenalty;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void ResetForNewSession()
        {
            Remaining = BreathConfig.TotalSeconds;
            Submerged = false;
            ChokePenaltyActive = false;
        }
    }
}
