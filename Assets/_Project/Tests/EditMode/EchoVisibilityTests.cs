using Marco.Core.Role;
using NUnit.Framework;

namespace Marco.Core.Tests
{
    /// <summary>
    /// GAP-63: §3.2 메아리를 생존자에게 숨기는 규칙.
    ///
    /// **여기서 검증하는 것은 "누가 누구를 봐야 하는가"의 순수 판정뿐이다.**
    /// 실제 <c>Renderer.enabled</c> 반영과 역할 전환 시점의 갱신은 `MonoBehaviour` 수명주기와
    /// 렌더링 컨텍스트가 필요해 EditMode로 재현할 수 없다 — 실기(§29-0 B6/D)로만 확인된다.
    /// </summary>
    public class EchoVisibilityTests
    {
        // ── 핵심 규칙: 생존자는 메아리를 못 본다 ──────────────────────────

        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Seeker)]
        public void Survivor_CannotSeeEcho(RoleType viewerRole)
        {
            Assert.IsFalse(EchoVisibility.ShouldRender(viewerRole, RoleType.Echo, isSelf: false),
                "생존자에게 메아리가 보이면 §3.2 노크의 은밀한 유인이 성립하지 않는다.");
        }

        // ── 생존자끼리는 평소대로 ────────────────────────────────────────

        [TestCase(RoleType.Runner, RoleType.Runner)]
        [TestCase(RoleType.Runner, RoleType.Seeker)]
        [TestCase(RoleType.Seeker, RoleType.Runner)]
        [TestCase(RoleType.Seeker, RoleType.Seeker)]
        public void Survivors_SeeEachOther(RoleType viewerRole, RoleType targetRole)
        {
            Assert.IsTrue(EchoVisibility.ShouldRender(viewerRole, targetRole, isSelf: false));
        }

        // ── 메아리 상호 가시성 (기획서 미명시 — 이 프로젝트의 가정) ───────

        [Test]
        public void Echo_SeesOtherEcho()
        {
            // 가정: §3.2가 메아리끼리 별도 사망자 채널로 대화한다고 하므로 서로 인지하는 관계로 봤다.
            Assert.IsTrue(EchoVisibility.ShouldRender(RoleType.Echo, RoleType.Echo, isSelf: false));
        }

        [Test]
        public void Echo_StillSeesSurvivors()
        {
            // 메아리는 관전·유인 역할이라 생존자를 볼 수 있어야 한다(§3.2 노크 지점 선택의 근거).
            Assert.IsTrue(EchoVisibility.ShouldRender(RoleType.Echo, RoleType.Runner, isSelf: false));
            Assert.IsTrue(EchoVisibility.ShouldRender(RoleType.Echo, RoleType.Seeker, isSelf: false));
        }

        // ── 자기 자신은 이 규칙이 건드리지 않는다 ─────────────────────────

        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Seeker)]
        [TestCase(RoleType.Echo)]
        public void Self_IsAlwaysRendered(RoleType role)
        {
            // 1인칭 카메라가 캡슐 안이라 어차피 안 보이지만, 규칙이 자기 몸체를 끄면
            // 그림자·3인칭 전환 같은 후속 작업이 꼬인다. 명시적으로 건드리지 않는다.
            Assert.IsTrue(EchoVisibility.ShouldRender(role, role, isSelf: true));
        }

        [Test]
        public void Self_AsEcho_IsNotHiddenEvenWhenRuleWouldHideOthers()
        {
            // 뷰어가 생존자로 잘못 전달돼도 자기 자신이면 숨기지 않는다(방어적 단락).
            Assert.IsTrue(EchoVisibility.ShouldRender(RoleType.Runner, RoleType.Echo, isSelf: true));
        }

        // ── 역할 전환 시나리오 (실기 B6/D가 확인할 전이) ──────────────────

        [Test]
        public void ViewerBecomingEcho_RevealsPreviouslyHiddenEchoes()
        {
            // 실기 시나리오: 러너였던 내가 태그당해 메아리가 되는 순간,
            // 그전까지 안 보이던 기존 메아리들이 보이기 시작해야 한다.
            Assert.IsFalse(EchoVisibility.ShouldRender(RoleType.Runner, RoleType.Echo, isSelf: false));
            Assert.IsTrue(EchoVisibility.ShouldRender(RoleType.Echo, RoleType.Echo, isSelf: false));
        }

        [Test]
        public void TargetBecomingEcho_HidesItFromSurvivor()
        {
            // 반대 방향: 내가 러너인 채로, 눈앞의 러너가 태그당하면 그 순간 사라져야 한다.
            Assert.IsTrue(EchoVisibility.ShouldRender(RoleType.Runner, RoleType.Runner, isSelf: false));
            Assert.IsFalse(EchoVisibility.ShouldRender(RoleType.Runner, RoleType.Echo, isSelf: false));
        }
    }
}
