# -*- coding: utf-8 -*-
"""
[通用] T-Pose → A-Pose 转换（修复 [11b]/[11C] 的"腕权重问题：部分顶点留在原地"）
================================================================================
A-Pose 标准（本脚本目标）：
  旋转后，「腕骨(上臂骨 腕) → 右ひじ」的连线与纵轴(Y轴) 成 45°。
  （即上臂从水平 T-Pose 向下旋转 45°；可改 APOSE_DEG 适配其他角度。）

相对 [11b]（apose_init.py）/ [11C]（apose_1025_2.py）的修复：
  1. ★ 顶点旋转集合纳入「腕」本身：
       旧脚本 rot_set = 腕 的子树（不含 腕），导致 100% 绑在 腕 上的顶点
       （肩窝 / 上臂根部）不被旋转 → 转换后"留在原地"，出现破洞/错位。
       本脚本 vert_rot_set = {腕} ∪ 腕的子树，保证上臂顶点同步旋转。
  2. 手捩(前臂捩) 的固定轴也随旋转更新：
       [11C] 只更新了 腕捩 的固定轴，手捩固定轴仍指向水平，与 1025_2 参考
       (手捩固定轴 = (±0.788,-0.616)) 不符。本脚本两者都按旋转量更新。
  3. 角度统一为 45°（对齐"与纵轴成 45°"标准）。
  4. 通用性：腕/ひじ/手首/腕捩/手捩 按日文名 + 英文 name_g 多路匹配，
      无捩骨、无手捩、尾端为偏移或子骨索引 的模型均兼容。

用法：
  python apose_general.py   # 批量处理当前目录 .pmx（备份到 [Apose]备份_时间戳/，原地覆盖）
"""
import math
import struct
import os
import sys
import glob
import shutil
import datetime

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402

# 目标：腕→ひじ 连线与纵轴(Y)成 45°，即从水平向下 45°
APOSE_DEG = 45.0
_TGT = -APOSE_DEG                      # 从水平计，向下为负
_TOL_DEG = 2.0                         # 幂等守卫容差


# ---------------------------------------------------------------- 骨骼工具
def parent_of(r, i):
    return int.from_bytes(r.bone_items[i][4][12:12 + r.bidx], 'little')


def bone_xyz(r, i):
    return struct.unpack_from('<3f', r.bone_items[i][4], 0)


def collect_subtree(r, par_list, root_idx):
    """收集 root_idx 的全部子孙索引（不含自身）"""
    none_idx = (1 << (8 * r.bidx)) - 1
    res = []
    for i in range(r.bone_count):
        cur = par_list[i]
        while cur != none_idx and cur < r.bone_count:
            if cur == root_idx:
                res.append(i)
                break
            cur = par_list[cur]
    return res


def field_offsets(r, raw):
    """解析 item_raw 各字段偏移；返回 dict。所有偏移相对 item_raw 起点。"""
    flag = int.from_bytes(raw[12 + r.bidx + 4:12 + r.bidx + 6], 'little')
    p = 12 + r.bidx + 4 + 2
    offs = {'flag': flag}
    if flag & 0x0001:
        offs['tail'] = ('index', p)
        p += r.bidx
    else:
        offs['tail'] = ('offset', p)
        p += 12
    if flag & 0x0100:
        p += r.bidx + 4
    if flag & 0x0200:
        p += r.bidx + 4
    if flag & 0x0400:
        offs['fixed_axis'] = p
        p += 12
    if flag & 0x0800:
        offs['local_axis'] = p
        p += 24
    return offs


def rot_xy(x, y, cx, cy, sin, cos):
    """绕 (cx,cy) 在 X-Y 平面旋转"""
    dx, dy = x - cx, y - cy
    return cx + dx * cos - dy * sin, cy + dx * sin + dy * cos


def rot_dir(fx, fy, sin, cos):
    """方向向量（不平移）在 X-Y 平面旋转"""
    return fx * cos - fy * sin, fx * sin + fy * cos


# ---------------------------------------------------------------- 骨骼查找
def lookup(idx_name, idx_g, keys):
    for k in keys:
        if k in idx_name:
            return idx_name[k]
    for k in keys:
        if k in idx_g:
            return idx_g[k]
    return None


def find_arm_bones(names, name_gs):
    """按 (name, name_g) 双路匹配左右臂的 腕/ひじ/手首/腕捩/手捩。返回 {s: dict}"""
    idx_name = {n: i for i, n in enumerate(names) if n}
    idx_g = {g: i for i, g in enumerate(name_gs) if g}
    out = {}
    for s, e in (('左', 'L'), ('右', 'R')):
        arm = lookup(idx_name, idx_g, [f'{s}腕', f'{s}上腕', f'Arm_{e}', f'UpperArm_{e}', f'upperarm_{e}'])
        elbow = lookup(idx_name, idx_g, [f'{s}ひじ', f'{s}肘', f'Elbow_{e}', f'elbow_{e}'])
        wrist = lookup(idx_name, idx_g, [f'{s}手首', f'Wrist_{e}', f'wrist_{e}'])
        twist = lookup(idx_name, idx_g, [f'{s}腕捩', f'{s}腕捩れ', f'ArmRoll_{e}', f'ArmTwist_{e}',
                                         f'Arm twist_{e}', f'arm twist_{e}'])
        wtwist = lookup(idx_name, idx_g, [f'{s}手捩', f'{s}手捩れ', f'WristRoll_{e}', f'WristTwist_{e}',
                                          f'Wrist twist_{e}', f'wrist twist_{e}'])
        out[s] = {'arm': arm, 'elbow': elbow, 'wrist': wrist, 'twist': twist, 'wtwist': wtwist}
    return out


# ---------------------------------------------------------------- 顶点旋转
def rotate_vertices_weighted(r, vertex_bytes, vert_sets, pivots, deltas):
    """顶点+法线 按权重加权旋转。

    vert_sets : {s: set(会旋转的骨骼索引)} —— 关键：包含「腕」本身
    pivots    : {s: (px, py)}          —— 旋转支点（腕头 = 肩关节）
    deltas    : {s: (sin, cos)}        —— 该侧旋转量
    """
    ba = bytearray(vertex_bytes)
    data = r.data
    vs = r.vertex_start
    p = 0
    for _ in range(r.vertex_count):
        pos_off = p
        p += 32 + r.uv_count * 16
        dt = data[vs + p]; p += 1
        bones = []; ws = []
        if dt == 0:
            bones = [int.from_bytes(data[vs + p:vs + p + r.bidx], 'little')]; ws = [1.0]
            p += r.bidx
        elif dt == 1:
            b1 = int.from_bytes(data[vs + p:vs + p + r.bidx], 'little')
            b2 = int.from_bytes(data[vs + p + r.bidx:vs + p + 2 * r.bidx], 'little')
            w = struct.unpack_from('<f', data, vs + p + 2 * r.bidx)[0]
            bones = [b1, b2]; ws = [w, 1.0 - w]
            p += r.bidx * 2 + 4
        elif dt == 2:
            for k in range(4):
                bones.append(int.from_bytes(data[vs + p + k * (r.bidx + 4):vs + p + k * (r.bidx + 4) + r.bidx], 'little'))
                ws.append(struct.unpack_from('<f', data, vs + p + k * (r.bidx + 4) + r.bidx)[0])
            p += r.bidx * 4 + 16
        elif dt == 3:
            b1 = int.from_bytes(data[vs + p:vs + p + r.bidx], 'little')
            b2 = int.from_bytes(data[vs + p + r.bidx:vs + p + 2 * r.bidx], 'little')
            w = struct.unpack_from('<f', data, vs + p + 2 * r.bidx)[0]
            bones = [b1, b2]; ws = [w, 1.0 - w]
            p += r.bidx * 2 + 4 + 36
        elif dt == 4:
            for k in range(4):
                bones.append(int.from_bytes(data[vs + p + k * r.bidx:vs + p + (k + 1) * r.bidx], 'little'))
                ws.append(struct.unpack_from('<f', data, vs + p + 4 * r.bidx + k * 4)[0])
            p += r.bidx * 4 + 16
        p += 4

        # 选侧：任一骨骼在左侧旋转集合 → 左；否则任一在右侧 → 右
        side = None
        for s in ('左', '右'):
            if any(b in vert_sets[s] for b in bones):
                side = s
                break
        if side is None:
            continue
        rset = vert_sets[side]
        px, py = pivots[side]
        sin, cos = deltas[side]
        x, y, z = struct.unpack_from('<3f', ba, pos_off)
        nrm = struct.unpack_from('<3f', ba, pos_off + 12)
        total = sum(ws)
        nx = ny = 0.0
        nnx = nny = 0.0
        for b, w in zip(bones, ws):
            if b in rset:
                tx, ty = rot_xy(x, y, px, py, sin, cos)
                tnx, tny = rot_dir(nrm[0], nrm[1], sin, cos)
            else:
                tx, ty = x, y
                tnx, tny = nrm[0], nrm[1]
            nx += w * tx; ny += w * ty
            nnx += w * tnx; nny += w * tny
        if total > 0:
            struct.pack_into('<2f', ba, pos_off, nx / total, ny / total)
            struct.pack_into('<2f', ba, pos_off + 12, nnx / total, nny / total)
    return bytes(ba)


# ---------------------------------------------------------------- 主处理
def apose_apply(src, dst):
    print('=' * 70)
    print(f'T-Pose→A-Pose(通用, {APOSE_DEG:.0f}°): {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐(剩余{r.remaining}字节)')
        return False
    print(f'  骨骼={r.bone_count} 顶点={r.vertex_count} 目标角={APOSE_DEG:.0f}°')

    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    par_list = [parent_of(r, i) for i in range(r.bone_count)]

    found = find_arm_bones(names, name_gs)
    arms = {}
    for s in ('左', '右'):
        arm = found[s]['arm']
        elbow = found[s]['elbow']
        if arm is None or elbow is None:
            print(f'  [SKIP] 缺少 {s}腕 或 {s}ひじ（无法定位手臂主链）')
            return 'skip'
        arms[s] = (arm, elbow, found[s]['wrist'], found[s]['twist'], found[s]['wtwist'])

    # ---- 当前角度 & 幂等守卫 ----
    cur_deg = {}
    for s, (arm, elbow, wrist, twist, wtwist) in arms.items():
        ax, ay, _ = bone_xyz(r, arm)
        ex, ey, _ = bone_xyz(r, elbow)
        cur_deg[s] = math.degrees(math.atan2(ey - ay, abs(ex - ax)))
        print(f'  {s}臂 当前 腕→ひじ 角度 = {cur_deg[s]:.1f}° (目标 {_TGT:.0f}°)')

    if all(abs(cur_deg[s] - _TGT) < _TOL_DEG for s in arms):
        print(f'  [SKIP] 已为 A-Pose({_TGT:.0f}°)')
        return True

    # ---- 旋转量（左臂负角、右臂正角，镜像对称）----
    deltas = {}
    for s in ('左', '右'):
        d = math.radians(_TGT - cur_deg[s])
        if s == '左':
            deltas[s] = (math.sin(d), math.cos(d))
        else:
            deltas[s] = (math.sin(-d), math.cos(-d))

    # ---- 旋转集合 ----
    vert_sets = {}
    pos_sets = {}
    pivots = {}
    for s, (arm, elbow, wrist, twist, wtwist) in arms.items():
        sub = collect_subtree(r, par_list, arm)
        pos_sets[s] = set(sub)                          # 位置旋转：腕的子树（不含腕）
        vert_sets[s] = set(sub) | {arm}                 # ★ 顶点旋转：含腕本身
        pivots[s] = bone_xyz(r, arm)[:2]
        print(f'  {s}臂: 位置旋转{len(sub)}骨, 顶点旋转{len(vert_sets[s])}骨(含腕)')

    # ---- 重建骨骼区：旋转位置 + 尾偏移 + 固定轴 + 本地轴 ----
    out_bone = bytearray()
    out_bone.extend(r.bone_count.to_bytes(4, 'little'))
    changed = 0
    for i, item in enumerate(r.bone_items):
        ba = bytearray(item[4])
        offs = field_offsets(r, bytes(ba))
        for s, (arm, elbow, wrist, twist, wtwist) in arms.items():
            sin, cos = deltas[s]
            px, py = pivots[s]
            in_arm = (i in pos_sets[s]) or (i == arm)   # 本侧手臂集合(含腕本身)
            if i in pos_sets[s]:
                x, y, z = struct.unpack_from('<3f', ba, 0)
                nx, ny = rot_xy(x, y, px, py, sin, cos)
                struct.pack_into('<3f', ba, 0, nx, ny, z)
                changed += 1
            # 腕 的尾端若为偏移，则同步旋转偏移量（子骨索引方式由子树旋转自动跟随）
            if i == arm:
                t = offs.get('tail')
                if t and t[0] == 'offset':
                    ox, oy, oz = struct.unpack_from('<3f', ba, t[1])
                    nox, noy = rot_dir(ox, oy, sin, cos)
                    struct.pack_into('<3f', ba, t[1], nox, noy, oz)
                    changed += 1
            # 腕捩 / 手捩 固定轴随旋转更新（本侧 twist/wtwist 专属，天然只命中一次）
            if i in (twist, wtwist):
                fo = offs.get('fixed_axis')
                if fo is not None:
                    fx, fy, fz = struct.unpack_from('<3f', ba, fo)
                    nfx, nfy = rot_dir(fx, fy, sin, cos)
                    struct.pack_into('<3f', ba, fo, nfx, nfy, fz)
                    changed += 1
            # 本地轴(0x0800)只对本侧手臂的骨旋转（否则左右两侧叠加会抵消/错转）
            if in_arm:
                lo = offs.get('local_axis')
                if lo is not None:
                    for k in (0, 3):
                        lx, ly, lz = struct.unpack_from('<3f', ba, lo + k * 4)
                        nlx, nly = rot_dir(lx, ly, sin, cos)
                        struct.pack_into('<3f', ba, lo + k * 4, nlx, nly, lz)
                    changed += 1
        out_bone.extend(len(item[2]).to_bytes(4, 'little', signed=True))
        out_bone.extend(item[2])
        out_bone.extend(len(item[3]).to_bytes(4, 'little', signed=True))
        out_bone.extend(item[3])
        out_bone.extend(bytes(ba))
    print(f'  已更新 {changed} 处骨骼字段')

    # ---- 顶点+法线 按权重加权旋转 ----
    vertex_out = rotate_vertices_weighted(r, r.vertex_bytes, vert_sets, pivots, deltas)

    # ---- 组装输出 ----
    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little'))
    out.extend(vertex_out)
    out.extend(r.face_count.to_bytes(4, 'little'))
    out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little'))
    out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little'))
    out.extend(r.mat_bytes)
    out.extend(out_bone)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(r.morph_bytes)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(r.frame_bytes)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(r.rigid_bytes)
    out.extend(r.joint_count.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    # ---- 写回验证 ----
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    assert v.bone_count == r.bone_count, f'验证失败: 骨骼数 {r.bone_count}->{v.bone_count}'
    vn = [it[0].rstrip() for it in v.bone_items]
    vi = {n: i for i, n in enumerate(vn) if n}
    for s, (arm, elbow, wrist, twist, wtwist) in arms.items():
        ax, ay, _ = bone_xyz(v, vi[vn[arm]])
        ex, ey, _ = bone_xyz(v, vi[vn[elbow]])
        ang = math.degrees(math.atan2(ey - ay, abs(ex - ax)))
        print(f'  {s}臂 腕→ひじ = {ang:.1f}° (目标 {_TGT:.0f}°)')
        assert abs(ang - _TGT) < 1.5, f'验证失败: {s}臂角度 {ang}'
    print(f'  [OK] 写入 {os.path.basename(dst)}: 骨骼 {r.bone_count} 剩余字节=0')
    return True




