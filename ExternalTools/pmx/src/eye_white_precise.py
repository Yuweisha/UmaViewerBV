# -*- coding: utf-8 -*-
"""
[工具] 精确检测眼白区域（基于左右目表情上下左右界值）
=====================================================
方案（用户）：
  眼白区域 = 瞳孔在 上/下/内/外 表情边界间可到达的区域（相交面）
  - 每眼: 瞳孔中心方向 u0 + 4 个边界点方向（表情满值位移）
  - 切平面投影成四边形, 眼白顶点 = face 材质(头骨)顶点投影在四边形内

输出:
  每眼精确眼白顶点数/范围, 与旧 sclera 对比
  瞳孔转到边界时到精确眼白顶点的穿模判定

用法:
  python src/eye_white_precise.py 输入.pmx
"""
import sys
import os
import math
import struct

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
import analyze_eye_rot as m  # noqa: E402
from eye_morph_control import load_eye_morphs  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass


def sub(a, b): return tuple(a[i]-b[i] for i in range(3))
def add(a, b): return tuple(a[i]+b[i] for i in range(3))
def dot(a, b): return sum(a[i]*b[i] for i in range(3))
def norm(a): return math.sqrt(dot(a, a))
def norm_v(a):
    n = norm(a)
    return tuple(x/n for x in a)


def point_in_quad(p, quad):
    """点在凸四边形内(叉积同号)"""
    def cross2(a, b, c):
        return (b[0]-a[0])*(c[1]-a[1]) - (b[1]-a[1])*(c[0]-a[0])
    signs = [cross2(quad[i], quad[(i+1) % 4], p) for i in range(4)]
    return all(s >= 0 for s in signs) or all(s <= 0 for s in signs)


def pupil_boundary_positions(res, morphs, side):
    """瞳孔顶点在 初始 + 4个表情边界 的位置集合（每顶点用各自位移）"""
    verts = res['verts']
    lp_idx, rp_idx = m.split_pupil(res)
    idxs = lp_idx if side == 'L' else rp_idx
    pre = '左' if side == 'L' else '右'
    pos = []
    for i in idxs:
        pos.append(verts[i][0])
    for nm in (f'{pre}目上', f'{pre}目下', f'{pre}目内', f'{pre}目外'):
        vals, avg = morphs[nm]
        for v, val in vals:
            pos.append(tuple(verts[v][0][k] + val[k] for k in range(3)))
    return pos


def precise_sclera_idx(res, morphs, side, tol=0.05):
    """眼白顶点 = face 顶点(头骨)中，距瞳孔扫过区域(初始+4边界) <= tol"""
    verts = res['verts']
    anchors = pupil_boundary_positions(res, morphs, side)
    out = set()
    for mi, (nm, d, t, fc) in enumerate(res['mats']):
        if 'face' not in nm.lower() and '顔' not in nm:
            continue
        for v in res['mat_verts'][mi]:
            if res['head'] not in verts[v][1]:
                continue
            p = verts[v][0]
            if min(math.dist(p, a) for a in anchors) <= tol:
                out.add(v)
    return out




