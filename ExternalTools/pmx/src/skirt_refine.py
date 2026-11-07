# -*- coding: utf-8 -*-
"""
裙骨细化（skirt_refine）—— 供工作台调用的新步骤
================================================
将模型导出的裙子骨骼 Sp_Hi_MSkirt0_{方向}_{段} 做：
  1. 竖向翻倍：每方向在相邻段间插入「中点段」，段号重排 _00~_0(2n-2)；
  2. 环向翻倍：按环腰角度排序，在相邻方向间插入「中间方向」；
  3. 权重线性插值：顶点按「到新骨骼的距离」把权重拆分到相邻新骨（BDEF1→BDEF2）；
  4. 引用重映射：顶点权重 / 骨骼内部引用 / Morph / 显示帧 / 刚体。
之后走 tail_skirt_1013_2.add_tail_skirt 生成刚体物理。
"""
import os
import sys
import re
import struct
import math

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader, decode_text  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

SKIRT_RE = re.compile(r'^Sp_Hi_MSkirt0_([A-Z]+)_(\d+)$')
NONE_IDX = lambda bidx: (1 << (8 * bidx)) - 1


def encode_text(s, encoding):
    raw = s.encode('utf-16-le' if encoding == 0 else 'utf-8')
    return len(raw).to_bytes(4, 'little', signed=True) + raw


def _parse_bone_item(r, item_raw):
    """解析一根骨骼 item 的字段，返回 dict。"""
    p = 0
    pos = struct.unpack_from('<3f', item_raw, p); p += 12
    parent = int.from_bytes(item_raw[p:p + r.bidx], 'little'); p += r.bidx
    layer = int.from_bytes(item_raw[p:p + 4], 'little'); p += 4
    flag = int.from_bytes(item_raw[p:p + 2], 'little'); p += 2
    tail = None
    if flag & 0x0001:
        tail = int.from_bytes(item_raw[p:p + r.bidx], 'little'); p += r.bidx
    else:
        tail = struct.unpack_from('<3f', item_raw, p); p += 12
    append = None
    if flag & 0x0100:
        append = (int.from_bytes(item_raw[p:p + r.bidx], 'little'), struct.unpack_from('<f', item_raw, p + r.bidx)[0]); p += r.bidx + 4
    if flag & 0x0200:
        p += r.bidx + 4
    if flag & 0x0400:
        p += 12
    if flag & 0x0800:
        p += 24
    if flag & 0x2000:
        p += 4
    ik = None
    if flag & 0x0020:
        ik_target = int.from_bytes(item_raw[p:p + r.bidx], 'little'); p += r.bidx
        p += 4 + 4
        ic = int.from_bytes(item_raw[p:p + 4], 'little'); p += 4
        links = []
        for _ in range(ic):
            links.append(int.from_bytes(item_raw[p:p + r.bidx], 'little')); p += r.bidx
            if int.from_bytes(item_raw[p:p + 1], 'little'):
                p += 1 + 24
            else:
                p += 1
        ik = (ik_target, links)
    return dict(pos=pos, parent=parent, layer=layer, flag=flag, tail=tail, append=append, ik=ik)


def _build_bone_item(r, d):
    """从 dict 重建骨骼 item 原始字节。"""
    out = bytearray()
    for v in d['pos']:
        out.extend(struct.pack('<f', v))
    out.extend(d['parent'].to_bytes(r.bidx, 'little'))
    out.extend(d['layer'].to_bytes(4, 'little'))
    out.extend(d['flag'].to_bytes(2, 'little'))
    if d['flag'] & 0x0001:
        out.extend(d['tail'].to_bytes(r.bidx, 'little'))
    else:
        for v in d['tail']:
            out.extend(struct.pack('<f', v))
    if d['flag'] & 0x0100:
        out.extend(d['append'][0].to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', d['append'][1]))
    if d['flag'] & 0x0200:
        out.extend(d.get('append_move', (0, 0.0))[0].to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', d.get('append_move', (0, 0.0))[1]))
    if d['flag'] & 0x0400:
        for v in d.get('fixed_axis', (0.0, 0.0, 0.0)):
            out.extend(struct.pack('<f', v))
    if d['flag'] & 0x0800:
        for v in d.get('local_x', (0.0, 0.0, 0.0)):
            out.extend(struct.pack('<f', v))
        for v in d.get('local_z', (0.0, 0.0, 0.0)):
            out.extend(struct.pack('<f', v))
    if d['flag'] & 0x2000:
        out.extend((d.get('ext_parent', 0)).to_bytes(4, 'little'))
    if d['flag'] & 0x0020:
        out.extend(d['ik'][0].to_bytes(r.bidx, 'little'))
        out.extend((d.get('ik_loop', 0)).to_bytes(4, 'little'))
        out.extend(struct.pack('<f', d.get('ik_rad', 0.0)))
        out.extend(len(d['ik'][1]).to_bytes(4, 'little'))
        for link in d['ik'][1]:
            out.extend(link.to_bytes(r.bidx, 'little'))
            out.extend(bytes([0]))
    return bytes(out)


def _parse_vertex(r, off):
    """解析单个顶点，返回 (下一偏移, 权重类型, [(骨骼索引, 权重值)], 边缘倍率字节)。"""
    p = off
    p += 32                      # 位置+法线+UV
    p += 16 * r.uv_count         # 附加UV
    dt = int.from_bytes(r.data[p:p + 1], 'little'); p += 1
    weights = []
    if dt == 0:                  # BDEF1
        b = int.from_bytes(r.data[p:p + r.bidx], 'little'); p += r.bidx
        weights = [(b, 1.0)]
    elif dt == 1:                # BDEF2
        b1 = int.from_bytes(r.data[p:p + r.bidx], 'little'); p += r.bidx
        b2 = int.from_bytes(r.data[p:p + r.bidx], 'little'); p += r.bidx
        w1 = struct.unpack_from('<f', r.data, p)[0]; p += 4
        weights = [(b1, w1), (b2, 1.0 - w1)]
    elif dt == 2:                # BDEF4
        bs = [int.from_bytes(r.data[p + j * r.bidx:p + (j + 1) * r.bidx], 'little') for j in range(4)]
        p += 4 * r.bidx
        ws = struct.unpack_from('<4f', r.data, p); p += 16
        weights = list(zip(bs, ws))
    elif dt == 3:                # SDEF
        b1 = int.from_bytes(r.data[p:p + r.bidx], 'little'); p += r.bidx
        b2 = int.from_bytes(r.data[p:p + r.bidx], 'little'); p += r.bidx
        w1 = struct.unpack_from('<f', r.data, p)[0]; p += 4
        weights = [(b1, w1), (b2, 1.0 - w1)]
        p += 36
    elif dt == 4:                # QDEF
        bs = [int.from_bytes(r.data[p + j * r.bidx:p + (j + 1) * r.bidx], 'little') for j in range(4)]
        p += 4 * r.bidx
        ws = struct.unpack_from('<4f', r.data, p); p += 16
        weights = list(zip(bs, ws))
    edge_off = p
    p += 4                       # 边缘倍率
    return p, dt, weights, r.data[edge_off:p]


def _build_vertex(r, head_bytes, weights, edge):
    """重建单个顶点字节。weights=[(骨索引,权重)]；>1 权重时用 BDEF4。"""
    if not weights:
        weights = [(0, 1.0)]
    out = bytearray()
    out.extend(head_bytes)       # 位置+法线+UV+附加UV
    if len(weights) == 1:
        out.extend(bytes([0]))
        out.extend(weights[0][0].to_bytes(r.bidx, 'little'))
    elif len(weights) == 2:
        out.extend(bytes([1]))
        out.extend(weights[0][0].to_bytes(r.bidx, 'little'))
        out.extend(weights[1][0].to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', weights[0][1]))
    else:
        out.extend(bytes([2]))
        bs = [b for b, w in weights[:4]]
        ws = [w for b, w in weights[:4]]
        while len(bs) < 4:
            bs.append(0)
        while len(ws) < 4:
            ws.append(0.0)
        for b in bs:
            out.extend(b.to_bytes(r.bidx, 'little'))
        for w in ws:
            out.extend(struct.pack('<f', w))
    out.extend(edge)
    return bytes(out)


def find_skirt(r):
    """识别裙骨 Sp_Hi_MSkirt0_{方向}_{段} -> {方向: {段号: 索引}}"""
    skirt = {}
    for i, (n, g, *_rest) in enumerate(r.bone_items):
        m = SKIRT_RE.match(g.rstrip())
        if m:
            skirt.setdefault(m.group(1), {})[int(m.group(2))] = i
    return skirt


def refine_skirt(src, dst, do_ring=True):
    r = PMXReader(src).parse()
    print('=' * 70)
    print(f'裙骨细化: {os.path.basename(src)}')
    print(f'骨骼={r.bone_count} 顶点={r.vertex_count}')

    skirt = find_skirt(r)
    if not skirt:
        print('[SKIP] 无裙骨 Sp_Hi_MSkirt0_*')
        return False
    print(f'裙骨: {len(skirt)} 方向')

    # 解析所有骨骼 item
    bone_dicts = [_parse_bone_item(r, it[4]) for it in r.bone_items]
    bone_positions = [d['pos'] for d in bone_dicts]

    # ============ 1. 竖向细分规划 + 生成中间骨骼 ============
    # mid[(dirn, k)] = (中间段位置, 中间段新索引)
    mid_pos = {}
    new_bone_dicts = []   # 追加的中间骨骼 dict
    for dirn in sorted(skirt):
        segs = sorted(skirt[dirn].keys())
        for k in range(len(segs) - 1):
            i0 = skirt[dirn][segs[k]]
            i1 = skirt[dirn][segs[k + 1]]
            p0 = bone_positions[i0]
            p1 = bone_positions[i1]
            mid = tuple((p0[j] + p1[j]) / 2 for j in range(3))
            mid_pos[(dirn, k)] = mid

    # 中间骨骼追加到末尾，新索引从 r.bone_count 开始
    new_idx_base = r.bone_count
    mid_idx = {}   # (dirn, k) -> 新索引
    _mid_counter = 0
    for dirn in sorted(skirt):
        segs = sorted(skirt[dirn].keys())
        for k in range(len(segs) - 1):
            i0 = skirt[dirn][segs[k]]
            i1 = skirt[dirn][segs[k + 1]]
            mid_idx[(dirn, k)] = new_idx_base + _mid_counter
            _mid_counter += 1
            # 中间段：parent=原段k, tail=原段k+1
            d = dict(pos=mid_pos[(dirn, k)],
                     parent=i0, layer=bone_dicts[i0]['layer'],
                     flag=0x001B, tail=i1, append=None, ik=None)
            new_bone_dicts.append(d)

    print(f'竖向细分: 新增中间骨骼 {_mid_counter} 个')

    # ============ 环向翻倍规划（参考 1033_3：相邻 2 方向 BDEF2 插值） ============
    # 竖向细分后的段位置列表：{方向: [段位置...]}（偶数为原段，奇数为中间段）
    seg_pos_v = {}
    for dirn, segs in skirt.items():
        segs_s = sorted(segs.keys())
        pl = []
        for k in range(len(segs_s)):
            pl.append(bone_positions[skirt[dirn][segs_s[k]]])
            if k < len(segs_s) - 1:
                pl.append(mid_pos[(dirn, k)])
        seg_pos_v[dirn] = pl

    ring_mid_name = []      # 环向中间方向名（按环向顺序）
    ring_seg_pos = {}       # {中间方向名: [段位置...]}
    dir_angle_map = {}      # {原方向: 角度}
    ring_neighbors = {}     # {中间方向名: (左原方向, 右原方向)}
    hip_i = None
    for i, (n, g, _, _, _) in enumerate(r.bone_items):
        if g.rstrip() == 'Hip':
            hip_i = i
            break
    center = bone_positions[hip_i] if hip_i is not None else (0, 0, 0)
    if do_ring:
        def _dir_angle(d):
            p = bone_positions[skirt[d][min(skirt[d].keys())]]
            return math.atan2(p[0] - center[0], p[2] - center[2])

        dirs_sorted = sorted(skirt.keys(), key=_dir_angle)
        for d in dirs_sorted:
            dir_angle_map[d] = _dir_angle(d)
        for i in range(len(dirs_sorted)):
            d_a = dirs_sorted[i]
            d_b = dirs_sorted[(i + 1) % len(dirs_sorted)]
            mname = 'M' + chr(ord('A') + i)   # MA, MB, ...
            pa = seg_pos_v[d_a]
            pb = seg_pos_v[d_b]
            mpos = []
            for j in range(min(len(pa), len(pb))):
                mpos.append(tuple((pa[j][k] + pb[j][k]) / 2 for k in range(3)))
            ring_mid_name.append(mname)
            ring_seg_pos[mname] = mpos
            ring_neighbors[mname] = (d_a, d_b)
        print(f'环向翻倍: 新增中间方向 {len(ring_mid_name)} 个')

    # 重建骨骼区（原骨骼 parent/tail 重连 + 追加中间骨骼）
    # 先复制原骨骼，重映射 parent/tail
    remap = {}   # 旧索引 -> 新索引（原骨骼索引不变）
    for i in range(r.bone_count):
        remap[i] = i

    # ============ 重建骨骼区 ============
    # 原骨骼索引不变（中间骨骼追加到末尾），故 Morph/显示帧/刚体 的骨骼引用无需改。
    # 裙骨原段：重建 item（parent/tail 重连 + 段号重排为偶数 2k）；非裙骨：原样复制。
    new_bone_items = []   # (name_raw, name_g_raw, item_raw)
    for i in range(r.bone_count):
        nm, nmg, nraw, ngraw, item = r.bone_items[i]
        is_skirt_seg = None
        for dirn, segs in skirt.items():
            if i in segs.values():
                s = next(s for s, idx in segs.items() if idx == i)
                is_skirt_seg = (dirn, s)
                break
        if is_skirt_seg:
            dirn, s = is_skirt_seg
            segs = sorted(skirt[dirn].keys())
            k = segs.index(s)
            d = dict(bone_dicts[i])
            if k > 0:
                d['parent'] = mid_idx[(dirn, k - 1)]
            if k < len(segs) - 1:
                d['tail'] = mid_idx[(dirn, k)]
            else:
                d['tail'] = NONE_IDX(r.bidx)
            new_name = f'Sp_Hi_MSkirt0_{dirn}_{2 * k:02d}'
            nb = new_name.encode('utf-16-le' if r.encoding == 0 else 'utf-8')
            nraw = nb
            ngraw = nb
            item_raw = _build_bone_item(r, d)
        else:
            item_raw = item
        new_bone_items.append((nraw, ngraw, item_raw))

    # 追加中间骨骼（段号奇数 2k+1）
    for dirn in sorted(skirt):
        segs = sorted(skirt[dirn].keys())
        for k in range(len(segs) - 1):
            i0 = skirt[dirn][segs[k]]
            i1 = skirt[dirn][segs[k + 1]]
            d = dict(pos=mid_pos[(dirn, k)], parent=i0, layer=bone_dicts[i0]['layer'],
                     flag=0x001B, tail=i1, append=None, ik=None)
            new_name = f'Sp_Hi_MSkirt0_{dirn}_{2 * k + 1:02d}'
            nb = new_name.encode('utf-16-le' if r.encoding == 0 else 'utf-8')
            new_bone_items.append((nb, nb, _build_bone_item(r, d)))

    # 追加环向中间方向骨骼（方向名 MA/MB/...，段号 0..m-1）
    ring_idx = {}   # {中间方向名: [段索引...]}
    _ring_counter = _mid_counter
    for mname in ring_mid_name:
        mpos = ring_seg_pos[mname]
        ring_idx[mname] = [r.bone_count + _ring_counter + j for j in range(len(mpos))]
        _ring_counter += len(mpos)
    for mname in ring_mid_name:
        mpos = ring_seg_pos[mname]
        for j in range(len(mpos)):
            parent = hip_i if j == 0 else ring_idx[mname][j - 1]
            tail = ring_idx[mname][j + 1] if j < len(mpos) - 1 else NONE_IDX(r.bidx)
            d = dict(pos=mpos[j], parent=parent, layer=0,
                     flag=0x001B, tail=tail, append=None, ik=None)
            name = f'Sp_Hi_MSkirt0_{mname}_{j:02d}'
            nb = name.encode('utf-16-le' if r.encoding == 0 else 'utf-8')
            new_bone_items.append((nb, nb, _build_bone_item(r, d)))

    # 方向 -> [左中间方向, 右中间方向]（用于环向插值）
    dir_to_mids = {d: [None, None] for d in skirt}
    for mname, (d_a, d_b) in ring_neighbors.items():
        dir_to_mids[d_b][0] = mname
        dir_to_mids[d_a][1] = mname

    new_bone_count = len(new_bone_items)
    bone_out = bytearray()
    bone_out.extend(new_bone_count.to_bytes(4, 'little'))
    for nraw, ngraw, item_raw in new_bone_items:
        bone_out.extend(len(nraw).to_bytes(4, 'little', signed=True))
        bone_out.extend(nraw)
        bone_out.extend(len(ngraw).to_bytes(4, 'little', signed=True))
        bone_out.extend(ngraw)
        bone_out.extend(item_raw)

    # ============ 顶点区重建（权重线性插值） ============
    skirt_by_idx = {}
    for dirn, segs in skirt.items():
        for s, idx in segs.items():
            skirt_by_idx[idx] = (dirn, s)

    vertex_out = bytearray()
    off = r.vertex_start
    head_len = 32 + 16 * r.uv_count
    for vi in range(r.vertex_count):
        next_off, dt, weights, edge = _parse_vertex(r, off)
        head = r.data[off:off + head_len]
        vpos = struct.unpack_from('<3f', r.data, off)
        new_weights = []
        for b, w in weights:
            if b not in skirt_by_idx:
                new_weights.append((b, w))
                continue
            dirn, s = skirt_by_idx[b]
            segs = sorted(skirt[dirn].keys())
            k = segs.index(s)
            ps = bone_positions[b]
            # ---- 竖向插值：拆到「原段 2k」和「中间段 2k±1」 ----
            v_parts = []   # [(段号t, 骨索引, 权重)]
            if k < len(segs) - 1:
                mdown = mid_pos[(dirn, k)]
                d_s = abs(vpos[1] - ps[1]); d_m = abs(vpos[1] - mdown[1])
                if d_s + d_m > 1e-6 and d_m < d_s:
                    w_s = w * d_m / (d_s + d_m); w_m = w * d_s / (d_s + d_m)
                    if w_m > 0.001:
                        v_parts = [(2 * k, b, w_s), (2 * k + 1, mid_idx[(dirn, k)], w_m)]
            if not v_parts and k > 0:
                mup = mid_pos[(dirn, k - 1)]
                d_s = abs(vpos[1] - ps[1]); d_m = abs(vpos[1] - mup[1])
                if d_s + d_m > 1e-6 and d_m < d_s:
                    w_s = w * d_m / (d_s + d_m); w_m = w * d_s / (d_s + d_m)
                    if w_m > 0.001:
                        v_parts = [(2 * k - 1, mid_idx[(dirn, k - 1)], w_m), (2 * k, b, w_s)]
            if not v_parts:
                v_parts = [(2 * k, b, w)]
            # ---- 环向插值：每个 part 拆到「dirn」和相邻中间方向（参考 1033_3 的 BDEF2） ----
            for t, bidx, wv in v_parts:
                if not do_ring:
                    new_weights.append((bidx, wv))
                    continue
                cands = []
                for m in dir_to_mids[dirn]:
                    if m is not None and t < len(ring_idx[m]):
                        mp = ring_seg_pos[m][t]
                        cands.append((m, math.hypot(vpos[0] - mp[0], vpos[2] - mp[2])))
                dp = seg_pos_v[dirn][t]
                d_self = math.hypot(vpos[0] - dp[0], vpos[2] - dp[2])
                if cands:
                    m, d_m = min(cands, key=lambda x: x[1])
                    if d_m < d_self and d_self + d_m > 1e-6:
                        w_self = wv * d_m / (d_self + d_m)
                        w_m = wv * d_self / (d_self + d_m)
                        if w_m > 0.001:
                            new_weights.append((bidx, w_self))
                            new_weights.append((ring_idx[m][t], w_m))
                            continue
                new_weights.append((bidx, wv))
        # 合并同索引权重
        merged = {}
        for b, w in new_weights:
            merged[b] = merged.get(b, 0.0) + w
        wf = [(b, w) for b, w in merged.items() if w > 0.001]
        if len(wf) > 4:
            wf.sort(key=lambda x: -x[1])
            wf = wf[:4]
            tot = sum(w for _, w in wf)
            wf = [(b, w / tot) for b, w in wf]
        vertex_out.extend(_build_vertex(r, head, wf, edge))
        off = next_off

    # ============ 组装输出 ============
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
    out.extend(bone_out)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(r.morph_bytes)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(r.frame_bytes)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(r.rigid_bytes)
    if r.joint_bytes:
        out.extend(r.joint_count.to_bytes(4, 'little'))
        out.extend(r.joint_bytes)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    v = PMXReader(dst).parse()
    print(f'[OK] 骨骼 {r.bone_count}->{v.bone_count}, 顶点 {r.vertex_count}, remaining={v.remaining}')
    return True


