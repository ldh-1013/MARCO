using System.Collections.Generic;
using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Core.Tagging;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// 라운드 결과를 좌우하는 상태(누가 탈출했는가)를 모으고, §6.3 판정을 Core
    /// <see cref="WinConditionEvaluator"/>에 위임해 **한 번 결정되면 고정(latch)** 한다.
    ///
    /// 판정식 자체는 Core 소유다 — 여기서는 입력을 모으고 결과를 붙잡아 둘 뿐이다.
    /// 라운드 종료는 되돌릴 수 없으므로, 결정된 뒤의 재평가는 무시한다.
    ///
    /// GAP-11 결정: 탈출로 집계되는 것은 **러너뿐이다.** §6.3 판정식의 변수명이
    /// `runnersEscaped`이고, 메아리는 §3.2상 유령(물리 상호작용 불가), 술래는 탈출할
    /// 이유가 없다. 기획서가 술래·메아리의 탈출 지점 도달을 명시하지 않았으므로
    /// 가장 보수적인 해석(러너만 집계)을 택했다.
    /// </summary>
    public sealed class RoundOutcomeTracker
    {
        private readonly HashSet<ulong> _escapedPlayers = new HashSet<ulong>();
        private readonly HashSet<ulong> _taggedPlayers = new HashSet<ulong>();

        public int EscapedCount => _escapedPlayers.Count;
        public int TaggedCount => _taggedPlayers.Count;
        public RoundResult Result { get; private set; } = RoundResult.InProgress;
        public bool IsDecided => Result != RoundResult.InProgress;

        public bool HasEscaped(ulong playerId) => _escapedPlayers.Contains(playerId);
        public bool IsTagged(ulong playerId) => _taggedPlayers.Contains(playerId);

        /// <summary>
        /// 탈출 등록을 시도한다. 실제로 새로 집계됐을 때만 true.
        ///
        /// §6.1 "밸브 3개 모두 Open → 배수로 게이트 Open → 탈출 가능" —
        /// 게이트가 닫혀 있으면 탈출 자체가 불가능하다(기획서 명시 규칙).
        /// </summary>
        public bool TryRegisterEscape(ulong playerId, RoleType role, bool gateOpen)
        {
            if (IsDecided)
                return false;

            if (!gateOpen)
                return false;

            if (role != RoleType.Runner) // GAP-11
                return false;

            return _escapedPlayers.Add(playerId); // 같은 러너의 중복 집계 방지
        }

        /// <summary>
        /// §3.1 태그 등록. 실제로 새로 태그됐을 때만 true.
        ///
        /// 역할 조건(술래만 태그 가능·도망자만 대상)은 <see cref="TagRules.CanTag"/>가,
        /// 거리 조건(1.2m)은 호출자가 본다.
        ///
        /// **탈출한 러너는 태그할 수 없다** — 이미 맵을 벗어났기 때문이다.
        /// **재태그가 불가능한 이유**: §3.1상 태그당한 즉시 메아리가 되고 메아리는
        /// 태그 불가라, 대상 역할이 Runner가 아니게 되어 구조적으로 걸러진다.
        /// 여기서는 그 위에 ID 중복 방지를 한 겹 더 둔다.
        /// </summary>
        public bool TryRegisterTag(RoleType taggerRole, ulong targetId, RoleType targetRole)
        {
            if (IsDecided)
                return false;

            if (!TagRules.CanTag(taggerRole, targetRole))
                return false;

            if (_escapedPlayers.Contains(targetId))
                return false;

            return _taggedPlayers.Add(targetId);
        }

        /// <summary>
        /// §6.3 판정을 수행하고, 승패가 갈렸으면 결과를 고정한다.
        /// 이번에 새로 결정됐을 때만 true — 호출자가 종료 처리를 1회만 하도록.
        ///
        /// **GAP-13 소멸(3단계)**: 예전에는 <c>AreAllRunnersTagged(int totalRunners)</c>로
        /// "전원 태그"를 판정했고, 분모(전체 러너 수)를 어디서 얻느냐가 GAP-13/GAP-18이었다.
        /// §6.3이 종료 조건을 <b>"태그 2명 도달"</b> 로 확정해 분모 자체가 사라졌으므로,
        /// 이제 <see cref="TaggedCount"/>를 그대로 넘긴다.
        /// </summary>
        public bool Evaluate(int valvesOpened, int totalValves, int taggedRunners, float timeRemainingSeconds)
        {
            if (IsDecided)
                return false;

            RoundResult result = WinConditionEvaluator.Evaluate(
                valvesOpened, totalValves, EscapedCount, taggedRunners, timeRemainingSeconds);

            if (result == RoundResult.InProgress)
                return false;

            Result = result;
            return true;
        }
    }
}
