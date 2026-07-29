using System;
using UnityEngine;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// 스폰 앵커 하나를 **여러 플레이어가 겹치지 않는 시작 지점들**로 펼치는 순수 계산
    /// (스프린트 18b 후속 — 실기 버그 수정).
    ///
    /// **왜 필요한가**: <see cref="SpawnAnchorRegistry"/>는 §10.1 "입구 로비" 한 지점만 제공하고,
    /// <c>PawnPhaseTeleporter</c>가 모든 플레이어를 그 **동일 좌표**로 옮겼다. 그 결과 라운드 시작
    /// 순간 술래와 러너가 같은 자리에 서게 되어 <see cref="Marco.Core.Tagging.TagRules.TagRadiusMeters"/>
    /// 안에 들어가고, 시작 즉시 태그가 성립해 라운드가 끝나 버렸다.
    ///
    /// 앵커를 중심으로 한 원 위에 슬롯을 균등 배치하고, 각 플레이어가 자기 슬롯을 쓴다. 서버가
    /// 좌표를 나눠 줄 필요가 없다 — 같은 인덱스에서 항상 같은 결과가 나오는 **결정론적** 계산이라
    /// 각 클라이언트가 스스로 계산해도 서로 겹치지 않는다.
    ///
    /// **판정 로직은 건드리지 않는다** — 태그 규칙은 그대로 두고 시작 배치만 떨어뜨린다.
    /// </summary>
    public static class SpawnRing
    {
        /// <summary>원 위에 둘 슬롯 수. 인덱스가 이 값을 넘으면 되돌아온다.</summary>
        public const int DefaultSlots = 8;

        /// <summary>
        /// 앵커에서 각 슬롯까지의 거리(m). 인접 슬롯 간 간격은
        /// <c>2 · r · sin(π / slots)</c> = 기본값에서 약 3.06m로, 태그 반경 1.2m보다 충분히 크다.
        /// </summary>
        public const float DefaultRadiusMeters = 4f;

        /// <summary>
        /// <paramref name="index"/> 슬롯의 시작 지점을 계산한다. 높이(Y)와 회전은 앵커 값을
        /// 그대로 유지한다 — 바닥 높이와 바라보는 방향은 맵이 정한 대로 두는 것이 맞다.
        /// </summary>
        /// <param name="anchor">맵이 등록한 기준 지점(§10.1 입구 로비).</param>
        /// <param name="index">플레이어 식별 인덱스. 음수·초과값도 안전하게 되돌아온다.</param>
        /// <param name="radiusMeters">0 이하면 앵커를 그대로 반환한다(분산 없음).</param>
        /// <param name="slots">1 이하면 앵커를 그대로 반환한다.</param>
        public static SpawnPose GetPose(SpawnPose anchor, int index,
                                        float radiusMeters = DefaultRadiusMeters,
                                        int slots = DefaultSlots)
        {
            if (slots <= 1 || radiusMeters <= 0f)
                return anchor;

            // 음수 인덱스에서도 0..slots-1로 정규화한다(% 는 음수를 그대로 남긴다).
            int slot = ((index % slots) + slots) % slots;

            double angle = slot * (2.0 * Math.PI / slots);
            float offsetX = (float)Math.Cos(angle) * radiusMeters;
            float offsetZ = (float)Math.Sin(angle) * radiusMeters;

            var position = new Vector3(
                anchor.Position.x + offsetX,
                anchor.Position.y,
                anchor.Position.z + offsetZ);

            return new SpawnPose(position, anchor.Rotation);
        }

        /// <summary>
        /// 인접 슬롯 사이의 거리(m). 태그 반경보다 큰지 확인하는 용도이며, 반경·슬롯 수를
        /// 조정할 때 안전 여부를 코드로 검증할 수 있게 공개한다.
        /// </summary>
        public static float AdjacentSpacing(float radiusMeters = DefaultRadiusMeters,
                                           int slots = DefaultSlots)
        {
            if (slots <= 1 || radiusMeters <= 0f)
                return 0f;

            return (float)(2.0 * radiusMeters * Math.Sin(Math.PI / slots));
        }
    }
}
