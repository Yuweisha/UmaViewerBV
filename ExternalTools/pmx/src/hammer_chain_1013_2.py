# -*- coding: utf-8 -*-
"""
给已生成主发刚体的模型程序化还原 1013_2 的「錘悬挂链层」
============================================================================
数据取自 Archieve/Mejiro McQueen目白麦昆决胜服/1013_2_Mejiro McQueen.pmx 实测。

## 一、1013_2 錘链层实测结构
    每束头发在主发骨下方挂一串「錘」配重骨 + 长胶囊錘刚体：
      - 錘骨：parent=对应主发骨，flag=0x13（旋转+操作，不显示），
              位置=主发骨 x/z + 发尾 y（下垂到发尾水平线），name_g 与主发骨共享。
      - 錘刚体：胶囊、nc=0（完全无碰撞）、mass 梯度同主刚体（0.50625/0.225/0.1），
              圆柱高（发根/中/尾）：披肩 4.5/3.4/1.3、及腰 10.15/7.29/6.00（仅后发 Hair4 挂锤链）。
      - 刚性连接：主刚体 ↔ 錘刚体（spring_m=spring_r=100000 焊死）。
      - 錘链补充约束：錘刚体之间（平移 ±50 自由 + 旋转锁死 rot_lo=0.017/rot_hi=0）。

## 二、作用
    分离「碰撞」与「摆动」：主刚体负责碰撞防穿模，无碰撞的錘链承载重力下垂与
    惯性摆动，主刚体通过刚性连接跟随錘链 —— 摆动顺滑、发尾有垂坠感。

用法：python src/hammer_chain_1013_2.py 输入.pmx 输出.pmx
前置：输入须已跑 body_physics（有 Head 刚体）+ hair_rigid_1013_2_real.py（有主发刚体）。
"""
import os
import sys
import struct
import math
import re

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ============ 1013_2 錘链层参数（实测） ============
GROUP = 5
NOCOLL_HAMMER = 0            # 錘刚体完全无碰撞
REP = 0.0
FRIC = 0.0

MASS = (0.50625, 0.225, 0.1)        # 与主刚体一致的梯度
LD = (0.9, 0.95, 0.999)
AD = (1.0, 1.5, 2.0)

# 半径（触觉 Hair2 固定；后发长发见 HAIR4_HAMMER）
RADIUS = {'2': 0.22}

# 触觉 Hair2 錘刚体长胶囊圆柱高（发根/中/尾，1013_2 实测）
HAMMER_LEN = {'2': (4.0, 2.7, 0.7)}

# 后发 Hair4 长发锤链参数（披肩 vs 及腰，按骨骼链折线总长自动判断）
# 披肩 = 1013_2；及腰 = Calstone Light O 金镇之光
HAIR4_HAMMER = {
    'shoulder': {'radius': 0.30, 'len': (4.5, 3.4, 1.3),
                 'mass': (0.50625, 0.225, 0.1), 'ld': (0.9, 0.95, 0.999), 'ad': (1.0, 1.5, 2.0)},
    'waist': {'radius': 0.40, 'len': (10.15, 7.29, 6.00),
              'mass': (1.1391, 0.5063, 0.225, 0.1), 'ld': (0.9, 0.93, 0.969, 0.999), 'ad': (1.0, 1.304, 1.696, 0.999)},
}
# 后发骨骼链折线总长阈值：>= 视为及腰长发（披肩约3.9 / 及腰约6.1）
HAIR4_WAIST_LEN = 5.0

# 錘骨 flag：接続先骨指定(0x0001) + 可旋转(0x0002) + 可操作(0x0010)，不显示（1013_2 实测 0x13）
BONE_FLAG = 0x0013

# 刚性连接（主刚体↔錘刚体）
RIGID_SPRING = 100000.0

# 錘链补充约束：平移自由 ±50，旋转锁死（min=max=0，正常区间）
# ★ 修复：原 1013_2 数据是反向区间 min=0.017 > max=0，导致 MMD 堆损坏 0xc0000374
HAMMER_MOV = 50.0
HAMMER_ROT_LO = 0.0
HAMMER_ROT_HI = 0.0

# 发锤后扬系数：发尾方向额外向后(z)倾斜量，避免极端长发（及腰）发尾贴背/卡背
# 0 = 不后扬（纯发尾方向下垂）；越大越向后扬（微后扬建议 0.2~0.5）
HAMMER_BACK_TILT = 0.4

SHAPE_SPHERE, SHAPE_BOX, SHAPE_CAPSULE = 0, 1, 2
TYP_DYNAMIC, TYP_DYNAMIC_ALIGN = 1, 2

HEAD_G = 'Head'
HAIR_RE = re.compile(r'^Sp_He_Hair([24])_([CLR])_(\d+)$')   # 触觉(Hair2)+后发(Hair4) 挂锤链（前发/呆毛不挂）

PART_CN = {'2': '触觉', '4': '后发'}
POS_CN = {'C': '中', 'L': '左', 'R': '右'}


def encode_text(s, encoding):
    raw = s.encode('utf-16-le' if encoding == 0 else 'utf-8')
    return len(raw).to_bytes(4, 'little', signed=True) + raw


def norm(d):
    l = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    if l < 1e-9:
        return (0.0, 1.0, 0.0), 0.0
    return (d[0] / l, d[1] / l, d[2] / l), l


def bone_pos(r, i):
    return struct.unpack_from('<3f', r.bone_items[i][4], 0)


def find_hair_chains(r):
    """识别触觉(Hair2)/后发(Hair4)骨骼链，排除 name 以 N/n 结尾的控制骨，
    以及 name 以「锤/錘」结尾的物理配重骨（与主发骨共享 name_g）。"""
    chains = {}
    for i, (nm, nmg, *_rest) in enumerate(r.bone_items):
        n = nm.rstrip()
        if n.endswith('N') or n.endswith('n') or n.endswith('锤') or n.endswith('錘'):
            continue
        m = HAIR_RE.match(nmg.rstrip())
        if not m:
            continue
        part, pos, num = m.group(1), m.group(2), int(m.group(3))
        chains.setdefault((part, pos), []).append((num, i))
    out = {}
    for key, lst in chains.items():
        lst.sort()
        out[key] = [i for _, i in lst]
    return out


def classify_hair4(chains, r):
    """判断后发 Hair4 类型。
    返回 'pony'（单马尾，仅中后发 C）/ 'shoulder'（披肩长发）/ 'waist'（及腰长发）。
    仅当有 L 且 R 时判长发；长发按最长一束的骨骼链折线总长区分披肩/及腰。"""
    hair4_keys = [k for k in chains if k[0] == '4']
    if not hair4_keys:
        return None
    has_L = any(k[1] == 'L' for k in hair4_keys)
    has_R = any(k[1] == 'R' for k in hair4_keys)
    if not (has_L and has_R):
        return 'pony'   # 仅有中后发 → 马尾
    longest = 0.0
    for key in hair4_keys:
        pos = [bone_pos(r, i) for i in chains[key]]
        tot = sum(math.sqrt(sum((b[k] - a[k]) ** 2 for k in range(3)))
                  for a, b in zip(pos, pos[1:]))
        longest = max(longest, tot)
    return 'waist' if longest >= HAIR4_WAIST_LEN else 'shoulder'


def make_bone(name, name_g, pos, parent_idx, tail_bone, bidx, encoding):
    """构造錘骨条目：flag=0x13（接続先骨指定+旋转+操作），tail 引用下一段主发骨。"""
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text(name_g, encoding))
    for v in pos:
        out.extend(struct.pack('<f', v))
    out.extend(parent_idx.to_bytes(bidx, 'little'))
    out.extend((0).to_bytes(4, 'little'))           # layer（変形階層）
    out.extend(BONE_FLAG.to_bytes(2, 'little'))     # flag = 0x13
    out.extend(tail_bone.to_bytes(bidx, 'little'))  # tail 骨（接続先）
    return bytes(out)


def make_rigid(name, bone_idx, shape, size, pos, rot, typ, mass, ld, ad, bidx, encoding):
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))
    out.extend(bone_idx.to_bytes(bidx, 'little'))
    out.extend(bytes([GROUP]))
    out.extend(NOCOLL_HAMMER.to_bytes(2, 'little'))   # nc=0 无碰撞
    out.extend(bytes([shape]))
    for v in size:
        out.extend(struct.pack('<f', v))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in rot:
        out.extend(struct.pack('<f', v))
    out.extend(struct.pack('<5f', mass, ld, ad, REP, FRIC))
    out.extend(bytes([typ]))
    return bytes(out)


def make_joint_general(name, ra, rb, pos, mov_lo, mov_hi, rot_lo, rot_hi, spring_m, spring_r, ridx, encoding):
    """通用 6DOF 弹簧（可指定非对称移动/旋转限与弹簧系数）。"""
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))
    out.extend(bytes([0]))
    out.extend(ra.to_bytes(ridx, 'little'))
    out.extend(rb.to_bytes(ridx, 'little'))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in (0.0, 0.0, 0.0):
        out.extend(struct.pack('<f', v))                # rot
    for v in mov_lo:
        out.extend(struct.pack('<f', v))                # mov_lo
    for v in mov_hi:
        out.extend(struct.pack('<f', v))                # mov_hi
    for v in rot_lo:
        out.extend(struct.pack('<f', v))                # rot_lo
    for v in rot_hi:
        out.extend(struct.pack('<f', v))                # rot_hi
    for v in spring_m:
        out.extend(struct.pack('<f', v))                # spring_m
    for v in spring_r:
        out.extend(struct.pack('<f', v))                # spring_r
    return bytes(out)


def _axis_to_euler(a):
    """把胶囊长轴(竖直Y)对齐到单位向量 a。"""
    ay = a[1]
    rx = math.atan2(a[2], ay)
    rz = math.atan2(-a[0], math.sqrt(ay * ay + a[2] * a[2]))
    return (rx, 0.0, rz)


def add_hammer_chain(src, dst):
    r = PMXReader(src).parse()
    print('=' * 70)
    print('输入:', src)
    print(f'骨骼={r.bone_count} 刚体={r.rigid_count} 约束={r.joint_count} '
          f'bidx={r.bidx} ridx={r.ridx} encoding={r.encoding}')

    # ---- 1. 识别主发链 ----
    chains = find_hair_chains(r)
    if not chains:
        print('[ERROR] 未找到触觉(Hair2)/后发(Hair4)骨骼')
        return False
    hair4_kind = classify_hair4(chains, r)   # pony / shoulder / waist / None
    if hair4_kind:
        kind_cn = {'pony': '单马尾', 'shoulder': '披肩长发', 'waist': '及腰长发'}[hair4_kind]
        print(f'后发类型: {kind_cn}')

    # 已有刚体 -> 绑定骨骼索引 映射（用于找主发刚体）
    rigid_by_bone = {}
    for rb_idx in range(r.rigid_count):
        off, bone = r.rigid_bone_points[rb_idx]
        rigid_by_bone.setdefault(bone, rb_idx)

    base_bone = r.bone_count
    base_rigid = r.rigid_count
    new_bones = bytearray()
    new_rigids = bytearray()
    new_joints = bytearray()
    nb = 0   # 新增骨骼计数
    nr = 0   # 新增刚体计数
    nj = 0   # 新增约束计数

    for key in sorted(chains.keys()):
        chain = chains[key]
        part, p = key
        pos = [bone_pos(r, i) for i in chain]
        n = len(chain)
        label = f'{PART_CN[part]}{POS_CN[p]}'

        # 后发 Hair4 按类型选参数；马尾（仅中后发）不挂锤链，跳过
        if part == '4':
            if hair4_kind == 'pony':
                print(f'\n[{label}] 单马尾：不挂锤链，跳过')
                continue
            prm = HAIR4_HAMMER[hair4_kind]
            radius = prm['radius']
            hlen = prm['len']
            mass_t, ld_t, ad_t = prm['mass'], prm['ld'], prm['ad']
        else:
            radius = RADIUS[part]
            hlen = HAMMER_LEN[part]
            mass_t, ld_t, ad_t = MASS, LD, AD

        n_ham = n - 1                          # 錘骨挂在 _00/_01/_02 下，末端 _03 不挂
        tip_y = pos[-1][1]                     # 发尾 y（下垂水平线）
        print(f'\n[{label}] {n} 段主发骨 → {n_ham} 个錘骨，半径={radius}')

        # 主发刚体索引（绑 chain[0..n-2]）
        main_rigids = []
        ok = True
        for seg in range(n_ham):
            bi = chain[seg]
            if bi in rigid_by_bone:
                main_rigids.append(rigid_by_bone[bi])
            else:
                print(f'  [WARN] 主发骨 {bi} 无刚体，跳过该束')
                ok = False
                break
        if not ok:
            continue

        # ---- 2. 创建錘骨 + 錘刚体 ----
        # 发尾方向（后下方）+ 后扬（额外向后 z 倾斜），避免发尾贴背
        d_tip, _ = norm((pos[-1][0] - pos[-2][0],
                         pos[-1][1] - pos[-2][1],
                         pos[-1][2] - pos[-2][2] + HAMMER_BACK_TILT))
        rot = _axis_to_euler(d_tip)            # 錘沿发尾方向（微后扬）下垂

        hammer_rigids = []
        for seg in range(n_ham):
            main_bone = chain[seg]
            main_name = r.bone_items[main_bone][0].rstrip()
            main_g = r.bone_items[main_bone][1].rstrip()
            hp = (pos[seg][0], tip_y, pos[seg][2])   # x/z 随主发骨，y 降到发尾
            tail_bone = chain[seg + 1]              # tail 指向下一段主发骨（定义朝向）
            bone_idx = base_bone + nb
            nb += 1
            new_bones.extend(make_bone(main_name + '錘', main_g, hp, main_bone, tail_bone, r.bidx, r.encoding))

            hlen_i = hlen[seg] if seg < len(hlen) else hlen[-1]
            mass = mass_t[seg] if seg < len(mass_t) else mass_t[-1]
            ld = ld_t[seg] if seg < len(ld_t) else ld_t[-1]
            ad = ad_t[seg] if seg < len(ad_t) else ad_t[-1]
            size = (radius, hlen_i, 0.0)
            rb = make_rigid(main_name + '錘', bone_idx, SHAPE_CAPSULE, size, hp, rot,
                            TYP_DYNAMIC, mass, ld, ad, r.bidx, r.encoding)
            new_rigids.extend(rb)
            rid = base_rigid + nr
            nr += 1
            hammer_rigids.append(rid)
            print(f'  錘骨[{bone_idx}] {main_name}錘 <- 父骨 {main_bone} '
                  f'pos=({hp[0]:.3f},{hp[1]:.3f},{hp[2]:.3f}) size=(r{radius},h{hlen_i}) m={mass:g} nc=0')

        # ---- 3. 刚性连接（主刚体 ↔ 錘刚体） ----
        for seg in range(n_ham):
            ra = main_rigids[seg]
            rb = hammer_rigids[seg]
            name = r.bone_items[chain[seg]][0].rstrip() + '錘'
            jb = make_joint_general(name, ra, rb, pos[seg],
                                    (0, 0, 0), (0, 0, 0),
                                    (0, 0, 0), (0, 0, 0),
                                    (RIGID_SPRING,) * 3, (RIGID_SPRING,) * 3,
                                    r.ridx, r.encoding)
            new_joints.extend(jb)
            nj += 1
            print(f'  刚性连接 {name}: 主刚体{ra} ↔ 錘刚体{rb} spring=100000')

        # ---- 4. 錘链补充约束（錘刚体之间，平移自由 + 旋转锁死） ----
        for seg in range(1, n_ham):
            ra = hammer_rigids[seg - 1]
            rb = hammer_rigids[seg]
            name = r.bone_items[chain[seg]][0].rstrip() + '補'
            jb = make_joint_general(name, ra, rb, (0, 0, 0),
                                    (-HAMMER_MOV,) * 3, (HAMMER_MOV,) * 3,
                                    (HAMMER_ROT_LO,) * 3, (HAMMER_ROT_HI,) * 3,
                                    (0, 0, 0), (0, 0, 0),
                                    r.ridx, r.encoding)
            new_joints.extend(jb)
            nj += 1
            print(f'  錘链补充 {name}: 錘刚体{ra} ↔ 錘刚体{rb} mov=±{HAMMER_MOV} rot锁死')

    # ---- 5. 组装输出 ----
    new_bc = r.bone_count + nb
    new_rc = r.rigid_count + nr
    new_jc = r.joint_count + nj
    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little')); out.extend(r.vertex_bytes)
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(new_bc.to_bytes(4, 'little'))
    out.extend(r.bone_bytes)
    out.extend(new_bones)
    out.extend(r.morph_count.to_bytes(4, 'little')); out.extend(r.morph_bytes)
    out.extend(r.frame_count.to_bytes(4, 'little')); out.extend(r.frame_bytes)
    out.extend(new_rc.to_bytes(4, 'little'))
    out.extend(r.rigid_bytes)
    out.extend(new_rigids)
    out.extend(new_jc.to_bytes(4, 'little'))
    out.extend(r.joint_bytes)
    out.extend(new_joints)

    with open(dst, 'wb') as f:
        f.write(bytes(out))

    # ---- 6. 验证 ----
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.bone_count == new_bc, f'骨骼数 {v.bone_count} != {new_bc}'
    assert v.rigid_count == new_rc, f'刚体数 {v.rigid_count} != {new_rc}'
    assert v.joint_count == new_jc, f'约束数 {v.joint_count} != {new_jc}'
    print(f'\n[OK] 骨骼 {r.bone_count}->{v.bone_count}, 刚体 {r.rigid_count}->{v.rigid_count}, '
          f'约束 {r.joint_count}->{v.joint_count}, remaining=0')
    print(f'写出: {os.path.basename(dst)}')
    return True




