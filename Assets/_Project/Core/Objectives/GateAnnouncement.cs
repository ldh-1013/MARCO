using System.Collections.Generic;
using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §10.5 게이트 개방 알림 — 게이트가 열리는 순간 <b>양쪽 출구에서 고함급(22m) 파문 1회</b>. 새 SoundType을 만들지 않는다
    /// (§5.1 "Shout은 … 게이트 개방 시 양쪽 출구(10.5)에서도 발생 — 재사용"). §6.2-1 종반 30초 출구 파문도 같은 종류다.
    /// 서버(<c>RoundNetworkSync</c>)가 이것을 부르고 결과대로 월드 파문을 낸다.
    /// </summary>
    public static class GateAnnouncement
    {
        /// <summary>출구 파문 종류 — §5.1 고함(발생 22m).</summary>
        public const SoundType PulseType = SoundType.Shout;

        /// <summary>
        /// 이번 틱에 낼 출구 파문 위치를 <paramref name="into"/>에 채운다. <paramref name="gateOpenedThisTick"/>은
        /// <see cref="RoundObjective.Tick"/>의 반환값(래치가 열린 틱에만 true)이라 한 판에 한 번이다.
        /// </summary>
        public static void CollectPulses(bool gateOpenedThisTick, IReadOnlyList<Vector3> exits, List<Vector3> into)
        {
            into.Clear();
            if (!gateOpenedThisTick || exits == null)
                return;

            for (int i = 0; i < exits.Count; i++)
                into.Add(exits[i]);
        }
    }
}
