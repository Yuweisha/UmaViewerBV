# -*- coding: utf-8 -*-
"""
手 IK 补齐（仿博丽灵梦分轴 IK 体系）
========================================
给 1033/1044 等程序导出模型补手臂分轴 IK（关节旋转限制）：
  每组 = 控制骨 + Tgt目标骨 + IK骨，IK 的 link 指向控制骨并设 limit（弧度）
  肩Y:   左腕y+ → 左腕yTgt ← 左腕yIK   (link 无限幅)
  腕YZ:  左腕yz → 左腕yzTgt ← 左腕yzIK (link 限幅 Y±80°/Z±150°)
  肘Y:   左ひじy+ → 左ひじyTgt ← 左ひじyIK (link 无限幅)
  手首Y: 左手首y+ → 左手首yTgt ← 左手首yIK (link 无限幅)
  左右对称，共 24 新骨。

参考（灵梦实测）：控制骨 flag=0x0013、Tgt=0x0002、IK=0x0036，layer=0，
IK loop=1~3 rad=π，位置：控制骨=关节pos、Tgt=关节+前臂方向×0.55×臂长、IK=Tgt pos。

用法：
  python src/gen_arm_ik.py 输入.pmx 输出.pmx
"""
import os
import sys
import struct
import math

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402
from hip_process import build_item_raw  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

SIDES = (('左', 'L'), ('右', 'R'))
TGT_T = 0.55            # Tgt 距离 = 关节间距离 × 0.55
YZ_OFF = (0.26, 0.69, 0.0)  # 腕 yzTgt 相对 yTgt 的额外 Y 偏（灵梦实测相对值）
Y_LIM = 1.3963          # ±80°
Z_LIM_L = (-0.0873, 2.618)   # 左腕 Z
Z_LIM_R = (-2.618, 0.0873)   # 右腕 Z

def _find(idx_name, idx_ng, jp, en):
    return idx_name.get(jp) or idx_ng.get(en)


def _n3(v):
    n = math.sqrt(sum(x * x for x in v))
    return (v[0] / n, v[1] / n, v[2] / n) if n else (0.0, 0.0, 0.0)


def _add(p, v):
    return (p[0] + v[0], p[1] + v[1], p[2] + v[2])


def _mul(v, t):
    return (v[0] * t, v[1] * t, v[2] * t)


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])

def gen_arm_ik(src, dst):
    print('=' * 60)
    print(f'手IK补齐: {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐 remaining={r.remaining}')
        return False
    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    idx_name = {n: i for i, n in enumerate(names)}
    idx_ng = {n: i for i, n in enumerate(name_gs)}
    print(f'  骨骼={r.bone_count}')

    # ===== 1. 定位手臂关节 =====
    arm = {}
    ok = True
    for s, e in SIDES:
        a = {}
        a['肩'] = _find(idx_name, idx_ng, f'{s}肩', f'Shoulder_{e}')
        a['腕'] = _find(idx_name, idx_ng, f'{s}腕', f'Arm_{e}')
        a['腕捩'] = _find(idx_name, idx_ng, f'{s}腕捩', f'arm twist_{e}')
        a['ひじ'] = _find(idx_name, idx_ng, f'{s}ひじ', f'Elbow_{e}')
        a['手捩'] = _find(idx_name, idx_ng, f'{s}手捩', f'wrist twist_{e}')
        a['手首'] = _find(idx_name, idx_ng, f'{s}手首', f'Wrist_{e}')
        arm[s] = a
        for k, v in a.items():
            if v is None:
                print(f'  [ERROR] 缺 {s}{k}')
                ok = False
    if not ok:
        return False

    # ===== 2. 关节位置 =====
    pos_of = {}
    for i in range(r.bone_count):
        raw = r.bone_items[i][4]
        pos_of[i] = struct.unpack_from('<3f', raw, 0)

    # ===== 3. 定义新骨（左右对称）=====
    inserts = {}   # before_old_idx -> [('NEW', ...)]
    def add_inserts(before, items):
        inserts.setdefault(before, []).extend(items)

    for s, e in SIDES:
        i_腕 = arm[s]['腕']
        i_肘 = arm[s]['ひじ']
        i_手首 = arm[s]['手首']
        i_腕捩 = arm[s]['腕捩']
        i_手捩 = arm[s]['手捩']
        p_腕 = pos_of[i_腕]
        p_肘 = pos_of[i_肘]
        p_手首 = pos_of[i_手首]

        # 方向：腕→肘、肘→手首
        d_wrist_elbow = _sub(p_肘, p_腕)
        d_elbow_wrist = _sub(p_手首, p_肘)
        n_we = _n3(d_wrist_elbow)
        n_ew = _n3(d_elbow_wrist)
        L_we = math.sqrt(sum(x * x for x in d_wrist_elbow))
        L_ew = math.sqrt(sum(x * x for x in d_elbow_wrist))

        # 目标位置
        yTgt_腕 = _add(p_腕, _mul(n_we, L_we * TGT_T))        # 腕 yTgt
        yzTgt_腕 = _add(yTgt_腕, YZ_OFF)                      # 腕 yzTgt
        yTgt_肘 = _add(p_肘, _mul(n_ew, L_ew * TGT_T))        # 肘 yTgt
        yTgt_手首 = _add(p_手首, _mul(_n3(_mul(n_ew, -1)), L_ew * TGT_T))  # 手首 yTgt（指向肘）

        z_lim = Z_LIM_L if e == 'L' else Z_LIM_R
        yz_lim = ((0.0, -Y_LIM, z_lim[0]), (0.0, Y_LIM, z_lim[1]))

        # 肩 Y
        add_inserts(i_腕捩, [
            ('NEW', f'{s}腕y+', '', p_腕, arm[s]['肩'], 0, 0x0013, f'{s}腕yTgt', None, None, None),
            ('NEW', f'{s}腕yTgt', '', yTgt_腕, f'{s}腕y+', 0, 0x0002, None, None, None, None),
            ('NEW', f'{s}腕yIK', '', yTgt_腕, i_腕, 0, 0x0036, None, None, None,
             (f'{s}腕yTgt', 1, math.pi, [(f'{s}腕y+', None)])),
        ])
        # 腕 YZ（限幅）
        add_inserts(i_腕捩, [
            ('NEW', f'{s}腕yz', '', p_腕, arm[s]['肩'], 0, 0x0013, f'{s}腕yzTgt', None, None, None),
            ('NEW', f'{s}腕yzTgt', '', yzTgt_腕, f'{s}腕yz', 0, 0x0002, None, None, None, None),
            ('NEW', f'{s}腕yzIK', '', yTgt_腕, i_腕, 0, 0x0036, None, None, None,
             (f'{s}腕yzTgt', 1, math.pi, [(f'{s}腕yz', yz_lim)])),
        ])
        # 肘 Y
        add_inserts(i_肘, [
            ('NEW', f'{s}ひじy+', '', p_肘, i_腕捩, 0, 0x0013, f'{s}ひじyTgt', None, None, None),
            ('NEW', f'{s}ひじyTgt', '', yTgt_肘, f'{s}ひじy+', 0, 0x0002, None, None, None, None),
            ('NEW', f'{s}ひじyIK', '', yTgt_肘, i_肘, 0, 0x0036, None, None, None,
             (f'{s}ひじyTgt', 3, math.pi, [(f'{s}ひじy+', None)])),
        ])
        # 手首 Y
        add_inserts(i_手首, [
            ('NEW', f'{s}手首y+', '', p_手首, i_手捩, 0, 0x0013, f'{s}手首yTgt', None, None, None),
            ('NEW', f'{s}手首yTgt', '', yTgt_手首, f'{s}手首y+', 0, 0x0002, None, None, None, None),
            ('NEW', f'{s}手首yIK', '', yTgt_手首, i_手捩, 0, 0x0036, None, None, None,
             (f'{s}手首yTgt', 2, math.pi, [(f'{s}手首y+', None)])),
        ])

    n_new = sum(len(v) for v in inserts.values())
    print(f'  新建分轴IK骨 {n_new} 个（左右各 12：肩Y/腕YZ/肘Y/手首Y）')
    # ===== 4. 骨骼区重建（仿 hip_process）=====
    new_item_plan = []
    old_to_new = {}
    new_idx = {}
    def _emit(old):
        old_to_new[old] = len(new_item_plan)
        new_item_plan.append(('OLD', old))
    for old in range(r.bone_count):
        if old in inserts:
            for item in inserts[old]:
                new_idx[item[1]] = len(new_item_plan)
                new_item_plan.append(item)
        old_to_new[old] = len(new_item_plan)
        new_item_plan.append(('OLD', old))
    def resolve(ref):
        if isinstance(ref, str):
            return new_idx[ref]
        return old_to_new[ref]

    out_bone = bytearray()
    out_bone.extend(len(new_item_plan).to_bytes(4, 'little'))
    old_new_off = {}
    for item in new_item_plan:
        if item[0] == 'OLD':
            old = item[1]
            _, _, name_raw, ng_raw, item_raw = r.bone_items[old]
            out_bone.extend(len(name_raw).to_bytes(4, 'little', signed=True))
            out_bone.extend(name_raw)
            out_bone.extend(len(ng_raw).to_bytes(4, 'little', signed=True))
            out_bone.extend(ng_raw)
            old_new_off[old] = len(out_bone)
            out_bone.extend(item_raw)
        else:
            nm, ng, xyz, pref, layer, flag, tail, aref, ratio, ik = item[1:]
            parent = resolve(pref)
            tail = resolve(tail) if isinstance(tail, (int, str)) else tail
            append_parent = resolve(aref) if aref is not None else None
            ik2 = None
            if ik is not None:
                tgt, loop, rad, links = ik
                ik2 = (resolve(tgt), loop, rad, [(resolve(li), limv) for li, limv in links])
            item_raw = build_item_raw(r, xyz, parent, layer, flag, tail=tail,
                                      append_parent=append_parent, append_ratio=ratio, ik=ik2)
            nb = nm.encode('utf-16-le')
            nbg = ng.encode('utf-16-le') if ng else b''
            out_bone.extend(len(nb).to_bytes(4, 'little', signed=True))
            out_bone.extend(nb)
            out_bone.extend(len(nbg).to_bytes(4, 'little', signed=True))
            out_bone.extend(nbg)
            out_bone.extend(item_raw)

    # ★ 重映射 OLD 骨内部引用（parent/tail/追加親/IK target/IK link）
    #   gen_arm_ik 插入 24 新骨后，旧骨索引全部偏移；OLD 骨 item_raw 原样写出时
    #   内部引用仍是旧索引 → 必须用 bone_refs 定点改写，否则骨骼指向混乱。
    bone_ba = bytearray(out_bone)
    none_idx = (1 << (8 * r.bidx)) - 1
    for old in range(r.bone_count):
        off = old_new_off.get(old)
        if off is None:
            continue
        for rel, oidx in r.bone_refs[old]:
            if oidx == none_idx:
                continue
            new = old_to_new.get(oidx)
            if new is None:
                new = none_idx
            v = new & 0xFFFFFFFF
            for k in range(r.bidx):
                bone_ba[off + rel + k] = (v >> (8 * k)) & 0xFF
    out_bone = bone_ba

    def remap(points, section_start, bytes_):
        ba = bytearray(bytes_)
        for abs_off, old in points:
            new = old_to_new.get(old)
            if new is not None:
                ba[abs_off - section_start:abs_off - section_start + r.bidx] = new.to_bytes(r.bidx, 'little')
        return bytes(ba)

    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little'))
    out.extend(remap(r.weight_points, r.vertex_start, r.vertex_bytes))
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(out_bone)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(remap(r.morph_bone_points, r.morph_start, r.morph_bytes))
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(remap(r.frame_bone_points, r.frame_start, r.frame_bytes))
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(remap(r.rigid_bone_points, r.rigid_start, r.rigid_bytes))
    out.extend(r.joint_count.to_bytes(4, 'little')); out.extend(r.joint_bytes)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    # ===== 5. 验证 =====
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.bone_count == r.bone_count + n_new, f'骨骼数 {v.bone_count} != {r.bone_count + n_new}'
    v_names = [it[0].rstrip() for it in v.bone_items]
    miss = [n for s, e in SIDES for n in (f'{s}腕yIK', f'{s}腕yzIK', f'{s}ひじyIK', f'{s}手首yIK') if n not in v_names]
    assert not miss, f'缺IK骨 {miss}'
    print(f'  [OK] 骨骼 {r.bone_count}->{v.bone_count}, remaining=0')
    print(f'  写出: {os.path.basename(dst)}')
    return True




