using System;
using UnityEngine;

namespace Marco.Presentation.GameFlow
{
    /// <summary>
    /// §6.2 라운드 제한시간 카운트다운. Unity 수명주기에 의존하지 않아
    /// EditMode 테스트로 만료 1회성까지 고정할 수 있다.
    ///
    /// 제한시간은 기획서에 명시된 값을 쓴다 — 임의로 정하지 않았다(§6.2 표).
    /// 만료 신호는 **정확히 한 번만** 발행한다. 밸브·파문 파이프라인에서 쓴
    /// "1회만 발행" 패턴과 동일하다.
    /// </summary>
    public sealed class RoundTimer
    {
        /// <summary>§6.2 2인 구간(v1.x).</summary>
        public const float TwoPlayerSeconds = 360f;

        /// <summary>§6.2 3인 구간(v1.x).</summary>
        public const float ThreePlayerSeconds = 480f;

        /// <summary>§6.2 4인 — MVP 대상. 10분.</summary>
        public const float FourPlayerSeconds = 600f;

        /// <summary>§6.2 5인 구간(v1.x). 4인과 동일한 10분.</summary>
        public const float FivePlayerSeconds = 600f;

        /// <summary>§6.2 6인 구간(v1.x). 12분.</summary>
        public const float SixPlayerSeconds = 720f;

        /// <summary>만료 시 1회만 발행된다.</summary>
        public event Action Expired;

        public float RemainingSeconds { get; private set; }
        public float DurationSeconds { get; private set; }
        public bool IsRunning { get; private set; }
        public bool HasExpired { get; private set; }

        /// <summary>0(시작) → 1(만료). 진행률 표시용.</summary>
        public float Elapsed01 =>
            DurationSeconds <= 0f ? 1f : Mathf.Clamp01(1f - RemainingSeconds / DurationSeconds);

        public void Start(float durationSeconds)
        {
            DurationSeconds = Mathf.Max(0f, durationSeconds);
            RemainingSeconds = DurationSeconds;
            IsRunning = true;
            HasExpired = false;
        }

        /// <summary>정지하면 더 이상 감소하지 않는다(라운드가 먼저 끝난 경우).</summary>
        public void Stop() => IsRunning = false;

        public void Tick(float deltaSeconds)
        {
            if (!IsRunning || HasExpired || deltaSeconds <= 0f)
                return;

            RemainingSeconds -= deltaSeconds;

            if (RemainingSeconds > 0f)
                return;

            // 음수로 흘러가지 않게 고정 — §6.3의 timeRemaining <= 0 판정 입력이 된다.
            RemainingSeconds = 0f;
            HasExpired = true;
            IsRunning = false;
            Expired?.Invoke();
        }
    }
}
