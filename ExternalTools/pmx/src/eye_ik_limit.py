# -*- coding: utf-8 -*-
"""
[落地] 眼睛 IK 角度限制（両目 -> 左右目IK -> 左右目 link 受限）
==================================================================
背景：PMX 普通骨骼无旋转限幅。采用「IK 角度限制」方案（另一AI建议的应用部分）：
  両目 旋转 ->(parent)-> 左目IK/右目IK 骨摆动 -> IK解算 -> 左目/右目(link) 旋转
  左目/右目 作为 IK link 并设置角度限制（从目表情范围反推）：
    绕X(上下) ±6.5°   绕Y(左右) ±7.1°   绕Z 0°

新增骨骼（插入左目之前）:
  両目(0x001A) 左目IK(0x003F IK) 右目IK 左目IK先(0x0000) 右目IK先
  IK target=左目IK先/右目IK先  链=[左目/右目](带角度限制)

用法：
  python src/eye_ik_limit.py 输入.pmx 输出.pmx
"""
import sys
import os
import struct
import math

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# 角度限制(rad): X(上下)±6.5°  Y(左右)±7.1°  Z 0
LIM_X = math.radians(6.5)
LIM_Y = math.radians(7.1)
IK_UNIT = 0.1     # 単位角 rad
IK_LOOP = 4       # 解算次数


def find_idx(names, name_gs, jp, en):
    for i, n in enumerate(names):
        if n == jp:
            return i
    for i, g in enumerate(name_gs):
        if g == en:
            return i
    return None


def build_item_raw(r, xyz, parent, layer, flag, tail_offset=None,
                   append_parent=None, append_ratio=None, ik=None):
    """构建骨骼 item_raw（tail偏移 + 追加親 + IK + link 角度限制）"""
    out = bytearray()
    out.extend(struct.pack('<3f', *xyz))
    out.extend(parent.to_bytes(r.bidx, 'little'))
    out.extend(struct.pack('<i', layer))
    out.extend(flag.to_bytes(2, 'little'))
    if flag & 0x0001:                       # tail 为骨
        none = (1 << (8 * r.bidx)) - 1
        out.extend(none.to_bytes(r.bidx, 'little'))
    else:                                    # tail 为偏移
        t = tail_offset if tail_offset else (0.0, 0.0, 0.0)
        out.extend(struct.pack('<3f', *t))
    if flag & 0x0100:                        # 追加親(回転)
        out.extend(append_parent.to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', append_ratio))
    if flag & 0x0200:                        # 追加親(移動)
        out.extend(append_parent.to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', append_ratio))
    if flag & 0x0020:                        # IK
        out.extend(ik['target'].to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<i', ik['loop']))
        out.extend(struct.pack('<f', ik['unit']))
        out.extend(struct.pack('<i', len(ik['links'])))
        for lb, lim, up, dn in ik['links']:
            out.extend(lb.to_bytes(r.bidx, 'little'))
            out.extend(bytes([1 if lim else 0]))
            if lim:
                out.extend(struct.pack('<3f', *up))
                out.extend(struct.pack('<3f', *dn))
    return bytes(out)


def _write_idx(ba, off, val, size):
    v = val & 0xFFFFFFFF
    for k in range(size):
        ba[off + k] = (v >> (8 * k)) & 0xFF


def remap_item(item_raw, old_to_new, bidx):
    """重映射骨骼 item 内部引用（parent/tail/追加親/IK target/IK links），插入新骨后旧骨索引 +N"""
    ba = bytearray(item_raw)
    flag = int.from_bytes(ba[12 + bidx + 4:12 + bidx + 6], 'little')
    p = 12
    old_parent = int.from_bytes(ba[p:p + bidx], 'little')
    _write_idx(ba, p, old_to_new.get(old_parent, old_parent), bidx)
    p += bidx + 4 + 2
    if flag & 0x0001:                        # tail 为骨
        old_tail = int.from_bytes(ba[p:p + bidx], 'little')
        _write_idx(ba, p, old_to_new.get(old_tail, old_tail), bidx)
        p += bidx
    else:                                     # tail 为偏移
        p += 12
    if flag & 0x0100:                         # 追加親(回転)
        old_ap = int.from_bytes(ba[p:p + bidx], 'little')
        _write_idx(ba, p, old_to_new.get(old_ap, old_ap), bidx)
        p += bidx + 4
    if flag & 0x0200:                         # 追加親(移動)
        old_ab = int.from_bytes(ba[p:p + bidx], 'little')
        _write_idx(ba, p, old_to_new.get(old_ab, old_ab), bidx)
        p += bidx + 4
    if flag & 0x0400:                         # 固定軸
        p += 12
    if flag & 0x0800:                         # ローカル軸
        p += 24
    if flag & 0x2000:                         # 外部親
        p += 4
    if flag & 0x0020:                         # IK
        old_ikt = int.from_bytes(ba[p:p + bidx], 'little')
        _write_idx(ba, p, old_to_new.get(old_ikt, old_ikt), bidx)
        p += bidx + 4 + 4
        ic = int.from_bytes(ba[p:p + 4], 'little', signed=True); p += 4
        for _ in range(ic):
            old_l = int.from_bytes(ba[p:p + bidx], 'little')
            _write_idx(ba, p, old_to_new.get(old_l, old_l), bidx)
            p += bidx
            if ba[p]:
                p += 24
            p += 1
    return bytes(ba)

def apply_eye_ik(src, dst):
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐 remaining={r.remaining}')
        return False
    names = [it[0].rstrip() for it in r.bone_items]
    name_gs = [it[1].rstrip() for it in r.bone_items]

    eye_l = find_idx(names, name_gs, '左目', 'Eye_L')
    eye_r = find_idx(names, name_gs, '右目', 'Eye_R')
    head = find_idx(names, name_gs, '頭', 'Head')
    both = find_idx(names, name_gs, '両目', 'Eyes')
    if both is not None:
        print('[SKIP] 已存在両目骨')
        return False
    if eye_l is None or eye_r is None or head is None:
        print(f'[SKIP] 缺 左目/右目/頭 ({eye_l}/{eye_r}/{head})')
        return False

    L = struct.unpack_from('<3f', r.bone_items[eye_l][4], 0)
    R = struct.unpack_from('<3f', r.bone_items[eye_r][4], 0)
    # 両目位置 = 两眼中点 (z=0 对齐参考)
    both_xyz = ((L[0]+R[0])/2, (L[1]+R[1])/2, 0.0)
    print(f'両目={tuple(round(x,3) for x in both_xyz)} 左目={tuple(round(x,3) for x in L)} 右目={tuple(round(x,3) for x in R)}')

    # 新骨插入位置（左目之前）与索引分配
    insert_before = min(eye_l, eye_r)
    n_new = 5   # 両目 左目IK 右目IK 左目IK先 右目IK先
    old_to_new = {i: (i if i < insert_before else i + n_new) for i in range(r.bone_count)}
    # 新骨新索引
    i_both = insert_before
    i_ikl = insert_before + 1
    i_ikr = insert_before + 2
    i_ikgl = insert_before + 3
    i_ikgr = insert_before + 4
    print(f'新骨索引: 両目={i_both} 左目IK={i_ikl} 右目IK={i_ikr} 左目IK先={i_ikgl} 右目IK先={i_ikgr}')
    print(f'左目={eye_l}->{old_to_new[eye_l]} 右目={eye_r}->{old_to_new[eye_r]} 頭={head}->{old_to_new[head]}')

    # 左右目 IK先 位置（目骨位置 + 前方 0.05）
    ikgl_xyz = (L[0], L[1], L[2] + 0.05)
    ikgr_xyz = (R[0], R[1], R[2] + 0.05)
    ikl_xyz = ikgl_xyz     # 左目IK 骨 = 左目IK先 绑定位置
    ikr_xyz = ikgr_xyz

    up = (LIM_X, LIM_Y, 0.0)
    dn = (-LIM_X, -LIM_Y, 0.0)

    # 构建新骨 item
    new_items = [
        (i_both, '両目', '', build_item_raw(r, both_xyz, old_to_new[head], 0, 0x001A, tail_offset=(0.0, 0.0, -1.0))),
        (i_ikl, '左目IK', '', build_item_raw(r, ikl_xyz, i_both, 0, 0x003F,
             ik=dict(target=i_ikgl, loop=IK_LOOP, unit=IK_UNIT,
                     links=[(old_to_new[eye_l], True, up, dn)]))),
        (i_ikr, '右目IK', '', build_item_raw(r, ikr_xyz, i_both, 0, 0x003F,
             ik=dict(target=i_ikgr, loop=IK_LOOP, unit=IK_UNIT,
                     links=[(old_to_new[eye_r], True, up, dn)]))),
        (i_ikgl, '左目IK先', '', build_item_raw(r, ikgl_xyz, old_to_new[eye_l], 0, 0x0000)),
        (i_ikgr, '右目IK先', '', build_item_raw(r, ikgr_xyz, old_to_new[eye_r], 0, 0x0000)),
    ]
    # 排序写入（按新索引顺序）
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

    pos = 0  # 旧骨游标（按新索引顺序消费）
    for new_idx in range(r.bone_count + n_new):
        if k < len(new_items) and new_items[k][0] == new_idx:
            ni = new_items[k]
            put_raw(ni[1].encode('utf-16-le'), ni[2].encode('utf-16-le'), ni[3])
            k += 1
        else:
            name, name_g, name_raw, name_g_raw, item_raw = r.bone_items[pos]
            # 旧骨：名称保留原始字节 + 内部引用重映射（+5）
            put_raw(name_raw, name_g_raw, remap_item(item_raw, old_to_new, r.bidx))
            pos += 1
    bone_ba = bytearray(out_bone)

    # 重映射其他区引用（旧骨索引 -> 新索引）
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
    out.extend(bone_ba)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(morph_out)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(frame_out)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(rigid_out)
    out.extend(r.joint_count.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    # 验证
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    assert v.bone_count == r.bone_count + n_new
    vn = [it[0].rstrip() for it in v.bone_items]
    for nm in ('両目', '左目IK', '右目IK', '左目IK先', '右目IK先'):
        assert nm in vn, f'缺 {nm}'
    print(f'[OK] 写入 {os.path.basename(dst)}: 骨骼 {r.bone_count}->{v.bone_count} remaining=0')
    return True




