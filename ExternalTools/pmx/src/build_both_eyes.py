# -*- coding: utf-8 -*-
"""
[7] 双目骨生成（両目同向 + 反推枢轴 + IK 限制）
=====================================================
决策（用户修正）：
  - 目表情「取小值对称化」仅用于両目骨生成的**计算**（枢轴/边界角，稳定）；
  - 模型内的目上下左右表情本身「取大值对称」（表情控制瞳孔移动幅度大、更明显）。

流程：
  1. 取小值对称 morphs -> v4 高精度枢轴 + 边界角
  2. 両目创建(两眼中点) + 左右目(位置=枢轴, 追加親=両目, 倍率=边界角/90)
  3. 両目作为 IK link（限制 X/Y ±90°，両目90°=瞳孔到位理论值）
  4. 模型目表情写入取大值对称
  5. 可选 sink 精确眼白内凹（δ=0.05 默认）

用法:
  python src/build_both_eyes.py 输入.pmx 输出.pmx [--nosink] [sink_delta]
"""
import sys
import os
import math
import struct
import shutil

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402
import analyze_eye_rot as m  # noqa: E402
from eye_morph_control import load_eye_morphs  # noqa: E402
from eye_pivot_ik import (compute_pivots, build_item_raw, remap_item,  # noqa: E402
                          _write_idx, find_idx)
from build_groups import fix_eye_morph_range  # noqa: E402
from sink_eye_precise import sink_precise  # noqa: E402
from eye_white_precise import pupil_boundary_positions  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

IK_UNIT = 0.1
IK_LOOP = 4
DIRS = ('上', '下', '内', '外')


def _mag(v):
    return math.sqrt(v[0]*v[0] + v[1]*v[1] + v[2]*v[2])


def _avg_vals(vals):
    n = len(vals)
    return tuple(sum(x[1][k] for x in vals) / n for k in range(3))


def sym_morphs(morphs, take):
    """构造取小/取大值对称化的 morphs（左右目位移=两目中较小/较大者的镜像）"""
    out = {}
    for d in DIRS:
        L = morphs['左目' + d]; R = morphs['右目' + d]
        lv = sorted(L[0]); rv = sorted(R[0])
        n = min(len(lv), len(rv))
        mL = _mag(L[1]); mR = _mag(R[1])
        if (mL <= mR) == (take == 'small'):
            # 取左目为基准，右目 = 左目位移镜像
            r_vals = [(rv[k][0], (-lv[k][1][0], lv[k][1][1], lv[k][1][2])) for k in range(n)]
            out['左目' + d] = (lv[:n], L[1])
            out['右目' + d] = (r_vals, _avg_vals(r_vals))
        else:
            l_vals = [(lv[k][0], (-rv[k][1][0], rv[k][1][1], rv[k][1][2])) for k in range(n)]
            out['左目' + d] = (l_vals, _avg_vals(l_vals))
            out['右目' + d] = (rv[:n], R[1])
    return out

def build_both_eyes(src, dst, do_sink=True, sink_delta=0.05, sink_tol=0.12):
    """新[7] 双目骨生成"""
    print(f"\n{'='*70}\n[7] 双目骨生成: {os.path.basename(src)}\n{'='*70}")
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
        print(f'[SKIP] 缺 左目/右目/頭 ({eye_l}/{eye_r}/{head})')
        return False

    res = m.analyze(src)
    morphs = load_eye_morphs(src)
    if not all(('左目' + d) in morphs for d in DIRS):
        print('[SKIP] 缺目上下左右表情')
        return False

    # 1. 取小值对称 morphs 用于计算枢轴/边界角（稳定）
    small = sym_morphs(morphs, 'small')
    pivots = compute_pivots(res, small)
    newL, newR = pivots['L'][0], pivots['R'][0]
    up_ang = pivots['L'][1]['up']      # 上边界角(取小值计算, 稳定)
    ratio = up_ang / 90.0              # 両目90° -> 左右目到位
    print(f'枢轴: L={tuple(round(x,3) for x in newL)} R={tuple(round(x,3) for x in newR)}')
    print(f'取小值边界角 up={up_ang:.3f}° ratio={ratio:.5f} (両目90°=左右目{up_ang:.2f}°)')

    # 2. 両目创建 + 左右目(位置=枢轴, 追加親=両目 ratio)
    L0 = struct.unpack_from('<3f', r.bone_items[eye_l][4], 0)
    R0 = struct.unpack_from('<3f', r.bone_items[eye_r][4], 0)
    both_xyz = ((L0[0]+R0[0])/2, (L0[1]+R0[1])/2, 0.0)

    insert_before = min(eye_l, eye_r)
    n_new = 3   # 両目 両目IK 両目IK先
    old_to_new = {i: (i if i < insert_before else i + n_new) for i in range(r.bone_count)}
    i_both = insert_before
    i_ik = insert_before + 1
    i_ikg = insert_before + 2

    up = (math.radians(90.0), math.radians(90.0), 0.0)
    dn = (math.radians(-90.0), math.radians(-90.0), 0.0)
    ARM = 0.15
    ikg_xyz = (both_xyz[0], both_xyz[1], both_xyz[2] + ARM)

    new_items = [
        (i_both, '両目', '', build_item_raw(r, both_xyz, old_to_new[head], 0, 0x001A, tail_offset=(0.0, 0.0, -1.0))),
        (i_ik, '両目IK', '', build_item_raw(r, ikg_xyz, old_to_new[head], 0, 0x003F,
             ik=dict(target=i_ikg, loop=IK_LOOP, unit=IK_UNIT,
                     links=[(i_both, True, up, dn)]))),
        (i_ikg, '両目IK先', '', build_item_raw(r, ikg_xyz, i_both, 0, 0x0000)),
    ]
    new_items.sort(key=lambda x: x[0])

    out_bone = bytearray()
    out_bone.extend((r.bone_count + n_new).to_bytes(4, 'little'))
    def put_raw(n_raw, ng_raw, item_raw):
        out_bone.extend(len(n_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(n_raw)
        out_bone.extend(len(ng_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(ng_raw)
        out_bone.extend(item_raw)

    k = 0
    pos = 0
    for new_idx in range(r.bone_count + n_new):
        if k < len(new_items) and new_items[k][0] == new_idx:
            ni = new_items[k]
            put_raw(ni[1].encode('utf-16-le'), ni[2].encode('utf-16-le'), ni[3])
            k += 1
            continue
        name, name_g, name_raw, name_g_raw, item_raw = r.bone_items[pos]
        if pos == eye_l or pos == eye_r:
            old_parent = int.from_bytes(item_raw[12:12+r.bidx], 'little')
            layer = int.from_bytes(item_raw[12+r.bidx:12+r.bidx+4], 'little', signed=True)
            new_pos = newL if pos == eye_l else newR
            item_raw = build_item_raw(r, new_pos, old_to_new.get(old_parent, old_parent), layer,
                                      0x0117, append_parent=i_both, append_ratio=ratio)
        else:
            item_raw = remap_item(item_raw, old_to_new, r.bidx)
        put_raw(name_raw, name_g_raw, item_raw)
        pos += 1

    def remap_pts(bytes_, points, start):
        ba = bytearray(bytes_)
        for abs_off, old in points:
            _write_idx(ba, abs_off - start, old_to_new.get(old, old), r.bidx)
        return bytes(ba)

    out = bytearray()
    out.extend(r.header_bytes); out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little')); out.extend(remap_pts(r.vertex_bytes, r.weight_points, r.vertex_start))
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(out_bone)
    out.extend(r.morph_count.to_bytes(4, 'little')); out.extend(remap_pts(r.morph_bytes, r.morph_bone_points, r.morph_start))
    out.extend(r.frame_count.to_bytes(4, 'little')); out.extend(remap_pts(r.frame_bytes, r.frame_bone_points, r.frame_start))
    out.extend(r.rigid_count.to_bytes(4, 'little')); out.extend(remap_pts(r.rigid_bytes, r.rigid_bone_points, r.rigid_start))
    out.extend(r.joint_count.to_bytes(4, 'little')); out.extend(r.joint_bytes)
    with open(dst, 'wb') as f:
        f.write(bytes(out))
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    print(f'[OK] 両目骨结构写入: 骨骼 {r.bone_count}->{v.bone_count} remaining=0')

    # 4. 模型目表情取大值对称（表情控制明显）
    fix_eye_morph_range(dst, take='large')

    # 5. 可选 sink 精确眼白内凹
    if do_sink:
        sink_precise(dst, dst, sink_delta, sink_tol)
    else:
        print(f'[SKIP] sink (nosink)')
    print(f'[OK] [7] 双目骨生成完成: {os.path.basename(dst)}')
    return True




