using UnityEngine;
using Marco.Core.Sound;

namespace Marco.Core.Net
{
    /// <summary>
    /// 파문(발소리·밸브 소음)의 Presentation ↔ Net 연결 계약(스프린트 14, §14.3 `SoundPulse`).
    ///
    /// **역할**: 파문이 네트워크로 관리될 때, 발생원 계층(<c>LocalPulsePipelineBehaviour</c>·
    /// <c>ValveInteractor</c>, Presentation)은 더 이상 로컬에서 §5.6 차폐를 판정하지 않고
    /// <b>"이런 소리가 났다"는 사실만 서버에 알린다</b>. 청취자별 판정과 개별 전송은 서버가 한다.
    ///
    /// **의도적으로 전달하지 않는 것**: 반경·지속시간·발생원 ID·발생 위치. 전부 서버가
    /// §5.1 표와 서버 측 플레이어 위치·RPC 호출자에서 스스로 얻는다(GAP-24 서버 권위) —
    /// 그래서 이 인터페이스는 <see cref="SoundType"/>만 받는다.
    ///
    /// **왜 Core에 두는가**: 구현체는 <c>PulseNetworkSync</c>(Net, <c>NetworkBehaviour</c>)지만
    /// 소비자는 Presentation이다. §15.2상 둘은 서로를 참조하지 않으므로 공통 조상 Core에
    /// 계약만 둔다 — <see cref="IValveNetworkBridge"/>·<c>IRoundNetworkBridge</c>와 같은 패턴.
    ///
    /// **로컬 폴백**: 네트워크가 시작되지 않으면 <see cref="NetworkActive"/>가 false다. 그때
    /// Presentation은 이 브릿지를 무시하고 스프린트 3~9의 로컬 스모크 리그(고정 청취자)를
    /// 그대로 쓴다(회귀 없음).
    /// </summary>
    public interface IPulseNetworkBridge
    {
        /// <summary>서버 권위 파문 경로가 활성인가. NetworkObject가 스폰된 뒤에만 true.</summary>
        bool NetworkActive { get; }

        /// <summary>
        /// 소리가 났음을 서버에 알린다(§14.3 `SoundPulse`, Client → Server).
        /// 위치·반경·지속·발생원은 서버가 결정하므로 여기서는 종류만 넘긴다.
        /// </summary>
        void SubmitPulse(SoundType type);

        /// <summary>
        /// §3.2 메아리 노크를 요청한다(§14.3 `EchoKnock`, Client → Server → All).
        ///
        /// **인자가 없다**(기획서 갱신). §3.2의 발생 위치가 "메아리의 현재 위치"로 바뀌면서
        /// 지점 클릭 방식이 폐기됐고, 위치는 <see cref="SubmitPulse"/>와 똑같이 서버가
        /// <c>caller.FirstObject</c>에서 얻는다(GAP-24). 역할·라운드 5회 하드캡·전환 후 20초
        /// 잠금·쿨다운·1.5초 지연도 전부 서버가 강제하므로,
        /// <b>클라이언트가 정할 수 있는 것은 "지금 쓴다"뿐이다.</b>
        /// </summary>
        void SubmitKnock();

        /// <summary>
        /// §3.5 술래 외침을 요청한다. <b>인자가 없다</b> — 역할·쿨다운 45초·선딜레이 1초·
        /// 발동 위치·공포 반경 판정을 전부 서버가 소유한다(GAP-24).
        /// 클라이언트가 정할 수 있는 것은 "지금 쓴다"뿐이다.
        /// </summary>
        void SubmitShout();

        /// <summary>
        /// §3.5 "숨 참기(선딜레이 1초 안에 입력)" 의사를 서버에 알린다.
        ///
        /// <b>여기서 게이지가 깎이지 않는다.</b> 실제 차감(-4.5, §5.9-1 [v0.4])은 외침이
        /// 발동하는 순간 서버가 공포 반경을 판정하면서 일어난다 — 외침이 취소되거나
        /// 내가 22m 밖이면 아무 비용도 들지 않아야 하기 때문이다.
        /// </summary>
        void SubmitHoldBreath();

        /// <summary>
        /// §4.3 "잠수(Left Ctrl 홀드)" <b>입력 상태</b>를 서버에 알린다. 상태가 바뀔 때만 보낸다.
        ///
        /// <para>
        /// <b>왜 이것만은 주장으로 받는가.</b> §5.9-1 숨 게이지는 서버 권위이므로 서버가
        /// 매 프레임 각 플레이어의 숨 상태를 알아야 하는데, 그 세 입력 중 둘은 서버가
        /// 스스로 재계산한다 — 물속 여부는 §10.1 수면 지오메트리 + 동기화된 위치에서,
        /// 잠수 가능 여부는 서버가 들고 있는 게이지에서. <b>"버튼을 누르고 있다"만이
        /// 서버가 알 수 없는 사실이다.</b> 그래서 그것만 받는다(GAP-24의 정확한 형태).
        /// </para>
        ///
        /// <para>
        /// 따라서 이 주장으로 할 수 있는 일에는 한계가 있다 — 마른 땅에서 잠수할 수 없고,
        /// 게이지 0으로 잠수를 유지할 수도 없다. 판정식은 <c>DiveRules.IsDiving</c> 하나다.
        /// </para>
        /// </summary>
        void SubmitDiveIntent(bool held);
    }

    /// <summary>
    /// 서버가 개별 전송한 §14.3 `PerceivedPulse`를 받아 화면에 반영하는 소비자 계약
    /// (스프린트 14). 구현체는 <c>PulseVisualRenderer</c>(T8, Presentation)이고 호출자는
    /// <c>PulseNetworkSync</c>(Net)라, 여기서도 Core에 계약만 둔다.
    ///
    /// 시그니처가 T8 렌더러의 기존 <c>Apply</c>와 동일하다 — 네트워크로 받은 델리버리를
    /// 로컬 판정 결과와 <b>완전히 같은 방식으로</b> 렌더링한다(새 시각화 코드 없음).
    ///
    /// <paramref name="now"/>는 <b>수신 측 로컬 시각</b>이다. 렌더러는 Appeared 시점에 받은
    /// <c>PerceivedDuration</c>으로 자체 타이머를 돌려 스스로 소멸하므로(스프린트 4 결론),
    /// 서버·클라이언트 시계를 동기화할 필요가 없다.
    /// </summary>
    public interface IPulseDeliverySink
    {
        void Apply(in PulseDelivery delivery, float now);
    }
}
