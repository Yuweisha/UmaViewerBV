# -*- coding: utf-8 -*-
"""
给新模型生成 1013_2 风格「头发刚体 + 链式约束」（覆盖 Hair0~Hair4）
============================================================================
数据取自 Archieve/Mejiro McQueen目白麦昆决胜服/1013_2_Mejiro McQueen.pmx 实测，
并保留现有「呆毛(Hair1)硬耳」与「耳朵(ear_rigid_1013_2.py)」处理。

## 一、头发类型与处理
    Hair0 前发 / Hair1 呆毛 / Hair2 触觉 → 硬耳（mass=9，仅绕Y±8°，group=4，效果更稳）
    Hair3 横发 / Hair4 后发 → 1013_2 梯度物理（发根稳发尾飘）
    耳朵 → 保留 ear_rigid_1013_2.py（本脚本不处理）

## 二、梯度头发参数（Hair3/4，1013_2 实测）
    mass 0.50625 / 0.225 / 0.1（等比 1/2.25）、ld 0.9/0.95/0.999、ad 1/1.5/2
    半径：后发 0.30/0.40（长发自动判断，见 HAIR4_LONG_PARAMS）；横发 自动解析（P90 顶点距离 ×0.8）
    公共：rep=0 fric=0 group=5 nc=65503（0xFFDF：位5=0 组内不碰；位0~4=1 与身体/头碰）
    约束：单层链式 Head→发1→发2→发3，平移锁死，旋转限 6/8/6°→22/18/14°（缓慢飘逸），spring_r 发尾 12

## 三、前发/呆毛/触觉硬耳（Hair0/1/2，保留 hair_rigid_1013_2.py 风格）
    mass=9 ld=0.9999 ad=0.9999 group=4 nc=65519 仅绕Y±8° 无弹簧
    几何：每段一个刚体（胶囊 + 末端球）

用法：python src/hair_rigid_1013_2_real.py 输入.pmx 输出.pmx
前置：输入模型须已有 Head 刚体（先跑 body_physics）。
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
from body_physics import parse_vertices  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ============ 1013_2 梯度头发参数（Hair0/2/3/4，实测） ============
GROUP = 5
NOCOLL = 65503            # 0xFFDF：位5=0 组内(头发)不碰；位0~4=1 与身体/头(组0~4)碰
REP = 0.0
FRIC = 0.0

# 每段梯度（发根 seg0 → 发尾 seg2）
MASS = (0.50625, 0.225, 0.1)        # 等比，公比 1/2.25
LD = (0.9, 0.95, 0.999)             # 线阻尼
AD = (1.0, 1.5, 2.0)                # 角阻尼

# 半径：Hair2 触觉 用 1013_2 固定值；其余（前发/横发/呆毛/马尾）自动解析；后发长发见 HAIR4_LONG_PARAMS
RADIUS_FIXED = {'2': 0.22}

# 后发 Hair4 长发参数（披肩 vs 及腰，按骨骼链折线总长自动判断）
# 披肩 = 1013_2 目白麦昆决胜服；及腰 = Calstone Light O 金镇之光决胜服
HAIR4_LONG_PARAMS = {
    'shoulder': {'radius': 0.30, 'mass': (0.50625, 0.225, 0.1),
                 'ld': (0.9, 0.95, 0.999), 'ad': (1.0, 1.5, 2.0)},
    'waist': {'radius': 0.40, 'mass': (1.1391, 0.5063, 0.225, 0.1),
              'ld': (0.9, 0.93, 0.969, 0.999), 'ad': (1.0, 1.304, 1.696, 0.999)},
}
# 后发骨骼链折线总长阈值：>= 视为及腰长发，否则披肩长发（披肩约3.9 / 及腰约6.1）
HAIR4_WAIST_LEN = 5.0

# 约束旋转限（度）+ spring_r，参考 1013_2 耳朵「缓慢飘逸」梯度（发根稳→发尾飘）
ROT_LIMITS = [
    (6, 8, 6),     # 发1 根：接近锁定，跟随头部
    (14, 14, 10),  # 发2 中
    (22, 18, 14),  # 发3 尾：缓慢飘逸
]
SPRING_R = (0.0, 12.0, 12.0)        # 发尾弹簧回正（慢回正）

# ============ 前发/呆毛/触觉 硬耳参数（hair_rigid_1013_2.py 硬耳：静止保持、移动微变） ============
AHO_GROUP = 4
AHO_NOCOLL = 65519         # 0xFFEF：位4=0 组内(耳/呆毛)不碰；位0=1 与头(组0)碰
AHO_MASS = 9.0
AHO_LD = 0.9999
AHO_AD = 0.9999
AHO_ROT_LIMIT = math.radians(8.0)       # 绕Y ±8°
AHO_ROT_LIMIT_XZ = 0.0                  # 前后X/左右Z 锁死（仅绕Y微摆）

# ============ 半径自动解析（同 hair_rigid_1013_2.py） ============
RADIUS_SCALE = 0.8
RADIUS_CAP_FRAC = 0.4

SHAPE_SPHERE, SHAPE_BOX, SHAPE_CAPSULE = 0, 1, 2
TYP_DYNAMIC, TYP_DYNAMIC_ALIGN = 1, 2

HEAD_G = 'Head'
HAIR_RE = re.compile(r'^Sp_He_Hair([012345])_([CLR])_(\d+)$')

PART_CN = {'0': '前发', '1': '呆毛', '2': '触觉', '3': '横发', '4': '后发', '5': '后发'}
POS_CN = {'C': '中', 'L': '左', 'R': '右'}


def encode_text(s, encoding):
    """编码文本。encoding 遵循 PMX 标准：0=UTF-16LE, 1=UTF-8"""
    raw = s.encode('utf-16-le' if encoding == 0 else 'utf-8')
    return len(raw).to_bytes(4, 'little', signed=True) + raw


def axis_to_euler(a):
    """把胶囊长轴(竖直Y)对齐到单位向量 a，返回欧拉角(rx,0,rz)。R=Ry·Rx·Rz；ry=0"""
    ay = a[1]
    rx = math.atan2(a[2], ay)
    rz = math.atan2(-a[0], math.sqrt(ay * ay + a[2] * a[2]))
    return (rx, 0.0, rz)


def norm(d):
    l = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    if l < 1e-9:
        return (0.0, 1.0, 0.0), 0.0
    return (d[0] / l, d[1] / l, d[2] / l), l


def lerp(a, b, t):
    return a + (b - a) * t


def grad(arr, seg, n_rigid):
    """按段数从 3 段基准梯度线性插值（n_rigid=1 时取根段）"""
    if n_rigid <= 1:
        return arr[0]
    t = seg / (n_rigid - 1) * (len(arr) - 1)
    i = int(t)
    if i >= len(arr) - 1:
        return arr[-1]
    return lerp(arr[i], arr[i + 1], t - i)


def make_rigid(name, bone_idx, shape, size, pos, rot, typ, mass, ld, ad, bidx, encoding,
               group=GROUP, nocoll=NOCOLL):
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))
    out.extend(bone_idx.to_bytes(bidx, 'little'))
    out.extend(bytes([group]))
    out.extend(nocoll.to_bytes(2, 'little'))
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


def make_joint(name, ra, rb, pos, rot_lim, spring_r, ridx, encoding, mov_lim=0.0, spring_m=0.0):
    """6DOF 弹簧：移动限(±mov_lim) + 三轴旋转限 + 旋转/移动弹簧回正"""
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
    for v in (-mov_lim, -mov_lim, -mov_lim):
        out.extend(struct.pack('<f', v))                # mov_lo
    for v in (mov_lim, mov_lim, mov_lim):
        out.extend(struct.pack('<f', v))                # mov_hi
    for v in (-rot_lim[0], -rot_lim[1], -rot_lim[2]):
        out.extend(struct.pack('<f', v))                # rot_lo
    for v in (rot_lim[0], rot_lim[1], rot_lim[2]):
        out.extend(struct.pack('<f', v))                # rot_hi
    for v in (spring_m, spring_m, spring_m):
        out.extend(struct.pack('<f', v))                # spring_m
    for v in (spring_r, spring_r, spring_r):
        out.extend(struct.pack('<f', v))                # spring_r
    return bytes(out)


def bone_pos(r, i):
    return struct.unpack_from('<3f', r.bone_items[i][4], 0)


def find_hair_chains(r):
    """识别所有头发骨骼链，返回 { (部位, 位置): [骨骼索引(编号升序)] }
    排除 name 以 N/n 结尾的控制骨，以及 name 以「锤/錘」结尾的物理配重骨（与主发骨共享 name_g）。"""
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
    hair4_keys = [k for k in chains if k[0] in ('4', '5')]
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


def _dist_to_chain(p, pos):
    """点 p 到折线 pos 的最近距离"""
    best = 1e9
    for a, b in zip(pos, pos[1:]):
        d = (b[0] - a[0], b[1] - a[1], b[2] - a[2])
        l2 = d[0] ** 2 + d[1] ** 2 + d[2] ** 2
        t = 0.0 if l2 < 1e-9 else max(0.0, min(1.0, (
            (p[0] - a[0]) * d[0] + (p[1] - a[1]) * d[1] + (p[2] - a[2]) * d[2]) / l2))
        q = (a[0] + t * d[0], a[1] + t * d[1], a[2] + t * d[2])
        best = min(best, math.sqrt(sum((p[i] - q[i]) ** 2 for i in range(3))))
    return best


def auto_radius(r, chain_idx, chain_pos):
    """自动解析半径：P90 顶点距离 × 0.8，上限=平均段长×0.4（同 hair_rigid_1013_2.py）"""
    verts = parse_vertices(r)
    idx_set = set(chain_idx)
    dists = []
    for v, bs in verts:
        if any(b in idx_set for b in bs):
            dists.append(_dist_to_chain(v, chain_pos))
    if not dists:
        return 0.3
    dists.sort()
    p90 = dists[int(len(dists) * 0.9)]
    segs = []
    for a, b in zip(chain_pos, chain_pos[1:]):
        segs.append(math.sqrt(sum((b[k] - a[k]) ** 2 for k in range(3))))
    avg_seg = sum(segs) / len(segs) if segs else 1.0
    return min(p90, avg_seg * RADIUS_CAP_FRAC) * RADIUS_SCALE


def add_hair_rigid(src, dst):
    r = PMXReader(src).parse()
    print('=' * 70)
    print('输入:', src)
    print(f'骨骼={r.bone_count} 刚体={r.rigid_count} 约束={r.joint_count} '
          f'bidx={r.bidx} ridx={r.ridx} encoding={r.encoding}')

    # ---- 1. Head 刚体 ----
    idx_by_g = {nmg.rstrip(): i for i, (nm, nmg, *_rest) in enumerate(r.bone_items)}
    if HEAD_G not in idx_by_g:
        print(f'[ERROR] 找不到 Head 骨 name_g={HEAD_G}')
        return False
    head_i = idx_by_g[HEAD_G]
    head_rigid = None
    for rb_idx in range(r.rigid_count):
        off, bone = r.rigid_bone_points[rb_idx]
        if bone == head_i:
            head_rigid = rb_idx
            break
    if head_rigid is None:
        print('[ERROR] 现有刚体中无 Head 刚体（请先跑 body_physics）')
        return False
    print(f'Head 骨={head_i} Head刚体={head_rigid}')

    # ---- 2. 识别头发链 ----
    chains = find_hair_chains(r)
    if not chains:
        print('[ERROR] 未找到头发骨骼（Sp_He_Hair0~4_*）')
        return False
    hair4_kind = classify_hair4(chains, r)   # pony / shoulder / waist / None
    if hair4_kind:
        kind_cn = {'pony': '单马尾', 'shoulder': '披肩长发', 'waist': '及腰长发'}[hair4_kind]
        print(f'后发类型: {kind_cn}')

    # ---- 3. 生成刚体 + 约束 ----
    base_rigid = r.rigid_count
    new_rigids = bytearray()
    new_joints = bytearray()
    k = 0

    for key in sorted(chains.keys()):
        chain = chains[key]
        part, p = key
        n = len(chain)
        pos = [bone_pos(r, i) for i in chain]
        label = f'{PART_CN[part]}{POS_CN[p]}'

        # ===== 前发(Hair0)/呆毛(Hair1)/触觉(Hair2)：硬耳（回退，效果更稳；n-1 个胶囊，末端无刚体，与梯度分支同构） =====
        if part in ('0', '1', '2'):
            radius = auto_radius(r, chain, pos)
            hg, hnc = AHO_GROUP, AHO_NOCOLL   # 4, 65519（硬耳组，与耳朵同组）
            n_rigid = n - 1                   # n-1 个胶囊，末端无刚体
            print(f'\n[{label}] {n} 段骨骼 → {n_rigid} 个刚体（硬耳，g={hg}），半径={radius:.3f}')
            rigid_ids = []
            for seg in range(n_rigid):
                dvec, seglen = norm((pos[seg + 1][0] - pos[seg][0],
                                     pos[seg + 1][1] - pos[seg][1],
                                     pos[seg + 1][2] - pos[seg][2]))
                mid = ((pos[seg][0] + pos[seg + 1][0]) / 2,
                       (pos[seg][1] + pos[seg + 1][1]) / 2,
                       (pos[seg][2] + pos[seg + 1][2]) / 2)
                rot = axis_to_euler(dvec)
                size = (radius, seglen, 0.0)
                shape = SHAPE_CAPSULE
                typ = TYP_DYNAMIC_ALIGN if seg == 0 else TYP_DYNAMIC

                name = r.bone_items[chain[seg]][0].rstrip()
                rb = make_rigid(name, chain[seg], shape, size, mid, rot, typ,
                                AHO_MASS, AHO_LD, AHO_AD, r.bidx, r.encoding,
                                group=hg, nocoll=hnc)
                new_rigids.extend(rb)
                rid = base_rigid + k
                rigid_ids.append(rid)
                print(f'  刚体[{rid}] {name} <- 绑骨 {chain[seg]} shape={shape} '
                      f'size=(r{radius:.3f},h{size[1]:.3f}) typ={typ} m={AHO_MASS:g} g={hg}')
                k += 1

            for seg in range(n_rigid):
                rb = rigid_ids[seg]
                ra = head_rigid if seg == 0 else rigid_ids[seg - 1]
                jpos = pos[seg]
                name = r.bone_items[chain[seg]][0].rstrip()
                rot_lim = (AHO_ROT_LIMIT_XZ, AHO_ROT_LIMIT, AHO_ROT_LIMIT_XZ)
                jb = make_joint(name, ra, rb, jpos, rot_lim, 0.0,
                                r.ridx, r.encoding)
                new_joints.extend(jb)
                print(f'  约束 {name}: ra={ra} rb={rb} 仅绕Y±8° 无弹簧')
            continue

        # ===== 其他头发（Hair0/2/3/4）：1013_2 梯度 =====
        # 后发 Hair4 按类型（披肩/及腰长发）选参数；马尾用自动半径+通用梯度
        mass_t, ld_t, ad_t = MASS, LD, AD
        if part in ('4', '5') and hair4_kind != 'pony':
            prm = HAIR4_LONG_PARAMS[hair4_kind]
            radius = prm['radius']
            mass_t, ld_t, ad_t = prm['mass'], prm['ld'], prm['ad']
        else:
            radius = RADIUS_FIXED.get(part) or auto_radius(r, chain, pos)
        n_rigid = n - 1          # 1013_2 规律：n 段骨骼 → n-1 个胶囊，末端无刚体
        print(f'\n[{label}] {n} 段骨骼 → {n_rigid} 个刚体（梯度），半径={radius:.3f}')

        rigid_ids = []
        for seg in range(n_rigid):
            dvec, seglen = norm((pos[seg + 1][0] - pos[seg][0],
                                 pos[seg + 1][1] - pos[seg][1],
                                 pos[seg + 1][2] - pos[seg][2]))
            mid = ((pos[seg][0] + pos[seg + 1][0]) / 2,
                   (pos[seg][1] + pos[seg + 1][1]) / 2,
                   (pos[seg][2] + pos[seg + 1][2]) / 2)
            rot = axis_to_euler(dvec)
            size = (radius, seglen, 0.0)
            shape = SHAPE_CAPSULE
            typ = TYP_DYNAMIC_ALIGN if seg == 0 else TYP_DYNAMIC
            mass = grad(mass_t, seg, n_rigid)
            ld = grad(ld_t, seg, n_rigid)
            ad = grad(ad_t, seg, n_rigid)

            name = r.bone_items[chain[seg]][0].rstrip()
            rb = make_rigid(name, chain[seg], shape, size, mid, rot, typ,
                            mass, ld, ad, r.bidx, r.encoding)
            new_rigids.extend(rb)
            rid = base_rigid + k
            rigid_ids.append(rid)
            print(f'  刚体[{rid}] {name} <- 绑骨 {chain[seg]} '
                  f'size=(r{radius:.3f},h{seglen:.3f}) typ={typ} m={mass:g} ld={ld:g} ad={ad:g}')
            k += 1

        for seg in range(n_rigid):
            rb = rigid_ids[seg]
            ra = head_rigid if seg == 0 else rigid_ids[seg - 1]
            jpos = pos[seg]
            name = r.bone_items[chain[seg]][0].rstrip()
            # 初始段长（相邻骨骼距离）→ 移动限 + 移动弹簧，保持初始距离防止刚体重叠
            _, seglen = norm((pos[seg + 1][0] - pos[seg][0],
                              pos[seg + 1][1] - pos[seg][1],
                              pos[seg + 1][2] - pos[seg][2]))
            mov_lim = seglen * 0.5          # 移动限 = 半个段长（防止相邻胶囊重叠）
            spring_m = 100.0                # 移动弹簧（把刚体拉回初始骨骼链位置）
            fx, ty, sz = ROT_LIMITS[seg] if seg < len(ROT_LIMITS) else ROT_LIMITS[-1]
            spring_r = SPRING_R[seg] if seg < len(SPRING_R) else SPRING_R[-1]
            rot_lim = (math.radians(fx), math.radians(ty), math.radians(sz))
            jb = make_joint(name, ra, rb, jpos, rot_lim, spring_r, r.ridx, r.encoding, mov_lim, spring_m)
            new_joints.extend(jb)
            print(f'  约束 {name}: ra={ra} rb={rb} '
                  f'rot_lim=(±{fx}°,±{ty}°,±{sz}°) mov=±{mov_lim:.2f} spring_m={spring_m}')

    # ---- 4. 组装输出 ----
    new_rc = r.rigid_count + k
    new_jc = r.joint_count + k
    out = bytearray()
    out.extend(r.header_bytes)
    out.extend(r.info_bytes)
    out.extend(r.vertex_count.to_bytes(4, 'little')); out.extend(r.vertex_bytes)
    out.extend(r.face_count.to_bytes(4, 'little')); out.extend(r.face_bytes)
    out.extend(r.texture_count.to_bytes(4, 'little')); out.extend(r.tex_bytes)
    out.extend(r.material_count.to_bytes(4, 'little')); out.extend(r.mat_bytes)
    out.extend(r.bone_count.to_bytes(4, 'little')); out.extend(r.bone_bytes)
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

    # ---- 5. 验证 ----
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.rigid_count == new_rc, f'刚体数 {v.rigid_count} != {new_rc}'
    assert v.joint_count == new_jc, f'约束数 {v.joint_count} != {new_jc}'
    print(f'\n[OK] 刚体 {r.rigid_count}->{v.rigid_count}, 约束 {r.joint_count}->{v.joint_count}, remaining=0')
    print(f'写出: {os.path.basename(dst)}')
    return True




