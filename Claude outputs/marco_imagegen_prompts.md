# 마르코! (MARCO!) — 컨셉아트 AI 이미지 프롬프트 모음

> `docs/마르코_상세기획서.md` §16(아트 디렉션)·§10~11(맵)·§7(아이템)·§12(UI) 기준으로 작성.
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

셋 다 **같은 저폴리 인체 실루엣**이어야 하고, 오직 림라이트 색으로만 구분된다. 별도 의상·장식 디자인은 없다(1인 개발 전략, §16.3).

### 1.1 도망자 (Runner) — 청록 `#7FE7E0`

```
[공통 스타일 프리픽스]
A single humanoid character silhouette made of matte pure-white low-poly
geometry, standing in a black void, with a thin rim light glowing teal
(#7FE7E0) tracing the edges of the body only, no face detail, no texture,
neutral standing pose, full body, front three-quarter view
```

### 1.2 술래 (Seeker) — 적색 `#EF6A4C`

```
[공통 스타일 프리픽스]
A single humanoid character silhouette made of matte pure-white low-poly
geometry, standing in a black void, with a thin rim light glowing red-orange
(#EF6A4C) tracing the edges of the body only, slightly more aggressive
forward-leaning stance than a neutral pose, no face detail, no texture,
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

52m×40m L자형 2층, 13구역. 아래는 핵심 구역만 뽑았다 — 그레이박스(ProBuilder 블록아웃) 단계 참고용 실루엣 컨셉이지, 완성 렌더가 아니다.

### 2.1 메인 풀 홀 (중앙 허브, 타일 ×1.3, 수중 밸브 B)

```
[공통 스타일 프리픽스]
Interior of a large abandoned indoor swimming pool hall at night, seen from
a first-person low angle, glowing tiled floor edges only, a dark still pool
of water in the center with faint ripple glow lines on the surface, tall
empty black space above, distant glowing outlines of pool-side railings,
oppressive emptiness, no people
```

### 2.2 기계실 (밸브 A, 콘크리트, 막다른 방, 출입구 1개)

```
[공통 스타일 프리픽스]
A small dead-end mechanical room, single doorway visible as a glowing
rectangular outline, glowing outlines of large pipes and machinery boxes
against pure black, claustrophobic, no other exits visible, first-person
low angle
```

### 2.3 물탱크실 (밸브 C, 2층, 콘크리트)

```
[공통 스타일 프리픽스]
An elevated water tank room on a second floor, large cylindrical tank
outlined in glowing white lines, a metal grating staircase glowing amber
(#FFB84D) leading up into the room, view looking down from the tank room
across a railing into darkness below, isolated and high up
```

### 2.4 직원 통로 (금속 그레이팅 ×1.5, 출구 1 접근로)

```
[공통 스타일 프리픽스]
A long narrow service corridor with a metal grating floor, floor rendered
as a glowing grid-line texture pattern (not filled, outline only), corridor
stretching far into black distance, faint glow at the far end suggesting an
exit door, first-person view down the corridor
```

### 2.5 로비 (스폰 지점, 카펫, 출구 2)

```
[공통 스타일 프리픽스]
A quiet building lobby entrance area, soft glowing outlines of a reception
desk and double front doors, carpeted floor suggested only by a subtly
softer/dimmer outline than surrounding tile areas, calm but empty, faint
glow escaping from the gap under the front doors
```

### 2.6 라커룸 (매트/카펫, 락커 미로)

```
[공통 스타일 프리픽스]
A maze of tall locker rows forming narrow blind corridors, each locker face
outlined in a thin glowing line, repeating grid pattern creating a maze-like
claustrophobic layout, deep black shadow between rows, first-person view
down one locker aisle
```

### 2.7 관람석 (2층, 카펫, 풀 홀 내려다보는 좌석열)

```
[공통 스타일 프리픽스]
Rows of empty stadium-style spectator seating on an upper floor balcony,
seat backs outlined in thin glowing lines receding into the distance, a
railing edge glowing at the front overlooking a dark void below (the pool
hall), view from behind the seats looking down
```

---

## 3. 맵 환경 — §11 폐극장 (v1.0 콘텐츠, 참고용 선반영)

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

### 5.2 로비 브리핑 — 맵 평면도 스타일 (§12.3, 라운드 시작 전 30초 표시)

```
[공통 스타일 프리픽스]
A minimalist top-down architectural floor plan diagram of an indoor
swimming pool facility, thin glowing white outlines for walls, distinct
colored dots for three valve locations in amber (#FFB84D), two exit
markers highlighted, clean blueprint-like schematic style, black background,
no character positions shown
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
| 배경 | `#000000` | §16.2 |
| 술래 파문 = 이중 테두리 / 도망자·환경 = 단일 테두리 | — | §16.4 |
| 잔상 밝기·지속 | 15%→0%, 12초 (차선책 8%, 8초) | §16.4 |

이 표에 없는 색을 새로 쓰고 싶으면 기획서 §16.2에 먼저 추가하고("정본은 하나만 둔다" 원칙), 그다음에 이 프롬프트 문서를 갱신할 것 — 순서를 바꾸면 아트와 문서가 또 어긋난다.
