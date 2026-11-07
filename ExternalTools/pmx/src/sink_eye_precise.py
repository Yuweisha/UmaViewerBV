# -*- coding: utf-8 -*-
"""
[落地] 精确眼白区域内凹（基于表情界值相交面，不误伤面部）
==================================================================
眼白区域 = 瞳孔在 初始+上/下/内/外表情边界 扫过的区域（相交面）+ tol
只内凹这些精确眼白顶点，面部其他顶点不动。

用法:
  python src/sink_eye_precise.py 输入.pmx 输出.pmx [δ默认0.02] [tol默认0.12]
"""
import sys
import os
import math
import struct

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402
import analyze_eye_rot as m  # noqa: E402
from eye_morph_control import load_eye_morphs  # noqa: E402
from eye_white_precise import precise_sclera_idx, pupil_boundary_positions  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass


def vertex_pos_offsets(r):
    data = r.data
    vs = r.vertex_start
    p = 0
    offs = []
    for _ in range(r.vertex_count):
        offs.append(vs + p)
        p += 32
        p += r.uv_count * 16
        dt = data[vs + p]; p += 1
        if dt == 0:
            p += r.bidx
        elif dt == 1:
            p += r.bidx * 2 + 4
        elif dt == 2:
            p += r.bidx * 4 + 16
        elif dt == 3:
            p += r.bidx * 2 + 4 + 36
        elif dt == 4:
            p += r.bidx * 4 + 16
        else:
            raise ValueError(f'未知变形方式 {dt}')
        p += 4
    return offs


def sink_precise(src, dst, delta, tol, gradient=True):
    """精确眼白内凹；gradient=True 时边缘顶点渐变(中心全凹→边缘0)，避免与面部断裂"""
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐 remaining={r.remaining}')
        return False
    res = m.analyze(src)
    morphs = load_eye_morphs(src)
    sL = precise_sclera_idx(res, morphs, 'L', tol)
    sR = precise_sclera_idx(res, morphs, 'R', tol)
    s_all = sL | sR
    C = res['sc']
    print(f'精确眼白顶点: L={len(sL)} R={len(sR)} 并集={len(s_all)} (tol={tol}, gradient={gradient})')

    # 瞳孔扫过区域锚点（用于边缘渐变权重）
    anchors = pupil_boundary_positions(res, morphs, 'L') + pupil_boundary_positions(res, morphs, 'R')

    offs = vertex_pos_offsets(r)
    vertex_ba = bytearray(r.vertex_bytes)
    moved = 0
    max_shift = 0.0
    for vi in s_all:
        p = res['verts'][vi][0]
        d = math.dist(C, p)
        if d < 1e-6:
            continue
        if gradient:
            dmin = min(math.dist(p, a) for a in anchors)
            w = max(0.0, min(1.0, 1.0 - dmin / tol))   # 中心1 → 边缘0
            if w <= 0.01:
                continue
        else:
            w = 1.0
        shift = delta * w
        dirv = tuple((p[k] - C[k]) / d for k in range(3))
        np_ = tuple(p[k] - dirv[k] * shift for k in range(3))
        struct.pack_into('<3f', vertex_ba, offs[vi] - r.vertex_start, *np_)
        moved += 1
        max_shift = max(max_shift, shift)
    print(f'内凹 {moved} 个顶点, δ={delta} (max_shift={max_shift:.3f}, gradient={gradient})')

    out = bytearray()
    out.extend(r.header_bytes); out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little')); out.extend(vertex_ba)
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(r.bone_count.to_bytes(4, 'little')); out.extend(r.bone_bytes)
    out.extend(r.morph_count.to_bytes(4, 'little')); out.extend(r.morph_bytes)
    out.extend(r.frame_count.to_bytes(4, 'little')); out.extend(r.frame_bytes)
    out.extend(r.rigid_count.to_bytes(4, 'little')); out.extend(r.rigid_bytes)
    out.extend(r.joint_count.to_bytes(4, 'little')); out.extend(r.joint_bytes)
    with open(dst, 'wb') as f:
        f.write(bytes(out))
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    print(f'[OK] 写入 {os.path.basename(dst)}  remaining=0')
    return True




