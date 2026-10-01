using System.Collections.Generic;
using UnityEngine;

namespace Marco.Presentation.Sound
{
    /// <summary>
    /// "말하면 보인다" 벽 윤곽 리빌의 시간 규칙. <b>새 수치가 없다</b> — 반경 · 지속은 파문 자신의 값
    /// (§5.1 표 → 인지 배율을 거친 <see cref="PulseVisualState.Radius"/> · <see cref="PulseVisualState.Duration"/>,
    /// 확장 링과 같은 값)이고, 밝기는 확장 링의 투명도 곡선(<c>1 − 진행도</c>)을 그대로 쓴다.
    /// 그래서 소리 종류별 반경 · 지속 차이가 자동으로 반영된다. Unity 수명주기와 무관 — EditMode 테스트 가능.
    /// </summary>
    public static class WallRevealTiming
    {
        /// <summary>
        /// 확장 링(반경 = <paramref name="radius"/> × 진행도, 선형)이 발생 지점에서 <paramref name="distance"/>만큼
        /// 떨어진 벽에 닿는 시각. 반경 밖이면 링 소멸 시각(그때 밝기는 0이라 보이지 않는다).
        /// </summary>
        public static float ArrivalTime(float startTime, float duration, float radius, float distance)
        {
            if (radius <= 0f || duration <= 0f)
                return startTime;

            return startTime + duration * Mathf.Clamp01(distance / radius);
        }

        /// <summary>
        /// 벽 윤곽의 밝기(0~1). 링이 닿기 전 0 → 닿는 순간 그 시점 링의 밝기로 드러나 → 링과 함께 0으로 사라진다.
        /// </summary>
        public static float Alpha(float now, float startTime, float duration, float arrivalTime)
        {
            if (now < arrivalTime || duration <= 0f)
                return 0f;

            return 1f - Mathf.Clamp01((now - startTime) / duration);
        }

        /// <summary>
        /// BoxCollider의 월드 모서리 8개. 인덱스 0~3 = 로컬 −z 면(−x−y, +x−y, +x+y, −x+y 순), 4~7 = +z 면(같은 순).
        /// <paramref name="inflate"/>(m)만큼 바깥으로 부풀린다 — 선이 벽 면에 반쯤 묻히지 않게.
        /// </summary>
        public static void BoxCorners(Matrix4x4 localToWorld, Vector3 center, Vector3 size, Vector3 lossyScale,
            float inflate, Vector3[] corners)
        {
            Vector3 half = size * 0.5f;
            half.x += SafeDiv(inflate, lossyScale.x);
            half.y += SafeDiv(inflate, lossyScale.y);
            half.z += SafeDiv(inflate, lossyScale.z);

            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 3) == 1 || (i & 3) == 2 ? 1f : -1f;
                float sy = (i & 3) >= 2 ? 1f : -1f;
                float sz = i >= 4 ? 1f : -1f;
                Vector3 local = center + new Vector3(half.x * sx, half.y * sy, half.z * sz);
                corners[i] = localToWorld.MultiplyPoint3x4(local);
            }
        }

        /// <summary>
        /// 단위 큐브(로컬 −0.5~0.5, 프리미티브 큐브 메시)를 <paramref name="localToWorld"/>로 놓은 상자에서 <paramref name="point"/>의 최근접점.
        /// 회전한 상자도 맞다 — 로컬로 옮겨 각 축을 자른 뒤 되돌린다. 콜라이더 없는 상자(<see cref="RevealOutlineRegistry"/>)에 쓴다.
        /// </summary>
        public static Vector3 ClosestPointOnUnitBox(Matrix4x4 localToWorld, Vector3 point)
        {
            Vector3 local = localToWorld.inverse.MultiplyPoint3x4(point);
            local.x = Mathf.Clamp(local.x, -0.5f, 0.5f);
            local.y = Mathf.Clamp(local.y, -0.5f, 0.5f);
            local.z = Mathf.Clamp(local.z, -0.5f, 0.5f);
            return localToWorld.MultiplyPoint3x4(local);
        }

        private static float SafeDiv(float value, float scale)
        {
            float s = Mathf.Abs(scale);
            return s > 1e-5f ? value / s : 0f;
        }
    }

    /// <summary>
    /// 파문 하나가 닿는 <b>벽의 윤곽(모서리)만</b> 확장 링과 같은 가산 합성 선으로 잠깐 그린다 — 면 채움 없음.
    /// <see cref="PulseVisualRenderer"/>가 소유하고, 확장 링이 보이는 파문(<see cref="PulseVisualKind.WorldRing"/>)에만 붙인다.
    ///
    /// <para>
    /// <b>범위</b>: 발생 지점에서 파문 반경 안의 벽 — <c>Physics.OverlapSphere</c>로 SoundBlocking 레이어의
    /// <c>Wall</c> 태그 · <b>고체</b> 콜라이더만 찾는다(맵 v2 생성기의 벽 차폐 자식 <c>Wall_n/Sound</c> — 벽과 같은 크기의
    /// BoxCollider). 트리거(수면판 · 풀 측벽)는 빠진다. 벽은 정적이라 파문이 생길 때 한 번만 찾는다.
    /// [10-02] 콜라이더 없는 출구 문짝(<see cref="RevealOutlineRegistry"/>)도 같은 반경 · 도달 시각 · 차폐 규칙으로 함께 그린다.
    /// </para>
    ///
    /// <para>
    /// <b>차폐</b>: 후보마다 발생 지점의 <b>눈높이</b>(<see cref="Marco.Core.Locomotion.DiveRules.StandingHeadHeight"/> —
    /// §7 찰칵이 섬광의 시야 판정과 같은 규칙)에서 그 벽의 최근접점으로 레이를 쏘아, <b>처음 맞는 SoundBlocking 고체가
    /// 그 벽 자신일 때만</b> 남긴다. 다른 벽이나 층간 차폐판(<c>FloorSlab</c>)에 먼저 막히면 뺀다 — 가려진 벽 · 다른 층 벽이
    /// 뚫려 보이지 않게. 같은 거리에서 두 면이 겹치는 경우(1층 벽 윗면 = 2층 차폐판 윗면)는 보이지 않는 쪽으로 친다.
    /// 최근접점 한 점만 보므로, 일부만 가려진 긴 벽은 그 점이 가려지면 통째로 빠진다.
    /// </para>
    ///
    /// <para>
    /// <b>선 구성</b>: 상자 모서리 12개 = 앞 · 뒤 면의 닫힌 사각형 2개 + 두 면을 잇는 선분 4개(선 6개). 한 줄로 이으면
    /// 모서리 3개를 되짚게 되는데, 가산 합성이라 그 부분만 두 배로 밝아진다.
    /// </para>
    /// </summary>
    public sealed class PulseWallRevealer
    {
        private const int LinesPerWall = 6;
        private const int QueryCapacity = 128;

        private readonly struct WallOutline
        {
            public readonly float ArrivalTime;
            public readonly int FirstLine; // _lines 안의 시작 인덱스(6개 연속)

            public WallOutline(float arrivalTime, int firstLine)
            {
                ArrivalTime = arrivalTime;
                FirstLine = firstLine;
            }
        }

        private sealed class Reveal
        {
            public Vector3 Source;
            public float Radius;
            public float StartTime;
            public float Duration;
            public readonly List<WallOutline> Walls = new List<WallOutline>();
            public readonly List<LineRenderer> Lines = new List<LineRenderer>();
        }

        private readonly Transform _parent;
        private readonly Material _material;
        private readonly float _lineWidth;
        private readonly Dictionary<int, Reveal> _reveals = new Dictionary<int, Reveal>();
        private readonly Stack<LineRenderer> _pool = new Stack<LineRenderer>();
        private readonly Stack<Reveal> _revealPool = new Stack<Reveal>();
        private readonly Collider[] _hits = new Collider[QueryCapacity];
        private readonly Vector3[] _corners = new Vector3[8];
        private readonly Vector3[] _loop = new Vector3[4];
        private readonly Vector3[] _segment = new Vector3[2];
        private readonly List<int> _removeScratch = new List<int>();

        private int _wallMask;
        private bool _maskResolved;

        public PulseWallRevealer(Transform parent, Material material, float lineWidth)
        {
            _parent = parent;
            _material = material;
            _lineWidth = lineWidth;
        }

        /// <summary>진단용 — 지금 윤곽을 가진 파문 수.</summary>
        public int ActiveRevealCount => _reveals.Count;

        /// <summary>파문이 생겼다(또는 인지 반경이 바뀌어 다시 붙인다). 반경 안 벽을 찾아 도달 시각을 정해 둔다.</summary>
        public void Add(in PulseVisualState state)
        {
            // 인지 결과 갱신(Updated)이 같은 반경 · 위치로 다시 오면 벽을 다시 찾지 않는다.
            if (_reveals.TryGetValue(state.PulseId, out Reveal existing)
                && existing.Source == state.SourcePos
                && Mathf.Approximately(existing.Radius, state.Radius)
                && Mathf.Approximately(existing.Duration, state.Duration)
                && Mathf.Approximately(existing.StartTime, state.StartTime))
                return;

            Remove(state.PulseId);
            if (state.Radius <= 0f || state.Duration <= 0f)
                return;

            int count = Physics.OverlapSphereNonAlloc(state.SourcePos, state.Radius, _hits, WallMask(),
                QueryTriggerInteraction.Ignore);
            // 반경 안에 벽이 없어도 콜라이더 없는 상자(출구 문 — RevealOutlineRegistry)는 있을 수 있다 — 여기서 끝내지 않는다.

            Reveal reveal = _revealPool.Count > 0 ? _revealPool.Pop() : new Reveal();
            reveal.Source = state.SourcePos;
            reveal.Radius = state.Radius;
            reveal.StartTime = state.StartTime;
            reveal.Duration = state.Duration;

            Vector3 eye = state.SourcePos + Vector3.up * Marco.Core.Locomotion.DiveRules.StandingHeadHeight;

            for (int i = 0; i < count; i++)
            {
                Collider wall = _hits[i];
                _hits[i] = null;
                if (wall == null || !wall.CompareTag(PhysicsOcclusionProbe.WallTag))
                    continue;

                if (!IsUnobstructed(eye, wall))
                    continue;

                float distance = Vector3.Distance(state.SourcePos, wall.ClosestPoint(state.SourcePos));
                float arrival = WallRevealTiming.ArrivalTime(state.StartTime, state.Duration, state.Radius, distance);

                reveal.Walls.Add(new WallOutline(arrival, reveal.Lines.Count));
                BuildOutline(wall, reveal.Lines);
            }

            // [10-02] 콜라이더 없는 상자(출구 문짝) — 벽과 같은 반경 · 도달 시각 · 눈높이 차폐 규칙.
            IReadOnlyList<Transform> boxes = RevealOutlineRegistry.All;
            for (int i = 0; i < boxes.Count; i++)
            {
                Transform box = boxes[i];
                if (box == null)
                    continue;

                Matrix4x4 m = box.localToWorldMatrix;
                float distance = Vector3.Distance(state.SourcePos, WallRevealTiming.ClosestPointOnUnitBox(m, state.SourcePos));
                if (distance > state.Radius || !IsUnobstructed(eye, WallRevealTiming.ClosestPointOnUnitBox(m, eye)))
                    continue;

                float arrival = WallRevealTiming.ArrivalTime(state.StartTime, state.Duration, state.Radius, distance);
                reveal.Walls.Add(new WallOutline(arrival, reveal.Lines.Count));
                WallRevealTiming.BoxCorners(m, Vector3.zero, Vector3.one, box.lossyScale, _lineWidth * 0.5f, _corners);
                AddOutlineLines(reveal.Lines);
            }

            if (reveal.Walls.Count == 0)
            {
                _revealPool.Push(reveal);
                return;
            }

            _reveals[state.PulseId] = reveal;
        }

        /// <summary>눈높이에서 벽의 최근접점까지 — 처음 맞는 SoundBlocking 고체가 그 벽 자신인가.</summary>
        private bool IsUnobstructed(Vector3 eye, Collider wall)
        {
            Vector3 delta = wall.ClosestPoint(eye) - eye;
            float distance = delta.magnitude;
            if (distance <= 1e-4f)
                return true; // 눈이 벽에 붙어 있다

            // 최근접점은 벽 표면 위라 레이가 표면 직전에서 멈출 수 있다 — 선 폭만큼 더 쏜다.
            if (!Physics.Raycast(eye, delta / distance, out RaycastHit hit, distance + _lineWidth, WallMask(),
                    QueryTriggerInteraction.Ignore))
                return true; // 사이에 아무것도 없다

            return hit.collider == wall;
        }

        /// <summary>
        /// 눈높이에서 콜라이더 없는 상자의 최근접점까지 — 사이에 SoundBlocking 고체가 하나도 없는가. 상자가 벽 앞면에 붙어 있으므로
        /// 레이를 최근접점 선 폭만큼 앞에서 멈춘다(뒤의 벽에 닿아 "가렸다"로 치지 않게).
        /// </summary>
        private bool IsUnobstructed(Vector3 eye, Vector3 target)
        {
            Vector3 delta = target - eye;
            float distance = delta.magnitude - _lineWidth;
            if (distance <= 1e-4f)
                return true;

            return !Physics.Raycast(eye, delta.normalized, distance, WallMask(), QueryTriggerInteraction.Ignore);
        }

        public void Remove(int pulseId)
        {
            if (!_reveals.TryGetValue(pulseId, out Reveal reveal))
                return;

            _reveals.Remove(pulseId);
            Release(reveal);
        }

        public void Clear()
        {
            foreach (Reveal reveal in _reveals.Values)
                Release(reveal);
            _reveals.Clear();
        }

        /// <summary>매 프레임: 링이 닿은 벽만 링의 현재 밝기로 켠다. 파문이 끝난 것은 스스로 정리한다.</summary>
        public void Tick(float now, Color color)
        {
            _removeScratch.Clear();

            foreach (KeyValuePair<int, Reveal> entry in _reveals)
            {
                Reveal reveal = entry.Value;
                if (now - reveal.StartTime >= reveal.Duration)
                {
                    _removeScratch.Add(entry.Key);
                    continue;
                }

                for (int w = 0; w < reveal.Walls.Count; w++)
                {
                    WallOutline wall = reveal.Walls[w];
                    float alpha = WallRevealTiming.Alpha(now, reveal.StartTime, reveal.Duration, wall.ArrivalTime);
                    bool visible = alpha > 0f;

                    Color c = color;
                    c.a = alpha;
                    for (int l = 0; l < LinesPerWall; l++)
                    {
                        LineRenderer line = reveal.Lines[wall.FirstLine + l];
                        line.enabled = visible;
                        if (!visible)
                            continue;

                        line.startColor = c;
                        line.endColor = c;
                    }
                }
            }

            for (int i = 0; i < _removeScratch.Count; i++)
                Remove(_removeScratch[i]);
        }

        private void BuildOutline(Collider wall, List<LineRenderer> lines)
        {
            Transform t = wall.transform;
            if (wall is BoxCollider box)
            {
                WallRevealTiming.BoxCorners(t.localToWorldMatrix, box.center, box.size, t.lossyScale,
                    _lineWidth * 0.5f, _corners);
            }
            else
            {
                // 맵 v2의 벽은 전부 BoxCollider다. 다른 모양이면 월드 AABB 윤곽으로 대신한다.
                Bounds b = wall.bounds;
                WallRevealTiming.BoxCorners(Matrix4x4.identity, b.center, b.size, Vector3.one,
                    _lineWidth * 0.5f, _corners);
            }

            AddOutlineLines(lines);
        }

        /// <summary><see cref="_corners"/>(상자 모서리 8개)를 선 6개로 — 앞 · 뒤 면의 닫힌 사각형 2개 + 잇는 선분 4개.</summary>
        private void AddOutlineLines(List<LineRenderer> lines)
        {
            // 앞(−z) · 뒤(+z) 면의 닫힌 사각형.
            for (int face = 0; face < 2; face++)
            {
                for (int k = 0; k < 4; k++)
                    _loop[k] = _corners[face * 4 + k];
                lines.Add(RentLine(_loop, loop: true));
            }

            // 두 면을 잇는 모서리 4개.
            for (int k = 0; k < 4; k++)
            {
                _segment[0] = _corners[k];
                _segment[1] = _corners[k + 4];
                lines.Add(RentLine(_segment, loop: false));
            }
        }

        private LineRenderer RentLine(Vector3[] points, bool loop)
        {
            LineRenderer line = _pool.Count > 0 ? _pool.Pop() : CreateLine();
            line.gameObject.SetActive(true);
            line.loop = loop;
            line.positionCount = points.Length;
            line.SetPositions(points);
            line.enabled = false; // 링이 닿을 때 Tick이 켠다
            return line;
        }

        private void Release(Reveal reveal)
        {
            for (int i = 0; i < reveal.Lines.Count; i++)
            {
                LineRenderer line = reveal.Lines[i];
                if (line == null)
                    continue;

                line.enabled = false;
                line.gameObject.SetActive(false);
                _pool.Push(line);
            }

            reveal.Lines.Clear();
            reveal.Walls.Clear();
            _revealPool.Push(reveal);
        }

        private LineRenderer CreateLine()
        {
            var go = new GameObject("PulseWallEdge");
            go.transform.SetParent(_parent, worldPositionStays: false);

            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = _material; // 확장 링과 같은 가산 합성 Unlit 재질(공유)
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View; // 세로 모서리도 어느 각도에서나 보이게
            line.textureMode = LineTextureMode.Stretch;
            line.widthMultiplier = _lineWidth;
            line.numCornerVertices = 0;
            line.enabled = false;
            return line;
        }

        private int WallMask()
        {
            if (_maskResolved)
                return _wallMask;

            _maskResolved = true;
            int soundBlocking = LayerMask.GetMask(PhysicsOcclusionProbe.SoundBlockingLayerName);
            _wallMask = soundBlocking == 0 ? ~0 : soundBlocking;
            return _wallMask;
        }
    }
}
