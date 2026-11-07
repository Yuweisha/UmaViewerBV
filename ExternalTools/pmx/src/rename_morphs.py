# -*- coding: utf-8 -*-
"""
表情双语化脚本（仿 merge_morphs 架构）
======================================
参考模型：1033 (Admire Vega) —— 其 Morph 的 name=日文、name_g=英文
目标模型：其他导出的 PMX（当前 name=英文、name_g=英文）
处理：把目标模型 Morph 的 name(本地名) 中匹配术语表的英文，
      替换为日文 name + 保留英文 name_g，形成"日文→英文"双语显示。

术语表来源（优先次序）：
1. 内置术语表 glossary_data.py（1033 预提取，打包进 exe —— 转发无需带参考模型）
2. 若目录存在外部参考模型(1033) → 运行时动态提取并覆盖（保持最新）

实现策略（与 merge_morphs 相同的"原始字节保留"）：
- 除 Morph 区外，顶点/面/纹理/材质/骨骼/显示帧/刚体/约束 全部按原始字节保留
- 只完整解析并重建 Morph 区（仅名称变更，offset 数据原样保留）
"""
import os
import sys
import glob
import shutil
import datetime
from merge_morphs import PMXReader, PMXWriter

# 内置术语表（由 extract_glossary.py 从 1033 预提取，打包进 exe）
try:
    from glossary_data import GLOSSARY_EN2JP as _BUILTIN_GLOSSARY
except Exception:
    _BUILTIN_GLOSSARY = {}


class RenameReader(PMXReader):
    """
    重命名专用读取器：UTF-16LE 优先解码。
    1025/1033 系列模型是"声明UTF-8、实际UTF-16LE"存储，
    且日文假名(あ/い/う/お等)的UTF-16LE编码恰为ASCII可打印字符
    (如 あ=0x3042 -> b'B0')，merge_morphs 的UTF-8优先启发式会误判，
    故本脚本对文本一律按 UTF-16LE 解码。
    """

    def rtext(self):
        n = self.ri(4)
        if n < 0 or self.pos + n > len(self.data):
            raise ValueError('Invalid PMX text length')
        raw = self.rb(n)
        if self.encoding not in (0, 1):
            raise ValueError('Invalid PMX encoding flag')
        return raw.decode('utf-16-le' if self.encoding == 0 else 'utf-8')


def build_glossary(ref_path=None):
    """
    构建 英文名→日文名 映射(name_g -> name)。

    默认使用内置术语表(_BUILTIN_GLOSSARY)，无需参考模型文件；
    若传入 ref_path（兼容测试），则动态从参考模型提取覆盖。
    返回 (reader_or_None, glossary)
    """
    if ref_path is None:
        return None, dict(_BUILTIN_GLOSSARY)
    reader = RenameReader(ref_path).parse()
    glossary = {}
    for mo in reader.morphs:
        en = mo['name_g']
        jp = mo['name']
        if en and jp and en != jp:
            glossary[en] = jp
    return reader, glossary


def bilingualize_pmx_file(src, glossary):
    """将单个PMX文件的 Morph 本地名替换为日文（保留英文全局名），实现双语"""
    print(f"\n{'='*60}")
    print(f"处理文件: {os.path.basename(src)}")
    print(f"{'='*60}")

    # 解析
    reader = RenameReader(src).parse()
    print(f"  顶点={reader.vertex_count} 面={reader.face_count} 骨骼={reader.bone_count}")
    print(f"  Morph总数={reader.morph_count} 显示帧={reader.frame_count} 刚体={reader.rigid_count} 约束={reader.joint_count}")
    print(f"  剩余字节={reader.remaining} (应为0)")

    # 防御：解析未对齐说明文件结构异常，拒绝处理避免损坏
    if reader.remaining != 0:
        print(f"  [ERROR] 文件解析未对齐(剩余{reader.remaining}字节)，已跳过，请先修复模型")
        return False

    # 统计命中的表情
    renamed = []
    for mo in reader.morphs:
        en = mo['name']
        jp = glossary.get(en)
        if jp:
            mo['name'] = jp          # 本地名 → 日文
            mo['name_g'] = en        # 全局名 → 保持英文
            renamed.append((en, jp))

    if not renamed:
        print("  术语表未命中任何表情（该模型表情无对应日文名或已双语化）")
        return False

    # 输出重命名明细
    print(f"  双语化 {len(renamed)} 个表情（name→日文 / name_g→英文）：")
    for en, jp in renamed:
        print(f"    {en}  =>  {jp} / {en}")

    # 写回（覆盖原文件）
    writer = PMXWriter(reader)
    data = writer.build()
    with open(src, 'wb') as f:
        f.write(data)

    # 验证
    v = RenameReader(src).parse()
    assert v.remaining == 0, "验证失败：剩余字节不为0"
    assert v.morph_count == reader.morph_count, "验证失败：Morph数量变化"
    # 验证：所有术语表英文名在 name 中不再残留（已被日文替换）
    en_remain = [mo['name'] for mo in v.morphs if mo['name'] in glossary]
    assert not en_remain, f"验证失败：仍有英文名残留 {en_remain}"
    # 验证：被改的名词 name_g 保持英文、name=日文
    for mo in v.morphs:
        if mo['name'] in glossary.values():
            assert mo['name_g'] in glossary, f"验证失败：{mo['name']} 的 name_g 不是英文"
    print(f"  [OK] 双语化成功并验证通过 (共{len(renamed)}个, 剩余字节=0, Morph数不变)")
    return True




