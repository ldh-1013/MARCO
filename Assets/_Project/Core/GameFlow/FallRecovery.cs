using UnityEngine;

namespace Marco.Core.GameFlow
{
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

        private readonly float _killPlaneY;
        private readonly float _sampleIntervalSeconds;

        private float _sinceLastSample;
        private bool _hasSafePoint;
        private Vector3 _safePoint;

        public FallRecovery(float killPlaneY = DefaultKillPlaneY,
                            float sampleIntervalSeconds = DefaultSampleIntervalSeconds)
        {
            _killPlaneY = killPlaneY;
            // 0 이하면 매 호출 기록이 되어 의미가 없다 — 최소값을 강제한다.
            _sampleIntervalSeconds = sampleIntervalSeconds > 0f ? sampleIntervalSeconds : DefaultSampleIntervalSeconds;
        }

        public float KillPlaneY => _killPlaneY;

        /// <summary>되돌릴 안전 지점이 기록돼 있는가.</summary>
        public bool HasSafePoint => _hasSafePoint;

        /// <summary>마지막으로 접지 상태였던 위치. <see cref="HasSafePoint"/>가 false면 의미 없음.</summary>
        public Vector3 SafePoint => _safePoint;

        /// <summary>
        /// 매 프레임 호출한다. **접지 상태일 때만** 안전 지점을 갱신하며, 첫 접지는 즉시 기록하고
        /// 이후에는 <see cref="DefaultSampleIntervalSeconds"/> 주기로 갱신한다.
        /// </summary>
        public void Sample(float deltaTime, Vector3 position, bool isGrounded)
        {
            if (deltaTime > 0f)
                _sinceLastSample += deltaTime;

            if (!isGrounded)
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

        /// <summary>맵 전환·새 라운드에서 이전 맵의 좌표를 물고 있지 않도록 비운다.</summary>
        public void Reset()
        {
            _hasSafePoint = false;
            _safePoint = default;
            _sinceLastSample = 0f;
        }
    }
}
