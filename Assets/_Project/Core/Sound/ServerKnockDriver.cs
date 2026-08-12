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

        /// <summary>§3.2 쿨다운 30초가 남아 있다.</summary>
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
    /// **왜 노크만 위치를 클라이언트에게 받는가**: 발소리·밸브·음성은 전부 발생원이 곧
    /// 플레이어라 서버가 <c>caller.FirstObject</c>에서 위치를 얻었다(GAP-24). 노크는 §3.2가
    /// <b>"사거리 제한 없음(맵 내 임의 지점 지정)"</b>이라고 규정한 유일한 소리라, 지점을
    /// 클라이언트가 보내지 않으면 능력 자체가 성립하지 않는다. 대신 <b>역할·쿨다운·지연은
    /// 서버가 강제</b>해 "누가 얼마나 자주 쓸 수 있는가"는 여전히 클라이언트가 못 정한다.
    ///
    /// **임의 지점 지정이 악용이 아닌 이유**: §3.2가 "도망자를 도와 술래를 유인하거나,
    /// 반대로 도망자를 낚아 술래에게 정보를 흘릴 수도 있음(플레이어 재량)"이라고 명시한다 —
    /// 러너 위치에 노크하는 것도 설계된 플레이다.
    /// </summary>
    public sealed class ServerKnockDriver
    {
        /// <summary>
        /// 유인 성공으로 인정할 거리. §3.2 노크 파문 반경을 그대로 쓴다 —
        /// "술래가 그 소리를 들을 수 있는 곳까지 실제로 왔다"가 유인의 자연스러운 정의다(GAP-59).
        /// </summary>
        public const float LureRadiusMeters = KnockConfig.RadiusMeters;

        /// <summary>
        /// 유인 판정을 유지하는 시간. §3.2 쿨다운을 그대로 쓴다 — 다음 노크가 가능해지기
        /// 전까지가 한 노크의 유효 구간이다(GAP-59).
        /// </summary>
        public const float LureWindowSeconds = KnockConfig.CooldownSeconds;

        private sealed class Pending
        {
            public ulong PlayerId;
            public Vector3 Point;
            public float ActivateAt;
        }

        /// <summary>발생한 노크 하나에 대한 유인 감시.</summary>
        private sealed class LureWatch
        {
            public ulong PlayerId;
            public Vector3 Point;
            public float ExpiresAt;

            /// <summary>발생 시점에 술래가 반경 **밖**에 있었는가(유인이 성립할 수 있는 상태).</summary>
            public bool Armed;

            /// <summary>발생 시점 평가를 마쳤는가.</summary>
            public bool Evaluated;
        }

        private readonly Dictionary<ulong, float> _lastRequestAt = new Dictionary<ulong, float>();
        private readonly List<Pending> _pending = new List<Pending>();
        private readonly List<LureWatch> _watches = new List<LureWatch>();

        private readonly List<KnockActivation> _activationBuffer = new List<KnockActivation>();
        private readonly List<ulong> _lureBuffer = new List<ulong>();

        /// <summary>지연 대기 중인 노크 수(진단·테스트용).</summary>
        public int PendingCount => _pending.Count;

        /// <summary>유인 감시 중인 노크 수(진단·테스트용).</summary>
        public int WatchCount => _watches.Count;

        /// <summary>
        /// 노크 요청을 서버가 검증해 접수한다. §3.2 역할·쿨다운을 여기서 강제한다.
        /// </summary>
        /// <param name="playerId">서버가 RPC 호출자에서 얻은 플레이어 ID(클라 주장값 금지).</param>
        /// <param name="role">서버가 확인한 실제 역할(클라 주장값 금지).</param>
        public KnockRequestResult TryRequest(ulong playerId, RoleType role, Vector3 point, float now)
        {
            if (role != RoleType.Echo)
                return KnockRequestResult.NotEcho;

            if (_lastRequestAt.TryGetValue(playerId, out float last) &&
                now - last < KnockConfig.CooldownSeconds)
                return KnockRequestResult.OnCooldown;

            _lastRequestAt[playerId] = now;
            _pending.Add(new Pending
            {
                PlayerId = playerId,
                Point = point,
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

                // 발생 시점부터 유인 감시를 시작한다(§8 "노크 성공 유인 횟수").
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
        /// 술래 위치로 §8 "노크 성공 유인"을 판정한다. 유인에 성공한 **노커의 ID 목록**을 돌려주며,
        /// 호출자가 <c>AwardTally.RecordKnockLure</c>로 넘긴다.
        ///
        /// **판정 규칙(GAP-59 — §3.2에 정의가 없어 이 프로젝트가 정한 해석)**:
        /// 노크가 발생한 순간 술래가 <see cref="LureRadiusMeters"/> **밖**에 있었고,
        /// <see cref="LureWindowSeconds"/> 안에 그 반경 **안으로 들어오면** 유인 1회로 센다.
        /// 한 노크는 최대 1회만 집계된다(성공 즉시 감시 종료).
        ///
        /// 이미 그 자리에 있던 술래는 세지 않는다 — "유인"은 이동을 만들어낸 경우를 뜻하므로,
        /// 발생 시점에 이미 반경 안이면 그 노크는 감시에서 버린다.
        ///
        /// 술래가 없는 상황(전원 태그 전 등)에서는 호출하지 않으면 되고, 감시는 시간이 지나면
        /// 스스로 만료된다.
        /// </summary>
        public List<ulong> ResolveLures(Vector3 seekerPosition, float now)
        {
            _lureBuffer.Clear();

            for (int i = _watches.Count - 1; i >= 0; i--)
            {
                LureWatch w = _watches[i];

                if (now >= w.ExpiresAt)
                {
                    _watches.RemoveAt(i); // 시간 안에 오지 않았다 — 유인 실패.
                    continue;
                }

                float distance = Vector3.Distance(seekerPosition, w.Point);

                if (!w.Evaluated)
                {
                    w.Evaluated = true;
                    w.Armed = distance > LureRadiusMeters;

                    if (!w.Armed)
                        _watches.RemoveAt(i); // 이미 그 자리에 있었다 — 유인이 아니다.

                    continue;
                }

                if (w.Armed && distance <= LureRadiusMeters)
                {
                    _watches.RemoveAt(i);
                    _lureBuffer.Add(w.PlayerId);
                }
            }

            return _lureBuffer;
        }

        /// <summary>새 라운드를 위해 전부 비운다(쿨다운·대기·감시).</summary>
        public void Reset()
        {
            _lastRequestAt.Clear();
            _pending.Clear();
            _watches.Clear();
        }
    }
}
