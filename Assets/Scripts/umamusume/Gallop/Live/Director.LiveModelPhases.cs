using UnityEngine;

namespace Gallop.Live
{
    public partial class Director
    {
        readonly LiveModelPhaseRegistry liveModelPhases = new LiveModelPhaseRegistry();
        internal void ReserveFormationPropCySpring(int characterIndex, int dressIndex, float seconds) => _livePropsController?.ReserveWarmingUpCySpring(characterIndex, dressIndex, seconds);
        internal LiveModelPhaseRegistry RegisterLiveModelIK(UmaContainerCharacter character)
        {
            var phaseOwner = character.LiveModelPhaseOwner;
            if (phaseOwner == null)
            {
                character.LiveModelPhaseOwner = phaseOwner;
            }

            liveModelPhases.AddItem(phaseOwner);
            return liveModelPhases;
        }
    }
}
