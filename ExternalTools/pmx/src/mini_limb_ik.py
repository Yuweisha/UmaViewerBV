# -*- coding: utf-8 -*-
"""
mini 基础手足 IK 生成（仅 IK，不加 D骨、不迁移权重）
========================================================
mini 模型只有脚踝（无 toe/Ankle_offset/足D/足IK），且无 twist/腕捩/手捩，
通用 步骤[14]hip_process(腿) 与 [15]gen_arm_ik(手) 均因缺骨失效。
本脚本参照 hip_process(足IK) + gen_arm_ik(手分轴IK)，绕开 twist/toe 依赖，
为 mini 补「基础手足 IK」：
    每侧：足IK(足IK親 + 足ＩＫ + 足ＩＫ先) + 手IK(腕YZ 限幅、ひじY)
    → 左右共 18 新骨。父链用主臂骨(无腕捩/手捩)，不动现有骨架/权重。

用法：
    python src/mini_limb_ik.py 输入.pmx 输出.pmx
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
IK_SHIN_Z = 1.267                    # 足IK先 前向 z 偏移（向上）
KNEE_LIMIT = ((-math.pi, 0.0, 0.0), (-0.0087266, 0.0, 0.0))  # 膝盖 IK 限幅
TGT_T = 0.55                          # Tgt 距离 = 关节距离 × 0.55
YZ_OFF = (0.26, 0.69, 0.0)           # 腕 yzTgt 额外 Y 偏
Y_LIM = 1.3963                        # ±80°
Z_LIM_L = (-0.0873, 2.618)
Z_LIM_R = (-2.618, 0.0873)


def _find(idx_name, idx_ng, jp, en):
    return idx_name.get(jp) or idx_ng.get(en)


def _sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def _n3(v):
    n = math.sqrt(sum(x * x for x in v))
    return (v[0] / n, v[1] / n, v[2] / n) if n else (0.0, 0.0, 0.0)


def _add(p, v):
    return (p[0] + v[0], p[1] + v[1], p[2] + v[2])


def _mul(v, t):
    return (v[0] * t, v[1] * t, v[2] * t)


def add_mini_limb_ik(src, dst):
    print('=' * 60)
    print(f'mini基础手足IK: {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐 remaining={r.remaining}')
        return False
    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    idx_name = {n: i for i, n in enumerate(names) if n}
    idx_ng = {n: i for i, n in enumerate(name_gs) if n}
    print(f'  骨骼={r.bone_count} bidx={r.bidx}')

    # ---- 1. 定位 ----
    root = _find(idx_name, idx_ng, '全ての親', 'Position')
    hip = _find(idx_name, idx_ng, '下半身', 'Hip')
    if root is None or hip is None:
        print('  [ERROR] 缺 全ての親/下半身')
        return False
    b = {}
    for s, e in SIDES:
        b[(s, 'thigh')] = _find(idx_name, idx_ng, f'{s}足', f'Thigh_{e}')
        b[(s, 'knee')] = _find(idx_name, idx_ng, f'{s}ひざ', f'Knee_{e}')
        b[(s, 'ankle')] = _find(idx_name, idx_ng, f'{s}足首', f'Ankle_{e}')
        b[(s, 'shoulder')] = _find(idx_name, idx_ng, f'{s}肩', f'Shoulder_{e}')
        b[(s, 'arm')] = _find(idx_name, idx_ng, f'{s}腕', f'Arm_{e}')
        b[(s, 'elbow')] = _find(idx_name, idx_ng, f'{s}ひじ', f'Elbow_{e}')
        b[(s, 'wrist')] = _find(idx_name, idx_ng, f'{s}手首', f'Wrist_{e}')
    need = ('thigh', 'knee', 'ankle', 'shoulder', 'arm', 'elbow', 'wrist')
    missing = [f'{s}{k}' for s, e in SIDES for k in need if b[(s, k)] is None]
    if missing:
        print(f'  [ERROR] 缺骨骼: {missing}')
        return False

    pos_of = {}
    for i in range(r.bone_count):
        pos_of[i] = struct.unpack_from('<3f', r.bone_items[i][4], 0)

    return _build(r, b, pos_of, root, dst)


def _build(r, b, pos_of, root, dst):
    """定义新骨 + 重建 + 重映射 + 写回，返回成功与否。"""
    inserts = {}
    def add_inserts(before, items):
        inserts.setdefault(before, []).extend(items)

    n_new = 0
    for s, e in SIDES:
        thigh = b[(s, 'thigh')]
        knee = b[(s, 'knee')]
        ankle = b[(s, 'ankle')]
        shoulder = b[(s, 'shoulder')]
        arm = b[(s, 'arm')]
        elbow = b[(s, 'elbow')]
        wrist = b[(s, 'wrist')]
        p_ankle = pos_of[ankle]
        p_腕 = pos_of[arm]
        p_肘 = pos_of[elbow]
        p_手首 = pos_of[wrist]

        ik_floor = (p_ankle[0], 0.0, p_ankle[2])
        ik_shin = (p_ankle[0], p_ankle[1], p_ankle[2] + IK_SHIN_Z)
        add_inserts(thigh, [
            ('NEW', f'{s}足IK親', f'leg IKP_{e}', ik_floor, root, 0, 0x001E,
             (0.0, p_ankle[1], 0.0), None, None, None),
            ('NEW', f'{s}足ＩＫ', '', p_ankle, f'{s}足IK親', 1, 0x003F, f'{s}足ＩＫ先',
             None, None, (ankle, 40, 2.0, [(knee, KNEE_LIMIT), (thigh, None)])),
            ('NEW', f'{s}足ＩＫ先', '', ik_shin, f'{s}足ＩＫ', 1, 0x0013, None, None, None, None),
        ])

        d_we = _sub(p_肘, p_腕)
        d_ew = _sub(p_手首, p_肘)
        n_we = _n3(d_we)
        n_ew = _n3(d_ew)
        L_we = math.sqrt(sum(x * x for x in d_we))
        L_ew = math.sqrt(sum(x * x for x in d_ew))
        yTgt_腕 = _add(p_腕, _mul(n_we, L_we * TGT_T))
        yzTgt_腕 = _add(yTgt_腕, YZ_OFF)
        yTgt_肘 = _add(p_肘, _mul(n_ew, L_ew * TGT_T))
        z_lim = Z_LIM_L if e == 'L' else Z_LIM_R
        yz_lim = ((0.0, -Y_LIM, z_lim[0]), (0.0, Y_LIM, z_lim[1]))

        add_inserts(arm, [
            ('NEW', f'{s}腕yz', '', p_腕, shoulder, 0, 0x0013, f'{s}腕yzTgt', None, None, None),
            ('NEW', f'{s}腕yzTgt', '', yzTgt_腕, f'{s}腕yz', 0, 0x0002, None, None, None, None),
            ('NEW', f'{s}腕yzIK', '', yTgt_腕, arm, 0, 0x0036, None, None, None,
             (f'{s}腕yzTgt', 1, math.pi, [(f'{s}腕yz', yz_lim)])),
            ('NEW', f'{s}ひじy+', '', p_肘, arm, 0, 0x0013, f'{s}ひじyTgt', None, None, None),
            ('NEW', f'{s}ひじyTgt', '', yTgt_肘, f'{s}ひじy+', 0, 0x0002, None, None, None, None),
            ('NEW', f'{s}ひじyIK', '', yTgt_肘, elbow, 0, 0x0036, None, None, None,
             (f'{s}ひじyTgt', 3, math.pi, [(f'{s}ひじy+', None)])),
        ])
        n_new += 9

    return _write(r, inserts, n_new, dst)


def _write(r, inserts, n_new, dst):
    """骨骼区重建 + 内部引用重映射 + 引用区重映射 + 写回 + 验证。"""
    none_idx = (1 << (8 * r.bidx)) - 1
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
        if ref is None:
            return none_idx
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
            tail2 = resolve(tail) if isinstance(tail, (int, str)) else tail
            append_parent = resolve(aref) if aref is not None else None
            ik2 = None
            if ik is not None:
                tgt, loop, rad, links = ik
                ik2 = (resolve(tgt), loop, rad, [(resolve(li), limv) for li, limv in links])
            item_raw = build_item_raw(r, xyz, parent, layer, flag, tail=tail2,
                                      append_parent=append_parent, append_ratio=ratio, ik=ik2)
            nb = nm.encode('utf-16-le')
            nbg = ng.encode('utf-16-le') if ng else b''
            out_bone.extend(len(nb).to_bytes(4, 'little', signed=True))
            out_bone.extend(nb)
            out_bone.extend(len(nbg).to_bytes(4, 'little', signed=True))
            out_bone.extend(nbg)
            out_bone.extend(item_raw)

    # 重映射 OLD 骨内部引用（parent/tail/追加親/IK）
    bone_ba = bytearray(out_bone)
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

    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.bone_count == r.bone_count + n_new, \
        f'骨骼数 {v.bone_count} != {r.bone_count} + {n_new}'
    v_names = [it[0].rstrip() for it in v.bone_items]
    miss = [n for s, e in SIDES for n in (f'{s}足ＩＫ', f'{s}腕yzIK', f'{s}ひじyIK') if n not in v_names]
    assert not miss, f'缺IK骨 {miss}'
    print(f'  [OK] 骨骼 {r.bone_count}->{v.bone_count}, remaining=0')
    print(f'  写出: {os.path.basename(dst)}')
    return True




