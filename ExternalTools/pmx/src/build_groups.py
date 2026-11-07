# -*- coding: utf-8 -*-
"""
建立表情组脚本（仿 merge_morphs / rename_morphs 架构）—— [8]
==============================================================
（注：原 [7] 序号已顺延——新 [7] 为「双目骨生成」build_both_eyes.py）
术语表：groups_data.py（从 1033 提取的 14 个组表情结构：
        组名 -> [(子控件日文名, 倍率), ...]）

处理逻辑（对新模型）：
1. 检测组(type=0)表情：新模型中若存在任何组表达式，则**拒绝处理**
   （防止已有组被重复建立/覆盖）
2. 无组表情时，逐组按术语表：
   - 在新模型的 Morph 中找到所有匹配的子控件（按日文名 name 匹配）
   - 由这些子控件构建 组(Group, type=0) 表情
   - 每个组引用其子控件 Morph 的序号，倍率取术语表值
3. 新建的组表情 name=组名(日文)，name_g=组名(日文)（1033 同款：name_g 为空，
   但为兼容查看器这里 name_g 用同值，可由后续 [4] 覆盖）

安全规则：已存在组表情 → 拒绝处理。

实现策略：
- RenameReader(UTF-16LE优先) 解析 + PMXWriter 重建 Morph 区，其余区原始字节保留
- 备分：自动创建 [7]备份_时间戳/
"""
import os
import sys
import glob
import shutil
import datetime
import re
import math
from merge_morphs import PMXWriter
from rename_morphs import RenameReader
from sort_bones import PMXReader, decode_text

try:
    from groups_data import GROUPS as _BUILTIN_GROUPS
except Exception:
    from groups_data import GROUPS as _BUILTIN_GROUPS


# ================= 目表情修正 =================
# 结论(16.18)：目上下左右表情左右不对称（右目位移≈左目×2）。
# 修正规则：对比左右目同一方向表情的位移模长，取**两目中位移偏小的值**统一
# （偏大者改为偏小者的镜像，保持各侧方向）。
EYE_DIRS = ('上', '下', '内', '外')


def eye_morph_magnitude(offs):
    """表情的平均位移向量模长（type=1 顶点位移）"""
    vals = [o['value'] for o in offs if len(o['value']) >= 3]
    if not vals:
        return 0.0
    ax = sum(v[0] for v in vals) / len(vals)
    ay = sum(v[1] for v in vals) / len(vals)
    az = sum(v[2] for v in vals) / len(vals)
    return math.sqrt(ax * ax + ay * ay + az * az)


def fix_eye_morph_range(src, take='small'):
    """目表情修正：对比左右目上下左右位移，统一到偏小或偏大值（take='small'/'large'）"""
    print(f"\n{'='*60}")
    print(f"目表情修正[{take}]: {os.path.basename(src)}")
    reader = RenameReader(src).parse()
    if reader.remaining != 0:
        print(f"  [ERROR] 解析未对齐(剩余{reader.remaining}字节)，已跳过")
        return False
    morphs = {mo['name']: mo for mo in reader.morphs}
    changed = 0
    for d in EYE_DIRS:
        L = morphs.get('左目' + d)
        R = morphs.get('右目' + d)
        if not L or not R or L['type'] != 1 or R['type'] != 1:
            print(f"  [SKIP] {d}: 缺 左目/右目 表情或非顶点型(type={L['type'] if L else None}/{R['type'] if R else None})")
            continue
        mL = eye_morph_magnitude(L['offsets'])
        mR = eye_morph_magnitude(R['offsets'])
        if len(L['offsets']) != len(R['offsets']):
            print(f"  [SKIP] {d}: 左右目 offset 数不同 ({len(L['offsets'])} vs {len(R['offsets'])})")
            continue
        small, big, small_nm, big_nm = L, R, '左目' + d, '右目' + d
        if mL <= mR:
            small, big, small_nm, big_nm = L, R, '左目' + d, '右目' + d
        else:
            small, big, small_nm, big_nm = R, L, '右目' + d, '左目' + d
        if take == 'large':
            # 取偏大：小者改为大者的镜像（统一到大值）
            for so, bo in zip(small['offsets'], big['offsets']):
                bv = bo['value']
                so['value'] = [-bv[0], bv[1], bv[2]]
            print(f"  {d}: 左目 {mL:.4f} vs 右目 {mR:.4f} → 取偏大({big_nm} {max(mL, mR):.4f}), 修正 {small_nm}")
        else:
            # 取偏小：大者改为小者的镜像（统一到小值）
            for bo, so in zip(big['offsets'], small['offsets']):
                sv = so['value']
                bo['value'] = [-sv[0], sv[1], sv[2]]
            print(f"  {d}: 左目 {mL:.4f} vs 右目 {mR:.4f} → 取偏小({small_nm} {min(mL, mR):.4f}), 修正 {big_nm}")
        changed += 1
    if changed == 0:
        print("  无目上下左右表情(或已对称)，跳过")
        return False
    writer = PMXWriter(reader)
    data = writer.build()
    with open(src, 'wb') as f:
        f.write(data)
    v = RenameReader(src).parse()
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    print(f"  [OK] 目表情修正 {changed}/4 个方向, remaining=0")
    return True


# ================= 隐藏表情骨 =================
# 表情骨识别：名字以这些模式开头（英文为主；兼日文）
FACE_RE = re.compile(
    r'^(Eye|Eyebrow|Mouth|Tooth|Tongue|Eyelash|Cheek|Nose|Chin|M_Line|'
    r'Head_attach|Head_height|Head_shade|'
    r'両目|左目|右目|眉|口|歯|舌|頬|鼻|あご)'
)


def is_face_bone(name, name_g):
    """name 或 name_g 命中表情骨模式"""
    for nm in (name, name_g):
        if nm and FACE_RE.match(nm):
            return True
    return False


def hide_face_bones(src):
    """隐藏表情骨：清除骨骼 flag 的 0x0008(表示)位（骨骼保留，权重/动画不受影响）"""
    print(f"\n{'='*60}")
    print(f"隐藏表情骨: {os.path.basename(src)}")
    print(f"{'='*60}")

    r = PMXReader(src).parse()
    if r.remaining != 0:
        print(f"  [ERROR] 解析未对齐(剩余{r.remaining}字节)，已跳过")
        return False

    face_idx = [i for i, it in enumerate(r.bone_items)
                if is_face_bone(it[0].rstrip(), it[1].rstrip())]
    print(f"  表情骨: {len(face_idx)}/{r.bone_count} 个")
    if not face_idx:
        print("  无表情骨，跳过")
        return False

    flag_off = 12 + r.bidx + 4   # item_raw 内 flag 偏移：位置12 + 父 + layer4

    # 重建骨骼区：清除表情骨 flag bit3(0x0008 表示)
    out_bone = bytearray()
    out_bone.extend(r.bone_count.to_bytes(4, 'little'))
    changed = 0
    already_hidden = 0
    for i, it in enumerate(r.bone_items):
        name, name_g, name_raw, name_g_raw, item_raw = it
        out_bone.extend(len(name_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_raw)
        out_bone.extend(len(name_g_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_g_raw)
        if i in face_idx:
            flag = int.from_bytes(item_raw[flag_off:flag_off + 2], 'little')
            if flag & 0x0008:
                new_flag = flag & ~0x0008
                ir = bytearray(item_raw)
                ir[flag_off:flag_off + 2] = new_flag.to_bytes(2, 'little')
                out_bone.extend(ir)
                changed += 1
                continue
            else:
                already_hidden += 1
        out_bone.extend(item_raw)
    new_bone_bytes = bytes(out_bone)
    print(f"  清除0x0008(表示)位: {changed} 个, 原本已隐藏: {already_hidden} 个")

    if changed == 0:
        print("  表情骨均已隐藏，无需修改")
        return False

    # 组装输出（仅骨骼区变化）
    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little'))
    out.extend(r.vertex_bytes)
    out.extend(r.face_count.to_bytes(4, 'little'))
    out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little'))
    out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little'))
    out.extend(r.mat_bytes)
    out.extend(new_bone_bytes)
    out.extend(r.morph_count.to_bytes(4, 'little'))
    out.extend(r.morph_bytes)
    out.extend(r.frame_count.to_bytes(4, 'little'))
    out.extend(r.frame_bytes)
    out.extend(r.rigid_count.to_bytes(4, 'little'))
    out.extend(r.rigid_bytes)
    out.extend(r.joint_count.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)

    with open(src, 'wb') as f:
        f.write(bytes(out))

    # 验证
    v = PMXReader(src).parse()
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    assert v.bone_count == r.bone_count, "验证失败：骨骼数变化"
    vflag_off = 12 + v.bidx + 4
    hidden = 0
    for i, it in enumerate(v.bone_items):
        if is_face_bone(it[0].rstrip(), it[1].rstrip()):
            fg = int.from_bytes(it[4][vflag_off:vflag_off + 2], 'little')
            assert not (fg & 0x0008), f'表情骨 {it[0]} 未被隐藏'
            hidden += 1
    print(f"  [OK] 隐藏表情骨成功并验证通过 (剩余字节=0, 已隐藏 {hidden} 个表情骨)")
    return True


def find_group_morphs(reader):
    """返回所有 type==0 (组) 的表情"""
    return [mo for mo in reader.morphs if mo['type'] == 0]


def build_groups_pmx(src, groups):
    """在新模型中建立组表情；若已存在任何组表情则拒绝"""
    print(f"\n{'='*60}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'='*60}")

    reader = RenameReader(src).parse()
    print(f"  顶点={reader.vertex_count} 面={reader.face_count} 骨骼={reader.bone_count}")
    print(f"  Morph总数={reader.morph_count} 显示帧={reader.frame_count} 刚体={reader.rigid_count} 约束={reader.joint_count}")
    print(f"  剩余字节={reader.remaining} (应为0)")

    if reader.remaining != 0:
        print(f"  [ERROR] 文件解析未对齐(剩余{reader.remaining}字节)，已跳过")
        return False

    # 1. 检测已有组表情
    existing = find_group_morphs(reader)
    print(f"  已存在的组(type=0)表情: {len(existing)} 个")
    if existing:
        print("  [ERROR] 模型已含组表情，拒绝处理（防止重复建立）")
        for mo in existing[:10]:
            print(f"    {mo['name']!r}")
        return False

    # 2. 建立 名字->Morph索引 映射（用于组引用）
    name_to_idx = {}
    for i, mo in enumerate(reader.morphs):
        key = mo['name'] or mo['name_g'] or ''
        if key not in name_to_idx:
            name_to_idx[key] = i

    # 3. 逐组建立
    new_groups = []
    created = 0
    for gname, refs in groups.items():
        # 校验该组所有子控件是否存在于模型
        missing = [rn for rn, _ in refs if rn not in name_to_idx]
        if missing:
            print(f"  [WARN] {gname}: 缺少子控件 {missing}，跳过该组")
            continue
        # 构建组 Morph
        mo = {
            'name': gname,
            'name_g': gname,
            'panel': 0,
            'type': 0,
            'offset_count': len(refs),
            'offsets': [],
        }
        for rn, rate in refs:
            mo['offsets'].append({
                'index': name_to_idx[rn],
                'value': [rate],
            })
        new_groups.append(mo)
        created += 1
        print(f"    建立组 {gname} -> 引用 {[rn for rn, _ in refs]}")

    if created == 0:
        print("  没有可建立的组(全部缺子控件或空术语表)，跳过")
        return False

    # 4. 把新组追加到 Morph 区末尾（组通常引用前方控件，追加安全）
    reader.morphs.extend(new_groups)

    # 写回
    writer = PMXWriter(reader)
    data = writer.build()
    with open(src, 'wb') as f:
        f.write(data)

    # 5. 验证
    v = RenameReader(src).parse()
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    vg = find_group_morphs(v)
    assert len(vg) == created, f"验证失败：组数 {len(vg)} != {created}"
    # 每个组引用数应与术语表一致
    for g in vg:
        assert g['type'] == 0, f'验证失败: {g["name"]} 不是组类型'
        # 引用索引应在范围内
        for off in g['offsets']:
            assert 0 <= off['index'] < v.morph_count, f'引用越界'
    print(f"  [OK] 建立表情组成功并验证通过 (新建 {created} 个组, 剩余字节=0)")
    return True




