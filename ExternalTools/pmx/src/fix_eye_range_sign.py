# -*- coding: utf-8 -*-
"""
眼球控件修正+翻倍脚本（仿 merge_morphs 架构）—— [6] 新版逻辑
==============================================================
当前 1025 状态（[5]清理后，4个控件）：
  - 左目内(Eye_20_L XRange) 正常；左目上(Eye_21_L YRange) 正常
  - 右目内(Eye_20_R XRange) 实际内容为 右目外 → 需改名
  - 右目上(Eye_21_R YRange) 实际内容为 右目下 → 需改名

处理步骤：
1. 匹配形如 Eye_<数字>_[LR](XRange|YRange)[M_Face] 的 **4个** 眼球控件（≠4拒绝）
2. 修正右目名称（内容不变）：
     Eye_20_R(XRange): 右目内 -> 右目外
     Eye_21_R(YRange): 右目上 -> 右目下
3. 翻倍：每个控件 **拷贝 1 份**（name_g 相同，name 改为对立名，偏移按反转规则取反）
     - 左目内  -> 拷出 左目外(反转)
     - 右目外  -> 拷出 右目内(反转)
     - 左目上  -> 拷出 左目下(反转)
     - 右目下  -> 拷出 右目上(反转)
   → 最终 8 条（4 组 × 内/外、上/下 各2条），与 1033 结构一致
   （零位移(0,0,0)保持 0，非零位移取反）

安全规则：匹配数量必须恰好 4 个，否则拒绝处理。
实现策略：RenameReader(UTF-16LE优先) 解析 + PMXWriter 重建 Morph 区，其余区原始字节保留。
"""
import os
import re
import sys
import glob
import shutil
import copy
import datetime
from merge_morphs import PMXWriter
from rename_morphs import RenameReader

# 匹配 Eye_20_R(XRange)[M_Face] / Eye_21_L(YRange)[M_Face]
EYE_RANGE_RE = re.compile(r'^Eye_(\d+)_([LR])\((XRange|YRange)\)\[M_Face\]$')

# name(日文) 对立名映射（用于拷贝新控件改名）
OPPOSITE_NAME = {
    '左目内': '左目外',
    '右目内': '右目外',
    '右目外': '右目内',
    '左目外': '左目内',
    '左目上': '左目下',
    '右目上': '右目下',
    '右目下': '右目上',
    '左目下': '左目上',
}

# 右目名称修正（内容不变）
FIX_RIGHT_NAME = {
    '右目内': '右目外',
    '右目上': '右目下',
}


def find_eye_range_morphs(reader):
    """返回匹配的 Morph 列表"""
    hits = []
    for mo in reader.morphs:
        if EYE_RANGE_RE.match(mo['name_g'] or ''):
            hits.append(mo)
    return hits


def flip_offset(v):
    """非零位移取反，零位移保持0"""
    return [-x for x in v] if tuple(v) != (0.0, 0.0, 0.0) else list(v)


def process_pmx(src):
    """修正+翻倍单个PMX文件的眼球控件，返回True表示处理成功"""
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

    targets = find_eye_range_morphs(reader)
    print(f"  匹配到眼球控件: {len(targets)} 个 (期望 4)")
    if len(targets) != 4:
        print("  [ERROR] 匹配数量不是4，拒绝处理！")
        for mo in targets:
            print(f"    匹配: {mo['name_g']!r}")
        return False

    # ---- 步骤2: 修正右目名称（内容不变） ----
    print("\n  [步骤1] 修正右目名称(内容不变):")
    renamed = 0
    for mo in targets:
        jp = mo['name']
        new = FIX_RIGHT_NAME.get(jp)
        if new:
            print(f"    {mo['name_g']!r}: {jp} -> {new}")
            mo['name'] = new
            renamed += 1
    if renamed == 0:
        print("    无需修正(R侧名称已正确)")

    # ---- 步骤3: 翻倍（拷贝+改名+反转） ----
    print("\n  [步骤2] 翻倍：拷贝出对立面控件(反转偏移):")
    new_morphs = []
    for mo in targets:
        jp = mo['name']
        opp = OPPOSITE_NAME.get(jp)
        if not opp:
            continue
        # 深拷贝 Morph
        clone = copy.deepcopy(mo)
        clone['name'] = opp
        clone['name_g'] = mo['name_g']   # name_g 保持原名(如 1033 中的 右目内/右目外 共用)
        # 反转偏移（非零取反，零保持）
        for o in clone['offsets']:
            o['value'] = flip_offset(o['value'])
        new_morphs.append(clone)
        print(f"    {mo['name_g']!r}: {jp} -> 拷贝为 {opp} (反转)")

    if not new_morphs:
        print("  [ERROR] 无对立名映射，无法翻倍")
        return False

    # 在目标 4 个之后插入新控件（保持组内相邻）
    insert_after_idx = max(reader.morphs.index(mo) for mo in targets)
    reader.morphs[insert_after_idx+1:insert_after_idx+1] = new_morphs

    new_total = reader.morph_count + len(new_morphs)
    print(f"  Morph数: {reader.morph_count} -> {new_total} (新增{len(new_morphs)}个)")

    # ---- 写回 ----
    writer = PMXWriter(reader)
    data = writer.build()
    with open(src, 'wb') as f:
        f.write(data)

    # ---- 验证 ----
    v = RenameReader(src).parse()
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    assert v.morph_count == new_total, f"验证失败：Morph数 {v.morph_count} != {new_total}"
    vt = find_eye_range_morphs(v)
    assert len(vt) == 8, f"验证失败：眼球控件应8个, 实际{len(vt)}"
    # 每个 name_g 应有 2 条（一对 内/外 或 上/下）
    from collections import Counter
    gcnt = Counter(m['name_g'] for m in vt)
    for g, c in gcnt.items():
        assert c == 2, f"验证失败：{g} 条数={c} (应2)"
    print(f"  [OK] 修正+翻倍成功并验证通过 "
          f"(4->8个控件, 每name_g 2条, 剩余字节=0)")
    return True




