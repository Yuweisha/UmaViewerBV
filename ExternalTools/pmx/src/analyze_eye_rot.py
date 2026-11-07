# -*- coding: utf-8 -*-
"""
[探索] 模拟瞳孔旋转轴 + 计算瞳孔与眼白(眼眶)的相交情况
=====================================================
背景：知识库[16] 眼球両目骨骼处理——用「両目」骨旋转把瞳孔映射到眼眶内移动，
需要知道：瞳孔绕哪个轴/中心旋转、旋转后瞳孔是否会穿出眼白(眼眶)范围。

本脚本对每个候选 PMX：
  1. 定位 両目/左目/右目/頭 骨骼
  2. 提取「瞳孔顶点」(权重含 左目/右目) 与「眼白顶点」(头骨绑定 + 面部材质 + 瞳孔附近)
  3. 计算瞳孔/眼白几何中心、包围盒、拟合眼球球体
  4. 模拟旋转轴（多种方案）旋转瞳孔顶点，用眼白顶点拟合球壳
     计算旋转后瞳孔顶点与眼白边界的「穿透」程度（正=穿出眼眶，负=留在内部）
  5. 输出结论：能否模拟旋转轴、能否计算相交
"""
import sys
import os
import math
import struct

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader, decode_text  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

ROOT = os.path.dirname(SRC)


def read_cstr(data, off):
    n = int.from_bytes(data[off:off + 4], 'little', signed=True)
    off += 4
    return data[off:off + n], off + n


def parse_materials(r):
    """解析材质：[(名称, Diffuse, 纹理索引, 面数)]"""
    data = r.mat_bytes
    p = 0
    mats = []
    for _ in range(r.material_count):
        nm_raw, p = read_cstr(data, p)
        ng_raw, p = read_cstr(data, p)
        nm = decode_text(nm_raw)
        diffuse = struct.unpack_from('<4f', data, p); p += 16
        p += 12 + 4 + 12
        p += 1
        p += 16 + 4
        tex = int.from_bytes(data[p:p + r.tidx], 'little'); p += r.tidx
        p += r.tidx
        p += 1
        toon = data[p]; p += 1
        if toon == 0:
            p += r.tidx
        else:
            p += 1
        _, p = read_cstr(data, p)
        fc = int.from_bytes(data[p:p + 4], 'little'); p += 4
        mats.append((nm, diffuse, tex, fc))
    return mats


def parse_vertices(r):
    """返回 [(位置tuple3, [骨骼索引], [权重])]"""
    data = r.data
    vs = r.vertex_start
    p = 0
    verts = []
    for _ in range(r.vertex_count):
        pos = struct.unpack_from('<3f', data, vs + p); p += 32
        p += r.uv_count * 16
        dt = data[vs + p]; p += 1
        bones, weights = [], []
        if dt == 0:                              # BDEF1
            bones = [int.from_bytes(data[vs + p:vs + p + r.bidx], 'little')]
            weights = [1.0]
            p += r.bidx
        elif dt == 1:                            # BDEF2
            b1 = int.from_bytes(data[vs + p:vs + p + r.bidx], 'little')
            b2 = int.from_bytes(data[vs + p + r.bidx:vs + p + 2 * r.bidx], 'little')
            w = struct.unpack_from('<f', data, vs + p + 2 * r.bidx)[0]
            bones, weights = [b1, b2], [w, 1.0 - w]
            p += r.bidx * 2 + 4
        elif dt == 2:                            # BDEF4
            for k in range(4):
                bones.append(int.from_bytes(data[vs + p + k * (r.bidx + 4):vs + p + k * (r.bidx + 4) + r.bidx], 'little'))
                weights.append(struct.unpack_from('<f', data, vs + p + k * (r.bidx + 4) + r.bidx)[0])
            p += r.bidx * 4 + 16
        elif dt == 3:                            # SDEF
            b1 = int.from_bytes(data[vs + p:vs + p + r.bidx], 'little')
            b2 = int.from_bytes(data[vs + p + r.bidx:vs + p + 2 * r.bidx], 'little')
            w = struct.unpack_from('<f', data, vs + p + 2 * r.bidx)[0]
            bones, weights = [b1, b2], [w, 1.0 - w]
            p += r.bidx * 2 + 4 + 36
        elif dt == 4:                            # QDEF
            for k in range(4):
                bones.append(int.from_bytes(data[vs + p + k * r.bidx:vs + p + (k + 1) * r.bidx], 'little'))
            p += r.bidx * 4 + 16
        else:
            raise ValueError(f'未知变形方式 {dt}')
        p += 4                                   # 边缘倍率
        verts.append((pos, bones, weights))
    return verts


def parse_faces(r):
    data = r.face_bytes
    mult = len(data) // max(1, r.face_count) // r.vidx  # 1 或 3
    idx_list = []
    p = 0
    for _ in range(r.face_count * mult):
        idx_list.append(int.from_bytes(data[p:p + r.vidx], 'little'))
        p += r.vidx
    return idx_list


def parse_verts_of_materials(r):
    """返回 (材质列表, 每材质顶点索引列表)"""
    mats = parse_materials(r)
    idx_list = parse_faces(r)
    mat_verts = {}
    p = 0
    for mi, (nm, diff, tex, fc) in enumerate(mats):
        mat_verts[mi] = idx_list[p:p + fc]
        p += fc
    return mats, mat_verts


def bone_item_pos(item_raw, bidx):
    """骨骼位置 float3（item_raw 前 12 字节）"""
    return struct.unpack_from('<3f', item_raw, 0)


def bbox(pts):
    xs = [p[0] for p in pts]; ys = [p[1] for p in pts]; zs = [p[2] for p in pts]
    return (min(xs), max(xs), min(ys), max(ys), min(zs), max(zs))


def center_of(pts):
    return tuple(sum(p[i] for p in pts) / len(pts) for i in range(3))

def rotate(p, pivot, axis, rad):
    """绕经过 pivot 的 axis 轴旋转向量 p，Rodrigues 公式"""
    ax, ay, az = axis
    n = math.sqrt(ax * ax + ay * ay + az * az)
    if n < 1e-12:
        return p
    ax, ay, az = ax / n, ay / n, az / n
    c = math.cos(rad); s = math.sin(rad)
    v = (p[0] - pivot[0], p[1] - pivot[1], p[2] - pivot[2])
    cross = (ay * v[2] - az * v[1], az * v[0] - ax * v[2], ax * v[1] - ay * v[0])
    dot = ax * v[0] + ay * v[1] + az * v[2]
    rv = (
        v[0] * c + cross[0] * s + ax * dot * (1 - c),
        v[1] * c + cross[1] * s + ay * dot * (1 - c),
        v[2] * c + cross[2] * s + az * dot * (1 - c),
    )
    return (rv[0] + pivot[0], rv[1] + pivot[1], rv[2] + pivot[2])


def fit_sphere(pts):
    """用顶点均值做球心 + 平均半径（眼白壳的近似）"""
    cx, cy, cz = center_of(pts)
    r = sum(math.dist((cx, cy, cz), p) for p in pts) / len(pts)
    return (cx, cy, cz), r


def signed_depth_from_sphere(sc, sr, p):
    """点到球面距离：负=球内(被眼白覆盖/安全)，正=穿出球面"""
    return math.dist(sc, p) - sr


def analyze(pmx_path):
    print('=' * 78)
    print(f'模型: {os.path.basename(pmx_path)}')
    r = PMXReader(pmx_path).parse()
    if r.remaining != 0:
        print(f'  [跳过] 解析未对齐 remaining={r.remaining}')
        return None
    print(f'  顶点={r.vertex_count} 面={r.face_count} 骨骼={r.bone_count} '
          f'材质={r.material_count} bidx={r.bidx} '
          f'face_mult={len(r.face_bytes)//max(1,r.face_count)//r.vidx}')

    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]

    def find_idx(jp_keys, en_keys):
        # 精确匹配优先（避免「左目」误命中「左目IK」）
        for i, n in enumerate(names):
            if any(n == k for k in jp_keys):
                return i
        for i, g in enumerate(name_gs):
            if any(g == k for k in en_keys):
                return i
        for i, n in enumerate(names):
            if any(k in n for k in jp_keys):
                return i
        for i, g in enumerate(name_gs):
            if any(k in g for k in en_keys):
                return i
        return None

    eye_l = find_idx(['左目'], ['Eye_L'])
    eye_r = find_idx(['右目'], ['Eye_R'])
    head = find_idx(['頭'], ['Head'])
    both = find_idx(['両目', '両目戻'], ['Eyes', 'Eye'])

    print(f'  骨骼: 左目={eye_l} 右目={eye_r} 両目={both} 頭={head}')
    if eye_l is None or eye_r is None or head is None:
        print('  [跳过] 缺左目/右目/頭')
        return None

    verts = parse_vertices(r)
    mats, mat_verts = parse_verts_of_materials(r)

    # 瞳孔顶点 = 权重含 左目/右目
    pupil = [i for i, (pos, bones, w) in enumerate(verts)
             if eye_l in bones or eye_r in bones]
    pmats = {}
    for mi, vs in mat_verts.items():
        for v in vs:
            if v in pupil:
                pmats[mi] = pmats.get(mi, 0) + 1
    print(f'  瞳孔顶点={len(pupil)} 材质分布={sorted(pmats.items(), key=lambda x: -x[1])}')

    pupil_pos = [verts[v][0] for v in pupil]
    pc = center_of(pupil_pos)
    print(f'  瞳孔中心={tuple(round(x, 4) for x in pc)}')
    (x0, x1, y0, y1, z0, z1) = bbox(pupil_pos)
    print(f'  瞳孔包围盒 x=[{x0:.3f},{x1:.3f}] y=[{y0:.3f},{y1:.3f}] z=[{z0:.3f},{z1:.3f}]')

    # 眼白顶点 = 头骨绑定 + face材质 + 瞳孔附近
    face_mi = next((mi for mi, (nm, d, t, fc) in enumerate(mats)
                    if 'face' in nm.lower() or '顔' in nm), None)
    if face_mi is not None:
        print(f'  面部材质 face_mi={face_mi} name={mats[face_mi][0]!r}')
    else:
        print('  面部材质 face_mi=None')

    r0 = max(math.dist(pc, p) for p in pupil_pos) * 1.8
    sclera = []
    if face_mi is not None:
        for v in mat_verts[face_mi]:
            if head in verts[v][1] and math.dist(pc, verts[v][0]) <= r0:
                sclera.append(v)
    sclera = list(dict.fromkeys(sclera))
    if not sclera:
        print('  [警告] 无face材质眼白顶点，改用瞳孔周围头骨顶点')
        for i, (pos, bones, w) in enumerate(verts):
            if head in bones and math.dist(pc, pos) <= r0:
                sclera.append(i)
    sclera_pos = [verts[v][0] for v in sclera]
    if not sclera_pos:
        print('  [跳过] 无眼白顶点')
        return None
    print(f'  眼白顶点={len(sclera_pos)}')
    (sx0, sx1, sy0, sy1, sz0, sz1) = bbox(sclera_pos)
    print(f'  眼白包围盒 x=[{sx0:.3f},{sx1:.3f}] y=[{sy0:.3f},{sy1:.3f}] z=[{sz0:.3f},{sz1:.3f}]')
    sc, sr = fit_sphere(sclera_pos)
    print(f'  眼白拟合球 中心={tuple(round(x, 4) for x in sc)} 半径={sr:.4f}')

    return dict(r=r, eye_l=eye_l, eye_r=eye_r, both=both, head=head,
                verts=verts, mats=mats, mat_verts=mat_verts,
                pupil=pupil, pupil_pos=pupil_pos, pc=pc,
                sclera=sclera, sclera_pos=sclera_pos, sc=sc, sr=sr)

def split_pupil(res):
    """瞳孔顶点按绑定骨分左右两组"""
    verts = res['verts']
    el, er = res['eye_l'], res['eye_r']
    left = [i for i in res['pupil'] if el in verts[i][1]]
    right = [i for i in res['pupil'] if er in verts[i][1]]
    return left, right


def run_case(label, res, pts, rot_fn):
    """对每个顶点做 rot_fn 旋转，统计相对眼白球壳的穿出"""
    sc, sr = res['sc'], res['sr']
    n = len(pts)
    out_count = 0
    max_dep = -1e9
    for p in pts:
        rp = rot_fn(p)
        dep = signed_depth_from_sphere(sc, sr, rp)
        max_dep = max(max_dep, dep)
        if dep > 0:
            out_count += 1
    mark = '!穿出眼白' if out_count else 'OK 仍在眼白内'
    print(f'    {label:<26} 穿出={out_count}/{n} 最大穿透={max_dep:+.4f}  {mark}')


def simulate(res):
    r = res['r']
    el, er, eb = res['eye_l'], res['eye_r'], res['both']
    sc, sr = res['sc'], res['sr']
    verts = res['verts']
    lp_idx, rp_idx = split_pupil(res)
    left_p = [verts[i][0] for i in lp_idx]
    right_p = [verts[i][0] for i in rp_idx]
    B = struct.unpack_from('<3f', r.bone_items[eb][4], 0) if eb is not None else res['pc']
    L = struct.unpack_from('<3f', r.bone_items[el][4], 0)
    R = struct.unpack_from('<3f', r.bone_items[er][4], 0)
    print()
    print('--- 模拟瞳孔旋转（眼白球壳穿透判定） ---')
    print(f'  左瞳={len(left_p)} 右瞳={len(right_p)}  両目位置={tuple(round(x, 4) for x in B)}')
    print(f'  左目位置={tuple(round(x, 4) for x in L)}  右目位置={tuple(round(x, 4) for x in R)}')
    print(f'  眼白球 中心={tuple(round(x, 4) for x in sc)} 半径={sr:.4f}')
    print('  (穿透>0 = 瞳孔顶点穿出眼白球面)')

    for deg in (10.0, 20.0, 30.0):
        rad = math.radians(deg)
        print(f'\n  === 旋转 {deg:>4.0f}° ===')
        for ax_name, axis in (('X轴(上下看)', (1, 0, 0)), ('Y轴(左右看)', (0, 1, 0))):
            run_case(f'両目级联{ax_name}', res, left_p,
                     lambda p, ax=axis, b=B: rotate(p, rotate(L, b, ax, rad), ax, rad))
            run_case(f'両目级联{ax_name}', res, right_p,
                     lambda p, ax=axis, b=B: rotate(p, rotate(R, b, ax, rad), ax, rad))
            run_case(f'左目骨{ax_name}', res, left_p, lambda p, ax=axis: rotate(p, L, ax, rad))
            run_case(f'右目骨{ax_name}', res, right_p, lambda p, ax=axis: rotate(p, R, ax, rad))
        run_case('両目级联Z轴(原地转瞳)', res, left_p + right_p,
                 lambda p, b=B: rotate(p, b, (0, 0, 1), rad))




