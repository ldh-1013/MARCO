using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// §10.2 맵에 배치된 밸브 5개. <b>배치는 5개 고정이고 매 라운드 3개가 활성</b>(수영장 B · E 고정 + 1개, 10-01).
    /// </summary>
    public enum ValveId
    {
        A,
        B,
        C,
        D,
        E,
    }

    /// <summary>
    /// §6.1 [v0.4] 밸브별 <b>총 점유 시간 배분</b>. 진입 · 회전 · 부상 세 구간으로 나뉘며,
    /// <b>총 점유는 다섯 밸브 모두 8.0초로 같다.</b>
    ///
    /// <para>
    /// <b>8.0을 다섯 곳에 적지 않는다.</b> 배분만 적고 총 점유는 합으로 유도한다 —
    /// 그래야 배분을 고칠 때 합이 자동으로 따라오고, "합이 8.0"을 테스트 한 줄로 고정할 수 있다.
    /// 값을 따로 적으면 배분과 합이 어긋난 채 남는다.
    /// </para>
    ///
    /// <para>
    /// <b>왜 균등해야 하는가</b>(§6.1 원문): 도망자가 4개 중 3개를 고를 수 있게 되는 순간
    /// 비용 격차가 크면 <b>항상 싼 3개만 고르고 가장 비싼 하나는 영원히 선택되지 않는다.</b>
    /// 그러면 술래는 "버려질 밸브"를 알기 때문에 나머지만 순찰하면 되고 —
    /// <b>봉쇄 문제가 형태만 바꿔 재발한다.</b> 비용은 균등하게, 리스크의 성격만 다르게.
    /// </para>
    /// </summary>
    public static class ValveOccupancy
    {
        /// <summary>진입(잠수 하강) 시간. 수중 밸브만 0보다 크다 — §6.1-1.</summary>
        public static float EntrySeconds(ValveId id)
        {
            switch (id)
            {
                case ValveId.B: return 1.5f;
                case ValveId.E: return 0.5f;
                default: return 0f;
            }
        }

        /// <summary>
        /// 회전 시간. <b>진행도의 분모</b>이며 초당 증가율은 <c>1 / 회전시간</c>이다
        /// (§6.1 "1인 작업 +1/8 per sec → 8.0초 완료"는 회전 8.0초인 밸브의 경우다).
        /// </summary>
        public static float RotateSeconds(ValveId id)
        {
            switch (id)
            {
                case ValveId.B: return 5f;
                case ValveId.E: return 7f;
                default: return 8f; // A · C · D
            }
        }

        /// <summary>부상 시간. 수중 밸브만 0보다 크다 — §6.1-1.</summary>
        public static float SurfaceSeconds(ValveId id)
        {
            switch (id)
            {
                case ValveId.B: return 1.5f;
                case ValveId.E: return 0.5f;
                default: return 0f;
            }
        }

        /// <summary>
        /// §6.1 총 점유 시간 = 진입 + 회전 + 부상. <b>유도값이다</b> —
        /// A/C/D는 0+8+0, B는 1.5+5+1.5, E는 0.5+7+0.5로 전부 8.0이 된다.
        /// </summary>
        public static float TotalSeconds(ValveId id)
        {
            return EntrySeconds(id) + RotateSeconds(id) + SurfaceSeconds(id);
        }

        /// <summary>§6.1-1 수중 밸브인가(진입·부상이 있는가). B · E.</summary>
        public static bool IsUnderwater(ValveId id)
        {
            return EntrySeconds(id) > 0f || SurfaceSeconds(id) > 0f;
        }

        /// <summary>
        /// §6.1 밸브 A "기계 앰비언스로 밸브 소음 ×0.5(발생 12m→6m)".
        /// 다른 밸브는 1.0이다.
        /// </summary>
        public static float SoundRadiusMultiplier(ValveId id)
        {
            return id == ValveId.A ? 0.5f : 1f;
        }

        /// <summary>
        /// §5.1 [v0.4] 밸브 회전 파문 지속 — "각 밸브 회전 시간(6.1)". 회전을 시작할 때 1회 발생해
        /// 회전 내내 퍼진다(중간에 멈춰도 이미 난 소리는 취소되지 않는다 — §6.1).
        /// </summary>
        public static float PulseDurationSeconds(ValveId id) => RotateSeconds(id);

        /// <summary>이 밸브의 실제 회전 소음 발생 반경(m). §5.1 12m × 위 배율.</summary>
        public static float SoundRadiusMeters(ValveId id)
        {
            return Valve.SoundRadiusMeters * SoundRadiusMultiplier(id);
        }

        /// <summary>
        /// §6.1 동시 작업 배율 — <c>1인 1.0 / 2인 ×1.6 / 3인 이상 ×1.9(상한)</c>.
        ///
        /// <para>
        /// 상한이 있는 이유는 §6.2 "요구 개방 3 고정"과 같다 — 병렬 작업 이득을 묶지 않으면
        /// 인원이 늘수록 클리어가 과도하게 쉬워진다.
        /// </para>
        /// </summary>
        public static float ConcurrencyMultiplier(int interactorCount)
        {
            if (interactorCount <= 0)
                return 0f;
            if (interactorCount == 1)
                return 1f;
            if (interactorCount == 2)
                return 1.6f;

            return 1.9f; // 3인 이상 상한
        }

        /// <summary>
        /// 이 인원으로 회전을 마치는 데 걸리는 시간(초). 검증·HUD 표시용 유도값이다.
        /// </summary>
        public static float SecondsToComplete(ValveId id, int interactorCount)
        {
            float multiplier = ConcurrencyMultiplier(interactorCount);
            return multiplier <= 0f ? float.PositiveInfinity : RotateSeconds(id) / multiplier;
        }

        /// <summary>표시용 이름. 로그·HUD가 쓴다.</summary>
        public static string ZoneOf(ValveId id)
        {
            switch (id)
            {
                case ValveId.A: return "기계실";
                case ValveId.B: return "메인 풀(수중)";
                case ValveId.C: return "물탱크실(2층)";
                case ValveId.D: return "약품창고";
                default: return "유아풀(얕은 수중)";
            }
        }

        /// <summary>모든 밸브 id. 배치는 5개 고정이다(§6.1-0).</summary>
        public static readonly ValveId[] All =
        {
            ValveId.A, ValveId.B, ValveId.C, ValveId.D, ValveId.E,
        };

        /// <summary>
        /// 전 밸브의 총 점유가 같은지. §6.1의 균등 제약을 코드에서 물을 수 있게 노출한다
        /// (테스트와 진단이 같은 함수를 쓴다).
        /// </summary>
        public static bool AllTotalsEqual(out float total)
        {
            total = TotalSeconds(All[0]);
            for (int i = 1; i < All.Length; i++)
            {
                if (!Mathf.Approximately(TotalSeconds(All[i]), total))
                    return false;
            }

            return true;
        }
    }
}
