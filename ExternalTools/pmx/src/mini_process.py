# -*- coding: utf-8 -*-
"""mini 赛马娘模型处理 —— [1]材质表情统一处理
供工作台 GUI 调用的完整流程（打包环境从 _MEIPASS/src import）：
  1. 复制 src_dir(含 pmx + Texture2D) -> out_dir
  2. 腮红 cheek0：肤色图 × 腮红图 Multiply 合成（肤色取肤色图中心，通配 face0_*）
  3. 眉眼嘴 UV 重映射到贴图顶部/左上角（同系列 1025/1033 布局一致，数值可复用）
  4. 添加 eye/mouth/mayu UV-morph（编号 eye_0../mouth_0../mayu_0..，0/1 开关）
使用：from mini_process import process; process(src_dir, out_dir, log)
"""
import os, sys, io, shutil, subprocess, struct, glob

# ---------- morph cell（同系列 1025/1033 一致，来自贴图有内容 cell 检测） ----------
EYE_CELLS = [(i, j) for i in range(3) for j in range(4)] + [(3, 0), (3, 1), (3, 2)]  # 15
MOUTH_CELLS = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 0), (1, 1), (1, 2), (1, 3),
               (2, 0), (2, 1), (2, 2), (2, 3), (3, 0), (3, 1), (3, 2), (3, 3),
               (4, 0), (4, 1), (4, 2)]  # 19
MAYU_CELLS = [(0, 0), (0, 1), (0, 2), (0, 3), (1, 0), (1, 1), (1, 2), (1, 3), (2, 0)]  # 9


def _magick(args):
    r = subprocess.run(['magick'] + args, capture_output=True, text=True,
                       encoding='utf-8', errors='replace')
    if r.returncode != 0:
        raise RuntimeError('magick 失败: ' + (r.stderr or r.stdout or '').strip())
    return r


def _copy_model_and_tex(src_dir, out_dir, log):
    src_tex = os.path.join(src_dir, 'Texture2D'); out_tex = os.path.join(out_dir, 'Texture2D')
    if os.path.isdir(out_dir):
        shutil.rmtree(out_dir, ignore_errors=True)
    os.makedirs(out_tex, exist_ok=True)
    pmx = None
    for fn in os.listdir(src_dir):
        if fn.lower().endswith('.pmx'):
            shutil.copy2(os.path.join(src_dir, fn), os.path.join(out_dir, fn))
            pmx = os.path.join(out_dir, fn)
    if pmx is None:
        raise RuntimeError(f'未找到 pmx：{src_dir}')
    for fn in os.listdir(src_tex):
        shutil.copy2(os.path.join(src_tex, fn), os.path.join(out_tex, fn))
    log(f'复制模型 + {len(os.listdir(src_tex))} 张贴图')
    return pmx, out_tex


def _cheek(src_tex, out_tex, log):
    cheek = glob.glob(os.path.join(src_tex, 'tex_mchr0001_00_face0_*_cheek0.png'))
    face = glob.glob(os.path.join(src_tex, 'tex_mchr0001_00_face0_*_diff.png'))
    if not cheek or not face:
        log('  [跳过腮红] 未找到腮红/肤色贴图'); return
    r = subprocess.run(['magick', face[0], '-gravity', 'center', '-crop', '1x1+0+0', '+repage',
                        '-format', '%[hex:p{0,0}]', 'info:'],
                       capture_output=True, text=True, encoding='utf-8', errors='replace')
    skin = (r.stdout or 'FEE2BA').strip()
    if len(skin) >= 6:
        skin = '#' + skin[:6]
    log(f'腮红肤色: {skin}')
    _magick(['-size', '256x256', 'xc:' + skin, cheek[0], '-compose', 'Multiply', '-composite',
             os.path.join(out_tex, os.path.basename(cheek[0]))])


# ---------- UV 重映射（眉/眼/嘴，同系列数值） ----------
def _uv_remap(pmx, log):
    data = bytearray(open(pmx, 'rb').read())
    pos = 0
    def rb(n):
        nonlocal pos
        d = data[pos:pos+n]; pos += n; return d
    def ri(sz, signed=True):
        return int.from_bytes(rb(sz), 'little', signed=signed)
    def rf():
        return struct.unpack('<f', rb(4))[0]
    def rtext():
        n = ri(4); raw = rb(n)
        if n == 0: return ''
        try:
            s = raw.decode('utf-8')
            if '\x00' in s and n % 2 == 0: return raw.decode('utf-16-le')
            return s
        except Exception: return raw.decode('utf-16-le', 'replace')
    rb(4); rf(); ri(1, False); ri(1, False)
    uvc = ri(1, False)
    vidx = ri(1, False); tidx = ri(1, False); midx = ri(1, False)
    bidx = ri(1, False); moidx = ri(1, False); ridx = ri(1, False)
    for _ in range(4): rtext()
    vc = ri(4, False)
    uv_off = []
    for _ in range(vc):
        uo = pos + 24
        [rf() for _ in range(3)]; [rf() for _ in range(3)]
        rf(); rf()
        for _ in range(uvc): [rf() for _ in range(4)]
        dt = ri(1, False)
        if dt == 0: ri(bidx)
        elif dt == 1: ri(bidx); ri(bidx); rf()
        elif dt == 2: pos += 4*bidx+16
        elif dt == 3: pos += 2*bidx+4+36
        elif dt == 4: pos += 4*bidx+16
        rf()
        uv_off.append(uo)
    fc = ri(4, False)
    faces = [ri(vidx, False) for _ in range(fc)]
    tc = ri(4, False)
    for _ in range(tc): rtext()
    mc = ri(4, False)
    mats = []
    for _ in range(mc):
        name = rtext(); rtext()
        [rf() for _ in range(4)]; [rf() for _ in range(3)]; [rf() for _ in range(3)]; rf()
        ri(1, False); [rf() for _ in range(4)]; rf()
        ri(tidx); ri(tidx); ri(1, False)
        toon = ri(1, False)
        if toon == 0: ri(tidx)
        elif toon == 1: ri(1, False)
        rtext(); fcnt = ri(4, False)
        mats.append((name, fcnt))
    face_offset = 0; updated = 0; visited = set()
    for name, fcnt in mats:
        is_mayu = ('mayu' in name)
        is_eye = name.lower().endswith('_eye')
        is_mouth = ('mouth' in name)
        if is_mayu or is_eye or is_mouth:
            for f in range(fcnt):
                vi = faces[face_offset + f]
                if vi in visited: continue
                visited.add(vi)
                off = uv_off[vi]
                u = struct.unpack('<f', data[off:off+4])[0]
                vv = struct.unpack('<f', data[off+4:off+8])[0]
                if is_mayu:
                    v2 = (0.9835 - vv) * 1.1876; u2 = u
                elif is_eye:
                    v2 = (0.9923 - vv) * (0.125/(0.9923-0.8876)); u2 = u
                else:  # mouth：组A(u缩放)+组B(v缩放)到左上角
                    if vv < 0.5:
                        u2 = (u - 0.8099)/(0.9363 - 0.8099) * 0.25; v2 = vv
                    else:
                        u2 = u; v2 = (0.9764 - vv)/(0.9764 - 0.9094) * 0.125
                data[off+4:off+8] = struct.pack('<f', v2)
                data[off:off+4] = struct.pack('<f', u2)
                updated += 1
        face_offset += fcnt
    open(pmx, 'wb').write(data)
    log(f'表情 UV 重映射({updated} 顶点)')


# ---------- morph 添加（UV-morph，UTF-16LE 名称） ----------
def _add_morphs(pmx, log):
    data = bytearray(open(pmx, 'rb').read())
    pos = 0
    def rb(n):
        nonlocal pos
        d = data[pos:pos+n]; pos += n; return d
    def ri(sz, signed=True):
        return int.from_bytes(rb(sz), 'little', signed=signed)
    def rf():
        return struct.unpack('<f', rb(4))[0]
    def rtext():
        n = ri(4); raw = rb(n)
        if n == 0: return ''
        try:
            s = raw.decode('utf-8')
            if '\x00' in s and n % 2 == 0: return raw.decode('utf-16-le')
            return s
        except Exception: return raw.decode('utf-16-le', 'replace')
    rb(4); rf(); ri(1, False); ri(1, False)
    uvc = ri(1, False)
    vidx = ri(1, False); tidx = ri(1, False); midx = ri(1, False)
    bidx = ri(1, False); moidx = ri(1, False); ridx = ri(1, False)
    for _ in range(4): rtext()
    vc = ri(4, False)
    for _ in range(vc):
        pos += 12 + 12 + 8 + 16 * uvc
        dt = ri(1, False)
        if dt == 0: pos += bidx
        elif dt == 1: pos += bidx + bidx + 4
        elif dt == 2: pos += 4*bidx+16
        elif dt == 3: pos += 2*bidx+4+36
        elif dt == 4: pos += 4*bidx+16
        pos += 4
    fc = ri(4, False)
    faces = [ri(vidx, False) for _ in range(fc)]
    tc = ri(4, False)
    for _ in range(tc): rtext()
    mc = ri(4, False)
    eye_range = mouth_range = mayu_l_range = mayu_r_range = None
    fo = 0
    for _ in range(mc):
        name = rtext(); rtext()
        [rf() for _ in range(4)]; [rf() for _ in range(3)]; [rf() for _ in range(3)]; rf()
        ri(1, False); [rf() for _ in range(4)]; rf()
        ri(tidx); ri(tidx); ri(1, False)
        toon = ri(1, False)
        if toon == 0: ri(tidx)
        elif toon == 1: ri(1, False)
        rtext(); fcnt = ri(4, False)
        ln = name.lower()
        if ln.endswith('_eye'): eye_range = (fo, fcnt)
        elif 'mouth' in ln: mouth_range = (fo, fcnt)
        elif ln.endswith('_mayu_l'): mayu_l_range = (fo, fcnt)
        elif ln.endswith('_mayu_r'): mayu_r_range = (fo, fcnt)
        fo += fcnt
    bc = ri(4, False)
    for _ in range(bc):
        rtext(); rtext(); [rf() for _ in range(3)]
        ri(bidx, False); ri(4, False); bflag = ri(2, False)
        if bflag & 0x0001: ri(bidx, False)
        else: [rf() for _ in range(3)]
        if bflag & 0x0100: ri(bidx, False); rf()
        if bflag & 0x0200: ri(bidx, False); rf()
        if bflag & 0x0400: [rf() for _ in range(3)]
        if bflag & 0x0800: [rf() for _ in range(3)]; [rf() for _ in range(3)]
        if bflag & 0x2000: ri(4, False)
        if bflag & 0x0020:
            ri(bidx, False); ri(4, False); rf()
            ic = ri(4, False)
            for _ in range(ic):
                ri(bidx, False)
                if ri(1, False): [rf() for _ in range(3)]; [rf() for _ in range(3)]
    morph_start = pos
    if ri(4, False) != 0:
        raise RuntimeError('仅支持 Morph=0 的模型')
    def get_verts(rng):
        fo, fcnt = rng; s = []; seen = set()
        for f in range(fcnt):
            vi = faces[fo+f]
            if vi not in seen: seen.add(vi); s.append(vi)
        return s
    eye_verts = get_verts(eye_range)
    mouth_verts = get_verts(mouth_range)
    mseen = set(); mayu_verts = []
    for rng in (mayu_l_range, mayu_r_range):
        for vi in get_verts(rng):
            if vi not in mseen: mseen.add(vi); mayu_verts.append(vi)
    def enc(s):
        b = s.encode('utf-16-le'); return struct.pack('<I', len(b)) + b
    mb = bytearray()
    mb += struct.pack('<i', len(EYE_CELLS) + len(MOUTH_CELLS) + len(MAYU_CELLS))
    def add(name, panel, verts, du, dv):
        mb.extend(enc(name)); mb.extend(enc(name))
        mb.extend(struct.pack('<B', panel)); mb.extend(struct.pack('<B', 3))
        mb.extend(struct.pack('<i', len(verts)))
        for vi in verts:
            mb.extend(struct.pack('<H', vi)); mb.extend(struct.pack('<4f', du, dv, 0.0, 0.0))
    for k, (i, j) in enumerate(EYE_CELLS):
        add(f'eye_{k}', 2, eye_verts, 0.25*j, 0.125*i)
    for k, (i, j) in enumerate(MOUTH_CELLS):
        add(f'mouth_{k}', 3, mouth_verts, 0.25*j, 0.125*i)
    for k, (i, j) in enumerate(MAYU_CELLS):
        add(f'mayu_{k}', 1, mayu_verts, 0.25*j, 0.25*i)
    open(pmx, 'wb').write(data[:morph_start] + mb + data[morph_start+4:])
    log(f'  添加 UV-morph: 眼睛{len(EYE_CELLS)} + 嘴{len(MOUTH_CELLS)} + 眉毛{len(MAYU_CELLS)}'
        f' = {len(EYE_CELLS)+len(MOUTH_CELLS)+len(MAYU_CELLS)} 个')


def process(src_dir, out_dir, log=print):
    """完整处理：复制贴图+腮红+UV重映射+morph。"""
    log('=== [1]材质表情统一处理 (mini 赛马娘) ===')
    pmx, out_tex = _copy_model_and_tex(src_dir, out_dir, log)
    _cheek(os.path.join(src_dir, 'Texture2D'), out_tex, log)
    _uv_remap(pmx, log)
    _add_morphs(pmx, log)
    log('完成')
    return True


# ---------- 模型位置修正：上移使脚底对齐地面(Y=0) ----------
def fix_position(pmx, log):
    """向上整体移动模型（顶点 + 骨骼 position Y），使脚底最低顶点对齐地面 Y=0。
    说明：PMX 顶点是模型空间绝对坐标，仅改 Hip 骨骼 rest 不生效，须顶点+骨骼一起平移到脚底。"""
    data = bytearray(open(pmx, 'rb').read())
    pos = 0
    def rb(n):
        nonlocal pos
        d = data[pos:pos+n]; pos += n; return d
    def ri(sz, signed=True):
        return int.from_bytes(rb(sz), 'little', signed=signed)
    def rf():
        return struct.unpack('<f', rb(4))[0]
    def rtext():
        n = ri(4); raw = rb(n)
        if n == 0: return ''
        try:
            s = raw.decode('utf-8')
            if '\x00' in s and n % 2 == 0: return raw.decode('utf-16-le')
            return s
        except Exception: return raw.decode('utf-16-le', 'replace')
    rb(4); rf(); ri(1, False); ri(1, False)
    uvc = ri(1, False)
    vidx = ri(1, False); tidx = ri(1, False); midx = ri(1, False)
    bidx = ri(1, False); moidx = ri(1, False); ridx = ri(1, False)
    for _ in range(4): rtext()
    vc = ri(4, False)
    y_off = []; miny = 1e9
    for _ in range(vc):
        py = struct.unpack('<f', data[pos+4:pos+8])[0]
        if py < miny: miny = py
        y_off.append(pos + 4)
        pos += 12 + 12 + 8 + 16 * uvc
        dt = ri(1, False)
        if dt == 0: pos += bidx
        elif dt == 1: pos += bidx + bidx + 4
        elif dt == 2: pos += 4*bidx+16
        elif dt == 3: pos += 2*bidx+4+36
        elif dt == 4: pos += 4*bidx+16
        pos += 4
    off = -miny
    log(f'模型位置修正: 脚底Y={miny:.4f} 上移{off:.4f}')
    if abs(off) < 1e-6:
        log('  脚底已在地面，无需调整'); return True
    for yo in y_off:                      # 顶点 Y += off
        y = struct.unpack('<f', data[yo:yo+4])[0]
        data[yo:yo+4] = struct.pack('<f', y + off)
    fc = ri(4, False); pos += fc * vidx
    tc = ri(4, False)
    for _ in range(tc): rtext()
    mc = ri(4, False)
    for _ in range(mc):
        rtext(); rtext()
        [rf() for _ in range(4)]; [rf() for _ in range(3)]; [rf() for _ in range(3)]; rf()
        ri(1, False); [rf() for _ in range(4)]; rf()
        ri(tidx); ri(tidx); ri(1, False)
        toon = ri(1, False)
        if toon == 0: ri(tidx)
        elif toon == 1: ri(1, False)
        rtext(); ri(4, False)
    bc = ri(4, False)
    for _ in range(bc):
        rtext(); rtext()
        bo = pos
        rf(); py = rf(); rf()             # position 的 Y
        data[bo+4:bo+8] = struct.pack('<f', py + off)   # 骨骼 position Y += off
        ri(bidx, False)                   # parent
        ri(4, False)                      # layer
        bflag = ri(2, False)
        if bflag & 0x0001: ri(bidx, False)
        else: [rf() for _ in range(3)]
        if bflag & 0x0100: ri(bidx, False); rf()
        if bflag & 0x0200: ri(bidx, False); rf()
        if bflag & 0x0400: [rf() for _ in range(3)]
        if bflag & 0x0800: [rf() for _ in range(3)]; [rf() for _ in range(3)]
        if bflag & 0x2000: ri(4, False)
        if bflag & 0x0020:
            ri(bidx, False); ri(4, False); rf()
            ic = ri(4, False)
            for _ in range(ic):
                ri(bidx, False)
                if ri(1, False): [rf() for _ in range(3)]; [rf() for _ in range(3)]
    open(pmx, 'wb').write(data)
    log('模型位置修正完成')
    return True


# ---------- 工作区版处理（供工作台步骤 [22] 使用） ----------
def process_workspace(pmx, tex_dir, log=print):
    """对工作区模型就地执行材质表情统一处理：贴图复制到临时目录+腮红合成、UV重映射、morph。
    返回 result_tex_dir（导出时使用；不修改导入贴图源文件夹）。"""
    import tempfile
    base = os.path.splitext(os.path.basename(pmx))[0]
    result = os.path.join(tempfile.gettempdir(), 'pmx_workflow', 'mini_tex_' + base)
    if os.path.isdir(result):
        shutil.rmtree(result, ignore_errors=True)
    os.makedirs(result, exist_ok=True)
    log('=== [1]材质表情统一处理 ===')
    log('[1/4] 复制贴图到临时目录')
    if tex_dir and os.path.isdir(tex_dir):
        for fn in os.listdir(tex_dir):
            shutil.copy2(os.path.join(tex_dir, fn), os.path.join(result, fn))
    log(f'  已复制 {len(os.listdir(result))} 张贴图 → {result}')
    log('[2/4] 腮红 cheek0 合成')
    _cheek(tex_dir, result, log)
    log('[3/4] 眉眼嘴 UV 重映射')
    _uv_remap(pmx, log)
    log('[4/4] 添加 UV-morph（眼/嘴/眉）')
    _add_morphs(pmx, log)
    log('=== 完成 ===')
    return result
