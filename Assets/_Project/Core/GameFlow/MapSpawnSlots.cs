using System;
using UnityEngine;

namespace Marco.Core.GameFlow
{
    /// <summary>
    /// 맵(Game 씬) 도망자 스폰 슬롯 — 맵 스폰 앵커(<see cref="SpawnAnchorRegistry"/>)를 슬롯 수만큼 펼친다.
    /// <c>PawnPhaseTeleporter</c>와 씬 테스트가 <b>같은 함수</b>를 부른다(테스트 전용 사본을 두지 않는다).
    ///
    /// <para>
    /// <b>10-01 — 링에서 격자(4열 × 2행)로.</b> 예전에는 로비 중심 (7, 36)을 앵커로 반지름 3m 링(<see cref="SpawnRing"/>)을
    /// 썼는데, 로비는 남북 8m(z 32~40)이고 정문 출구가 북벽 가운데 (7, 40)이라 북쪽 슬롯이 출구에 붙었다 — 슬롯 2 (7, 39)는
    /// 출구 판정 반경(2m) 안 1.35m라 게이트가 열리면 걷지 않고 탈출했고, 슬롯 1 · 3도 2.97m였다. 8슬롯 링은 반지름을 줄여도
    /// 로비 남쪽 절반(정문에서 5m 밖)에 들어가지 않아(지름이 남은 폭 2.5m를 넘거나 슬롯끼리 겹친다), 앵커를 로비 남쪽 절반
    /// 중심으로 옮기고 동서로 긴 격자로 펼친다. 슬롯 번호(PlayerId % 슬롯 수)와 개수는 그대로다.
    /// </para>
    ///
    /// <para>
    /// 배치(앵커 기준, 월드 축 — 링과 같다): 0~3번 = 북쪽 줄(앵커 +z <see cref="RowSpacingMeters"/>/2), 4~7번 = 남쪽 줄,
    /// 각 줄은 서 → 동으로 <see cref="ColumnSpacingMeters"/> 간격. 높이 · 회전은 앵커 값을 그대로 쓴다.
    /// 출구 거리 · 바닥 · 벽 · 겹침은 <c>SpawnPointSceneTests</c>가 실제 Game 씬으로 확인한다.
    /// </para>
    /// </summary>
    public static class MapSpawnSlots
    {
        /// <summary>한 줄의 슬롯 수.</summary>
        public const int Columns = 4;

        /// <summary>같은 줄 슬롯 간격(m). 로비 동서 폭 10m(x 2~12) 안에서 벽 여유를 남긴다.</summary>
        public const float ColumnSpacingMeters = 2.1f;

        /// <summary>줄 간격(m). 캡슐 지름(0.7m) · 태그 반경(1.2m)보다 넓다.</summary>
        public const float RowSpacingMeters = 1.5f;

        /// <summary><paramref name="index"/>번 슬롯의 시작 지점(앵커 높이 · 회전 유지). 음수 · 초과 인덱스는 되돌아온다.</summary>
        public static SpawnPose GetPose(SpawnPose anchor, int index, int slots)
        {
            if (slots <= 1)
                return anchor;

            int slot = ((index % slots) + slots) % slots;
            int columns = Math.Min(Columns, slots);
            int rows = (slots + columns - 1) / columns;
            int row = slot / columns;
            int column = slot % columns;

            float x = (column - (columns - 1) * 0.5f) * ColumnSpacingMeters;
            float z = ((rows - 1) * 0.5f - row) * RowSpacingMeters;

            return new SpawnPose(anchor.Position + new Vector3(x, 0f, z), anchor.Rotation);
        }

        /// <summary>가장 가까운 두 슬롯 사이 거리(m) — 진단 로그용.</summary>
        public static float MinSpacing(int slots)
        {
            if (slots <= 1)
                return 0f;

            return slots <= Columns ? ColumnSpacingMeters : Math.Min(ColumnSpacingMeters, RowSpacingMeters);
        }
    }
}
