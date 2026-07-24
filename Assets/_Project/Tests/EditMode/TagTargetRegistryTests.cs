using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Marco.Core.Net;
using Marco.Core.Role;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 스프린트 11: 태그 대상 등록소의 계약을 고정한다.
    /// 로컬 대역과 네트워크 플레이어를 한 목록으로 다루고, 태그 확정을 라운드 집계로
    /// 통지하는 조정 지점이므로, 등록·해제·통지·세션 초기화가 정확해야 한다.
    /// </summary>
    public class TagTargetRegistryTests
    {
        /// <summary>테스트용 최소 대상. MonoBehaviour 없이 계약만 구현한다.</summary>
        private sealed class FakeTarget : ITagTarget
        {
            public ulong PlayerId { get; set; }
            public RoleType Role { get; set; } = RoleType.Runner;
            public bool IsTagged { get; set; }
            public Vector3 WorldPosition { get; set; }
            public bool NetworkActive { get; set; }
            public int RequestCount { get; private set; }
            public void RequestTag(ulong seekerId, RoleType seekerRole) => RequestCount++;
        }

        // TagTargetRegistry는 static이라 테스트 간 상태가 누적된다. Unity Test Runner는
        // [SetUp]을 돌리지만, 각 테스트가 스스로도 초기화하도록 첫 줄에서 리셋한다
        // (스크래치패드 리플렉션 러너처럼 [SetUp]을 안 부르는 환경에서도 독립적으로 통과).
        [SetUp]
        public void Reset() => TagTargetRegistry.ResetForNewSession();

        [Test]
        public void Register_AddsTargetOnce()
        {
            TagTargetRegistry.ResetForNewSession();
            var t = new FakeTarget();
            TagTargetRegistry.Register(t);
            TagTargetRegistry.Register(t); // 중복 등록 방지

            Assert.AreEqual(1, TagTargetRegistry.Targets.Count);
            Assert.AreSame(t, TagTargetRegistry.Targets[0]);
        }

        [Test]
        public void Unregister_RemovesTarget()
        {
            TagTargetRegistry.ResetForNewSession();
            var t = new FakeTarget();
            TagTargetRegistry.Register(t);

            TagTargetRegistry.Unregister(t);

            Assert.AreEqual(0, TagTargetRegistry.Targets.Count);
        }

        [Test]
        public void NotifyTagged_RaisesEventWithTarget()
        {
            TagTargetRegistry.ResetForNewSession();
            var received = new List<ITagTarget>();
            TagTargetRegistry.TargetTagged += received.Add;
            var t = new FakeTarget { PlayerId = 7 };

            TagTargetRegistry.NotifyTagged(t);

            Assert.AreEqual(1, received.Count);
            Assert.AreSame(t, received[0]);
        }

        [Test]
        public void ResetForNewSession_ClearsTargetsAndSubscribers()
        {
            TagTargetRegistry.ResetForNewSession();
            int calls = 0;
            TagTargetRegistry.TargetTagged += _ => calls++;
            TagTargetRegistry.Register(new FakeTarget());

            TagTargetRegistry.ResetForNewSession();

            Assert.AreEqual(0, TagTargetRegistry.Targets.Count);
            // 구독자도 비워졌으므로 이후 통지는 아무도 받지 않는다(이전 판의 파괴된 구독자 방지).
            TagTargetRegistry.NotifyTagged(new FakeTarget());
            Assert.AreEqual(0, calls);
        }
    }
}
