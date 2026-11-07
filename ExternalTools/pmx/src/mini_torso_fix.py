# -*- coding: utf-8 -*-
"""
mini 躯干层级修复 —— 连通「上半身/下半身」，补「上半身」骨
========================================================
背景：mini 未跑 hip_process(躯干重构)，导致：
  1) 下半身(hip) 独立挂 全ての親，未连到 腰 -> 上下半身两条链，VMD 驱动时断连；
  2) mini 无「上半身」骨，VMD(#千鳥等) 的「上半身」帧无处映射。
本脚本对齐 MMD 标准躯干链：
  全ての親 → センター → グルーブ → 腰 → 下半身(腿)
                             腰 → 上半身(新) → 上半身2 → 首 → 頭
改动：下半身.parent → 腰；若缺「上半身」则插入(腰 与 上半身2 中点)，上半身2.parent → 上半身。
用法：python src/mini_torso_fix.py 输入.pmx 输出.pmx
"""
import os, sys, struct

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402
from hip_process import build_item_raw, parse_item  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass


def _find(idx_name, idx_ng, jp, en):
    return idx_name.get(jp) or idx_ng.get(en)


def mini_torso_fix(src, dst):
    print('=' * 60)
    print(f'mini躯干层级修复: {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐 remaining={r.remaining}')
        return False
    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]
    idx_name = {n: i for i, n in enumerate(names) if n}
    idx_ng = {n: i for i, n in enumerate(name_gs) if n}
    print(f'  骨骼={r.bone_count} bidx={r.bidx}')

    waist = _find(idx_name, idx_ng, '腰', 'Waist')
    hip = _find(idx_name, idx_ng, '下半身', 'Hip')
    upper2 = _find(idx_name, idx_ng, '上半身2', 'Chest')
    upper = _find(idx_name, idx_ng, '上半身', 'Spine')
    if waist is None or hip is None or upper2 is None:
        print(f'  [ERROR] 缺 腰/下半身/上半身2 (waist={waist},hip={hip},upper2={upper2})')
        return False

    pos_of = {}
    for i in range(r.bone_count):
        pos_of[i] = struct.unpack_from('<3f', r.bone_items[i][4], 0)

    hip_parent = parse_item(r, r.bone_items[hip][4])['parent']
    inserting = upper is None
    print(f'  下半身.parent 现={names[hip_parent] if 0 <= hip_parent < len(names) else "ROOT"}'
          f' -> 将改为 腰;  上半身存在={not inserting}')

    return _build(r, names, waist, hip, upper2, upper, pos_of, inserting, dst)


def _build(r, names, waist, hip, upper2, upper, pos_of, inserting, dst):
    none_idx = (1 << (8 * r.bidx)) - 1
    inserts = {}
    if inserting:
        pw = pos_of[waist]; pu = pos_of[upper2]
        ux = ((pw[0]+pu[0])/2, (pw[1]+pu[1])/2, (pw[2]+pu[2])/2)
        inserts.setdefault(upper2, []).append(
            ('NEW', '上半身', 'Spine', ux, waist, 0, 0x001F, upper2, None, None, None))
        print(f'  插入 上半身 pos=({ux[0]:.3f},{ux[1]:.3f},{ux[2]:.3f}) parent=腰 (接続先=上半身2)')
    return _write(r, inserts, names, hip, waist, upper2, inserting, dst)


def _write(r, inserts, names, hip, waist, upper2, inserting, dst):
    none_idx = (1 << (8 * r.bidx)) - 1
    # 腰权重迁移到下半身（对齐 hip_process：腰=纯挂点 0 权重，
    # 避免转身时 腰(0°)/上半身(58.7°) 线性混合 → 腹部麻花）
    w_redirect = {waist: hip}
    pre_w = sum(1 for _p, bb in r.weight_points if bb == waist)
    new_item_plan = []
    old_to_new = {}
    new_idx = {}
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

    parent_fix = {hip: resolve(waist)}
    new_upper = new_idx.get('上半身')
    if new_upper is not None:
        parent_fix[upper2] = new_upper

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
            item_raw = build_item_raw(r, xyz, parent, layer, flag, tail=tail2,
                                      append_parent=append_parent, append_ratio=ratio)
            nb = nm.encode('utf-16-le')
            nbg = ng.encode('utf-16-le') if ng else b''
            out_bone.extend(len(nb).to_bytes(4, 'little', signed=True))
            out_bone.extend(nb)
            out_bone.extend(len(nbg).to_bytes(4, 'little', signed=True))
            out_bone.extend(nbg)
            out_bone.extend(item_raw)

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

    def w_parent(off, val):
        for k in range(r.bidx):
            bone_ba[off + 12 + k] = ((val & 0xFFFFFFFF) >> (8 * k)) & 0xFF
    for old, newp in parent_fix.items():
        w_parent(old_new_off[old], newp)
    # 腰 的接続先(tail=骨)重定向到新「上半身」，使 腰→上半身→上半身2 头尾全连接
    def w_tail(off, val):
        for k in range(r.bidx):
            bone_ba[off + 12 + r.bidx + 4 + 2 + k] = ((val & 0xFFFFFFFF) >> (8 * k)) & 0xFF
    if new_upper is not None and waist in old_new_off:
        w_tail(old_new_off[waist], new_upper)
    out_bone = bone_ba

    def remap(points, section_start, bytes_, redirect=None):
        ba = bytearray(bytes_)
        redirect = redirect or {}
        for abs_off, old in points:
            tgt = redirect.get(old, old)   # 腰 -> 下半身（仅顶点权重）
            new = old_to_new.get(tgt)
            if new is not None:
                ba[abs_off - section_start:abs_off - section_start + r.bidx] = new.to_bytes(r.bidx, 'little')
        return bytes(ba)

    out = bytearray()
    out.extend(r.header_bytes); out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little'))
    out.extend(remap(r.weight_points, r.vertex_start, r.vertex_bytes, redirect=w_redirect))
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
    assert v.bone_count == r.bone_count + (1 if inserting else 0), \
        f'骨骼数 {v.bone_count} != {r.bone_count} + {1 if inserting else 0}'
    vn = [it[0].rstrip() for it in v.bone_items]
    vi = {n: i for i, n in enumerate(vn)}
    post_w = sum(1 for _p, bb in v.weight_points if bb == vi['腰'])
    post_h = sum(1 for _p, bb in v.weight_points if bb == vi['下半身'])
    if inserting:
        assert '上半身' in vi, '缺 上半身'
    assert post_w == 0, f'腰权重未清零 post_w={post_w}'
    print(f'  [腰权重迁移] 腰引用顶点 {pre_w} -> 0 (下半身 -> {post_h})')
    assert parse_item(v, v.bone_items[vi['下半身']][4])['parent'] == vi['腰'], '下半身 未挂到 腰'
    if inserting:
        assert parse_item(v, v.bone_items[vi['上半身2']][4])['parent'] == vi['上半身'], '上半身2 未挂到 上半身'
    print(f'  [OK] 骨骼 {r.bone_count}->{v.bone_count}, remaining=0')
    print(f'  写出: {os.path.basename(dst)}')
    return True




