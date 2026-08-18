from PIL import Image
import numpy as np

# 티어별 보정 강도.
# 초저녁(1) 밝음 → 자정(3) 가장 어두움 → 동틀녘(6) 다시 밝음
# 이 곡선이 6단계 진행을 밝기만으로도 읽히게 한다.
GAMMA = {1:0.70, 2:0.71, 3:0.74, 4:0.72, 5:0.88, 6:0.74}

# 검은 영역이 완전한 검정이 되지 않게 들어올릴 바닥색.
# 순수 검정 대신 짙은 남색을 쓰면 밤 분위기를 유지하면서 형태가 보인다.
LIFT_COLOR = np.array([0x1A, 0x25, 0x40], dtype=float)
LIFT = {1:0.24, 2:0.30, 3:0.32, 4:0.28, 5:0.14, 6:0.24}

def brighten(path, tier, out):
    im = Image.open(path).convert('RGB')
    a = np.array(im).astype(float) / 255.0

    # 1) 감마로 어두운 쪽을 연다. 밝은 쪽은 거의 안 건드린다.
    a = np.power(a, GAMMA[tier])

    # 2) 바닥 들어올리기 — 어두울수록 강하게 적용해
    #    밝은 영역(달·등불·여명)은 그대로 둔다.
    lum = 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
    weight = np.clip(1.0 - lum*2.2, 0, 1)[..., None]   # 어두운 곳에만
    a = a + (LIFT_COLOR/255.0 - a) * weight * LIFT[tier]

    # 3) 대비를 살짝 회복 (들어올리면 밋밋해지므로)
    a = np.clip((a - 0.5) * 1.06 + 0.5, 0, 1)

    Image.fromarray((a*255).astype(np.uint8), 'RGB').save(out)

def stats(path):
    a = np.array(Image.open(path).convert('RGB')).astype(float)
    l = 0.2126*a[...,0] + 0.7152*a[...,1] + 0.0722*a[...,2]
    return l.mean(), np.median(l), (l<40).mean(), (l<25).mean()

print(f"{'티어':4}{'평균':>16}{'중앙값':>16}{'40미만':>16}{'25미만':>16}")
for i in range(1,7):
    src=f'tier_{i}_background.png'; dst=f'fixed_tier_{i}.png'
    b=stats(src); brighten(src,i,dst); a=stats(dst)
    print(f"{i:<4}{b[0]:6.1f} → {a[0]:5.1f}{b[1]:8.1f} → {a[1]:5.1f}"
          f"{b[2]:9.0%} → {a[2]:4.0%}{b[3]:9.0%} → {a[3]:4.0%}")
