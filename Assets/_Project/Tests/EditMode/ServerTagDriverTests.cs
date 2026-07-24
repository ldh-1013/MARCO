using NUnit.Framework;
using UnityEngine;
using Marco.Core.Role;
using Marco.Core.Tagging;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 11: 서버 권위 태그 검증기의 계약을 고정한다.
    ///
    /// **가장 중요한 것은 "서버가 실제로 재검증한다"**이다(§5.3). 클라이언트가 태그를
    /// 요청해도, 서버 측 값(거리·역할·이미태그됨)이 §3.1 규칙을 통과하지 못하면 확정되지
    /// 않는다. 특히 거리를 벗어난 요청은 거부돼야 한다.
    ///
    /// 규칙 자체(1.2m·술래→도망자)는 <see cref="TagRules"/>가 소유하고 <c>TaggingTests</c>가
    /// 이미 고정하므로, 여기서는 <see cref="ServerTagDriver.Validate"/>가 그 규칙을 올바르게
    /// 조합해 서버 권위 판정을 내리는지만 본다.
    /// </summary>
    public class ServerTagDriverTests
    {
        private static readonly Vector3 SeekerPos = Vector3.zero;
        private static Vector3 InRange => new Vector3(1.0f, 0f, 0f);   // 1.0m ≤ 1.2m
        private static Vector3 OutOfRange => new Vector3(2.0f, 0f, 0f); // 2.0m > 1.2m

        // 1) 정상: 술래가 범위 안 도망자를 태그 → 유효.
        [Test]
        public void SeekerTagsRunnerInRange_IsValid()
        {
            bool ok = ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner,
                targetAlreadyTagged: false, SeekerPos, InRange);

            Assert.IsTrue(ok);
        }

        // 2) **핵심(§5.3)**: 범위를 벗어난 요청은 서버가 거부한다.
        [Test]
        public void OutOfRange_IsRejectedByServer()
        {
            bool ok = ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner,
                targetAlreadyTagged: false, SeekerPos, OutOfRange);

            Assert.IsFalse(ok, "서버는 클라이언트가 거리 밖에서 보낸 태그 요청을 거부해야 한다");
        }

        // 3) 경계값 1.2m 정확히는 포함(TagRules와 일치).
        [Test]
        public void ExactlyAtRadius_IsValid()
        {
            bool ok = ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner,
                targetAlreadyTagged: false, SeekerPos, new Vector3(1.2f, 0f, 0f));

            Assert.IsTrue(ok);
        }

        // 4) 이미 태그된 대상(메아리)은 범위·역할이 맞아도 재태그 거부.
        [Test]
        public void AlreadyTagged_IsRejected()
        {
            bool ok = ServerTagDriver.Validate(RoleType.Seeker, RoleType.Runner,
                targetAlreadyTagged: true, SeekerPos, InRange);

            Assert.IsFalse(ok);
        }

        // 5) 역할 위반: 대상이 도망자가 아니면 거부(§3.1). 메아리·술래 대상 등.
        [TestCase(RoleType.Echo)]
        [TestCase(RoleType.Seeker)]
        public void NonRunnerTarget_IsRejected(RoleType targetRole)
        {
            bool ok = ServerTagDriver.Validate(RoleType.Seeker, targetRole,
                targetAlreadyTagged: false, SeekerPos, InRange);

            Assert.IsFalse(ok);
        }

        // 6) 역할 위반: 술래가 아닌 자의 태그 요청은 거부(도망자·메아리가 태그 주체 불가).
        [TestCase(RoleType.Runner)]
        [TestCase(RoleType.Echo)]
        public void NonSeekerTagger_IsRejected(RoleType seekerRole)
        {
            bool ok = ServerTagDriver.Validate(seekerRole, RoleType.Runner,
                targetAlreadyTagged: false, SeekerPos, InRange);

            Assert.IsFalse(ok);
        }

        // 7) 복합: 범위 밖 + 역할 위반이 동시여도 당연히 거부(거부 조건은 OR).
        [Test]
        public void OutOfRangeAndWrongRole_IsRejected()
        {
            bool ok = ServerTagDriver.Validate(RoleType.Runner, RoleType.Echo,
                targetAlreadyTagged: false, SeekerPos, OutOfRange);

            Assert.IsFalse(ok);
        }
    }
}
