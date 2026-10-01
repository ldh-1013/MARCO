using UnityEngine;

namespace Marco.Core.Objectives
{
    /// <summary>
    /// [10-02] 출구 문의 모양 · 발광 규칙 — 문 생성기(에디터)와 런타임 표시(<c>EscapeDoorVisual</c>)가 같이 쓴다.
    ///
    /// <para>
    /// <b>닫힘</b>: 발광 없음 — 어둠 속에서는 안 보이고, 내 목소리 빛(Lit 표면)이나 파문 윤곽(<c>PulseWallRevealer</c>)이 닿을 때만
    /// 보인다(벽과 같은 규칙). <b>열림</b>: 약한 초록 자체 발광 — 게이트가 열렸다는 소식(§6.1)을 문에서도 보게 한다.
    /// 다른 구역을 비출 만큼 밝지 않다(빛을 내는 광원이 아니라 표면 색).
    /// </para>
    /// </summary>
    public static class EscapeDoorLook
    {
        /// <summary>문 폭(m) — 문짝 기준. 문틀은 이 바깥에 붙는다.</summary>
        public const float WidthMeters = 2f;

        /// <summary>문 높이(m) — 문짝 기준. 벽 높이(3.5m)보다 낮다.</summary>
        public const float HeightMeters = 2.5f;

        /// <summary>게이트가 열린 동안 문짝의 발광 색(약한 초록 — 밸브 열림 색 계열, 어둡게).</summary>
        public static readonly Color OpenEmission = new Color(0.05f, 0.38f, 0.14f, 1f);

        /// <summary>문짝 발광 색 — 게이트 상태(서버 확정값)만 본다. 닫히면 검정(발광 없음).</summary>
        public static Color PanelEmission(bool gateOpen) => gateOpen ? OpenEmission : Color.black;
    }
}
