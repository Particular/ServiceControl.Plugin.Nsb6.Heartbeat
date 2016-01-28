namespace ServiceControl.Plugin.Nsb5.Heartbeat
{
    using NServiceBus;
    using NServiceBus.Hosting;
    using NServiceBus.MessageMutator;
    using NServiceBus.Unicast;

    class EnrichPreV44MessagesWithHostDetailsMutator : IMutateIncomingTransportMessages, INeedInitialization
    {
        public UnicastBus UnicastBus { get; set; }
        public void MutateIncoming(TransportMessage transportMessage)
        {
            if (transportMessage.Headers.ContainsKey("$.diagnostics.hostid"))
            {
                return;
            }

            transportMessage.Headers["$.diagnostics.hostid"] = HostInformation.HostId.ToString("N");
            transportMessage.Headers["$.diagnostics.hostdisplayname"] = HostInformation.DisplayName;
        }

        HostInformation HostInformation
        {
            get
            {
                if (cachedHostInformation == null)
                {
                    cachedHostInformation = UnicastBus.HostInformation;
                }

                return cachedHostInformation;
            }
        }

        static HostInformation cachedHostInformation;

        public void Customize(BusConfiguration builder)
        {
            if (VersionChecker.CoreVersionIsAtLeast(4, 4))
            {
                return;
            }

            builder.RegisterComponents(c => c.ConfigureComponent<EnrichPreV44MessagesWithHostDetailsMutator>(DependencyLifecycle.SingleInstance));
        }
    }
}