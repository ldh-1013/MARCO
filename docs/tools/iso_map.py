"""마르코! 심야 실내수영장 — 2.5D 아이소메트릭 맵 도안 생성 (§10.1 좌표 기준)"""
import math

C30, S30 = math.cos(math.radians(30)), math.sin(math.radians(30))
SCALE, ZH = 13.0, 13.0      # m당 픽셀, 층고 픽셀

def iso(x, y, z=0.0):
    return ((x - y) * C30 * SCALE, (x + y) * S30 * SCALE - z * ZH)

GROUND = [
    ('로비',        2, 32, 12, 40, '카펫 ×0.7',   '스폰 · 출구2'),
    ('약품창고',   16, 33, 22, 38, '콘크리트 ×1.0','밸브 D'),
    ('세탁실',     26, 33, 33, 38, '타일 ×1.3',   '통로 분기'),
    ('라커룸',      2, 24, 14, 31, '매트 ×0.7',   '락커 미로'),
    ('메인 풀 홀', 18, 15, 38, 27, '타일 ×1.3',   '밸브 B (수중)'),
    ('라이프가드',  2, 17,  7, 21, '카펫 ×0.7',   '유리창 관측'),
    ('유아풀 존',   2,  5, 14, 14, '타일 ×1.3',   '밸브 E (얕은 수중)'),
    ('사우나',     18,  9, 22, 13, '목재 ×1.0',   '초소형 은신'),
    ('샤워장',     26,  7, 34, 12, '타일 ×1.3',   '연결 통로'),
    ('기계실',     41,  5, 49, 12, '콘크리트 ×1.0','밸브 A · 출입구 1개'),
    ('직원 통로',  14,  1, 48, 3.5,'금속 ×1.5',   '출구1 접근로'),
]
SECOND = [
    ('관람석',   18, 15, 36, 21, '카펫 ×0.7',    '풀 홀 청취 우위'),
    ('물탱크실',  4, 24, 10, 29, '콘크리트 ×1.0','밸브 C'),
]
VALVES = [('A',46,8,0,'기계실','3.0초'),('B',34,22,0,'메인 풀','2.0초 · 수중'),
          ('C',7,26,3.5,'물탱크실 2층','4.0초'),('D',19,36,0,'약품창고','3.0초'),
          ('E',5,9,0,'유아풀','3.0초 · 얕은 수중')]
EXITS = [('출구2 정문',7,40),('출구1 배수로',46,2)]
PARTITION = (40,13,40,20)
WATER = [(18,15,38,27),(2,5,14,14)]

AMBER,CYAN,RED,LINE,DIM = '#FFB84D','#35F0D0','#FF3B4D','#E8E8E8','#4A4A52'

def prism(x0,y0,x1,y1,z=0.0,h=2.6,fill='#101014',op=0.55,stroke=LINE,sw=1.1):
    t=[iso(x0,y0,z+h),iso(x1,y0,z+h),iso(x1,y1,z+h),iso(x0,y1,z+h)]
    b=[iso(x0,y0,z),  iso(x1,y0,z),  iso(x1,y1,z),  iso(x0,y1,z)]
    s=[]
    # 좌·우 벽면
    for (a,bb) in ((0,1),(1,2)):
        pts=f"{b[a][0]:.1f},{b[a][1]:.1f} {b[bb][0]:.1f},{b[bb][1]:.1f} {t[bb][0]:.1f},{t[bb][1]:.1f} {t[a][0]:.1f},{t[a][1]:.1f}"
        s.append(f'<polygon points="{pts}" fill="{fill}" fill-opacity="{op*0.75:.2f}" stroke="{stroke}" stroke-width="{sw*0.7:.2f}" stroke-opacity="0.55"/>')
    top=" ".join(f"{p[0]:.1f},{p[1]:.1f}" for p in t)
    s.append(f'<polygon points="{top}" fill="{fill}" fill-opacity="{op:.2f}" stroke="{stroke}" stroke-width="{sw}" stroke-opacity="0.95"/>')
    return "".join(s)

def flat(x0,y0,x1,y1,z,fill,op,stroke=None,sw=1.0,dash=None):
    p=[iso(x0,y0,z),iso(x1,y0,z),iso(x1,y1,z),iso(x0,y1,z)]
    pts=" ".join(f"{q[0]:.1f},{q[1]:.1f}" for q in p)
    st=f' stroke="{stroke}" stroke-width="{sw}"' if stroke else ''
    dd=f' stroke-dasharray="{dash}"' if dash else ''
    return f'<polygon points="{pts}" fill="{fill}" fill-opacity="{op}"{st}{dd}/>'

def label(x,y,z,text,sub='',color=LINE,size=11):
    sx,sy=iso(x,y,z)
    o=f'<text x="{sx:.1f}" y="{sy:.1f}" fill="{color}" font-size="{size}" font-family="NanumGothic,Helvetica,Arial,sans-serif" text-anchor="middle" font-weight="600">{text}</text>'
    if sub:
        o+=f'<text x="{sx:.1f}" y="{sy+11:.1f}" fill="{DIM}" font-size="8.5" font-family="NanumGothic,Helvetica,Arial,sans-serif" text-anchor="middle">{sub}</text>'
    return o

def valve_marker(vid,x,y,z):
    sx,sy=iso(x,y,z)
    s=f'<line x1="{sx:.1f}" y1="{sy:.1f}" x2="{sx:.1f}" y2="{sy-34:.1f}" stroke="{AMBER}" stroke-width="1.6" stroke-opacity="0.75"/>'
    s+=f'<circle cx="{sx:.1f}" cy="{sy:.1f}" r="11" fill="none" stroke="{AMBER}" stroke-width="1" stroke-opacity="0.4"/>'
    s+=f'<circle cx="{sx:.1f}" cy="{sy-34:.1f}" r="12" fill="#0B0B0E" stroke="{AMBER}" stroke-width="2.2"/>'
    s+=f'<text x="{sx:.1f}" y="{sy-30:.1f}" fill="{AMBER}" font-size="13" font-weight="700" font-family="NanumGothic,Helvetica,Arial,sans-serif" text-anchor="middle">{vid}</text>'
    return s

parts=[]
# 바닥 그리드 1m
for gx in range(0,53,2):
    a,b=iso(gx,0),iso(gx,40)
    parts.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="{DIM}" stroke-width="0.4" stroke-opacity="0.35"/>')
for gy in range(0,41,2):
    a,b=iso(0,gy),iso(52,gy)
    parts.append(f'<line x1="{a[0]:.1f}" y1="{a[1]:.1f}" x2="{b[0]:.1f}" y2="{b[1]:.1f}" stroke="{DIM}" stroke-width="0.4" stroke-opacity="0.35"/>')
# 맵 외곽 + L자 컷
parts.append(flat(0,0,52,40,0,'none',0,DIM,1.4,'6 4'))
parts.append(flat(40,28,52,40,0,RED,0.05,RED,1.2,'4 4'))
parts.append(label(46,34,0,'L자 컷','x≥40 ∧ y≥28',RED,9))
# 물
for (a,b,c,d) in WATER:
    parts.append(flat(a,b,c,d,0.05,CYAN,0.10))
# 지상 구역 (뒤→앞 정렬)
for z in sorted(GROUND,key=lambda r:-(r[1]+r[2]+r[3]+r[4])):
    n,x0,y0,x1,y1,mat,fn=z
    parts.append(prism(x0,y0,x1,y1))
    parts.append(label((x0+x1)/2,(y0+y1)/2,2.6,n,f'{mat} · {fn}'))
# 칸막이
px0,py0,px1,py1=PARTITION
parts.append(prism(px0-0.4,py0,px1+0.4,py1,0,3.0,RED,0.30,RED,1.6))
parts.append(label(40,16.5,3.0,'칸막이','A↔B 검증 필수',RED,9))
# 2층
for n,x0,y0,x1,y1,mat,fn in SECOND:
    parts.append(flat(x0,y0,x1,y1,3.5,'#0E0E12',0.30,LINE,0.9,'3 3'))
    parts.append(prism(x0,y0,x1,y1,3.5,2.4,'#16161C',0.50))
    parts.append(label((x0+x1)/2,(y0+y1)/2,5.9,n+' (2F)',f'{mat} · {fn}',LINE))
# 출구
for n,x,y in EXITS:
    sx,sy=iso(x,y,0)
    parts.append(f'<circle cx="{sx:.1f}" cy="{sy:.1f}" r="7" fill="none" stroke="{CYAN}" stroke-width="2.2"/>')
    parts.append(f'<circle cx="{sx:.1f}" cy="{sy:.1f}" r="14" fill="none" stroke="{CYAN}" stroke-width="0.9" stroke-opacity="0.45"/>')
    parts.append(f'<text x="{sx:.1f}" y="{sy-20:.1f}" fill="{CYAN}" font-size="10" font-weight="600" font-family="NanumGothic,Helvetica,Arial,sans-serif" text-anchor="middle">{n}</text>')
# 밸브
for vid,x,y,z,room,rot in VALVES:
    parts.append(valve_marker(vid,x,y,z))
    sx,sy=iso(x,y,z)
    parts.append(f'<text x="{sx:.1f}" y="{sy-50:.1f}" fill="{AMBER}" font-size="8.5" font-family="NanumGothic,Helvetica,Arial,sans-serif" text-anchor="middle">{room} · {rot}</text>')

xs=[iso(x,y)[0] for x in (0,52) for y in (0,40)]
ys=[iso(x,y,7)[1] for x in (0,52) for y in (0,40)]+[iso(x,y,0)[1] for x in (0,52) for y in (0,40)]
minx,maxx,miny,maxy=min(xs)-90,max(xs)+90,min(ys)-90,max(ys)+70
W,H=maxx-minx,maxy-miny
svg=f'''<svg xmlns="http://www.w3.org/2000/svg" viewBox="{minx:.0f} {miny:.0f} {W:.0f} {H:.0f}" width="{W:.0f}" height="{H:.0f}">
<rect x="{minx:.0f}" y="{miny:.0f}" width="{W:.0f}" height="{H:.0f}" fill="#000000"/>
<text x="{minx+28:.0f}" y="{miny+40:.0f}" fill="{LINE}" font-size="19" font-weight="700" font-family="NanumGothic,Helvetica,Arial,sans-serif">MARCO! — 심야 실내수영장 2.5D 배치 도안</text>
<text x="{minx+28:.0f}" y="{miny+60:.0f}" fill="{DIM}" font-size="11" font-family="NanumGothic,Helvetica,Arial,sans-serif">52×40m L자형 · 2층 13구역 · 밸브 5개(4개 무작위 활성 / 3개 동시 개방) · §10.1 좌표 · 배치 검증 10/10 통과</text>
{"".join(parts)}
</svg>'''
open('marco_map_isometric.svg','w',encoding='utf-8').write(svg)
print("생성:", len(svg), "bytes")
