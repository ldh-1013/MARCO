using System;
using System.Collections.Generic;
using Marco.Core.Role;

namespace Marco.Core.Sound
{
    /// <summary>
    /// §3.6 장시간 정지 시 자동 소리(캠핑 방지) 수치. 전부 §3.6 표에서 옮겼다.
    /// </summary>
    public static class CampingConfig
    {
        /// <summary>§3.6 "20초 동안" — 슬라이딩 윈도우 길이.</summary>
        public const float WindowSeconds = 20f;

        /// <summary>§3.6 "누적 이동거리 3m 미만". 3.0m 정확히는 발동하지 않는다(미만).</summary>
        public const float TriggerDistanceMeters = 3f;

        /// <summary>§3.6 1단계 호흡음 발생 반경 3m. §5.1 Breath 행 "3m → 9m"의 시작값.</summary>
        public const float FirstRadiusMeters = 3f;

        /// <summary>§3.6 · §5.1 호흡음 지속 0.6초.</summary>
        public const float DurationSeconds = 0.6f;

        /// <summary>§3.6 "15초마다 발생 반경 +2m" — 증폭 주기이자 발생 빈도(§3.6 억제 근거 표 "15초마다").</summary>
        public const float IntervalSeconds = 15f;

        /// <summary>§3.6 증폭 폭 +2m.</summary>
        public const float StepMeters = 2f;

        /// <summary>§3.6 최대 9m.</summary>
        public const float MaxRadiusMeters = 9f;

        /// <summary>§3.6 "5m 이상 이동 시 즉시" 초기화. 5.0m 정확히 초기화된다(이상).</summary>
        public const float ResetDistanceMeters = 5f;

        /// <summary>§3.6 "적용: 도망자 + 술래 / 미적용: 메아리".</summary>
        public static bool AppliesTo(RoleType role) => role != RoleType.Echo;

        /// <summary>
        /// 발동 후 <paramref name="pulseIndex"/>번째(0부터) 호흡음의 반경.
        /// 20초 3m → 35초 5m → 50초 7m → 65초 9m(§3.6 "캠핑 최대(65초~)") → 이후 9m 유지.
        /// </summary>
        public static float RadiusForPulse(int pulseIndex)
        {
            if (pulseIndex < 0)
                pulseIndex = 0;

            return Math.Min(MaxRadiusMeters, FirstRadiusMeters + pulseIndex * StepMeters);
        }
    }

    /// <summary>
    /// §3.6 캠핑 방지 판정기(플레이어 1명분). <b>서버가 소유한다</b> — 위치·잠수·밸브 조작은 서버가
    /// 알고, 클라이언트가 주장할 수 있는 것은 "메뉴가 열려 있다" 하나뿐이다(GAP-24).
    ///
    /// <para>
    /// <b>판정은 순간 속도가 아니라 20초 슬라이딩 윈도우의 누적 이동거리다</b>(§3.6 "미세 이동 악용
    /// 방지"). 제자리 흔들기로 3m를 채울 수 없게 하려는 것이며, 이동거리는 서버가 관측한 위치
    /// 변화의 합이다.
    /// </para>
    ///
    /// <para>
    /// <b>일시중단(§3.6 "발동하지 않는 상태")은 초기화가 아니라 정지다.</b> 중단 중에는 시간도
    /// 이동도 세지 않는다 — 밸브를 8초 돌리고 나와도 그 전의 정지 시간이 이어진다.
    /// </para>
    /// </summary>
    public sealed class CampingMonitor
    {
        private readonly struct Sample
        {
            public readonly float Time;
            public readonly float Meters;

            public Sample(float time, float meters)
            {
                Time = time;
                Meters = meters;
            }
        }

        private readonly Queue<Sample> _window = new Queue<Sample>();
        private float _windowMeters;

        /// <summary>중단을 뺀 관측 시간(초). 초기화 후 20초가 차야 발동할 수 있다.</summary>
        private float _clock;
        private float _observedSince;

        private float _activeElapsed;
        private float _movedSinceActive;
        private int _pulsesFired;

        /// <summary>발동 중인가(호흡음이 나고 있는 상태).</summary>
        public bool IsActive { get; private set; }

        /// <summary>발동 후 낸 호흡음 수.</summary>
        public int PulsesFired => _pulsesFired;

        /// <summary>최근 20초(중단 제외) 누적 이동거리.</summary>
        public float WindowMeters => _windowMeters;

        /// <summary>
        /// 시간과 이동을 진전시킨다. 이번 틱에 낼 호흡음의 반경을 돌려준다(없으면 0).
        /// 한 틱에 둘 이상이 몰리면(긴 틱) 마지막 것 하나만 낸다 — 같은 자리 파문을 겹쳐 봐야
        /// 정보가 늘지 않는다.
        /// </summary>
        /// <param name="movedMeters">이번 틱에 서버가 관측한 이동거리.</param>
        /// <param name="suspended">§3.6 미발동 상태(밸브·잠수·메뉴·이동불가 연출).</param>
        public float Tick(float deltaSeconds, float movedMeters, bool suspended)
        {
            if (suspended || deltaSeconds <= 0f)
                return 0f;

            if (movedMeters < 0f)
                movedMeters = 0f;

            if (IsActive)
                return TickActive(deltaSeconds, movedMeters);

            _clock += deltaSeconds;
            _window.Enqueue(new Sample(_clock, movedMeters));
            _windowMeters += movedMeters;

            while (_window.Count > 0 && _window.Peek().Time <= _clock - CampingConfig.WindowSeconds)
                _windowMeters -= _window.Dequeue().Meters;

            // 부동소수 누적 오차로 음수가 되지 않게.
            if (_windowMeters < 0f)
                _windowMeters = 0f;

            bool observedFullWindow = _clock - _observedSince >= CampingConfig.WindowSeconds - 1e-4f;
            if (!observedFullWindow || _windowMeters >= CampingConfig.TriggerDistanceMeters)
                return 0f;

            // 발동 — §3.6 "1단계 호흡음 3m"를 즉시 1회.
            IsActive = true;
            _activeElapsed = 0f;
            _movedSinceActive = 0f;
            _pulsesFired = 1;
            return CampingConfig.RadiusForPulse(0);
        }

        private float TickActive(float deltaSeconds, float movedMeters)
        {
            _movedSinceActive += movedMeters;
            if (_movedSinceActive >= CampingConfig.ResetDistanceMeters)
            {
                Reset();
                return 0f;
            }

            _activeElapsed += deltaSeconds;
            float radius = 0f;
            while (_activeElapsed >= _pulsesFired * CampingConfig.IntervalSeconds - 1e-4f)
            {
                radius = CampingConfig.RadiusForPulse(_pulsesFired);
                _pulsesFired++;
            }

            return radius;
        }

        /// <summary>§3.6 초기화(5m 이동·라운드 시작·역할 전환). 20초를 처음부터 다시 센다.</summary>
        public void Reset()
        {
            IsActive = false;
            _window.Clear();
            _windowMeters = 0f;
            _observedSince = _clock;
            _activeElapsed = 0f;
            _movedSinceActive = 0f;
            _pulsesFired = 0;
        }
    }
}
