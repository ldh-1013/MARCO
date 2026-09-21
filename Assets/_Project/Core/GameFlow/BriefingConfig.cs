namespace Marco.Core.GameFlow
{
    /// <summary>
    /// §12.4 로비 브리핑 · 오프닝 가이드 수치.
    /// </summary>
    public static class BriefingConfig
    {
        /// <summary>
        /// §12.4 "라운드 시작 전 <b>맵 평면도를 30초간</b> 표시한다. 라운드 시작 시 사라진다."
        ///
        /// <para>
        /// <b>새 <see cref="GameFlowState"/>를 만들지 않는다.</b> §15.4 RoleAssign("3초 연출, 역할 배정")
        /// 뒤, 맵 로드가 끝난 시점부터 <c>RoleAssign</c> 페이즈 안의 하위 구간으로 센다 — 새 상태를
        /// 추가하면 <c>!= InGame</c> 게이트 전체가 그 상태를 알아야 한다(블록 4에서 같은 이유로
        /// 최후 생존자 페이즈도 하위 상태로 두었다). 라운드 타이머는 브리핑이 끝나고 시작한다.
        /// </para>
        /// </summary>
        public const float Seconds = 30f;

        /// <summary>
        /// §12.4 "첫 20초는 강제 오프닝 가이드(내 발소리 확인 → 첫 속삭임 유도)". 라운드 시작 기준.
        /// </summary>
        public const float OpeningGuideSeconds = 20f;
    }
}
