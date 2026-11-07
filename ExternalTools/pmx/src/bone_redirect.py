# -*- coding: utf-8 -*-
"""
骨骼「删除 / 改造」的通用引用重定向工具
========================================
解决「删骨 / 改造骨导致引用悬空(None) → 骨骼错位」的通用方案。

核心原则：
  任何「删骨 / 改造骨」操作，都必须显式声明 redirect（被删骨 -> 语义替代骨），
  把指向被删骨的引用（parent / tail / 追加親 / 顶点权重 / Morph / 帧 / 刚体）
  重定向到替代骨，绝不静默置 None。仅当「确实无替代骨」时才置 None。

语义替代规则（本项目归纳，供各脚本参照）：
  1. Handle 尾端骨（名以 _Handle 结尾）：去 _Handle 后缀找主骨；
     主骨是零长度节(_01)时连带删除，其上父骨(_00) 的 tail 重定向到「方向继承骨」（脚尖 EX）。
  2. 废品肩捩 ShoulderRoll（挂大臂下）：父骨 腕(Arm)。
  3. 废品腕捩 ArmRoll（挂前臂下）：父骨 ひじ(Elbow)。
  4. 足首EX Ankle_offset（改造为 D 骨）：足首主骨。
  5. 腰 Waist（重构为纯挂点）：下半身 Hip（顶点权重迁移）。

用法：
  from bone_redirect import resolve_redirect, build_parent_redirect
"""


def resolve_redirect(b, redirect, none_idx=None):
    """沿 redirect 链迭代解析到最终替代骨；带环保护。

    redirect: {旧索引 -> 替代索引}。替代索引本身也可能是被删骨，递归解析到最上层。
    """
    seen = set()
    while b in redirect and b not in seen:
        seen.add(b)
        b = redirect[b]
    return b


def build_parent_redirect(parents, delete_set):
    """通用规则「子骨没了，父骨顶上」：删除 delete_set 中的骨时，
    把指向它的引用重定向到其父骨；父骨本身也被删时继续向上找。

    返回 {被删骨索引 -> 替代父骨索引}（父骨也可能不在 delete_set 里）。
    """
    redirect = {}
    for b in delete_set:
        p = parents[b]
        chain = []
        while p in delete_set and p not in chain:
            chain.append(p)
            p = parents[p]
        redirect[b] = p
    return redirect
