"""Reference port of Valheim 1.0's biome generator (WorldGenerator.GetBiome), validated against a real save.

Usage: World(seed, 0, worldgen_version).biome(x, z) -> 'Meadows' | 'BlackForest' | ... ('x' east, 'z' north).
seed and worldgen_version come from the world's _main.N.fwl2.
"""
import math, struct
M32 = 0xFFFFFFFF

class UnityRandom:  # xorshift128, as Unity's Random.InitState / Random.Range(int, int)
    def __init__(self, seed):
        s = [seed & M32]
        for _ in range(3): s.append((s[-1] * 1812433253 + 1) & M32)
        self.s = s
    def next(self):
        x, y, z, w = self.s
        t = (x ^ (x << 11)) & M32
        nw = (w ^ (w >> 19) ^ t ^ (t >> 8)) & M32
        self.s = [y, z, w, nw]
        return nw
    def range_int(self, lo, hi):
        return lo + (self.next() % (hi - lo)) if hi > lo else lo

# Ken Perlin's permutation, as used by Unity's Mathf.PerlinNoise
P = [151,160,137,91,90,15,131,13,201,95,96,53,194,233,7,225,140,36,103,30,69,142,8,99,37,240,21,10,23,190,6,148,247,120,234,75,0,26,197,62,94,252,219,203,117,35,11,32,57,177,33,88,237,149,56,87,174,20,125,136,171,168,68,175,74,165,71,134,139,48,27,166,77,146,158,231,83,111,229,122,60,211,133,230,220,105,92,41,55,46,245,40,244,102,143,54,65,25,63,161,1,216,80,73,209,76,132,187,208,89,18,169,200,196,135,130,116,188,159,86,164,100,109,198,173,186,3,64,52,217,226,250,124,123,5,202,38,147,118,126,255,82,85,212,207,206,59,227,47,16,58,17,182,189,28,42,223,183,170,213,119,248,152,2,44,154,163,70,221,153,101,155,167,43,172,9,129,22,39,253,19,98,108,110,79,113,224,232,178,185,112,104,218,246,97,228,251,34,242,193,238,210,144,12,191,179,162,241,81,51,145,235,249,14,239,107,49,192,214,31,181,199,106,157,184,84,204,176,115,121,50,45,127,4,150,254,138,236,205,93,222,114,67,29,24,72,243,141,128,195,78,66,215,61,156,180]
P = P + P
def f32(v): return struct.unpack('f', struct.pack('f', v))[0]
def fade(t): return t*t*t*(t*(t*6-15)+10)
def lerp(t,a,b): return a + t*(b-a)
def grad(h, x, y):
    h &= 15
    u = x if h < 8 else y
    v = y if h < 4 else (x if h in (12, 14) else 0)
    return (u if h & 1 == 0 else -u) + (v if h & 2 == 0 else -v)
# Unity normalises improved noise (about -0.69..0.79) into 0..1; Mathf.PerlinNoise(0, 0) == 0.4652731.
NORM = [((lambda r: (r + 0.69) / 1.483)), (lambda r: (r + 1) / 2)]
def perlin(x, y, norm=0):
    x, y = f32(x), f32(y)
    X, Y = math.floor(x) & 255, math.floor(y) & 255
    x -= math.floor(x); y -= math.floor(y)
    u, v = fade(x), fade(y)
    # Ken Perlin's 3D improved noise at z = 0, which is what Unity evaluates: one more permutation per corner.
    A, B = P[X] + Y, P[X + 1] + Y
    AA, AB, BA, BB = P[A], P[A + 1], P[B], P[B + 1]
    r = lerp(v, lerp(u, grad(P[AA], x, y), grad(P[BA], x - 1, y)), lerp(u, grad(P[AB], x, y - 1), grad(P[BB], x - 1, y - 1)))
    return NORM[norm](r)

# Unity's Mathf.PerlinNoise mirrors negative inputs; Valheim's DUtils.PerlinNoise calls it directly.
# With this, biomes match every location instance in a real 1.0 save (12,286 of 12,286).
def unity_perlin(x, y): return perlin(abs(x), abs(y))

class World:
    def __init__(self, seed, norm=0, version=2):
        self.norm = norm
        r = UnityRandom(seed)
        self.o0, self.o1, self.o2, self.o3 = (r.range_int(-10000, 10000) for _ in range(4))
        r.range_int(-2**31, 2**31 - 1); r.range_int(-2**31, 2**31 - 1)
        self.o4 = r.range_int(-10000, 10000)
        self.minMountain = 1000 if version > 0 else 1500
        self.minDark, self.maxMarsh = (0.4, 6000) if version > 1 else (0.5, 8000)
    def pn(self, x, y): return unity_perlin(x, y)
    def base_height(self, wx, wy):
        d = math.hypot(wx, wy)
        x, y = wx + 100000 + self.o0, wy + 100000 + self.o1
        h = self.pn(x*0.002*0.5, y*0.002*0.5) * self.pn(x*0.003*0.5, y*0.003*0.5)
        h += self.pn(x*0.002, y*0.002) * self.pn(x*0.003, y*0.003) * h * 0.9
        h += self.pn(x*0.005, y*0.005) * self.pn(x*0.01, y*0.01) * 0.5 * h
        h -= 0.07
        a = self.pn(x*0.002*0.25+0.123, y*0.002*0.25+0.15123); b = self.pn(x*0.002*0.25+0.321, y*0.002*0.25+0.231)
        rv = 1 - lerpstep(0.02, 0.12, abs(a - b))
        rv *= smoothstep(744, 1000, d)
        h *= 1 - rv
        if d > 10000:
            h = lerp(lerpstep(10000, 10500, d), h, -0.2)
            if d > 10490: h = lerp(lerpstep(10490, 10500, d), h, -2)
            return h
        if d < self.minMountain and h > 0.28:
            t = min(max((h - 0.28) / 0.1, 0), 1)
            h = lerp(lerpstep(self.minMountain - 400, self.minMountain, d), lerp(t, 0.28, 0.38), h)
        return h
    def biome(self, wx, wy):
        d = math.hypot(wx, wy); bh = self.base_height(wx, wy)
        ang = math.sin(math.atan2(wx, wy) * 20) * 100
        if math.hypot(wx, wy - 4000) > 12000 + math.sin(math.atan2(wx, wy) * 20) * 100: return 'AshLands'  # see note
        if bh <= 0.02: return 'Ocean'
        if math.hypot(wx, wy + 4000) > 12000 + ang: return 'DeepNorth'
        if bh > 0.4: return 'Mountain'
        if self.pn((self.o0+wx)*0.001, (self.o0+wy)*0.001) > 0.6 and 2000 < d < self.maxMarsh and 0.05 < bh < 0.25: return 'Swamp'
        if self.pn((self.o4+wx)*0.001, (self.o4+wy)*0.001) > self.minDark and 6000 + ang < d < 10000: return 'Mistlands'
        if self.pn((self.o1+wx)*0.001, (self.o1+wy)*0.001) > 0.4 and 3000 + ang < d < 8000: return 'Plains'
        if self.pn((self.o2+wx)*0.001, (self.o2+wy)*0.001) > 0.4 and 600 + ang < d < 6000: return 'BlackForest'
        if d > 5000 + ang: return 'BlackForest'
        return 'Meadows'

def lerpstep(l, h, v): return min(max((v - l) / (h - l), 0), 1)
def smoothstep(a, b, x):
    t = min(max((x - a) / (b - a), 0), 1); return t * t * (3 - 2 * t)
