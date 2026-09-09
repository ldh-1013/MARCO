using System.Collections.Generic;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Core.Sound
{
    /// <summary>노크 요청에 대한 서버 판정 결과.</summary>
    public enum KnockRequestResult
    {
        /// <summary>접수됨 — §3.2 지연 후 발생한다.</summary>
        Accepted,

        /// <summary>메아리가 아니다(§3.2 노크는 메아리 전용 능력).</summary>
        NotEcho,

        /// <summary>§3.2 라운드당 5회를 모두 썼다. **쿨다운과 무관하게 더 이상 불가능하다.**</summary>
        NoUsesLeft,

        /// <summary>§3.2 메아리 전환 후 20초 잠금이 아직 풀리지 않았다.</summary>
        Locked,

        /// <summary>§3.2 쿨다운 25초가 남아 있다.</summary>
        OnCooldown
    }

    /// <summary>지연이 끝나 이번 틱에 실제로 발생하는 노크.</summary>
    public readonly struct KnockActivation
    {
        /// <summary>노크를 지정한 메아리의 플레이어 ID.</summary>
        public readonly ulong PlayerId;

        /// <summary>소음이 발생할 지점(§3.2 "해당 지점에서 소음 발생").</summary>
        public readonly Vector3 Point;

        public KnockActivation(ulong playerId, Vector3 point)
        {
            PlayerId = playerId;
            Point = point;
        }
    }

    /// <summary>
    /// §3.2 메아리 노크의 서버 권위 구동기(스프린트 27). 역할·쿨다운·지연·유인 판정을
    /// 전부 서버가 소유한다. FishNet도 UnityEngine 수명주기도 모른다 — EditMode 테스트 가능
    /// (<c>ServerValveDriver</c>·<c>ServerTagDriver</c>·<c>ServerPulseDriver</c>와 같은 구조).
    ///
    /// **위치도 서버가 정한다(기획서 갱신)**: §3.2가 "발생 위치 = 메아리의 현재 위치,
    /// 지점 클릭 방식은 채택하지 않는다"로 바뀌면서, 노크도 발소리·밸브·음성과 **완전히 같은
    /// 규칙**이 됐다 — 서버가 <c>caller.FirstObject</c>에서 위치를 얻는다(GAP-24).
    /// 따라서 <b>클라이언트가 정할 수 있는 것은 "지금 쓴다"뿐이며 페이로드가 비어 있다.</b>
    ///
    /// <see cref="TryRequest"/>의 <c>echoPosition</c>은 **서버가 관측한 메아리 위치**이며
    /// 클라이언트 주장값이 아니다. Core는 FishNet을 모르므로 Net 계층이 읽어서 넘긴다.
    ///
    /// **위치는 요청 시점에 확정된다(§3.2 해석 — GAP-66)**: 1.5초 지연 뒤 메아리가 이미
    /// 이동했을 수 있는데, 그때 위치가 아니라 **요청한 자리**에서 소리가 난다. 그래야
    /// "그 자리에 가서 누르고 빠져나온다"는 §3.2의 유인 플레이가 성립한다.
    /// </summary>
    public sealed class ServerKnockDriver
    {
        /// <summary>
        /// 유인 성공으로 인정할 거리. §3.2 노크 파문 반경을 그대로 쓴다 —
        /// "술래가 그 소리를 들을 수 있는 곳까지 실제로 왔다"가 유인의 자연스러운 정의다(GAP-59).
        /// </summary>
        public const float LureRadiusMeters = KnockConfig.RadiusMeters;

        /// <summary>
        /// §8.3 "감시 시간 30초". <b>쿨다운(25초)과 다르다</b> — 이 차이가 겹침을 만든다.
        ///
        /// 노크 A를 t0에 발동하면 감시는 t0+30까지 살아 있는데, 다음 노크는 요청 기준
        /// 25초 뒤에 가능하므로 발동은 t0+25다. 즉 <b>최대 5초간 같은 메아리의 감시 2개가
        /// 공존</b>할 수 있다. §8.3 표 4·5가 이 경우를 "1회만 — 진입 시 가장 최근 성공 건 1개만
        /// 집계 / 동일 위치 활성 감시는 최신 1개로 갱신"으로 정했으므로,
        /// <see cref="Tick"/>이 새 감시를 만들 때 <b>같은 플레이어의 이전 감시를 버린다.</b>
        ///
        /// 그렇게 하지 않으면 술래가 한 번 접근하는 동안 두 감시가 각각 성공 판정을 내려
        /// 유인 1회가 2회로 세어진다.
        /// </summary>
        public const float LureWindowSeconds = 30f;

        /// <summary>§8.3 "사전 판정 창 2초" — ②의 "접근 중이 아니었음"을 확인하는 구간.</summary>
        public const float PreApproachWindowSeconds = 2f;

        /// <summary>§8.3 "샘플 간격 0.5초" — 네트워크 위치 갱신에 충분한 주기.</summary>
        public const float ApproachSampleSeconds = 0.5f;

        /// <summary>§8.3 "접근누적 하한 3.0초" — 스쳐 지나감과 실제 유인을 가르는 임계값.</summary>
        public const float ApproachRequiredSeconds = 3f;

        private sealed class Pending
        {
            public ulong PlayerId;
            public Vector3 Point;
            public float ActivateAt;
        }

        /// <summary>발생한 노크 하나에 대한 §8.3 유인 감시.</summary>
        private sealed class LureWatch
        {
            public ulong PlayerId;
            public Vector3 Point;
            public float ExpiresAt;

            /// <summary>§8.3 ①② 개시 조건을 통과했는가(통과하지 못하면 즉시 폐기된다).</summary>
            public bool Armed;

            /// <summary>개시 조건 평가를 마쳤는가.</summary>
            public bool Evaluated;

            /// <summary>§8.3 ③ 접근누적(초). 0.5초 샘플에서 거리가 줄었을 때만 0.5씩 오른다.</summary>
            public float ApproachSeconds;

            /// <summary>직전 샘플의 거리·시각(§8.3 "직전 샘플보다 거리가 줄었을 때만").</summary>
            public float LastSampleDistance;
            public float LastSampleAt;
        }

        /// <summary>§8.3 ②를 판정하기 위한 술래 위치 이력 한 점.</summary>
        private readonly struct SeekerSample
        {
            public readonly float Time;
            public readonly Vector3 Position;

            public SeekerSample(float time, Vector3 position)
            {
                Time = time;
                Position = position;
            }
        }

        private readonly Dictionary<ulong, float> _lastRequestAt = new Dictionary<ulong, float>();

        /// <summary>§3.2 "라운드당 정확히 5회" — 이번 라운드에 실제로 접수된 요청 수.</summary>
        private readonly Dictionary<ulong, int> _usesThisRound = new Dictionary<ulong, int>();

        /// <summary>§3.2 "메아리 전환 후 20초" — 서버가 이 플레이어를 메아리로 처음 관측한 시각.</summary>
        private readonly Dictionary<ulong, float> _becameEchoAt = new Dictionary<ulong, float>();

        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<LureWatch> _watches = new List<LureWatch>();

        private readonly List<KnockActivation> _activationBuffer = new List<KnockActivation>();
        private readonly List<ulong> _lureBuffer = new List<ulong>();

        /// <summary>
        /// §8.3 ②용 술래 위치 이력. <see cref="PreApproachWindowSeconds"/>보다 오래된 것은
        /// 한 점만 남기고 버린다 — 2초 전 거리를 알면 되므로 무한히 쌓을 이유가 없다.
        /// </summary>
        private readonly List<SeekerSample> _seekerHistory = new List<SeekerSample>();

        private float _lastHistorySampleAt = float.NegativeInfinity;

        /// <summary>지연 대기 중인 노크 수(진단·테스트용).</summary>
        public int PendingCount => _pending.Count;

        /// <summary>유인 감시 중인 노크 수(진단·테스트용).</summary>
        public int WatchCount => _watches.Count;

        /// <summary>
        /// 이 플레이어가 메아리 상태임을 서버가 관측했다고 알린다. **최초 관측 시각만 기록**하며
        /// 이후 호출은 무시된다 — §3.2 "메아리 전환 후 20초간 사용 불가"의 기준점이다.
        ///
        /// 매 틱 호출해도 안전하도록 멱등하게 만들었다. 러너였다가 태그당해 메아리가 된 순간부터
        /// 20초를 세며, <see cref="Reset"/> 이후 다시 처음부터 센다.
        /// </summary>
        public void ObserveEcho(ulong playerId, float now)
        {
            if (!_becameEchoAt.ContainsKey(playerId))
                _becameEchoAt[playerId] = now;
        }

        /// <summary>이 플레이어가 이번 라운드에 남긴 노크 횟수(§3.2 5회 중 몇 회).</summary>
        public int UsesRemaining(ulong playerId)
        {
            int used = _usesThisRound.TryGetValue(playerId, out int u) ? u : 0;
            int left = KnockConfig.MaxUsesPerRound - used;
            return left > 0 ? left : 0;
        }

        /// <summary>
        /// §3.2 "메아리 전환 후 20초" 잠금의 남은 시간(초). 풀렸으면 0.
        /// 아직 메아리로 관측된 적이 없으면 잠금 전체가 남은 것으로 본다 — 관측 없이
        /// 요청이 통과하는 일이 없어야 한다.
        /// </summary>
        public float LockoutRemaining(ulong playerId, float now)
        {
            if (!_becameEchoAt.TryGetValue(playerId, out float since))
                return KnockConfig.EchoLockoutSeconds;

            float remaining = KnockConfig.EchoLockoutSeconds - (now - since);
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>
        /// 노크 요청을 서버가 검증해 접수한다. §3.2 역할·횟수·잠금·쿨다운을 여기서 전부 강제한다.
        ///
        /// **판정 순서는 NotEcho → NoUsesLeft → Locked → OnCooldown**이다. 더 근본적인 거부
        /// 사유를 먼저 알려주기 위함이다 — 5회를 다 쓴 사람에게 "쿨다운 12초"라고 표시하면
        /// 기다리면 다시 쓸 수 있다는 잘못된 기대를 준다.
        /// </summary>
        /// <param name="playerId">서버가 RPC 호출자에서 얻은 플레이어 ID(클라 주장값 금지).</param>
        /// <param name="role">서버가 확인한 실제 역할(클라 주장값 금지).</param>
        /// <param name="echoPosition">
        /// **서버가 관측한** 메아리의 현재 위치(§3.2 "발생 위치 = 메아리의 현재 위치").
        /// 클라이언트가 보낸 값이 아니다 — Net 계층이 <c>caller.FirstObject</c>에서 읽어 넘긴다.
        /// </param>
        public KnockRequestResult TryRequest(ulong playerId, RoleType role, Vector3 echoPosition, float now)
        {
            if (role != RoleType.Echo)
                return KnockRequestResult.NotEcho;

            // 역할이 메아리로 확인된 이상, 관측 기록이 없다면 지금이 최초 관측이다.
            // (Net 계층이 매 틱 ObserveEcho를 부르지만, 그 틱보다 요청이 먼저 올 수 있다.)
            ObserveEcho(playerId, now);

            int used = _usesThisRound.TryGetValue(playerId, out int u) ? u : 0;
            if (used >= KnockConfig.MaxUsesPerRound)
                return KnockRequestResult.NoUsesLeft;

            if (LockoutRemaining(playerId, now) > 0f)
                return KnockRequestResult.Locked;

            if (_lastRequestAt.TryGetValue(playerId, out float last) &&
                now - last < KnockConfig.CooldownSeconds)
                return KnockRequestResult.OnCooldown;

            _lastRequestAt[playerId] = now;
            _usesThisRound[playerId] = used + 1;
            _pending.Add(new Pending
            {
                PlayerId = playerId,
                // §3.2 해석(GAP-66): 발생 지점은 **요청 시점**의 위치로 고정한다.
                Point = echoPosition,
                ActivateAt = now + KnockConfig.ActivationDelaySeconds
            });

            return KnockRequestResult.Accepted;
        }

        /// <summary>이 플레이어의 남은 쿨다운(초). 쓸 수 있으면 0.</summary>
        public float CooldownRemaining(ulong playerId, float now)
        {
            if (!_lastRequestAt.TryGetValue(playerId, out float last))
                return 0f;

            float remaining = KnockConfig.CooldownSeconds - (now - last);
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>
        /// §3.2 "1.5초 지연" 이 끝난 노크를 발생시킨다. 반환된 각 항목은 호출자가
        /// <c>ServerPulseDriver.AddPulse(..., SoundType.Knock, point, now)</c>로 파문화해야 한다.
        ///
        /// 반환 리스트는 **내부 재사용 버퍼**다 — 다음 호출 전까지만 유효하다.
        /// </summary>
        public List<KnockActivation> Tick(float now)
        {
            _activationBuffer.Clear();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (now < p.ActivateAt)
                    continue;

                _pending.RemoveAt(i);
                _activationBuffer.Add(new KnockActivation(p.PlayerId, p.Point));

                // §8.3 표 4·5 "1회만 — 최신 1개로 갱신": 감시 30초 > 쿨다운 25초라 같은
                // 메아리의 이전 감시가 아직 살아 있을 수 있다. 새 노크가 나가는 순간 그것을
                // 버려야 술래의 한 번의 접근이 두 번으로 세어지지 않는다.
                DropWatchesOf(p.PlayerId);

                _watches.Add(new LureWatch
                {
                    PlayerId = p.PlayerId,
                    Point = p.Point,
                    ExpiresAt = now + LureWindowSeconds
                });
            }

            return _activationBuffer;
        }

        /// <summary>
        /// §8.3 최고의 거짓말상 — 노크 유인의 인과관계 판정. 유인에 성공한 **노커의 ID 목록**을
        /// 돌려주며, 호출자가 <c>AwardTally.RecordKnockLure</c>로 넘긴다.
        ///
        /// **§8.3 원문(4조건)**:
        /// <code>
        /// d(t) = 술래와 Knock 위치 사이의 거리
        ///
        /// [감시 개시]  Knock 발생 시점 t0에서
        ///   ① d(t0) > 9m                  ← 아니면 감시를 시작하지 않는다
        ///   ② d(t0) >= d(t0 - 2초)         ← 직전 2초간 접근 중이 아니었을 것
        ///
        /// [감시]  t0부터 30초간 0.5초 간격 샘플링
        ///   ③ 접근누적 += 0.5초  (직전 샘플보다 거리가 줄었을 때만)
        ///   ④ d <= 9m 도달 시점에 접근누적 >= 3.0초  →  유인 성공
        ///
        /// [감시 종료 — 전부 실패]  30초 경과 / 술래가 도망자를 태그 / 라운드 종료
        /// </code>
        ///
        /// **거리와 시간만 쓴다** — 이동 벡터·시야·경로 분석을 쓰지 않는다(§8.3 명시).
        ///
        /// **매 프레임 호출해도 된다.** 0.5초 샘플링은 이 메서드 안에서 시각으로 관리한다.
        /// 술래가 없는 상황에서는 호출하지 않으면 되고, 감시는 시간이 지나면 스스로 만료된다.
        /// </summary>
        public List<ulong> ResolveLures(Vector3 seekerPosition, float now)
        {
            _lureBuffer.Clear();

            RecordSeekerHistory(seekerPosition, now);

            for (int i = _watches.Count - 1; i >= 0; i--)
            {
                LureWatch w = _watches[i];

                if (now >= w.ExpiresAt)
                {
                    _watches.RemoveAt(i); // §8.3 30초 경과 — 실패
                    continue;
                }

                float distance = Vector3.Distance(seekerPosition, w.Point);

                if (!w.Evaluated)
                {
                    EvaluateStartConditions(w, distance, now);

                    if (!w.Armed)
                        _watches.RemoveAt(i); // ① 또는 ② 불만족 — 감시를 시작하지 않는다

                    continue;
                }

                // ③ 0.5초 간격 샘플링. 거리가 줄어든 샘플에서만 누적한다.
                if (now - w.LastSampleAt >= ApproachSampleSeconds)
                {
                    if (distance < w.LastSampleDistance)
                        w.ApproachSeconds += ApproachSampleSeconds;

                    w.LastSampleDistance = distance;
                    w.LastSampleAt = now;
                }

                // ④ 반경 안 도달 + 접근누적 하한 충족 → 유인 성공.
                //
                // 반경 안인데 누적이 모자라면 감시를 **끝내지 않는다** — §8.3의 종료 조건은
                // 30초·태그·라운드 종료 셋뿐이다. 술래가 스쳐 지나갔다가 다시 다가오면
                // 그때 성립할 수 있어야 한다(표 3 "반대로 갔다가 복귀 → 성공").
                if (distance <= LureRadiusMeters && w.ApproachSeconds >= ApproachRequiredSeconds)
                {
                    _watches.RemoveAt(i);
                    _lureBuffer.Add(w.PlayerId);
                }
            }

            return _lureBuffer;
        }

        /// <summary>
        /// §8.3 ①② 감시 개시 조건.
        ///
        /// ②의 <c>d(t0 - 2초)</c>는 술래 위치 이력에서 얻는다. <b>이력이 2초에 못 미치면
        /// 가장 오래된 샘플로 대신한다</b>(라운드 시작 직후 등). 그마저 없으면 현재 거리를
        /// 써서 <c>d(t0) >= d(t0)</c>가 성립하므로 ②를 통과시킨다 — 확인할 수 없다는 이유로
        /// 정당한 노크를 탈락시키지 않는 쪽을 택했다(§8.3이 정하지 않은 부분).
        /// </summary>
        private void EvaluateStartConditions(LureWatch w, float distance, float now)
        {
            w.Evaluated = true;
            w.LastSampleDistance = distance;
            w.LastSampleAt = now;
            w.ApproachSeconds = 0f;

            // ① 발생 시점에 술래가 반경 밖이어야 한다 — 이미 그 자리에 있었다면 유인이 아니다.
            if (distance <= LureRadiusMeters)
            {
                w.Armed = false;
                return;
            }

            // ② 직전 2초간 접근 중이 아니었을 것.
            float past = DistanceAt(now - PreApproachWindowSeconds, w.Point, distance);
            w.Armed = distance >= past;
        }

        /// <summary>§8.3 ② 판정용 — 지정 시각 이하의 가장 최근 이력에서 잰 거리.</summary>
        private float DistanceAt(float time, Vector3 point, float fallback)
        {
            if (_seekerHistory.Count == 0)
                return fallback;

            // 이력은 시간 오름차순이다. time 이하의 마지막 샘플을 찾는다.
            SeekerSample chosen = _seekerHistory[0];
            for (int i = 0; i < _seekerHistory.Count; i++)
            {
                if (_seekerHistory[i].Time > time)
                    break;

                chosen = _seekerHistory[i];
            }

            return Vector3.Distance(chosen.Position, point);
        }

        /// <summary>
        /// §8.3 ②용 술래 위치 이력을 <see cref="ApproachSampleSeconds"/> 간격으로 적는다.
        /// 2초 창을 덮을 만큼만 남기고 버린다(가장 오래된 한 점은 경계 판정을 위해 남긴다).
        /// </summary>
        private void RecordSeekerHistory(Vector3 seekerPosition, float now)
        {
            if (now - _lastHistorySampleAt < ApproachSampleSeconds)
                return;

            _lastHistorySampleAt = now;
            _seekerHistory.Add(new SeekerSample(now, seekerPosition));

            float cutoff = now - PreApproachWindowSeconds;
            while (_seekerHistory.Count > 1 && _seekerHistory[1].Time <= cutoff)
                _seekerHistory.RemoveAt(0);
        }

        /// <summary>
        /// §8.3 감시 종료 조건 "**술래가 도망자를 태그**" — 그 순간 모든 감시를 실패 처리한다.
        ///
        /// 태그가 나면 술래의 이동 동기가 노크가 아니라 추격이었다고 보는 것이 자연스럽고,
        /// 태그 직후의 이동을 유인으로 세면 "잡고 나서 우연히 노크 지점을 지났다"가
        /// 거짓말상이 되어버린다.
        /// </summary>
        public void NotifySeekerTagged()
        {
            _watches.Clear();
        }

        /// <summary>§8.3 표 5 "동일 위치 활성 감시는 최신 1개로 갱신" — 이 플레이어의 감시를 버린다.</summary>
        private void DropWatchesOf(ulong playerId)
        {
            for (int i = _watches.Count - 1; i >= 0; i--)
            {
                if (_watches[i].PlayerId == playerId)
                    _watches.RemoveAt(i);
            }
        }

        /// <summary>
        /// 새 라운드를 위해 전부 비운다(쿨다운·라운드 사용 횟수·전환 잠금·대기·감시).
        ///
        /// §3.2의 "라운드당 5회"는 **라운드 경계에서만** 회복된다 — 라운드 중 재충전은 없다.
        /// 전환 잠금 기록도 함께 비워, 새 라운드에서 다시 메아리가 되면 20초를 새로 센다.
        /// </summary>
        public void Reset()
        {
            _lastRequestAt.Clear();
            _usesThisRound.Clear();
            _becameEchoAt.Clear();
            _pending.Clear();
            _watches.Clear();          // §8.3 "라운드 종료" 감시 종료 조건
            _seekerHistory.Clear();
            _lastHistorySampleAt = float.NegativeInfinity;
        }
    }
}
