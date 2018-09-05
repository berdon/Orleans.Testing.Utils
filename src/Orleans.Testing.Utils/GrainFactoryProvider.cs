using Orleans;

namespace SharedOrleansUtils
{
    public interface IGrainFactoryProvider
    {
        IGrainFactory GrainFactory { get; }
    }

    public class GrainFactoryProvider : IGrainFactoryProvider
    {
        public IGrainFactory GrainFactory { get; }

        public GrainFactoryProvider(IGrainFactory getGrainFactory)
        {
            GrainFactory = getGrainFactory;
        }
    }
}
