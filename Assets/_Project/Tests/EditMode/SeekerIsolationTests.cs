using Marco.Core.GameFlow;
using Marco.Core.Role;
using NUnit.Framework;

namespace Marco.Tests.EditMode
{
    /// <summary>
    /// 스프린트 24 술래 격리(§10.1 "술래는 격리 공간에서 3초 후 별도 진입").
    /// 순수 로직이라 Unity 없이 그대로 돈다.
    /// </summary>
    public class SeekerIsolationTests
    {
        [Test]
        public void IsolationSeconds_MatchesDesignDoc()
        {
            // §10.1 "3초 후 별도 진입".
            Assert.AreEqual(3f, SeekerIsolation.IsolationSeconds, 0.001f);
        }

        [Test]
        public void AppliesToSeekerOnly()
        {
            // §10.1은 술래만 언급한다 — 러너·메아리는 격리 대상이 아니다.
            Assert.IsTrue(SeekerIsolation.AppliesTo(RoleType.Seeker));
            Assert.IsFalse(SeekerIsolation.AppliesTo(RoleType.Runner));
            Assert.IsFalse(SeekerIsolation.AppliesTo(RoleType.Echo));
        }

        [Test]
        public void Seeker_HoldsAtRoundStart()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);

            Assert.IsTrue(isolation.IsHolding);
            Assert.AreEqual(3f, isolation.Remaining, 0.001f);
        }

        [Test]
        public void Runner_IsFreeImmediately()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Runner);

            Assert.IsFalse(isolation.IsHolding, "러너는 격리 없이 바로 움직일 수 있어야 한다.");
            Assert.AreEqual(0f, isolation.Remaining, 0.001f);
        }

        [Test]
        public void Seeker_ReleasesAfterThreeSeconds()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);

            isolation.Tick(1.5f);
            Assert.IsTrue(isolation.IsHolding, "1.5초 시점에는 아직 묶여 있어야 한다.");

            isolation.Tick(1.5f);
            Assert.IsFalse(isolation.IsHolding, "3초가 지나면 풀려야 한다.");
            Assert.AreEqual(0f, isolation.Remaining, 0.001f);
        }

        [Test]
        public void Remaining_NeverGoesNegative()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);

            isolation.Tick(99f);

            Assert.AreEqual(0f, isolation.Remaining, 0.001f);
            Assert.IsFalse(isolation.IsHolding);
        }

        [Test]
        public void Tick_IgnoresNonPositiveDelta()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);

            isolation.Tick(0f);
            isolation.Tick(-10f);

            Assert.AreEqual(3f, isolation.Remaining, 0.001f);
        }

        [Test]
        public void Tick_DoesNothingForRunner()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Runner);

            isolation.Tick(1f);

            Assert.IsFalse(isolation.IsHolding);
        }

        [Test]
        public void Begin_RestartsHoldForRematch()
        {
            // 리매치로 새 라운드가 시작되면 격리가 다시 걸려야 한다(스프린트 22 스폰 리셋과 연동).
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);
            isolation.Tick(3f);
            Assert.IsFalse(isolation.IsHolding);

            isolation.Begin(RoleType.Seeker);

            Assert.IsTrue(isolation.IsHolding);
            Assert.AreEqual(3f, isolation.Remaining, 0.001f);
        }

        [Test]
        public void Begin_AsRunner_ClearsPreviousSeekerHold()
        {
            // §2.3 로테이션으로 다음 판에 러너가 되면 격리가 남아 있으면 안 된다.
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);

            isolation.Begin(RoleType.Runner);

            Assert.IsFalse(isolation.IsHolding);
        }

        [Test]
        public void Clear_EndsHoldImmediately()
        {
            var isolation = new SeekerIsolation();
            isolation.Begin(RoleType.Seeker);

            isolation.Clear();

            Assert.IsFalse(isolation.IsHolding);
            Assert.AreEqual(0f, isolation.Remaining, 0.001f);
        }

        [Test]
        public void FreshInstance_IsNotHolding()
        {
            Assert.IsFalse(new SeekerIsolation().IsHolding);
        }
    }
}
