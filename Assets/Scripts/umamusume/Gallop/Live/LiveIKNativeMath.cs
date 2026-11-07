using System;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    // Original CRT acosf 0x452570 on the solver's [-1,1] domain. Unity's
    // Mathf.Acos can differ by one ULP and change downstream joint rotations.
    public static class LiveIKNativeMath
    {
        static float F(float x)=>LiveTimelineControl.MonitorFloat32(x);
        static float V(uint bits)=>BitConverter.Int32BitsToSingle(unchecked((int)bits));
        // Original CRT sinf 0x450de0 for acosf's [0,pi] output domain.
        // Keep the double polynomial/range-reduction order before float cast.
        public static float SinAcosAngle(float angle)
        {
            uint bits=unchecked((uint)BitConverter.SingleToInt32Bits(angle));uint absolute=bits&0x7fffffffu;
            double x=System.Math.Abs((double)angle);
            double Sine(double v)
            {
                double z=v*v,z2=z*z,cube=v*z;
                double first=2.7557319223985893e-6*z;
                double second=.008333333333333333*z;
                first+=-.0001984126984126984;first*=z2;
                second+=-.16666666666666666;first+=second;first*=cube;
                return v+first;
            }
            double Cosine(double v)
            {
                double z=v*v,z2=z*z;
                double first=-2.755731922398589e-7*z;
                double second=-.0013888888888888887*z;
                double result=-.5*z;
                first+=2.4801587301587298e-5;first*=z2;
                second+=.041666666666666664;first+=second;first*=z2;
                result+=1.0;return result+first;
            }
            double result;
            if(absolute<=0x3f490fdbu)
            {
                if(absolute<0x39000000u)return angle;
                result=absolute<0x3c000000u?x-((x*x)*x)*.16666666666666666:Sine(x);
            }
            else
            {
                int quadrant=(int)(x*.6366197723675814+.5);
                double first=x-1.5707963267341256*quadrant;
                double reduced=first-6.077100506506192e-11*quadrant;
                int exponent=(int)((unchecked((ulong)BitConverter.DoubleToInt64Bits(reduced))>>52)&0x7ff);
                int originalExponent=(int)((unchecked((ulong)BitConverter.DoubleToInt64Bits(x))>>52)&0x7ff);
                if(originalExponent-exponent>15)
                {
                    double previous=first,high=6.077100506303966e-11*quadrant;
                    first-=high;double error=(previous-first)-high;
                    double tail=2.0222662487959506e-21*quadrant-error;reduced=first-tail;
                }
                bool cosine=(quadrant&1)!=0;
                if(exponent<0x3f2)
                    result=exponent<=0x3de?(cosine?1.0:reduced):cosine?1.0-(reduced*reduced)*.5:reduced-(.16666666666666666*reduced)*(reduced*reduced);
                else result=cosine?Cosine(reduced):Sine(reduced);
                if((quadrant&2)!=0)result=-result;
            }
            return (float)((bits&0x80000000u)!=0?-result:result);
        }
        public static float Acos(float input)
        {
            uint bits=unchecked((uint)BitConverter.SingleToInt32Bits(input));uint absolute=bits&0x7fffffffu;uint exponent=(absolute>>23)&255;
            if(absolute>0x3f800000u)return float.NaN;
            if(exponent<0x65)return V(0x3fc90fdb);
            if(absolute==0x3f800000u)return bits==absolute?0f:V(0x40490fdb);
            float x=V(absolute),z,sqrt=0f;
            if(exponent>=0x7e){z=F(F(1f-x)*.5f);sqrt=F((float)System.Math.Sqrt(z));x=sqrt;}
            else z=F(x*x);
            float numerator=F(V(0xbc5b3fe1)-F(z*V(0x3b81ce6b)));
            float denominator=F(V(0x3f8d6fa5)-F(z*V(0x3f561f0d)));
            numerator=F(F(numerator*z)-V(0x3d678bdd));
            numerator=F(F(numerator*z)+V(0x3e3c94dc));
            numerator=F(F(numerator*z)/denominator);
            if(exponent<0x7e)
            {
                double correction=6.123233995736766e-17-(double)F(numerator*input);
                return F((float)(System.Math.PI*.5-((double)input-correction)));
            }
            if((bits&0x80000000u)!=0)
            {
                double correction=(double)F(x*numerator)-6.123233995736766e-17;
                double doubled=(correction+(double)sqrt)*2.0;
                return F((float)(System.Math.PI-doubled));
            }
            float twice=F(F(x+x)*numerator);
            float truncated=V(unchecked((uint)BitConverter.SingleToInt32Bits(sqrt))&0xffff0000u);
            float remainder=F(F(z-F(truncated*truncated))/F(truncated+sqrt));
            return F(F(twice+F(remainder+remainder))+F(truncated+truncated));
        }
    }
}
