using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Marco.Core.Sound;
using Marco.Presentation.Sound;

namespace Marco.Core.Tests
{
    /// <summary>
    /// T8: 델리버리 → 시각 오브젝트 수명 매핑을 고정한다.
    /// 렌더링(LineRenderer·IMGUI) 자체는 에디터 Play 검증 항목이고,
    /// 여기서는 Unity 의존이 없는 수명 규칙만 검증한다.
    ///
    /// 핵심 계약: **자연 만료는 Disappeared 델리버리 없이 자체 타이머로 끝난다**
    /// (T7 설계 · 스프린트 3 버그 조사 결론). 이걸 어기면 파문이 화면에 영원히 남는다.
    /// </summary>
    public class PulseVisualRegistryTests
    {
        private const ulong ListenerId = 999;

        private static PerceivedPulse Perceived(float radius, float duration, bool ringVisible,
            DirectionOctant octant = DirectionOctant.N, Vector3? sourcePos = null)
        {
            return new PerceivedPulse(radius, duration,
                ringVisible ? (sourcePos ?? Vector3.zero) : (Vector3?)null,
                octant, ringVisible);
        }

        private static PulseDelivery Appeared(int pulseId, PerceivedPulse p) =>
            new PulseDelivery(ListenerId, pulseId, PulseDeliveryKind.Appeared, p);

        private static PulseDelivery Updated(int pulseId, PerceivedPulse p) =>
            new PulseDelivery(ListenerId, pulseId, PulseDeliveryKind.Updated, p);

        private static PulseDelivery Disappeared(int pulseId) =>
            new PulseDelivery(ListenerId, pulseId, PulseDeliveryKind.Disappeared, null);

        // 1) Appeared → 시각 오브젝트 생성 + 이벤트 발행.
        [Test]
        public void Appeared_AddsVisual()
        {
            var registry = new PulseVisualRegistry();
            var added = new List<PulseVisualState>();
            registry.VisualAdded += added.Add;

            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            Assert.AreEqual(1, registry.ActiveVisualCount);
            Assert.AreEqual(1, added.Count);
            Assert.AreEqual(0, added[0].PulseId);
        }

        // 2) GAP-2: 벽 0개(좌표 공개) → WorldRing 표현.
        [Test]
        public void RingVisible_MapsToWorldRing()
        {
            var registry = new PulseVisualRegistry();
            var pos = new Vector3(3f, 0f, 4f);

            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true, sourcePos: pos)), now: 0f);

            Assert.IsTrue(registry.TryGet(0, out PulseVisualState state));
            Assert.AreEqual(PulseVisualKind.WorldRing, state.Kind);
            Assert.AreEqual(pos, state.SourcePos);
        }

        // 3) GAP-2: 차폐됨(좌표 비공개) → DirectionOnly 표현, 방위만 유지.
        [Test]
        public void RingHidden_MapsToDirectionOnly()
        {
            var registry = new PulseVisualRegistry();

            registry.Apply(Appeared(0, Perceived(3f, 0.4f, ringVisible: false, octant: DirectionOctant.SE)), now: 0f);

            Assert.IsTrue(registry.TryGet(0, out PulseVisualState state));
            Assert.AreEqual(PulseVisualKind.DirectionOnly, state.Kind);
            Assert.AreEqual(DirectionOctant.SE, state.Direction);
        }

        // 4) Updated는 반경·지속을 갱신하되 StartTime(발생 시각)은 보존한다.
        [Test]
        public void Updated_PreservesStartTime_ButRefreshesValues()
        {
            var registry = new PulseVisualRegistry();
            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 10f);

            registry.Apply(Updated(0, Perceived(3f, 0.4f, ringVisible: false)), now: 10.25f);

            Assert.IsTrue(registry.TryGet(0, out PulseVisualState state));
            Assert.AreEqual(10f, state.StartTime, 0.001f, "타이머가 재시작되면 안 된다");
            Assert.AreEqual(3f, state.Radius, 0.001f);
            Assert.AreEqual(0.4f, state.Duration, 0.001f);
        }

        // 5) 차폐 발생 시 WorldRing → DirectionOnly로 표현이 전환된다.
        [Test]
        public void Updated_FlipsWorldRingToDirectionOnly()
        {
            var registry = new PulseVisualRegistry();
            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            registry.Apply(Updated(0, Perceived(3f, 0.4f, ringVisible: false)), now: 0.25f);

            registry.TryGet(0, out PulseVisualState state);
            Assert.AreEqual(PulseVisualKind.DirectionOnly, state.Kind);
        }

        // 6) Disappeared(조기 소실) → 즉시 제거.
        [Test]
        public void Disappeared_RemovesImmediately()
        {
            var registry = new PulseVisualRegistry();
            var removed = new List<int>();
            registry.VisualRemoved += removed.Add;
            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            registry.Apply(Disappeared(0), now: 0.25f);

            Assert.AreEqual(0, registry.ActiveVisualCount);
            CollectionAssert.AreEqual(new[] { 0 }, removed);
        }

        // 7) **핵심**: duration 경과 시 Disappeared 없이 자체 타이머로 사라진다.
        [Test]
        public void Tick_PastDuration_SelfRemovesWithoutDisappearedDelivery()
        {
            var registry = new PulseVisualRegistry();
            var removed = new List<int>();
            registry.VisualRemoved += removed.Add;
            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            registry.Tick(0.5f);
            Assert.AreEqual(1, registry.ActiveVisualCount, "아직 duration 이내");

            registry.Tick(0.8f); // 정확히 duration 도달

            Assert.AreEqual(0, registry.ActiveVisualCount);
            CollectionAssert.AreEqual(new[] { 0 }, removed);
        }

        // 8) Updated로 지속시간이 짧아지면 그만큼 일찍 사라진다.
        [Test]
        public void Updated_ShorteningDuration_CausesEarlierExpiry()
        {
            var registry = new PulseVisualRegistry();
            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            registry.Apply(Updated(0, Perceived(3f, 0.4f, ringVisible: false)), now: 0.25f);
            registry.Tick(0.45f); // 원래 0.8초였다면 살아있어야 하지만 0.4초로 줄었다

            Assert.AreEqual(0, registry.ActiveVisualCount);
        }

        // 9) Progress01: 확장·페이드 애니메이션의 기준값이 0→1로 정규화된다.
        [Test]
        public void Progress01_NormalizesOverDuration()
        {
            var registry = new PulseVisualRegistry();
            registry.Apply(Appeared(0, Perceived(6f, 0.8f, ringVisible: true)), now: 10f);
            registry.TryGet(0, out PulseVisualState state);

            Assert.AreEqual(0f, state.Progress01(10f), 0.001f);
            Assert.AreEqual(0.5f, state.Progress01(10.4f), 0.001f);
            Assert.AreEqual(1f, state.Progress01(10.8f), 0.001f);
            Assert.AreEqual(1f, state.Progress01(99f), 0.001f, "1을 넘지 않는다");
        }

        // 10) 여러 파문이 독립적으로 만료된다.
        [Test]
        public void MultiplePulses_ExpireIndependently()
        {
            var registry = new PulseVisualRegistry();
            registry.Apply(Appeared(0, Perceived(2f, 0.4f, ringVisible: true)), now: 0f);
            registry.Apply(Appeared(1, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            registry.Tick(0.5f); // 0번(0.4초)만 만료

            Assert.AreEqual(1, registry.ActiveVisualCount);
            Assert.IsFalse(registry.TryGet(0, out _));
            Assert.IsTrue(registry.TryGet(1, out _));
        }

        // 11) 모르는 ID에 대한 Updated/Disappeared는 조용히 무시된다(견고성).
        [Test]
        public void UnknownPulseId_IsIgnored()
        {
            var registry = new PulseVisualRegistry();

            Assert.DoesNotThrow(() => registry.Apply(Updated(42, Perceived(6f, 0.8f, true)), now: 0f));
            Assert.DoesNotThrow(() => registry.Apply(Disappeared(42), now: 0f));
            Assert.AreEqual(0, registry.ActiveVisualCount);
        }

        // 12) 스트레스: 30개를 동시에 올려도 전부 추적되고, 일괄 만료된다(§15.5 목표치).
        [Test]
        public void ThirtyConcurrentPulses_AllTrackedThenExpire()
        {
            var registry = new PulseVisualRegistry();
            for (int i = 0; i < 30; i++)
                registry.Apply(Appeared(i, Perceived(6f, 0.8f, ringVisible: true)), now: 0f);

            Assert.AreEqual(30, registry.ActiveVisualCount);

            registry.Tick(0.9f);

            Assert.AreEqual(0, registry.ActiveVisualCount);
        }

        // 13) CopyTo: 렌더러가 매 프레임 쓰는 무할당 순회 경로.
        //     사전을 인터페이스로 노출해 foreach를 돌리면 열거자가 박싱돼 프레임마다
        //     힙 할당이 생기므로, 재사용 버퍼에 복사하는 방식으로 바꿨다.
        [Test]
        public void CopyTo_FillsBufferWithAllVisuals()
        {
            var registry = new PulseVisualRegistry();
            registry.Apply(Appeared(0, Perceived(2f, 0.4f, ringVisible: true)), now: 0f);
            registry.Apply(Appeared(1, Perceived(6f, 0.8f, ringVisible: false)), now: 0f);

            var buffer = new List<PulseVisualState>();
            registry.CopyTo(buffer);

            Assert.AreEqual(2, buffer.Count);
            CollectionAssert.AreEquivalent(new[] { 0, 1 }, buffer.ConvertAll(v => v.PulseId));
        }

        // 14) CopyTo는 버퍼를 재사용해도 이전 내용이 남지 않는다(매 프레임 호출되는 경로).
        [Test]
        public void CopyTo_ClearsPreviousBufferContents()
        {
            var registry = new PulseVisualRegistry();
            var buffer = new List<PulseVisualState>();

            registry.Apply(Appeared(0, Perceived(2f, 0.4f, ringVisible: true)), now: 0f);
            registry.CopyTo(buffer);
            Assert.AreEqual(1, buffer.Count);

            registry.Apply(Disappeared(0), now: 0.1f);
            registry.CopyTo(buffer);

            Assert.AreEqual(0, buffer.Count, "이전 프레임 잔여물이 남으면 사라진 파문이 계속 그려진다");
        }

        // ── §3.4 게이지 플래그 전달 (스프린트 26 더블체크) ──────────────────
        // 서버가 내린 GaugeLit 결론이 시각 상태까지 손실 없이 도달해야 렌더러가 그릴 수 있다.

        // 15) Appeared의 GaugeLit이 시각 상태로 그대로 넘어간다.
        [Test]
        public void Appeared_CarriesGaugeLitIntoVisualState()
        {
            var registry = new PulseVisualRegistry();

            registry.Apply(new PulseDelivery(ListenerId, 7, PulseDeliveryKind.Appeared,
                Perceived(22f, 2.5f, ringVisible: false), gaugeLit: true), now: 0f);

            Assert.IsTrue(registry.TryGet(7, out PulseVisualState state));
            Assert.IsTrue(state.GaugeLit);
        }

        // 16) 게이지는 차폐 여부와 무관하다 — 월드 링이 함께 보여도 술래는 게이지를 받는다.
        [Test]
        public void Appeared_WorldRingPulse_CanAlsoLightGauge()
        {
            var registry = new PulseVisualRegistry();

            registry.Apply(new PulseDelivery(ListenerId, 8, PulseDeliveryKind.Appeared,
                Perceived(9f, 1.2f, ringVisible: true), gaugeLit: true), now: 0f);

            Assert.IsTrue(registry.TryGet(8, out PulseVisualState state));
            Assert.AreEqual(PulseVisualKind.WorldRing, state.Kind);
            Assert.IsTrue(state.GaugeLit, "§3.4 게이지는 벽 0개(월드 링) 상황에서도 뜬다.");
        }

        // 17) Updated가 게이지 상태를 갱신하되 StartTime(페이드 기준)은 보존한다.
        [Test]
        public void Updated_RefreshesGaugeLitButKeepsStartTime()
        {
            var registry = new PulseVisualRegistry();

            registry.Apply(new PulseDelivery(ListenerId, 9, PulseDeliveryKind.Appeared,
                Perceived(22f, 2.5f, ringVisible: false), gaugeLit: true), now: 1f);

            registry.Apply(new PulseDelivery(ListenerId, 9, PulseDeliveryKind.Updated,
                Perceived(11f, 2.5f, ringVisible: false), gaugeLit: true), now: 1.25f);

            Assert.IsTrue(registry.TryGet(9, out PulseVisualState state));
            Assert.IsTrue(state.GaugeLit);
            Assert.AreEqual(1f, state.StartTime, 0.0001f, "갱신이 페이드 타이머를 되감으면 안 된다.");
        }
    }
}
