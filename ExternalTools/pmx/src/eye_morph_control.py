# -*- coding: utf-8 -*-
"""
[模型控制] 目骨/両目骨旋转 -> 目上下左右表情权重 映射控制器
=============================================================
控制链（用户定义的标准）：
    両目骨旋转 --(追加親 1.0)--> 左目/右目骨旋转 --> 瞳孔位移 --> 目上下左右表情权重

提供两级映射：
    一级 bone_to_morph : 目骨旋转角 -> 瞳孔位移 -> 表情权重（每帧左右分别）
    二级 both_to_morph : 両目骨旋转角 -> 両目级联位移 -> 表情权重

表情权重应用 = 瞳孔顶点按权重 × 表情位移向量 叠加，即瞳孔在眼眶内上下左右移动。

用法（代码控制示例）：
    ctl = EyeMorphControl("1033_Admire Vega.pmx")
    w = ctl.both_to_morph(rx=15, ry=-10)   # 両目骨转 15°上/10°右
    pos = ctl.apply(w)                      # 瞳孔最终顶点位置
    gap = ctl.check_gap(pos)                # 贴边验证(最小孔间隙°)
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

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# 1033 目上下左右表情满值位移(平均, 方向规范: x+外 y+上)
MORPH_MAG = {
    '左目外': +0.0524, '左目内': -0.0524,
    '右目外': -0.1048, '右目内': +0.1048,
    '左目上': +0.0481, '左目下': -0.0481,
    '右目上': +0.0902, '右目下': -0.0902,
}


def read_cstr(data, off):
    n = int.from_bytes(data[off:off+4], 'little', signed=True)
    off += 4
    return data[off:off+n], off+n


def load_eye_morphs(pmx_path):
    """从模型读取 目上下左右 8 个表情的实际位移量(每表情平均向量)"""
    r = PMXReader(pmx_path).parse()
    data = r.data
    p = r.morph_start
    out = {}
    for _ in range(r.morph_count):
        nm_raw, p = read_cstr(data, p)
        ng_raw, p = read_cstr(data, p)
        nm = m.decode_text(nm_raw)
        panel = data[p]; p += 1
        mtype = data[p]; p += 1
        oc = int.from_bytes(data[p:p+4], 'little'); p += 4
        vals = []
        for _ in range(oc):
            if mtype == 1:
                v = int.from_bytes(data[p:p+r.vidx], 'little'); p += r.vidx
                val = struct.unpack_from('<3f', data, p); p += 12
                vals.append((v, val))
            elif mtype in (0, 9):
                p += r.moidx + 4
            elif mtype == 2:
                p += r.bidx + 28
            elif mtype in (3,4,5,6,7):
                p += r.vidx + 16
            elif mtype == 8:
                p += r.midx + 113
            elif mtype == 10:
                p += r.ridx + 25
            else:
                p += r.vidx + 16
        if nm in MORPH_MAG and vals:
            avg = tuple(sum(x[1][i] for x in vals)/len(vals) for i in range(3))
            out[nm] = (vals, avg)   # (各[顶点索引, 位移], 平均向量)
    return out

class EyeMorphControl:
    """模型瞳孔控制：両目骨/目骨旋转 -> 目上下左右表情权重 -> 瞳孔位移"""

    def __init__(self, pmx_path):
        self.pmx = pmx_path
        self.res = m.analyze(pmx_path)   # 模型几何(瞳孔顶点/目骨/両目/眼白球/孔边缘)
        self.eye_morphs = load_eye_morphs(pmx_path)  # 8 个表情的实际位移
        self.eye_l = self.res['eye_l']
        self.eye_r = self.res['eye_r']
        self.both = self.res['both']
        self.verts = self.res['verts']
        self.lp_idx, self.rp_idx = m.split_pupil(self.res)
        r = self.res['r']
        self.B = m.bone_item_pos(r.bone_items[self.both][4], r.bidx)
        self.L = m.bone_item_pos(r.bone_items[self.eye_l][4], r.bidx)
        self.R = m.bone_item_pos(r.bone_items[self.eye_r][4], r.bidx)
        # 孔边缘(未动的眼白外缘顶点)
        C, sr = self.res['sc'], self.res['sr']
        self.C, self.sr = C, sr
        self.edge_u = []
        for p in self.res['sclera_pos']:
            d = math.dist(C, p)
            if d > sr + 0.20:
                self.edge_u.append(tuple((p[i]-C[i])/d for i in range(3)))

    # ---------- 一级: 目骨旋转 -> 瞳孔位移 -> 表情权重 ----------
    def bone_eye_displacement(self, side, rx, ry):
        """目骨直接旋转 rx(绕X上下)/ry(绕Y左右) 后 瞳孔中心位移"""
        idx = self.lp_idx if side == 'L' else self.rp_idx
        bone = self.L if side == 'L' else self.R
        p0 = m.center_of([self.verts[i][0] for i in idx])
        p_x = m.rotate(p0, bone, (1, 0, 0), math.radians(rx))
        p_y = m.rotate(p0, bone, (0, 1, 0), math.radians(ry))
        return (p_x[0]-p0[0] + p_y[0]-p0[0], p_x[1]-p0[1] + p_y[1]-p0[1])

    # ---------- 二级: 両目骨旋转 -> 両目级联位移 ----------
    def both_eye_displacement(self, side, rx, ry):
        """両目骨旋转 rx/ry (両目级联: 目骨摆动+绕新骨转) 后 瞳孔中心位移"""
        idx = self.lp_idx if side == 'L' else self.rp_idx
        bone = self.L if side == 'L' else self.R
        p0 = m.center_of([self.verts[i][0] for i in idx])
        B2x = m.rotate(bone, self.B, (1, 0, 0), math.radians(rx))
        p_x = m.rotate(p0, B2x, (1, 0, 0), math.radians(rx))
        B2y = m.rotate(bone, self.B, (0, 1, 0), math.radians(ry))
        p_y = m.rotate(p0, B2y, (0, 1, 0), math.radians(ry))
        return (p_x[0]-p0[0] + p_y[0]-p0[0], p_x[1]-p0[1] + p_y[1]-p0[1])

    # ---------- 位移 -> 表情权重 ----------
    def displacement_to_weights(self, side, dx, dy):
        """瞳孔位移(dx,dy) -> 该侧 目上下左右 4 个表情权重"""
        pre = '左' if side == 'L' else '右'
        w = {f'{pre}目上': 0.0, f'{pre}目下': 0.0, f'{pre}目内': 0.0, f'{pre}目外': 0.0}
        if dy > 0:
            w[f'{pre}目上'] = min(1.0, dy / max(1e-9, MORPH_MAG[f'{pre}目上']))
        else:
            w[f'{pre}目下'] = min(1.0, -dy / max(1e-9, MORPH_MAG[f'{pre}目下']))
        # dx: 正=向外(左眼x+, 右眼x-), 负=向内
        if side == 'L':
            if dx > 0:
                w['左目外'] = min(1.0, dx / max(1e-9, MORPH_MAG['左目外']))
            else:
                w['左目内'] = min(1.0, -dx / max(1e-9, MORPH_MAG['左目内']))
        else:
            if dx > 0:
                w['右目内'] = min(1.0, dx / max(1e-9, MORPH_MAG['右目内']))
            else:
                w['右目外'] = min(1.0, -dx / max(1e-9, MORPH_MAG['右目外']))
        return w

    # ---------- 角度限制（从表情范围反推，见 eye_angle_limit.py） ----------
    # 両目骨级联: rx(上下)≤6.5° ry(左右)≤6.25°  目骨直接: rx≤6.5° ry≤7.1°
    LIMIT_BOTH = {'rx': 6.5, 'ry': 6.25}
    LIMIT_BONE = {'rx': 6.5, 'ry': 7.1}

    # ---------- 对外接口 ----------
    def bone_to_morph(self, rx, ry, clamp=True):
        """目骨直接旋转 rx/ry -> 左右眼表情权重 (一级映射); clamp=True 时应用角度限制"""
        if clamp:
            rx = max(-self.LIMIT_BONE['rx'], min(self.LIMIT_BONE['rx'], rx))
            ry = max(-self.LIMIT_BONE['ry'], min(self.LIMIT_BONE['ry'], ry))
        w = {}
        for side in ('L', 'R'):
            dx, dy = self.bone_eye_displacement(side, rx, ry)
            w.update(self.displacement_to_weights(side, dx, dy))
        return w

    def both_to_morph(self, rx, ry, clamp=True):
        """両目骨旋转 rx/ry -> 左右眼表情权重 (二级映射, 両目级联); clamp=True 时应用角度限制"""
        if clamp:
            rx = max(-self.LIMIT_BOTH['rx'], min(self.LIMIT_BOTH['rx'], rx))
            ry = max(-self.LIMIT_BOTH['ry'], min(self.LIMIT_BOTH['ry'], ry))
        w = {}
        for side in ('L', 'R'):
            dx, dy = self.both_eye_displacement(side, rx, ry)
            w.update(self.displacement_to_weights(side, dx, dy))
        return w

    # ---------- 应用表情权重 -> 瞳孔最终位置 ----------
    def apply(self, weights):
        """按权重叠加表情位移, 返回瞳孔顶点最终位置 dict"""
        pos = {i: list(self.verts[i][0]) for i in self.res['pupil']}
        for nm, (vals, avg) in self.eye_morphs.items():
            w = weights.get(nm, 0.0)
            if abs(w) < 1e-6:
                continue
            for (v, val) in vals:
                pos[v][0] += val[0] * w
                pos[v][1] += val[1] * w
                pos[v][2] += val[2] * w
        return {i: tuple(p) for i, p in pos.items()}

    # ---------- 贴边验证 ----------
    def check_gap(self, positions):
        """瞳孔顶点到孔边缘的最小角间隙(°), >0 安全"""
        g = 1e9
        for i, p in positions.items():
            d = math.dist(self.C, p)
            u = tuple((p[k]-self.C[k])/d for k in range(3))
            for ue in self.edge_u:
                c = sum(u[k]*ue[k] for k in range(3))
                g = min(g, math.degrees(math.acos(max(-1, min(1, c)))))
        return g


