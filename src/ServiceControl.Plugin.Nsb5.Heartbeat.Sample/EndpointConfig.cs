namespace ServiceControl.Plugin.Nsb5.Heartbeat.Sample
{
    using NServiceBus;

    public class EndpointConfig : IConfigureThisEndpoint, AsA_Server {
        public void Customize(BusConfiguration configuration)
        {
            configuration.UsePersistence<InMemoryPersistence>();
        }
    }
}
