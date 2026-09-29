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
        /// <b>대화</b>(9m) 파문 링 시작 순간의 라이트 세기(프로토타입 값 — 튜닝 대상). URP 포인트 라이트는 1/d² 감쇠라 벽에 닿는 조도는
        /// 세기 ÷ 거리²다. 09-27에는 40으로 잡았으나 09-30 실기에서 2m 앞 벽이 회백색(조도 10)으로 과노출이라 18로 낮췄다
        /// (2m 벽 조도 4.5, 바닥 알베도 sRGB 0.1 · 벽 0.24 기준으로 sRGB ≈ 0.5). 최종값은 실기 F5 · F6/F7로 정한다.
        /// </summary>
        public const float TalkPeakIntensity = 18f;

        /// <summary>속삭임(4m) 세기 — 기본값은 <see cref="DerivedPeak"/>(대화 기준 × 4 ÷ 9 = 8). 튜닝하면 이 줄만 숫자로 바꾼다.</summary>
        public static readonly float WhisperPeakIntensity = DerivedPeak(SoundType.Whisper);

        /// <summary>고함(22m) 세기 — 기본값은 <see cref="DerivedPeak"/>(대화 기준 × 22 ÷ 9 = 44). 튜닝하면 이 줄만 숫자로 바꾼다.</summary>
        public static readonly float ShoutPeakIntensity = DerivedPeak(SoundType.Shout);

        /// <summary>
        /// 등급별 기준 세기(<b>QA 배율 적용 전</b>). 목소리가 아니면 대화 값(조명은 목소리에만 켜지므로 쓰이지 않는다).
        /// </summary>
        public static float BasePeakIntensityFor(SoundType type)
        {
            switch (type)
            {
                case SoundType.Whisper: return WhisperPeakIntensity;
                case SoundType.Shout: return ShoutPeakIntensity;
                default: return TalkPeakIntensity;
            }
        }

        /// <summary>실제로 쓰는 등급별 세기 = 기준 세기 × QA 배율(<see cref="QaMultiplier"/>, 기본 1).</summary>
        public static float PeakIntensityFor(SoundType type) => BasePeakIntensityFor(type) * QaMultiplier;

        // ── QA 배율(09-30) — 실기에서 세 등급을 한꺼번에 밝히거나 어둡히며 최종값을 정한다 ─────────
        // 배율은 정수 단계의 거듭제곱(1.25^n)이라 F6/F7을 번갈아 눌러도 1.00으로 정확히 돌아온다(0.8 × 1.25 부동소수 오차 없음).
        // 기본 1이고 QA 빌드의 F6/F7만 바꾼다 — 릴리즈에는 손대는 곳이 없다.

        /// <summary>한 단계의 배율(F7 = ×1.25, F6 = ×0.8 = 1 ÷ 1.25).</summary>
        public const float QaStepFactor = 1.25f;

        /// <summary>단계 범위 — 1.25^±8 ≈ ×0.17 ~ ×5.96.</summary>
        public const int QaStepLimit = 8;

        private static int _qaStep;

        /// <summary>현재 QA 배율(기본 1).</summary>
        public static float QaMultiplier => _qaStep == 0 ? 1f : Mathf.Pow(QaStepFactor, _qaStep);

        /// <summary>배율을 <paramref name="steps"/>단계 올리거나(+) 내린다(−). 범위를 넘으면 끝에서 멈춘다. 바뀐 배율을 돌려준다.</summary>
        public static float AdjustQaMultiplier(int steps)
        {
            _qaStep = Mathf.Clamp(_qaStep + steps, -QaStepLimit, QaStepLimit);
            return QaMultiplier;
        }

        public static void ResetQaMultiplier() => _qaStep = 0;

        /// <summary>오버레이 한 줄 — 현재 배율과 (배율 적용 후) 세 등급 값.</summary>
        public static string FormatQaSummary() =>
            System.FormattableString.Invariant(
                $"음성 조명 ×{QaMultiplier:0.00} — 속삭임 {PeakIntensityFor(SoundType.Whisper):0.#} · 대화 {PeakIntensityFor(SoundType.Talk):0.#} · 고함 {PeakIntensityFor(SoundType.Shout):0.#}");

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => _qaStep = 0;

        /// <summary>
        /// 기본값 유도 — <b>대화 기준 × (반경 ÷ 대화 반경)</b>(반경에 1제곱 비례). 처음(09-29)에는 제곱 비례였다 — 각 등급이 자기 반경의 같은
        /// 비율 거리에서 같은 밝기가 되도록. 그러나 <b>1/d² 감쇠와 겹쳐</b> 같은 벽(2m)에서 고함이 대화의 6배(조도 ≈ 60 vs 10)가 되어
        /// 완전 백색으로 날아갔고 속삭임은 대화 옆에서 묻혔다(09-30 실기). 1제곱이면 같은 벽에서 세 등급이 8 : 18 : 44 = 1 : 2.25 : 5.5로,
        /// 큰 소리가 더 밝되 넘치지 않는다. 반경은 §5.1 표(<see cref="ServerPulseDriver.TryGetPulseSpec"/>)에서 읽는다.
        /// </summary>
        public static float DerivedPeak(SoundType type)
        {
            if (!ServerPulseDriver.TryGetPulseSpec(SoundType.Talk, out float talkRadius, out _) || talkRadius <= 0f
                || !ServerPulseDriver.TryGetPulseSpec(type, out float radius, out _))
                return TalkPeakIntensity;

            return TalkPeakIntensity * (radius / talkRadius);
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
