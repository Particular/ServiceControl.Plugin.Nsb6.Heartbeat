namespace ServiceControl.Plugin.Nsb5.Heartbeat
{
    using System;
    using System.Configuration;
    using System.Threading;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Features;
    using NServiceBus.Hosting;
    using NServiceBus.Logging;
    using NServiceBus.Transports;
    using NServiceBus.Unicast;
    using ServiceControl.Plugin.Heartbeat.Messages;

    class Heartbeats : Feature
    {
        static ILog Logger = LogManager.GetLogger(typeof(Heartbeats));
        
        public Heartbeats()
        {
            EnableByDefault();

            // we need a mechanism to start and stop to register and stop heartbeats timers.
            // and both StartupTasks and IWantToRunWhenBusStartsAndStops aren't supported for send-only endpoints.
            Prerequisite(context => !context.Settings.GetOrDefault<bool>("Endpoint.SendOnly"), "The Heartbeats plugin currently isn't supported for Send-Only endpoints");

            RegisterStartupTask<HeartbeatStartup>();
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
        }

        class HeartbeatStartup : FeatureStartupTask, IDisposable
        {
            public HeartbeatStartup(ISendMessages sendMessages, Configure configure, UnicastBus unicastBus)
            {
                this.unicastBus = unicastBus;

                backend = new ServiceControlBackend(sendMessages, configure);
                endpointName = configure.Settings.EndpointName();

                var interval = ConfigurationManager.AppSettings[@"Heartbeat/Interval"];
                if (!String.IsNullOrEmpty(interval))
                {
                    heartbeatInterval = TimeSpan.Parse(interval);
                }

                ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks * 4); // Default ttl
                var ttl = ConfigurationManager.AppSettings[@"Heartbeat/TTL"];
                if (!String.IsNullOrWhiteSpace(ttl))
                {
                    if (TimeSpan.TryParse(ttl, out ttlTimeSpan))
                    {
                        Logger.InfoFormat("Heartbeat/TTL set to {0}", ttlTimeSpan);
                    }
                    else
                    {
                        ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks * 4);
                        Logger.Warn("Invalid Heartbeat/TTL specified in AppSettings. Reverted to default TTL (4 x Heartbeat/Interval)");
                    }
                }
            }

            protected override void OnStart()
            {
                cancellationTokenSource = new CancellationTokenSource();

                NotifyEndpointStartup(unicastBus.HostInformation, DateTime.UtcNow);
                StartHeartbeats(unicastBus.HostInformation);
            }

            protected override void OnStop()
            {
                if (heartbeatTimer != null)
                {
                    heartbeatTimer.Dispose();
                }

                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Cancel();
                }

                base.OnStop();
            }

            void NotifyEndpointStartup(HostInformation hostInfo, DateTime startupTime)
            {
                // don't block here since StartupTasks are executed synchronously.
                Task.Run(() => SendEndpointStartupMessage(hostInfo, startupTime, cancellationTokenSource.Token));
            }

            void StartHeartbeats(HostInformation hostInfo)
            {
                Logger.DebugFormat("Start sending heartbeats every {0}", heartbeatInterval);
                heartbeatTimer = new Timer(x => SendHeartbeatMessage(hostInfo), null, TimeSpan.Zero, heartbeatInterval);
            }

            void SendEndpointStartupMessage(HostInformation hostInfo, DateTime startupTime, CancellationToken cancellationToken)
            {
                try
                {
                    backend.Send(
                        new RegisterEndpointStartup
                        {
                            HostId = hostInfo.HostId,
                            Host = hostInfo.DisplayName,
                            Endpoint = endpointName,
                            HostDisplayName = hostInfo.DisplayName,
                            HostProperties = hostInfo.Properties,
                            StartedAt = startupTime
                        }, ttlTimeSpan);
                }
                catch (Exception ex)
                {
                    Logger.Warn(string.Format("Unable to register endpoint startup with ServiceControl. Going to reattempt registration after {0}.", registrationRetryInterval), ex);

                    Task.Delay(registrationRetryInterval, cancellationToken)
                        .ContinueWith(t => SendEndpointStartupMessage(hostInfo, startupTime, cancellationToken), cancellationToken);
                }
            }

            void SendHeartbeatMessage(HostInformation hostInfo)
            {
                var heartBeat = new EndpointHeartbeat
                {
                    ExecutedAt = DateTime.UtcNow,
                    EndpointName = endpointName,
                    Host = hostInfo.DisplayName,
                    HostId = hostInfo.HostId
                };

                try
                {
                    backend.Send(heartBeat, ttlTimeSpan);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Unable to send heartbeat to ServiceControl:", ex);
                }
            }

            public void Dispose()
            {
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Dispose();
                }
            }

            readonly UnicastBus unicastBus;

            ServiceControlBackend backend;
            CancellationTokenSource cancellationTokenSource;
            string endpointName;
            Timer heartbeatTimer;
            TimeSpan ttlTimeSpan;
            TimeSpan heartbeatInterval = TimeSpan.FromSeconds(10);
            TimeSpan registrationRetryInterval = TimeSpan.FromMinutes(1);
        }
    }
}