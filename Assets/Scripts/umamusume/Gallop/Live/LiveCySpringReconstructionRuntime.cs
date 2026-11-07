namespace Gallop.Live
{
    public interface ILiveCySpringReconstructionOwner
    {
        int ClothCategory{get;set;}
        bool IsSpringLoaded{get;}
        void ResetSpring();
        void ResetNativeCloth();
        void DeleteSpring();
        void InitializeSkirt();
        void InitializeSpring();
        void ReserveWarmup(float seconds,int delayFrame);
    }
    // Original ModelController.ReconstructCySpring 0x52a32a0 and
    // ResetCyspring 0x52a3920. Resource loading and actual engine peers are
    // owner responsibilities; neither may be replaced by only setting Category.
    public sealed class LiveCySpringReconstructionRuntime
    {
        readonly ILiveCySpringReconstructionOwner owner;
        public LiveCySpringReconstructionRuntime(ILiveCySpringReconstructionOwner value){owner=value;}
        public void Reset(){if(owner.IsSpringLoaded)owner.ResetSpring();}
        public void Reconstruct(int category)
        {
            owner.ClothCategory=category;
            if(owner.IsSpringLoaded){owner.ResetNativeCloth();owner.DeleteSpring();}
            owner.InitializeSkirt();owner.InitializeSpring();
            // Initialization can change the loaded gate; read it again.
            if(owner.IsSpringLoaded)owner.ReserveWarmup(1f,0);
        }
    }
}
