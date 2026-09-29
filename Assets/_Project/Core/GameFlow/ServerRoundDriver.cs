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

        /// <summary>
        /// §6.5 최후 생존자 페이즈에 들어갔는가. <b>1회성 latch</b>다 —
        /// 태그로 1명이 된 뒤 그 1명이 탈출하면 0명이 되지만, 페이즈에 들어갔다는
        /// 사실은 되돌아가지 않는다.
        /// </summary>
        public bool LastSurvivorPhase { get; private set; }

        /// <summary>
        /// §6.3 최후 생존자가 탈출했는가. <b>★ 사건 플래그이며 절대 집계에서 역산하지 않는다.</b>
        ///
        /// <para>
        /// §6.3 전수검증이 52케이스 중 <b>4건</b>이 최종 집계만으로 결정되지 않음을 보였다
        /// (도망자 3 E1T2N0 / 4 E1T3N0 / 5 E1T4N0 / 5 E2T3N0). 같은 숫자에서 마지막
        /// 이탈이 탈출인지 태그인지에 따라 승패가 갈리므로, 최종 숫자만 보고 역산하면
        /// 그 4건에서 반드시 틀린다.
        /// </para>
        /// </summary>
        public bool LastSurvivorEscaped { get; private set; }

        /// <summary>
        /// §6.5-1 페이즈 잔여(초). 진입 시 <see cref="DrainConfig.PhaseSeconds"/>(90)로 시작한다.
        /// 페이즈 밖에서는 0이며 의미가 없다.
        /// </summary>
        public float PhaseRemainingSeconds { get; private set; }

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
        /// <summary>이 러너의 탈출이 이미 확정됐는가 — 출구 트리거의 재시도(0.5초 간격)를 서버가 조용히 무시할 때 쓴다.</summary>
        public bool HasEscaped(ulong playerId) => _escaped.Contains(playerId);

        public bool TryRegisterEscape(ulong playerId, RoleType role, bool gateOpen)
        {
            if (IsDecided)
                return false;

            // §6.3 "탈출 = **출구 게이트 개방 후** 출구 접촉" — 게이트는 판정식의 항이 아니라
            //      탈출의 **전제 조건**이고, 그 강제가 여기다. 구 totalValves > 0 방어의
            //      의도("목표가 구성되지 않은 씬을 승리로 읽지 않는다")도 여기서 산다 —
            //      밸브가 0개면 게이트가 열리지 않아 escaped가 애초에 증가하지 않는다.
            //
            // **★ 단 하나의 예외가 배수구다.** §6.5-1 "마지막 생존자의 탈출 = 팀 승리"는
            //      게이트 개방 여부와 무관하므로 TryRegisterDrainEscape가 따로 받는다.
            if (!gateOpen)
                return false;

            if (role != RoleType.Runner) // GAP-11: 러너만 탈출 집계
                return false;

            if (!_escaped.Add(playerId)) // 같은 러너의 중복 집계 방지
                return false;

            // §6.3 페이즈 중에 일어난 탈출이면 그 사건을 여기서 세운다.
            if (LastSurvivorPhase)
                LastSurvivorEscaped = true;

            return true;
        }

        /// <summary>
        /// §6.5-2 배수구를 통한 최후 생존자 탈출. <b>게이트 개방을 요구하지 않는다</b> —
        /// §6.5-2가 *"게이트 개방 상태: 활성화하지 않는다 — 배수구 자체가 탈출구다"* 라고
        /// 정했고, §6.3이 *"마지막 1인의 탈출은 게이트 개방 여부와 무관하게 팀 승리"* 라고
        /// 못박았다.
        ///
        /// <para>
        /// 페이즈 중이 아니면 거부한다 — 배수구는 §6.5 페이즈 전용 오브젝트다.
        /// </para>
        /// </summary>
        public bool TryRegisterDrainEscape(ulong playerId, RoleType role)
        {
            if (IsDecided || !LastSurvivorPhase)
                return false;

            if (role != RoleType.Runner)
                return false;

            if (!_escaped.Add(playerId))
                return false;

            LastSurvivorEscaped = true;
            return true;
        }

        /// <summary>
        /// §6.5-1 최후 생존자 페이즈로 진입한다. <b>진입 판정은 호출부가 <see cref="RunnerCensus"/>로
        /// 한다</b> — 살아있는 도망자 셈의 소유자가 한 곳이어야 하기 때문이다.
        /// 이미 들어갔으면 false(1회성).
        /// </summary>
        public bool TryEnterLastSurvivorPhase()
        {
            if (IsDecided || LastSurvivorPhase)
                return false;

            LastSurvivorPhase = true;
            PhaseRemainingSeconds = DrainConfig.PhaseSeconds;
            return true;
        }

        /// <summary>
        /// §6.5-1 페이즈 제한과 라운드 잔여 중 <b>더 짧은 쪽</b>. §6.5-1이 "라운드 잔여가
        /// 더 짧으면 그쪽 우선"이라고 정했다 — <b>페이즈 타이머가 라운드 타이머를 연장하지 않는다.</b>
        /// 페이즈 중이 아니면 라운드 잔여 그대로다.
        /// </summary>
        public float EffectiveRemainingSeconds(float phaseRemainingSeconds)
        {
            if (!LastSurvivorPhase)
                return RemainingSeconds;

            return Math.Min(RemainingSeconds, Math.Max(0f, phaseRemainingSeconds));
        }

        /// <summary>서버 권위 타이머를 진전시킨다. 판정 확정 후에는 멈춘다. 음수로 흘러가지 않는다.</summary>
        public void Tick(float deltaSeconds)
        {
            if (IsDecided || deltaSeconds <= 0f)
                return;

            RemainingSeconds = Math.Max(0f, RemainingSeconds - deltaSeconds);

            if (LastSurvivorPhase)
                PhaseRemainingSeconds = Math.Max(0f, PhaseRemainingSeconds - deltaSeconds);
        }

        /// <summary>한 번의 서버 판정 단계 결과 — <see cref="Step"/>.</summary>
        public readonly struct RoundStep
        {
            /// <summary>이번 단계에서 승패가 새로 확정됐는가.</summary>
            public readonly bool Decided;

            /// <summary>이번 단계에서 최후 생존자 페이즈에 새로 들어갔는가(1회성).</summary>
            public readonly bool EnteredLastSurvivorPhase;

            public RoundStep(bool decided, bool enteredLastSurvivorPhase)
            {
                Decided = decided;
                EnteredLastSurvivorPhase = enteredLastSurvivorPhase;
            }
        }

        /// <summary>
        /// 서버가 탈출 · 태그 · 타이머 이벤트마다 부르는 한 단계 — 승패 판정과 페이즈 진입의 <b>순서</b>를 한 곳이 소유한다
        /// (<c>RoundNetworkSync.EvaluateAndPush</c>가 이것을 호출한다).
        ///
        /// <para>
        /// <b>판정이 먼저, 결정되지 않았을 때만 페이즈에 진입한다</b>(09-30). 진입을 먼저 하면 탈출로 승리가 확정되는 같은 처리에서
        /// "최후 생존자 페이즈 진입"이 찍히고 결과 화면에 "최후 생존자 1:30"이 남는다. §6.3 "페이즈 진입과 탈출이 동일 프레임 —
        /// 탈출 먼저 확정. 도망자가 0명이 되면 페이즈 미진입". 진입 직후의 판정은 진입 전과 같다(생존 1명 · 페이즈 타이머 90초 ≥ 라운드 잔여
        /// 판정 없음) — 다시 판정하지 않는다.
        /// </para>
        /// </summary>
        public RoundStep Step(in RunnerCensus census)
        {
            if (Evaluate(census))
                return new RoundStep(decided: true, enteredLastSurvivorPhase: false);

            bool entered = census.ShouldEnterLastSurvivorPhase && TryEnterLastSurvivorPhase();
            return new RoundStep(decided: false, entered);
        }

        /// <summary>
        /// §6.3 판정을 수행하고, 승패가 갈렸으면 결과를 고정한다. 이번에 새로 결정됐을 때만
        /// true — 서버가 전파(SyncVar)와 로깅을 1회만 하도록. 판정식은 Core에 위임한다.
        /// </summary>
        public bool Evaluate(in RunnerCensus census)
        {
            if (IsDecided)
                return false;

            // §6.3 "최후 생존자 페이즈 90초 경과 → 즉시 종료, 판정". 페이즈 중에는 라운드 잔여와
            // 페이즈 잔여 중 **짧은 쪽**이 시간 조건이다 — 페이즈가 라운드를 연장하지 않는다(§6.5-1).
            RoundResult result = WinConditionEvaluator.Evaluate(
                census.Total, census.Escaped, census.Alive, LastSurvivorEscaped,
                EffectiveRemainingSeconds(PhaseRemainingSeconds));

            if (result == RoundResult.InProgress)
                return false;

            Result = result;
            return true;
        }

        /// <summary>
        /// 이번 라운드의 도망자 인구를 만든다. <b>탈출 수는 이 구동기가 소유한 집계</b>를 쓰고,
        /// 총수·태그 수만 외부에서 받는다 — 같은 숫자를 두 곳에서 세지 않기 위함이다.
        /// </summary>
        public RunnerCensus Census(int totalRunners, int taggedRunners) =>
            new RunnerCensus(totalRunners, taggedRunners, EscapedCount);

        /// <summary>
        /// §6.3 <c>taggedRunners</c> 입력을 태그 대상 집합에서 <b>서버 측으로</b> 계산한다.
        ///
        /// **GAP-19 재검토(3단계)**: 이전에는 <c>AllRunnersTagged</c>(전원 태그) 불리언을
        /// 돌려줬고, "분모(전체 러너 수)를 어떻게 아는가"가 GAP-19의 본체였다.
        /// §6.3이 종료 조건을 "태그 2명 도달"로 확정하면서 그 분모가 한 번 필요 없어졌고,
        /// <b>v0.4에서 다시 필요해졌다</b> — "살아있는 도망자 = 전체 − 태그 − 탈출"을 세려면
        /// 전체 수를 알아야 한다. 그 분모는 이제 <see cref="RunnerCensus.Total"/>이 들고
        /// 있으며 호출부가 넘긴다. 여기서 세는 것은 <b>태그된 절대 인원</b>뿐이다.
        ///
        /// 태그된 대상은 §3.1대로 메아리(Echo)가 되어 더는 Role==Runner가 아니므로,
        /// 역할이 아니라 <see cref="ITagTarget.IsTagged"/>만 센다 — 태그 대상은 §3.1상
        /// 도망자뿐이라 이 집합의 태그 수가 곧 "태그당한 도망자 수"다.
        /// </summary>
        public static int TaggedCount(IReadOnlyList<ITagTarget> targets)
        {
            if (targets == null)
                return 0;

            int tagged = 0;

            for (int i = 0; i < targets.Count; i++)
            {
                ITagTarget t = targets[i];
                if (t != null && t.IsTagged)
                    tagged++;
            }

            return tagged;
        }
    }
}
