using System.Collections.Generic;
using Marco.Core.Breath;
using Marco.Core.Role;
using UnityEngine;

namespace Marco.Core.Sound
{
    /// <summary>§3.5 외침 요청에 대한 서버 판정 결과.</summary>
    public enum ShoutRequestResult
    {
        /// <summary>접수됨 — §3.5 선딜레이 1초 뒤 발동한다.</summary>
        Accepted,

        /// <summary>술래가 아니다(§3.5 "술래 전용").</summary>
        NotSeeker,

        /// <summary>이미 선딜레이 중이다 — 연타로 창을 늘릴 수 없다.</summary>
        AlreadyWindingUp,

        /// <summary>§3.5 쿨다운 45초가 남아 있다.</summary>
        OnCooldown
    }

    /// <summary>§3.5 공포 반경 안 도망자 한 명의 반응.</summary>
    public enum ScreamOutcome
    {
        /// <summary>§3.5 "아무것도 안 함 → 비명 발생(9m / 1.0초)". 억제 실패도 여기로 온다.</summary>
        Screamed,

        /// <summary>§3.5 "숨 참기 → 숨 게이지 3초 → 비명 발생 안 함".</summary>
        SuppressedByHeldBreath,

        /// <summary>§3.5 "잠수 중 → 자동 억제(물속이라 비명 못 지름)". 추가 비용 없음.</summary>
        SuppressedByDive,

        /// <summary>§3.5 "게이지 3초 미만 → 억제 불가. 비명 강제 발생". 시도했으나 실패했다.</summary>
        SuppressionFailed
    }

    /// <summary>공포 반경 판정 대상 한 명(서버가 아는 값으로만 채운다).</summary>
    public readonly struct ShoutTarget
    {
        public readonly ulong PlayerId;
        public readonly Vector3 Position;
        public readonly RoleType Role;

        /// <summary>§5.9-1 이 플레이어의 숨 상태(<see cref="BreathConfig.ZoneOf"/>로 유도한 값).</summary>
        public readonly BreathZone Zone;

        /// <summary>서버가 소유한 이 플레이어의 숨 게이지. 억제 비용이 여기서 빠진다.</summary>
        public readonly BreathGauge Breath;

        public ShoutTarget(ulong playerId, Vector3 position, RoleType role, BreathZone zone, BreathGauge breath)
        {
            PlayerId = playerId;
            Position = position;
            Role = role;
            Zone = zone;
            Breath = breath;
        }
    }

    /// <summary>공포 반경 판정 결과 한 건.</summary>
    public readonly struct ScreamReaction
    {
        public readonly ulong PlayerId;
        public readonly Vector3 Position;
        public readonly ScreamOutcome Outcome;

        /// <summary>실제로 비명 파문이 발생하는가(§3.5 억제 성공이면 false).</summary>
        public bool EmitsScream =>
            Outcome == ScreamOutcome.Screamed || Outcome == ScreamOutcome.SuppressionFailed;

        public ScreamReaction(ulong playerId, Vector3 position, ScreamOutcome outcome)
        {
            PlayerId = playerId;
            Position = position;
            Outcome = outcome;
        }
    }

    /// <summary>선딜레이가 끝나 이번 틱에 실제로 발동하는 외침.</summary>
    public readonly struct ShoutActivation
    {
        public readonly ulong SeekerId;

        /// <summary>발동 지점. §3.5상 선딜레이 중 이동하면 취소되므로 요청 시점 위치와 같다.</summary>
        public readonly Vector3 Origin;

        public ShoutActivation(ulong seekerId, Vector3 origin)
        {
            SeekerId = seekerId;
            Origin = origin;
        }
    }

    /// <summary>
    /// §3.5 술래 외침의 서버 권위 구동기. 역할·쿨다운·선딜레이·공포 반경 판정을 전부 서버가 소유한다.
    /// FishNet도 UnityEngine 수명주기도 모른다 — EditMode 테스트 가능
    /// (<c>ServerKnockDriver</c>·<c>ServerTagDriver</c>와 같은 구조).
    ///
    /// **위치는 서버가 정한다**: 발동 지점도 대상 위치도 <c>caller.FirstObject</c>/서버 스냅샷에서
    /// 오며 클라이언트가 주장하지 않는다(GAP-24). 클라이언트가 보내는 것은
    /// "외침을 쓴다"와 "숨을 참는다" 두 의사표시뿐이다.
    ///
    /// **§3.5의 세 22m를 섞지 않는다**: 이 클래스가 쓰는 것은 <b>B(공포 반경)</b> 하나뿐이고,
    /// 차폐를 적용하지 않는 <b>순수 직선거리</b>다. A(외침 소리 22m)는 호출자가 파문으로
    /// 등록하며 거기서 차폐가 걸리고, C(비명 청취 10.8m)는 §5.7 역할 배율의 계산 결과라
    /// 이 클래스가 관여하지 않는다.
    /// </summary>
    public sealed class ServerShoutDriver
    {
        private sealed class Pending
        {
            public ulong SeekerId;
            public Vector3 Origin;
            public float RequestedAt;
            public float ActivateAt;
        }

        private readonly Dictionary<ulong, float> _cooldownUntil = new Dictionary<ulong, float>();
        private readonly List<Pending> _pending = new List<Pending>();

        /// <summary>§3.5 "숨 참기(선딜레이 1초 안에 입력)" — 플레이어별 마지막 입력 시각.</summary>
        private readonly Dictionary<ulong, float> _suppressAttemptAt = new Dictionary<ulong, float>();

        private readonly List<ShoutActivation> _activationBuffer = new List<ShoutActivation>();
        private readonly List<ScreamReaction> _reactionBuffer = new List<ScreamReaction>();

        /// <summary>선딜레이 대기 중인 외침 수(진단·테스트용).</summary>
        public int PendingCount => _pending.Count;

        /// <summary>이 술래가 선딜레이 중인가.</summary>
        public bool IsWindingUp(ulong seekerId)
        {
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].SeekerId == seekerId)
                    return true;
            }

            return false;
        }

        /// <summary>이 술래의 남은 쿨다운(초). 쓸 수 있으면 0.</summary>
        public float CooldownRemaining(ulong seekerId, float now)
        {
            if (!_cooldownUntil.TryGetValue(seekerId, out float until))
                return 0f;

            float remaining = until - now;
            return remaining > 0f ? remaining : 0f;
        }

        /// <summary>
        /// 외침 요청을 서버가 검증해 접수한다. §3.5 역할·쿨다운을 여기서 강제한다.
        /// </summary>
        /// <param name="seekerId">서버가 RPC 호출자에서 얻은 플레이어 ID(클라 주장값 금지).</param>
        /// <param name="role">서버가 확인한 실제 역할(클라 주장값 금지).</param>
        /// <param name="origin">**서버가 관측한** 술래의 현재 위치.</param>
        public ShoutRequestResult TryRequest(ulong seekerId, RoleType role, Vector3 origin, float now)
        {
            if (role != RoleType.Seeker)
                return ShoutRequestResult.NotSeeker;

            if (IsWindingUp(seekerId))
                return ShoutRequestResult.AlreadyWindingUp;

            if (CooldownRemaining(seekerId, now) > 0f)
                return ShoutRequestResult.OnCooldown;

            _pending.Add(new Pending
            {
                SeekerId = seekerId,
                Origin = origin,
                RequestedAt = now,
                ActivateAt = now + SeekerShoutConfig.WindupSeconds
            });

            return ShoutRequestResult.Accepted;
        }

        /// <summary>
        /// §3.5 "선딜레이 중 이동하면 취소, **쿨다운 소모 없음**".
        ///
        /// 쿨다운을 여기서 걸지 않는 것이 규칙의 핵심이다 — 취소는 공짜이므로 술래는
        /// 안전할 때까지 시도를 미룰 수 있다. 실제로 움직였는지 판정하는 것은 호출자다
        /// (서버가 아는 위치의 프레임 간 변화).
        /// </summary>
        /// <returns>실제로 취소된 외침이 있었으면 true.</returns>
        public bool CancelOnMove(ulong seekerId)
        {
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                if (_pending[i].SeekerId != seekerId)
                    continue;

                _pending.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>
        /// §3.5 "숨 참기 — 선딜레이 1초 안에 입력". 러너가 억제 의사를 밝힌 시각을 기록한다.
        ///
        /// **여기서 게이지를 깎지 않는다.** 실제 차감은 외침이 발동하는 순간
        /// <see cref="ResolveFear"/>에서 일어난다 — 외침이 취소되면 아무 비용도 들지 않아야 하고,
        /// 공포 반경 밖의 러너가 눌러도 소모되면 안 되기 때문이다.
        /// </summary>
        public void NotifySuppressAttempt(ulong playerId, float now)
        {
            _suppressAttemptAt[playerId] = now;
        }

        /// <summary>
        /// §3.5 선딜레이가 끝난 외침을 발동시킨다. 반환된 각 항목에 대해 호출자는
        /// ①<see cref="SoundType.Shout"/> 파문(A, 22m)을 등록하고
        /// ②<see cref="ResolveFear"/>로 공포 반경(B)을 판정해야 한다.
        ///
        /// 쿨다운은 §3.5대로 **선딜레이 완료 시점부터** 계산한다.
        /// 반환 리스트는 <b>내부 재사용 버퍼</b>다 — 다음 호출 전까지만 유효하다.
        /// </summary>
        public List<ShoutActivation> Tick(float now)
        {
            _activationBuffer.Clear();

            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                Pending p = _pending[i];
                if (now < p.ActivateAt)
                    continue;

                _pending.RemoveAt(i);
                _cooldownUntil[p.SeekerId] = now + SeekerShoutConfig.CooldownSeconds;
                _activationBuffer.Add(new ShoutActivation(p.SeekerId, p.Origin));
            }

            return _activationBuffer;
        }

        /// <summary>
        /// §3.5 <b>B — 공포 반경</b> 판정. 22m 안의 살아있는 도망자 각각에 대해
        /// 억제 여부를 결정하고, 억제 비용(§5.9-1 -3)을 여기서 차감한다.
        ///
        /// **차폐를 적용하지 않는다**(§3.5) — "B는 능력 판정이라 벽 뒤 도망자도 놀란다."
        ///
        /// 대상은 <b>살아있는 도망자뿐</b>이다. 술래 자신과 메아리(§3.2 유령)는 제외한다 —
        /// §3.5가 "22m 안의 **살아있는 도망자**"라고 못박았다.
        ///
        /// 반환 리스트는 <b>내부 재사용 버퍼</b>다.
        /// </summary>
        /// <param name="origin">외침 발동 지점(서버 값).</param>
        /// <param name="now">현재 서버 시각 — 억제 입력이 선딜레이 창 안이었는지 판정에 쓴다.</param>
        public List<ScreamReaction> ResolveFear(Vector3 origin, IReadOnlyList<ShoutTarget> targets, float now)
        {
            _reactionBuffer.Clear();

            if (targets == null)
                return _reactionBuffer;

            for (int i = 0; i < targets.Count; i++)
            {
                ShoutTarget t = targets[i];

                if (t.Role != RoleType.Runner) // §3.5 "살아있는 도망자"
                    continue;

                if (!IsInFearRadius(origin, t.Position))
                    continue;

                _reactionBuffer.Add(new ScreamReaction(t.PlayerId, t.Position, Decide(t, now)));
            }

            return _reactionBuffer;
        }

        private ScreamOutcome Decide(in ShoutTarget target, float now)
        {
            // §3.5 "잠수 중 — 자동 억제". 입력이 없어도 억제되고 추가 비용도 없다.
            if (target.Zone == BreathZone.Submerged)
                return ScreamOutcome.SuppressedByDive;

            if (!TriedToSuppress(target.PlayerId, now))
                return ScreamOutcome.Screamed; // §3.5 "아무것도 안 함 → 비명 발생"

            if (target.Breath == null)
                return ScreamOutcome.SuppressionFailed;

            SuppressionResult result = target.Breath.TrySuppressScream(target.Zone);
            switch (result)
            {
                case SuppressionResult.Suppressed:
                    return ScreamOutcome.SuppressedByHeldBreath;
                case SuppressionResult.SuppressedByDive:
                    return ScreamOutcome.SuppressedByDive;
                default:
                    return ScreamOutcome.SuppressionFailed; // §3.5 "게이지 3초 미만 → 억제 불가"
            }
        }

        /// <summary>§3.5 "선딜레이 1초 안에 입력"이 있었는가.</summary>
        private bool TriedToSuppress(ulong playerId, float now)
        {
            if (!_suppressAttemptAt.TryGetValue(playerId, out float at))
                return false;

            float elapsed = now - at;
            return elapsed >= 0f && elapsed <= SeekerShoutConfig.WindupSeconds;
        }

        /// <summary>
        /// §3.5 B — 공포 반경 안인가. <b>순수 직선거리</b>이며 차폐를 보지 않는다.
        /// 경계(정확히 22m)는 포함이다 — §5.6 청취 판정과 같은 경계 규칙.
        /// </summary>
        public static bool IsInFearRadius(Vector3 origin, Vector3 position)
        {
            return Vector3.Distance(origin, position) <= SeekerShoutConfig.FearRadiusMeters;
        }

        /// <summary>새 라운드를 위해 전부 비운다(쿨다운·선딜레이·억제 입력 기록).</summary>
        public void Reset()
        {
            _cooldownUntil.Clear();
            _pending.Clear();
            _suppressAttemptAt.Clear();
        }
    }
}
