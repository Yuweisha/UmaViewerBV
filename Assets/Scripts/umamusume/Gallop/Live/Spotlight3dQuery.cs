namespace Gallop.Live
{
    // StageController.GetSpotlight3dObject 0x4ead110:
    // root destroyed -> null; negative index -> null; child/component lookup -> result.
    public static class Spotlight3dQuery
    {
        public static object Get(object root, bool destroyed, int index, bool hasChild, object component)
        {
            if (root == null || destroyed) return null;
            if (index < 0) return null;
            if (!hasChild) return null;
            return component;
        }
    }
}
