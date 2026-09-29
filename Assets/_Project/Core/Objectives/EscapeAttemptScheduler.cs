namespace Marco.Core.Objectives
{
    /// <summary><see cref="EscapeAttemptScheduler.Tick"/>의 결정.</summary>
    public enum EscapeAttemptDecision
    {
        /// <summary>아무것도 하지 않는다.</summary>
        None,

        /// <summary>서버에 탈출 요청을 보낸다.</summary>
        Request,

        /// <summary>범위에 들어왔지만 게이트가 닫혀 있다(안내용).</summary>
        BlockedByGate
    }

    /// <summary>
    /// 출구 트리거가 언제 탈출 요청을 보낼지 — <c>EscapePointTrigger</c>가 매 프레임 부른다. Unity와 무관 — EditMode 테스트 가능.
    /// <para>
    /// <b>"범위 안 + 게이트 열림 + 아직 탈출 안 함 + 라운드 진행 중"인 동안 <see cref="RetryIntervalSeconds"/> 간격으로</b> 요청한다
    /// (09-29). 서버 확정 · 범위 이탈 · 결과 확정이면 멈춘다. 예전에는 범위에 들어온 순간에만 시도해, 게이트가 열릴 때 이미
    /// 범위 안에 있던 러너(스폰 슬롯 2는 정문에서 1.34m)는 움직이지 않으면 탈출하지 못했다. 서버는 중복 등록을 막는다.
    /// </para>
    /// </summary>
    public sealed class EscapeAttemptScheduler
    {
        /// <summary>요청 재시도 간격(초).</summary>
        public const float RetryIntervalSeconds = 0.5f;

        private bool _wasInside;
        private bool _hasPending;
        private float _nextRequestAt;

        /// <param name="inside">출구 판정 반경 안인가.</param>
        /// <param name="gateOpen">서버가 공개한 게이트 래치.</param>
        /// <param name="alreadyEscaped">서버가 이 플레이어의 탈출을 확정했는가.</param>
        /// <param name="roundInProgress">라운드가 진행 중인가(결과가 나기 전).</param>
        /// <param name="now">현재 시각(초).</param>
        public EscapeAttemptDecision Tick(bool inside, bool gateOpen, bool alreadyEscaped, bool roundInProgress, float now)
        {
            bool entered = inside && !_wasInside;
            _wasInside = inside;

            if (!inside || alreadyEscaped || !roundInProgress)
            {
                _hasPending = false; // 다시 들어오면 바로 요청한다
                return EscapeAttemptDecision.None;
            }

            if (!gateOpen)
                return entered ? EscapeAttemptDecision.BlockedByGate : EscapeAttemptDecision.None;

            if (_hasPending && now < _nextRequestAt)
                return EscapeAttemptDecision.None;

            _hasPending = true;
            _nextRequestAt = now + RetryIntervalSeconds;
            return EscapeAttemptDecision.Request;
        }
    }
}
