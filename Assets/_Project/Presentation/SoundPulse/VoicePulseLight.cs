using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// 목소리 파문 조명 리빌 수치 — <b>2026-09-27 디자인 변경</b>(§16.1 · §16.4 개정): 자기 목소리 파문은 차폐되는
    /// 로컬 포인트 라이트로 면을 비춘다. 발소리 · 타인 파문은 윤곽선만.
    ///
    /// <para>
    /// 반경 · 지속은 여기 없다 — 파문 자신의 값(§5.1, 확장 링 · 벽 윤곽 리빌과 같은 값)을 쓴다. 여기 있는 것은 표현값뿐이다.
    /// </para>
    /// </summary>
    public static class VoicePulseLightConfig
    {
        /// <summary>
        /// <b>대화</b>(9m) 파문 링 시작 순간의 라이트 세기(프로토타입 값 — 튜닝 대상). URP 포인트 라이트는 1/d² 감쇠라, 바닥 알베도
        /// sRGB 0.1(선형 ≈ 0.01) · 벽 0.24(선형 ≈ 0.047) 기준으로 대화 파문에서 3~4m 벽이 보이게 잡은 값이다.
        /// 1.5 수준이면 1~2m 밖이 거의 검게 남는다.
        /// </summary>
        public const float TalkPeakIntensity = 40f;

        /// <summary>속삭임(4m) 세기 — 기본값은 <see cref="DerivedPeak"/>(대화 기준 × (4 ÷ 9)² ≈ 7.9). 튜닝하면 이 줄만 숫자로 바꾼다.</summary>
        public static readonly float WhisperPeakIntensity = DerivedPeak(SoundType.Whisper);

        /// <summary>고함(22m) 세기 — 기본값은 <see cref="DerivedPeak"/>(대화 기준 × (22 ÷ 9)² ≈ 239). 튜닝하면 이 줄만 숫자로 바꾼다.</summary>
        public static readonly float ShoutPeakIntensity = DerivedPeak(SoundType.Shout);

        /// <summary>등급별 세기. 목소리가 아니면 대화 값(조명은 목소리에만 켜지므로 쓰이지 않는다).</summary>
        public static float PeakIntensityFor(SoundType type)
        {
            switch (type)
            {
                case SoundType.Whisper: return WhisperPeakIntensity;
                case SoundType.Shout: return ShoutPeakIntensity;
                default: return TalkPeakIntensity;
            }
        }

        /// <summary>
        /// 기본값 유도 — <b>대화 기준 × (반경 ÷ 대화 반경)²</b>. 포인트 라이트는 1/d² 감쇠라, 이렇게 두면 각 등급이 자기 반경의
        /// 같은 비율 거리(예: 반경의 40%)에서 대화와 같은 밝기가 된다. 반경은 §5.1 표(<see cref="ServerPulseDriver.TryGetPulseSpec"/>)에서 읽는다.
        /// </summary>
        public static float DerivedPeak(SoundType type)
        {
            if (!ServerPulseDriver.TryGetPulseSpec(SoundType.Talk, out float talkRadius, out _) || talkRadius <= 0f
                || !ServerPulseDriver.TryGetPulseSpec(type, out float radius, out _))
                return TalkPeakIntensity;

            float ratio = radius / talkRadius;
            return TalkPeakIntensity * ratio * ratio;
        }

        /// <summary>동시에 켜지는 조명 리빌 상한. 초과 시 가장 오래된 것부터 회수한다(그림자 포인트 라이트는 6면 렌더라 비싸다).</summary>
        public const int MaxConcurrent = 2;

        /// <summary>
        /// 타인의 목소리 파문도 비출지. 기본 false. <b>지금은 켜도 효과가 없다</b> — 타인 파문은 서버 델리버리
        /// (<c>PerceivedPulse</c>)로 오는데 소리 종류(<see cref="SoundType"/>)가 실려 있지 않아 목소리인지 알 수 없다.
        /// 켜려면 델리버리에 종류를 싣는 네트워크 변경이 먼저 필요하다(이번 범위 밖 — 순수 Presentation).
        /// </summary>
        public const bool LightOthersVoicePulses = false;

        /// <summary>무채색 — 약간 차가운 회색(§16.1 모노크롬 유지).</summary>
        public static readonly Color LightColor = new Color(0.88f, 0.92f, 0.96f, 1f);
    }

    /// <summary>
    /// 목소리 조명 리빌의 시간 규칙 — range는 확장 링의 반경(0 → R), intensity는 링의 밝기 곡선(1 − 진행도).
    /// Unity 수명주기와 무관 — EditMode 테스트 가능.
    /// </summary>
    public static class VoicePulseLightMath
    {
        /// <summary>목소리 3등급(§5.1 속삭임 · 대화 · 고함)인가. 비명 · 노크 · 호흡음은 목소리 파문이 아니다.</summary>
        public static bool IsVoice(SoundType type) =>
            type == SoundType.Whisper || type == SoundType.Talk || type == SoundType.Shout;

        /// <summary>이 파문을 비출지 — 목소리이고, 자기 파문이거나 타인 목소리 조명이 켜져 있을 때.</summary>
        public static bool ShouldLight(SoundType type, bool isLocalSource) =>
            IsVoice(type) && (isLocalSource || VoicePulseLightConfig.LightOthersVoicePulses);

        public static float Progress(float now, float startTime, float duration) =>
            duration <= 0f ? 1f : Mathf.Clamp01((now - startTime) / duration);

        /// <summary>그 순간 링의 반경 — 벽 윤곽 리빌과 같은 값(선형 확장).</summary>
        public static float Range(float radius, float progress) => Mathf.Max(0f, radius) * Mathf.Clamp01(progress);

        /// <summary>링의 페이드 곡선과 같다.</summary>
        public static float Intensity(float peak, float progress) => peak * (1f - Mathf.Clamp01(progress));
    }

    /// <summary>
    /// 조명 슬롯 배정 — 빈 슬롯이 있으면 그것을, 없으면 <b>가장 오래된 것</b>을 회수해 준다.
    /// Unity 수명주기와 무관 — EditMode 테스트 가능.
    /// </summary>
    public sealed class VoiceLightSlots
    {
        private readonly bool[] _active;
        private readonly float[] _startTime;

        public VoiceLightSlots(int capacity)
        {
            if (capacity < 1)
                capacity = 1;
            _active = new bool[capacity];
            _startTime = new float[capacity];
        }

        public int Capacity => _active.Length;

        public bool IsActive(int slot) => _active[slot];

        public float StartTime(int slot) => _startTime[slot];

        public int ActiveCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _active.Length; i++)
                    if (_active[i])
                        n++;
                return n;
            }
        }

        /// <summary>슬롯 하나를 차지한다. 가득 찼으면 시작 시각이 가장 이른 슬롯을 회수한다.</summary>
        public int Acquire(float startTime)
        {
            int slot = -1;
            for (int i = 0; i < _active.Length; i++)
            {
                if (!_active[i])
                {
                    slot = i;
                    break;
                }
            }

            if (slot < 0)
            {
                slot = 0;
                for (int i = 1; i < _active.Length; i++)
                {
                    if (_startTime[i] < _startTime[slot])
                        slot = i;
                }
            }

            _active[slot] = true;
            _startTime[slot] = startTime;
            return slot;
        }

        public void Release(int slot) => _active[slot] = false;

        public void Clear()
        {
            for (int i = 0; i < _active.Length; i++)
                _active[i] = false;
        }
    }

    /// <summary>
    /// 목소리 조명 리빌 — 파문 원점의 눈높이에 URP 포인트 라이트 하나를 켜 링과 함께 넓히고 흐리게 한다.
    /// <see cref="PulseVisualRenderer"/>가 소유한다(확장 링 · 잔상 · 벽 윤곽 리빌은 그대로 — 겹쳐 그려진다).
    ///
    /// <para>
    /// <b>차폐</b>: 라이트는 그림자(Hard)를 드리운다 — 벽 뒤 · 다른 층으로 빛이 새지 않는다. PC 품질 URP 에셋은
    /// Additional Lights 그림자가 켜져 있고(아틀라스 2048), 맵 v2 레벨 렌더러는 전부 그림자 On이다(09-27 확인).
    /// 광원이 로컬 캡슐 안(눈높이 1.62 &lt; 캡슐 1.8)에 있어도 몸 머티리얼이 단면(Cull Back)이라 그림자 맵에 그려지지 않는다.
    /// </para>
    ///
    /// <para>
    /// RenderSettings(환경광 흑)는 건드리지 않는다 — 라이트를 더하기만 한다. 서버 · 게임플레이와 무관한 로컬 연출.
    /// </para>
    /// </summary>
    public sealed class VoicePulseLighting
    {
        private readonly Light[] _lights;
        private readonly float[] _radius;
        private readonly float[] _duration;
        private readonly float[] _peak;
        private readonly VoiceLightSlots _slots;

        public VoicePulseLighting(Transform parent)
        {
            _slots = new VoiceLightSlots(VoicePulseLightConfig.MaxConcurrent);
            _lights = new Light[_slots.Capacity];
            _radius = new float[_slots.Capacity];
            _duration = new float[_slots.Capacity];
            _peak = new float[_slots.Capacity];

            for (int i = 0; i < _lights.Length; i++)
            {
                var go = new GameObject($"VoicePulseLight_{i}");
                go.transform.SetParent(parent, worldPositionStays: false);

                Light light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = VoicePulseLightConfig.LightColor;
                light.shadows = LightShadows.Hard;
                light.shadowStrength = 1f;
                light.range = 0f;
                light.intensity = 0f;
                go.SetActive(false);
                _lights[i] = light;
            }
        }

        /// <summary>진단용 — 지금 켜진 조명 수.</summary>
        public int ActiveCount => _slots.ActiveCount;

        /// <summary>파문이 생겼다. 목소리가 아니면(발소리 등) 아무것도 하지 않는다.</summary>
        public void Emit(SoundType type, bool isLocalSource, Vector3 feet, float radius, float duration, float now)
        {
            if (!VoicePulseLightMath.ShouldLight(type, isLocalSource) || radius <= 0f || duration <= 0f)
                return;

            int slot = _slots.Acquire(now);
            _radius[slot] = radius;
            _duration[slot] = duration;
            _peak[slot] = VoicePulseLightConfig.PeakIntensityFor(type);

            Light light = _lights[slot];
            light.transform.position = feet + Vector3.up * Marco.Core.Locomotion.DiveRules.StandingHeadHeight;
            light.range = 0f;
            light.intensity = 0f;
            light.gameObject.SetActive(true);
        }

        public void Tick(float now)
        {
            for (int i = 0; i < _lights.Length; i++)
            {
                if (!_slots.IsActive(i))
                    continue;

                float progress = VoicePulseLightMath.Progress(now, _slots.StartTime(i), _duration[i]);
                if (progress >= 1f)
                {
                    _slots.Release(i);
                    _lights[i].gameObject.SetActive(false);
                    continue;
                }

                _lights[i].range = VoicePulseLightMath.Range(_radius[i], progress);
                _lights[i].intensity = VoicePulseLightMath.Intensity(_peak[i], progress);
            }
        }

        public void Clear()
        {
            _slots.Clear();
            for (int i = 0; i < _lights.Length; i++)
            {
                if (_lights[i] != null)
                    _lights[i].gameObject.SetActive(false);
            }
        }
    }
}
