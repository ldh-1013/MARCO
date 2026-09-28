using UnityEngine;

namespace Marco.Core.GameFlow
{
    /// <summary><see cref="FallRecovery.Recover"/>가 어디로 되돌렸는가 — 로그 · 진단용.</summary>
    public enum FallRecoveryKind
    {
        /// <summary>마지막 안전 지점(첫 낙하).</summary>
        SafePoint,

        /// <summary>직전 복구 뒤 <see cref="FallRecovery.DefaultRepeatWindowSeconds"/> 안에 또 떨어짐 → 배정 스폰(루프 차단).</summary>
        AssignedSpawnRepeatedFall,

        /// <summary>기록된 안전 지점이 없음(스폰 직후 · 맵 전환 직후) → 배정 스폰.</summary>
        AssignedSpawnNoSafePoint
    }

    /// <summary>
    /// 맵 밖으로 떨어진 플레이어를 되돌리기 위한 **순수 판정**(스프린트 20).
    ///
    /// **왜 필요한가**: 스프린트 18b 실기에서 낙하 한계·리스폰 로직이 전혀 없다는 것이
    /// 실제 증상으로 드러났다 — 한 번 떨어지면 돌아올 방법이 없고, 상대 화면에서는 그 플레이어가
    /// 사라져 "접속되지 않은 것처럼" 보인다. 기획서에는 낙하·리스폰 스펙이 없다(GAP-32).
    ///
    /// **책임 분리**: 이 클래스는 Unity에 의존하지 않는 판정만 한다 — 언제 안전 지점을 기록할지,
    /// 떨어졌는지, 어디로 되돌릴지. 실제 이동(<c>CharacterController</c> 조작)은 Presentation이 한다.
    /// 그래야 이 규칙을 Unity 없이 테스트할 수 있다(§15.2 Core 원칙).
    ///
    /// **안전 지점 정의**: "접지 상태로 서 있던 마지막 위치". 공중에 있는 동안에는 기록하지 않으므로,
    /// 낭떠러지에서 뛰어내리는 도중의 좌표가 기록돼 다시 떨어지는 자리로 복구되는 일이 없다.
    /// <b>[09-29] 발밑 레이가 바닥을 맞힐 때만</b> 기록한다 — 캐릭터 컨트롤러는 벽 모서리 · 틈 가장자리에 걸친 순간에도
    /// 접지로 보고할 수 있어, 그 자리가 안전 지점이 되면 복구 → 다시 낙하 루프가 생긴다.
    ///
    /// **[09-29] 반복 낙하 루프 차단**: 첫 복구는 안전 지점, 직전 복구 뒤 <see cref="DefaultRepeatWindowSeconds"/> 안에 또
    /// 떨어지면 배정 스폰 지점으로 보낸다(안전 지점 자체가 틀린 경우의 탈출구). 매번 스폰으로 보내지 않는 이유 —
    /// 러너가 일부러 떨어져 스폰으로 순간이동하는 악용을 막는다. 창이 지나면 다시 안전 지점부터.
    /// </summary>
    public sealed class FallRecovery
    {
        /// <summary>
        /// 이 높이 아래로 내려가면 떨어진 것으로 본다. 맵(§10.1 실내수영장)의 바닥은 y=0 부근이고
        /// 물은 그 바로 위 얕은 층이라, 잠수(§4.2 Diving)와 혼동되지 않을 만큼 충분히 아래다.
        /// </summary>
        public const float DefaultKillPlaneY = -10f;

        /// <summary>안전 지점 기록 주기(초). 너무 잦으면 낭비, 너무 드물면 복구 위치가 어색해진다.</summary>
        public const float DefaultSampleIntervalSeconds = 0.5f;

        /// <summary>
        /// 직전 복구 뒤 이 시간(초) <b>안에</b> 또 떨어지면 안전 지점 대신 배정 스폰으로 보낸다(09-29 결정 — 루프 차단).
        /// 경계값(정확히 10초)은 창 밖이다.
        /// </summary>
        public const float DefaultRepeatWindowSeconds = 10f;

        private readonly float _killPlaneY;
        private readonly float _sampleIntervalSeconds;

        private readonly float _repeatWindowSeconds;

        private float _sinceLastSample;
        private bool _hasSafePoint;
        private Vector3 _safePoint;
        private bool _hasRecovered;
        private float _lastRecoveryTime;

        public FallRecovery(float killPlaneY = DefaultKillPlaneY,
                            float sampleIntervalSeconds = DefaultSampleIntervalSeconds,
                            float repeatWindowSeconds = DefaultRepeatWindowSeconds)
        {
            _killPlaneY = killPlaneY;
            _repeatWindowSeconds = repeatWindowSeconds;
            // 0 이하면 매 호출 기록이 되어 의미가 없다 — 최소값을 강제한다.
            _sampleIntervalSeconds = sampleIntervalSeconds > 0f ? sampleIntervalSeconds : DefaultSampleIntervalSeconds;
        }

        public float KillPlaneY => _killPlaneY;

        /// <summary>되돌릴 안전 지점이 기록돼 있는가.</summary>
        public bool HasSafePoint => _hasSafePoint;

        /// <summary>마지막으로 접지 상태였던 위치. <see cref="HasSafePoint"/>가 false면 의미 없음.</summary>
        public Vector3 SafePoint => _safePoint;

        /// <summary>
        /// 매 프레임 호출한다. <b>접지 상태이고 발밑 레이가 바닥을 맞혔을 때만</b>(<paramref name="hasFloorBelow"/>)
        /// 안전 지점을 갱신하며, 첫 기록은 즉시, 이후에는 <see cref="DefaultSampleIntervalSeconds"/> 주기로 갱신한다.
        /// </summary>
        public void Sample(float deltaTime, Vector3 position, bool isGrounded, bool hasFloorBelow)
        {
            if (deltaTime > 0f)
                _sinceLastSample += deltaTime;

            if (!isGrounded || !hasFloorBelow)
                return;

            // 킬 플레인 아래에서 접지했다면(맵 밖 지오메트리 위 등) 그 자리는 안전하지 않다.
            if (position.y < _killPlaneY)
                return;

            if (_hasSafePoint && _sinceLastSample < _sampleIntervalSeconds)
                return;

            _hasSafePoint = true;
            _safePoint = position;
            _sinceLastSample = 0f;
        }

        /// <summary>킬 플레인 아래로 떨어졌는가.</summary>
        public bool HasFallen(Vector3 position) => position.y < _killPlaneY;

        /// <summary>
        /// 복구 지점. 기록된 안전 지점이 없으면(스폰 직후 떨어진 경우 등)
        /// <paramref name="fallback"/>을 쓴다 — 보통 맵 스폰 지점이다.
        /// </summary>
        public Vector3 GetRecoveryPoint(Vector3 fallback) => _hasSafePoint ? _safePoint : fallback;

        /// <summary>
        /// 떨어졌을 때 어디로 되돌릴지 정하고 그 복구를 기록한다(반복 판정의 기준 시각).
        /// 안전 지점이 없으면 · 직전 복구 뒤 창 안에 또 떨어졌으면 <paramref name="assignedSpawn"/>, 아니면 안전 지점.
        /// </summary>
        public Vector3 Recover(float now, Vector3 assignedSpawn, out FallRecoveryKind kind)
        {
            bool repeated = _hasRecovered && now - _lastRecoveryTime < _repeatWindowSeconds;

            if (!_hasSafePoint)
                kind = FallRecoveryKind.AssignedSpawnNoSafePoint;
            else if (repeated)
                kind = FallRecoveryKind.AssignedSpawnRepeatedFall;
            else
                kind = FallRecoveryKind.SafePoint;

            _hasRecovered = true;
            _lastRecoveryTime = now;
            return kind == FallRecoveryKind.SafePoint ? _safePoint : assignedSpawn;
        }

        /// <summary>
        /// 맵 전환·새 라운드에서 이전 맵의 좌표를 물고 있지 않도록 비운다(반복 낙하 기록도).
        /// <b>맵이 내려가거나 올라오면 반드시 부른다</b> — 사라진 맵의 안전 지점으로 되돌리면 그 자리에 바닥이 없어
        /// 복구 → 낙하가 다음 맵 로드까지 반복된다(09-28 기계실 루프의 원인).
        /// </summary>
        public void Reset()
        {
            _hasSafePoint = false;
            _safePoint = default;
            _sinceLastSample = 0f;
            _hasRecovered = false;
            _lastRecoveryTime = 0f;
        }
    }
}
