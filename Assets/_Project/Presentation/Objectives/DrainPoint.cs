using Marco.Core.Objectives;
using Marco.Core.Role;
using Marco.Presentation.GameFlow;
using Marco.Presentation.Player;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Marco.Presentation.Objectives
{
    /// <summary>
    /// §6.5-2 배수구 지점(맵 v2의 <c>Drain_1_메인풀</c>·<c>Drain_2_유아풀</c> 마커에 붙는다).
    ///
    /// <list type="number">
    /// <item><b>위치 등록</b> — <see cref="DrainRegistry"/>에 스스로 등록한다. Net(<c>RoundNetworkSync</c>)이
    ///   진입 알림 파문·작업 자격(거리)을 이 좌표로 계산하는데, Presentation 타입을 직접 찾으면
    ///   §15.2 경계가 깨진다(<see cref="EscapePointTrigger"/>와 같은 패턴).</item>
    /// <item><b>작업 의사 전달</b> — 로컬 도망자가 활성 배수구 근처에서 E를 누르고 있는지를
    ///   <see cref="RoundCoordinator.SubmitDrainHold"/>로 보낸다. <b>값이 바뀔 때만</b> 보낸다.</item>
    /// </list>
    ///
    /// <para>
    /// 여기서의 역할·거리 검사는 RPC 낭비를 막는 <b>사전 필터일 뿐</b>이다. 서버는 의사를 보관하고
    /// 역할·수평 거리·잠수 여부를 매 틱 다시 판정한다(<see cref="DrainHatch.CanWork"/>, GAP-24).
    /// 잠수 여부를 여기서 거르지 않는 이유 — 잠수 판정의 권위는 서버 숨 게이지에 있고, 클라이언트가
    /// 먼저 잘라내면 "E를 누른 채 잠수"하는 순서가 서버에 전달되지 않는다.
    /// </para>
    /// </summary>
    public sealed class DrainPoint : MonoBehaviour
    {
        [SerializeField] private DrainId _id = DrainId.MainPool;
        [SerializeField] private Key _interactKey = Key.E;

        private RoundCoordinator _round;
        private bool _lastSentHeld;

        public DrainId Id => _id;

        /// <summary>맵 생성기(에디터)가 마커에 붙이면서 식별자를 지정한다.</summary>
        public void Configure(DrainId id) => _id = id;

        private void OnEnable() => DrainRegistry.Register(_id, transform);

        private void OnDisable()
        {
            DrainRegistry.Unregister(_id, transform);

            // 비활성화되는 순간 누르고 있던 의사를 거둬들인다 — 서버에 "누르고 있다"가 남지 않게.
            if (_lastSentHeld && _round != null)
                _round.SubmitDrainHold(false);
            _lastSentHeld = false;
        }

        private void Update()
        {
            // 맵(이 컴포넌트)과 시스템(RoundCoordinator)이 다른 씬일 수 있어 지연 바인딩한다.
            if (_round == null)
                _round = FindAnyObjectByType<RoundCoordinator>();
            if (_round == null)
                return;

            bool want = WantsToWork();
            if (want == _lastSentHeld)
                return;

            _lastSentHeld = want;
            _round.SubmitDrainHold(want);
        }

        private bool WantsToWork()
        {
            if (!_round.IsNetworkActive || _round.ActiveDrain != (int)_id)
                return false;

            // GAP-61: 로컬 플레이어는 매 프레임 재조회한다(필드 캐시 금지).
            FirstPersonController player = LocalPlayerRegistry.Current;
            if (player == null || !player.IsLocallyControlled || player.Role != RoleType.Runner)
                return false;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard[_interactKey].isPressed)
                return false;

            return InteractionRules.InRange(player.transform.position, transform.position, underwaterTarget: true);
        }
    }
}
