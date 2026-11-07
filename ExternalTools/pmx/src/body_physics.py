# -*- coding: utf-8 -*-
"""
给 test/1033 添加「基础人体刚体 + 手掌 + 两段足」，用于观察碰撞效果
================================================================
1. 从参考模型 1033_2 字节级移植 13 个人体核心 static 刚体 + 4 个胸口刚体(乳摇)
2. 移植胸口 4 个 Joint（锚定 + 摆动）
3. 手工生成 手掌×2（球 static）+ 足×4（胶囊 static，脚踝段+脚尖段）
输出：带刚体的 PMX

用法：python src/body_physics.py 输入.pmx 输出.pmx
"""
import os
import sys
import struct

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader, decode_text  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# 内置胸口 4 个 Joint 的 header + param（字节级，从参考模型 1033_2 提取后内置，2026-08-30，不再依赖参考模型文件）
# 结构：header = 名称(UTF-16LE含长度前缀) + name_g(空) + 类型(0=6DOF弹簧)
#       param  = 96字节 = pos(12,占位) + rot(12) + 移动限(24) + 旋转限(24) + 弹簧(24)
CHEST_JOINT_BLOCKS = {
    17: (bytes.fromhex("1a000000530070005f00430068005f00f880e3535f004c005f00300030000000000000"), bytes.fromhex("7cc8f33ed2c66e413f23243e000000800000000000000000000000000000000000000000000000000000000000000000c3b832bec3b832bec3b832bec3b8323ec3b8323ec3b8323e000000000000000000000000000000000000000000000000")),
    18: (bytes.fromhex("1a000000530070005f00430068005f00f880e3535f004c005f00300031000000000000"), bytes.fromhex("2fd91b3fabb36a415ff4a7be000000800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")),
    19: (bytes.fromhex("1a000000530070005f00430068005f00f880e3535f0052005f00300030000000000000"), bytes.fromhex("3cc3f3bea6c66e413823243e000000800000000000000000000000000000000000000000000000000000000000000000c3b832bec3b832bec3b832bec3b8323ec3b8323ec3b8323e000000000000000000000000000000000000000000000000")),
    20: (bytes.fromhex("1a000000530070005f00430068005f00f880e3535f0052005f00300031000000000000"), bytes.fromhex("68d61bbf7cb36a41a0f3a7be000000800000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000")),
}
RIGID_PARAM = 61
JOINT_PARAM = 96

# 目标骨骼组：[(绑骨名, [顶点来源骨骼], 方向参考骨, 形状, 类型)]
# 方向参考骨：腿的 D骨 tail=65535 无效，需用主骨(左足/左ひざ)的方向与长度
BODY_PLAN = [
    ('下半身', ['下半身'], None, 'capsule', 0),
    ('上半身', ['上半身'], None, 'box', 0),
    ('上半身2', ['上半身2'], None, 'box', 0),
    ('首', ['首'], '首', 'capsule', 0),
    ('頭', ['頭'], None, 'sphere', 0),
    ('左腕', ['左腕', '左腕捩1', '左腕捩2', '左腕捩3'], '左腕', 'capsule', 0),
    ('左ひじ', ['左ひじ', '左手捩1', '左手捩2', '左手捩3'], '左ひじ', 'capsule', 0),
    ('右腕', ['右腕', '右腕捩1', '右腕捩2', '右腕捩3'], '右腕', 'capsule', 0),
    ('右ひじ', ['右ひじ', '右手捩1', '右手捩2', '右手捩3'], '右ひじ', 'capsule', 0),
    ('左足D', ['左足D', '左足'], '左足', 'capsule', 0),
    ('左ひざD', ['左ひざD', '左ひざ'], '左ひざ', 'capsule', 0),
    ('右足D', ['右足D', '右足'], '右足', 'capsule', 0),
    ('右ひざD', ['右ひざD', '右ひざ'], '右ひざ', 'capsule', 0),
    ('左胸0', ['左胸0', '左胸1'], '左胸0', 'capsule', 0),
    ('左胸1', ['左胸1', '左胸0'], '左胸1', 'sphere', 2),
    ('右胸0', ['右胸0', '右胸1'], '右胸0', 'capsule', 0),
    ('右胸1', ['右胸1', '右胸0'], '右胸1', 'sphere', 2),
]
# 手掌：[(刚体名, 绑骨, [顶点来源骨骼])]  -> 球 static
HAND_PLAN = [
    ('左手掌', '左ダミー', ['左ダミー', '左手首', '左親指０', '左人指１', '左中指１', '左薬指１', '左小指１']),
    ('右手掌', '右ダミー', ['右ダミー', '右手首', '右親指０', '右人指１', '右中指１', '右薬指１', '右小指１']),
]
# 足：[(刚体名, 绑骨, [顶点来源骨骼])]  -> 胶囊 static
FOOT_PLAN = [
    ('左足首刚体', '左足首', ['左足先EX', '左足首', '左足首D']),
    ('左つま先刚体', '左つま先', ['左つま先', '左足先EX']),
    ('右足首刚体', '右足首', ['右足先EX', '右足首', '右足首D']),
    ('右つま先刚体', '右つま先', ['右つま先', '右足先EX']),
]
# (参考Joint索引, ra产物骨名, rb产物骨名) —— 胸口 4 个（两段胸骨：上半身↔胸0↔胸1）
JOINT_PLAN = [
    (17, '上半身', '左胸0'),
    (18, '左胸0', '左胸1'),
    (19, '上半身', '右胸0'),
    (20, '右胸0', '右胸1'),
]

SHAPE_SPHERE, SHAPE_BOX, SHAPE_CAPSULE = 0, 1, 2


def encode_text(s, encoding):
    """编码文本。encoding 遵循 PMX 标准：0=UTF-16LE, 1=UTF-8"""
    raw = s.encode('utf-16-le' if encoding == 0 else 'utf-8')
    return len(raw).to_bytes(4, 'little', signed=True) + raw


def make_rigid(name, bone_idx, shape, size, pos, rot=(0.0, 0.0, 0.0),
               mass=1.0, typ=0, group=0, nocoll=65534, encoding=0,
               linear_damp=0.0, angular_damp=0.0):
    """从零构造一个刚体条目字节（PMX 2.0，参数区 61 字节，bidx=2）"""
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))          # 名称_en 留空
    out.extend(bone_idx.to_bytes(2, 'little'))     # 绑骨 bidx=2
    out.extend(bytes([group]))                     # 组
    out.extend(nocoll.to_bytes(2, 'little'))       # 非碰撞组
    out.extend(bytes([shape]))                     # 形状
    for v in size:
        out.extend(struct.pack('<f', v))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in rot:
        out.extend(struct.pack('<f', v))
    out.extend(struct.pack('<5f', mass, linear_damp, angular_damp, 0.0, 0.5))  # 质量/线减/角减/反発/摩擦
    out.extend(bytes([typ]))                       # 类型
    return bytes(out)


import math


def parse_vertices(r):
    """解析顶点 -> [(xyz, [骨骼索引,...]), ...]"""
    d = r.data; p = r.vertex_start; out = []
    for _ in range(r.vertex_count):
        xyz = struct.unpack_from('<3f', d, p); p += 12
        p += 12 + 8 + 16 * r.uv_count
        dt = d[p]; p += 1
        bones = []
        if dt == 0:
            bones.append(int.from_bytes(d[p:p+r.bidx], 'little')); p += r.bidx
        elif dt == 1:
            bones += [int.from_bytes(d[p:p+r.bidx], 'little')]; p += r.bidx
            bones += [int.from_bytes(d[p:p+r.bidx], 'little')]; p += r.bidx
            p += 4
        elif dt == 2:
            for _ in range(4):
                bones.append(int.from_bytes(d[p:p+r.bidx], 'little')); p += r.bidx
            p += 16
        elif dt == 3:
            bones.append(int.from_bytes(d[p:p+r.bidx], 'little')); p += r.bidx
            bones.append(int.from_bytes(d[p:p+r.bidx], 'little')); p += r.bidx
            p += 40
        elif dt == 4:
            for _ in range(4):
                bones.append(int.from_bytes(d[p:p+r.bidx], 'little')); p += r.bidx
            p += 16
        p += 4
        out.append((xyz, bones))
    return out


def bone_pos(r, i):
    return struct.unpack_from('<3f', r.bone_items[i][4], 0)


def bone_axis(r, i):
    """返回 (骨骼方向单位向量, 骨骼长度)"""
    raw = r.bone_items[i][4]
    p = 0
    xyz = struct.unpack_from('<3f', raw, p); p += 12
    parent = int.from_bytes(raw[p:p+r.bidx], 'little'); p += r.bidx
    layer = int.from_bytes(raw[p:p+4], 'little', signed=True); p += 4
    flag = int.from_bytes(raw[p:p+2], 'little'); p += 2
    if flag & 0x0001:
        tail = int.from_bytes(raw[p:p+r.bidx], 'little')
        tp = bone_pos(r, tail) if tail < r.bone_count else None
    else:
        tp = struct.unpack_from('<3f', raw, p)
    if tp is None:
        return (0.0, -1.0, 0.0), 1.0
    d = (tp[0]-xyz[0], tp[1]-xyz[1], tp[2]-xyz[2])
    l = math.sqrt(d[0]**2 + d[1]**2 + d[2]**2)
    if l < 1e-9:
        return (0.0, -1.0, 0.0), 1.0
    return (d[0]/l, d[1]/l, d[2]/l), l


def _dot(a, b):
    return a[0]*b[0] + a[1]*b[1] + a[2]*b[2]


def axis_to_euler(a):
    """把胶囊长度方向(Y轴)对齐到 a，返回欧拉角(rad)。绕X处理YZ、绕Z处理XY"""
    ay = abs(a[1]) if abs(a[1]) > 1e-9 else 1.0
    pitch = math.atan2(-a[2], ay)
    roll = math.atan2(a[0], ay)
    return (pitch, 0.0, roll)


def fit_capsule(pts, origin, axis, bone_len):
    """origin=骨骼位置, axis=单位方向 -> (pos, size, rot)
    pos=顶点投影中点(身体段中心), 长度=投影跨度, 半径=P90×0.65"""
    dists = []
    projs = []
    for p in pts:
        v = (p[0]-origin[0], p[1]-origin[1], p[2]-origin[2])
        pr = _dot(v, axis)
        perp = (v[0]-pr*axis[0], v[1]-pr*axis[1], v[2]-pr*axis[2])
        dists.append(math.sqrt(_dot(perp, perp)))
        projs.append(pr)
    dists.sort()
    r = dists[int(len(dists)*0.9)] * 0.65
    projs.sort()
    mid = (projs[0] + projs[-1]) / 2
    pos = (origin[0] + mid*axis[0], origin[1] + mid*axis[1], origin[2] + mid*axis[2])
    l = max(bone_len * 0.85, 0.3)
    return pos, (r, l, 0.0), axis_to_euler(axis)


def collect_pts(verts, bone_set):
    """收集骨骼组所有骨骼绑定的顶点（按坐标去重）"""
    seen = set()
    pts = []
    for v, bs in verts:
        if any(b in bone_set for b in bs):
            key = (round(v[0], 4), round(v[1], 4), round(v[2], 4))
            if key not in seen:
                seen.add(key)
                pts.append(v)
    return pts


def fit_sphere(pts, scale=0.65, offset=(0.0, 0.0, 0.0)):
    """球心用 bbox 中心 + offset；半径 P90×scale"""
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    c = ((min(xs)+max(xs))/2 + offset[0], (min(ys)+max(ys))/2 + offset[1], (min(zs)+max(zs))/2 + offset[2])
    dists = sorted(math.sqrt((p[0]-c[0])**2 + (p[1]-c[1])**2 + (p[2]-c[2])**2) for p in pts)
    r = dists[int(len(dists)*0.9)] * scale
    return c, (r, 0.0, 0.0), (0.0, 0.0, 0.0)


def fit_box(pts, rot=(0.0, 0.0, 0.0), thin_axis=None):
    """用顶点 bbox 拟合板状箱体：指定薄轴(或自动最薄轴)压缩到 35%，整体 ×0.7"""
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    cx = (min(xs)+max(xs))/2; cy = (min(ys)+max(ys))/2; cz = (min(zs)+max(zs))/2
    hx = (max(xs)-min(xs))/2 * 0.7
    hy = (max(ys)-min(ys))/2 * 0.7
    hz = (max(zs)-min(zs))/2 * 0.7
    dims = [hx, hy, hz]
    thin = thin_axis if thin_axis is not None else dims.index(min(dims))
    dims[thin] *= 0.35
    return (cx, cy, cz), (dims[0], dims[1], dims[2]), rot


def fit_torso_box(pts):
    """上半身/上半身2 箱体：z(前后)厚度用顶点分位数 P5~P95 完整包含后背到前胸。
    增粗：厚度不缩小（×1.0），后表面落到后背(P5)之外，箱体完整包住躯干。"""
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = sorted(p[2] for p in pts)
    cx = (min(xs) + max(xs)) / 2
    cy = (min(ys) + max(ys)) / 2
    hx = (max(xs) - min(xs)) / 2 * 0.7
    hy = (max(ys) - min(ys)) / 2 * 0.7
    n = len(zs)
    back = zs[int(n * 0.05)]     # 后背（P5，包含后背）
    front = zs[int(n * 0.95)]    # 前胸（P95）
    cz = (back + front) / 2
    hz = max((front - back) / 2, 0.15)   # 厚度不缩小，完整包含（增粗）
    return (cx, cy, cz), (hx, hy, hz), (0.0, 0.0, 0.0)


def shrink_torso_for_bust(torso, busts):
    """自动化检测：躯干箱体若覆盖胸部球形碰撞箱(胸球)，缩减箱体前表面(z负)到胸球后表面，
    后表面(z正)保持不变（仍覆盖后背）。busts=[(中心, 半径)]。"""
    bx, by, bz = torso[0]
    hx, hy, hz = torso[1]
    box_back = bz + hz          # 后表面（后背侧）
    box_front = bz - hz         # 前表面（前胸侧）
    new_front = box_front
    for bpos, br in busts:
        bust_back = bpos[2] + br   # 胸球后表面
        if box_front < bust_back:  # 箱体前表面盖过胸球
            new_front = max(new_front, bust_back)
    if new_front > box_front:
        bz = (box_back + new_front) / 2
        hz = (box_back - new_front) / 2
    return (bx, by, bz), (hx, hy, hz)


def probe_face_mult(r):
    """探测面区单位：3=每面3索引(标准)，1=每面1索引(非标准)，None=无法判定"""
    d = r.data

    def _valid_tex(p):
        if p + 4 > len(d):
            return None
        tc = int.from_bytes(d[p:p + 4], 'little')
        if tc <= 0 or tc > 100000:
            return None
        q = p + 4
        for _ in range(tc):
            n = int.from_bytes(d[q:q + 4], 'little', signed=True)
            q += 4
            if n < 0 or n > 65536 or q + n > len(d):
                return None
            q += n
        return tc

    r3 = _valid_tex(r.face_start + r.face_count * 3 * r.vidx)
    r1 = _valid_tex(r.face_start + r.face_count * r.vidx)
    return 3 if r3 else (1 if r1 else None)


def parse_ref_joints():
    """胸口 4 个 Joint 的 header + param（内置，不再依赖参考模型文件）。"""
    return None, CHEST_JOINT_BLOCKS


def add_body_physics(src, dst, chest=True, foot_simplified=False):
    """chest=False 时不生成胸部(左胸0/1、右胸0/1)及胸 Joint；foot_simplified=True 时仅生成足首刚体(适配无趾的简化脚)。"""
    print('=' * 60)
    print(f'添加基础人体刚体+手掌+足(权重拟合): {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 输入解析未对齐 remaining={r.remaining}')
        return False
    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    idx = {n: i for i, n in enumerate(names) if n}
    idx_g = {n: i for i, n in enumerate(name_gs) if n}
    verts = parse_vertices(r)

    def find_bone(*cands):
        for c in cands:
            i = idx.get(c) or idx_g.get(c)
            if i is not None:
                return i
        return None

    new_rigid = bytearray()
    bone2rigid = {}
    rigid_poss = []   # 刚体索引 -> pos（用于 Joint 锚点定位）
    rid = 0

    def emit(nm, bi, shape, size, pos, rot, typ, mass=1.0, angular_damp=0.0):
        nonlocal rid
        # 碰撞组拆分：躯干/头/胸/手臂 → 组0（长发碰，防穿模）；腿/手掌/足 → 组5（长发不碰）
        TORSO = {'下半身', '上半身', '上半身2', '首', '頭',
                 '左胸0', '右胸0', '左胸1', '右胸1',
                 '左腕', '右腕', '左ひじ', '右ひじ'}
        if nm in TORSO:
            grp, nc = 0, 65534
        else:
            grp, nc = 5, 65503
        new_rigid.extend(make_rigid(nm, bi, shape, size, pos, rot, mass=mass, typ=typ,
                                    group=grp, nocoll=nc,
                                    encoding=r.encoding, angular_damp=angular_damp))
        bone2rigid[nm] = rid
        rigid_poss.append(pos)
        rid += 1
        print(f'  刚体[{rid-1:2d}] {nm} <- 绑骨={names[bi]} size={tuple(round(v,3) for v in size)} rot={tuple(round(v,2) for v in rot)} mass={mass} ad={angular_damp}')
        return rid - 1

    def gather(bone_list):
        return {find_bone(b) for b in bone_list} - {None}

    hair_names = [n for n in names if '髪' in n and '後' not in n]  # 贴头头发(前髪/横髪)，排除後髪马尾
    head_bones = gather(['頭'] + hair_names)

    # 预计算胸球位置（供躯干箱体缩减检测，避免箱体覆盖胸部球形碰撞箱）
    busts = []
    if chest:
        for _bn in ('左胸1', '右胸1'):
            _bi = find_bone(_bn)
            if _bi is None:
                continue
            _pts = collect_pts(verts, gather([_bn]))
            if len(_pts) >= 3:
                _pos, _size, _ = fit_sphere(_pts)
                busts.append((_pos, _size[0]))

    # ---- 1. 基础人体（权重拟合）----
    CHEST_NAMES = {'左胸0', '左胸1', '右胸0', '右胸1'}
    for bn, bone_list, dir_bone, shape, typ in BODY_PLAN:
        if not chest and bn in CHEST_NAMES:
            continue
        bi = find_bone(bn)
        if bi is None:
            print(f'  [SKIP] 缺骨 {bn}')
            continue
        pts = collect_pts(verts, gather(bone_list))
        if len(pts) < 3:
            print(f'  [SKIP] {bn} 顶点不足({len(pts)})')
            continue

        if bn == '頭':
            # 頭骨为球底，球心在上方+向后
            hp = bone_pos(r, bi)
            hpts = collect_pts(verts, head_bones)
            n = len(hpts)
            c = [sum(p[k] for p in hpts)/n for k in range(3)]
            dists = sorted(math.sqrt((p[0]-c[0])**2 + (p[1]-c[1])**2 + (p[2]-c[2])**2) for p in hpts)
            rad = dists[int(n*0.9)] * 0.55
            pos = (hp[0], hp[1] + rad * 0.85, hp[2] - 0.25)
            size, rot = (rad, 0.0, 0.0), (0.0, 0.0, 0.0)
            emit(bn, bi, SHAPE_SPHERE, size, pos, rot, typ)
            continue

        if shape == 'sphere':
            pos, size, rot = fit_sphere(pts)
            m = 6.0 if bn in ('左胸1', '右胸1') else 1.0   # 胸球加大质量(惯性)
            ad = 0.2 if bn in ('左胸1', '右胸1') else 0.0   # 胸球角度减衰(阻尼)，让摆动衰减停下
            emit(bn, bi, SHAPE_SPHERE, size, pos, rot, typ, mass=m, angular_damp=ad)
            continue

        if shape == 'box':
            pos, size, rot = fit_torso_box(pts)   # 上半身箱体：z 厚度用分位数贴合后背
            pos, size = shrink_torso_for_bust((pos, size), busts)  # 自动化缩减，避免覆盖胸球
            if bn == '上半身2':
                # 上半身2 上边缘不超过腕骨(左腕/右腕)上边缘
                top_y = None
                arm = find_bone('左腕') or find_bone('右腕')
                if arm is not None:
                    top_y = bone_pos(r, arm)[1]
                if top_y is not None:
                    size = (size[0], max(0.2, top_y - pos[1]), size[2])
            emit(bn, bi, SHAPE_BOX, size, pos, rot, typ)
            continue

        # 胶囊
        if bn == '下半身':
            axis = (0.0, 1.0, 0.0)
            ys = [p[1] for p in pts]
            blen = max(ys) - min(ys)
            pos, size, rot = fit_capsule(pts, bone_pos(r, bi), axis, blen)
            rot = (-0.15, 0.0, 0.0)   # 前倾（方向翻转）
        elif dir_bone is None:
            axis = (0.0, 1.0, 0.0)
            ys = [p[1] for p in pts]
            blen = max(ys) - min(ys)
            pos, size, rot = fit_capsule(pts, bone_pos(r, bi), axis, blen)
            pos = (pos[0], pos[1], pos[2] + 0.6)   # 上半身进一步前移
        else:
            di = find_bone(dir_bone)
            axis, blen = bone_axis(r, di)
            if bn in ('左ひじ', '右ひじ'):
                blen *= 1.3   # 小臂向大臂延长，形成肘关节
            if bn in ('左腕', '右腕'):
                blen *= 1.2   # 上臂向躯干延长，填补与上半身箱体的缝隙
            pos, size, rot = fit_capsule(pts, bone_pos(r, bi), axis, blen)
            if bn in ('左胸0', '右胸0'):
                size = (size[0] * 0.3, size[1], size[2])   # 胸0锚定缩小为细连接，避免与胸球重叠抖动
        emit(bn, bi, SHAPE_CAPSULE, size, pos, rot, typ)

    # ---- 2. 手掌（板状箱体 static）----
    for nm, bind_bone, bone_list in HAND_PLAN:
        bi = find_bone(bind_bone)
        if bi is None:
            print(f'  [SKIP] 缺手骨 {bind_bone}')
            continue
        pts = collect_pts(verts, gather(bone_list))
        if len(pts) < 3:
            print(f'  [SKIP] {nm} 顶点不足({len(pts)})')
            continue
        rz = 0.79 if '左' in bind_bone else -0.79   # 手掌沿手臂方向旋转(±45°)
        pos, size, rot = fit_box(pts, rot=(0.0, 0.0, rz), thin_axis=0)   # 手掌固定左右(x)薄
        wrist_bone = '左手首' if '左' in bind_bone else '右手首'
        wi = find_bone(wrist_bone)
        if wi is not None:
            wp = bone_pos(r, wi)
            d = (wp[0]-pos[0], wp[1]-pos[1], wp[2]-pos[2])
            dl = math.sqrt(d[0]**2 + d[1]**2 + d[2]**2)
            if dl > 1e-9:
                pos = (pos[0] + d[0]/dl*0.15, pos[1] + d[1]/dl*0.15, pos[2] + d[2]/dl*0.15)  # 向手腕微量偏移
        emit(nm, bi, SHAPE_BOX, size, pos, rot, 0)

    # ---- 3. 足 ----
    foot_plan = FOOT_PLAN if not foot_simplified else [x for x in FOOT_PLAN if x[0].endswith('足首刚体')]
    for nm, bind_bone, bone_list in foot_plan:
        bi = find_bone(bind_bone)
        if bi is None:
            print(f'  [SKIP] 缺足骨 {bind_bone}')
            continue
        pts = collect_pts(verts, gather(bone_list))
        if len(pts) < 3:
            print(f'  [SKIP] {nm} 顶点不足({len(pts)})')
            continue
        if nm.endswith('足首刚体'):
            # 脚踝大块：胶囊，绑足首，方向足首→足先EX，半径小包含在脚背内
            tail_bone = '左足先EX' if '左' in nm else '右足先EX'
            ti = find_bone(tail_bone)
            bp = bone_pos(r, bi)
            if ti is not None:
                tp = bone_pos(r, ti)
                d = (tp[0]-bp[0], tp[1]-bp[1], tp[2]-bp[2])
                blen = math.sqrt(d[0]**2 + d[1]**2 + d[2]**2)
                axis = (d[0]/blen, d[1]/blen, d[2]/blen)
            else:
                axis, blen = (0.0, -1.0, 0.0), 1.0
                if foot_simplified and len(pts) >= 3:
                    ys = [p[1] for p in pts]
                    blen = max(max(ys) - min(ys), 0.3)   # 简化脚(无趾)：足首段长用顶点Y跨度适配
            pos, size, rot = fit_capsule(pts, bp, axis, blen)
            size = (size[0]*0.5, size[1], size[2])   # 半径再缩小，包含在脚背内
            emit(nm, bi, SHAPE_CAPSULE, size, pos, rot, 0)
        else:
            pos, size, rot = fit_box(pts)
            size = (size[0]*0.6, size[1]*0.6, size[2]*0.6)
            emit(nm, bi, SHAPE_BOX, size, pos, rot, 0)

    # ---- 4. 移植胸口 4 个 Joint（骨名 -> 刚体索引）----
    rr, joint_blocks = parse_ref_joints()
    new_joint = bytearray()
    jc = 0
    if chest:
        for ref_j, ra_bone, rb_bone in JOINT_PLAN:
            if ref_j not in joint_blocks:
                print(f'  [SKIP] 缺参考约束 {ref_j}')
                continue
            ra = bone2rigid.get(ra_bone)
            rb = bone2rigid.get(rb_bone)
            if ra is None or rb is None:
                print(f'  [SKIP] 约束 {ref_j} 引用刚体不在({ra_bone}/{rb_bone})')
                continue
            header, param = joint_blocks[ref_j]
            # Joint 锚点 pos 用产物 rb 刚体的位置（参考模型世界坐标会错位）
            rb_pos = rigid_poss[rb]
            param = struct.pack('<3f', rb_pos[0], rb_pos[1], rb_pos[2]) + param[12:]
            # 摆动 Joint（胸球）设置乳摇参数：旋转限 ±0.6 rad + Y轴移动限放开 + 弹簧
            if rb_bone in ('左胸1', '右胸1'):
                mov_lo = (-0.05, -0.3, -0.05)     # 移动限：Y轴放开 ±0.3（上下抖动）
                mov_hi = (0.05, 0.3, 0.05)
                rot_lo = (-0.6, -0.6, -0.6)
                rot_hi = (0.6, 0.6, 0.6)
                spring_m = (100.0, 300.0, 100.0)  # 弹簧-移动：Y轴 300（上下弹性）
                spring_r = (200.0, 200.0, 200.0)  # 弹簧-旋转
                param = (param[:24]
                         + struct.pack('<3f', *mov_lo) + struct.pack('<3f', *mov_hi)
                         + struct.pack('<3f', *rot_lo) + struct.pack('<3f', *rot_hi)
                         + struct.pack('<3f', *spring_m) + struct.pack('<3f', *spring_r))
            new_joint.extend(header)
            new_joint.extend(ra.to_bytes(r.ridx, 'little'))
            new_joint.extend(rb.to_bytes(r.ridx, 'little'))
            new_joint.extend(param)
            jc += 1
            print(f'  约束[{ref_j}] ra={ra}({ra_bone}) rb={rb}({rb_bone}) pos={tuple(round(v,2) for v in rb_pos)}')

    # ---- 4b. 方式B：生成后删除胸0刚体（区别于"不生成胸0"）----
    del_idx = {bone2rigid.get('左胸0'), bone2rigid.get('右胸0')}
    del_idx = {i for i in del_idx if i is not None}
    if del_idx:
        # 解析刚体条目（名称 + 名称_en + 绑骨 + 61 参数）
        items = []
        p = 0
        while p < len(new_rigid):
            start = p
            n1 = int.from_bytes(new_rigid[p:p + 4], 'little', signed=True); p += 4 + n1
            n2 = int.from_bytes(new_rigid[p:p + 4], 'little', signed=True); p += 4 + n2
            p += r.bidx + 61
            items.append(bytes(new_rigid[start:p]))
        old2new = {}
        kept = []
        ni = 0
        for i, b in enumerate(items):
            if i in del_idx:
                continue
            old2new[i] = ni
            kept.append(b)
            ni += 1
        new_rigid = b''.join(kept)
        rid = ni
        # 解析 Joint 条目，删除胸0锚定 Joint，胸球 Joint 的 ra 重映射到上半身
        jitems = []
        p = 0
        while p < len(new_joint):
            start = p
            n1 = int.from_bytes(new_joint[p:p + 4], 'little', signed=True); p += 4 + n1
            n2 = int.from_bytes(new_joint[p:p + 4], 'little', signed=True); p += 4 + n2
            p += 1  # Joint種類
            ra_rel = p - start
            ra = int.from_bytes(new_joint[p:p + r.ridx], 'little'); p += r.ridx
            rb_rel = p - start
            rb = int.from_bytes(new_joint[p:p + r.ridx], 'little'); p += r.ridx
            p += 96
            jitems.append((bytes(new_joint[start:p]), ra_rel, rb_rel, ra, rb))
        upper = bone2rigid.get('上半身')
        new_joint2 = bytearray()
        jc2 = 0
        for jb, ra_rel, rb_rel, ra, rb in jitems:
            if rb in del_idx:
                continue  # 删除"上半身↔胸0"锚定 Joint
            ba = bytearray(jb)
            if ra in del_idx:
                ra2 = upper if upper is not None else ra
            else:
                ra2 = old2new.get(ra, ra)
            rb2 = old2new.get(rb, rb)
            ba[ra_rel:ra_rel + r.ridx] = ra2.to_bytes(r.ridx, 'little')
            ba[rb_rel:rb_rel + r.ridx] = rb2.to_bytes(r.ridx, 'little')
            new_joint2.extend(ba)
            jc2 += 1
        new_joint = bytes(new_joint2)
        jc = jc2
        print(f'  [方式B] 生成后删除胸0刚体: 刚体 {len(items)}→{rid}, 约束 {len(jitems)}→{jc2}')

    # ---- 5. 组装输出 ----
    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little')); out.extend(r.vertex_bytes)
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(r.bone_count.to_bytes(4, 'little')); out.extend(r.bone_bytes)
    out.extend(r.morph_count.to_bytes(4, 'little')); out.extend(r.morph_bytes)
    out.extend(r.frame_count.to_bytes(4, 'little')); out.extend(r.frame_bytes)
    out.extend(rid.to_bytes(4, 'little'))
    out.extend(new_rigid)
    out.extend(jc.to_bytes(4, 'little'))
    out.extend(new_joint)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    # ---- 6. 验证 ----
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.rigid_count == rid, f'刚体数 {v.rigid_count} != {rid}'
    assert v.joint_count == jc, f'约束数 {v.joint_count} != {jc}'
    print(f'  [OK] 刚体 {r.rigid_count}->{v.rigid_count}, 约束 {r.joint_count}->{v.joint_count}, remaining=0')
    print(f'  写出: {os.path.basename(dst)}')
    return True




