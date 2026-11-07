using System;

namespace Gallop.Live
{
    // Controlled complete-target path of original SetFace/SetPrevWeight/
    // BlendProgrammable (4aca9e0/4ad3bb0/4ac2d70).
    public sealed class LiveFacialWeightTransition
    {
        public readonly float[] Current, Target, Previous;
        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public DrivenKeyComponent.InterpolateType Interpolate { get; private set; }
        public bool Initialized { get; private set; }

        public LiveFacialWeightTransition(int count)
        { Current = new float[count]; Target = new float[count]; Previous = new float[count]; }

        public bool SetTarget(float[] values, float duration, DrivenKeyComponent.InterpolateType interpolate, bool force = false)
        {
            if (values.Length != Current.Length) throw new ArgumentException(nameof(values));
            bool changed = force || !Initialized || Duration != duration || Interpolate != interpolate;
            for (int i = 0; i < values.Length && !changed; i++) changed = values[i] != Target[i];
            if (!changed) return false;
            if (duration > 0f) Array.Copy(Current, Previous, Current.Length);
            Array.Copy(values, Target, values.Length);
            Duration = duration; Elapsed = 0f; Interpolate = interpolate; Initialized = true;
            if (duration <= 0f) Array.Copy(Target, Current, Current.Length);
            return true;
        }

        public void Advance(float delta)
        {
            if (Elapsed >= Duration) return;
            Elapsed = (float)((double)Elapsed + delta);
            float ratio = LiveFacialInterpolation.Rate(Interpolate, Elapsed, Duration);
            for (int i = 0; i < Current.Length; i++)
            {
                if (Previous[i] == Target[i]) continue;
                float difference = (float)((double)Target[i] - Previous[i]);
                float step = (float)((double)difference * ratio);
                Current[i] = (float)((double)Previous[i] + step);
            }
        }
    }
}
