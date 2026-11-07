# -*- coding: utf-8 -*-
"""
[14] D骨 ik骨统一处理 —— 对齐 1025_2 的腿部标准结构（含 D骨链/腰キャンセル/IK/辅助骨）
================================================================
在 ankle_process 基础上补全，每块骨骼的新建/改动都打印到控制台。

处理（左右对称，以左腿为例）：
  1. 权重迁移：足->足D、ひざ->ひざD（皮肤从主骨改绑 D骨）
  2. 主骨改球状：足/ひざ/足首 flag 0x001F->0x001B、つま先 0x001F->0x0013（去移動可）
  3. 新建 腰キャンセル（回転付与=腰 -1.0）
  4. 新建 D骨链 足D/ひざD（回転付与=主骨 1.0，layer=1）；足首EX->足首D；足先EX 归位
  5. 新建 足IK（target=足首 loop=40 rad=2.0 链=ひざ[限幅]+足）、つま先IK（target=つま先 loop=3 rad=4.0 链=足首）
  6. 新建 ひざ補助（回転付与=ひざD 0.5）、尻補助腰C/尻補助

用法：
  python hip_process.py    # 批量处理当前目录所有 .pmx（自动备份到 [12]备份_时间戳/，原地覆盖）
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
from bone_redirect import resolve_redirect  # noqa: E402

SIDES = (('左', 'L'), ('右', 'R'))
TOE_EX_T = 0.60              # 足先EX 前脚掌位置：足首->つま先 60%
IK_SHIN_Z = 1.267            # 足IK先 z 偏移（向上）
IK_TOE_Y = -1.047            # つま先IK先 y 偏移（向下）
KNEE_LIMIT = ((-3.1415927, 0.0, 0.0), (-0.0087266, 0.0, 0.0))  # 膝盖 IK 限幅 [-π, -0.5°]


def _write_idx(ba, off, val, size):
    v = val & 0xFFFFFFFF
    for k in range(size):
        ba[off + k] = (v >> (8 * k)) & 0xFF


def _find(idx_name, idx_ng, jp, en):
    if jp in idx_name:
        return idx_name[jp]
    if en in idx_ng:
        return idx_ng[en]
    return None


def lerp3(a, b, t):
    return (a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t)


def parse_item(r, raw):
    """解析骨骼 item_raw（不含 name/name_g），返回全部字段"""
    p = 0
    xyz = struct.unpack_from('<3f', raw, p); p += 12
    parent = int.from_bytes(raw[p:p + r.bidx], 'little'); p += r.bidx
    layer = int.from_bytes(raw[p:p + 4], 'little', signed=True); p += 4
    flag = int.from_bytes(raw[p:p + 2], 'little'); p += 2
    tail = None
    if flag & 0x0001:
        tail = int.from_bytes(raw[p:p + r.bidx], 'little'); p += r.bidx
    else:
        tail = struct.unpack_from('<3f', raw, p); p += 12
    ap = None; ratio = None
    if flag & 0x0100:
        ap = int.from_bytes(raw[p:p + r.bidx], 'little'); p += r.bidx
        ratio = struct.unpack_from('<f', raw, p)[0]; p += 4
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
        iktarget = int.from_bytes(raw[p:p + r.bidx], 'little'); p += r.bidx
        ikloop = int.from_bytes(raw[p:p + 4], 'little', signed=True); p += 4
        ikrad = struct.unpack_from('<f', raw, p)[0]; p += 4
        ikn = int.from_bytes(raw[p:p + 4], 'little', signed=True); p += 4
        links = []
        for _ in range(ikn):
            li = int.from_bytes(raw[p:p + r.bidx], 'little'); p += r.bidx
            lim = raw[p]; p += 1
            limv = None
            if lim:
                low = struct.unpack_from('<3f', raw, p); p += 12
                high = struct.unpack_from('<3f', raw, p); p += 12
                limv = (low, high)
            links.append((li, limv))
        ik = (iktarget, ikloop, ikrad, links)
    return {'xyz': xyz, 'parent': parent, 'layer': layer, 'flag': flag,
            'tail': tail, 'ap': ap, 'ratio': ratio, 'ik': ik}


def build_item_raw(r, xyz, parent, layer, flag, tail=None, append_parent=None,
                   append_ratio=None, ik=None):
    """构建骨骼 item_raw（支持 tail骨/offset、回転付与、IK 链）"""
    out = bytearray()
    out.extend(struct.pack('<3f', *xyz))
    out.extend(parent.to_bytes(r.bidx, 'little'))
    out.extend(struct.pack('<i', layer))
    out.extend(flag.to_bytes(2, 'little'))
    none_idx = (1 << (8 * r.bidx)) - 1
    if flag & 0x0001:
        out.extend((none_idx if tail is None else tail).to_bytes(r.bidx, 'little'))
    else:
        t = tail if isinstance(tail, tuple) else (0.0, 0.0, 0.0)
        out.extend(struct.pack('<3f', *t))
    if flag & 0x0100:
        out.extend(append_parent.to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', append_ratio))
    if flag & 0x0200:
        out.extend(append_parent.to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<f', append_ratio))
    if flag & 0x0400:
        out.extend(struct.pack('<3f', 0.0, 0.0, 0.0))
    if flag & 0x0800:
        out.extend(struct.pack('<6f', *([0.0] * 6)))
    if flag & 0x2000:
        out.extend(struct.pack('<i', -1))
    if flag & 0x0020 and ik is not None:
        iktarget, ikloop, ikrad, links = ik
        out.extend(iktarget.to_bytes(r.bidx, 'little'))
        out.extend(struct.pack('<i', ikloop))
        out.extend(struct.pack('<f', ikrad))
        out.extend(struct.pack('<i', len(links)))
        for li, limv in links:
            out.extend(li.to_bytes(r.bidx, 'little'))
            if limv is None:
                out.extend(b'\x00')
            else:
                out.extend(b'\x01')
                out.extend(struct.pack('<3f', *limv[0]))
                out.extend(struct.pack('<3f', *limv[1]))
    return bytes(out)


def hip_process(src, dst):
    print('=' * 70)
    print(f'D骨 ik骨统一处理: {os.path.basename(src)}')
    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f'[ERROR] 解析未对齐(剩余{r.remaining}字节)')
        return False
    print(f'  骨骼={r.bone_count} bidx={r.bidx}')

    # ---- 1. 定位骨骼 ----
    idx_name = {it[0].rstrip(): i for i, it in enumerate(r.bone_items)}
    idx_ng = {it[1].rstrip(): i for i, it in enumerate(r.bone_items) if it[1].rstrip()}
    hip = _find(idx_name, idx_ng, '下半身', 'Hip')
    waist = _find(idx_name, idx_ng, '腰', 'Waist')
    root = _find(idx_name, idx_ng, '全ての親', 'Position')
    upbody = _find(idx_name, idx_ng, '上半身Ctrl', 'UpBody_Ctrl')
    b = {}
    for s, e in SIDES:
        b[(s, 'thigh')] = _find(idx_name, idx_ng, f'{s}足', f'Thigh_{e}')
        b[(s, 'knee')] = _find(idx_name, idx_ng, f'{s}ひざ', f'Knee_{e}')
        b[(s, 'ankle')] = _find(idx_name, idx_ng, f'{s}足首', f'Ankle_{e}')
        b[(s, 'toe')] = _find(idx_name, idx_ng, f'{s}つま先', f'Toe_{e}')
        b[(s, 'ankle_off')] = _find(idx_name, idx_ng, f'{s}足首EX', f'Ankle_offset_{e}')
        b[(s, 'toe_off')] = _find(idx_name, idx_ng, f'{s}足先EX', f'Toe_offset_{e}')
    need = ('thigh', 'knee', 'ankle', 'toe', 'ankle_off', 'toe_off')
    missing = [f'{s}{k}' for s, e in SIDES for k in need if b[(s, k)] is None]
    for nm, v in (('下半身', hip), ('腰', waist), ('全ての親', root)):
        if v is None:
            missing.append(nm)
    if all(b[(s, 'ankle_off')] is None for s, e in SIDES):
        print('  [SKIP] 未找到足首EX(Ankle_offset)，可能已处理或非本体系，跳过')
        return 'skip'
    if missing:
        print(f'  [ERROR] 缺少骨骼: {missing}')
        return False

    info = {(s, k): parse_item(r, r.bone_items[b[(s, k)]][4]) for s, e in SIDES for k in need}
    info_hip = parse_item(r, r.bone_items[hip][4])
    info_waist = parse_item(r, r.bone_items[waist][4])

    # ---- 2. 生成参数 ----
    inserts = {}   # before_old_idx -> [('NEW', ...)]
    mods = {}      # old_idx -> ('MOD', ...)
    w_migrate = {}  # 旧骨索引 -> 新骨标识（权重迁移）
    for s, e in SIDES:
        thigh = b[(s, 'thigh')]
        knee = b[(s, 'knee')]
        ankle = b[(s, 'ankle')]
        toe = b[(s, 'toe')]
        ao = b[(s, 'ankle_off')]
        tof = b[(s, 'toe_off')]
        p_thigh = info[(s, 'thigh')]['xyz']
        p_knee = info[(s, 'knee')]['xyz']
        p_ankle = info[(s, 'ankle')]['xyz']
        p_toe = info[(s, 'toe')]['xyz']
        lc = f'腰キャンセル{s}'
        td = f'{s}足D'
        kd = f'{s}ひざD'

        # 新建骨（插在足之前）
        ik_shin = (p_ankle[0], p_ankle[1], p_ankle[2] + IK_SHIN_Z)
        ik_floor = (p_ankle[0], 0.0, p_ankle[2])
        toe_ik_tip = (p_toe[0], p_toe[1] + IK_TOE_Y, p_toe[2])
        inserts.setdefault(thigh, []).extend([
            ('NEW', lc, '', p_thigh, hip, 0, 0x0103, None, waist, -1.0, None),
            ('NEW', td, 'D', p_thigh, hip, 1, 0x011B, None, thigh, 1.0, None),
            ('NEW', kd, 'D', p_knee, td, 1, 0x011B, None, knee, 1.0, None),
            ('NEW', f'{s}足IK親', f'leg IKP_{e}', ik_floor, root, 0, 0x001E, (0.0, p_ankle[1], 0.0), None, None, None),
            ('NEW', f'{s}足ＩＫ', '', p_ankle, f'{s}足IK親', 1, 0x003F, f'{s}足ＩＫ先', None, None,
             (ankle, 40, 2.0, [(knee, KNEE_LIMIT), (thigh, None)])),
            ('NEW', f'{s}足ＩＫ先', '', ik_shin, f'{s}足ＩＫ', 1, 0x0013, None, None, None, None),
            ('NEW', f'{s}つま先ＩＫ', '', p_toe, f'{s}足ＩＫ', 1, 0x003F, f'{s}つま先ＩＫ先', None, None,
             (toe, 3, 4.0, [(ankle, None)])),
            ('NEW', f'{s}つま先ＩＫ先', '', toe_ik_tip, f'{s}つま先ＩＫ', 1, 0x0013, None, None, None, None),
            ('NEW', f'{s}ひざ補助', '', p_knee, td, 1, 0x0102, (0, 0, 0), kd, 0.5, None),
            ('NEW', f'{s}尻補助腰C', '', p_thigh, hip, 0, 0x0103, None, waist, -0.5, None),
            ('NEW', f'{s}尻補助', '', p_thigh, f'{s}尻補助腰C', 2, 0x0102, (0, 0, 0), td, 0.5, None),
        ])

        # 改造骨
        mods[thigh] = ('MOD', thigh, f'{s}足', f'Thigh_{e}', p_thigh, hip, 0, 0x001B,
                       knee, None, None, None)
        mods[knee] = ('MOD', knee, f'{s}ひざ', f'Knee_{e}', p_knee, thigh, 0, 0x001B,
                      ankle, None, None, None)
        mods[ankle] = ('MOD', ankle, f'{s}足首', f'Ankle_{e}', p_ankle, knee, 0, 0x001B,
                       toe, None, None, None)
        mods[toe] = ('MOD', toe, f'{s}つま先', f'Toe_{e}', p_toe, ankle, 1, 0x0013,
                     None, None, None, None)
        mods[ao] = ('MOD', ao, f'{s}足首D', 'D', p_ankle, kd, 1, 0x011B,
                    None, ankle, 1.0, None)
        mods[tof] = ('MOD', tof, f'{s}足先EX', f'Toe_offset_{e}',
                     lerp3(p_ankle, p_toe, TOE_EX_T), ao, 1, 0x001A,
                     (0.0, 0.0, 0.0), None, None, None)

        # 权重迁移
        w_migrate[thigh] = td
        w_migrate[knee] = kd

    # ---- 2b. 躯干层级重构：腰成为下半身的父骨（对齐 1025_2/1033_2） ----
    #   1025 错误链: 全ての親 -> 下半身 -> 上半身Ctrl -> 腰
    #   正确链     : 全ての親 -> 腰 -> [下半身 -> 腿] 和 [躯干]
    mods[waist] = ('MOD', waist, '腰', 'Waist', info_waist['xyz'], root,
                   info_waist['layer'], info_waist['flag'], info_waist['tail'], None, None, None)
    mods[hip] = ('MOD', hip, '下半身', 'Hip', info_hip['xyz'], waist,
                 info_hip['layer'], info_hip['flag'], info_hip['tail'], None, None, None)

    # ---- 打印：每块骨骼的新建/改动 ----
    old_name = {i: r.bone_items[i][0].rstrip() for i in range(r.bone_count)}
    print('  [新建] 下半身骨骼:')
    for before in sorted(inserts):
        for item in inserts[before]:
            nm, ng, xyz, pref, layer, flag, tail, aref, ratio, ik = item[1:]
            extra = ''
            if aref is not None:
                extra += f' 回転付与={old_name.get(aref, aref)}率{ratio}'
            if ik is not None:
                extra += f' IK(target={old_name.get(ik[0], ik[0])},loop={ik[1]},rad={ik[2]})'
            print(f'    {nm:<12} 位置=({xyz[0]:.2f},{xyz[1]:.2f},{xyz[2]:.2f}) '
                  f'parent={pref} flag=0x{flag:04X} layer={layer}{extra}')
    print('  [改造] 主骨/足首骨:')
    for old in sorted(mods):
        item = mods[old]
        nm = item[2]
        pref = item[5]
        layer = item[6]
        flag = item[7]
        aref = item[9]
        ratio = item[10]
        extra = ''
        if aref is not None:
            extra += f' 回転付与={old_name.get(aref, aref)}率{ratio}'
        print(f'    {nm:<12} parent->{pref} flag=0x{flag:04X} layer={layer}{extra}')
    print('  [权重迁移]:')
    for old, new in w_migrate.items():
        wcnt = sum(1 for _p, bb in r.weight_points if bb == old)
        print(f'    {old_name[old]}({wcnt}顶点) -> {new}')

    # ---- 3. new_item_plan + old_to_new + 新骨索引 ----
    new_item_plan = []
    old_to_new = {}
    new_idx = {}

    def _emit(old):
        old_to_new[old] = len(new_item_plan)
        new_item_plan.append(mods[old] if old in mods else ('OLD', old))

    # ★ 躯干层级重构修正：腰(waist) 已改为 下半身(hip) 的父骨，
    #   必须把 waist 排到 root 之后、hip 之前——否则 TransformView 按
    #   (layer,Index) 求值时，腰 会排在 下半身/腰キャンセル(回転付与=腰) 之后，
    #   违反"親骨/追加親必须先于依赖骨求值"，导致腰旋转时腿部拉伸变形。
    waist_early = (waist != root)
    for old in range(r.bone_count):
        if old == root:
            _emit(root)
            if waist_early:
                _emit(waist)
            continue
        if old == waist and waist_early:
            continue
        if old in inserts:
            for item in inserts[old]:
                new_idx[item[1]] = len(new_item_plan)   # item[1] = name
                new_item_plan.append(item)
        old_to_new[old] = len(new_item_plan)
        new_item_plan.append(mods[old] if old in mods else ('OLD', old))

    def resolve(ref):
        if isinstance(ref, str):
            return new_idx[ref]
        return old_to_new[ref]

    # ---- 4. 重建骨骼区 ----
    out_bone = bytearray()
    out_bone.extend(len(new_item_plan).to_bytes(4, 'little'))
    old_new_off = {}
    mod_set = set(mods.keys())
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
            if item[0] == 'NEW':
                nm, ng, xyz, pref, layer, flag, tail, aref, ratio, ik = item[1:]
            else:  # MOD
                _, oidx, nm, ng, xyz, pref, layer, flag, tail, aref, ratio, ik = item
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
    bone_ba = bytearray(out_bone)

    # ---- 5. 骨骼区内部引用重映射（未改造旧骨） ----
    none_idx = (1 << (8 * r.bidx)) - 1
    # ★ 足首EX(Ankle_offset) 下的弹簧骨 parent 重定向到足首主骨
    #   ⚠ 2026-08-26 回退说明：曾尝试"鞋带挂左足首D(D骨链)"（_is_ribbon 排除重定向）
    #     以解决"旋转左足D 时鞋带跟随"，但 MMD 追加親只传旋转不传位移——
    #     transform 模式拖主骨链腿(平移)时 D骨链不随主骨 → 鞋带错位留原地。
    #     回退到参考模型结构：鞋带挂主骨链 左足首（transform 拖腿正常跟随）。
    parent_redirect = {}
    for s, e in SIDES:
        ao = b[(s, 'ankle_off')]
        ankle = b[(s, 'ankle')]
        if ao is not None and ankle is not None:
            parent_redirect[ao] = ankle
    for old in range(r.bone_count):
        if old in mod_set:
            continue
        off = old_new_off.get(old)
        if off is None:
            continue
        for rel, oidx in r.bone_refs[old]:
            if oidx == none_idx:
                continue
            if rel == 12 and oidx in parent_redirect:   # parent 字段指向足首EX -> 足首主骨
                oidx = parent_redirect[oidx]
            new = old_to_new.get(oidx)
            if new is None:
                new = none_idx
            _write_idx(bone_ba, off + rel, new, r.bidx)
    new_bone_bytes = bytes(bone_ba)

    # ---- 6. 其余区引用重映射（顶点权重做 w_migrate + 腰权重迁移到下半身） ----
    # ★ 腰(waist) 权重迁移到下半身(hip)：对齐 1025_2（腰=纯挂点 0 权重）
    #   否则千鸟等 VMD 转身时 腰(0°)/上半身(58.7°) 线性混合导致腹部麻花扭曲。
    w_redirect = {}
    if waist is not None and hip is not None:
        w_redirect[waist] = hip

    def remap(points, section_start, bytes_, migrate=None, redirect=None):
        migrate = migrate or {}
        redirect = redirect or {}
        ba = bytearray(bytes_)
        for abs_off, old in points:
            if old == none_idx:
                continue
            old2 = redirect.get(old, old)   # 腰 -> 下半身
            if old2 in migrate:
                new = new_idx[migrate[old2]]
            else:
                new = old_to_new.get(old2)
                if new is None:
                    new = none_idx
            _write_idx(ba, abs_off - section_start, new, r.bidx)
        return bytes(ba)

    vertex_out = remap(r.weight_points, r.vertex_start, r.vertex_bytes, migrate=w_migrate, redirect=w_redirect)
    morph_out = remap(r.morph_bone_points, r.morph_start, r.morph_bytes, redirect=w_redirect)
    frame_out = remap(r.frame_bone_points, r.frame_start, r.frame_bytes, redirect=w_redirect)
    rigid_out = remap(r.rigid_bone_points, r.rigid_start, r.rigid_bytes, redirect=w_redirect)

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
    expect = r.bone_count + 22
    assert v.bone_count == expect, f'验证失败: 骨骼数 {expect} != {v.bone_count}'
    vnames = [it[0].rstrip() for it in v.bone_items]
    assert '左足首D' in vnames and '左足首EX' not in vnames, '足首D 未改造'
    assert '腰キャンセル左' in vnames and '左足ＩＫ' in vnames, '下半身骨未补全'
    print(f'  [OK] 写入 {os.path.basename(dst)}: 骨骼 {r.bone_count} -> {v.bone_count} 剩余字节=0')
    return True




