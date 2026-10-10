import struct, sys, collections

def stable_hash(s):
    n1 = n2 = 5381
    i = 0
    while i < len(s):
        n1 = ((n1 << 5) + n1 ^ ord(s[i])) & 0xFFFFFFFF
        if i == len(s) - 1: break
        n2 = ((n2 << 5) + n2 ^ ord(s[i + 1])) & 0xFFFFFFFF
        i += 2
    r = (n1 + n2 * 1566083941) & 0xFFFFFFFF
    return r - (1 << 32) if r >= 1 << 31 else r

class R:
    def __init__(s, b): s.b, s.p = b, 0
    def u(s, f):
        v = struct.unpack_from(f, s.b, s.p); s.p += struct.calcsize(f); return v
    def num(s):
        n = s.b[s.p]; s.p += 1
        if n & 0x80: n = ((n & 0x7F) << 8) | s.b[s.p]; s.p += 1
        return n
    def string(s):  # .NET 7-bit length prefix
        n = shift = 0
        while True:
            c = s.b[s.p]; s.p += 1; n |= (c & 0x7F) << shift; shift += 7
            if not c & 0x80: break
        v = s.b[s.p:s.p + n].decode('utf-8', 'replace'); s.p += n; return v

def small_rot(r):
    a, = r.u('<H')
    if a & 0x8000: return (0.0, (a & 0x7FFF) * 0.5, 0.0)
    b, = r.u('<H'); n = (a << 16) | b
    return ((n & 0x3FF) * .5, ((n >> 10) & 0x3FF) * .5, ((n >> 20) & 0x3FF) * .5)

def read_chunk(path):
    r = R(open(path, 'rb').read())
    ver, count = r.u('<hi')
    out = []
    for _ in range(count):
        f, = r.u('<H')
        if f & 0x2000: x, z = r.u('<hh'); pos = (float(x), 0.0, float(z))
        else: pos = r.u('<fff')
        prefab, = r.u('<i')
        rot = small_rot(r) if f & 0x1000 else (0.0, 0.0, 0.0)
        d = {}
        if f & 0xFF:
            if f & 1: d['conn'] = r.u('<Bi')
            for bit, fn in ((2, lambda: r.u('<f')[0]), (4, lambda: r.u('<fff')), (8, lambda: r.u('<ffff')),
                            (0x10, lambda: r.u('<i')[0]), (0x20, lambda: r.u('<q')[0]),
                            (0x40, r.string), (0x80, lambda: bytes(r.b[r.p:r.p + (lambda n: (setattr(r, 'p', r.p), n)[1])(r.u('<i')[0])]))):
                if f & bit:
                    for _ in range(r.num()):
                        k, = r.u('<i')
                        if bit == 0x80:
                            n, = r.u('<i'); v = bytes(r.b[r.p:r.p + n]); r.p += n
                        else: v = fn()
                        d[k] = v
        out.append(dict(flags=f, pos=pos, prefab=prefab, rot=rot, data=d))
    assert r.p == len(r.b), (path, r.p, len(r.b))
    return ver, out
