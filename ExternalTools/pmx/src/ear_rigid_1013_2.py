# -*- coding: utf-8 -*-
"""
给新模型建立「耳朵刚体 + 链式约束」（数值套用 1013_2_Mejiro McQueen 参考）
============================================================================

## 一、参考模型 1013_2 的耳骨物理实测（Archieve/Mejiro McQueen目白麦昆决胜服）
   - 耳骨链（左耳，右耳镜像）：
       头(Head) ─ Sp_He_Ear0_L_00(耳親,不绑刚体) ─ Ear_01_L ─ Ear_02_L ─ Ear_03_L
   - 刚体 6 个（绑蒙皮骨 Ear_01~03，不绑弹簧骨/耳親）：
       Ear_01 : 胶囊  dynamic+对齐(2)    r=0.33  半高=段长|P1-P0|
       Ear_02 : 胶囊  dynamic(1)         r=0.33  半高=段长|P2-P1|
       Ear_03 : 球    dynamic(1)         r=0.33
   - 约束 6 个（全部 6DOF 弹簧，链式 头→耳1→耳2→耳3）：
       平移全锁(mov=0)，仅绕局部Y ±0.139626rad(=8°)，无弹簧(spring=0)

## 二、刚体生成的数学逻辑（本脚本实现）
   设每耳三个骨骼位置 P0=Ear_01, P1=Ear_02, P2=Ear_03（左右各一组）：
   1. 段向量  d1 = P1-P0, d2 = P2-P1
   2. 胶囊(第1段)  位置 mid = (P0+P1)/2       半高 = |d1|   半径 r = 0.33
      胶囊(第2段)  位置 mid = (P1+P2)/2       半高 = |d2|   半径 r = 0.33
      球(第3段/末) 位置     = P2              半径 r = 0.33（末端无下一段，球心=骨位置）
   3. 朝向 rot = axis_to_euler(段单位向量)
      （MMD 刚体欧拉约定 R = Ry·Rx·Rz；胶囊绕长轴Y轴对称 → ry=0 省略滚动，不影响形状）

## 三、物理参数（1013_2 基准 + 软硬梯度）
   公共（1013_2 参考，不随软硬变化）：rep=0  fric=0  group=4  nocoll=65519
   软硬梯度 GRADIENT[seg]：耳根(0)偏硬 → 耳尖(2)软
     1013_2 原始硬耳基准：mass=9, ld=0.9999, ad=0.9999, 仅Y±8°, spring=0
     调软方向：质量↓ / 阻尼↓ / 旋转限(前后X,扭转Y,左右Z)↑ / spring_r↑

用法：python src/ear_rigid_1013_2.py 输入.pmx 输出.pmx
前置：输入模型须已有 Head 刚体（先跑 body_physics），且耳骨 name_g = Ear_01~03_L/R
"""
import os
import sys
import struct
import math

SRC = os.path.dirname(os.path.abspath(__file__))
if SRC not in sys.path:
    sys.path.insert(0, SRC)
from clean_handle_bones import PMXReader  # noqa: E402

try:
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
except Exception:
    pass

# ============ 公共参数（1013_2 参考，不随软硬变化） ============
REP = 0.0                         # 反発
FRIC = 0.0                        # 摩擦
GROUP = 4                         # 碰撞组（1013_2 耳=4 组）
NOCOLL = 65519                    # 非碰撞组 0xFFEF：位4=0 → 不与自身组4碰撞
EAR_RADIUS = 0.33                 # 耳段截面半径（1013_2 固定值，可按目标模型耳朵粗细调）

# ============ 软硬梯度（seg=0 耳根 → seg=2 耳尖，越靠上/尖越软） ============
# 每段 (mass, linear_damp, angular_damp, 前后X°, 扭转Y°, 左右Z°, spring_r)
# 耳根(seg0) 保持 1013_2 硬耳质感；耳尖(seg2) 参考 ear_physics 软耳
# 1013_2 原始硬耳基准：mass=9, ld=0.9999, ad=0.9999, 仅Y±8°, spring=0
#
# ★ 缓慢飘逸感 = 低 spring_r（恢复慢）+ 低 ad（衰减慢）：
#   spring_r 越低 → 回正越慢（橡皮筋→慢回弹海绵）；ad 越低 → 摆动余韵越长
#   当前 spring_r：耳尖 12、耳中 12（远低于快弹量级 200），恢复缓慢飘逸
GRADIENT = [
    (3.0, 0.40, 0.50,  6,  8,  6,   0),   # 耳1 根：偏硬（根不动）
    (1.0, 0.20, 0.25, 14, 14, 10,  12),   # 耳2 中：慢回弹
    (0.2, 0.08, 0.12, 22, 18, 14,  12),   # 耳3 尖：缓慢飘逸（弹簧=12，恢复更慢）
]

SHAPE_SPHERE, SHAPE_BOX, SHAPE_CAPSULE = 0, 1, 2
TYP_DYNAMIC, TYP_DYNAMIC_ALIGN = 1, 2

# 骨骼 name_g 匹配（英文，稳定）
HEAD_G = 'Head'
EAR_G = ['Ear_01_L', 'Ear_02_L', 'Ear_03_L', 'Ear_01_R', 'Ear_02_R', 'Ear_03_R']


def encode_text(s, encoding):
    """编码文本。encoding 遵循 PMX 标准：0=UTF-16LE, 1=UTF-8"""
    raw = s.encode('utf-16-le' if encoding == 0 else 'utf-8')
    return len(raw).to_bytes(4, 'little', signed=True) + raw


def axis_to_euler(a):
    """把胶囊长轴(竖直Y)对齐到单位向量 a，返回欧拉角(rx,0,rz)。
    MMD 刚体欧拉约定 R=Ry·Rx·Rz；胶囊绕长轴(Y)轴对称 → ry=0 省略滚动。
    即 R = Rx(rx)·Rz(rz)，使 R·(0,1,0) == a。"""
    ay = a[1]
    rx = math.atan2(a[2], ay)
    rz = math.atan2(-a[0], math.sqrt(ay * ay + a[2] * a[2]))
    return (rx, 0.0, rz)


def norm(d):
    l = math.sqrt(d[0] ** 2 + d[1] ** 2 + d[2] ** 2)
    if l < 1e-9:
        return (0.0, 1.0, 0.0), 0.0
    return (d[0] / l, d[1] / l, d[2] / l), l


def make_rigid(name, bone_idx, shape, size, pos, rot, typ, mass, ld, ad, bidx, encoding):
    """构造一个 PMX2.0 刚体条目（参数区 = bidx + 61 字节），mass/ld/ad 按软硬梯度传入"""
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))              # name_g 留空
    out.extend(bone_idx.to_bytes(bidx, 'little'))      # 绑骨索引
    out.extend(bytes([GROUP]))                         # 碰撞组
    out.extend(NOCOLL.to_bytes(2, 'little'))           # 非碰撞组
    out.extend(bytes([shape]))                         # 形状
    for v in size:
        out.extend(struct.pack('<f', v))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in rot:
        out.extend(struct.pack('<f', v))
    out.extend(struct.pack('<5f', mass, ld, ad, REP, FRIC))
    out.extend(bytes([typ]))
    return bytes(out)


def make_joint(name, ra, rb, pos, rot_lim, spring, ridx, encoding):
    """构造一个 6DOF 弹簧约束。
    rot_lim=(fx,ty,sz) 三轴旋转限（弧度）；spring=旋转弹簧系数（三轴等值）。
    平移 mov_lo/mov_hi=0 锁死；spring_m=0。"""
    out = bytearray()
    out.extend(encode_text(name, encoding))
    out.extend(encode_text('', encoding))
    out.extend(bytes([0]))                              # Joint 种类：0=バネ付6DOF
    out.extend(ra.to_bytes(ridx, 'little'))
    out.extend(rb.to_bytes(ridx, 'little'))
    for v in pos:
        out.extend(struct.pack('<f', v))
    for v in (0.0, 0.0, 0.0):
        out.extend(struct.pack('<f', v))                # rot（约束朝向=0）
    for v in (0.0, 0.0, 0.0):
        out.extend(struct.pack('<f', v))                # mov_lo（平移锁死）
    for v in (0.0, 0.0, 0.0):
        out.extend(struct.pack('<f', v))                # mov_hi
    for v in (-rot_lim[0], -rot_lim[1], -rot_lim[2]):
        out.extend(struct.pack('<f', v))                # rot_lo
    for v in (rot_lim[0], rot_lim[1], rot_lim[2]):
        out.extend(struct.pack('<f', v))                # rot_hi
    for v in (0.0, 0.0, 0.0):
        out.extend(struct.pack('<f', v))                # spring_m（无回弹）
    for v in (spring, spring, spring):
        out.extend(struct.pack('<f', v))                # spring_r（回弹挺立）
    return bytes(out)


def add_ear_rigid(src, dst):
    r = PMXReader(src).parse()
    print('=' * 70)
    print('输入:', src)
    print(f'骨骼={r.bone_count} 刚体={r.rigid_count} 约束={r.joint_count} '
          f'bidx={r.bidx} ridx={r.ridx} encoding={r.encoding}')

    # ---- 1. 定位 Head 骨 / Head 刚体（约束锚点） ----
    idx_by_g = {}
    for i, (nm, nmg, _, _, _) in enumerate(r.bone_items):
        idx_by_g[nmg] = i
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

    # ---- 2. 定位耳骨并取位置 ----
    def bone_pos(i):
        return struct.unpack_from('<3f', r.bone_items[i][4], 0)

    ear_idx = []
    for g in EAR_G:
        if g not in idx_by_g:
            print(f'[ERROR] 找不到耳朵骨 name_g={g}')
            return False
        ear_idx.append(idx_by_g[g])
    ear_name = [r.bone_items[i][0] for i in ear_idx]
    ear_pos = [bone_pos(i) for i in ear_idx]
    print(f'Head 骨={head_i} Head刚体={head_rigid}')

    # ---- 3. 生成 6 刚体 ----
    base_rigid = r.rigid_count
    new_rigids = bytearray()
    for k in range(6):
        side = 0 if k < 3 else 1   # 0=左 1=右
        seg = k % 3
        P = ear_pos[side * 3: side * 3 + 3]  # [P0, P1, P2]

        if seg < 2:
            # 胶囊段：中心=段中点，半高=段长，朝向沿段方向
            dvec, seglen = norm((P[seg + 1][0] - P[seg][0],
                                 P[seg + 1][1] - P[seg][1],
                                 P[seg + 1][2] - P[seg][2]))
            mid = ((P[seg][0] + P[seg + 1][0]) / 2,
                   (P[seg][1] + P[seg + 1][1]) / 2,
                   (P[seg][2] + P[seg + 1][2]) / 2)
            rot = axis_to_euler(dvec)
            size = (EAR_RADIUS, seglen, 0.0)
            shape = SHAPE_CAPSULE
            typ = TYP_DYNAMIC_ALIGN if seg == 0 else TYP_DYNAMIC
        else:
            # 末端球：球心=骨位置
            mid = P[2]
            rot = (0.0, 0.0, 0.0)
            size = (EAR_RADIUS, 0.0, 0.0)
            shape = SHAPE_SPHERE
            typ = TYP_DYNAMIC

        mass, ld, ad, fx, ty, sz, spring = GRADIENT[seg]
        rb = make_rigid(ear_name[k], ear_idx[k], shape, size, mid, rot, typ,
                        mass, ld, ad, r.bidx, r.encoding)
        new_rigids.extend(rb)
        print(f'  刚体[{base_rigid + k}] {ear_name[k]} <- 绑骨 {ear_idx[k]} '
              f'shape={shape} mid=({mid[0]:.4f},{mid[1]:.4f},{mid[2]:.4f}) '
              f'size=(r{EAR_RADIUS:.3f},h{size[1]:.4f}) rot=({rot[0]:.4f},{rot[1]:.4f},{rot[2]:.4f}) '
              f'typ={typ} m={mass:g} ld={ld:g} ad={ad:g}')

    # ---- 4. 生成 6 约束（链式：头→耳1→耳2→耳3） ----
    new_joints = bytearray()
    for k in range(6):
        side = 0 if k < 3 else 1
        seg = k % 3
        rb = base_rigid + k
        ra = head_rigid if seg == 0 else base_rigid + side * 3 + seg - 1
        jpos = ear_pos[k]                      # 约束锚点 = 骨骼位置（1013_2 实测）
        mass, ld, ad, fx, ty, sz, spring = GRADIENT[seg]
        rot_lim = (math.radians(fx), math.radians(ty), math.radians(sz))
        jb = make_joint(ear_name[k], ra, rb, jpos, rot_lim, spring, r.ridx, r.encoding)
        new_joints.extend(jb)
        print(f'  约束 {ear_name[k]}: ra={ra} rb={rb} pos=({jpos[0]:.4f},{jpos[1]:.4f},{jpos[2]:.4f}) '
              f'rot_lim=(±{fx}°,±{ty}°,±{sz}°) spring_r={spring}')

    # ---- 5. 组装输出 ----
    new_rc = r.rigid_count + 6
    new_jc = r.joint_count + 6
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

    # ---- 6. 验证 ----
    v = PMXReader(dst).parse()
    assert v.remaining == 0, f'验证失败 remaining={v.remaining}'
    assert v.rigid_count == new_rc, f'刚体数 {v.rigid_count} != {new_rc}'
    assert v.joint_count == new_jc, f'约束数 {v.joint_count} != {new_jc}'
    print(f'[OK] 刚体 {r.rigid_count}->{v.rigid_count}, 约束 {r.joint_count}->{v.joint_count}, remaining=0')
    print(f'写出: {os.path.basename(dst)}')
    return True




