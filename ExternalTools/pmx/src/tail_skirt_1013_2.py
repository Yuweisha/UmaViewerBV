# -*- coding: utf-8 -*-
"""
给新模型生成 1013_2 风格「尾巴刚体 + 裙子刚体」及链式约束
============================================================================
数据取自 Archieve/Mejiro McQueen目白麦昆决胜服/1013_2_Mejiro McQueen.pmx 实测。

## 一、尾巴（Sp_Hi_Tail0_B_00~04，5 段）
    刚体：胶囊，半径 0.40，每段一个
      mass 梯度 9.0/7.113/4.55/1.987/0.1（发根重→发尾轻）
      ld 0.9/0.921/0.95/0.978/0.999、ad 1/1.212/1.5/1.788/2
      group=10  nc=64511
    约束：链式 Hip→发1→发2→发3→发4→发5，旋转限梯度（发根锁死→发尾约15°）

## 二、裙子（Skirt_0~5_0~9，6 层 × 10 方向，环形）
    刚体：箱，mass=13，ld 按层梯度 0.899/0.924/0.949/0.974/0.999/0.999、ad=1
      group=7  nc=65407
    约束：锚定(Hip→层0) + 纵向链(层间) + 环向链(同层)，旋转限 ±10°(环向 Y 轴 ±5°)

用法：python src/tail_skirt_1013_2.py 输入.pmx 输出.pmx
前置：输入须已有 Hip 刚体（先跑 body_physics），且尾巴/裙子骨骼 name_g 匹配。
"""
import os
import sys
import struct
import math
import re

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ============ 尾巴参数（1013_2 实测） ============
TAIL_G = 'Hip'
TAIL_RE = re.compile(r'^Sp_Hi_Tail0_B_(\d+)$')
TAIL_RADIUS = 0.40
TAIL_MASS = (9.0, 7.113, 4.55, 1.987, 0.1)
TAIL_LD = (0.9, 0.921, 0.95, 0.978, 0.999)
TAIL_AD = (1.0, 1.212, 1.5, 1.788, 2.0)
TAIL_GROUP = 10
TAIL_NOCOLL = 64511
# 链式约束旋转限（弧度，发根→发尾），X/Y/Z
TAIL_ROT_LIMITS = [
    (0.0, 0.0, 0.0),
    (0.056, 0.037, 0.074),
    (0.131, 0.087, 0.175),
    (0.206, 0.138, 0.275),
    (0.262, 0.175, 0.349),
]

# ============ 裙子参数（1033_3 圣诞爱慕织姬 长裙实测，2026-08-30 学习） ============
SKIRT_RE = re.compile(r'^Skirt_(\d+)_(\d+)$')
# 1025 风格裙子：Sp_Hi_MSkirt0_{方向}_{段}（只裙号0；Sp_Hi_MSkirt1_* 是特殊骨骼，排除）
SKIRT2_RE = re.compile(r'^Sp_Hi_MSkirt0_([A-Z]+)_(\d+)$')
SKIRT_MASS_TOP = 5.0       # 顶层质量（随层线性递减到底层；1033_3 实测 mass=(35-2层)/7）
SKIRT_MASS_BOTTOM = 1.0    # 底层质量
SKIRT_LD = 0.9999          # 线阻尼（恒定极高，防抖）
SKIRT_AD = 0.9999          # 角阻尼（恒定极高，防抖）
SKIRT_GROUP = 3            # 碰撞组（1033_3 实测 g3）
SKIRT_NOCOLL = 65527       # 非碰撞掩码（1033_3 实测 nc65527）
SKIRT_DEPTH = 0.25         # 半深（径向厚度，1033_3 实测 0.25）
SKIRT_ROT_LO = (-0.262, 0.0, -0.175)    # 旋转限下限 X-15°、Y0°、Z-10°（1033_3 实测，锚定/纵向/环向统一非对称）
SKIRT_ROT_HI = (0.0, 1.047, 0.175)      # 旋转限上限 X0°、Y+60°、Z+10°

SHAPE_SPHERE, SHAPE_BOX, SHAPE_CAPSULE = 0, 1, 2
TYP_DYNAMIC, TYP_DYNAMIC_ALIGN = 1, 2


def encode_text(s, encoding):
    raw = s.encode('utf-16-le' if encoding == 0 else 'utf-8')
    return len(raw).to_bytes(4, 'little', signed=True) + raw


def axis_to_euler(a):
    ay = a[1]
    rx = math.atan2(a[2], ay)
    rz = math.atan2(-a[0], math.sqrt(ay * ay + a[2] * a[2]))
    return (rx, 0.0, rz)


def norm(d):
    l = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    if l < 1e-9:
        return (0.0, 1.0, 0.0), 0.0
    return (d[0] / l, d[1] / l, d[2] / l), l


def bone_pos(r, i):
    return struct.unpack_from('<3f', r.bone_items[i][4], 0)


def make_rigid(name, bone_idx, shape, size, pos, rot, typ, mass, ld, ad, bidx, encoding, group, nocoll):
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))
    out.extend(bone_idx.to_bytes(bidx, 'little'))
    out.extend(bytes([group]))
    out.extend(nocoll.to_bytes(2, 'little'))
    out.extend(bytes([shape]))
    for v in size:
        out.extend(struct.pack('<f', v))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in rot:
        out.extend(struct.pack('<f', v))
    out.extend(struct.pack('<5f', mass, ld, ad, 0.0, 0.0))
    out.extend(bytes([typ]))
    return bytes(out)


def make_joint(name, ra, rb, pos, rot_lim, ridx, encoding, spring_r=0.0, spring_m=0.0, mov_lim=0.0, rot_lo=None, rot_hi=None):
    """6DOF 弹簧：平移锁死 + 三轴旋转限。
    rot_lim=(x,y,z) 生成对称限 ±x/±y/±z；若传 rot_lo/rot_hi 则用非对称（覆盖对称）。"""
    if rot_lo is None:
        rot_lo = (-rot_lim[0], -rot_lim[1], -rot_lim[2])
    if rot_hi is None:
        rot_hi = (rot_lim[0], rot_lim[1], rot_lim[2])
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))
    out.extend(bytes([0]))
    out.extend(ra.to_bytes(ridx, 'little'))
    out.extend(rb.to_bytes(ridx, 'little'))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in (0.0, 0.0, 0.0):
        out.extend(struct.pack('<f', v))                # rot
    for v in (-mov_lim, -mov_lim, -mov_lim):
        out.extend(struct.pack('<f', v))                # mov_lo
    for v in (mov_lim, mov_lim, mov_lim):
        out.extend(struct.pack('<f', v))                # mov_hi
    for v in rot_lo:
        out.extend(struct.pack('<f', v))                # rot_lo
    for v in rot_hi:
        out.extend(struct.pack('<f', v))                # rot_hi
    for v in (spring_m, spring_m, spring_m):
        out.extend(struct.pack('<f', v))                # spring_m
    for v in (spring_r, spring_r, spring_r):
        out.extend(struct.pack('<f', v))                # spring_r
    return bytes(out)


def find_tail_chain(r):
    """识别尾巴骨骼链 Sp_Hi_Tail0_B_*，返回编号升序的骨骼索引列表"""
    lst = []
    for i, (nm, nmg, *_rest) in enumerate(r.bone_items):
        m = TAIL_RE.match(nmg.rstrip())
        if m:
            lst.append((int(m.group(1)), i))
    lst.sort()
    return [i for _, i in lst]


def skirt_mass(t):
    """裙子刚体质量随层/段线性递减：顶层→底层（t∈[0,1]），对齐 1033_3 实测 mass=(35-2层)/7"""
    return SKIRT_MASS_TOP - (SKIRT_MASS_TOP - SKIRT_MASS_BOTTOM) * t


def _vnorm(v):
    l = math.sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2])
    return (v[0] / l, v[1] / l, v[2] / l)


def _vdot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def _vcross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def basis_to_euler(y_dir, x_dir):
    """让箱子 y 轴（长边）顺着 y_dir（骨骼 tail 方向）、x 轴（宽边）顺着 x_dir（环向切向）。
    返回 PMX 欧拉角 (rx, ry, rz)（约定 R=Ry·Rx·Rz）。"""
    y = _vnorm(y_dir)
    dyx = _vdot(x_dir, y)
    xp = (x_dir[0] - dyx * y[0], x_dir[1] - dyx * y[1], x_dir[2] - dyx * y[2])
    x = _vnorm(xp)
    z = _vcross(x, y)
    rx = math.asin(max(-1.0, min(1.0, -z[1])))
    ry = math.atan2(z[0], z[2])
    rz = math.atan2(x[1], y[1])
    return (rx, ry, rz)


def find_skirt(r):
    """识别裙子骨骼 Skirt_层_方向，返回 {层: {方向: 骨骼索引}}"""
    skirt = {}
    for i, (nm, nmg, *_rest) in enumerate(r.bone_items):
        m = SKIRT_RE.match(nmg.rstrip())
        if m:
            layer, dirn = int(m.group(1)), int(m.group(2))
            skirt.setdefault(layer, {})[dirn] = i
    return skirt


def find_skirt2(r):
    """识别 1025 裙子 Sp_Hi_MSkirt{裙号}_{方向}_{段}，返回 {方向: [骨骼索引(段号升序)]}"""
    skirt = {}
    for i, (nm, nmg, *_rest) in enumerate(r.bone_items):
        m = SKIRT2_RE.match(nmg.rstrip())
        if m:
            dirn = m.group(1)
            seg = int(m.group(2))
            skirt.setdefault(dirn, []).append((seg, i))
    out = {}
    for dirn, lst in skirt.items():
        lst.sort()
        out[dirn] = [i for _, i in lst]
    return out


def add_tail_skirt(src, dst, with_skirt=True):
    """with_skirt=False 时只生成尾巴刚体，跳过裙子(Skirt_*/Sp_Hi_MSkirt*)部分。"""
    r = PMXReader(src).parse()
    print('=' * 70)
    print('输入:', src)
    print(f'骨骼={r.bone_count} 刚体={r.rigid_count} 约束={r.joint_count} '
          f'bidx={r.bidx} ridx={r.ridx} encoding={r.encoding}')

    idx_by_g = {nmg.rstrip(): i for i, (nm, nmg, *_rest) in enumerate(r.bone_items)}
    base_rigid = r.rigid_count
    new_rigids = bytearray()
    new_joints = bytearray()
    k = 0
    nj = 0

    # ---- 1. 定位 Hip 刚体（尾巴/裙子锚定到 Hip） ----
    hip_i = idx_by_g.get('Hip')
    hip_rigid = None
    if hip_i is not None:
        for rb_idx in range(r.rigid_count):
            off, bone = r.rigid_bone_points[rb_idx]
            if bone == hip_i:
                hip_rigid = rb_idx
                break
    if hip_rigid is None:
        print('[WARN] 无 Hip 刚体（先跑 body_physics），尾巴/裙子将跳过锚定约束')

    # ---- 2. 尾巴 ----
    tail_chain = find_tail_chain(r)
    if tail_chain:
        n = len(tail_chain)
        pos = [bone_pos(r, i) for i in tail_chain]
        print(f'\n[尾巴] {n} 段骨骼 → {n} 个胶囊，半径={TAIL_RADIUS}')
        rigid_ids = []
        for seg in range(n):
            name = r.bone_items[tail_chain[seg]][0].rstrip()
            if seg < n - 1:
                dvec, seglen = norm((pos[seg + 1][0] - pos[seg][0],
                                     pos[seg + 1][1] - pos[seg][1],
                                     pos[seg + 1][2] - pos[seg][2]))
                mid = ((pos[seg][0] + pos[seg + 1][0]) / 2,
                       (pos[seg][1] + pos[seg + 1][1]) / 2,
                       (pos[seg][2] + pos[seg + 1][2]) / 2)
                rot = axis_to_euler(dvec)
                size = (TAIL_RADIUS, seglen, 0.0)
                typ = TYP_DYNAMIC_ALIGN if seg == 0 else TYP_DYNAMIC
            else:
                dvec, seglen = norm((pos[seg][0] - pos[seg - 1][0],
                                     pos[seg][1] - pos[seg - 1][1],
                                     pos[seg][2] - pos[seg - 1][2]))
                mid = pos[seg]
                rot = axis_to_euler(dvec)
                size = (TAIL_RADIUS, seglen, 0.0)
                typ = TYP_DYNAMIC
            mi = seg if seg < len(TAIL_MASS) else len(TAIL_MASS) - 1
            mass = TAIL_MASS[mi]
            ld = TAIL_LD[mi]
            ad = TAIL_AD[mi]
            rb = make_rigid(name, tail_chain[seg], SHAPE_CAPSULE, size, mid, rot, typ,
                            mass, ld, ad, r.bidx, r.encoding, TAIL_GROUP, TAIL_NOCOLL)
            new_rigids.extend(rb)
            rid = base_rigid + k
            k += 1
            rigid_ids.append(rid)
            print(f'  刚体[{rid}] {name} size=(r{TAIL_RADIUS},h{seglen:.2f}) m={mass:g} ld={ld:g} ad={ad:g} g={TAIL_GROUP}')

        for seg in range(n):
            rb = rigid_ids[seg]
            ra = hip_rigid if seg == 0 else rigid_ids[seg - 1]
            jpos = pos[seg]
            name = r.bone_items[tail_chain[seg]][0].rstrip()
            rl = TAIL_ROT_LIMITS[seg] if seg < len(TAIL_ROT_LIMITS) else TAIL_ROT_LIMITS[-1]
            jb = make_joint(name, ra, rb, jpos, rl, r.ridx, r.encoding)
            new_joints.extend(jb)
            nj += 1
            print(f'  约束 {name}: ra={ra} rb={rb} rot_lim=(±{math.degrees(rl[0]):.0f}°,±{math.degrees(rl[1]):.0f}°,±{math.degrees(rl[2]):.0f}°)')

    # ---- 3. 裙子 ----
    skirt = find_skirt(r) if with_skirt else None
    if skirt:
        layers = sorted(skirt.keys())
        n_layer = len(layers)
        dirs = sorted(skirt[layers[0]].keys())
        n_dir = len(dirs)
        print(f'\n[裙子] {n_layer} 层 × {n_dir} 方向，mass={SKIRT_MASS_TOP}→{SKIRT_MASS_BOTTOM} 递减')

        skirt_rigids = {}
        for layer in layers:
            for dirn in dirs:
                bi = skirt[layer][dirn]
                p = bone_pos(r, bi)
                # pos 偏移到层间中点 + 骨骼 tail 方向（箱子长边顺着骨骼）
                half_h = 0.3
                center = p
                tail_dir = (0.0, -1.0, 0.0)
                if layer + 1 in skirt and dirn in skirt[layer + 1]:
                    q = bone_pos(r, skirt[layer + 1][dirn])
                    half_h = math.sqrt((p[0]-q[0])**2 + (p[1]-q[1])**2 + (p[2]-q[2])**2) / 2
                    center = ((p[0]+q[0])/2, (p[1]+q[1])/2, (p[2]+q[2])/2)
                    tail_dir = (q[0]-p[0], q[1]-p[1], q[2]-p[2])
                elif layer - 1 in skirt and dirn in skirt[layer - 1]:
                    q0 = bone_pos(r, skirt[layer - 1][dirn])
                    tail_dir = (p[0]-q0[0], p[1]-q0[1], p[2]-q0[2])
                nxt = skirt[layer].get((dirn + 1) % n_dir)
                half_w = 0.5
                tan_dir = None
                if nxt is not None:
                    q = bone_pos(r, nxt)
                    half_w = math.sqrt((p[0]-q[0])**2 + (p[1]-q[1])**2 + (p[2]-q[2])**2) / 2
                    tan_dir = (q[0]-p[0], 0.0, q[2]-p[2])
                size = (half_w, half_h, SKIRT_DEPTH)
                name = r.bone_items[bi][0].rstrip()
                mass = skirt_mass(layer / max(n_layer - 1, 1))
                if tan_dir is not None and (tan_dir[0] or tan_dir[2]):
                    rot = basis_to_euler(tail_dir, tan_dir)
                else:
                    rot = (0.0, 0.0, 0.0)
                rb = make_rigid(name, bi, SHAPE_BOX, size, center, rot, TYP_DYNAMIC,
                                mass, SKIRT_LD, SKIRT_AD, r.bidx, r.encoding, SKIRT_GROUP, SKIRT_NOCOLL)
                new_rigids.extend(rb)
                rid = base_rigid + k
                k += 1
                skirt_rigids[(layer, dirn)] = rid
            print(f'  层{layer} {n_dir} 个箱刚体，ld={SKIRT_LD}')

        # 锚定（Hip→层0）+ 纵向链（层间）+ 环向链（同层）
        for layer in layers:
            for dirn in dirs:
                rb = skirt_rigids[(layer, dirn)]
                bi = skirt[layer][dirn]
                p = bone_pos(r, bi)
                name = r.bone_items[bi][0].rstrip()
                if layer == 0 and hip_rigid is not None:
                    jb = make_joint(name, hip_rigid, rb, p, (0, 0, 0), r.ridx, r.encoding, rot_lo=SKIRT_ROT_LO, rot_hi=SKIRT_ROT_HI)
                    new_joints.extend(jb)
                    nj += 1
                elif layer > 0:
                    ra = skirt_rigids[(layer - 1, dirn)]
                    jb = make_joint(name, ra, rb, p, (0, 0, 0), r.ridx, r.encoding, rot_lo=SKIRT_ROT_LO, rot_hi=SKIRT_ROT_HI)
                    new_joints.extend(jb)
                    nj += 1
                nxt = (dirn + 1) % n_dir
                if nxt in skirt[layer]:
                    rb2 = skirt_rigids[(layer, nxt)]
                    jb = make_joint(name + '_ring', rb, rb2, p, (0, 0, 0), r.ridx, r.encoding, rot_lo=SKIRT_ROT_LO, rot_hi=SKIRT_ROT_HI)
                    new_joints.extend(jb)
                    nj += 1
        print('  锚定+纵向链+环向链完成')

    # ---- 3b. 1025 风格裙子（Sp_Hi_MSkirt*_方向_段，垂直链） ----
    skirt2 = find_skirt2(r) if with_skirt else None
    if skirt2:
        print(f'\n[裙子2(1025风格)] {len(skirt2)} 个方向')
        center = bone_pos(r, hip_i) if hip_i is not None else (0, 0, 0)

        def dir_angle(dirn):
            p = bone_pos(r, skirt2[dirn][0])
            return math.atan2(p[0] - center[0], p[2] - center[2])

        dirs_sorted = sorted(skirt2.keys(), key=dir_angle)
        n_dir = len(dirs_sorted)

        # 生成箱刚体（每段一个，顺着方向朝向 + 向外俯仰 + 横向延长减小缝隙）
        skirt2_rigids = {}
        for di, dirn in enumerate(dirs_sorted):
            chain = skirt2[dirn]
            n = len(chain)
            pos = [bone_pos(r, i) for i in chain]
            ang = dir_angle(dirn)
            ndirn = dirs_sorted[(di + 1) % n_dir]          # 下一个相邻方向（环向）
            nchain = skirt2[ndirn]
            npos = [bone_pos(r, i) for i in nchain]
            for seg in range(n):
                bi = chain[seg]
                p = pos[seg]
                # pos 偏移到段间中点 + 骨骼 tail 方向（箱子长边顺着骨骼）
                half_h = 0.3
                center = p
                tail_dir = (0.0, -1.0, 0.0)
                if seg < n - 1:
                    q = pos[seg + 1]
                    half_h = math.sqrt((p[0]-q[0])**2 + (p[1]-q[1])**2 + (p[2]-q[2])**2) / 2
                    center = ((p[0]+q[0])/2, (p[1]+q[1])/2, (p[2]+q[2])/2)
                    tail_dir = (q[0]-p[0], q[1]-p[1], q[2]-p[2])
                elif seg > 0:
                    tail_dir = (p[0]-pos[seg-1][0], p[1]-pos[seg-1][1], p[2]-pos[seg-1][2])
                # 半宽 = 环向相邻方向距离的一半（去钳制）+ 水平切向
                half_w = 0.6
                tan_dir = None
                if seg < len(nchain):
                    q2 = npos[seg]
                    d = math.sqrt((p[0]-q2[0])**2 + (p[1]-q2[1])**2 + (p[2]-q2[2])**2)
                    half_w = d / 2
                    tan_dir = (q2[0]-p[0], 0.0, q2[2]-p[2])
                mass = skirt_mass(seg / max(n - 1, 1))
                size = (half_w, half_h, SKIRT_DEPTH)
                # 箱子长边（y）顺着骨骼 tail、宽边（x）顺着环向切向
                if tan_dir is not None and (tan_dir[0] or tan_dir[2]):
                    rot = basis_to_euler(tail_dir, tan_dir)
                else:
                    rot = (0.0, ang, 0.0)
                name = r.bone_items[bi][0].rstrip()
                rb = make_rigid(name, bi, SHAPE_BOX, size, center, rot, TYP_DYNAMIC,
                                mass, SKIRT_LD, SKIRT_AD, r.bidx, r.encoding, SKIRT_GROUP, SKIRT_NOCOLL)
                new_rigids.extend(rb)
                rid = base_rigid + k
                k += 1
                skirt2_rigids[(dirn, seg)] = rid

        # 约束：锚定 + 纵向链（段间）+ 环向链（同段相邻方向，防散开/穿模）
        for di, dirn in enumerate(dirs_sorted):
            chain = skirt2[dirn]
            n = len(chain)
            pos = [bone_pos(r, i) for i in chain]
            for seg in range(n):
                rb = skirt2_rigids[(dirn, seg)]
                bi = chain[seg]
                name = r.bone_items[bi][0].rstrip()
                if seg == 0 and hip_rigid is not None:
                    jb = make_joint(name, hip_rigid, rb, pos[seg], (0, 0, 0), r.ridx, r.encoding, rot_lo=SKIRT_ROT_LO, rot_hi=SKIRT_ROT_HI)
                    new_joints.extend(jb); nj += 1
                elif seg > 0:
                    ra = skirt2_rigids[(dirn, seg - 1)]
                    jb = make_joint(name, ra, rb, pos[seg], (0, 0, 0), r.ridx, r.encoding, rot_lo=SKIRT_ROT_LO, rot_hi=SKIRT_ROT_HI)
                    new_joints.extend(jb); nj += 1
                # 环向链（同段相邻方向）
                ndirn = dirs_sorted[(di + 1) % n_dir]
                if seg < len(skirt2[ndirn]):
                    rb2 = skirt2_rigids[(ndirn, seg)]
                    jb = make_joint(name + '_ring', rb, rb2, pos[seg], (0, 0, 0), r.ridx, r.encoding, rot_lo=SKIRT_ROT_LO, rot_hi=SKIRT_ROT_HI)
                    new_joints.extend(jb); nj += 1
        print('  锚定+纵向链+环向链完成')

    # ---- 4. 组装输出 ----
    new_rc = r.rigid_count + k
    new_jc = r.joint_count + nj
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
    out.extend(new_rc.to_bytes(4, 'little'))
    out.extend(r.rigid_bytes)
    out.extend(new_rigids)
    out.extend(new_jc.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)
    out.extend(new_joints)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.rigid_count == new_rc, f'刚体数 {v.rigid_count} != {new_rc}'
    assert v.joint_count == new_jc, f'约束数 {v.joint_count} != {new_jc}'
    print(f'\n[OK] 刚体 {r.rigid_count}->{v.rigid_count}, 约束 {r.joint_count}->{v.joint_count}, remaining=0')
    print(f'写出: {os.path.basename(dst)}')
    return True




