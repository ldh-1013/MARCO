namespace Marco.Core.Breath
{
    /// <summary>§3.5 비명 억제 시도의 결과.</summary>
    public enum SuppressionResult
    {
        /// <summary>§3.5 "숨 참기 — 숨 게이지 3초 → 비명 발생 안 함". 게이지에서 3이 빠졌다.</summary>
        Suppressed,

        /// <summary>
        /// §3.5 "잠수 중 — 이미 게이지 소모 중 → 자동 억제(물속이라 비명 못 지름)".
        /// <b>추가 비용이 없다</b> — 잠수의 초당 -1이 이미 대가이기 때문이다.
        /// </summary>
        SuppressedByDive,

        /// <summary>§3.5 "게이지 3초 미만 — 억제 불가. 비명 강제 발생".</summary>
        NotEnoughBreath
    }

    /// <summary>이번 틱에 게이지에서 일어난 일.</summary>
    public readonly struct BreathTick
    {
        /// <summary>§5.9-1 "잠수 중 게이지가 0이 되면" — 이번 틱에 질식이 시작됐는가(1회성).</summary>
        public readonly bool Choked;

        public BreathTick(bool choked)
        {
            Choked = choked;
        }
    }

    /// <summary>
    /// §5.9-1 숨 게이지. **게이지는 하나뿐이고**, 잠수(§4.3)와 비명 억제(§3.5)가 이것을 공유한다.
    ///
    /// UnityEngine.Time을 참조하지 않는 순수 로직이라 EditMode로 전부 고정된다
    /// (<c>LocomotionSimulator</c>·<c>ServerKnockDriver</c>와 같은 구조).
    ///
    /// **잠수 판정을 새로 만들지 않았다** — <see cref="BreathConfig.ZoneOf"/>가
    /// <c>MovementState.Diving</c>을 그대로 읽는다. 자세한 이유는 그쪽 주석 참조.
    ///
    /// **§5.9-1 계산 검증표를 그대로 재현한다**(테스트로 고정):
    /// <code>
    /// | 상황                  | 소모 | 대기  | 회복    | 총    |
    /// | 육상 비명 억제 1회     | -3  | 2초  | 0.75초 | 2.75초 |
    /// | 수면에서 비명 억제 1회 | -3  | 2초  | 1.5초  | 3.5초  |
    /// | 7초 잠수 후 물 밖      | -7  | 2초  | 1.75초 | 3.75초 |
    /// | 전체 고갈(8초) 후 물 밖 | -8  | 2초  | 2.0초  | 4.0초  |
    /// </code>
    /// </summary>
    public sealed class BreathGauge
    {
        private float _current = BreathConfig.TotalSeconds;
        private float _recoveryDelayRemaining;
        private float _chokePenaltyRemaining;

        /// <summary>남은 숨(초). 0 ~ <see cref="BreathConfig.TotalSeconds"/>.</summary>
        public float Current => _current;

        /// <summary>
        /// §5.9-1 "회복 대기" 플래그. 마지막 소모로부터 <see cref="BreathConfig.RecoveryDelaySeconds"/>가
        /// 지나지 않았으면 true — 이 동안에는 수면·물 밖이어도 회복하지 않는다.
        /// </summary>
        public bool IsRecoveryDelayed => _recoveryDelayRemaining > 0f;

        /// <summary>회복이 시작되기까지 남은 시간(초). 진단·HUD용.</summary>
        public float RecoveryDelayRemaining => _recoveryDelayRemaining;

        /// <summary>§5.9-1 질식 페널티가 남아 있는가(이동속도 -20% 구간).</summary>
        public bool IsChokePenaltyActive => _chokePenaltyRemaining > 0f;

        /// <summary>질식 페널티 잔여 시간(초).</summary>
        public float ChokePenaltyRemaining => _chokePenaltyRemaining;

        /// <summary>
        /// §5.9-1 질식 페널티 "이동속도 -20% 3초"를 배율로 노출한다.
        /// 페널티 중 0.8, 평소 1.0.
        /// </summary>
        public float SpeedMultiplier => IsChokePenaltyActive ? BreathConfig.ChokeSpeedMultiplier : 1f;

        /// <summary>
        /// §5.9-1 "강제 부상" — 숨이 0이면 더 잠수할 수 없다.
        ///
        /// 기획서는 고갈 **순간**의 강제 부상만 적었지만, 0인 채로 다시 잠수할 수 있으면
        /// 그 프레임에 또 질식해 강제 부상이 무의미해진다. 최소한의 충실한 해석으로
        /// "숨이 남아 있어야 잠수할 수 있다"를 둔다.
        /// </summary>
        public bool CanSubmerge => _current > 0f;

        /// <summary>§3.5 억제를 시도할 수 있는가(§3.5 "게이지 3초 미만 — 억제 불가").</summary>
        public bool CanSuppress => _current >= BreathConfig.SuppressionCost;

        /// <summary>
        /// 시간을 진전시킨다. <paramref name="zone"/>은 §5.9-1 3상태이며,
        /// <see cref="BreathConfig.ZoneOf"/>로 이동 상태에서 유도해 넘기는 것이 정석이다.
        ///
        /// **순서가 규칙 그 자체다**:
        /// ① 잠수면 초당 -1을 빼고 회복 대기를 2초로 **매 틱 리셋**한다 —
        ///    §5.9-1 "회복 중 다시 잠수하면 즉시 중단되고 초당 -1로 전환된다(회복 대기 플래그도
        ///    2초로 리셋)"가 이것으로 성립한다. 부상하는 순간부터 2초를 세기 시작한다.
        /// ② 잠수가 아니면 회복 대기를 소진하고, 다 소진됐을 때만 회복한다.
        ///    <b>대기와 회복은 같은 틱에 겹치지 않는다</b> — 대기가 끝난 잔여 시간까지
        ///    회복으로 돌리면 §5.9-1 검증표의 "2초 + 회복시간" 합이 어긋난다.
        /// </summary>
        public BreathTick Tick(BreathZone zone, float deltaSeconds)
        {
            if (deltaSeconds <= 0f)
                return new BreathTick(false);

            if (_chokePenaltyRemaining > 0f)
                _chokePenaltyRemaining = Max0(_chokePenaltyRemaining - deltaSeconds);

            if (zone == BreathZone.Submerged)
                return new BreathTick(TickSubmerged(deltaSeconds));

            TickRecovery(zone, deltaSeconds);
            return new BreathTick(false);
        }

        private bool TickSubmerged(float deltaSeconds)
        {
            bool hadBreath = _current > 0f;

            _current = Max0(_current - BreathConfig.DivePerSecond * deltaSeconds);

            // 잠수 중에는 계속 소모하고 있으므로 "마지막 소모 시점"이 매 틱 갱신된다.
            _recoveryDelayRemaining = BreathConfig.RecoveryDelaySeconds;

            // §5.9-1 "잠수 중 게이지가 0이 되면" — 0으로 **떨어지는 그 틱**에만 1회.
            if (!hadBreath || _current > 0f)
                return false;

            _chokePenaltyRemaining = BreathConfig.ChokePenaltySeconds;
            return true;
        }

        private void TickRecovery(BreathZone zone, float deltaSeconds)
        {
            if (_recoveryDelayRemaining > 0f)
            {
                _recoveryDelayRemaining = Max0(_recoveryDelayRemaining - deltaSeconds);
                return; // §5.9-1 "회복 조건: … 회복 대기 플래그 = false"
            }

            _current += BreathConfig.RecoveryPerSecond(zone) * deltaSeconds;
            if (_current > BreathConfig.TotalSeconds)
                _current = BreathConfig.TotalSeconds; // §5.9-1 "최대 8초에서 상한"
        }

        /// <summary>
        /// §3.5 비명 억제를 시도한다. 성공하면 게이지에서 <see cref="BreathConfig.SuppressionCost"/>가
        /// 즉시 빠지고 회복 대기가 2초로 리셋된다.
        ///
        /// <paramref name="zone"/>이 잠수면 §3.5 표대로 **자동 억제**이며 추가 비용이 없다 —
        /// "이미 게이지 소모 중"이 그 대가다. 그래서 억제(-3)와 잠수 소모(-1/초)가 같은 프레임에
        /// 겹쳐 이중으로 빠지는 일이 구조적으로 발생하지 않는다.
        ///
        /// 게이지가 3 미만이면 <see cref="SuppressionResult.NotEnoughBreath"/>이며
        /// <b>게이지를 건드리지 않는다</b> — 부분 차감도, 음수도 없다(§3.5 "억제 불가").
        /// </summary>
        public SuppressionResult TrySuppressScream(BreathZone zone)
        {
            if (zone == BreathZone.Submerged)
                return SuppressionResult.SuppressedByDive;

            if (!CanSuppress)
                return SuppressionResult.NotEnoughBreath;

            _current = Max0(_current - BreathConfig.SuppressionCost);
            _recoveryDelayRemaining = BreathConfig.RecoveryDelaySeconds;
            return SuppressionResult.Suppressed;
        }

        /// <summary>새 라운드를 위해 만충 상태로 되돌린다(대기·질식 페널티도 함께 비운다).</summary>
        public void Reset()
        {
            _current = BreathConfig.TotalSeconds;
            _recoveryDelayRemaining = 0f;
            _chokePenaltyRemaining = 0f;
        }

        private static float Max0(float value) => value > 0f ? value : 0f;
    }
}
