namespace ServiceControl.Plugin.Nsb5.Heartbeat.Sample
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