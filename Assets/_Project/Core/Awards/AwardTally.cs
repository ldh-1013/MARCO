using System.Collections.Generic;
using Marco.Core.Sound;

namespace Marco.Core.Awards
{
    /// <summary>§8 어워드 3종.</summary>
    public enum AwardKind
    {
        /// <summary>최다 비명상 — "SoundType.Shout 발생 횟수".</summary>
        LoudestScream,

        /// <summary>무성 생존상 — "이동거리 대비 파문 발생 0회 여부".</summary>
        SilentSurvivor,

        /// <summary>최고의 거짓말상 — "메아리 노크 성공 유인 횟수".</summary>
        BestLiar
    }

    /// <summary>어워드 3종의 수상자(플레이어 id). <see cref="AwardTally.NoWinner"/>면 수상자 없음.</summary>
    public readonly struct AwardResults
    {
        public readonly int LoudestScream;
        public readonly int SilentSurvivor;
        public readonly int BestLiar;

        public AwardResults(int loudestScream, int silentSurvivor, int bestLiar)
        {
            LoudestScream = loudestScream;
            SilentSurvivor = silentSurvivor;
            BestLiar = bestLiar;
        }

        public int For(AwardKind kind) => kind switch
        {
            AwardKind.LoudestScream => LoudestScream,
            AwardKind.SilentSurvivor => SilentSurvivor,
            _ => BestLiar
        };
    }

    /// <summary>
    /// §8 "결과 화면 전환 즉시 어워드 3종 판정"의 순수 집계(스프린트 22).
    ///
    /// **기획서 원문(§8)**: 판정 기준은 세션 로그의
    /// <c>SoundType.Shout 발생 횟수</c> / <c>이동거리 대비 파문 발생 0회 여부</c> /
    /// <c>메아리 노크 성공 유인 횟수</c>로 각각 산출.
    ///
    /// 원문이 정하지 않은 것은 만들어내지 않고 GAP으로 남겼다 — 동점 처리(GAP-38),
    /// "성공 유인"의 정의(GAP-39), 최소 이동거리 기준(GAP-40).
    ///
    /// **집계 단위**: §8이 "세션 로그"라고 하므로 라운드가 아니라 **세션 누적**이다.
    /// 서버만 집계하고 결과(수상자 id)만 전파한다.
    ///
    /// Unity·FishNet을 모르는 순수 클래스라 EditMode에서 그대로 검증된다.
    /// </summary>
    public sealed class AwardTally
    {
        /// <summary>수상자가 없음(조건을 만족한 플레이어가 하나도 없을 때).</summary>
        public const int NoWinner = -1;

        private sealed class Entry
        {
            public int ShoutCount;
            public int PulseCount;
            public float DistanceMeters;
            public int KnockLureCount;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        /// <summary>집계에 잡힌 플레이어 수(테스트·진단용).</summary>
        public int TrackedPlayers => _entries.Count;

        /// <summary>
        /// 파문 1건을 기록한다. <see cref="SoundType.Shout"/>이면 비명 횟수도 함께 오른다.
        ///
        /// **무성 생존상의 "파문 발생 0회"는 종류를 가리지 않는다** — 발소리든 비명이든
        /// 파문을 냈으면 무성이 아니다.
        /// </summary>
        public void RecordPulse(int playerId, SoundType type)
        {
            Entry entry = GetOrCreate(playerId);
            entry.PulseCount++;

            if (type == SoundType.Shout)
                entry.ShoutCount++;
        }

        /// <summary>이동 거리를 누적한다(음수·0은 무시).</summary>
        public void AddDistance(int playerId, float meters)
        {
            if (meters <= 0f)
                return;

            GetOrCreate(playerId).DistanceMeters += meters;
        }

        /// <summary>
        /// 메아리 노크로 술래를 유인하는 데 성공한 횟수. **현재 호출자가 없다** —
        /// 노크(§3.2 메아리 능력)가 미구현이라 데이터 소스가 존재하지 않는다(GAP-37).
        /// 기능이 생기면 여기로 넣으면 되도록 자리를 맞춰 둔다.
        /// </summary>
        public void RecordKnockLure(int playerId) => GetOrCreate(playerId).KnockLureCount++;

        /// <summary>플레이어가 집계에 등록만 되도록 한다(파문·이동이 없어도 후보로 잡히게).</summary>
        public void Track(int playerId) => GetOrCreate(playerId);

        /// <summary>§8 세 기준으로 수상자를 산출한다.</summary>
        public AwardResults Evaluate()
        {
            return new AwardResults(
                BestBy(static e => e.ShoutCount > 0, static e => e.ShoutCount),
                BestBy(static e => e.PulseCount == 0 && e.DistanceMeters > 0f, static e => e.DistanceMeters),
                BestBy(static e => e.KnockLureCount > 0, static e => e.KnockLureCount));
        }

        /// <summary>세션 집계를 비운다(새 세션 시작 시).</summary>
        public void Reset() => _entries.Clear();

        /// <summary>진단용 조회. 없는 플레이어는 전부 0.</summary>
        public void Peek(int playerId, out int shouts, out int pulses, out float distance, out int knockLures)
        {
            if (!_entries.TryGetValue(playerId, out Entry entry))
            {
                shouts = 0;
                pulses = 0;
                distance = 0f;
                knockLures = 0;
                return;
            }

            shouts = entry.ShoutCount;
            pulses = entry.PulseCount;
            distance = entry.DistanceMeters;
            knockLures = entry.KnockLureCount;
        }

        private Entry GetOrCreate(int playerId)
        {
            if (!_entries.TryGetValue(playerId, out Entry entry))
            {
                entry = new Entry();
                _entries[playerId] = entry;
            }

            return entry;
        }

        /// <summary>
        /// 자격을 갖춘 플레이어 중 점수가 가장 높은 쪽. 동점이면 **id가 작은 쪽**을 택한다 —
        /// 기획서에 동점 규칙이 없어(GAP-38) 결정론적인 값을 고른 것이다(서버·테스트 재현성).
        /// </summary>
        private int BestBy(System.Func<Entry, bool> qualifies, System.Func<Entry, float> score)
        {
            int winner = NoWinner;
            float best = 0f;

            foreach (KeyValuePair<int, Entry> pair in _entries)
            {
                if (!qualifies(pair.Value))
                    continue;

                float value = score(pair.Value);
                if (winner == NoWinner || value > best || (value == best && pair.Key < winner))
                {
                    winner = pair.Key;
                    best = value;
                }
            }

            return winner;
        }
    }
}
