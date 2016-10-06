namespace ServiceControl.Plugin.Nsb6.Heartbeat.Sample
{
    using NServiceBus;

    class UseJsonSerializer : INeedInitialization
    {
        public void Customize(EndpointConfiguration builder)
        {
            builder.UseSerialization<JsonSerializer>();
        }
    }
}