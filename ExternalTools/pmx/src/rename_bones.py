# -*- coding: utf-8 -*-
"""
[9] 骨骼重命名 —— 仿 merge_morphs 架构
=========================================
从 1033_2 抽取术语表（bone_glossary_data.py）：
  将目标模型的骨骼 name 由英文改为日语，name_g(英文) 保持不变。

处理规则（按优先级）：
  1. EXACT 整词表命中（1033_2 name_g->name，覆盖核心骨/肢体/手指/耳朵）
  2. Sp_ 弹簧骨：仅当中间片段在 SP_MID 中"精确匹配"才翻译（Hair0->头发0, Tail0->尻尾, ...），
     前缀(Sp_He_/Sp_Hi_/...)与后缀(_B_00/_C_00...)保留；无精确匹配（CSkirt0 裙子、
     Tie0/Necklace0 项链等）保持不翻译
  3. SPECIAL 表（1025 特有骨，参照 1033_2 风格：Toe_offset_L=左足先EX, Tail_Ctrl=尻尾親，
     左右耳亲骨 Sp_He_Ear0_L/R_00=左/右耳親）
  4. 其余（表情骨等，1033_2 无日文参照）保持英文 name 不变

实现：
  - 仅重建骨骼区（新 name + 原 name_g 原始字节 + 原 item_raw），索引/引用不变
  - 其余区全部原始字节保留
  - main() 与 merge_morphs 一致：备份/批处理/验证/回车退出
"""
import struct
import os
import sys
import glob
import shutil
import datetime
import re

SRC_DIR = os.path.dirname(os.path.abspath(__file__))
if SRC_DIR not in sys.path:
    sys.path.insert(0, SRC_DIR)
try:
    from bone_glossary_data import EXACT, SP_MID, SPECIAL, HAIR_STYLE_1013
except Exception as e:  # exe 打包时数据文件可能未随附
    EXACT, SP_MID, SPECIAL, HAIR_STYLE_1013 = {}, {}, {}, {}
    print(f'[WARN] 术语表加载失败: {e}')


def decode_text(raw):
    if len(raw) == 0:
        return ''
    if len(raw) % 2 == 0:
        try:
            s = raw.decode('utf-16-le')
            if not any(0xD800 <= ord(c) <= 0xDFFF for c in s):
                return s
        except Exception:
            pass
    try:
        s = raw.decode('utf-8')
        if '\x00' in s and len(raw) % 2 == 0:
            return raw.decode('utf-16-le')
        return s
    except Exception:
        pass
    if len(raw) % 2 == 0:
        try:
            return raw.decode('utf-16-le')
        except Exception:
            pass
    return repr(raw)


# ================= 翻译逻辑 =================
HAIR_RE = re.compile(r'^Sp_He_Hair(\d+)_([A-Za-z]+)_(\d+)$')


def translate_hair_1013(name, name_g):
    """按 1013 风格翻译头发骨：Sp_He_Hair4_C_00 -> 後髪 / Sp_He_Hair2_L_01 -> 左触覚1
    规则: L→左 R→右 C→留空; 部位 前髪/アホ毛/触覚/横髪/後髪; _00(0号)省略编号。"""
    for nm in (name, name_g):
        m = HAIR_RE.match(nm)
        if not m:
            continue
        n, pos, num = m.group(1), m.group(2), m.group(3)
        part = HAIR_STYLE_1013.get('HAIR_PART', {}).get(f'Hair{n}')
        pos_cn = HAIR_STYLE_1013.get('HAIR_POS', {}).get(pos, '')
        num_cn = HAIR_STYLE_1013.get('HAIR_NUM', {}).get(num)
        if not (part and num_cn is not None):
            continue
        return f'{pos_cn}{part}{num_cn}'
    return None


BUST0_RE = re.compile(r'^Sp_Ch_Bust0_([LR])_(\d+)$')
EAR0_RE = re.compile(r'^Sp_He_Ear0_([LR])_(\d+)$')
TAIL0_RE = re.compile(r'^Sp_Hi_Tail0_[A-Za-z]+_(\d+)$')


def translate_bust0(name, name_g):
    """胸口: Sp_Ch_Bust0_L_00 -> 左胸0 / Sp_Ch_Bust0_R_01 -> 右胸1"""
    for nm in (name, name_g):
        m = BUST0_RE.match(nm)
        if m:
            pos_cn = {'L': '左', 'R': '右'}.get(m.group(1), m.group(1))
            num = HAIR_STYLE_1013.get('HAIR_NUM', {}).get(m.group(2), m.group(2))
            return f'{pos_cn}胸{num}'
    return None


def translate_ear0(name, name_g):
    """耳朵: Sp_He_Ear0_L_00 -> 左耳0 / Sp_He_Ear0_R_02 -> 右耳2"""
    for nm in (name, name_g):
        m = EAR0_RE.match(nm)
        if m:
            pos_cn = {'L': '左', 'R': '右'}.get(m.group(1), m.group(1))
            num = HAIR_STYLE_1013.get('HAIR_NUM', {}).get(m.group(2), m.group(2))
            return f'{pos_cn}耳{num}'
    return None


def translate_tail0(name, name_g):
    """尾巴: Sp_Hi_Tail0_B_00 -> 尻尾0（位置忽略，只保留编号）"""
    for nm in (name, name_g):
        m = TAIL0_RE.match(nm)
        if m:
            num = HAIR_STYLE_1013.get('HAIR_NUM', {}).get(m.group(1), m.group(1))
            return f'尻尾{num}'
    return None


def translate_sp(name, name_g=None):
    """Sp_ 弹簧骨：取消混杂命名——去掉 Sp_ 前缀，只译中间片段（保留 _XX 位置/序号后缀）
    如 Sp_Hi_Tail0_B_00 -> 尻尾_B_00。对已日文化的 name，用 name_g(英文) 匹配。
    ★ 仅"精确匹配"：中间片段整体命中 SP_MID 才翻译；无精确匹配（如 CSkirt0/MSkirt0 裙子等）
    则保持不翻译（不做子串模糊替换，避免误译）。"""
    for nm in (name, name_g or ''):
        if not nm.startswith('Sp_'):
            continue
        m = re.match(r'^(Sp_[A-Za-z]+_)(.+?)((?:_[A-Za-z0-9]+_[A-Za-z0-9]+)?)$', nm)
        if not m:
            continue
        prefix, body, tail = m.group(1), m.group(2), m.group(3) or ''
        new_body = SP_MID.get(body)   # 仅整词精确匹配
        if new_body is None or new_body == body:
            continue                  # 无精确匹配 → 保持不翻译
        return new_body + tail      # 取消混杂：去掉 Sp_ 前缀
    return name


def translate_name(name, name_g=None):
    """按优先级翻译骨骼 name（name_g 不变）：
    EXACT 整词 → SPECIAL 特有骨 → 头发/胸/耳/尾(用 name_g 匹配) → SP_MID 弹簧片段(仅精确匹配，无则不译) → 保持
    注意：EXACT/SPECIAL 的 key 是英文 name_g；已纯命名的 name 也通过 name_g 重新翻译/更新编号。"""
    key = name
    if name not in EXACT and name not in SPECIAL and name_g:
        key = name_g
    if key in EXACT:
        return EXACT[key]
    if key in SPECIAL:
        return SPECIAL[key]
    # 头发/胸口/耳朵/尾巴：用 name_g(英文) 匹配，name 是否 Sp_ 前缀均可
    h = translate_hair_1013(name, name_g or name)
    if h:
        return h
    b = translate_bust0(name, name_g or name)
    if b:
        return b
    e = translate_ear0(name, name_g or name)
    if e:
        return e
    t = translate_tail0(name, name_g or name)
    if t:
        return t
    if name.startswith('Sp_'):
        return translate_sp(name, name_g)
    return name


# ================= PMXReader（复用 sort_bones 的分段解析） =================
from sort_bones import PMXReader  # noqa: E402


def rename_bones_file(src):
    """处理单个PMX文件：骨骼 name 英->日，name_g 保持英文"""
    print(f"\n{'=' * 70}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'=' * 70}")

    r = PMXReader(src).parse()
    print(f"  顶点={r.vertex_count} 面={r.face_count} 纹理={r.texture_count} "
          f"材质={r.material_count} 骨骼={r.bone_count} Morph={r.morph_count} "
          f"帧={r.frame_count} 刚体={r.rigid_count} 约束={r.joint_count}")
    print(f"  剩余字节={r.remaining} (应为0)")
    if r.remaining != 0:
        print(f"  [ERROR] 解析未对齐(剩余{r.remaining}字节)，已跳过")
        return False

    # 1. 翻译每根骨
    renames = []   # (旧name, 新name)
    for it in r.bone_items:
        name, name_g, name_raw, name_g_raw, item_raw = it
        old = name.rstrip()
        new = translate_name(old, name_g.rstrip())
        if new != old:
            renames.append((old, new))

    print(f"  将重命名: {len(renames)}/{r.bone_count} 根骨")
    if not renames:
        print("  无骨骼需要重命名，跳过")
        return False

    # 2. 控制台输出明细（前60）
    print(f"\n  --- 重命名明细 (前60) ---")
    for i, (old, new) in enumerate(renames[:60]):
        print(f"    {old:<40} -> {new}")
    if len(renames) > 60:
        print(f"    ... 共{len(renames)}条")

    # 3. 重建骨骼区（新 name + 原 name_g 原始字节 + 原 item_raw）
    out_bone = bytearray()
    out_bone.extend(r.bone_count.to_bytes(4, 'little'))
    for it in r.bone_items:
        name, name_g, name_raw, name_g_raw, item_raw = it
        new_name = translate_name(name.rstrip(), name_g.rstrip())
        nb = new_name.encode('utf-16-le')
        out_bone.extend(len(nb).to_bytes(4, 'little', signed=True))
        out_bone.extend(nb)
        out_bone.extend(len(name_g_raw).to_bytes(4, 'little', signed=True))
        out_bone.extend(name_g_raw)   # name_g 原始字节保持不变
        out_bone.extend(item_raw)
    new_bone_bytes = bytes(out_bone)

    # 4. 组装输出（仅骨骼区变化，其余区原样）
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
    data = bytes(out)

    # 5. 写回 + 验证
    with open(src, 'wb') as f:
        f.write(data)
    v = PMXReader(src).parse()
    assert v.remaining == 0, f"验证失败：剩余字节={v.remaining}"
    assert v.bone_count == r.bone_count, "验证失败：骨骼数变化"
    # name_g 应保持英文不变（与 1033_2 的 name_g 一致）
    new_names = [it[0].rstrip() for it in v.bone_items]
    new_name_gs = [it[1].rstrip() for it in v.bone_items]
    jp_cnt = sum(1 for n in new_names if not n.isascii())
    print(f"  [OK] 骨骼重命名成功并验证通过 (剩余字节=0, 改名{len(renames)}骨, "
          f"新name日文化{jp_cnt}个, name_g保持英文)")
    return True




# __MORE__
# __MORE__
