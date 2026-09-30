namespace Marco.Core.Net
{
    /// <summary>접속 시도 한 번의 상태(09-30).</summary>
    public enum JoinAttemptState
    {
        /// <summary>시도 없음(메뉴).</summary>
        Idle,

        /// <summary>참가 · 호스트 시작을 요청했고 연결을 기다린다 — "접속 중… (N초)" · "Esc — 취소".</summary>
        Connecting,

        /// <summary>전송 계층이 연결됐다 — 로비 화면이 이어받는다.</summary>
        Connected,

        /// <summary>실패(연결 안 됨 · 시간 초과 · 연결 뒤 끊김) — 메인 메뉴로 돌아가 빨간 안내.</summary>
        Failed,

        /// <summary>사용자가 취소했다(Esc) — 메인 메뉴로 돌아간다.</summary>
        Cancelled,
    }

    /// <summary>
    /// 접속 시도 한 번(09-30, 실기 결함 — 실패하면 로비 UI가 꺼진 채 빈 월드만 남았다). 시도 중 → 연결됨 / 실패 / 취소, 시간 초과, 재시도를
    /// 순수 로직으로 둔다. <c>ConnectionService</c>(Net)가 전송 계층 사건(Started · Stopped)을 넣고 매 프레임 <see cref="Tick"/>을 부르며,
    /// 접속 중 화면(<c>JoinProgressOverlay</c>, Presentation)이 읽는다.
    ///
    /// <para>
    /// <b>이미 끝난 시도(실패 · 취소)에 뒤늦게 오는 Stopped는 무시한다</b> — 시간 초과 · 취소로 전송 계층을 내리면 Stopped가 따라오는데,
    /// 그것을 다시 "연결 실패"로 세면 취소가 실패로 바뀌거나 안내가 덮인다. Tugboat가 스스로 포기하는 경로(포트 무응답 ≈ 5.9초,
    /// 이름 풀이 실패 0.1초 — 09-30 로그)는 시간 초과보다 먼저 와서 그대로 실패가 된다.
    /// </para>
    /// </summary>
    public sealed class JoinAttempt
    {
        /// <summary>연결이 이 시간 안에 되지 않으면 실패로 본다(Tugboat 자체 포기 ≈ 5초보다 길게 — 안전망).</summary>
        public const float TimeoutSeconds = 10f;

        /// <summary>접속 중 화면의 취소 안내.</summary>
        public const string CancelHint = "Esc — 취소";

        private float _startedAt;

        public JoinAttemptState State { get; private set; } = JoinAttemptState.Idle;

        public bool IsHost { get; private set; }

        public JoinAddress Target { get; private set; }

        public ushort HostPort { get; private set; }

        /// <summary>실패 안내(원인 후보 포함). 실패가 아니면 null.</summary>
        public string FailureMessage { get; private set; }

        /// <summary>실패 원인이 시간 초과였는가.</summary>
        public bool TimedOut { get; private set; }

        /// <summary>메인 메뉴로 돌아가야 하는가(실패 · 취소).</summary>
        public bool ShouldReturnToMenu => State == JoinAttemptState.Failed || State == JoinAttemptState.Cancelled;

        /// <summary>메뉴로 돌아간 뒤 입력 칸에 채울 직전 참가 주소(호스트 시도 · 시도 없음이면 null).</summary>
        public string RetryText => !IsHost && State != JoinAttemptState.Idle ? Target.ToString() : null;

        /// <summary>참가 시도를 시작한다. 이미 시도 중이거나 연결돼 있으면 false(무시).</summary>
        public bool BeginJoin(JoinAddress target, float now)
        {
            if (!Reset(now))
                return false;

            IsHost = false;
            Target = target;
            return true;
        }

        /// <summary>호스트 시작을 시도한다(방 열기 → 로컬 클라이언트 연결). 이미 시도 중이거나 연결돼 있으면 false.</summary>
        public bool BeginHost(ushort port, float now)
        {
            if (!Reset(now))
                return false;

            IsHost = true;
            HostPort = port;
            Target = new JoinAddress("localhost", port);
            return true;
        }

        /// <summary>전송 계층 Started — 시도 중이면 연결됨.</summary>
        public void OnTransportStarted()
        {
            if (State == JoinAttemptState.Connecting)
                State = JoinAttemptState.Connected;
        }

        /// <summary>
        /// 전송 계층 Stopped — 시도 중이면 실패(원인 후보), 연결돼 있었으면 끊김. 이미 끝난 시도(실패 · 취소) · 시도 없음이면 무시.
        /// </summary>
        public void OnTransportStopped()
        {
            if (State == JoinAttemptState.Connecting)
                Fail(IsHost ? ConnectionMessages.HostFailed(HostPort) : ConnectionMessages.JoinFailed(Target));
            else if (State == JoinAttemptState.Connected)
                Fail(IsHost ? ConnectionMessages.HostStopped() : ConnectionMessages.Disconnected(Target));
        }

        /// <summary>매 프레임. 시도 중 <see cref="TimeoutSeconds"/>가 지나면 실패로 확정하고 true(호출자가 전송 계층을 내린다). 한 번만.</summary>
        public bool Tick(float now)
        {
            if (State != JoinAttemptState.Connecting || now - _startedAt < TimeoutSeconds)
                return false;

            Fail(IsHost ? ConnectionMessages.HostFailed(HostPort) : ConnectionMessages.JoinTimedOut(Target, TimeoutSeconds));
            TimedOut = true;
            return true;
        }

        /// <summary>Esc. 시도 중이면 취소하고 true. 연결된 뒤 · 끝난 뒤에는 false.</summary>
        public bool Cancel()
        {
            if (State != JoinAttemptState.Connecting)
                return false;

            State = JoinAttemptState.Cancelled;
            return true;
        }

        /// <summary>경과 초(시도 중일 때만 — 아니면 0).</summary>
        public float Elapsed(float now) =>
            State == JoinAttemptState.Connecting ? System.Math.Max(0f, now - _startedAt) : 0f;

        /// <summary>"127.0.0.1:7999에 접속 중… (3초)" · 호스트면 "방을 여는 중… (0초)". 시도 중이 아니면 빈 문자열.</summary>
        public string ProgressLine(float now)
        {
            if (State != JoinAttemptState.Connecting)
                return string.Empty;

            int seconds = (int)Elapsed(now);
            return IsHost ? $"방을 여는 중… ({seconds}초)" : $"{Target}에 접속 중… ({seconds}초)";
        }

        /// <summary>메뉴에 보여 줄 안내 — 실패면 빨간 원인 안내, 취소면 회색 안내, 그 외 null.</summary>
        public void GetMenuNotice(out string text, out bool isError)
        {
            switch (State)
            {
                case JoinAttemptState.Failed:
                    text = FailureMessage;
                    isError = true;
                    return;
                case JoinAttemptState.Cancelled:
                    text = ConnectionMessages.Cancelled();
                    isError = false;
                    return;
                default:
                    text = null;
                    isError = false;
                    return;
            }
        }

        /// <summary>새 시도 준비 — 시도 중 · 연결됨이면 거절. 실패 · 취소 · 없음 뒤에는 처음부터(재시도).</summary>
        private bool Reset(float now)
        {
            if (State == JoinAttemptState.Connecting || State == JoinAttemptState.Connected)
                return false;

            State = JoinAttemptState.Connecting;
            _startedAt = now;
            FailureMessage = null;
            TimedOut = false;
            return true;
        }

        private void Fail(string message)
        {
            State = JoinAttemptState.Failed;
            FailureMessage = message;
        }
    }
}
