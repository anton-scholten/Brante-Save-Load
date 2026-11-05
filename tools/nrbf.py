#!/usr/bin/env python3
"""Minimal reader for .NET BinaryFormatter (MS-NRBF) streams - the game's .dat
saves - into plain Python dicts/lists, for inspecting saves outside the game.

    python3 tools/nrbf.py SAVE.dat [field ...]

With no fields, prints the hero's stats, time fields and last scene. Fields
are dotted paths into the SaveLoadHelper (`ModelParameters.Will`, `Turn`).
As a module: `load(path)` returns the root object (a dict per class, with the
class name under "__class"; List<T> becomes a Python list, enums their int).
"""
import struct
import sys


class _Reader:
    def __init__(self, data):
        self.d = data
        self.p = 0
        self.objs = {}
        self.meta = {}

    def u8(self):
        v = self.d[self.p]
        self.p += 1
        return v

    def i32(self):
        v = struct.unpack_from("<i", self.d, self.p)[0]
        self.p += 4
        return v

    def string(self):
        n = 0
        shift = 0
        while True:
            b = self.u8()
            n |= (b & 0x7F) << shift
            shift += 7
            if not b & 0x80:
                break
        s = self.d[self.p:self.p + n].decode("utf-8", "replace")
        self.p += n
        return s

    _FMT = {1: "<?", 2: "<B", 6: "<d", 7: "<h", 8: "<i", 9: "<q", 10: "<b",
            11: "<f", 12: "<q", 13: "<q", 14: "<H", 15: "<I", 16: "<Q"}

    def prim(self, t):
        if t == 18:
            return self.string()
        if t == 5:
            return self.string()
        if t == 3:
            b = self.u8()
            n = 1 if b < 0x80 else 2 if b < 0xE0 else 3 if b < 0xF0 else 4
            s = self.d[self.p - 1:self.p - 1 + n].decode("utf-8", "replace")
            self.p += n - 1
            return s
        f = self._FMT[t]
        v = struct.unpack_from(f, self.d, self.p)[0]
        self.p += struct.calcsize(f)
        return v

    def class_info(self):
        oid = self.i32()
        name = self.string()
        n = self.i32()
        return oid, name, [self.string() for _ in range(n)]

    def type_info(self, n):
        kinds = [self.u8() for _ in range(n)]
        extra = []
        for k in kinds:
            if k in (0, 7):
                extra.append(self.u8())
            elif k == 3:
                extra.append(self.string())
            elif k == 4:
                extra.append((self.string(), self.i32()))
            else:
                extra.append(None)
        return list(zip(kinds, extra))

    def members(self, oid, name, names, types):
        obj = {"__class": name}
        self.objs[oid] = obj
        for m, (k, x) in zip(names, types):
            if k == 0:
                obj[m] = self.prim(x)
            else:
                obj[m] = self.record()
        return obj

    def elements(self, n, ptype=None):
        out = []
        while len(out) < n:
            if ptype is not None:
                out.append(self.prim(ptype))
                continue
            r = self.record(nulls=True)
            if isinstance(r, _Nulls):
                out.extend([None] * r.n)
            else:
                out.append(r)
        return out

    def record(self, nulls=False):
        t = self.u8()
        if t == 12:  # BinaryLibrary
            self.i32()
            self.string()
            return self.record(nulls)
        if t in (4, 5):
            oid, name, names = self.class_info()
            types = self.type_info(len(names))
            if t == 5:
                self.i32()
            self.meta[oid] = (name, names, types)
            return self.members(oid, name, names, types)
        if t in (2, 3):
            oid, name, names = self.class_info()
            if t == 3:
                self.i32()
            types = [(2, None)] * len(names)
            self.meta[oid] = (name, names, types)
            return self.members(oid, name, names, types)
        if t == 1:
            oid = self.i32()
            name, names, types = self.meta[self.i32()]
            self.meta[oid] = (name, names, types)
            return self.members(oid, name, names, types)
        if t == 6:
            oid = self.i32()
            s = self.string()
            self.objs[oid] = s
            return s
        if t == 7:
            oid = self.i32()
            kind = self.u8()
            rank = self.i32()
            lens = [self.i32() for _ in range(rank)]
            if kind in (3, 4, 5):
                [self.i32() for _ in range(rank)]
            (k, x), = self.type_info(1)
            n = 1
            for L in lens:
                n *= L
            arr = self.elements(n, x if k == 0 else None)
            self.objs[oid] = arr
            return arr
        if t == 8:
            return self.prim(self.u8())
        if t == 9:
            return _Ref(self.i32())
        if t == 10:
            return None
        if t == 13:
            return _Nulls(self.u8()) if nulls else None
        if t == 14:
            return _Nulls(self.i32()) if nulls else None
        if t == 15:
            oid = self.i32()
            n = self.i32()
            arr = self.elements(n, self.u8())
            self.objs[oid] = arr
            return arr
        if t in (16, 17):
            oid = self.i32()
            arr = self.elements(self.i32())
            self.objs[oid] = arr
            return arr
        raise ValueError("record type %d at %d" % (t, self.p - 1))


class _Ref:
    def __init__(self, i):
        self.i = i


class _Nulls:
    def __init__(self, n):
        self.n = n


def _resolve(r, v, seen):
    if isinstance(v, _Ref):
        v = r.objs.get(v.i)
    if isinstance(v, (dict, list)):
        if id(v) in seen:
            return v
        seen.add(id(v))
        items = v.items() if isinstance(v, dict) else enumerate(v)
        for k, x in list(items):
            v[k] = _resolve(r, x, seen)
        if isinstance(v, dict):
            c = v.get("__class", "")
            if c.startswith("System.Collections.Generic.List`1") and "_items" in v:
                return list(v["_items"] or [])[:v.get("_size", 0)]
            if set(v) == {"__class", "value__"}:
                return v["value__"]
    return v


def load(path):
    with open(path, "rb") as f:
        r = _Reader(f.read())
    if r.u8() != 0:
        raise ValueError("not a BinaryFormatter stream")
    root = r.i32()
    r.p += 12
    while r.p < len(r.d):
        if r.d[r.p] == 11:
            break
        r.record()
    return _resolve(r, r.objs[root], set())


def get(obj, dotted):
    for part in dotted.split("."):
        if obj is None:
            return None
        if isinstance(obj, dict):
            alt = "_" + part[:1].lower() + part[1:]   # property -> backing field
            obj = obj.get(part, obj.get(alt))
        else:
            obj = obj[int(part)]
    return obj


DEFAULT = ["LastOpenScene", "Chapter", "Year", "PreviousYear", "Turn", "Season", "HeroAge"]


def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 2
    h = load(argv[1])
    fields = argv[2:]
    if fields:
        for f in fields:
            print(f, "=", get(h, f))
        return 0
    print(" ".join("%s=%s" % (f, get(h, f)) for f in DEFAULT))
    mp = get(h, "ModelParameters") or {}
    print(" ".join("%s=%s" % (k, v) for k, v in mp.items()
                   if not k.startswith("__") and isinstance(v, int) and v))
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
