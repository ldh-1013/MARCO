namespace Marco.Net
{
    /// <summary>
    /// 서버 틱의 <b>명시적 실행 순서</b>(<c>[DefaultExecutionOrder]</c> 값). 한 프레임 안에서
    /// ① 숨 게이지(<see cref="PulseNetworkSync"/>) → ② 수중 작업 세션(<see cref="ValveNetworkSync"/> · <see cref="RoundNetworkSync"/>) 순으로 돈다.
    ///
    /// <para>
    /// <b>왜 고정하는가 — §5.9-1 "게이지 0초 경계 처리"</b>: 부상 완료와 게이지 고갈이 같은 틱에 성립하면
    /// <b>부상 완료가 우선</b>(질식 무시)이다. 이전에는 두 컴포넌트의 <c>Update</c> 순서가 정해져 있지 않아
    /// 숨이 먼저 돌면 질식(고함급 파문 · 감속 · 강제 부상)이, 세션이 먼저 돌면 정상 부상이 나왔다 — 같은 입력에
    /// 결과가 프레임마다 달랐다. 이제 숨이 먼저 소모되고(그 틱 동안 잠수였으므로), 게이지는 세션에게
    /// "이번 틱에 부상이 끝나는가"를 물어 질식을 판정하며, 세션은 갱신된 숨으로 전이한다.
    /// </para>
    ///
    /// <para>
    /// 숨은 기본값(0)과 같은 값이라 다른 기본 스크립트와의 상대 순서는 바뀌지 않는다. 세션 소유자 두 개만 뒤로 민다.
    /// FishNet 송신 루프(<c>NetworkWriterLoop</c>)는 <c>short.MaxValue</c>라 이 뒤에서 도므로 SyncVar 전송 프레임은 그대로다.
    /// </para>
    /// </summary>
    internal static class ServerTickOrder
    {
        /// <summary>① §5.9-1 숨 게이지 · 파문 판정(<see cref="PulseNetworkSync"/>).</summary>
        public const int Breath = 0;

        /// <summary>② 수중 작업 세션 — 밸브 B·E(<see cref="ValveNetworkSync"/>), 배수구(<see cref="RoundNetworkSync"/>).</summary>
        public const int UnderwaterWork = 100;
    }
}
