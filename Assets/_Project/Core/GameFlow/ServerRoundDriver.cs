using System;
using System.Collections.Generic;
using Marco.Core.Net;
using Marco.Core.Objectives;
using Marco.Core.Role;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// 서버 권위 라운드 판정기(스프린트 12). 서버에서만 존재하며, 타이머·탈출 집계·
    /// §6.3 최종 판정을 <b>서버 한 곳에서만</b> 수행해 확정한다. FishNet도 UnityEngine도
    /// 모르므로 EditMode 테스트로 서버 권위 성질을 직접 검증할 수 있다
    /// (<c>ServerValveDriver</c>·<c>ServerTagDriver</c>와 같은 구조).
    ///
    /// **기존 판정 재사용(변경 금지)**: 승패식은 Core <see cref="WinConditionEvaluator"/>를
    /// 그대로 호출한다. 탈출 규칙(게이트 개방·러너만·중복 방지)은 <c>RoundOutcomeTracker</c>가
    /// 이미 강제하던 것과 같은 규칙을 서버 측에서 재검증하는 것이다(§5.3) — 새 규칙을 만들지 않는다.
    ///
    /// **판정 한 번이면 고정(latch)**: 라운드 종료는 되돌릴 수 없으므로, 한 번 결정된 뒤의
    /// 재평가·탈출은 무시한다. 이 성질을 <c>ServerRoundDriverTests</c>가 고정한다.
    /// </summary>
    public sealed class ServerRoundDriver
    {
        private readonly HashSet<ulong> _escaped = new HashSet<ulong>();

        public float RemainingSeconds { get; private set; }
        public RoundResult Result { get; private set; } = RoundResult.InProgress;
        public bool IsDecided => Result != RoundResult.InProgress;
        public int EscapedCount => _escaped.Count;

        public ServerRoundDriver(float durationSeconds)
        {
            RemainingSeconds = Math.Max(0f, durationSeconds);
        }

        /// <summary>
        /// 탈출 요청을 서버가 재검증해 집계한다. 실제로 새로 집계됐을 때만 true.
        ///
        /// §6.1 "밸브 전부 Open → 게이트 Open → 탈출 가능"과 GAP-11(러너만 집계)을
        /// <b>서버 측 값으로</b> 재확인한다. 게이트 개방 여부는 서버 권위 밸브 상태
        /// (<see cref="IEscapeGateState.IsGateOpen"/>)에서 온다.
        /// </summary>
        public bool TryRegisterEscape(ulong playerId, RoleType role, bool gateOpen)
        {
            if (IsDecided)
                return false;

            if (!gateOpen) // §6.1 게이트 미개방 시 탈출 불가(서버 재검증)
                return false;

            if (role != RoleType.Runner) // GAP-11: 러너만 탈출 집계
                return false;

            return _escaped.Add(playerId); // 같은 러너의 중복 집계 방지
        }

        /// <summary>서버 권위 타이머를 진전시킨다. 판정 확정 후에는 멈춘다. 음수로 흘러가지 않는다.</summary>
        public void Tick(float deltaSeconds)
        {
            if (IsDecided || deltaSeconds <= 0f)
                return;

            RemainingSeconds = Math.Max(0f, RemainingSeconds - deltaSeconds);
        }

        /// <summary>
        /// §6.3 판정을 수행하고, 승패가 갈렸으면 결과를 고정한다. 이번에 새로 결정됐을 때만
        /// true — 서버가 전파(SyncVar)와 로깅을 1회만 하도록. 판정식은 Core에 위임한다.
        /// </summary>
        public bool Evaluate(int valvesOpened, int totalValves, bool allRunnersTagged)
        {
            if (IsDecided)
                return false;

            RoundResult result = WinConditionEvaluator.Evaluate(
                valvesOpened, totalValves, EscapedCount, allRunnersTagged, RemainingSeconds);

            if (result == RoundResult.InProgress)
                return false;

            Result = result;
            return true;
        }

        /// <summary>
        /// §6.3 <c>allRunnersTagged</c> 입력을 태그 대상 집합에서 <b>서버 측으로</b> 계산한다.
        ///
        /// GAP-19 결정: 고정 분모(전체 러너 수)를 쓰지 않고, "태그되지 않은 러너가 하나도
        /// 남지 않았는가"로 판정한다. 태그된 대상은 §3.1대로 메아리(Echo)가 되어 더는
        /// Role==Runner가 아니므로, "현재 태그 안 된 러너 == 0 && 태그된 대상 ≥ 1"이면
        /// 전원 태그다. 이렇게 하면 역할 배정 네트워크화(미구현) 없이도 분모 문제가 사라지고,
        /// 서버가 <see cref="ITagTarget"/> 집합(<c>TagTargetRegistry</c>)을 그대로 읽어 계산할 수 있다.
        /// 러너가 애초에 없으면(공허한 참 방지) false다.
        /// </summary>
        public static bool AllRunnersTagged(IReadOnlyList<ITagTarget> targets)
        {
            if (targets == null)
                return false;

            int taggedCount = 0;
            int untaggedRunners = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                ITagTarget t = targets[i];
                if (t == null)
                    continue;

                if (t.IsTagged)
                    taggedCount++; // 태그돼 메아리가 된 대상(= 원래 러너)
                else if (t.Role == RoleType.Runner)
                    untaggedRunners++; // 아직 안 태그된 러너
            }

            return taggedCount > 0 && untaggedRunners == 0;
        }
    }
}
