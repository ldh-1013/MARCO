using System.Collections.Generic;
using UnityEngine;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// [10-02] 콜라이더 없이 파문 윤곽(<see cref="PulseWallRevealer"/>)에 드러나야 하는 상자 — 출구 문짝(<c>EscapeDoorVisual</c>).
    ///
    /// <para>
    /// 벽은 SoundBlocking 콜라이더를 <c>OverlapSphere</c>로 찾지만, 문에 콜라이더를 달면 이동 · 소리 차폐가 바뀐다(문은 보이기만 한다).
    /// 그래서 문은 자기 Transform(단위 큐브 = 메시 크기)을 여기 등록하고, 리빌러가 벽 다음에 이 목록을 같은 규칙(반경 · 도달 시각 ·
    /// 눈높이 차폐)으로 훑는다. 비활성화되면 빠진다.
    /// </para>
    /// </summary>
    public static class RevealOutlineRegistry
    {
        private static readonly List<Transform> Boxes = new List<Transform>();

        public static IReadOnlyList<Transform> All => Boxes;

        public static void Register(Transform box)
        {
            if (box != null && !Boxes.Contains(box))
                Boxes.Add(box);
        }

        public static void Unregister(Transform box) => Boxes.Remove(box);
    }
}
