# 마르코! (MARCO!) — 컨셉아트 AI 이미지 프롬프트 모음

> `docs/마르코_상세기획서.md` §16(아트 디렉션)·§10(맵 — v0.6, 좌표 정본 `docs/맵설계.md`)·§7(아이템)·§12(UI) 기준으로 작성.
> ChatGPT(DALL·E)·Gemini(Imagen)·Midjourney 등 범용 텍스트→이미지 툴에 그대로 붙여넣도록 영어로 작성했고, 각 항목 위에 한국어로 "이게 뭔지 / 왜 이런지"를 먼저 설명한다.
> 툴마다 결과가 다르니, 스타일이 안 맞으면 "공통 스타일 프리픽스"만 남기고 나머지를 조정할 것.

---

## 사용법

1. 각 프롬프트 앞의 **공통 스타일 프리픽스**를 항상 붙인다 — 이게 흩어지면 전체 그림체가 제각각이 된다.
2. `[HEX]`로 표시된 색은 §16.1/16.2 원문 값이다. 절대 임의로 바꾸지 말 것 — 바꾸려면 기획(마스터리) 확인 먼저.
3. 결과물이 텍스처·디테일이 과한 "예쁜 3D 렌더"로 나오면 실패다. 이 게임의 아트는 **완전한 흑 배경 위 발광선**이 전부이며, 저사양 1인 개발 파이프라인 전제라 실제 인게임 비주얼과 컨셉아트의 괴리가 크면 안 된다. 컨셉아트는 "이 실루엣이 그 라인아트가 됐을 때 읽히는가"를 확인하는 용도로 써라.
4. 색맹 모드 대체 팔레트는 별도 표기가 없으면 기본 팔레트로 작업한다.

---

## 공통 스타일 프리픽스 (모든 프롬프트 앞에 붙일 것)

```
Ultra-minimalist horror game concept art, pure black background (#000000),
thin glowing neon linework only, no texture, no fill shading except the
character silhouette itself, monochrome base with a single accent color,
low-poly geometry aesthetic, high contrast, negative space dominant,
clean vector-like glow lines, subtle bloom on the glow, no environment
lighting other than the glow sources, top-down horror party game concept art
```

**공통 네거티브 프롬프트** (지원하는 툴에서 사용):
```
photorealistic, painterly, textured surfaces, warm ambient lighting,
cluttered background, multiple light sources, realistic skin, cartoon
outline style, cel-shading, anime style, bright colorful palette
```

---

## 1. 캐릭터 — §16.1 "무광 순백색 저폴리 3D + 역할별 림라이트"

셋 다 **같은 저폴리 인체 실루엣**이어야 하고, 오직 림라이트 색으로만 구분된다. 별도 의상·장식 디자인은 없다(1인 개발 전략, §16.3). **술래는 무기·소품을 들지 않는다** — 공격은 맨손 수평 후려치기다(§16.1 · `연출.md` 15.1). 어떤 프롬프트에도 무기·막대·그물을 넣지 말 것.

### 1.1 도망자 (Runner) — 청록 `#7FE7E0`

```
[공통 스타일 프리픽스]
A single humanoid character silhouette made of matte pure-white low-poly
geometry, standing in a black void, with a thin rim light glowing teal
(#7FE7E0) tracing the edges of the body only, no face detail, no texture,
neutral standing pose, full body, front three-quarter view
```

### 1.2 술래 (Seeker) — 적색 `#EF6A4C`

두 팔을 앞으로 뻗고 허공을 더듬는 마르코폴로 자세(`연출.md` 15.7). 손에는 아무것도 들지 않는다.

```
[공통 스타일 프리픽스]
A single humanoid character silhouette made of matte pure-white low-poly
geometry, standing in a black void, with a thin rim light glowing red-orange
(#EF6A4C) tracing the edges of the body only, both arms stretched straight
forward at shoulder height with open hands, groping blindly like a Marco
Polo player, empty-handed with no weapon or prop, no face detail, no texture,
full body, front three-quarter view
```

- 참고: §16.1 "고함 시 실루엣 하이라이트" — 원한다면 위 프롬프트에 `with the entire silhouette briefly flaring brighter red, mid-shout pose, head tilted back` 를 추가한 "고함 순간" 버전도 따로 뽑아라.

### 1.3 메아리 (Echo) — 옅은 보라 `#B79CFF`, 반투명

```
[공통 스타일 프리픽스]
A single humanoid character silhouette made of translucent semi-transparent
white low-poly geometry, ghost-like, standing in a black void, with a soft
faint rim light glowing pale purple (#B79CFF) tracing the edges of the body,
the body slightly see-through revealing faint internal glow, floating
slightly above the ground with no visible legs contact, ethereal, no face
detail, full body, front three-quarter view
```

- 주의: 메아리는 §16.1상 **생존자에게는 렌더링되지 않는 존재**다. 이 이미지는 UI·마케팅·팀 내부 참고용이지, "생존자 시점에 보이는 모습"이 아니라는 걸 팀원들에게 설명해둘 것.

### 1.4 3역할 비교 시트 (팀 내부 레퍼런스용)

```
[공통 스타일 프리픽스]
Character reference sheet, three identical low-poly humanoid silhouettes
standing side by side in a black void, same neutral pose, left figure with
teal rim light (#7FE7E0) labeled "RUNNER", center figure with red-orange
rim light (#EF6A4C) labeled "SEEKER", right figure translucent with pale
purple rim light (#B79CFF) labeled "ECHO", clean comparison layout,
consistent scale
```

---

## 2. 맵 환경 — §10 심야 실내수영장 (MVP)

64m × 62m 2층, 21구역(기획서 §10, 좌표 · 도면은 `docs/맵설계.md`). 실제 25m 공공 수영장의 세 동선(이용객 · 관람객 · 직원)을 뼈대로 배치한 맵이다. 아래는 핵심 8곳만 뽑았다 — 블록아웃 단계 참고용 실루엣 컨셉이지, 완성 렌더가 아니다.

### 2.1 메인 풀 홀 (맵의 중심, 타일 ×1.3, 수중 밸브 B · E, 천장 9m)

경영풀(25 × 21m, 8레인)과 교습풀(12 × 8m)이 한 홀에 있다. 데크에는 출발대 · 레인 로프 릴 · 구조원 감시 의자 · 벤치 같은 풀 비품이 흩어져 엄폐물이 된다. 남쪽 장변 위로는 2층 관람석이 물러나 있다(풀 위에 걸치지 않는다).

```
[공통 스타일 프리픽스]
Interior of a very large indoor swimming pool hall at night with a 9-meter
high ceiling, seen from a first-person low angle on the pool deck, a long
8-lane lap pool and a smaller shallow teaching pool side by side, dark still
water with faint ripple glow lines, glowing outlines of starting blocks,
lane-rope reels, tall lifeguard chairs and wall benches scattered on the
deck, a tiered spectator stand set back behind one long side of the pool,
vast empty black space above, oppressive emptiness, no people
```

### 2.2 관람석 (2층, 카펫, 경영풀 남측 장변의 계단식 좌석)

좌석 단 사이로는 걸을 수 없고 ㄷ자 통로로만 다닌다. 앞 난간이 수면에서 3m 뒤에 있어 풀을 내려다보지만 풀 위로 나와 있지 않다. 데크로 내려가는 길은 서쪽 끝 비상계단 하나뿐이다.

```
[공통 스타일 프리픽스]
Tiered empty spectator seating along the long side of an indoor pool,
seen from the back aisle of the upper stand, stepped seat rows outlined in
thin glowing lines, a narrow walkway running in front of the seats behind a
glowing railing, the dark pool surface visible below and a few meters
beyond the railing, a single narrow emergency staircase at the far end
leading down to the deck, high and exposed
```

### 2.3 남 · 여 탈의실 (매트 ×0.7, 벽 부착 락커 빗살 8열)

락커는 벽에 붙인 빗살형으로 늘어서 있고 가운데가 탈의 공간이다. 락커 열 사이의 좁은 틈이 숨을 자리다 — 락커는 벽처럼 소리와 태그를 막는다. 미로가 아니다.

```
[공통 스타일 프리픽스]
A changing room with rows of tall lockers attached to both side walls like
comb teeth, each locker face outlined in a thin glowing line, narrow dark
gaps between the locker rows, an open changing space with a low bench down
the middle, two doorways at opposite ends, first-person view from the
central space looking into one gap between locker rows
```

### 2.4 남 · 여 샤워실 (타일 ×1.3, 탈의실과 데크 사이 필수 통과)

양쪽 벽에 샤워 부스가 늘어선 통과 공간이다. 탈의실 쪽 개구부와 데크 쪽 넓은 개구부(4m)가 서로 엇갈려 있어 데크에서 탈의실이 정면으로 보이지 않는다.

```
[공통 스타일 프리픽스]
A shower room corridor with rows of open shower stalls along both walls,
stall partitions outlined in glowing lines, wet tiled floor suggested by
faint reflective glow, a wide open doorway at the far end offset to one
side and opening onto a dark pool deck, the entrance from the changing room
offset the other way, first-person view walking through
```

### 2.5 기계실 (밸브 A, 콘크리트, 막다른 방, 출입구 1개)

여과기 · 배관 헤더 · 순환 펌프 · 밸런스 탱크가 들어찬 넓은 설비실이다. 문은 하나뿐이고, 기계 소음 때문에 안에서 돌리는 밸브 소리가 문 밖까지 새지 않는다.

```
[공통 스타일 프리픽스]
A large dead-end pool filtration plant room, a single double doorway
visible behind as a glowing rectangular outline, glowing outlines of three
large filter tanks, a pipe header along the far wall, rows of circulation
pumps and a concrete balance tank, a valve wheel on the pipe header,
claustrophobic despite its size, no other exits, first-person low angle
```

### 2.6 직원 통로 (금속 그레이팅 ×1.5, 가장 빠르고 가장 시끄러운 길)

직원 구역 · 직원 계단 · 약품 저장실 · 기계실 · 하역장 · 홀 비상문을 잇는 2m 폭 통로다. 중간에서 한 번 꺾여 끝까지 한 번에 보이지 않는다.

```
[공통 스타일 프리픽스]
A narrow 2-meter-wide service corridor with a metal grating floor, floor
rendered as a glowing grid-line pattern (outline only), several plain
doors along the walls outlined in thin glowing lines, the corridor jogging
sideways once in the distance so the end is hidden, first-person view down
the corridor
```

### 2.7 하역장 · 직원 출입구 (콘크리트, 출구 1)

약품 · 장비 반입구. 북쪽 벽의 하역 셔터가 출구 1이고, 홀로 들어가는 장비 반입 양개문이 함께 있다. 게이트가 열리면 출구 문짝이 약한 초록으로 빛난다.

```
[공통 스타일 프리픽스]
A concrete loading bay at the back of a pool facility, a wide roller
shutter door outlined in glowing lines with a faint green glow (#40A669)
on the door panel, stacked pallets along one wall, a wide double door on
the side leading into the dark pool hall, empty and echoing, first-person
view
```

### 2.8 2층 관람 로비 · 학부모 대기 (카펫 ×0.7)

주계단으로 올라오는 2층 허브다. 관람석 입구 두 곳과 물탱크실 · 관리 복도 문이 양끝에 있고, 매점 카운터 · 자판기 · 정수기 · 대기 벤치 · 화분이 엄폐물이 된다.

```
[공통 스타일 프리픽스]
A long upper-floor spectator lobby and parents' waiting area, glowing
outlines of a small snack counter, vending machines, a water dispenser,
low waiting benches and two large potted plants, the top of a wide main
staircase arriving in the middle, two openings into the spectator stand
along one wall, doors at both far ends, quiet carpeted space, first-person
view
```

---

## 3. 맵 환경 — §11 폐극장 (v1.0 콘텐츠, 참고용 선반영) — 폐기

> **폐기 — 기획서 §11 결번, 참고용 보관.** 아래 본문 프롬프트는 그대로 둔다.

MVP 범위는 아니지만, 방향성을 미리 확보해두면 디자이너가 나중에 급하게 작업하지 않아도 된다.

### 3.1 무대 (커튼 = 파문 완전 차단막, HardBlocker)

```
[공통 스타일 프리픽스]
An abandoned theater stage seen from the audience side, massive heavy
curtains hanging fully closed across the stage rendered as thick glowing
double-outlined fabric folds (visually distinct from thin single-outline
walls elsewhere), curtains implying they completely block anything behind
them, empty stage floor, oppressive scale
```

### 3.2 무대 아래 지하통로 (나무 마루, 밸브 3, 은신 불가 — 하이리스크 구역)

```
[공통 스타일 프리픽스]
A cramped low-ceiling underground passage beneath a theater stage, wooden
plank floor outlined with individual creaking board lines (visually
emphasized as "loud" flooring, slightly warm-tinted glow #FFB84D compared
to the cool white elsewhere), narrow, no alcoves or hiding spots visible,
oppressive and exposed
```

### 3.3 조명실/그리드 (밸브 2, 수직 구조, 진입로 1개, 고립 리스크)

```
[공통 스타일 프리픽스]
A vertical theater lighting grid catwalk structure, thin metal ladders and
narrow walkways glowing against black void, extreme verticality, a single
visible entry ladder at the bottom with no other visible connections,
vertigo-inducing isolation, view looking up the shaft
```

---

## 4. 이펙트 · 오브젝트

### 4.1 SoundPulse 파문 — 등급별 (§5.1/§16.2)

**이동 소리(발소리)는 링을 그리지 않는다** — 링은 목소리 · 행동 파문에만 쓴다(기획서 §5.5-1). 발소리 링 이미지는 만들지 말 것.

링 표시 파문은 차폐가 없을 때 발생원의 그 순간 실루엣을 함께 드러낸다(기획서 §5.5-2 발생원 스냅샷).

도망자/환경음은 **단일 테두리**, 술래는 **이중 테두리**(§16.4 형태적 구분) — 반드시 반영할 것.

```
[공통 스타일 프리픽스]
A single circular sound pulse ring expanding outward on a pure black
background, thin single-outline glowing ring, cyan color (#35F0D0), soft
outer glow fading to transparent at the edge, minimal, top-down view,
isolated on black
```

```
[공통 스타일 프리픽스]
A single circular sound pulse ring expanding outward on a pure black
background, DOUBLE concentric outline (two thin glowing rings close
together) to visually distinguish it as seeker-originated, red color
(#FF3B4D), soft outer glow fading to transparent at the edge, top-down
view, isolated on black
```

### 4.2 잔상 (Afterglow) — §16.4

```
[공통 스타일 프리픽스]
Faint glowing outlines tracing only the edges of environmental geometry
(door frames, wall corners, furniture edges) against pure black, no filled
surfaces, very low opacity (~15%) white glow, ghostly afterimage quality as
if fading out, no characters or objects shown, only architectural edges
```

### 4.3 찰칵이 (아이템 아이콘) — §7, 1회용 소모품

```
[공통 스타일 프리픽스]
A simple minimalist icon of a small vintage disposable camera, thin white
glowing outline only, no fill, no texture, flat icon style suitable for a
game HUD item slot, centered composition, single object on black background
```

### 4.4 찰칵이 섬광 이펙트

```
[공통 스타일 프리픽스]
A brief bright camera flash effect, a soft white radial burst of light
expanding from a point and instantly fading, no ring outline (this is a
silent flash of light, not a sound pulse), illuminating only the geometric
edges of nearby environment for an instant, top-down view, isolated on
black
```

### 4.5 (설계 검토 중) 긴급 탈출구 — "1인 탈출" 기믹용 오브젝트

> 아직 기획 확정 전(이번 대화의 제안안)이라, 확정되면 이름과 형태가 바뀔 수 있다. 우선 대기/활성 두 상태만 뽑아둔다.

```
[공통 스타일 프리픽스]
A sealed ventilation hatch set into a wall, rendered as a dim, barely
visible thin outline (much dimmer than normal interactive objects — nearly
invisible in the dark), inactive/locked state, no glow emphasis, blends
into the environment
```

```
[공통 스타일 프리픽스]
The same ventilation hatch now active, outlined in a bright pulsing amber
glow (#FFB84D) with small mechanical detail lines visible, clearly readable
as an interactive object now, single light source in an otherwise dark
corridor
```

---

## 5. UI/UX 컨셉 — §12

### 5.1 타이틀 화면 무드보드

```
[공통 스타일 프리픽스]
Game title screen mood board, pure black background, a single expanding
sound pulse ring glowing white-cyan at the center, a faint humanoid
silhouette barely visible at the edge of the ring's light, large negative
space, minimalist typography placeholder area at the bottom, ominous and
quiet
```

### 5.2 로비 브리핑 — 맵 평면도 스타일 (§12.4, 라운드 시작 전 30초 표시)

```
[공통 스타일 프리픽스]
Two side-by-side minimalist top-down architectural floor plan diagrams of
an indoor swimming pool facility, ground floor and partial upper floor,
thin glowing white outlines for walls, a large central pool hall with two
pools, distinct colored dots for three valve locations in amber (#FFB84D),
two exit markers in green (#40A669), clean blueprint-like schematic style,
black background, no character positions shown
```

### 5.3 인게임 HUD 무드보드 (§12.4)

```
[공통 스타일 프리픽스]
In-game HUD mockup overlay on a black background, minimal UI elements only:
a small round item slot icon bottom-right, a thin directional gauge arc
element top-of-screen barely visible, small escape progress pips
top-center ("● ●" style dots), no large panels, no bright colors except
functional accents, extremely sparse and unobtrusive design
```

### 5.4 결과 화면 (§12.5)

```
[공통 스타일 프리픽스]
Post-match result screen mockup, pure black background, three small glowing
character silhouette icons in a row (teal, red, pale purple) each with a
minimal stat readout beneath, a single large glowing headline area at the
top reserved for "RUNNERS WIN" or "SEEKER WINS" text, calm and clean,
no clutter
```

---

## 6. 마케팅 / 스토어 자산

### 6.1 스팀 캡슐 아트 (§20 원문: "어둠 속 비명 파문 한가운데의 붉은 실루엣")

```
[공통 스타일 프리픽스]
Key art composition: a single red-orange (#EF6A4C) glowing humanoid
silhouette standing at the exact center of an expanding circular sound
pulse ring, the ring rendered in white glowing linework, everything else
pure black void, extreme negative space, the silhouette small within the
frame to emphasize isolation and dread, portrait or landscape composition
suitable for a store capsule image, no text
```

### 6.2 로고/마스코트 방향성 (참고용, 확정 아님)

```
[공통 스타일 프리픽스]
Abstract logo concept exploration: a minimalist circular sound wave motif
that could double as a logomark, thin concentric glowing rings of varying
opacity, could incorporate a subtle exclamation mark negative space (game
title is "MARCO!"), monochrome white glow on black, multiple small
variations in a grid for logo exploration
```

---

## 참고 — 임의로 바꾸면 안 되는 값

| 항목 | 값 | 출처 |
|---|---|---|
| 도망자 림라이트 | `#7FE7E0` | §16.1 |
| 술래 림라이트 | `#EF6A4C` | §16.1 |
| 메아리 림라이트 | `#B79CFF` (반투명) | §16.1 |
| 도망자(파문/UI) | `#35F0D0` | §16.2 |
| 술래(파문/UI) | `#FF3B4D` | §16.2 |
| 상호작용 오브젝트(앰버) | `#FFB84D` | §16.2 |
| 출구 오브젝트(초록) | `#40A669` | §16.2 — 문 형태 윤곽으로 구분, 시안과 색상 근접 |
| 배경 | `#000000` | §16.2 |
| 술래 파문 = 이중 테두리 / 도망자·환경 = 단일 테두리 | — | §16.4 |
| 잔상 밝기·지속 | 15%→0%, 12초 (차선책 8%, 8초) | §16.4 |

이 표에 없는 색을 새로 쓰고 싶으면 기획서 §16.2에 먼저 추가하고("정본은 하나만 둔다" 원칙), 그다음에 이 프롬프트 문서를 갱신할 것 — 순서를 바꾸면 아트와 문서가 또 어긋난다.
