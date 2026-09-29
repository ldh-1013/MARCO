using System.Collections.Generic;
using Marco.Core.Objectives;
using Marco.Core.Sound;
using Marco.Presentation.UI;
using NUnit.Framework;
using UnityEngine;

namespace Marco.Core.Tests
{
    /// <summary>
    /// 2-1 게이트 개방 알림 — §10.5 "게이트가 열리는 순간 양쪽 출구에서 고함급(22m) 개방음", §12.4 "탈출구 개방(양쪽 출구)".
    /// 서버(<c>RoundNetworkSync.ServerTickGate</c>)가 <see cref="RoundObjective.Tick"/> → <see cref="GateAnnouncement.CollectPulses"/>를
    /// 그대로 부른다. 문구는 HUD · 출구 트리거가 <see cref="HudFormatter"/>를 부른다.
    /// </summary>
    public class GateOpenAnnouncementTests
    {
        private static readonly Vector3 FrontDoor = new Vector3(7f, 1f, 40f);
        private static readonly Vector3 DrainExit = new Vector3(46f, 1f, 2f);

        [Test]
        public void GateOpens_ShoutPulseAtBothExits_ExactlyOnce()
        {
            var objective = new RoundObjective();
            objective.BeginRound(totalPlayers: 3, activeValves: 3);
            var exits = new List<Vector3> { FrontDoor, DrainExit };
            var tick = new List<Vector3>();
            var emitted = new List<Vector3>();

            // 0 → 1 → 2(개방) → 3 → 역류로 0 → 다시 2: 알림은 래치가 열린 순간 한 번뿐이다.
            foreach (int opened in new[] { 0, 1, 2, 3, 0, 2 })
            {
                GateAnnouncement.CollectPulses(objective.Tick(opened), exits, tick);
                emitted.AddRange(tick);
            }

            Assert.AreEqual(SoundType.Shout, GateAnnouncement.PulseType, "새 SoundType 없이 고함급(22m)을 재사용한다");
            CollectionAssert.AreEqual(exits, emitted, "게이트가 열린 순간 정문 · 배수로에서 한 번씩");
        }

        [Test]
        public void GateHint_Open_NamesBothExits()
        {
            Assert.AreEqual("탈출구 개방 — 정문·배수로", HudFormatter.FormatGateHint(true));
            Assert.IsEmpty(HudFormatter.FormatGateHint(false));
        }

        [TestCase(2)]
        [TestCase(3)]
        public void GateClosedHint_UsesRequiredCount_NotAllThreeValves(int required)
        {
            string text = HudFormatter.FormatGateClosedHint(required);
            int other = required == 2 ? 3 : 2;

            StringAssert.Contains($"밸브 {required}개", text);
            StringAssert.DoesNotContain($"{other}개", text, "요구 수가 아닌 숫자가 보이면 안 된다");
            StringAssert.DoesNotContain("모두", text, "활성 밸브를 모두 열 필요는 없다(동시 개방 요구 수)");
            StringAssert.DoesNotContain("배수로 게이트", text, "출구는 정문 · 배수로 둘이다");
        }
    }
}
