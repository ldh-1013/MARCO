using UnityEngine;

namespace Marco.Core.Tagging
{
    /// <summary>
    /// §3.1-1 [신설] 태그 성립 시 처리의 <b>밸런스 수치</b>. 표현(플래시·카메라·SFX)의 정본은
    /// <c>docs/연출.md</c> §4이며 여기에는 수치만 둔다.
    ///
    /// <para>
    /// <b>왜 술래 경직이 3초가 아니라 1초인가</b>(§3.1-1): *"잡은 쪽이 손해 보는 연출은 만들지
    /// 않는다. 3초를 묶으면 남은 도망자가 공짜로 도망친다."*
    /// </para>
    /// </summary>
    public static class TagAftermath
    {
        /// <summary>
        /// §3.1-1 술래 경직 1.0초 — <b>이동·태그 모두 불가</b>.
        ///
        /// <para>
        /// <b>★ 이동만 막으면 안 된다.</b> 태그 판정까지 막지 않으면 제자리에서 연속 태그가
        /// 가능해져 도망자 두 명이 붙어 있을 때 한 번에 정리된다.
        /// <see cref="IsSeekerStunned"/>가 두 가지를 같이 답한다.
        /// </para>
        /// </summary>
        public const float SeekerStunSeconds = 1.0f;

        /// <summary>§3.1-1 태그된 도망자 3.0초 암전 + 조작 불가 후 메아리 전환.</summary>
        public const float RunnerBlackoutSeconds = 3.0f;

        /// <summary>
        /// §3.1-1 태그 지점 파문 — <b>고함급(발생 22m / 지속 2.5초)</b>.
        /// <b>새 SoundType을 만들지 않는다</b>(§3.3) — 기존 <c>Shout</c> 등급 그대로다.
        ///
        /// <para>
        /// 22m로 잡은 이유는 §3.1-1에 있다 — *"근처에 있었다면 알고, 멀리 있었다면 모른다"* 는
        /// 경계를 만들기 위함이다. 맵 전체에 공유되면 도망자들이 항상 술래 위치를 알게 되어
        /// 정보 비대칭이 무너진다.
        /// </para>
        /// </summary>
        public static readonly Sound.SoundType PulseType = Sound.SoundType.Shout;

        /// <summary>경직이 남아 있는가. 경직 시작 시각과 현재 시각으로 판정한다.</summary>
        public static bool IsSeekerStunned(float stunStartedAt, float now)
        {
            if (stunStartedAt <= 0f)
                return false;

            return now - stunStartedAt < SeekerStunSeconds;
        }

        /// <summary>경직 잔여(초). HUD·연출이 읽는다.</summary>
        public static float StunRemaining(float stunStartedAt, float now)
        {
            if (stunStartedAt <= 0f)
                return 0f;

            return Mathf.Max(0f, SeekerStunSeconds - (now - stunStartedAt));
        }

        /// <summary>암전이 남아 있는가.</summary>
        public static bool IsRunnerBlackedOut(float taggedAt, float now)
        {
            if (taggedAt <= 0f)
                return false;

            return now - taggedAt < RunnerBlackoutSeconds;
        }

        /// <summary>암전 잔여(초).</summary>
        public static float BlackoutRemaining(float taggedAt, float now)
        {
            if (taggedAt <= 0f)
                return 0f;

            return Mathf.Max(0f, RunnerBlackoutSeconds - (now - taggedAt));
        }
    }
}
