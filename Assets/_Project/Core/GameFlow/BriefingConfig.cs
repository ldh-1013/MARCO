namespace Marco.Core.GameFlow
{
    /// <summary>
    /// §12.4 로비 브리핑 · 오프닝 가이드 수치.
    /// </summary>
    public static class BriefingConfig
    {
        /// <summary>
        /// §12.4 <b>정본</b> — "라운드 시작 전 <b>맵 평면도를 30초간</b> 표시한다. 라운드 시작 시 사라진다."
        /// <b>지금은 쓰지 않는다</b>(아래 <see cref="Seconds"/>가 테스트 반복용 임시값). 기획서 값을 코드에 남겨
        /// <c>PrototypeScenarioTests.BriefingDesignSeconds_MatchDesignDoc</c>가 고정한다 — 정식 밸런스로 되돌릴 때
        /// 30을 다시 손으로 적지 않게(§16.4 잔상의 본안/차선책 상수와 같은 방식).
        /// </summary>
        public const float DesignSeconds = 30f;

        /// <summary>
        /// §12.4 브리핑 길이 — 실제로 쓰는 값.
        ///
        /// <para>
        /// <b>새 <see cref="GameFlowState"/>를 만들지 않는다.</b> §15.4 RoleAssign("3초 연출, 역할 배정")
        /// 뒤, 맵 로드가 끝난 시점부터 <c>RoleAssign</c> 페이즈 안의 하위 구간으로 센다 — 새 상태를
        /// 추가하면 <c>!= InGame</c> 게이트 전체가 그 상태를 알아야 한다(블록 4에서 같은 이유로
        /// 최후 생존자 페이즈도 하위 상태로 두었다). 라운드 타이머는 브리핑이 끝나고 시작한다.
        /// </para>
        ///
        /// <para>
        /// <b>[09-24 테스트 반복용 임시값 — 정본은 <see cref="DesignSeconds"/> 30초]</b> 3인 실기 반복 테스트 속도를 위해
        /// <b>7초</b>로 단축했다(카운트다운 3초 + 이 값 ≈ 로비 준비 완료부터 라운드 시작까지 10초).
        /// <b>정식 밸런스 복원 = 이 줄을 <c>public const float Seconds = DesignSeconds;</c>로 바꾸는 한 줄.</b>
        /// 그러면 <c>PrototypeScenarioTests.BriefingSeconds_MatchPrototypeOverride</c>가 의도대로 실패하니 그 테스트를 지운다.
        /// 다른 테스트는 이 상수에서 틱 수를 계산하므로 고칠 필요가 없다.
        /// </para>
        /// </summary>
        public const float Seconds = 7f;

        /// <summary>
        /// §12.4 "첫 20초는 강제 오프닝 가이드(내 발소리 확인 → 첫 속삭임 유도)". 라운드 시작 기준.
        /// </summary>
        public const float OpeningGuideSeconds = 20f;
    }
}
