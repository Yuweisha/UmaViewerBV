"""Independent, strict PMX 2.0 validation (stdlib only; never writes the model)."""
import argparse
import hashlib
import json
import math
from pathlib import Path
import struct


class PMXError(ValueError):
    pass


class Reader:
    def __init__(self, data):
        self.data, self.pos = data, 0
        self.references = []

    def take(self, n):
        if n < 0 or self.pos + n > len(self.data):
            raise PMXError(f'Truncated data at byte {self.pos}, requested {n}')
        value = self.data[self.pos:self.pos+n]
        self.pos += n
        return value

    def u8(self):
        return self.take(1)[0]

    def integer(self, n=4, signed=True):
        return int.from_bytes(self.take(n), 'little', signed=signed)

    def count(self):
        n = self.integer()
        if n < 0 or n > len(self.data):
            raise PMXError(f'Invalid count {n} at byte {self.pos-4}')
        return n

    def floats(self, n):
        values = struct.unpack('<'+'f'*n, self.take(4*n))
        if not all(math.isfinite(x) for x in values):
            raise PMXError(f'Nonfinite float at byte {self.pos-4*n}')
        return values

    def text(self):
        raw = self.take(self.count())
        try:
            text = raw.decode(self.encoding)
        except UnicodeDecodeError as e:
            raise PMXError(f'Text disagrees with encoding flag at byte {self.pos-len(raw)}') from e
        if '\x00' in text:
            raise PMXError('Embedded NUL in PMX text; check encoding declaration')
        return text

    def ref(self, kind, allow_none=False, unsigned=False):
        value = self.integer(self.sizes[kind], not unsigned)
        self.references.append((kind, value, allow_none))
        return value

    def validate(self):
        if self.take(4) != b'PMX ':
            raise PMXError('Invalid PMX magic')
        version, = self.floats(1)
        if abs(version-2.0) > 1e-5:
            raise PMXError('This validator supports PMX 2.0; PMX 2.1 soft bodies require another validator')
        header = self.take(self.u8())
        if len(header) != 8 or header[0] not in (0, 1) or header[1] > 4:
            raise PMXError('Invalid PMX global settings')
        self.encoding = ('utf-16-le', 'utf-8')[header[0]]
        self.sizes = dict(zip(('vertex','texture','material','bone','morph','rigid'), header[2:]))
        if any(n not in (1,2,4) for n in self.sizes.values()):
            raise PMXError('Invalid index width')
        names = [self.text() for _ in range(4)]
        counts = {}
        counts['vertex'] = self.count()
        for _ in range(counts['vertex']):
            self.floats(8 + header[1]*4)
            kind = self.u8()
            if kind == 0:
                self.ref('bone')
            elif kind == 1:
                self.ref('bone'); self.ref('bone', True)
                weight, = self.floats(1)
                if not 0 <= weight <= 1: raise PMXError('BDEF2 weight outside [0,1]')
            elif kind == 2:
                refs = [self.ref('bone', True) for _ in range(4)]
                weights = self.floats(4)
                if any(w < -1e-5 or w > 1.00001 for w in weights) or abs(sum(weights)-1) > 1e-3:
                    raise PMXError('Invalid BDEF4 weights')
                if any(i == -1 and w > 1e-6 for i,w in zip(refs,weights)):
                    raise PMXError('Positive skin weight refers to missing bone')
            elif kind == 3:
                self.ref('bone'); self.ref('bone', True)
                self.floats(10)
            else:
                raise PMXError(f'Unsupported deform type {kind}')
            self.floats(1)
        indices = self.count()
        if indices % 3: raise PMXError('Triangle index count is not divisible by 3')
        for _ in range(indices): self.ref('vertex', unsigned=True)
        counts['texture'] = self.count()
        textures = [self.text() for _ in range(counts['texture'])]
        counts['material'] = self.count()
        face_sum = 0
        for _ in range(counts['material']):
            self.text(); self.text(); self.floats(11); self.u8(); self.floats(5)
            self.ref('texture', True); self.ref('texture', True); self.u8()
            shared = self.u8()
            if shared == 0: self.ref('texture', True)
            elif shared == 1:
                if self.u8() > 9: raise PMXError('Invalid shared toon index')
            else: raise PMXError('Invalid toon flag')
            self.text()
            surface = self.count()
            if surface % 3: raise PMXError('Material surface index count is not divisible by 3')
            face_sum += surface
        if face_sum != indices: raise PMXError(f'Material surface sum {face_sum} != index count {indices}')
        counts['bone'] = self.count()
        parents, bone_names, ik_limits = [], [], []
        for b in range(counts['bone']):
            bone_names.append(self.text()); self.text(); self.floats(3)
            parent = self.ref('bone', True)
            if parent == b: raise PMXError('Bone is its own parent')
            parents.append(parent)
            self.integer(); flags = self.integer(2, False)
            if flags & 1: self.ref('bone', True)
            else: self.floats(3)
            if flags & 0x300: self.ref('bone'); self.floats(1)
            if flags & 0x400: self.floats(3)
            if flags & 0x800: self.floats(6)
            if flags & 0x2000: self.integer()
            if flags & 0x20:
                self.ref('bone'); self.count(); self.floats(1)
                for _ in range(self.count()):
                    self.ref('bone')
                    limited = self.u8()
                    if limited not in (0,1): raise PMXError('Invalid IK limit flag')
                    if limited:
                        low, high = self.floats(3), self.floats(3)
                        if any(a > z for a,z in zip(low,high)): raise PMXError('IK lower limit exceeds upper')
                        ik_limits.append({'bone':bone_names[-1], 'lower':low, 'upper':high})
        counts['morph'] = self.count()
        for _ in range(counts['morph']):
            self.text(); self.text(); self.u8(); kind = self.u8()
            for _ in range(self.count()):
                if kind == 0: self.ref('morph'); self.floats(1)
                elif kind == 1: self.ref('vertex', unsigned=True); self.floats(3)
                elif kind == 2: self.ref('bone'); self.floats(7)
                elif 3 <= kind <= 7: self.ref('vertex', unsigned=True); self.floats(4)
                elif kind == 8: self.ref('material', True); self.u8(); self.floats(28)
                else: raise PMXError(f'Unsupported PMX 2.0 morph type {kind}')
        frames = self.count()
        for _ in range(frames):
            self.text(); self.text(); self.u8()
            for _ in range(self.count()):
                kind = self.u8()
                if kind not in (0,1): raise PMXError('Invalid display frame type')
                self.ref(('bone','morph')[kind])
        counts['rigid'] = self.count()
        for _ in range(counts['rigid']):
            self.text(); self.text(); self.ref('bone', True)
            if self.u8() > 15: raise PMXError('Invalid collision group')
            self.integer(2, False); self.u8(); self.floats(14); self.u8()
        joints = self.count()
        for _ in range(joints):
            self.text(); self.text()
            if self.u8() != 0: raise PMXError('Unsupported PMX 2.0 joint type')
            self.ref('rigid', True); self.ref('rigid', True); self.floats(24)
        if self.pos != len(self.data): raise PMXError(f'{len(self.data)-self.pos} unparsed bytes')
        for kind, value, allow_none in self.references:
            if not (0 <= value < counts[kind] or (value == -1 and allow_none)):
                raise PMXError(f'Invalid {kind} index {value}/{counts[kind]}')
        for b in range(len(parents)):
            seen, current = set(), b
            while current != -1:
                if current in seen: raise PMXError('Cycle in bone hierarchy')
                seen.add(current); current = parents[current]
        return {'valid':True,'version':version,'encoding':self.encoding,'name':names[0],
                'counts':dict(counts,triangles=indices//3,frames=frames,joints=joints),
                'textures':textures,'bone_names':bone_names,'ik_limits':ik_limits}


def validate(path):
    path = Path(path)
    data = path.read_bytes()
    result = Reader(data).validate()
    result['sha256'] = hashlib.sha256(data).hexdigest()
    result['missing_textures'] = [t for t in result['textures'] if not (path.parent/t.replace('\\','/')).is_file()]
    return result


def main():
    cli = argparse.ArgumentParser()
    cli.add_argument('model', type=Path)
    cli.add_argument('--report', type=Path)
    args = cli.parse_args()
    try:
        result = validate(args.model)
    except (PMXError, OSError) as e:
        result = {'valid':False,'error':str(e)}
    text = json.dumps(result, ensure_ascii=False, indent=2)
    if args.report: args.report.write_text(text, encoding='utf-8')
    print(text)
    return 0 if result['valid'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
