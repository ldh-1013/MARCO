"""
마르코! 밸브 배치 — **좌표 후보 탐색용 보조 도구**.

  ★ 합격 판정은 이 스크립트가 하지 않는다. 판정 정본은
      Assets/_Project/Editor/MapV2ValidationTool.cs
      (에디터 메뉴 Tools/MARCO/맵 v2 배치 검증)
    이며, 생성된 씬의 실제 콜라이더를 §5.6 차폐 프로브로 직접 읽는다.

  이 스크립트가 정본이 될 수 없는 이유 네 가지 —
    · 벽을 1m 격자 셀로 근사한다(폭 2m 문이 3칸으로 뭉개진다)
    · §5.6 [v0.4] 수면 차폐 ×0.5와 풀 측벽을 모르므로 수중 밸브 B·E가 틀린다
    · 층간 바닥을 상수 2장으로 가정한다(계단 개구부를 반영하지 못한다)
    · 계단을 12m 페널티로 대신한다 — 실제 계단은 편도 6.3m라
      C↔D가 30.5m로 나오지만 실측은 21.8m다

  좌표 후보를 빠르게 훑는 용도로는 그대로 쓸 만하다. 후보를 고른 뒤에는
  반드시 C# 정본 툴로 재판정할 것.

기획서 §10.2-1을 자동 판정한다(근사).

  ★ 합격 조건은 ② 하나다. ①은 폐지됐다(10.2-1절 폐지 근거 3가지 참조) —
    이 스크립트는 참고용으로 경로 거리를 계속 출력하지만 판정에 쓰지 않는다.
  ① (폐지) 경로 30m 이상 — 기록용. 30m 미만은 "주의"일 뿐 불합격이 아니다
  ② 동시 감시 지점 0개 — 9.3-1절이 전제하는 청취 반경 14.4m를
     직접 검증한다. 임계 = 14.4 × (1 + 0.5^n), n = 두 밸브 사이 벽 수
  ③ 배수구 육상 거리 3.0m 이상 — 6.5-3절 수심 3.5m 규칙의 수평 짝.
     물가에 붙으면 술래가 마른 바닥에서 부상 지점을 태그한다.
     유아풀은 수면 폭 6m라 3.0m가 기하학적 상한이다(여유 0).

그레이박스 완성 후 ROOMS/DOORS/VALVES를 실제 좌표로 갱신해 재실행할 것.
좌표계: 원점 = 남서 귀퉁이, +x = 동, +y = 북, 단위 m (§10.1)

    python3 valve_layout_check.py
"""
import math, itertools
from collections import deque

# ── 기획서 수치 (§5.1 / §5.6 / §5.7 / §6.1) ──────────────────────────
VALVE_SOUND_RADIUS = 12.0   # §5.1 밸브 회전 소음 발생 반경
SEEKER_RADIUS_MULT = 1.2    # §5.7 술래 반경 청취 배율
WALL_ATTEN         = 0.5    # §5.6 벽 통과당 반경 배율
FLOOR_WALLS        = 2      # §5.6 층간 바닥 = 벽 2장 상당
STAIR_PENALTY      = 12.0   # 2층 계단 왕복 근사
MIN_PATH           = 30.0   # §10.2-1 ① 경로 거리 하한

# ── 배수구 (§6.5-2 / §6.5-3) ────────────────────────────────────────
TAG_RADIUS  = 1.2    # §6.5-3 태그 반경
MIN_LAND    = 3.0    # D-1 배수구 ↔ 육상 최단 거리 (태그 1.2 + 여유 1.8)
MIN_VALVE_D = 3.0    # D-2 배수구 ↔ 같은 구역 밸브 (상호작용 프롬프트 분리)

WATER = {            # §10.1 수면 영역 — 잠정(GAP-72)
    '메인풀홀':  (21, 17, 35, 25),
    '유아풀존':  (3.5, 6.5, 12.5, 12.5),
}
DRAINS = {           # §6.5-2
    '배수구1 메인풀': ('메인풀홀', (31, 21),  'B 메인풀'),
    '배수구2 유아풀': ('유아풀존', (8, 9.5),  'E 유아풀'),
}
SEEKER_HEAR = VALVE_SOUND_RADIUS * SEEKER_RADIUS_MULT   # 14.4m

W, H = 52, 40
CUT = lambda x, y: (x >= 40 and y >= 28)          # §10.1 L자 컷

ROOMS = {   # 이름: (x0,y0,x1,y1)  — §10.1
    '로비':      (2, 32, 12, 40), '약품창고': (16, 33, 22, 38),
    '세탁실':    (26, 33, 33, 38), '라커룸':   (2, 24, 14, 31),
    '물탱크실':  (4, 24, 10, 29),  '메인풀홀': (18, 15, 38, 27),
    '라이프가드': (2, 17, 7, 21),  '유아풀존': (2, 5, 14, 14),
    '사우나':    (18, 9, 22, 13),  '샤워장':   (26, 7, 34, 12),
    '기계실':    (41, 5, 49, 12),  '직원통로': (14, 1, 48, 3.5),
}
SECOND = {'물탱크실'}                              # 2층 구역

DOORS = [   # (구역, 문 중심, 폭)
    ('로비', (12, 36), 2), ('로비', (7, 32), 2),
    ('약품창고', (16, 35), 2), ('세탁실', (29, 33), 2),
    ('라커룸', (14, 27), 2), ('라커룸', (8, 24), 2),
    ('메인풀홀', (18, 20), 2), ('메인풀홀', (34, 27), 2), ('메인풀홀', (38, 24), 2),
    ('라이프가드', (7, 19), 2), ('유아풀존', (14, 10), 2), ('유아풀존', (8, 14), 2),
    ('사우나', (20, 13), 2), ('샤워장', (30, 12), 2),
    ('기계실', (45, 5), 2),                        # ★ 출입구 1개(남쪽) — §6.1 퇴로 없음
    ('직원통로', (14, 3.5), 2), ('직원통로', (30, 3.5), 2), ('직원통로', (44, 3.5), 2),
]
EXTRA_WALLS = [(40, y) for y in range(13, 21)]     # ★ 기계실–풀홀 칸막이

VALVES = {  # §10.2
    'A 기계실': (46, 8), 'B 메인풀': (34, 22), 'C 물탱크실(2층)': (7, 26),
    'D 약품창고': (19, 36), 'E 유아풀': (5, 9),
}


def build():
    walk = [[True] * H for _ in range(W)]
    for x in range(W):
        for y in range(H):
            if CUT(x, y):
                walk[x][y] = False
    walls = set()
    for name, (x0, y0, x1, y1) in ROOMS.items():
        if name in SECOND:
            continue                                # 2층은 평면 충돌에서 제외
        for x in range(int(x0), int(x1) + 1):
            walls.add((x, int(y0))); walls.add((x, int(y1)))
        for y in range(int(y0), int(y1) + 1):
            walls.add((int(x0), y)); walls.add((int(x1), y))
    for _, (dx, dy), wd in DOORS:                   # 문 뚫기
        for o in range(-wd // 2, wd // 2 + 1):
            walls.discard((int(dx + o), int(dy))); walls.discard((int(dx), int(dy + o)))
    for (x, y) in walls:
        if 0 <= x < W and 0 <= y < H:
            walk[x][y] = False
    for (x, y) in EXTRA_WALLS:
        if 0 <= x < W and 0 <= y < H:
            walk[x][y] = False
    return walk


def bfs(walk, src):
    d = {src: 0}; q = deque([src])
    while q:
        c = q.popleft()
        for dx, dy in ((1,0),(-1,0),(0,1),(0,-1),(1,1),(1,-1),(-1,1),(-1,-1)):
            n = (c[0] + dx, c[1] + dy)
            if not (0 <= n[0] < W and 0 <= n[1] < H): continue
            if not walk[n[0]][n[1]] or n in d: continue
            d[n] = d[c] + (1.414 if dx and dy else 1.0)
            q.append(n)
    return d


def wall_hits(a, b, walk, extra=0):
    """a→b 직선이 지나는 벽 덩어리 수 (+ 층간 바닥 보정)"""
    n = int(max(abs(b[0]-a[0]), abs(b[1]-a[1])) * 4) + 1
    hits, prev = 0, False
    for i in range(n + 1):
        t = i / n
        x = int(round(a[0] + (b[0]-a[0]) * t)); y = int(round(a[1] + (b[1]-a[1]) * t))
        blocked = not (0 <= x < W and 0 <= y < H) or not walk[x][y]
        if blocked and not prev: hits += 1
        prev = blocked
    return hits + extra


def audible(p, v, walk, extra):
    """술래가 p에서 밸브 v의 회전 소음을 듣는가 (§5.6 차폐 반영)"""
    return math.dist(p, v) <= SEEKER_HEAR * (WALL_ATTEN ** wall_hits(p, v, walk, extra))


def co_watch_spots(v1, v2, walk, e1, e2):
    """두 밸브를 동시에 감시 가능한 지점 목록 — 비어 있어야 합격"""
    out = []
    for x in range(W):
        for y in range(H):
            if not walk[x][y]: continue
            p = (x, y)
            if math.dist(p, v1) > SEEKER_HEAR or math.dist(p, v2) > SEEKER_HEAR:
                continue                            # 차폐 0이어도 못 듣는 거리
            if audible(p, v1, walk, e1) and audible(p, v2, walk, e2):
                out.append(p)
    return out


def land_distance(p, water):
    """배수구에서 물 밖(육상)까지의 최단 거리 — 수면 사각형 경계까지의 거리"""
    x0, y0, x1, y1 = water
    return min(p[0] - x0, x1 - p[0], p[1] - y0, y1 - p[1])


def check_drains():
    """§6.5-3 수평 거리 제약 판정. 밸브 검사와 독립이다."""
    print(f"\n{'배수구':20} {'육상거리':>8} {'밸브거리':>8}  판정")
    print("-" * 54)
    bad = 0
    for name, (room, p, valve) in DRAINS.items():
        w = WATER[room]
        ld = land_distance(p, w)
        vd = math.dist(p, VALVES[valve])
        ok = ld >= MIN_LAND and vd >= MIN_VALVE_D
        if not ok:
            bad += 1
        cap = min(w[2] - w[0], w[3] - w[1]) / 2      # 이 수면에서 가능한 최대 육상거리
        note = "  ← 수면 기하학적 상한" if abs(ld - cap) < 1e-6 else ""
        print(f"{name:20} {ld:8.2f} {vd:8.2f}  {'OK' if ok else 'FAIL'}{note}")
    print(f"\n배수구 불합격 {bad}개 / 총 {len(DRAINS)}개")
    if bad:
        print("→ 조정: ①배수구 좌표 ②수면 영역 확대 (§6.5-3, §10.1)")
    return bad


def main():
    walk = build()
    for name, p in VALVES.items():
        if not walk[int(p[0])][int(p[1])]:
            print(f"[경고] {name} {p} 가 벽 또는 맵 바깥에 있다")
    dist = {n: bfs(walk, p) for n, p in VALVES.items()}

    print(f"{'쌍':30} {'직선':>6} {'경로':>7} {'동시감시':>8}  판정")
    print("-" * 66)
    bad = 0
    for a, b in itertools.combinations(VALVES, 2):
        pa, pb = VALVES[a], VALVES[b]
        e1 = FLOOR_WALLS if '2층' in a else 0
        e2 = FLOOR_WALLS if '2층' in b else 0
        path = dist[a].get(pb, float('inf'))
        if e1 or e2: path += STAIR_PENALTY
        spots = co_watch_spots(pa, pb, walk, e1, e2)
        ok = path >= MIN_PATH and not spots
        if not ok:
            bad += 1
        reason = ""
        if not ok:
            parts = []
            if path < MIN_PATH: parts.append(f"경로 {path:.0f}m < {MIN_PATH:.0f}m")
            if spots: parts.append(f"동시감시 {spots[:3]}")
            reason = "  (" + ", ".join(parts) + ")"
        print(f"{a + ' ↔ ' + b:30} {math.dist(pa,pb):6.1f} {path:7.1f} {len(spots):8}  "
              f"{'OK' if ok else 'FAIL'}{reason}")
    print(f"\n불합격 {bad}쌍 / 총 {len(list(itertools.combinations(VALVES,2)))}쌍")
    if bad:
        print("→ 조정 순서: ①문 위치 ②칸막이 추가 ③밸브 좌표 ④구역 배치 (§10.2-1)")
    check_drains()


if __name__ == '__main__':
    main()
