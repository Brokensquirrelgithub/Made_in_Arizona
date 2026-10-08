"""Builds the 'too steep to climb' cliff-face PBR set from the licensed Outdoor Ground Textures.

ground11 (horizontally layered strata) and ground14 (flaky red rock) supply the photographic grain. On top: bands of
sandstone of varied thickness and colour, each stepping back under a protruding lip (a staircase of ledges), vertical
joint fractures, spalled patches and dark desert varnish streaking down from the ledges, as on Arizona cliffs.
Everything is periodic, so the maps tile. Outputs 1024x1024 albedo (sRGB), height, normal (tangent space, +Y up) and
ambient occlusion. Usage: python Tools/cliff_texture.py [output folder] (needs numpy and Pillow).
"""
import os, sys
import numpy as np
from PIL import Image

project = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..')
src = os.path.join(project, 'Assets', 'ADG_Textures', 'ground_vol1')
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(project, 'Assets', 'MadeInArizona', 'Resources', 'Terrain')
N = 1024
rng = np.random.default_rng(1170)

def load(name, mode='RGB'):
    im = Image.open(os.path.join(src, name)).convert(mode).resize((N, N), Image.LANCZOS)
    return np.asarray(im).astype(np.float32) / 255.0

strata = load('ground11/ground11_Diffuse.tga')
strataH = load('ground11/ground11_Height.tga', 'L')
strataN = load('ground11/ground11_Normal.tga')
flaky = load('ground14/ground14_Diffuse.tga')
flakyH = load('ground14/ground14_Height.tga', 'L')

y, x = np.mgrid[0:N, 0:N].astype(np.float32) / N  # y = rows (v, vertical on the face), x = u (horizontal)

def periodic_noise(fx, fy, seed, octaves=4, gain=.5):
    """Tileable fractal noise from random-phase sinusoids on integer frequencies."""
    r = np.random.default_rng(seed)
    total = np.zeros((N, N), np.float32); amp = 1.0; norm = 0
    for o in range(octaves):
        layer = np.zeros((N, N), np.float32)
        for k in range(6):
            kx = int(r.integers(1, fx * 2 ** o + 1)) * r.choice([-1, 1]); ky = int(r.integers(0, fy * 2 ** o + 1))
            ph = r.uniform(0, 2 * np.pi)
            layer += np.cos(2 * np.pi * (kx * x + ky * y) + ph)
        total += layer / 6 * amp; norm += amp; amp *= gain
    t = total / norm
    return (t - t.min()) / (t.max() - t.min())

# --- Strata: bands of varied thickness, each stepping back a little (a staircase of ledges) ---------------------------
wobble = (periodic_noise(3, 1, 11, 3) - .5) * .03 + (periodic_noise(9, 2, 12, 2) - .5) * .008
cuts = np.sort(rng.uniform(0, 1, 12))
yy = (y + wobble) % 1.0
index = np.searchsorted(cuts, yy) % len(cuts)            # band under each pixel
top = cuts[index - 1]                                    # band starts at the previous cut (rows grow downward)
size = (cuts[index] - top) % 1.0
size[size == 0] = 1
t = ((yy - top) % 1.0) / size                            # 0 at the band's top edge, 1 at its foot
lip = np.exp(-t / .07)                                   # the protruding lip at each band's top
undercut = np.exp(-(1 - t) / .05)                        # shadowed recess under the next lip
bandRelief = 1 - .45 * t + .25 * lip - .35 * undercut
# Vertical joints: a few wandering cracks, each running part of the height.
joints = np.zeros((N, N), np.float32)
for j, u0 in enumerate(rng.uniform(0, 1, 5)):
    drift = (periodic_noise(1, 3, 40 + j, 3) - .5) * .06
    d = np.abs(((x - u0 - drift) + .5) % 1.0 - .5)
    width = rng.uniform(.004, .008)
    run = np.clip(np.sin(2 * np.pi * (y * rng.integers(1, 3) + rng.uniform(0, 1))) * 1.6 + .5, 0, 1)
    joints = np.maximum(joints, np.exp(-(d / width) ** 2) * run)
spall = np.clip((periodic_noise(10, 6, 66, 3) - .62) / .15, 0, 1)   # flaked-off patches
fineCracks = np.exp(-((periodic_noise(24, 6, 77, 2) - .5) / .02) ** 2) * .3
height = .3 * strataH + .15 * flakyH + .55 * (bandRelief - bandRelief.min()) / (bandRelief.max() - bandRelief.min())
height = height - .5 * joints - .1 * fineCracks - .12 * spall
height = (height - height.min()) / (height.max() - height.min())

# --- Albedo ---------------------------------------------------------------------------------------------------------
lum = lambda c: c[..., 0] * .299 + c[..., 1] * .587 + c[..., 2] * .114
grain = lum(strata) * .55 + lum(flaky) * .45
grain = (grain - grain.mean()) / grain.std()
palette = np.array([[.86, .76, .6], [.8, .62, .43], [.76, .48, .29], [.62, .32, .19], [.5, .28, .19], [.83, .69, .5]], np.float32)
bandColor = palette[rng.integers(0, len(palette), len(cuts))]
albedo = bandColor[index]
# Within a band: a little paler at the weathered lip, and the photo grain for texture.
albedo = albedo * (1 + .08 * lip[..., None]) * (1 + .13 * grain[..., None])
albedo = albedo * .85 + strata * .15 * (albedo.mean() / strata.mean())
# Desert varnish: dark streaks that start under ledges and fade down the band, heavier on some bands.
columns = periodic_noise(30, 1, 91, 3)
columns = np.clip((columns - .5) / .18, 0, 1) * (.4 + .6 * periodic_noise(5, 1, 92, 2))
bandVarnish = rng.uniform(.2, 1, len(cuts))[index]
streak = columns * np.exp(-t * 2.2) * bandVarnish
varnish = np.array([.17, .12, .1], np.float32)
albedo = albedo * (1 - .7 * streak[..., None]) + varnish * .7 * streak[..., None]
# Fresh rock where flakes spalled off is paler; cracks and recesses are dark.
albedo = albedo * (1 - spall[..., None] * .15) + np.array([.9, .74, .56], np.float32) * spall[..., None] * .15
albedo *= np.clip(1 - joints * .8 - fineCracks * .35 - undercut * .3, 0, 1)[..., None]
albedo = np.clip(albedo, 0, 1)

# --- Normal from height (wrapping), plus the photo normal's fine detail ----------------------------------------------
strength = 9.0
dx = (np.roll(height, -1, 1) - np.roll(height, 1, 1)) * .5 * strength
dy = (np.roll(height, -1, 0) - np.roll(height, 1, 0)) * .5 * strength
nx = -dx + (strataN[..., 0] * 2 - 1) * .35
ny = dy + (strataN[..., 1] * 2 - 1) * .35  # image rows run down; OpenGL-style normal maps have +Y up
nz = np.ones_like(nx)
length = np.sqrt(nx * nx + ny * ny + nz * nz)
normal = np.stack([nx / length, ny / length, nz / length], -1) * .5 + .5

# --- Ambient occlusion: cavities relative to the local average height -------------------------------------------------
blur = height.copy()
for k in range(6):
    blur = (np.roll(blur, 1, 0) + np.roll(blur, -1, 0) + np.roll(blur, 1, 1) + np.roll(blur, -1, 1) + blur * 4) / 8
for k in range(3):
    blur = (np.roll(blur, 4, 0) + np.roll(blur, -4, 0) + np.roll(blur, 4, 1) + np.roll(blur, -4, 1) + blur * 4) / 8
ao = np.clip(1 - np.clip(blur - height, 0, 1) * 3.5 - joints * .45 - undercut * .25, .25, 1)

os.makedirs(out, exist_ok=True)
save = lambda a, name, mode: Image.fromarray((np.clip(a, 0, 1) * 255 + .5).astype(np.uint8), mode).save(os.path.join(out, name))
save(albedo, 'CliffFace_Diffuse.png', 'RGB')
save(height, 'CliffFace_Height.png', 'L')
save(normal, 'CliffFace_Normal.png', 'RGB')
save(ao, 'CliffFace_Occlusion.png', 'L')
print('written', out)
