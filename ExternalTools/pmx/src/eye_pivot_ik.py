# -*- coding: utf-8 -*-
"""
[落地] 目表情边界反推旋转轴(枢轴) + IK 角度限制
====================================================
方法（用户方案）:
  1. 目上下左右表情满值位移 -> 瞳孔边界点(每眼4个)
  2. 球拟合反推旋转中心 O（瞳孔绕 O 旋转经过边界）
  3. 退化检测: |O-P|>3 或 边界角<1° -> 回退眼白球心 C
  4. 目骨位置 = O, IK link 限制角度 = 边界对应角度

结构（同 eye_ik_limit）:
  頭 -> 両目 -> 左目IK/右目IK ->(IK)-> 左目/右目(link 受限)

用法:
  python src/eye_pivot_ik.py 输入.pmx 输出.pmx
"""
import sys
import os
import struct
import math

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402
import analyze_eye_rot as m  # noqa: E402
from eye_morph_control import load_eye_morphs  # noqa: E402
from eye_ik_limit import build_item_raw, remap_item, _write_idx, find_idx  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

IK_UNIT = 0.1
IK_LOOP = 4


def solve3(M, b):
    MM = [M[i][:] + [b[i]] for i in range(3)]
    for col in range(3):
        piv = max(range(col, 3), key=lambda rr: abs(MM[rr][col]))
        MM[col], MM[piv] = MM[piv], MM[col]
        for rr in range(col + 1, 3):
            f = MM[rr][col] / MM[col][col]
            for cc in range(col, 4):
                MM[rr][cc] -= f * MM[col][cc]
    x = [0.0] * 3
    for rr in range(2, -1, -1):
        x[rr] = (MM[rr][3] - sum(MM[rr][cc] * x[cc] for cc in range(rr + 1, 3))) / MM[rr][rr]
    return x


def ball_fit(points, p0):
    A, b = [], []
    for pi in points[1:]:
        A.append([pi[k] - p0[k] for k in range(3)])
        b.append((sum(pi[k] ** 2 for k in range(3)) - sum(p0[k] ** 2 for k in range(3))) / 2.0)
    AtA = [[sum(A[i][xr] * A[i][xc] for i in range(len(A))) for xc in range(3)] for xr in range(3)]
    Atb = [sum(A[i][xr] * b[i] for i in range(len(A))) for xr in range(3)]
    return tuple(solve3(AtA, Atb))


def angle_around(O, p0, p1):
    u0 = tuple((p0[k] - O[k]) / math.dist(O, p0) for k in range(3))
    u1 = tuple((p1[k] - O[k]) / math.dist(O, p1) for k in range(3))
    return math.degrees(math.acos(max(-1, min(1, sum(u0[k] * u1[k] for k in range(3))))))


def rot_angle(O, p0, p1, axis):
    """精确绕轴角：绕X=YZ平面角差(带符号)，绕Y=XZ平面角差(带符号)"""
    v0 = (p0[0] - O[0], p0[1] - O[1], p0[2] - O[2])
    v1 = (p1[0] - O[0], p1[1] - O[1], p1[2] - O[2])
    if axis == 'X':
        return math.degrees(math.atan2(v0[1] * v1[2] - v0[2] * v1[1],
                                       v0[1] * v1[1] + v0[2] * v1[2]))
    return math.degrees(math.atan2(v0[2] * v1[0] - v0[0] * v1[2],
                                   v0[0] * v1[0] + v0[2] * v1[2]))


def pupil_all_states(res, morphs, side):
    """该眼所有瞳孔顶点在 初始+4边界 的位置（每顶点用各自位移，115 点）"""
    verts = res['verts']
    idxs = m.split_pupil(res)[0] if side == 'L' else m.split_pupil(res)[1]
    pre = '左' if side == 'L' else '右'
    states = []
    for i in idxs:
        states.append(verts[i][0])
    for nm in (f'{pre}目上', f'{pre}目下', f'{pre}目内', f'{pre}目外'):
        vals, avg = morphs[nm]
        for v, disp in vals:
            states.append(tuple(verts[v][0][k] + disp[k] for k in range(3)))
    return states


def ball_fit_full(points):
    """最小二乘球拟合（中心化提高数值精度，适用于多顶点 115 点），返回球心 O"""
    n = len(points)
    mean = tuple(sum(p[k] for p in points) / n for k in range(3))
    pts = [tuple(p[k] - mean[k] for k in range(3)) for p in points]
    p0 = pts[0]
    A, b = [], []
    for pi in pts[1:]:
        A.append([pi[k] - p0[k] for k in range(3)])
        b.append((sum(pi[k] ** 2 for k in range(3)) - sum(p0[k] ** 2 for k in range(3))) / 2.0)
    AtA = [[sum(A[i][xr] * A[i][xc] for i in range(len(A))) for xc in range(3)] for xr in range(3)]
    Atb = [sum(A[i][xr] * b[i] for i in range(len(A))) for xr in range(3)]
    O_rel = solve3(AtA, Atb)
    return tuple(O_rel[k] + mean[k] for k in range(3))


def multi_rot_angle(res, O, side, morphs, dirn, axis):
    """该眼所有瞳孔顶点绕 O 的旋转角（带符号），取中位数（鲁棒）"""
    verts = res['verts']
    idxs = m.split_pupil(res)[0] if side == 'L' else m.split_pupil(res)[1]
    pre = '左' if side == 'L' else '右'
    vals, avg = morphs[f'{pre}目{dirn}']
    disp = {v: d for v, d in vals}
    angles = []
    for i in idxs:
        P = verts[i][0]
        dd = disp.get(i, (0.0, 0.0, 0.0))   # 零位移顶点（被步骤5清理删除）按零位移处理，避免 KeyError
        U = tuple(P[k] + dd[k] for k in range(3))
        v0 = (P[0] - O[0], P[1] - O[1], P[2] - O[2])
        v1 = (U[0] - O[0], U[1] - O[1], U[2] - O[2])
        if axis == 'X':
            angles.append(math.degrees(math.atan2(v0[1] * v1[2] - v0[2] * v1[1],
                                                  v0[1] * v1[1] + v0[2] * v1[2])))
        else:
            angles.append(math.degrees(math.atan2(v0[2] * v1[0] - v0[0] * v1[2],
                                                  v0[0] * v1[0] + v0[2] * v1[2])))
    angles.sort()
    return angles[len(angles) // 2]


def compute_pivots(res, morphs):
    """返回 {侧: (O, {up,dn,in,out})}，高精度：115点球拟合 + 多顶点中位角"""
    verts = res['verts']
    lp_idx, rp_idx = m.split_pupil(res)
    PL = m.center_of([verts[i][0] for i in lp_idx])
    PR = m.center_of([verts[i][0] for i in rp_idx])
    C = res['sc']
    out = {}
    O_L = None
    for side, P in (('L', PL), ('R', PR)):
        pre = '左' if side == 'L' else '右'
        # 高精度: 全部顶点(初始+4边界=115点) 中心化球拟合
        states = pupil_all_states(res, morphs, side)
        O = ball_fit_full(states)
        d = math.dist(O, P)
        # 球拟合残差(半径一致性 = 精度指标)
        rr = [math.dist(O, p) for p in states]
        spread = max(rr) - min(rr)
        if d > 3.0:
            if side == 'L':
                print(f'  [L] 反推球心退化(|OP|={d:.1f}), 回退眼白球心 C')
                O = C
            else:
                O = (-O_L[0], O_L[1], O_L[2])
                print(f'  [R] 反推球心退化(|OP|={d:.1f}), 改用左眼枢轴镜像 O={tuple(round(x,3) for x in O)}')
        if side == 'L':
            O_L = O
        # 高精度: 多顶点中位角
        a = dict(
            up=multi_rot_angle(res, O, side, morphs, '上', 'X'),
            dn=multi_rot_angle(res, O, side, morphs, '下', 'X'),
            in_=multi_rot_angle(res, O, side, morphs, '内', 'Y'),
            out=multi_rot_angle(res, O, side, morphs, '外', 'Y'),
        )
        print(f'  [{side}] 枢轴 O={tuple(round(x,4) for x in O)} |OP|={math.dist(O,P):.3f} 球拟合半径差={spread:.6f}')
        print(f'      高精度边界角: 上{a["up"]:+.4f}° 下{a["dn"]:+.4f}° 内{a["in_"]:+.4f}° 外{a["out"]:+.4f}°')
        out[side] = (O, a)
    return out

def apply_pivot_ik(src, dst, sync=False):
    """sync=True: 左右目IK 骨 parent=頭(静止) + 左右目追加親=両目(同向驱动)"""
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐 remaining={r.remaining}')
        return False
    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    eye_l = find_idx(names, name_gs, '左目', 'Eye_L')
    eye_r = find_idx(names, name_gs, '右目', 'Eye_R')
    head = find_idx(names, name_gs, '頭', 'Head')
    if any(x is None for x in (eye_l, eye_r, head)):
        print('[SKIP] 缺 左目/右目/頭')
        return False

    res = m.analyze(src)
    morphs = load_eye_morphs(src)
    if not all(k in morphs for k in ('左目上', '左目下', '左目内', '左目外', '右目上', '右目下', '右目内', '右目外')):
        print('[SKIP] 缺目上下左右表情')
        return False
    pivots = compute_pivots(res, morphs)

    # 目骨新位置 = O（左右）
    newL = pivots['L'][0]
    newR = pivots['R'][0]
    print(f'目骨位置: 左目 {tuple(round(x,3) for x in struct.unpack_from("<3f", r.bone_items[eye_l][4], 0))} -> {tuple(round(x,3) for x in newL)}')
    print(f'          右目 {tuple(round(x,3) for x in struct.unpack_from("<3f", r.bone_items[eye_r][4], 0))} -> {tuple(round(x,3) for x in newR)}')

    L = struct.unpack_from('<3f', r.bone_items[eye_l][4], 0)
    R = struct.unpack_from('<3f', r.bone_items[eye_r][4], 0)
    both_xyz = ((L[0]+R[0])/2, (L[1]+R[1])/2, 0.0)

    insert_before = min(eye_l, eye_r)
    n_new = 5
    old_to_new = {i: (i if i < insert_before else i + n_new) for i in range(r.bone_count)}
    i_both = insert_before
    i_ikl = insert_before + 1
    i_ikr = insert_before + 2
    i_ikgl = insert_before + 3
    i_ikgr = insert_before + 4

    # IK link 角度限制（非对称精确：上/下/内/外各自角度，带符号）
    def link_lim(side):
        a = pivots[side][1]                 # {'up','dn','in_','out'}
        upx = math.radians(a['up'])
        dnx = math.radians(a['dn'])
        if side == 'L':
            upy = math.radians(a['in_'])    # 左目 Y+ = 内
            dny = math.radians(a['out'])    # 左目 Y- = 外
        else:
            upy = math.radians(a['out'])    # 右目 Y+ = 外
            dny = math.radians(a['in_'])    # 右目 Y- = 内
        print(f'  {side}目 IK限制: X[{math.degrees(upx):+.1f},{math.degrees(dnx):+.1f}]° '
              f'Y[{math.degrees(upy):+.1f},{math.degrees(dny):+.1f}]°')
        return (upx, upy, 0.0), (dnx, dny, 0.0)

    up_l, dn_l = link_lim('L')
    up_r, dn_r = link_lim('R')

    ARM = 0.15   # IK 臂长（IK先 到目骨距离），保证両目旋转时 IK 可解
    ikgl_xyz = (newL[0], newL[1], newL[2] + ARM)
    ikgr_xyz = (newR[0], newR[1], newR[2] + ARM)
    ikl_xyz = ikgl_xyz
    ikr_xyz = ikgr_xyz

    ik_parent = old_to_new[head] if sync else i_both   # sync: IK骨静止在頭下
    new_items = [
        (i_both, '両目', '', build_item_raw(r, both_xyz, old_to_new[head], 0, 0x001A, tail_offset=(0.0, 0.0, -1.0))),
        (i_ikl, '左目IK', '', build_item_raw(r, ikl_xyz, ik_parent, 0, 0x003F,
             ik=dict(target=i_ikgl, loop=IK_LOOP, unit=IK_UNIT, links=[(old_to_new[eye_l], True, up_l, dn_l)]))),
        (i_ikr, '右目IK', '', build_item_raw(r, ikr_xyz, ik_parent, 0, 0x003F,
             ik=dict(target=i_ikgr, loop=IK_LOOP, unit=IK_UNIT, links=[(old_to_new[eye_r], True, up_r, dn_r)]))),
        (i_ikgl, '左目IK先', '', build_item_raw(r, ikgl_xyz, old_to_new[eye_l], 0, 0x0000)),
        (i_ikgr, '右目IK先', '', build_item_raw(r, ikgr_xyz, old_to_new[eye_r], 0, 0x0000)),
    ]
    new_items.sort(key=lambda x: x[0])

    out_bone = bytearray()
    out_bone.extend((r.bone_count + n_new).to_bytes(4, 'little'))
    k = 0
    def put_raw(n_raw, ng_raw, item_raw):
        out_bone.extend(len(n_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(n_raw)
        out_bone.extend(len(ng_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(ng_raw)
        out_bone.extend(item_raw)

    pos = 0
    for new_idx in range(r.bone_count + n_new):
        if k < len(new_items) and new_items[k][0] == new_idx:
            ni = new_items[k]
            put_raw(ni[1].encode('utf-16-le'), ni[2].encode('utf-16-le'), ni[3])
            k += 1
        else:
            name, name_g, name_raw, name_g_raw, item_raw = r.bone_items[pos]
            # 左右目位置改为 O；sync 模式加追加親=両目(同向)
            if pos == eye_l or pos == eye_r:
                old_parent = int.from_bytes(item_raw[12:12 + r.bidx], 'little')
                layer = int.from_bytes(item_raw[12 + r.bidx:12 + r.bidx + 4], 'little', signed=True)
                new_pos = newL if pos == eye_l else newR
                if sync:
                    item_raw = build_item_raw(r, new_pos, old_to_new.get(old_parent, old_parent), layer,
                                              0x0117, append_parent=i_both, append_ratio=1.0)
                else:
                    ba = bytearray(remap_item(item_raw, old_to_new, r.bidx))
                    struct.pack_into('<3f', ba, 0, *new_pos)
                    item_raw = bytes(ba)
            else:
                item_raw = remap_item(item_raw, old_to_new, r.bidx)
            put_raw(name_raw, name_g_raw, item_raw)
            pos += 1

    def remap_pts(bytes_, points, start):
        ba = bytearray(bytes_)
        for abs_off, old in points:
            _write_idx(ba, abs_off - start, old_to_new.get(old, old), r.bidx)
        return bytes(ba)

    vertex_out = remap_pts(r.vertex_bytes, r.weight_points, r.vertex_start)
    morph_out = remap_pts(r.morph_bytes, r.morph_bone_points, r.morph_start)
    frame_out = remap_pts(r.frame_bytes, r.frame_bone_points, r.frame_start)
    rigid_out = remap_pts(r.rigid_bytes, r.rigid_bone_points, r.rigid_start)

    out = bytearray()
    out.extend(r.header_bytes); out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little')); out.extend(vertex_out)
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(bone_ba) if False else out.extend(out_bone)
    out.extend(r.morph_count.to_bytes(4, 'little')); out.extend(morph_out)
    out.extend(r.frame_count.to_bytes(4, 'little')); out.extend(frame_out)
    out.extend(r.rigid_count.to_bytes(4, 'little')); out.extend(rigid_out)
    out.extend(r.joint_count.to_bytes(4, 'little')); out.extend(r.joint_bytes)

    with open(dst, 'wb') as f:
        f.write(bytes(out))
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    assert v.bone_count == r.bone_count + n_new
    print(f'[OK] 写入 {os.path.basename(dst)}: 骨骼 {r.bone_count}->{v.bone_count} remaining=0')
    return True




