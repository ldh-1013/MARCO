using System.Collections.Generic;
using Marco.Core.Sound;

namespace Marco.Core.Awards
{
    /// <summary>§8 어워드 3종.</summary>
    public enum AwardKind
    {
        /// <summary>§8.1 최다 비명상 — <see cref="SoundType.Scream"/> 이벤트 발생 횟수가 가장 많은 도망자.</summary>
        LoudestScream,

        /// <summary>§8.2 무성 생존상 — Σ(파문 소음량) ÷ 총 이동거리가 **가장 낮은** 생존자.</summary>
        SilentSurvivor,

        /// <summary>§8.3 최고의 거짓말상 — 메아리 노크 성공 유인 횟수.</summary>
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
    /// §8 "결과 화면 전환 즉시 어워드 3종 판정"의 순수 집계.
    ///
    /// **§8 갱신본 기준으로 전면 교체했다(6단계).** 이전 판정 기준 셋 중 둘이 기획서에서
    /// 잘못이라고 명시적으로 폐기됐다:
    /// <code>
    /// | 어워드      | 이전(폐기)                  | 현재(§8 갱신본)                                  |
    /// | 최다 비명상 | SoundType.Shout 발생 횟수   | SoundType.Scream 이벤트 횟수 (Shout 일절 미반영)  |
    /// | 무성 생존상 | 파문 발생 0회 (이진)         | Σ(반경 × 지속) ÷ 이동거리, 최솟값 (연속값)         |
    /// | 최고의 거짓말상 | 노크 성공 유인 횟수      | 동일 — 판정 규칙만 §8.3 4조건으로 정밀화          |
    /// </code>
    ///
    /// §8.1 갱신 근거: "`Shout`(고함)은 플레이어가 의도해서 내는 소리이고, 비명은 술래 외침에
    /// 의해 강제로 발생하는 `Scream`이다. 집계 기준은 '술래가 인지했는가'가 아니라
    /// **'비명 이벤트가 발생했는가'**다."
    ///
    /// §8.2 갱신 근거: "기존 기준은 사실상 달성 불가능했다(움직이면 반드시 발소리가 난다).
    /// 또 제곱 공식은 **밸브 수행자를 처벌**하고 고함 1회로 수상이 불가능해지는 결함이 있었다.
    /// **제곱 → 선형 + 밸브 제외**의 최소 수정으로 두 결함을 해소했으며 새 상수는 0개다."
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

        /// <summary>
        /// §8.2 "총 이동거리 **1m 미만이면 수상 대상 제외**"(0 나누기 방어).
        /// 가만히 서 있던 플레이어가 소음량 0으로 자동 수상하는 것도 이 조건이 막는다.
        /// </summary>
        public const float MinimumDistanceMeters = 1f;

        private sealed class Entry
        {
            /// <summary>§8.1 <see cref="SoundType.Scream"/> 이벤트 횟수.</summary>
            public int ScreamCount;

            /// <summary>§8.2 Σ(발생 반경 × 지속시간). **밸브는 여기 들어오지 않는다.**</summary>
            public float NoiseSum;

            public float DistanceMeters;
            public int KnockLureCount;

            /// <summary>§8.2 "태그당한 메아리는 제외" — 이번 세션에 태그당한 적이 있는가.</summary>
            public bool WasTaggedOut;
        }

        private readonly Dictionary<int, Entry> _entries = new Dictionary<int, Entry>();

        /// <summary>집계에 잡힌 플레이어 수(테스트·진단용).</summary>
        public int TrackedPlayers => _entries.Count;

        /// <summary>
        /// §8.2 "파문 소음량(1회) = 발생 반경 × 지속시간 — 제곱이 아니라 **선형**".
        ///
        /// **선형인 이유**(§5.0-1): 등급 격차 설계(§9.1)는 노출 *면적*(제곱)을 쓰지만,
        /// 어워드 채점은 상대 비교이므로 선형이 맞다 — 제곱이면 고함 1회로 수상이 불가능해져
        /// **정상 플레이를 처벌한다**. 두 지표는 통합하지 않는다.
        /// </summary>
        public static float NoiseOf(float radiusMeters, float durationSeconds)
        {
            if (radiusMeters <= 0f || durationSeconds <= 0f)
                return 0f;

            return radiusMeters * durationSeconds;
        }

        /// <summary>§8.2 "밸브 회전음은 합계에서 제외" — 목표 수행을 처벌하지 않기 위함이다.</summary>
        public static bool CountsTowardNoise(SoundType type) => type != SoundType.Valve;

        /// <summary>
        /// 파문 1건을 기록한다.
        /// </summary>
        /// <param name="radiusMeters">
        /// §8.2 "**재질 배율 적용 후**의 값 사용(타일 걷기 = 2 × 1.3 = 2.6m)".
        /// 서버가 <c>ServerPulseDriver</c>에서 실제로 등록한 반경을 그대로 넘겨야 한다 —
        /// §5.1 원본 반경을 넘기면 §5.9 재질 효과가 소음량에서 사라진다.
        /// </param>
        /// <param name="durationSeconds">§5.1 지속시간(역할 청취 배율을 곱하기 **전**의 발생 값).</param>
        public void RecordPulse(int playerId, SoundType type, float radiusMeters, float durationSeconds)
        {
            Entry entry = GetOrCreate(playerId);

            // §8.1: Scream만 센다. Shout(고함)은 일절 반영하지 않는다.
            if (type == SoundType.Scream)
                entry.ScreamCount++;

            // §8.2: 밸브를 뺀 나머지 파문의 소음량을 선형 합산한다.
            // 한 파문이 여러 명에게 들려도 여기 오는 것은 **발생 1건**뿐이라
            // "중복 집계 금지(청취자 수 무관)"가 구조적으로 성립한다.
            if (CountsTowardNoise(type))
                entry.NoiseSum += NoiseOf(radiusMeters, durationSeconds);
        }

        /// <summary>이동 거리를 누적한다(음수·0은 무시).</summary>
        public void AddDistance(int playerId, float meters)
        {
            if (meters <= 0f)
                return;

            GetOrCreate(playerId).DistanceMeters += meters;
        }

        /// <summary>
        /// §8.2 "생존 요건 — **탈출 또는 미탈출**만 수상 대상. 태그당한 메아리는 제외".
        ///
        /// 한 번 태그당하면 §3.2상 라운드 종료까지 메아리로 남으므로 되돌리지 않는다.
        /// 이동거리 제외(GAP-56)만으로는 부족하다 — 태그 전까지 쌓인 거리와 낮은 소음량으로
        /// 여전히 수상 후보가 되기 때문이다.
        /// </summary>
        public void MarkTaggedOut(int playerId) => GetOrCreate(playerId).WasTaggedOut = true;

        /// <summary>§8.3 노크로 술래를 유인하는 데 성공한 횟수(<c>ServerKnockDriver</c>가 판정한다).</summary>
        public void RecordKnockLure(int playerId) => GetOrCreate(playerId).KnockLureCount++;

        /// <summary>플레이어가 집계에 등록만 되도록 한다(파문·이동이 없어도 후보로 잡히게).</summary>
        public void Track(int playerId) => GetOrCreate(playerId);

        /// <summary>
        /// §8.2 무성 점수 = Σ(소음량) ÷ 총 이동거리. **낮을수록 좋다.**
        /// 자격 미달이면 <see cref="float.PositiveInfinity"/>.
        /// </summary>
        public float SilenceScore(int playerId)
        {
            return _entries.TryGetValue(playerId, out Entry entry) ? SilenceScoreOf(entry) : float.PositiveInfinity;
        }

        /// <summary>§8 세 기준으로 수상자를 산출한다.</summary>
        public AwardResults Evaluate()
        {
            return new AwardResults(
                // §8.1 최다 비명상 — Scream 횟수 최대. 0회는 후보가 아니다.
                BestBy(static e => e.ScreamCount > 0, static e => e.ScreamCount, highestWins: true),

                // §8.2 무성 생존상 — 무성 점수 **최소**. 태그 아웃·이동 1m 미만은 제외.
                BestBy(static e => IsSilentSurvivorCandidate(e), static e => SilenceScoreOf(e), highestWins: false),

                // §8.3 최고의 거짓말상 — 유인 성공 횟수 최대.
                BestBy(static e => e.KnockLureCount > 0, static e => e.KnockLureCount, highestWins: true));
        }

        /// <summary>세션 집계를 비운다(새 세션 시작 시).</summary>
        public void Reset() => _entries.Clear();

        /// <summary>진단용 조회. 없는 플레이어는 전부 0.</summary>
        public void Peek(int playerId, out int screams, out float noiseSum, out float distance, out int knockLures)
        {
            if (!_entries.TryGetValue(playerId, out Entry entry))
            {
                screams = 0;
                noiseSum = 0f;
                distance = 0f;
                knockLures = 0;
                return;
            }

            screams = entry.ScreamCount;
            noiseSum = entry.NoiseSum;
            distance = entry.DistanceMeters;
            knockLures = entry.KnockLureCount;
        }

        private static bool IsSilentSurvivorCandidate(Entry e)
        {
            return !e.WasTaggedOut && e.DistanceMeters >= MinimumDistanceMeters;
        }

        private static float SilenceScoreOf(Entry e)
        {
            if (!IsSilentSurvivorCandidate(e))
                return float.PositiveInfinity;

            return e.NoiseSum / e.DistanceMeters;
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
        /// 자격을 갖춘 플레이어 중 점수가 가장 좋은 쪽. <paramref name="highestWins"/>가 false면
        /// **작을수록 좋다**(§8.2 무성 점수).
        ///
        /// 동점이면 **id가 작은 쪽**을 택한다. §8은 "동점자 전원 공동 수상"이라고 적었지만,
        /// 결과 전파가 수상자 id **하나**(SyncVar 3개)로 되어 있어 공동 수상을 표현할 수단이 없다.
        /// 공동 수상은 자료구조·전파·HUD를 함께 바꿔야 하는 별도 작업이라 여기서는
        /// 결정론적 단일 수상자를 유지한다(GAP-38 잔존).
        /// </summary>
        private int BestBy(System.Func<Entry, bool> qualifies, System.Func<Entry, float> score, bool highestWins)
        {
            int winner = NoWinner;
            float best = 0f;

            foreach (KeyValuePair<int, Entry> pair in _entries)
            {
                if (!qualifies(pair.Value))
                    continue;

                float value = score(pair.Value);
                bool better = winner == NoWinner || (highestWins ? value > best : value < best);

                if (better || (value == best && pair.Key < winner))
                {
                    winner = pair.Key;
                    best = value;
                }
            }

            return winner;
        }
    }
}
