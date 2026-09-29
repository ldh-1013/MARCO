using Marco.Core.Role;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// 상호작용 한 줄 안내(09-29) — 밸브 · 배수구 공용. 범위 안이면 "[E] 밸브 A 돌리기", 누르는 동안 진행 %, 못 하면 이유
    /// (메아리 불가 · 이번 판 비활성 · 이미 열림). 범위 밖이면 빈 문자열. <b>술래에게는 띄우지 않는다</b>(09-30 결정 — 밸브 · 배수구는
    /// 도망자만 조작하고, 술래 화면에 조작 안내가 뜰 이유가 없다). 거부 사유의 판정은 서버와 같은 규칙
    /// (<see cref="Valve.CheckInteract(RoleType, bool, ValveState)"/>)을 쓴다 — 표시가 서버 판정과 어긋나지 않게.
    /// </summary>
    public static class InteractionPrompt
    {
        /// <summary>배수구 거부 사유. 밸브는 <see cref="ValveInteractionRejection"/>을 그대로 쓴다.</summary>
        public enum DrainBlock
        {
            None,
            Echo,
            RunnersOnly,
            NotActive,
        }

        public static string Valve(ValveId id, bool inRange, ValveInteractionRejection rejection, bool working, float progress01) =>
            Build($"밸브 {id}", "돌리기", "돌리는 중", inRange, ReasonOf(rejection), working, progress01);

        /// <summary>
        /// 배수구 거부 사유 — 서버 자격(<see cref="UnderwaterWorkSession.CanWork(RoleType, bool, bool)"/> · <see cref="DrainHatch.TryWork"/>: 도망자만)과
        /// 같은 순서. 술래(<see cref="DrainBlock.RunnersOnly"/>)에게는 안내를 띄우지 않는다.
        /// </summary>
        public static DrainBlock CheckDrain(RoleType role, bool activeThisPhase)
        {
            if (role == RoleType.Echo)
                return DrainBlock.Echo;
            if (role != RoleType.Runner)
                return DrainBlock.RunnersOnly;
            return activeThisPhase ? DrainBlock.None : DrainBlock.NotActive;
        }

        public static string Drain(string drainName, bool inRange, DrainBlock block, bool working, float progress01) =>
            Build($"배수구 {drainName}", "열기", "여는 중", inRange, ReasonOf(block), working, progress01);

        /// <summary>이 사유면 안내 자체를 띄우지 않는다 — 술래(09-30 결정).</summary>
        private const string NoPrompt = "";

        private static string Build(string target, string action, string doing, bool inRange, string reason, bool working, float progress01)
        {
            if (!inRange || reason == NoPrompt)
                return string.Empty;

            if (reason != null)
                return $"{target} — {reason}";

            if (!working)
                return $"[E] {target} {action}";

            // 내림 — 99.6%를 100%로 올려 보이면 "다 됐다"로 읽힌다(HudFormatter 배수구 줄과 같은 규칙).
            if (progress01 < 0f) progress01 = 0f;
            if (progress01 > 1f) progress01 = 1f;
            int percent = (int)System.Math.Floor(progress01 * 100f);
            return $"{target} {doing} {percent}%";
        }

        private static string ReasonOf(ValveInteractionRejection rejection)
        {
            switch (rejection)
            {
                case ValveInteractionRejection.EchoCannotInteract: return "메아리 불가";
                case ValveInteractionRejection.SeekerCannotInteract: return NoPrompt;
                case ValveInteractionRejection.NotActiveThisRound: return "이번 판 비활성";
                case ValveInteractionRejection.AlreadyOpen: return "이미 열림";
                default: return null;
            }
        }

        private static string ReasonOf(DrainBlock block)
        {
            switch (block)
            {
                case DrainBlock.Echo: return "메아리 불가";
                case DrainBlock.RunnersOnly: return NoPrompt;
                case DrainBlock.NotActive: return "이번 판 비활성";
                default: return null;
            }
        }
    }
}
