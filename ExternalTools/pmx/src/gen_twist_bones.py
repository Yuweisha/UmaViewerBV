# -*- coding: utf-8 -*-
"""
腕捩生成器 —— 为"程序导出同体系"简化骨架生成 MMD 标准 twist 骨（腕捩/手捩 + 捩1/2/3）
通用处理 1025/1033 等程序导出模型。废品捩骨特征（命名错位）：
  腕捩=ArmRoll 实际在前臂（本应手捩）、肩捩=ShoulderRoll 实际在大臂（本应腕捩）。

生成逻辑（对照 1025_2 实证，左右臂对称）：
  1. 腕捩/手捩 位置 = 骨段 60% 处（腕→肘 / 肘→手首）
  2. 捩1/2/3 = 本地追加继承 腕捩/手捩，比例 0.25/0.50/0.75；位置沿骨段 t≈1/3、1/2、3/4
  3. 腕捩/手捩 flag=0x041B（tail|旋转|可见|可操作|固定轴）；捩1/2/3 flag=0x0113（tail|旋转|可操作|继承）
  4. 固定轴 = 骨方向单位向量（腕→肘 / 肘→手首）
  5. 父链重链接：腕 → 腕捩 → ひじ → 手捩 → 手首
  6. 删除原 肩捩(ShoulderRoll)+腕捩(ArmRoll) 4 根废品，权重按段归位：
       肩捩(上臂) -> 腕捩(ARM)、腕捩(前臂) -> 手捩(WRIST)
  7. 修复 ひじ.tail（原指向被删腕捩）-> 指向新手捩（前臂方向）

用法：
  python gen_twist_bones.py    # 批量处理当前目录所有 .pmx（自动备份到 [11]备份_时间戳/，原地覆盖）
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
from clean_handle_bones import PMXReader, decode_text  # noqa: E402
from bone_redirect import build_parent_redirect, resolve_redirect  # noqa: E402


def _write_idx(ba, off, val, size):
    v = val & 0xFFFFFFFF
    for k in range(size):
        ba[off + k] = (v >> (8 * k)) & 0xFF


def norm3(v):
    n = (v[0]**2 + v[1]**2 + v[2]**2) ** 0.5
    return (v[0]/n, v[1]/n, v[2]/n) if n else (0.0, 0.0, 0.0)


def lerp3(a, b, t):
    return (a[0]+(b[0]-a[0])*t, a[1]+(b[1]-a[1])*t, a[2]+(b[2]-a[2])*t)


# 位置比例（实测自 1025_2，左右臂一致）
ARM_TWIST_T = 0.60
WRIST_TWIST_T = 0.60
ARM_TWIST_SUB_T = (0.3287, 0.5376, 0.7464)    # 腕捩1/2/3
WRIST_TWIST_SUB_T = (0.3259, 0.5317, 0.7375)  # 手捩1/2/3
INHERIT_RATIO = (0.25, 0.50, 0.75)

# 程序导出同体系模型的左右侧（日文前缀 / 英文后缀）
SIDES = (('左', 'L'), ('右', 'R'))


def _find_bone(idx_name, idx_ng, jp_keys, en_keys):
    """按候选名查找骨骼索引：日文 name 优先，英文 name_g 兜底；找不到返回 None"""
    for k in jp_keys:
        v = idx_name.get(k)
        if v is not None:
            return v
    for k in en_keys:
        v = idx_ng.get(k)
        if v is not None:
            return v
    return None


def resolve_arm_bones(idx_name, idx_ng):
    """解析左右臂主链骨 + 废品捩骨，返回 {arm/elbow/wrist/armroll/shoulderroll: {side_cn: index}}"""
    res = {'arm': {}, 'elbow': {}, 'wrist': {}, 'armroll': {}, 'shoulderroll': {}}
    for side_cn, side_en in SIDES:
        # 主链骨：日文 name 优先，英文 name_g 兜底
        res['arm'][side_cn] = _find_bone(idx_name, idx_ng, [f'{side_cn}腕'], [f'Arm_{side_en}'])
        res['elbow'][side_cn] = _find_bone(idx_name, idx_ng, [f'{side_cn}ひじ'], [f'Elbow_{side_en}'])
        res['wrist'][side_cn] = _find_bone(idx_name, idx_ng, [f'{side_cn}手首'], [f'Wrist_{side_en}'])
        # 废品捩骨：以英文 name_g 为准（ArmRoll/ShoulderRoll 是程序导出的唯一标识，
        # 避免与重建后的正确捩骨同名「左腕捩」冲突）
        res['armroll'][side_cn] = _find_bone(idx_name, idx_ng, [], [f'ArmRoll_{side_en}'])
        res['shoulderroll'][side_cn] = _find_bone(idx_name, idx_ng, [], [f'ShoulderRoll_{side_en}'])
    return res


def collect_weight_positions(r):
    """返回 {权重点绝对偏移: 顶点位置(x,y,z)}（偏移与 PMXReader.weight_points 一一对应）"""
    p = r.vertex_start
    wpos = {}
    for _ in range(r.vertex_count):
        vpos = struct.unpack_from('<3f', r.data, p)
        p += 32
        p += r.uv_count * 16
        dt = r.data[p]; p += 1
        if dt == 0:                      # BDEF1
            wpos[p] = vpos; p += r.bidx
        elif dt == 1:                    # BDEF2
            wpos[p] = vpos; p += r.bidx
            wpos[p] = vpos; p += r.bidx
            p += 4
        elif dt == 2:                    # BDEF4（与 clean_handle_bones 解析一致）
            for _ in range(4):
                wpos[p] = vpos; p += r.bidx
                p += 4
        elif dt == 3:                    # SDEF
            wpos[p] = vpos; p += r.bidx
            wpos[p] = vpos; p += r.bidx
            p += 4 + 36
        elif dt == 4:                    # QDEF
            for _ in range(4):
                wpos[p] = vpos; p += r.bidx
            p += 16
        p += 4                          # 边缘倍率
    return wpos


def pick_gradient(p_parent, p_child, sub_bones, vpos):
    """按顶点在骨段上的归一化投影 t 选择渐变骨：[捩1(25%), 捩2(50%), 捩3(75%), 主(100%)]"""
    ab = (p_child[0] - p_parent[0], p_child[1] - p_parent[1], p_child[2] - p_parent[2])
    d = ab[0] * ab[0] + ab[1] * ab[1] + ab[2] * ab[2]
    if d <= 0:
        return sub_bones[3]
    ap = (vpos[0] - p_parent[0], vpos[1] - p_parent[1], vpos[2] - p_parent[2])
    t = (ap[0] * ab[0] + ap[1] * ab[1] + ap[2] * ab[2]) / d
    t = max(0.0, min(1.0, t))
    # 就近量化到 25%/50%/75%/100% 四个渐变档（比例中点 0.375/0.625/0.875）
    if t < 0.375:
        return sub_bones[0]
    if t < 0.625:
        return sub_bones[1]
    if t < 0.875:
        return sub_bones[2]
    return sub_bones[3]


def build_new_bone_item(r, pos, parent, flag, axis=None,
                        append_parent=None, ratio=None):
    """构建一根新骨骼的 item_raw（不含 name 段）"""
    out = bytearray()
    out.extend(struct.pack('<3f', *pos))
    out.extend(parent.to_bytes(r.bidx, 'little'))
    out.extend((0).to_bytes(4, 'little'))          # layer
    out.extend(flag.to_bytes(2, 'little'))
    if flag & 0x0001:                              # tail 骨（None）
        none_idx = (1 << (8 * r.bidx)) - 1
        out.extend(none_idx.to_bytes(r.bidx, 'little'))
    else:
        out.extend(struct.pack('<3f', 0.0, 0.0, 0.0))
    if flag & 0x0100:                              # 本地追加(继承)
        out.extend(append_parent.to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', ratio))
    if flag & 0x0400:                              # 固定轴
        out.extend(struct.pack('<3f', *axis))
    return bytes(out)
def gen_twist_bones(src, dst):
    print('=' * 70)
    print(f'腕捩生成: {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐(剩余{r.remaining}字节)')
        return False
    print(f'  骨骼={r.bone_count} encoding={r.encoding} bidx={r.bidx}')

    # ---- 1. 解析旧骨位置 ----
    base = r.bone_start
    pos = 0
    old_xyz = {}
    for i in range(r.bone_count):
        n = int.from_bytes(r.data[base+pos:base+pos+4], 'little', signed=True); pos += 4
        nm = decode_text(r.data[base+pos:base+pos+n]); pos += n
        n2 = int.from_bytes(r.data[base+pos:base+pos+4], 'little', signed=True); pos += 4
        ng = decode_text(r.data[base+pos:base+pos+n2]); pos += n2
        xyz = struct.unpack_from('<3f', r.data, base+pos); pos += 12
        old_xyz[i] = (nm.rstrip(), ng.rstrip(), xyz)
        pos += r.bidx + 4 + 2
        flag = int.from_bytes(r.data[base+pos-2:base+pos], 'little')
        if flag & 0x0001:
            pos += r.bidx
        else:
            pos += 12
        if flag & 0x0100:
            pos += r.bidx + 4
        if flag & 0x0200:
            pos += r.bidx + 4
        if flag & 0x0400:
            pos += 12
        if flag & 0x0800:
            pos += 24
        if flag & 0x2000:
            pos += 4
        if flag & 0x0020:
            pos += r.bidx + 8
            ic = int.from_bytes(r.data[base+pos:base+pos+4], 'little'); pos += 4
            for _ in range(ic):
                pos += r.bidx
                if r.data[base+pos]:
                    pos += 24
                pos += 1

    idx_name = {nm: i for i, (nm, ng, _) in old_xyz.items()}
    idx_ng = {ng: i for i, (nm, ng, _) in old_xyz.items() if ng}
    bones = resolve_arm_bones(idx_name, idx_ng)

    # ---- 1b. 体系识别：必须存在 4 根废品捩骨，否则跳过 ----
    junk_present = all(bones['armroll'][s] is not None and bones['shoulderroll'][s] is not None
                       for s, _ in SIDES)
    if not junk_present:
        print(f'  [SKIP] 未找到废品捩骨(腕捩=ArmRoll/肩捩=ShoulderRoll)，'
              f'可能已处理或非本体系模型，跳过')
        return 'skip'
    main_missing = [f'{s}{k}' for s, _ in SIDES
                    for k, d in (('腕', 'arm'), ('ひじ', 'elbow'), ('手首', 'wrist'))
                    if bones[d][s] is None]
    if main_missing:
        print(f'  [ERROR] 缺少主链骨 腕/ひじ/手首: {main_missing}')
        return False

    print(f'  废品捩骨: ' + ', '.join(
        f'{s}腕捩={bones["armroll"][s]}/{s}肩捩={bones["shoulderroll"][s]}' for s, _ in SIDES))

    # ---- 2. 生成新骨参数 ----
    new_bones = []   # (insert_before_old_idx, [bone tuples])
    link_fix = []    # (old_bone_idx, 'ELBOW_P'|'WRIST_P', side_cn)
    for side_cn, side_en in SIDES:
        i_arm = bones['arm'][side_cn]; i_elbow = bones['elbow'][side_cn]
        i_wrist = bones['wrist'][side_cn]
        p_arm = old_xyz[i_arm][2]; p_elbow = old_xyz[i_elbow][2]; p_wrist = old_xyz[i_wrist][2]

        grp = []   # 腕捩系列（插在 elbow 前）
        atw_axis = norm3((p_elbow[0]-p_arm[0], p_elbow[1]-p_arm[1], p_elbow[2]-p_arm[2]))
        grp.append((f'{side_cn}腕捩', f'arm twist_{side_en}', lerp3(p_arm, p_elbow, ARM_TWIST_T),
                    i_arm, 0x041B, atw_axis, None, None))
        for k, tt, rt in zip((1, 2, 3), ARM_TWIST_SUB_T, INHERIT_RATIO):
            grp.append((f'{side_cn}腕捩{k}', '', lerp3(p_arm, p_elbow, tt),
                        i_arm, 0x0113, None, 'ARM_TWIST', rt))
        new_bones.append((i_elbow, grp))

        grp2 = []   # 手捩系列（插在 wrist 前）
        wtw_axis = norm3((p_wrist[0]-p_elbow[0], p_wrist[1]-p_elbow[1], p_wrist[2]-p_elbow[2]))
        grp2.append((f'{side_cn}手捩', f'wrist twist_{side_en}', lerp3(p_elbow, p_wrist, WRIST_TWIST_T),
                     i_elbow, 0x041B, wtw_axis, None, None))
        for k, tt, rt in zip((1, 2, 3), WRIST_TWIST_SUB_T, INHERIT_RATIO):
            grp2.append((f'{side_cn}手捩{k}', '', lerp3(p_elbow, p_wrist, tt),
                         i_elbow, 0x0113, None, 'WRIST_TWIST', rt))
        new_bones.append((i_wrist, grp2))

        link_fix.append((i_elbow, 'ELBOW_P', side_cn))
        link_fix.append((i_wrist, 'WRIST_P', side_cn))

    delete_set = {bones['armroll'][s] for s, _ in SIDES} | {bones['shoulderroll'][s] for s, _ in SIDES}
    for i in sorted(delete_set):
        wcnt = sum(1 for _p, b in r.weight_points if b == i)
        print(f'  删除 {old_xyz[i][0]} (name_g={old_xyz[i][1]}, 权重引用={wcnt})')

    # 废品捩骨被删后，指向它们的骨骼 parent/其它区引用重映射到其父骨（通用规则"删骨→父骨"，
    # 见 bone_redirect.build_parent_redirect）：肩捩 ShoulderRoll 挂在大臂(腕)下 -> 腕；
    # 腕捩 ArmRoll 挂在前臂(ひじ)下 -> ひじ。否则 Sp_So_Collar0_L_00 等骨骼 parent 会被置 None。
    par_list = [int.from_bytes(r.bone_items[i][4][12:12 + r.bidx], 'little') for i in range(r.bone_count)]
    replace_map = build_parent_redirect(par_list, delete_set)

    # ---- 3. old_to_new + 新骨占位 ----
    inserts = {before: grp for before, grp in new_bones}
    old_to_new = {}
    new_item_plan = []
    for old in range(r.bone_count):
        if old in inserts:
            new_item_plan.extend(inserts[old])   # 新骨占位（引用待解析）
        if old in delete_set:
            continue
        old_to_new[old] = len(new_item_plan)
        new_item_plan.append(('OLD', old))

    # 登记 twist 骨新索引（供继承引用 + 渐变权重分配）
    twist_new_idx = {}   # (side, 'ARM'|'WRIST') -> 主捩骨索引
    sub_new_idx = {}     # (side, 'ARM'|'WRIST', 1|2|3) -> 子捩骨索引
    for k, item in enumerate(new_item_plan):
        if item[0] == 'OLD':          # 新骨 = 8元组；旧骨 = ('OLD', idx)
            continue
        nm = item[0]
        side = nm[0]
        if item[6] in ('ARM_TWIST', 'WRIST_TWIST'):
            seg = 'ARM' if item[6] == 'ARM_TWIST' else 'WRIST'
            sub_new_idx[(side, seg, int(nm[-1]))] = k
        elif (item[4] & 0x041B) == 0x041B:
            seg = 'ARM' if '腕捩' in nm else 'WRIST'
            twist_new_idx[(side, seg)] = k
    print(f'  新 twist 索引: {twist_new_idx}')
    # ---- 4. 重建骨骼区字节 ----
    out_bone = bytearray()
    out_bone.extend(len(new_item_plan).to_bytes(4, 'little'))
    old_new_off = {}
    for k, item in enumerate(new_item_plan):
        if item[0] != 'OLD':          # 新骨 = 8元组
            nm, ng, p, pold, flag, axis, apold, ratio = item
            parent = old_to_new.get(pold, pold)
            append_new = None
            if apold == 'ARM_TWIST':
                append_new = twist_new_idx[(nm[0], 'ARM')]
            elif apold == 'WRIST_TWIST':
                append_new = twist_new_idx[(nm[0], 'WRIST')]
            item_raw = build_new_bone_item(r, p, parent, flag, axis=axis,
                                           append_parent=append_new, ratio=ratio)
            nb = nm.encode('utf-16-le')          # 与 rename_bones 写入一致
            nbg = ng.encode('utf-16-le') if ng else b''
            out_bone.extend(len(nb).to_bytes(4, 'little', signed=True))
            out_bone.extend(nb)
            out_bone.extend(len(nbg).to_bytes(4, 'little', signed=True))
            out_bone.extend(nbg)
            out_bone.extend(item_raw)
        else:
            old = item[1]
            name_str, name_g_str, name_raw, ng_raw, item_raw = r.bone_items[old]
            out_bone.extend(len(name_raw).to_bytes(4, 'little', signed=True))
            out_bone.extend(name_raw)
            out_bone.extend(len(ng_raw).to_bytes(4, 'little', signed=True))
            out_bone.extend(ng_raw)
            off = len(out_bone)
            out_bone.extend(item_raw)
            old_new_off[old] = (off, off + len(item_raw))

    # ---- 5. 旧骨内部引用重映射 + 父链/尾链覆盖 ----
    bone_ba = bytearray(out_bone)
    none_idx = (1 << (8 * r.bidx)) - 1
    tail_rel = 12 + r.bidx + 4 + 2            # tail 字段相对 item 起点偏移
    elbow_old = {s: bones['elbow'][s] for s, _ in SIDES}
    skip_ref = {(old, 12) for old, kind, side in link_fix}  # 父字段稍后由 link_fix 覆盖
    for side_cn, i_elbow in elbow_old.items():
        skip_ref.add((i_elbow, tail_rel))               # ひじ.tail 稍后覆盖为手捩
    for old in range(r.bone_count):
        if old in delete_set:
            continue
        off, _ = old_new_off[old]
        for rel, oidx in r.bone_refs[old]:
            if oidx == none_idx:
                continue
            if (old, rel) in skip_ref:
                continue                       # 待覆盖的父/尾引用
            new = old_to_new.get(oidx)
            if new is None and oidx in replace_map:
                new = old_to_new.get(replace_map[oidx], none_idx)   # 指向被删捩骨 -> 其父骨
            if new is None:
                new = none_idx                 # 指向被删骨 -> None
            _write_idx(bone_ba, off + rel, new, r.bidx)
    for old, kind, side in link_fix:
        off, _ = old_new_off[old]
        k2 = 'ARM' if kind == 'ELBOW_P' else 'WRIST'
        newp = twist_new_idx[(side, k2)]
        _write_idx(bone_ba, off + 12, newp, r.bidx)   # 父字段 = item 起点+12
        print(f'  父链: {old_xyz[old][0]}.parent -> {newp} ({side}{"腕捩" if k2 == "ARM" else "手捩"})')
    for side_cn, i_elbow in elbow_old.items():
        off, _ = old_new_off[i_elbow]
        newt = twist_new_idx[(side_cn, 'WRIST')]
        _write_idx(bone_ba, off + tail_rel, newt, r.bidx)
        print(f'  尾链: {old_xyz[i_elbow][0]}.tail -> {newt} ({side_cn}手捩)')
    new_bone_bytes = bytes(bone_ba)

    # ---- 6. 其余区引用重映射 ----
    # 顶点权重做"方向分解"渐变：废品捩骨按段 + 沿骨投影位置 分配到 主捩骨 + 捩1/2/3
    #   （肩捩→腕捩1/2/3/腕捩；腕捩→手捩1/2/3/手捩）；其余区仅定点改写索引
    gradient_map = {}
    for s, _ in SIDES:
        p_arm = old_xyz[bones['arm'][s]][2]
        p_elbow = old_xyz[bones['elbow'][s]][2]
        p_wrist = old_xyz[bones['wrist'][s]][2]
        gradient_map[bones['shoulderroll'][s]] = (
            p_arm, p_elbow,
            [sub_new_idx[(s, 'ARM', 1)], sub_new_idx[(s, 'ARM', 2)],
             sub_new_idx[(s, 'ARM', 3)], twist_new_idx[(s, 'ARM')]])
        gradient_map[bones['armroll'][s]] = (
            p_elbow, p_wrist,
            [sub_new_idx[(s, 'WRIST', 1)], sub_new_idx[(s, 'WRIST', 2)],
             sub_new_idx[(s, 'WRIST', 3)], twist_new_idx[(s, 'WRIST')]])
    wpos = collect_weight_positions(r)

    def remap(points, section_start, bytes_):
        ba = bytearray(bytes_)
        for abs_off, old in points:
            if old == none_idx:
                continue
            new = old_to_new.get(old)
            if new is None and old in replace_map:
                new = old_to_new.get(replace_map[old], none_idx)
            if new is None:
                new = none_idx            # 指向被删骨 -> None
            _write_idx(ba, abs_off - section_start, new, r.bidx)
        return bytes(ba)

    grad_stat = {}
    def remap_weight(points, section_start, bytes_):
        ba = bytearray(bytes_)
        for abs_off, old in points:
            if old == none_idx:
                continue
            if old in gradient_map:
                p0, p1, sub_bones = gradient_map[old]
                new = pick_gradient(p0, p1, sub_bones, wpos[abs_off])
                grad_stat[new] = grad_stat.get(new, 0) + 1
            else:
                new = old_to_new.get(old)
                if new is None:
                    new = none_idx
            _write_idx(ba, abs_off - section_start, new, r.bidx)
        return bytes(ba)

    vertex_out = remap_weight(r.weight_points, r.vertex_start, r.vertex_bytes)
    morph_out = remap(r.morph_bone_points, r.morph_start, r.morph_bytes)
    frame_out = remap(r.frame_bone_points, r.frame_start, r.frame_bytes)
    rigid_out = remap(r.rigid_bone_points, r.rigid_start, r.rigid_bytes)
    grad_summary = {nm: grad_stat.get(i, 0)
                    for nm, i in sorted(sub_new_idx.items(), key=lambda x: (x[0][0], x[0][1], x[0][2]))}
    for nm, i in sorted(twist_new_idx.items()):
        grad_summary[nm] = grad_stat.get(i, 0)
    print(f'  渐变分配(权重点数): {grad_summary}')

    # ---- 7. 组装输出 ----
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
    out.extend(new_bone_bytes)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(morph_out)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(frame_out)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(rigid_out)
    out.extend(r.joint_count.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)

    # ---- 8. 写回 + 验证 ----
    with open(dst, 'wb') as f:
        f.write(bytes(out))
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败: 剩余{v.remaining}字节'
    expect = r.bone_count + 16 - len(delete_set)
    assert v.bone_count == expect, f'验证失败: 骨骼数 {expect} != {v.bone_count}'
    print(f'  [OK] 写入 {os.path.basename(dst)}: 骨骼 {r.bone_count} -> {v.bone_count} 剩余字节=0')
    return True




