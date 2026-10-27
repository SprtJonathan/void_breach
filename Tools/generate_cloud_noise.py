"""Bake periodic Perlin/Worley volume and independent weather data (NumPy, Pillow)."""
from pathlib import Path
import numpy as np
from PIL import Image

SIZE = 128
rng = np.random.default_rng(1247)
z, y, x = np.mgrid[:SIZE, :SIZE, :SIZE].astype(np.float32)


def worley(cells):
    features = rng.random((cells, cells, cells, 3), dtype=np.float32)
    coords = np.stack((z, y, x), axis=-1) * cells / SIZE
    base = np.floor(coords).astype(int)
    nearest = np.full(x.shape, np.inf, dtype=np.float32)
    for dz in (-1, 0, 1):
        for dy in (-1, 0, 1):
            for dx in (-1, 0, 1):
                cell = base + (dz, dy, dx)
                wrapped = cell % cells
                point = features[wrapped[..., 0], wrapped[..., 1], wrapped[..., 2]] + cell
                nearest = np.minimum(nearest, np.sum((point - coords) ** 2, axis=-1))
    return np.clip(np.sqrt(nearest) / 1.15, 0, 1)


def perlin(cells):
    gradients = rng.normal(size=(cells, cells, cells, 3)).astype(np.float32)
    gradients /= np.linalg.norm(gradients, axis=-1, keepdims=True)
    coords = [axis * cells / SIZE for axis in (z, y, x)]
    base = [np.floor(axis).astype(int) for axis in coords]
    frac = [axis - index for axis, index in zip(coords, base)]
    weights = [f * f * f * (f * (f * 6 - 15) + 10) for f in frac]
    result = np.zeros_like(x)
    for dz in (0, 1):
        for dy in (0, 1):
            for dx in (0, 1):
                bits = (dz, dy, dx)
                g = gradients[tuple((a + b) % cells for a, b in zip(base, bits))]
                dot = sum(g[..., axis] * (frac[axis] - bit) for axis, bit in enumerate(bits))
                weight = np.ones_like(x)
                for blend, bit in zip(weights, bits):
                    weight *= blend if bit else 1 - blend
                result += dot * weight
    return result


perlin_fbm = sum(perlin(n) * w for n, w in ((4, .58), (8, .27), (16, .11), (32, .04)))
perlin_fbm = np.clip(.5 + perlin_fbm * 1.6, 0, 1)
billows = [1 - worley(n) for n in (4, 8, 16)]
shape = np.clip(perlin_fbm * .75 + billows[0] * .25, 0, 1)
volume = np.stack((shape, *billows), axis=-1)
volume = np.rint(volume * 255).astype(np.uint8)
tile = SIZE + 2
atlas = np.zeros((tile * 8, tile * 16, 4), dtype=np.uint8)
for index in range(SIZE):
    row, column = divmod(index, 16)
    # One wrapped texel on every edge prevents bilinear bleeding between slices.
    atlas[row * tile:(row + 1) * tile, column * tile:(column + 1) * tile] = np.pad(
        volume[index], ((1, 1), (1, 1), (0, 0)), mode="wrap")
target = Path(__file__).resolve().parents[1] / "Assets/environment/textures/cloud_noise.tga"
target.parent.mkdir(parents=True, exist_ok=True)
Image.fromarray(atlas).save(target)
print(f"Generated {target}: {atlas.shape}, {target.stat().st_size} bytes")

# Independent two-dimensional fields: coverage, cloud type/top, and XY curl warp.
# No repeating row of slices is used to decide where entire cloud banks form.
wy, wx = np.mgrid[:512, :512].astype(np.float32)
def weather_noise(cells):
    grid = rng.random((cells, cells), dtype=np.float32)
    qx, qy = wx * cells / 512, wy * cells / 512
    ix, iy = np.floor(qx).astype(int), np.floor(qy).astype(int)
    fx, fy = qx - ix, qy - iy
    fx, fy = fx * fx * (3 - 2 * fx), fy * fy * (3 - 2 * fy)
    return ((1-fy) * ((1-fx)*grid[iy % cells, ix % cells] + fx*grid[iy % cells, (ix+1) % cells])
            + fy * ((1-fx)*grid[(iy+1) % cells, ix % cells] + fx*grid[(iy+1) % cells, (ix+1) % cells]))

weather = []
for channel in range(4):
    field = sum(weather_noise(n) * w for n, w in ((4, .6), (8, .27), (16, .13)))
    weather.append(np.clip((field - .22) / .56, 0, 1))
weather_image = np.rint(np.stack(weather, axis=-1) * 255).astype(np.uint8)
Image.fromarray(weather_image).save(target.with_name("cloud_weather.tga"))
print("Density percentiles:", np.percentile(shape, [1, 10, 50, 90, 99]))
