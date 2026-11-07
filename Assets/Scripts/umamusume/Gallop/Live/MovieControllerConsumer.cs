using System;

namespace Gallop.Live
{
    // StageController.UpdateMovieController 0x4ebc9e0:
    // setup(movie, arg); skipCheck ? fixedPath : deltaTimePath.
    public static class MovieControllerConsumer
    {
        public enum Mode { None, Fixed, Delta }

        public static Mode Apply(bool hasMovie, bool skipCheck, int arg,
            Action<int> setup, Action<float> fixedUpdate, Action<float> deltaUpdate, float deltaTime)
        {
            if (!hasMovie) return Mode.None;
            setup?.Invoke(arg);
            if (skipCheck)
            {
                fixedUpdate?.Invoke(0f);
                return Mode.Fixed;
            }
            deltaUpdate?.Invoke(deltaTime);
            return Mode.Delta;
        }
    }
}
