using Gallop.Live.Cutt;
namespace Gallop.Live
{
    public partial class Director
    {
        public void BindSweatLocator()
        {
            if (_liveTimelineControl == null) return;
            _liveTimelineControl.OnUpdateSweatLocator -= UpdateSweatLocator;
            _liveTimelineControl.OnUpdateSweatLocator += UpdateSweatLocator;
        }
        public void UnbindSweatLocator()
        {
            if (_liveTimelineControl != null)
                _liveTimelineControl.OnUpdateSweatLocator -= UpdateSweatLocator;
        }
        // Native Director 4e400f0 performs a physical-owner lookup. Never use a
        // character id, bitmask, or compressed position in the host actor list.
        public void UpdateSweatLocator(ref SweatLocatorUpdateInfo info)
        {
            if (info.owner < 0 || info.owner >= 20) return;
            foreach (var actor in CharaContainerScript)
                if (actor != null && actor.LiveSweatPhysicalOwner == info.owner)
                { actor.ApplyLiveSweat(info); return; }
        }
    }
}
