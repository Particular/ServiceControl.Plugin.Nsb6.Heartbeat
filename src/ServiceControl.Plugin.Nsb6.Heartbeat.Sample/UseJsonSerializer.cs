namespace ServiceControl.Plugin.Nsb6.Heartbeat.Sample
{
    using NServiceBus;

    class UseJsonSerializer : INeedInitialization
    {
        public void Customize(BusConfiguration builder)
        {
            builder.UseSerialization<JsonSerializer>();
        }
    }
}