// Independent CySpring-compatible numeric kernel.
// Reference ABI/kernel SHA256: 74acb76f179e359cb4e8840cc13d0eb405238050af314ca4e74b05c0e89d722c
// No reference binary is embedded or loaded. Only test tools load the oracle.
#include <cstdint>
#include <cstring>
#include <cmath>
#include <xmmintrin.h>
#include "batch_job.h"
static_assert(sizeof(void*)==8 && sizeof(float)==4,"UmaSpringNative requires the 64-bit float32 ABI");
using longlong=std::intptr_t;
using ulonglong=std::uint64_t;
using undefined8=std::uint64_t;
using uint=std::uint32_t;
using ushort=std::uint16_t;
static int IntAt(const float* p,int index){int v;std::memcpy(&v,p+index,4);return v;}
static uint Bits(float x){uint u;std::memcpy(&u,&x,4);return u;}
static uint Bits(uint x){return x;}
static ulonglong CONCAT44(float hi,float lo){return (ulonglong(Bits(hi))<<32)|Bits(lo);}
static float Root(float x){return _mm_cvtss_f32(_mm_sqrt_ss(_mm_set_ss(x)));}
static float SQRT(float x){return Root(x);}
static float ABS(float x){return std::fabs(x);}
// Windows system math avoids MinGW's x87 trigonometry helpers.
extern "C" {
 extern double (__cdecl* __imp_sin)(double);
 extern double (__cdecl* __imp_cos)(double);
 extern double (__cdecl* __imp_fmod)(double,double);
 extern double (__cdecl* __imp_acos)(double);
 extern double (__cdecl* __imp_asin)(double);
 extern double (__cdecl* __imp_atan2)(double,double);
}
static double RefSin(double x,double=0){return __imp_sin(x);}
static double RefCos(double x,double=0){return __imp_cos(x);}
static double RefMod(double x,double y){return __imp_fmod(x,y);}
static double RefAcos(double x){return __imp_acos(x);}
static double RefAsin(double x){return __imp_asin(x);}
static double RefAtan2(double y,double x){return __imp_atan2(y,x);}
// COMISS/JA followed by MINSS: unordered angles propagate, rather than
// selecting a finite bound as a conventional C++ clamp would do.
static float ClampAngle(float value,float minimum,float maximum){
 const float low=-minimum;
 if(low>value)return low;
 return _mm_cvtss_f32(_mm_min_ss(_mm_set_ss(maximum),_mm_set_ss(value)));
}
static float* QuaternionToEuler(float* result,const float* q);
static void EvaluateSkirt(float* work,const float* args);
static void CollideBone(longlong,longlong,longlong,float*);
static void EvaluateSkirtCollider(float*,float*,float,float,float,float*,float*);
static void IntegrateBone(float*,longlong,longlong,float,float,float,float,float,float,float,float,char,char,float,float,float);

static float* QuaternionToEuler(float* result,const float* q){
 double xx=double(float(q[0]*q[0]))*2.0, yy=double(float(q[1]*q[1]))*2.0, zz=double(float(q[2]*q[2]))*2.0;
 double wx=double(float(q[3]*q[0]))*2.0, wy=double(float(q[3]*q[1]))*2.0, wz=double(float(q[3]*q[2]))*2.0;
 double xy=double(float(q[0]*q[1]))*2.0, xz=double(float(q[0]*q[2]))*2.0, yz=double(float(q[1]*q[2]))*2.0;
 float s=float(yz-wx),x,y,z;
 if(std::fabs(double(float(1.f-s)))<1.1920928955078125e-7){x=1.5707963705062866f;y=0.f;z=float(RefAtan2(double(float(xy-wz)),double(float((1.0-yy)-zz))));}
 else if(std::fabs(double(float(-1.f-s)))<1.1920928955078125e-7){x=-1.5707963705062866f;y=0.f;z=float(RefAtan2(double(float(xy-wz)),double(float((1.0-yy)-zz))));}
 else{x=float(RefAsin(std::fmax(-1.0,std::fmin(1.0,-double(s)))));y=float(RefAtan2(double(float(xz+wy)),double(float((1.0-xx)-yy))));z=float(RefAtan2(double(float(xy+wz)),double(float((1.0-xx)-zz))));}
 result[0]=float(double(float(x*360.f))/6.283185307179586);
 result[1]=float(double(float(y*360.f))/6.283185307179586);
 result[2]=float(double(float(z*360.f))/6.283185307179586);
 return result;
}
static void EvaluateSkirt(float* work,const float* a){
 float root[4]={a[20],a[21],a[22],0};
 float center[4]={a[16]-a[20],a[17]-a[21],a[18]-a[22],0};
 float point[4];
 const int flags[4]={17,19,16,18};
 const int positions[4]={0,8,4,12};
 for(int i=0;i<4;i++)if(IntAt(work,flags[i])!=0){
  int p=positions[i];point[0]=a[p]-root[0];point[1]=a[p+1]-root[1];point[2]=a[p+2]-root[2];
  EvaluateSkirtCollider(work,point,a[(i==0||i==2)?24:25],a[26],a[27],center,root);
 }
}
// Restored from observed function RVA 0x72f0.
static void CollideBone(longlong param_1,longlong param_2,longlong param_3,float *param_4)

{
  longlong lVar1;
  float *pfVar2;
  int iVar3;
  float fVar4;
  float fVar5;
  float fVar6;
  float fVar7;
  float fVar8;
  float fVar9;
  float fVar10;
  float fVar11;
  float fVar12;
  float fVar13;
  float fVar14;
  float fVar15;
  float fVar16;
  float fVar17;
  float fVar18;
  float fVar19;
  float fVar20;
  float fVar21;
  float fVar22;
  float fVar23;
  
  iVar3 = 0;
  if (0 < *(int *)(param_1 + 0xf0)) {
    do {
      lVar1 = 0xf4;
      if (7 < iVar3) {
        lVar1 = 0x144;
      }
      lVar1 = (longlong)*(short *)(lVar1 + param_1 + (longlong)iVar3 * 2) * 0x4c;
      if ((*(int *)(lVar1 + 0x44 + param_2) != 0) &&
         ((*(int *)(param_1 + 0xe4) != 0 || (*(int *)(lVar1 + 0x48 + param_2) == 0)))) {
        pfVar2 = (float *)(lVar1 + param_2);
        int collisionType=IntAt(pfVar2,0xc);
        if (collisionType == 0) {
          fVar5 = *pfVar2;
          fVar6 = pfVar2[1];
          fVar10 = pfVar2[2];
          lVar1 = (longlong)IntAt(pfVar2,0x10) * 0x20;
          fVar12 = *(float *)(lVar1 + 0x1c + param_3);
          fVar15 = *(float *)(lVar1 + 0x10 + param_3);
          fVar9 = *(float *)(lVar1 + 0x14 + param_3);
          fVar18 = *(float *)(lVar1 + 0x18 + param_3);
          fVar11 = -fVar15;
          fVar7 = -fVar9;
          fVar8 = -fVar18;
          fVar4 = fVar12 * fVar5 + fVar15 * 0.0f + fVar9 * fVar10 + fVar8 * fVar6;
          fVar19 = fVar12 * fVar6 + fVar9 * 0.0f + fVar18 * fVar5 + fVar11 * fVar10;
          fVar15 = fVar12 * fVar10 + fVar18 * 0.0f + fVar15 * fVar6 + fVar7 * fVar5;
          fVar5 = fVar11 * fVar5 + fVar12 * 0.0f + fVar7 * fVar6 + fVar8 * fVar10;
          fVar9 = fVar5 * fVar11 + fVar4 * fVar12 + fVar19 * fVar8 + -fVar15 * fVar7 +
                  *(float *)(lVar1 + param_3);
          fVar10 = *param_4 - fVar9;
          fVar6 = fVar5 * fVar7 + fVar19 * fVar12 + fVar15 * fVar11 + -fVar4 * fVar8 +
                  *(float *)(lVar1 + 4 + param_3);
          fVar18 = param_4[1] - fVar6;
          fVar15 = fVar5 * fVar8 + fVar15 * fVar12 + fVar4 * fVar7 + -fVar19 * fVar11 +
                   *(float *)(lVar1 + 8 + param_3);
          fVar12 = param_4[2] - fVar15;
          fVar5 = fVar18 * fVar18 + fVar10 * fVar10 + fVar12 * fVar12;
          if (IntAt(pfVar2,0xd) == 0) {
            fVar19 = pfVar2[0xe] + *(float *)(param_1 + 0xcc);
            if (fVar5 <= fVar19 * fVar19) {
              if (fVar5 < 0.0f) {
                fVar5 = Root(fVar5);
              }
              else {
                fVar5 = SQRT(fVar5);
              }
              fVar9 = (fVar10 / fVar5) * fVar19 + fVar9;
              fVar6 = (fVar18 / fVar5) * fVar19 + fVar6;
              fVar15 = (fVar12 / fVar5) * fVar19 + fVar15;
              *param_4 = fVar9;
              param_4[1] = fVar6;
              param_4[2] = fVar15;
              fVar6 = fVar6 - *(float *)(param_1 + 0x94);
              fVar15 = fVar15 - *(float *)(param_1 + 0x98);
              fVar5 = *(float *)(param_1 + 0x90);
              fVar9 = fVar9 - fVar5;
              fVar10 = fVar9 * fVar9 + fVar6 * fVar6 + fVar15 * fVar15;
              if (fVar10 < 0.0f) goto LAB_180007fb7;
              fVar10 = SQRT(fVar10);
              goto LAB_180007fbf;
            }
          }
          else {
            fVar19 = pfVar2[0xe] - *(float *)(param_1 + 0xcc);
            if (fVar19 * fVar19 <= fVar5) {
              if (fVar5 < 0.0f) {
                fVar5 = Root(fVar5);
              }
              else {
                fVar5 = SQRT(fVar5);
              }
              fVar6 = (fVar18 / fVar5) * fVar19 + fVar6;
              fVar9 = (fVar10 / fVar5) * fVar19 + fVar9;
              fVar15 = (fVar12 / fVar5) * fVar19 + fVar15;
              param_4[1] = fVar6;
              *param_4 = fVar9;
              param_4[2] = fVar15;
              fVar6 = fVar6 - *(float *)(param_1 + 0x94);
              fVar15 = fVar15 - *(float *)(param_1 + 0x98);
              fVar5 = *(float *)(param_1 + 0x90);
              fVar9 = fVar9 - fVar5;
              fVar10 = fVar6 * fVar6 + fVar9 * fVar9 + fVar15 * fVar15;
              if (fVar10 < 0.0f) {
LAB_180007fb7:
                fVar10 = Root(fVar10);
              }
              else {
                fVar10 = SQRT(fVar10);
              }
LAB_180007fbf:
              *param_4 = (fVar9 / fVar10) * *(float *)(param_1 + 0xc0) + fVar5;
              param_4[1] = (fVar6 / fVar10) * *(float *)(param_1 + 0xc0) +
                           *(float *)(param_1 + 0x94);
              param_4[2] = (fVar15 / fVar10) * *(float *)(param_1 + 0xc0) +
                           *(float *)(param_1 + 0x98);
            }
          }
        }
        else if (collisionType == 2) {
          fVar5 = *pfVar2;
          fVar6 = pfVar2[2];
          fVar10 = pfVar2[1];
          lVar1 = (longlong)IntAt(pfVar2,0x10) * 0x20;
          fVar12 = *(float *)(lVar1 + 0x1c + param_3);
          fVar15 = *(float *)(lVar1 + 0x10 + param_3);
          fVar9 = *(float *)(lVar1 + 0x14 + param_3);
          fVar18 = *(float *)(lVar1 + 0x18 + param_3);
          fVar7 = -fVar15;
          fVar21 = -fVar9;
          fVar4 = -fVar18;
          fVar13 = fVar12 * fVar5 + fVar15 * 0.0f + fVar9 * fVar6 + fVar4 * fVar10;
          fVar11 = fVar12 * fVar10 + fVar9 * 0.0f + fVar18 * fVar5 + fVar7 * fVar6;
          fVar8 = fVar12 * fVar6 + fVar18 * 0.0f + fVar15 * fVar10 + fVar21 * fVar5;
          fVar16 = fVar7 * fVar5 + fVar12 * 0.0f + fVar21 * fVar10 + fVar4 * fVar6;
          fVar17 = fVar16 * fVar7 + fVar13 * fVar12 + fVar11 * fVar4 + -fVar8 * fVar21 +
                   *(float *)(lVar1 + param_3);
          fVar5 = pfVar2[6];
          fVar6 = pfVar2[5];
          fVar23 = fVar16 * fVar21 + fVar11 * fVar12 + fVar8 * fVar7 + -fVar13 * fVar4 +
                   *(float *)(lVar1 + 4 + param_3);
          fVar10 = *(float *)(lVar1 + 0x1c + param_3);
          fVar19 = pfVar2[4];
          fVar8 = fVar16 * fVar4 + fVar8 * *(float *)(lVar1 + 0x1c + param_3) + fVar13 * fVar21 +
                  -fVar11 * fVar7 + *(float *)(lVar1 + 8 + param_3);
          fVar11 = fVar19 * fVar10 + fVar15 * 0.0f + fVar5 * fVar9 + fVar6 * fVar4;
          fVar9 = fVar6 * fVar10 + fVar9 * 0.0f + fVar19 * fVar18 + fVar5 * fVar7;
          fVar15 = fVar5 * fVar10 + fVar18 * 0.0f + fVar6 * *(float *)(lVar1 + 0x10 + param_3) +
                   fVar19 * fVar21;
          fVar13 = pfVar2[0xe] + *(float *)(param_1 + 0xcc);
          fVar5 = fVar19 * fVar7 + fVar12 * 0.0f + fVar6 * fVar21 + fVar5 * fVar4;
          fVar6 = fVar5 * fVar21 + fVar9 * fVar10 + fVar15 * fVar7 + -fVar11 * fVar4 +
                  *(float *)(lVar1 + 4 + param_3);
          fVar12 = fVar6 - fVar23;
          fVar18 = fVar5 * fVar7 + fVar11 * fVar10 + fVar9 * fVar4 + -fVar15 * fVar21 +
                   *(float *)(lVar1 + param_3);
          fVar19 = fVar18 - fVar17;
          fVar15 = fVar5 * fVar4 + fVar15 * fVar10 + fVar11 * fVar21 + -fVar9 * fVar7 +
                   *(float *)(lVar1 + 8 + param_3);
          fVar10 = fVar15 - fVar8;
          fVar5 = fVar19 * fVar19 + fVar12 * fVar12 + fVar10 * fVar10;
          if (fVar5 < 0.0f) {
            fVar5 = Root(fVar5);
          }
          else {
            fVar5 = SQRT(fVar5);
          }
          fVar9 = *param_4;
          fVar4 = param_4[1];
          fVar16 = fVar9 - fVar17;
          fVar21 = fVar4 - fVar23;
          fVar7 = param_4[2];
          fVar22 = fVar7 - fVar8;
          fVar11 = fVar21 * (fVar12 / fVar5) + fVar16 * (fVar19 / fVar5) + fVar22 * (fVar10 / fVar5)
          ;
          if ((0.0f <= fVar11) && (fVar11 < fVar5)) {
            fVar14 = fVar11 * (fVar19 / fVar5);
            fVar20 = fVar11 * (fVar12 / fVar5);
            fVar11 = fVar11 * (fVar10 / fVar5);
            fVar10 = fVar16 - fVar14;
            fVar12 = fVar21 - fVar20;
            fVar19 = fVar22 - fVar11;
            fVar5 = fVar12 * fVar12 + fVar10 * fVar10 + fVar19 * fVar19;
            if (fVar5 < 0.0f) {
              fVar5 = Root(fVar5);
            }
            else {
              fVar5 = SQRT(fVar5);
            }
            if (fVar5 < fVar13) {
              fVar13 = fVar13 / fVar5;
              fVar15 = fVar14 + fVar17 + fVar10 * fVar13;
              fVar12 = fVar20 + fVar23 + fVar12 * fVar13;
              fVar10 = fVar11 + fVar8 + fVar19 * fVar13;
              *param_4 = fVar15;
              param_4[1] = fVar12;
              param_4[2] = fVar10;
              fVar12 = fVar12 - *(float *)(param_1 + 0x94);
              fVar10 = fVar10 - *(float *)(param_1 + 0x98);
              fVar5 = *(float *)(param_1 + 0x90);
              fVar15 = fVar15 - fVar5;
              fVar6 = fVar12 * fVar12 + fVar15 * fVar15 + fVar10 * fVar10;
              if (fVar6 < 0.0f) {
                fVar6 = Root(fVar6);
              }
              else {
                fVar6 = SQRT(fVar6);
              }
              *param_4 = (fVar15 / fVar6) * *(float *)(param_1 + 0xc0) + fVar5;
              param_4[1] = (fVar12 / fVar6) * *(float *)(param_1 + 0xc0) +
                           *(float *)(param_1 + 0x94);
              param_4[2] = (fVar10 / fVar6) * *(float *)(param_1 + 0xc0) +
                           *(float *)(param_1 + 0x98);
              goto LAB_180008016;
            }
          }
          fVar5 = fVar21 * fVar21 + fVar16 * fVar16 + fVar22 * fVar22;
          if (fVar13 * fVar13 < fVar5) {
            fVar4 = fVar4 - fVar6;
            fVar9 = fVar9 - fVar18;
            fVar7 = fVar7 - fVar15;
            fVar5 = fVar9 * fVar9 + fVar4 * fVar4 + fVar7 * fVar7;
            if (fVar5 <= fVar13 * fVar13) {
              if (fVar5 < 0.0f) {
                fVar5 = Root(fVar5);
              }
              else {
                fVar5 = SQRT(fVar5);
              }
              *param_4 = (fVar9 / fVar5) * fVar13 + fVar18;
              param_4[1] = (fVar4 / fVar5) * fVar13 + fVar6;
              param_4[2] = (fVar7 / fVar5) * fVar13 + fVar15;
            }
          }
          else {
            if (fVar5 < 0.0f) {
              fVar5 = Root(fVar5);
            }
            else {
              fVar5 = SQRT(fVar5);
            }
            *param_4 = (fVar16 / fVar5) * fVar13 + fVar17;
            param_4[1] = (fVar21 / fVar5) * fVar13 + fVar23;
            param_4[2] = (fVar22 / fVar5) * fVar13 + fVar8;
          }
        }
        else if (collisionType == 3) {
          fVar5 = (param_4[1] * pfVar2[9] + pfVar2[8] * *param_4 + param_4[2] * pfVar2[10]) -
                  pfVar2[0xf];
          if (fVar5 <= *(float *)(param_1 + 0xcc)) {
            fVar5 = *(float *)(param_1 + 0xcc) - fVar5;
            fVar12 = pfVar2[8] * fVar5 + *param_4;
            *param_4 = fVar12;
            fVar15 = fVar5 * pfVar2[9] + param_4[1];
            param_4[1] = fVar15;
            fVar10 = fVar5 * pfVar2[10] + param_4[2];
            param_4[2] = fVar10;
            fVar15 = fVar15 - *(float *)(param_1 + 0x94);
            fVar10 = fVar10 - *(float *)(param_1 + 0x98);
            fVar5 = *(float *)(param_1 + 0x90);
            fVar12 = fVar12 - fVar5;
            fVar6 = fVar12 * fVar12 + fVar15 * fVar15 + fVar10 * fVar10;
            if (fVar6 < 0.0f) {
              fVar6 = Root(fVar6);
            }
            else {
              fVar6 = SQRT(fVar6);
            }
            *param_4 = (fVar12 / fVar6) * *(float *)(param_1 + 0xc0) + fVar5;
            param_4[1] = (fVar15 / fVar6) * *(float *)(param_1 + 0xc0) + *(float *)(param_1 + 0x94);
            param_4[2] = (fVar10 / fVar6) * *(float *)(param_1 + 0xc0) + *(float *)(param_1 + 0x98);
          }
        }
      }
LAB_180008016:
      iVar3 = iVar3 + 1;
    } while (iVar3 < *(int *)(param_1 + 0xf0));
  }
  return;
}

// Restored from observed function RVA 0xb670.
static void EvaluateSkirtCollider(float *param_1,float *param_2,float param_3,float param_4,float param_5,
                  float *param_6,float *param_7)

{
  float fVar1;
  double dVar2;
  double dVar3;
  float fVar4;
  float fVar5;
  float fVar6;
  float fVar7;
  float fVar8;
  float fVar9;
  float fVar10;
  float fVar11;
  float fVar12;
  float fVar13;
  float fVar14;
  float fVar15;
  float fVar16;
  float fVar17;
  float fVar18;
  float fVar19;
  float fVar20;
  float fVar21;
  float fVar22;
  float fVar23;
  
  fVar13 = param_1[2] - param_7[2];
  fVar10 = param_1[1] - param_7[1];
  fVar5 = param_1[5];
  fVar22 = param_1[4];
  fVar23 = *param_7;
  fVar6 = param_7[1];
  fVar8 = param_1[6];
  fVar18 = param_2[2] - fVar13;
  fVar9 = param_7[2];
  fVar12 = param_2[1] - fVar10;
  fVar7 = *param_1 - *param_7;
  fVar16 = *param_2 - fVar7;
  fVar17 = (param_2[1] - (fVar5 - fVar6)) * param_1[9] + (*param_2 - (fVar22 - fVar23)) * param_1[8]
           + (param_2[2] - (fVar8 - fVar9)) * param_1[10] + param_3;
  fVar14 = fVar12 * fVar12 + fVar16 * fVar16 + fVar18 * fVar18;
  if (fVar14 < 0.0f) {
    fVar19 = Root(fVar14);
  }
  else {
    fVar19 = SQRT(fVar14);
  }
  fVar14 = fVar14 - param_3 * param_3;
  if (fVar14 < 0.0f) {
    fVar14 = Root(fVar14);
  }
  else {
    fVar14 = SQRT(fVar14);
  }
  fVar1 = (fVar14 / fVar19) * fVar14;
  fVar14 = (param_3 / fVar19) * fVar14;
  fVar20 = param_1[0xd] * fVar18 - param_1[0xe] * fVar12;
  fVar15 = param_1[0xe] * fVar16 - param_1[0xc] * fVar18;
  fVar21 = param_1[0xc] * fVar12 - param_1[0xd] * fVar16;
  fVar4 = fVar15 * fVar15 + fVar20 * fVar20 + fVar21 * fVar21;
  if (fVar4 < 0.0f) {
    fVar4 = Root(fVar4);
  }
  else {
    fVar4 = SQRT(fVar4);
  }
  fVar8 = (fVar8 - fVar9) - fVar13;
  fVar22 = (fVar22 - fVar23) - fVar7;
  fVar23 = (fVar5 - fVar6) - fVar10;
  fVar5 = fVar23 * fVar23 + fVar22 * fVar22 + fVar8 * fVar8;
  if (fVar5 < 0.0f) {
    fVar5 = Root(fVar5);
  }
  else {
    fVar5 = SQRT(fVar5);
  }
  fVar11 = fVar23 / fVar5;
  fVar9 = fVar8 / fVar5;
  fVar5 = fVar22 / fVar5;
  dVar3 = 0;
  fVar6 = fVar11 * fVar11 + fVar5 * fVar5 + fVar9 * fVar9;
  fVar14 = ((fVar12 / fVar19) * fVar1 + (fVar15 / fVar4) * fVar14) * fVar11 +
           ((fVar16 / fVar19) * fVar1 + (fVar20 / fVar4) * fVar14) * fVar5 +
           ((fVar18 / fVar19) * fVar1 + (fVar21 / fVar4) * fVar14) * fVar9;
  if (fVar6 < 0.0f) {
    fVar6 = Root(fVar6);
  }
  else {
    fVar6 = SQRT(fVar6);
  }
  // Native JBE takes the evaluation path on unordered comparisons too.
  if (!(fVar6 < 1.1920929e-07f)) {
    fVar18 = (fVar14 * fVar5 + fVar7 + fVar17 * param_1[8]) - fVar7;
    fVar16 = (fVar14 * fVar11 + fVar10 + fVar17 * param_1[9]) - fVar10;
    fVar12 = (fVar14 * fVar9 + fVar13 + fVar17 * param_1[10]) - fVar13;
    fVar14 = fVar16 * fVar16 + fVar18 * fVar18 + fVar12 * fVar12;
    if (fVar14 < 0.0f) {
      fVar14 = Root(fVar14);
    }
    else {
      fVar14 = SQRT(fVar14);
    }
    dVar3 = 0;
    if (!(fVar14 < 1.1920929e-07f)) {
      dVar2 = (double)RefAcos((double)(((fVar16 * fVar11 + fVar18 * fVar5 +
                                                    fVar12 * fVar9) / fVar6) / fVar14));
      fVar5 = (float)((dVar2 * 360.0f) / 6.283185307179586);
    }
    else {
      fVar5 = 0.0f;
    }
  }
  else {
    fVar5 = 0.0f;
  }
  if (!(fVar17 < 0.0f)) {
    dVar2 = (double)(float)(fVar5 * 0.0174532924 * 0.5);
    dVar3 = RefSin(dVar2,dVar3);
    fVar19 = (float)((double)param_1[0xc] * dVar3);
    fVar14 = (float)((double)param_1[0xd] * dVar3);
    fVar16 = (float)((double)param_1[0xe] * dVar3);
    dVar3 = RefCos(dVar2,(double)param_1[0xe] * dVar3);
    fVar18 = (float)dVar3;
    fVar6 = -fVar14;
    fVar12 = -fVar19;
    fVar9 = -fVar16;
    fVar17 = fVar18 * fVar22 + fVar19 * 0.0f + fVar14 * fVar8 + fVar9 * fVar23;
    fVar14 = fVar18 * fVar23 + fVar14 * 0.0f + fVar16 * fVar22 + fVar12 * fVar8;
    fVar16 = fVar18 * fVar8 + fVar16 * 0.0f + fVar19 * fVar23 + fVar6 * fVar22;
    fVar8 = fVar12 * fVar22 + fVar18 * 0.0f + fVar6 * fVar23 + fVar9 * fVar8;
    fVar1 = *param_2 - *param_6;
    fVar19 = param_2[1] - param_6[1];
    fVar23 = param_2[2] - param_6[2];
    fVar22 = fVar19 * fVar19 + fVar1 * fVar1 + fVar23 * fVar23;
    if (fVar22 < 0.0f) {
      fVar22 = Root(fVar22);
    }
    else {
      fVar22 = SQRT(fVar22);
    }
    fVar4 = (fVar8 * fVar12 + fVar17 * fVar18 + fVar14 * fVar9 + -fVar16 * fVar6 + fVar7) - *param_6
    ;
    fVar7 = (fVar8 * fVar6 + fVar14 * fVar18 + fVar16 * fVar12 + -fVar17 * fVar9 + fVar10) -
            param_6[1];
    fVar8 = (fVar8 * fVar9 + fVar16 * fVar18 + fVar17 * fVar6 + -fVar14 * fVar12 + fVar13) -
            param_6[2];
    fVar6 = fVar7 * fVar7 + fVar4 * fVar4 + fVar8 * fVar8;
    if (fVar6 < 0.0f) {
      fVar6 = Root(fVar6);
    }
    else {
      fVar6 = SQRT(fVar6);
    }
    dVar3 = (double)RefAcos((double)((fVar7 / fVar6) * (fVar19 / fVar22) +
                                                 (fVar4 / fVar6) * (fVar1 / fVar22) +
                                                (fVar8 / fVar6) * (fVar23 / fVar22)));
    fVar22 = (float)(dVar3 * 57.29578);
    if (param_5 < fVar22) {
      if (!(param_1[0x14] < 0.0f)) {
        return;
      }
      param_1[0x14] = 0.0f;
      return;
    }
    if (!(fVar22 > param_4)) {
      fVar22 = 1.0f;
    }
    else {
      fVar22 = 1.0f - (fVar22 - param_4) / (param_5 - param_4);
    }
    fVar5 = fVar5 * fVar22;
  }
  else {
    fVar5 = fVar5 * -1.0f;
  }
  if (param_1[0x14] <= fVar5 && fVar5 != param_1[0x14]) {
    param_1[0x14] = fVar5;
  }
  return;
}

// Restored from observed function RVA 0x9760.
static void IntegrateBone(float *param_1,longlong param_2,longlong param_3,float param_4,float param_5,
                  float param_6,float param_7,float param_8,float param_9,float param_10,
                  float param_11,char param_12,char param_13,float param_14,float param_15,
                  float param_16)

{
  double dVar1;
  double dVar2;
  double dVar3;
  double dVar4;
  float *pfVar5;
  double dVar6;
  float fVar7;
  float fVar8;
  float fVar9;
  float fVar10;
  float fVar11;
  float fVar12;
  float fVar13;
  float fVar14;
  float fVar15;
  float fVar16;
  float fVar17;
  float fVar18;
  float fVar19;
  float fVar20;
  float fVar21;
  float fVar22;
  float local_f8;
  float local_f4;
  float pose[4]={};
  float local_d8;
  float local_d4;
  float local_d0 [46];
  
  if (IntAt(param_1,0x3a) == 0) {
    local_f8 = param_1[0x15] - param_1[0x11];
    local_f4 = param_1[0x16] - param_1[0x12];
    fVar22 = param_1[0x14] - param_1[0x10];
    if (param_13 == '\0') {
      fVar22 = fVar22 * 0.5f;
      local_f8 = local_f8 * 0.5f;
      local_f4 = local_f4 * 0.5f;
    }
    *(undefined8 *)(param_1 + 0x14) = *(undefined8 *)(param_1 + 0x10);
    *(undefined8 *)(param_1 + 0x16) = *(undefined8 *)(param_1 + 0x12);
    fVar8 = param_1[3];
    fVar9 = param_1[7];
    fVar10 = param_1[2];
    fVar12 = param_1[1];
    fVar15 = *param_1;
    fVar20 = param_1[4];
    fVar21 = param_1[6];
    fVar7 = param_1[5];
    fVar19 = fVar8 * fVar20 + fVar15 * fVar9 + fVar10 * fVar7 + fVar12 * -fVar21;
    fVar14 = param_1[0xd];
    fVar17 = fVar8 * fVar7 + fVar12 * fVar9 + fVar15 * fVar21 + fVar10 * -fVar20;
    fVar11 = param_1[0xe];
    fVar13 = param_1[0xc];
    local_d8 = fVar10 * fVar9 + fVar8 * fVar21 + fVar12 * fVar20 + fVar15 * -fVar7;
    fVar16 = -fVar17;
    fVar18 = -fVar19;
    fVar20 = fVar15 * -fVar20 + fVar8 * fVar9 + fVar12 * -fVar7 + fVar10 * -fVar21;
    fVar12 = -local_d8;
    fVar15 = fVar13 * fVar20 + fVar19 * 0.0f + fVar11 * fVar17 + fVar12 * fVar14;
    fVar10 = fVar14 * fVar20 + fVar17 * 0.0f + fVar13 * local_d8 + fVar18 * fVar11;
    fVar9 = fVar11 * fVar20 + local_d8 * 0.0f + fVar14 * fVar19 + fVar16 * fVar13;
    fVar8 = fVar18 * fVar13 + fVar20 * 0.0f + fVar16 * fVar14 + fVar12 * fVar11;
    fVar21 = fVar8 * fVar18 + fVar15 * fVar20 + fVar10 * fVar12 + -fVar9 * fVar16;
    param_1[0x1c] = fVar21;
    param_1[0x1d] = fVar8 * fVar16 + fVar10 * fVar20 + fVar9 * fVar18 + -fVar15 * fVar12;
    param_1[0x1e] = fVar8 * fVar12 + fVar9 * fVar20 + fVar15 * fVar16 + -fVar10 * fVar18;
    fVar15 = ((param_4 * param_1[0x31]) / 1000.0f) / 30.0f;
    fVar12 = ((param_5 * param_1[0x32]) / 100.0f) / 30.0f;
    fVar9 = (param_1[0x38] - param_1[0x37]) * param_10 + param_1[0x37];
    fVar10 = fVar15 * fVar21 + fVar12 * fVar22 + fVar9 * param_7 + param_1[0x51] / 30.0f;
    param_1[0x18] = fVar10;
    fVar8 = ((fVar15 * param_1[0x1d] + fVar12 * local_f8) -
            ((param_6 * param_1[0x34]) / 10000.0f) / 30.0f) +
            ((param_1[0x36] - param_1[0x35]) * param_10 + param_1[0x35]) * param_8 +
            param_1[0x52] / 30.0f;
    param_1[0x19] = fVar8;
    fVar9 = fVar15 * param_1[0x1e] + fVar12 * local_f4 + fVar9 * param_9 + param_1[0x53] / 30.0f;
    param_1[0x1a] = fVar9;
    if (param_13 == '\0') {
      fVar10 = (double(param_1[0x10]) - (double(fVar22) + double(fVar22)) * double(param_11)) + double(fVar10) * 3.0 * double(param_11);
      fVar8 = (double(param_1[0x11]) - (double(local_f8) + double(local_f8)) * double(param_11)) + double(fVar8) * 3.0 * double(param_11);
      fVar22 = (double(param_1[0x12]) - (double(local_f4) + double(local_f4)) * double(param_11)) + double(fVar9) * 3.0 * double(param_11);
    }
    else {
      fVar10 = (param_1[0x10] - fVar22 * param_11) + fVar10 * 1.5 * param_11;
      fVar8 = (param_1[0x11] - local_f8 * param_11) + fVar8 * 1.5 * param_11;
      fVar22 = (param_1[0x12] - local_f4 * param_11) + fVar9 * 1.5 * param_11;
    }
    if (IntAt(param_1,0x59) != 0) {
      param_14 = param_15;
    }
    if (0.0f <= param_14) {
      param_16 = ((1.0f - param_1[0x4f]) * param_14 + param_1[0x4f]) * param_16;
    }
    local_d4 = param_4;
    if (param_13 != '\0') {
      if (param_16 < 0.0f) {
        param_16 = Root(param_16);
      }
      else {
        param_16 = SQRT(param_16);
      }
    }
    if (param_16 < 1.0f) {
      fVar9 = param_1[0x1d];
      fVar12 = param_1[0x1e];
      fVar15 = fVar9 * fVar9 + fVar21 * fVar21 + fVar12 * fVar12;
      if (fVar15 < 0.0f) {
        fVar15 = Root(fVar15);
      }
      else {
        fVar15 = SQRT(fVar15);
      }
      fVar7 = param_1[0x30];
      fVar21 = (fVar21 / fVar15) * fVar7 + param_1[0x24];
      fVar9 = (fVar9 / fVar15) * fVar7 + param_1[0x25];
      pose[3] = local_d4;
      fVar12 = (fVar12 / fVar15) * fVar7 + param_1[0x26];
      fVar10 = (fVar10 - fVar21) * param_16 + fVar21;
      fVar8 = (fVar8 - fVar9) * param_16 + fVar9;
      fVar22 = (fVar22 - fVar12) * param_16 + fVar12;
    }
    fVar8 = fVar8 - param_1[0x25];
    fVar22 = fVar22 - param_1[0x26];
    fVar9 = param_1[0x24];
    fVar10 = fVar10 - fVar9;
    fVar12 = fVar8 * fVar8 + fVar10 * fVar10 + fVar22 * fVar22;
    if (fVar12 < 0.0f) {
      fVar12 = Root(fVar12);
    }
    else {
      fVar12 = SQRT(fVar12);
    }
    fVar15 = param_1[0x30];
    pose[0] = (fVar10 / fVar12) * fVar15 + fVar9;
    pose[1] = (fVar8 / fVar12) * fVar15 + param_1[0x25];
    pose[2] = (fVar22 / fVar12) * fVar15 + param_1[0x26];
    if ((0 < IntAt(param_1,0x3c)) && (param_12 != '\0')) {
      CollideBone((longlong)param_1,param_2,param_3,&pose[0]);
      fVar9 = param_1[0x24];
    }
    if (IntAt(param_1,0x4e) != 0) {
      fVar22 = (pose[1] - param_1[0x4b]) * param_1[0x47] +
               (pose[0] - param_1[0x4a]) * param_1[0x46] +
               (pose[2] - param_1[0x4c]) * param_1[0x48];
      if (fVar22 < param_1[0x33]) {
        fVar22 = param_1[0x33] - fVar22;
        fVar8 = (param_1[0x46] * fVar22 + pose[0]) - fVar9;
        fVar10 = (fVar22 * param_1[0x47] + pose[1]) - param_1[0x25];
        fVar12 = (fVar22 * param_1[0x48] + pose[2]) - param_1[0x26];
        fVar22 = fVar10 * fVar10 + fVar8 * fVar8 + fVar12 * fVar12;
        if (fVar22 < 0.0f) {
          fVar22 = Root(fVar22);
        }
        else {
          fVar22 = SQRT(fVar22);
        }
        fVar15 = param_1[0x30];
        pose[0] = (fVar8 / fVar22) * fVar15 + fVar9;
        pose[1] = (fVar10 / fVar22) * fVar15 + param_1[0x25];
        pose[2] = (fVar12 / fVar22) * fVar15 + param_1[0x26];
      }
    }
    fVar9 = pose[0] - fVar9;
    param_1[0x20] = fVar9;
    fVar8 = pose[1] - param_1[0x25];
    param_1[0x21] = fVar8;
    fVar10 = pose[2] - param_1[0x26];
    fVar22 = fVar8 * fVar8 + fVar9 * fVar9 + fVar10 * fVar10;
    if (fVar22 < 0.0f) {
      fVar22 = Root(fVar22);
    }
    else {
      fVar22 = SQRT(fVar22);
    }
    fVar8 = fVar8 / fVar22;
    fVar10 = fVar10 / fVar22;
    fVar9 = fVar9 / fVar22;
    param_1[0x21] = fVar8;
    param_1[0x20] = fVar9;
    param_1[0x22] = fVar10;
    fVar22 = param_1[0x1d];
    fVar12 = param_1[0x1c];
    fVar15 = param_1[0x1e];
    fVar21 = fVar22 * fVar8 + fVar12 * fVar9 + fVar15 * fVar10 + 1.0f;
    fVar21 = fVar21 + fVar21;
    if (fVar21 < 0.0f) {
      fVar21 = Root(fVar21);
    }
    else {
      fVar21 = SQRT(fVar21);
    }
    fVar7 = (fVar22 * fVar10 - fVar15 * fVar8) / fVar21;
    fVar10 = (fVar15 * fVar9 - fVar12 * fVar10) / fVar21;
    fVar8 = (fVar12 * fVar8 - fVar22 * fVar9) / fVar21;
    fVar21 = fVar21 * 0.5f;
    fVar9 = fVar17 * fVar21 + fVar10 * fVar20 + fVar8 * fVar19 + local_d8 * -fVar7;
    fVar12 = local_d8 * fVar21 + fVar8 * fVar20 + fVar7 * fVar17 + fVar19 * -fVar10;
    fVar22 = fVar19 * fVar21 + fVar7 * fVar20 + fVar10 * local_d8 + fVar17 * -fVar8;
    fVar8 = fVar19 * -fVar7 + fVar20 * fVar21 + fVar17 * -fVar10 + local_d8 * -fVar8;
    if (IntAt(param_1,0x3b) == 0) {
      param_1[8] = fVar22;
      param_1[9] = fVar9;
      param_1[10] = fVar12;
      param_1[0xb] = fVar8;
      *(ulonglong *)(param_1 + 0x10) = CONCAT44(pose[1],pose[0]);
      *(ulonglong *)(param_1 + 0x12) = CONCAT44(pose[3],pose[2]);
    }
    else {
      fVar10 = param_1[7];
      fVar15 = param_1[4];
      fVar20 = param_1[6];
      fVar11 = -param_1[5];
      fVar18 = fVar22 * fVar10 + -fVar15 * fVar8 + fVar11 * fVar12 + fVar9 * fVar20;
      fVar21 = param_1[2];
      fVar7 = *param_1;
      fVar17 = fVar9 * fVar10 + fVar11 * fVar8 + -fVar20 * fVar22 + fVar12 * fVar15;
      fVar14 = param_1[3];
      fVar16 = fVar12 * fVar10 + -fVar20 * fVar8 + -fVar15 * fVar9 + fVar22 * -fVar11;
      fVar13 = -param_1[1];
      fVar22 = fVar8 * fVar10 + fVar22 * fVar15 + fVar9 * -fVar11 + fVar12 * fVar20;
      pose[0] = -fVar7 * fVar22 + fVar14 * fVar18 + fVar13 * fVar16 + fVar21 * fVar17;
      pose[1] = fVar13 * fVar22 + fVar14 * fVar17 + -fVar21 * fVar18 + fVar7 * fVar16;
      pose[2] = -fVar21 * fVar22 + fVar14 * fVar16 + -fVar7 * fVar17 + -fVar13 * fVar18;
      pose[3] = fVar14 * fVar22 + fVar7 * fVar18 + -fVar13 * fVar17 + fVar21 * fVar16;
      pfVar5 = QuaternionToEuler(local_d0,&pose[0]);
      dVar6 = (double)RefMod((double)*pfVar5 + 360.0f,360.0f);
      fVar22 = (float)dVar6;
      dVar6 = (double)RefMod((double)pfVar5[1] + 360.0f,360.0f);
      fVar8 = (float)dVar6;
      dVar6 = (double)RefMod((double)pfVar5[2] + 360.0f,360.0f);
      fVar9 = (float)dVar6;
      if (180.0f < fVar22) {
        fVar22 = fVar22 - 360.0f;
      }
      if (180.0f < fVar8) {
        fVar8 = fVar8 - 360.0f;
      }
      if (180.0f < fVar9) {
        fVar9 = fVar9 - 360.0f;
      }
      dVar6=0;
      fVar10 = ClampAngle(fVar22,param_1[0x28],param_1[0x2c]);
      fVar22 = ClampAngle(fVar8,param_1[0x29],param_1[0x2d]);
      fVar8 = ClampAngle(fVar9,param_1[0x2a],param_1[0x2e]);
      dVar3 = (double)(float)((((fVar8 + fVar8) * 3.141592653589793) / 360.0f) * 0.5f);
      dVar1 = RefSin(dVar3,dVar6);
      fVar20 = (float)dVar1;
      dVar1 = RefCos(dVar3,dVar6);
      fVar15 = (float)dVar1;
      dVar3 = (double)(float)((((fVar10 + fVar10) * 3.141592653589793) / 360.0f) * 0.5f);
      dVar1 = RefSin(dVar3,dVar6);
      dVar3 = RefCos(dVar3,dVar6);
      dVar4 = (double)(float)((((fVar22 + fVar22) * 3.141592653589793) / 360.0f) * 0.5f);
      dVar2 = RefSin(dVar4,dVar6);
      dVar6 = RefCos(dVar4,dVar6);
      fVar22 = param_1[2];
      fVar8 = *param_1;
      fVar9 = param_1[1];
      fVar7 = (float)dVar2 * (float)dVar3;
      fVar10 = (float)dVar2 * (float)dVar1;
      fVar21 = (float)dVar6 * (float)dVar3;
      fVar12 = (float)dVar6 * (float)dVar1;
      fVar14 = fVar21 * fVar15 + fVar10 * fVar20;
      fVar16 = fVar21 * fVar20 - fVar10 * fVar15;
      fVar10 = param_1[3];
      fVar17 = fVar7 * fVar15 - fVar12 * fVar20;
      fVar7 = fVar12 * fVar15 + fVar7 * fVar20;
      fVar18 = fVar10 * fVar7 + fVar8 * fVar14 + fVar9 * fVar16 + -fVar22 * fVar17;
      fVar12 = param_1[4];
      fVar15 = param_1[7];
      fVar13 = fVar10 * fVar17 + fVar9 * fVar14 + fVar22 * fVar7 + -fVar8 * fVar16;
      fVar20 = param_1[6];
      fVar21 = param_1[5];
      fVar11 = fVar10 * fVar16 + fVar22 * fVar14 + fVar8 * fVar17 + -fVar9 * fVar7;
      fVar22 = fVar10 * fVar14 + -fVar8 * fVar7 + -fVar9 * fVar17 + -fVar22 * fVar16;
      fVar17 = fVar15 * fVar22 + -fVar12 * fVar18 + -fVar21 * fVar13 + -fVar20 * fVar11;
      fVar10 = fVar21 * fVar22 + fVar15 * fVar13 + fVar20 * fVar18 + -fVar12 * fVar11;
      fVar16 = fVar20 * fVar22 + fVar15 * fVar11 + fVar12 * fVar13 + -fVar21 * fVar18;
      param_1[9] = fVar10;
      fVar7 = -fVar16;
      param_1[10] = fVar16;
      param_1[0xb] = fVar17;
      fVar14 = -fVar10;
      fVar21 = fVar12 * fVar22 + fVar15 * fVar18 + fVar21 * fVar11 + -fVar20 * fVar13;
      param_1[8] = fVar21;
      fVar22 = param_1[0xc];
      fVar8 = param_1[0xe];
      fVar20 = -fVar21;
      fVar9 = param_1[0xd];
      fVar15 = fVar17 * fVar22 + fVar21 * 0.0f + fVar10 * fVar8 + fVar7 * fVar9;
      fVar12 = fVar17 * fVar9 + fVar10 * 0.0f + fVar16 * fVar22 + fVar20 * fVar8;
      fVar10 = fVar17 * fVar8 + fVar16 * 0.0f + fVar21 * fVar9 + fVar14 * fVar22;
      fVar22 = fVar20 * fVar22 + fVar17 * 0.0f + fVar14 * fVar9 + fVar7 * fVar8;
      fVar9 = fVar22 * fVar20 + fVar15 * fVar17 + fVar12 * fVar7 + -fVar10 * fVar14;
      fVar21 = fVar22 * fVar14 + fVar12 * fVar17 + fVar10 * fVar20 + -fVar15 * fVar7;
      fVar8 = fVar22 * fVar7 + fVar10 * fVar17 + fVar15 * fVar14 + -fVar12 * fVar20;
      fVar22 = fVar21 * fVar21 + fVar9 * fVar9 + fVar8 * fVar8;
      if (fVar22 < 0.0f) {
        fVar22 = Root(fVar22);
      }
      else {
        fVar22 = SQRT(fVar22);
      }
      fVar10 = param_1[0x30];
      param_1[0x10] = (fVar9 / fVar22) * fVar10 + param_1[0x24];
      param_1[0x11] = (fVar21 / fVar22) * fVar10 + param_1[0x25];
      param_1[0x12] = (fVar8 / fVar22) * fVar10 + param_1[0x26];
    }
    fVar22 = param_1[0x41];
    if (fVar22 < 1.0f) {
      fVar8 = param_1[0x42];
      fVar9 = param_1[0x43];
      fVar10 = param_1[8];
      fVar12 = param_1[9];
      fVar15 = param_1[0x44];
      fVar20 = param_1[10];
      fVar21 = param_1[0x45];
      fVar7 = param_1[0xb];
      if (fVar9 * fVar12 + fVar8 * fVar10 + fVar15 * fVar20 + fVar21 * fVar7 < 0.0f) {
        fVar10 = -fVar10;
        fVar12 = -fVar12;
        fVar20 = -fVar20;
        fVar7 = -fVar7;
      }
      fVar9 = (fVar12 - fVar9) * fVar22 + fVar9;
      fVar8 = (fVar10 - fVar8) * fVar22 + fVar8;
      fVar15 = (fVar20 - fVar15) * fVar22 + fVar15;
      fVar21 = (fVar7 - fVar21) * fVar22 + fVar21;
      fVar22 = fVar9 * fVar9 + fVar8 * fVar8 + fVar15 * fVar15 + fVar21 * fVar21;
      if (fVar22 < 0.0f) {
        fVar22 = Root(fVar22);
      }
      else {
        fVar22 = SQRT(fVar22);
      }
      param_1[8] = fVar8 / fVar22;
      param_1[9] = fVar9 / fVar22;
      param_1[10] = fVar15 / fVar22;
      param_1[0xb] = fVar21 / fVar22;
    }
  }
  return;
}

// Restored from observed function RVA 0x9100.
static void SolveCloth(float *param_1,int param_2,longlong param_3,longlong param_4,float param_5,
                  float param_6,float param_7,float param_8,float param_9,float param_10,
                  float param_11,char param_12,float param_13,char param_14,float param_15,
                  float param_16,float param_17)

{
  float *pfVar1;
  float fVar2;
  float fVar3;
  float fVar4;
  float fVar5;
  float fVar6;
  float fVar7;
  float fVar8;
  float fVar9;
  undefined8 uVar10;
  ulonglong uVar11;
  longlong lVar12;
  uint uVar13;
  int iVar14;
  
  uVar13 = 0;
  if (0 < param_2) {
    do {
      uVar11 = (ulonglong)uVar13;
      uVar13 = uVar13 + 1;
      param_1[uVar11 * 0x5a + 0x4e] = 0.0f;
    } while ((int)uVar13 < param_2);
  }
  fVar2 = param_1[0x45];
  fVar3 = param_1[0x42];
  fVar4 = param_1[0x44];
  fVar5 = param_1[0x43];
  lVar12 = (longlong)IntAt(param_1,0x50) * 0x20;
  fVar6 = *(float *)(lVar12 + 0x1c + param_4);
  fVar7 = *(float *)(lVar12 + 0x10 + param_4);
  fVar8 = *(float *)(lVar12 + 0x14 + param_4);
  fVar9 = *(float *)(lVar12 + 0x18 + param_4);
  param_1[0x42] = fVar6 * fVar3 + fVar7 * fVar2 + fVar8 * fVar4 + -fVar9 * fVar5;
  param_1[0x43] = fVar8 * fVar2 + fVar6 * fVar5 + fVar9 * fVar3 + -fVar7 * fVar4;
  param_1[0x44] = fVar9 * fVar2 + fVar6 * fVar4 + fVar7 * fVar5 + -fVar8 * fVar3;
  param_1[0x45] = -fVar7 * fVar3 + fVar6 * fVar2 + -fVar8 * fVar5 + -fVar9 * fVar4;
  IntegrateBone(param_1,param_3,param_4,param_5,param_6,param_7,param_8,param_9,param_10,
                      param_11,param_13,param_12,param_14,param_15,param_16,param_17);
  iVar14 = 1;
  if (1 < param_2) {
    lVar12 = 1;
    do {
      pfVar1 = param_1 + lVar12 * 0x5a;
      uVar10 = *(undefined8 *)(param_1 + lVar12 * 0x5a + -0x4a + 2);
      *(undefined8 *)(pfVar1 + 0x24) = *(undefined8 *)(param_1 + lVar12 * 0x5a + -0x4a);
      *(undefined8 *)(pfVar1 + 0x26) = uVar10;
      uVar10 = *(undefined8 *)(param_1 + lVar12 * 0x5a + -0x52 + 2);
      *(undefined8 *)(pfVar1 + 4) = *(undefined8 *)(param_1 + lVar12 * 0x5a + -0x52);
      *(undefined8 *)(pfVar1 + 6) = uVar10;
      fVar2 = pfVar1[0x42];
      fVar3 = pfVar1[0x45];
      fVar4 = param_1[lVar12 * 0x5a + -0x15];
      fVar5 = pfVar1[0x44];
      fVar6 = pfVar1[0x43];
      fVar7 = param_1[lVar12 * 0x5a + -0x18];
      fVar8 = param_1[lVar12 * 0x5a + -0x17];
      fVar9 = param_1[lVar12 * 0x5a + -0x16];
      pfVar1[0x42] = fVar2 * fVar4 + fVar7 * fVar3 + fVar8 * fVar5 + fVar6 * -fVar9;
      pfVar1[0x43] = fVar3 * fVar8 + fVar4 * fVar6 + fVar2 * fVar9 + -fVar7 * fVar5;
      pfVar1[0x44] = fVar3 * fVar9 + fVar4 * fVar5 + fVar7 * fVar6 + fVar2 * -fVar8;
      pfVar1[0x45] = -fVar7 * fVar2 + fVar4 * fVar3 + -fVar8 * fVar6 + -fVar9 * fVar5;
      IntegrateBone(pfVar1,param_3,param_4,param_5,param_6,param_7,param_8,param_9,param_10,
                          param_11,param_13,param_12,param_14,param_15,param_16,param_17);
      iVar14 = iVar14 + 1;
      lVar12 = lVar12 + 1;
    } while (iVar14 < param_2);
  }
  return;
}

// Restored from observed function RVA 0x8740.
static void SolveClothSkirt(float *param_1,int param_2,longlong param_3,float *param_4,float *param_5,
                  longlong param_6,float param_7,float param_8,float param_9,float param_10,
                  float param_11,float param_12,float param_13,char param_14,float param_15,
                  char param_16,float param_17,float param_18,float param_19)

{
  float *pfVar1;
  float fVar2;
  float fVar3;
  double dVar4;
  double dVar5;
  undefined8 uVar6;
  ulonglong uVar7;
  longlong lVar8;
  uint uVar9;
  int iVar10;
  int iVar11;
  double in_XMM1_Qa=0;
  float fVar12;
  float fVar13;
  float fVar14;
  float fVar15;
  float fVar16;
  float fVar17;
  float fVar18;
  float fVar19;
  float fVar20;
  float fVar21;
  
  uVar9 = 0;
  if (0 < param_2) {
    do {
      uVar7 = (ulonglong)uVar9;
      uVar9 = uVar9 + 1;
      param_1[uVar7 * 0x5a + 0x4e] = 0.0f;
    } while ((int)uVar9 < param_2);
  }
  param_4[0x14] = -360.0f;
  EvaluateSkirt(param_4,param_5);
  iVar10 = 1;
  if (-360.0f < param_4[0x14]) {
    dVar5 = (double)(float)((param_4[0x14] - param_4[0x15]) * 0.0174532924 * 0.5);
    dVar4 = RefSin(dVar5,in_XMM1_Qa);
    fVar15 = (float)((double)param_4[0xc] * dVar4);
    fVar13 = (float)((double)param_4[0xd] * dVar4);
    fVar14 = (float)((double)param_4[0xe] * dVar4);
    dVar4 = RefCos(dVar5,(double)param_4[0xe] * dVar4);
    fVar12 = param_4[8];
    fVar2 = param_4[10];
    fVar3 = param_4[9];
    fVar20 = (float)dVar4;
    fVar21 = -fVar15;
    fVar17 = -fVar14;
    fVar19 = -fVar13;
    fVar18 = fVar12 * fVar20 + fVar15 * 0.0f + fVar2 * fVar13 + fVar17 * fVar3;
    fVar16 = fVar3 * fVar20 + fVar13 * 0.0f + fVar12 * fVar14 + fVar21 * fVar2;
    fVar13 = fVar2 * fVar20 + fVar14 * 0.0f + fVar3 * fVar15 + fVar19 * fVar12;
    fVar12 = fVar21 * fVar12 + fVar20 * 0.0f + fVar19 * fVar3 + fVar17 * fVar2;
    iVar11 = 1;
    param_1[0x46] = fVar12 * fVar21 + fVar18 * fVar20 + fVar16 * fVar17 + -fVar13 * fVar19;
    param_1[0x47] = fVar12 * fVar19 + fVar16 * fVar20 + fVar13 * fVar21 + -fVar18 * fVar17;
    param_1[0x48] = fVar12 * fVar17 + fVar13 * fVar20 + fVar18 * fVar19 + -fVar16 * fVar21;
    param_1[0x4a] = *param_4 - param_5[0x14];
    param_1[0x4b] = param_4[1] - param_5[0x15];
    param_1[0x4c] = param_4[2] - param_5[0x16];
    param_1[0x4e] = 1.4013e-45f;
    if (1 < param_2) {
      lVar8 = 1;
      do {
        uVar6 = *(undefined8 *)(param_1 + 0x48);
        iVar11 = iVar11 + 1;
        *(undefined8 *)(param_1 + lVar8 * 0x5a + 0x46) = *(undefined8 *)(param_1 + 0x46);
        *(undefined8 *)(param_1 + lVar8 * 0x5a + 0x46 + 2) = uVar6;
        uVar6 = *(undefined8 *)(param_1 + 0x4c);
        *(undefined8 *)(param_1 + lVar8 * 0x5a + 0x4a) = *(undefined8 *)(param_1 + 0x4a);
        *(undefined8 *)(param_1 + lVar8 * 0x5a + 0x4a + 2) = uVar6;
        param_1[lVar8 * 0x5a + 0x4e] = param_1[0x4e];
        lVar8 = lVar8 + 1;
      } while (iVar11 < param_2);
    }
  }
  fVar12 = param_1[0x45];
  fVar2 = param_1[0x42];
  fVar3 = param_1[0x44];
  fVar13 = param_1[0x43];
  lVar8 = (longlong)IntAt(param_1,0x50) * 0x20;
  fVar14 = *(float *)(lVar8 + 0x1c + param_6);
  fVar15 = *(float *)(lVar8 + 0x10 + param_6);
  fVar16 = *(float *)(lVar8 + 0x14 + param_6);
  fVar17 = *(float *)(lVar8 + 0x18 + param_6);
  param_1[0x42] = fVar14 * fVar2 + fVar15 * fVar12 + fVar16 * fVar3 + -fVar17 * fVar13;
  param_1[0x43] = fVar16 * fVar12 + fVar14 * fVar13 + fVar17 * fVar2 + -fVar15 * fVar3;
  param_1[0x44] = fVar17 * fVar12 + fVar14 * fVar3 + fVar15 * fVar13 + -fVar16 * fVar2;
  param_1[0x45] = -fVar15 * fVar2 + fVar14 * fVar12 + -fVar16 * fVar13 + -fVar17 * fVar3;
  IntegrateBone(param_1,param_3,param_6,param_7,param_8,param_9,param_10,param_11,param_12,
                      param_13,param_15,param_14,param_16,param_17,param_18,param_19);
  if (1 < param_2) {
    lVar8 = 1;
    do {
      pfVar1 = param_1 + lVar8 * 0x5a;
      uVar6 = *(undefined8 *)(param_1 + lVar8 * 0x5a + -0x4a + 2);
      *(undefined8 *)(pfVar1 + 0x24) = *(undefined8 *)(param_1 + lVar8 * 0x5a + -0x4a);
      *(undefined8 *)(pfVar1 + 0x26) = uVar6;
      uVar6 = *(undefined8 *)(param_1 + lVar8 * 0x5a + -0x52 + 2);
      *(undefined8 *)(pfVar1 + 4) = *(undefined8 *)(param_1 + lVar8 * 0x5a + -0x52);
      *(undefined8 *)(pfVar1 + 6) = uVar6;
      fVar12 = pfVar1[0x42];
      fVar2 = pfVar1[0x45];
      fVar3 = param_1[lVar8 * 0x5a + -0x15];
      fVar13 = pfVar1[0x44];
      fVar14 = pfVar1[0x43];
      fVar15 = param_1[lVar8 * 0x5a + -0x18];
      fVar16 = param_1[lVar8 * 0x5a + -0x17];
      fVar17 = param_1[lVar8 * 0x5a + -0x16];
      pfVar1[0x42] = fVar12 * fVar3 + fVar15 * fVar2 + fVar16 * fVar13 + fVar14 * -fVar17;
      pfVar1[0x43] = fVar2 * fVar16 + fVar3 * fVar14 + fVar12 * fVar17 + -fVar15 * fVar13;
      pfVar1[0x44] = fVar2 * fVar17 + fVar3 * fVar13 + fVar15 * fVar14 + fVar12 * -fVar16;
      pfVar1[0x45] = -fVar15 * fVar12 + fVar3 * fVar2 + -fVar16 * fVar14 + -fVar17 * fVar13;
      IntegrateBone(pfVar1,param_3,param_6,param_7,param_8,param_9,param_10,param_11,param_12,
                          param_13,param_15,param_14,param_16,param_17,param_18,param_19);
      iVar10 = iVar10 + 1;
      lVar8 = lVar8 + 1;
    } while (iVar10 < param_2);
  }
  return;
}

#define API extern "C" __declspec(dllexport)
API void NativeClothUpdate(float* cloth,int count,void* collisions,void* parents,float stiffness,float drag,float gravity,float windX,float windY,float windZ,float windStrength,bool collision,float timescale,bool fps60,float move,float addMove,float spring){
 SolveCloth(cloth,count,(longlong)collisions,(longlong)parents,stiffness,drag,gravity,windX,windY,windZ,windStrength,char(collision),timescale,char(fps60),move,addMove,spring);
}
API void NativeClothSkirtUpdate(float* cloth,int count,void* collisions,float* skirt,float* arg,void* parents,float stiffness,float drag,float gravity,float windX,float windY,float windZ,float windStrength,bool collision,float timescale,bool fps60,float move,float addMove,float spring){
 SolveClothSkirt(cloth,count,(longlong)collisions,skirt,arg,(longlong)parents,stiffness,drag,gravity,windX,windY,windZ,windStrength,char(collision),timescale,char(fps60),move,addMove,spring);
}
API void NativeSkirtUpdate(float* skirt,float* arg){EvaluateSkirt(skirt,arg);}
API void NativeClothBatchUpdate(const NativeSpringBatchJob* jobs,int count){
 for(int i=0;i<count;i++){
  const auto& j=jobs[i];
  if(j.count<=0)continue;
  const char collision=char((j.flags&1)!=0),fps60=char((j.flags&2)!=0);
  if(j.skirt)
   SolveClothSkirt(j.cloth,j.count,(longlong)j.collisions,j.skirt,j.skirtArgs,(longlong)j.parents,
    j.stiffness,j.drag,j.gravity,j.windX,j.windY,j.windZ,j.windStrength,collision,j.timescale,fps60,j.move,j.addMove,j.spring);
  else
   SolveCloth(j.cloth,j.count,(longlong)j.collisions,(longlong)j.parents,
    j.stiffness,j.drag,j.gravity,j.windX,j.windY,j.windZ,j.windStrength,collision,j.timescale,fps60,j.move,j.addMove,j.spring);
 }
}
API std::uint32_t UmaSpringKernelVersion(){return 2;}
#ifdef UMA_SPRING_TESTING
API float* UmaSpringQuaternionToEuler(float* out,const float* q){return QuaternionToEuler(out,q);}
API void UmaSpringIntegrateBone(float* c,void* col,void* par,float s,float d,float g,float wx,float wy,float wz,float ws,float t,char collision,char fps60,float move,float add,float spring){IntegrateBone(c,(longlong)col,(longlong)par,s,d,g,wx,wy,wz,ws,t,collision,fps60,move,add,spring);}
#endif





#ifdef UMA_SPRING_TESTING
API unsigned UmaSpringGetMxcsr(){return _mm_getcsr();}
API void UmaSpringSetMxcsr(unsigned value){_mm_setcsr(value);}
#endif

