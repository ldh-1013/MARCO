using System;
using System.Collections.Generic;
using Marco.Core.Sound;
using UnityEngine;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// §16.4 잔상(Afterglow) 수치.
    ///
    /// <para>
    /// 기획서 본안은 <b>지형 모서리만 · 15% → 0% · 12초</b>이고, 차선책이 <b>8% · 8초</b>다.
    /// 이 프로토타입은 <b>차선책으로 확정</b>했다(사용자 지시 — "모서리 렌더링 시도 안 함").
    /// 현재 렌더러가 LineRenderer 링 기반이라 지형 모서리를 검출하는 경로(에지 포스트프로세스·
    /// 파문 셰이더)가 없고, §16.3 "파문 셰이더 1개로 수렴" 원칙상 새 파이프라인을 따로 만들지 않았다.
    /// 잔상은 <b>자기 파문 링의 최종 반경</b>에 남는다.
    /// </para>
    /// </summary>
    public static class AfterglowConfig
    {
        /// <summary>§16.4 본안 — 모서리 렌더 전용. 이 프로토타입에서는 쓰지 않는다.</summary>
        public const float EdgeBrightness = 0.15f;
        public const float EdgeSeconds = 12f;

        /// <summary>§16.4 차선책 — "모서리 렌더가 불가능할 경우에만 8% 밝기 / 8초".</summary>
        public const float FallbackBrightness = 0.08f;
        public const float FallbackSeconds = 8f;

        /// <summary>실제로 쓰는 값(차선책).</summary>
        public static float Brightness => FallbackBrightness;
        public static float Seconds => FallbackSeconds;

        /// <summary>§16.4 "선형 감쇠" — 시작 밝기에서 0까지.</summary>
        public static float AlphaAt(float age)
        {
            if (age < 0f)
                age = 0f;
            if (age >= Seconds)
                return 0f;

            return Brightness * (1f - age / Seconds);
        }
    }

    /// <summary>
    /// §16.4 잔상 목록. <b>자기 파문만</b> 여기 들어온다 — 타인 파문에 잔상이 남으면 정보량이 폭증해
    /// 게임이 쉬워진다(지시서 6-B). 호출부(렌더러)는 자기 파문 경로에서만 <see cref="Add"/>를 부른다.
    /// Unity 수명주기와 무관 — EditMode 테스트 가능.
    /// </summary>
    public sealed class AfterglowTracker
    {
        public readonly struct Entry
        {
            public readonly int Id;
            public readonly Vector3 Position;
            public readonly float Radius;
            public readonly float StartTime;

            public Entry(int id, Vector3 position, float radius, float startTime)
            {
                Id = id;
                Position = position;
                Radius = radius;
                StartTime = startTime;
            }

            public float Alpha(float now) => AfterglowConfig.AlphaAt(now - StartTime);
        }

        private readonly List<Entry> _entries = new List<Entry>();
        private int _nextId = 1;

        public event Action<int> Removed;

        public int Count => _entries.Count;

        public int Add(Vector3 position, float radius, float now)
        {
            int id = _nextId++;
            _entries.Add(new Entry(id, position, radius, now));
            return id;
        }

        public void Tick(float now)
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                if (now - _entries[i].StartTime < AfterglowConfig.Seconds)
                    continue;

                int id = _entries[i].Id;
                _entries.RemoveAt(i);
                Removed?.Invoke(id);
            }
        }

        public void CopyTo(List<Entry> buffer)
        {
            buffer.Clear();
            buffer.AddRange(_entries);
        }

        public void Clear()
        {
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                int id = _entries[i].Id;
                _entries.RemoveAt(i);
                Removed?.Invoke(id);
            }
        }
    }

    /// <summary>§3.2-1 메아리 소나 정보량 제한.</summary>
    public static class EchoSonarConfig
    {
        /// <summary>§3.2-1 "잔류 흔적 — 소멸 후 3초간 감쇠"(테스트 범위 2~5초).</summary>
        public const float ResidualSeconds = 3f;

        /// <summary>§3.2-1 "동시 표시 상한 — 최근 8개"(테스트 범위 6~12개).</summary>
        public const int MaxVisible = 8;
    }

    /// <summary>
    /// §3.2-1 메아리가 보는 파문 집합. 서버가 이미 "전부" 보내 주므로(판정은 서버), 여기서는
    /// <b>정보량 제한</b>만 한다 — 소멸 후 3초 잔류 + 최근 8개.
    ///
    /// <para>
    /// 밝기: 파문이 살아 있는 동안 1.0(단색), 소멸 후 3초간 1.0 → 0 선형. 새 밝기 값을 만들지 않으려고
    /// "소멸 후 감쇠"를 문자 그대로 옮겼다 — 살아 있는 동안에도 옅어지면 소멸 시점에 잔류가 다시
    /// 밝아지는 불연속이 생긴다.
    /// </para>
    ///
    /// <para>
    /// "최근"의 기준은 <b>파문 발생 시각</b>이다. 잔류 흔적도 자기 발생 시각으로 줄을 선다 —
    /// 오래된 것부터 버린다(지시서 6-A ★).
    /// </para>
    /// </summary>
    public sealed class EchoSonarView
    {
        public readonly struct Item
        {
            /// <summary>파문 ID(원본 파문과 같은 키). 잔류 흔적도 같은 ID를 쓴다.</summary>
            public readonly int PulseId;
            public readonly Vector3 Position;
            public readonly float Radius;
            public readonly float Alpha;
            public readonly bool Residual;
            public readonly float PulseStart;

            /// <summary>살아 있는 파문이면 확장 진행도(0~1), 잔류면 1.</summary>
            public readonly float Expansion;

            public Item(int pulseId, Vector3 position, float radius, float alpha, bool residual, float pulseStart,
                float expansion)
            {
                PulseId = pulseId;
                Position = position;
                Radius = radius;
                Alpha = alpha;
                Residual = residual;
                PulseStart = pulseStart;
                Expansion = expansion;
            }
        }

        private readonly struct ResidualTrace
        {
            public readonly int PulseId;
            public readonly Vector3 Position;
            public readonly float Radius;
            public readonly float PulseStart;
            public readonly float ExpiredAt;

            public ResidualTrace(int pulseId, Vector3 position, float radius, float pulseStart, float expiredAt)
            {
                PulseId = pulseId;
                Position = position;
                Radius = radius;
                PulseStart = pulseStart;
                ExpiredAt = expiredAt;
            }
        }

        private readonly List<ResidualTrace> _residuals = new List<ResidualTrace>();
        private readonly List<Item> _candidates = new List<Item>();

        private static readonly Comparison<Item> NewestFirst = (a, b) => b.PulseStart.CompareTo(a.PulseStart);

        public int ResidualCount => _residuals.Count;

        /// <summary>파문이 자연 만료됐다 — 잔류 흔적을 남긴다. 좌표가 있는(월드 링) 파문만.</summary>
        public void NotifyExpired(in PulseVisualState state, float now)
        {
            if (state.Kind != PulseVisualKind.WorldRing)
                return;

            _residuals.Add(new ResidualTrace(state.PulseId, state.SourcePos, state.Radius, state.StartTime, now));
        }

        public void Tick(float now)
        {
            for (int i = _residuals.Count - 1; i >= 0; i--)
            {
                if (now - _residuals[i].ExpiredAt >= EchoSonarConfig.ResidualSeconds)
                    _residuals.RemoveAt(i);
            }
        }

        /// <summary>
        /// 이번 프레임에 그릴 목록(최대 8개, 최신 순)을 <paramref name="output"/>에 채운다.
        /// </summary>
        public void Select(IReadOnlyList<PulseVisualState> active, float now, List<Item> output)
        {
            _candidates.Clear();

            for (int i = 0; i < active.Count; i++)
            {
                PulseVisualState s = active[i];
                if (s.Kind != PulseVisualKind.WorldRing)
                    continue;

                _candidates.Add(new Item(s.PulseId, s.SourcePos, s.Radius, 1f, false, s.StartTime, s.Progress01(now)));
            }

            for (int i = 0; i < _residuals.Count; i++)
            {
                ResidualTrace r = _residuals[i];
                float age = now - r.ExpiredAt;
                if (age >= EchoSonarConfig.ResidualSeconds)
                    continue;

                float alpha = 1f - Mathf.Clamp01(age / EchoSonarConfig.ResidualSeconds);
                _candidates.Add(new Item(r.PulseId, r.Position, r.Radius, alpha, true, r.PulseStart, 1f));
            }

            _candidates.Sort(NewestFirst);

            output.Clear();
            int take = Math.Min(EchoSonarConfig.MaxVisible, _candidates.Count);
            for (int i = 0; i < take; i++)
                output.Add(_candidates[i]);
        }

        public void Clear() => _residuals.Clear();
    }

    /// <summary>
    /// 로컬 플레이어가 <b>자기가 낸 소리</b>를 0ms로 보는 경로(GAP-1 "본인 발생 펄스는 서버 경로로
    /// 절대 생성되지 않는다 — 본인 목소리는 항상 로컬 0ms 렌더 경로로만 처리된다").
    ///
    /// <para>
    /// <b>[블록 6에서 발견·신설.]</b> 네트워크 모드에서는 발소리·밸브·음성이 서버로만 가고(서버가 반경·위치를
    /// 재계산) GAP-1로 본인에게는 돌아오지 않는데, 로컬 렌더도 하지 않아 <b>본인이 자기 파문을 전혀
    /// 보지 못했다.</b> "말해야 보인다"가 네트워크에서 성립하지 않았던 것이다. §16.4 잔상의 대상도
    /// "자기 SoundPulse"라 이 경로 없이는 잔상을 붙일 곳이 없다.
    /// </para>
    ///
    /// <para>
    /// 여기 올라오는 반경은 <b>발생 반경</b>(§5.0 ①)이다 — 인지 배율은 적용하지 않는다(GAP-1 문구).
    /// 판정 권위와 무관한 <b>표시 전용</b> 신호다. 다른 사람에게 무엇이 들리는지는 여전히 서버가 정한다.
    /// </para>
    /// </summary>
    public static class SelfPulseFeed
    {
        public static event Action<SoundType, float, float, Vector3> Emitted;

        public static void Raise(SoundType type, float radius, float duration, Vector3 position)
        {
            if (radius <= 0f || duration <= 0f)
                return;

            Emitted?.Invoke(type, radius, duration, position);
        }

        /// <summary>§5.1 표 반경·지속으로 올린다(음성·밸브처럼 표 값이 곧 발생값인 소리).</summary>
        public static void RaiseFromTable(SoundType type, Vector3 position)
        {
            if (ServerPulseDriver.TryGetPulseSpec(type, out float radius, out float duration))
                Raise(type, radius, duration, position);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForNewSession() => Emitted = null;
    }
}
