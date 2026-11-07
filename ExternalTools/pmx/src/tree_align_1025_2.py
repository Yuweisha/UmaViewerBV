# -*- coding: utf-8 -*-
"""
[14] 骨骼树对齐 1025_2 标准 —— 补根链(センター/グルーブ) + 腰挂腿(腰キャンセル)
================================================================================
参照 1025_2_Manhattan cafe.pmx 标准结构：
  1. 补根链：若缺 センター/グルーブ，在 全ての親 之后插入两骨，并把 腰 的 parent 挂到 グルーブ
  2. 腰挂腿：若 左足/右足/左足D/右足D 当前挂 下半身 下，改为挂 腰キャンセル左/右 下
              （让腰キャンセル(AP=腰, -1.0) 真正抵消腰旋转对腿的影响）
定位：センター/グルーブ 相对 下半身/腰キャンセル 的偏移取自 1025_2 实测值，通用适配各模型。

用法：
  python tree_align_1025_2.py   # 批量处理当前目录所有 .pmx（自动备份到 [14]备份_时间戳/，原地覆盖）
"""
import struct
import os
import sys
import glob
import shutil
import datetime

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402

# ---- 1033(专属/原模型)系列实测偏移（相对 下半身 / 腰キャンセル）----
# 基准：1033_2 完整版实测 センター(0,10.638,0.420) グルーブ(0,11.287,0.420)
#      下半身(0,11.087,0.420) 腰(0,10.000,0.420) 腰キャンセル左(0.822,10.758,0.421)
# 说明：早期沿用 1025_2 值(Y=-4.032 等) 对 1033 系列 (mini/专属) 会导致センター/グルーブ
#      相对下半身偏低 4.0 → 落在脚底下方附近，位置错误；已改用原模型实测基准。
CEN_OFF_Y = -0.449        # センター.y - 下半身.y   (10.638 - 11.087)
CEN_OFF_Z = 0.000         # センター.z - 下半身.z   (0.420 - 0.420)
CEN_FROM_LEG_Y = -0.120   # センター.y - 腰キャンセル.y (10.638 - 10.758)
CEN_FROM_LEG_Z = -0.001   # センター.z - 腰キャンセル.z (0.420 - 0.421)
GRU_UP_Y = 0.649          # グルーブ.y - センター.y   (11.287 - 10.638)
CEN_TAIL = (0.0, 3.8496, 0.0)   # センター tail(指向腰方向)
GRU_TAIL = (0.0, 1.4705, 0.0)   # グルーブ tail
CEN_FLAG = 0x001E
GRU_FLAG = 0x001E


def _write_idx(ba, off, val, size):
    v = val & 0xFFFFFFFF
    for k in range(size):
        ba[off + k] = (v >> (8 * k)) & 0xFF


def detect_enc(r):
    """检测骨骼名称编码：同时尝试 UTF-16LE/UTF-8，选可打印字符得分高的"""
    raw0 = r.bone_items[0][2] if r.bone_items else b''
    if not raw0:
        return 'utf-8'
    best, best_score = 'utf-8', -1
    for enc in ('utf-16-le', 'utf-8'):
        try:
            s = raw0.decode(enc)
            if any(0xD800 <= ord(c) <= 0xDFFF for c in s):
                continue
            score = sum(1 for c in s
                        if 0x20 <= ord(c) < 0x7F or 0x3040 <= ord(c) <= 0x30FF
                        or 0x4E00 <= ord(c) <= 0x9FFF)
            if score > best_score:
                best, best_score = enc, score
        except Exception:
            pass
    return best


def parent_of(r, i):
    return int.from_bytes(r.bone_items[i][4][12:12 + r.bidx], 'little')


def bone_xyz(r, i):
    return struct.unpack_from('<3f', r.bone_items[i][4], 0)


def build_root_item(r, xyz, parent, flag, tail):
    """构建 センター/グルーブ 这类无付与/无IK 的根骨 item_raw"""
    out = bytearray()
    out.extend(struct.pack('<3f', *xyz))
    out.extend(parent.to_bytes(r.bidx, 'little'))
    out.extend(struct.pack('<i', 0))          # layer = 0
    out.extend(flag.to_bytes(2, 'little'))
    if flag & 0x0001:
        out.extend(((1 << (8 * r.bidx)) - 1).to_bytes(r.bidx, 'little'))
    else:
        out.extend(struct.pack('<3f', *tail))
    return bytes(out)


def remap_item(r, raw, old_to_new, none_idx, new_parent=None):
    """重映射 item_raw 内全部骨骼索引字段；new_parent 覆盖 parent 字段（新索引空间值）"""
    ba = bytearray(raw)

    def w(off, val):
        _write_idx(ba, off, val, r.bidx)

    p = 12
    old_parent = int.from_bytes(raw[p:p + r.bidx], 'little')
    if new_parent is not None:
        w(p, new_parent)
    else:
        w(p, old_to_new.get(old_parent, none_idx))
    p += r.bidx
    p += 4                                   # layer
    flag = int.from_bytes(raw[p:p + 2], 'little'); p += 2
    if flag & 0x0001:                        # 接続先(骨)
        v = int.from_bytes(raw[p:p + r.bidx], 'little'); w(p, old_to_new.get(v, none_idx)); p += r.bidx
    else:
        p += 12
    if flag & 0x0100:                        # 回転付与
        v = int.from_bytes(raw[p:p + r.bidx], 'little'); w(p, old_to_new.get(v, none_idx)); p += r.bidx + 4
    if flag & 0x0200:                        # 移動付与
        v = int.from_bytes(raw[p:p + r.bidx], 'little'); w(p, old_to_new.get(v, none_idx)); p += r.bidx + 4
    if flag & 0x0400:                        # 固定軸
        p += 12
    if flag & 0x0800:                        # ローカル軸
        p += 24
    if flag & 0x2000:                        # 外部親（本项目无，保守 +4）
        p += 4
    if flag & 0x0020:                        # IK
        v = int.from_bytes(raw[p:p + r.bidx], 'little'); w(p, old_to_new.get(v, none_idx)); p += r.bidx
        p += 4 + 4                           # loop + rad
        n = int.from_bytes(raw[p:p + 4], 'little', signed=True); p += 4
        for _ in range(n):
            v = int.from_bytes(raw[p:p + r.bidx], 'little'); w(p, old_to_new.get(v, none_idx)); p += r.bidx
            lim = raw[p]; p += 1
            if lim:
                p += 24
    return bytes(ba)


def remap_section(pts, start, section, bidx, old_to_new, none_idx):
    """顶点权重 / Morph / 帧 / 刚体 区骨骼索引重映射"""
    ba = bytearray(section)
    for abs_off, old in pts:
        _write_idx(ba, abs_off - start, old_to_new.get(old, none_idx), bidx)
    return bytes(ba)


def align_bones(src, dst):
    print('=' * 70)
    print(f'骨骼树对齐1025_2: {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐(剩余{r.remaining}字节)')
        return False
    print(f'  骨骼={r.bone_count} bidx={r.bidx}')
    none_idx = (1 << (8 * r.bidx)) - 1

    names = [it[0].rstrip() for it in r.bone_items]
    idx = {n: i for i, n in enumerate(names) if n}
    root = idx.get('全ての親')
    waist = idx.get('腰')
    hip = idx.get('下半身')
    if root is None or waist is None or hip is None:
        print('  [ERROR] 缺少 全ての親/腰/下半身，跳过')
        return False

    has_center = ('センター' in idx) and ('グルーブ' in idx)
    legs = {}
    for s in ('左', '右'):
        legs[s] = (idx.get(f'腰キャンセル{s}'), idx.get(f'{s}足'), idx.get(f'{s}足D'))

    gru_exist = idx.get('グルーブ') if has_center else -1
    root_change = (not has_center) or (parent_of(r, waist) != gru_exist)
    leg_change = False
    for s, (lc, th, td) in legs.items():
        if lc is not None and th is not None and parent_of(r, th) != lc:
            leg_change = True
            break
    if not root_change and not leg_change:
        print('  [SKIP] 根链与腿挂载已符合 1025_2 标准')
        return True
    print(f'  需补根链={root_change} 需修正腿挂载={leg_change}')

    # ---- 索引映射 ----
    inserting = not has_center
    if inserting:
        old_to_new = {i: (i if i == 0 else i + 2) for i in range(r.bone_count)}
        new_cen, new_gru = 1, 2
    else:
        old_to_new = {i: i for i in range(r.bone_count)}
        new_cen, new_gru = idx['センター'], idx['グルーブ']
    enc = detect_enc(r)

    # ---- センター/グルーブ 定位（优先以 腰キャンセル 为基准，否则 下半身） ----
    lcL = legs['左'][0]
    if lcL is not None:
        lc_xyz = bone_xyz(r, lcL)
        cen = (0.0, lc_xyz[1] + CEN_FROM_LEG_Y, lc_xyz[2] + CEN_FROM_LEG_Z)
    else:
        hip_xyz = bone_xyz(r, hip)
        cen = (0.0, hip_xyz[1] + CEN_OFF_Y, hip_xyz[2] + CEN_OFF_Z)
    gru = (0.0, cen[1] + GRU_UP_Y, cen[2])

    # ---- parent 覆盖（旧索引 -> 新parent值） ----
    parent_fix = {}
    if root_change:
        parent_fix[waist] = new_gru
    for s, (lc, th, td) in legs.items():
        if lc is not None and th is not None and parent_of(r, th) != lc:
            nl = old_to_new[lc]
            parent_fix[th] = nl
            if td is not None:
                parent_fix[td] = nl

    # ---- 重建骨骼区 ----
    out_bone = bytearray()
    out_bone.extend((r.bone_count + (2 if inserting else 0)).to_bytes(4, 'little'))

    def put_bone(nm, ng, item_raw):
        nr = nm if isinstance(nm, bytes) else nm.encode(enc)
        gr = ng if isinstance(ng, bytes) else ng.encode(enc)
        out_bone.extend(len(nr).to_bytes(4, 'little', signed=True))
        out_bone.extend(nr)
        out_bone.extend(len(gr).to_bytes(4, 'little', signed=True))
        out_bone.extend(gr)
        out_bone.extend(item_raw)

    if inserting:
        # 新顺序：全ての親(0) → センター(1) → グルーブ(2) → 旧1..旧N-1
        item = r.bone_items[0]
        put_bone(item[2], item[3], remap_item(r, item[4], old_to_new, none_idx, parent_fix.get(0)))
        put_bone('センター'.encode(enc), b'', build_root_item(r, cen, root, CEN_FLAG, CEN_TAIL))
        put_bone('グルーブ'.encode(enc), 'groove'.encode(enc), build_root_item(r, gru, new_cen, GRU_FLAG, GRU_TAIL))
        print(f'  新建 センター{gold(cen)} グルーブ{gold(gru)}')
        old_range = range(1, r.bone_count)
    else:
        old_range = range(r.bone_count)
    for old in old_range:
        item = r.bone_items[old]
        put_bone(item[2], item[3],
                 remap_item(r, item[4], old_to_new, none_idx, parent_fix.get(old)))

    # ---- 引用重映射（顶点/Morph/帧/刚体） ----
    vertex_out = remap_section(r.weight_points, r.vertex_start, r.vertex_bytes, r.bidx, old_to_new, none_idx)
    morph_out = remap_section(r.morph_bone_points, r.morph_start, r.morph_bytes, r.bidx, old_to_new, none_idx)
    frame_out = remap_section(r.frame_bone_points, r.frame_start, r.frame_bytes, r.bidx, old_to_new, none_idx)
    rigid_out = remap_section(r.rigid_bone_points, r.rigid_start, r.rigid_bytes, r.bidx, old_to_new, none_idx)

    # ---- 组装输出 ----
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
    out.extend(out_bone)
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

    # ---- 写回 + 验证 ----
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    assert v.bone_count == r.bone_count + (2 if inserting else 0), \
        f'验证失败: 骨骼数 {r.bone_count}->{v.bone_count}'
    vn = [it[0].rstrip() for it in v.bone_items]
    vi = {n: i for i, n in enumerate(vn) if n}
    assert 'センター' in vi and 'グルーブ' in vi, '验证失败: 缺少 センター/グルーブ'
    if root_change:
        assert parent_of(v, vi['腰']) == vi['グルーブ'], '验证失败: 腰 parent != グルーブ'
    for s, (lc, th, td) in legs.items():
        if lc is not None and th is not None and parent_of(r, th) != lc:
            assert parent_of(v, vi[f'{s}足']) == vi[f'腰キャンセル{s}'], \
                f'验证失败: {s}足 未挂到 腰キャンセル{s}'
    print(f'  [OK] 写入 {os.path.basename(dst)}: 骨骼 {r.bone_count} -> {v.bone_count} 剩余字节=0')
    return True




def gold(v):
    return '(' + ','.join(f'{x:.3f}' for x in v) + ')'


