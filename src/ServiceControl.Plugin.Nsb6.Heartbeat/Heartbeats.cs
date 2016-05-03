namespace ServiceControl.Plugin.Nsb6.Heartbeat
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Threading;
    using System.Threading.Tasks;
    using Janitor;
    using NServiceBus;
    using NServiceBus.Features;
    using NServiceBus.Logging;
    using NServiceBus.Settings;
    using NServiceBus.Transports;
    using Plugin.Heartbeat.Messages;

    class Heartbeats : Feature
    {
        public Heartbeats()
        {
            EnableByDefault();

            // we need a mechanism to start and stop to register and stop heartbeats timers.
            // and both StartupTasks and IWantToRunWhenBusStartsAndStops aren't supported for send-only endpoints.
            Prerequisite(context => !context.Settings.GetOrDefault<bool>("Endpoint.SendOnly"), "The Heartbeats plugin currently isn't supported for Send-Only endpoints");
        }

        protected override void Setup(FeatureConfigurationContext context)
        {
            if (!VersionChecker.CoreVersionIsAtLeast(4, 4))
            {
                context.Pipeline.Register("EnrichPreV44MessagesWithHostDetailsBehavior", new EnrichPreV44MessagesWithHostDetailsBehavior(context.Settings), "Enriches pre v4 messages with details about the host");
            }

            context.RegisterStartupTask(builder => new HeartbeatStartup(builder.Build<IDispatchMessages>(), context.Settings));
        }

        static ILog Logger = LogManager.GetLogger(typeof(Heartbeats));

        [SkipWeaving]
        class HeartbeatStartup : FeatureStartupTask, IDisposable
        {
            public HeartbeatStartup(IDispatchMessages messageDispatcher, ReadOnlySettings settings)
            {
                backend = new ServiceControlBackend(messageDispatcher, settings);
                endpointName = settings.EndpointName().ToString();
                HostId = settings.Get<Guid>("NServiceBus.HostInformation.HostId");
                Host = settings.Get<string>("NServiceBus.HostInformation.DisplayName");
                Properties = settings.Get<Dictionary<string, string>>("NServiceBus.HostInformation.Properties");

                var interval = ConfigurationManager.AppSettings[@"Heartbeat/Interval"];
                if (!string.IsNullOrEmpty(interval))
                {
                    heartbeatInterval = TimeSpan.Parse(interval);
                }

                ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks*4); // Default ttl
                var ttl = ConfigurationManager.AppSettings[@"Heartbeat/TTL"];
                if (!string.IsNullOrWhiteSpace(ttl))
                {
                    if (TimeSpan.TryParse(ttl, out ttlTimeSpan))
                    {
                        Logger.InfoFormat("Heartbeat/TTL set to {0}", ttlTimeSpan);
                    }
                    else
                    {
                        ttlTimeSpan = TimeSpan.FromTicks(heartbeatInterval.Ticks*4);
                        Logger.Warn("Invalid Heartbeat/TTL specified in AppSettings. Reverted to default TTL (4 x Heartbeat/Interval)");
                    }
                }
            }

            public void Dispose()
            {
                if (cancellationTokenSource != null)
                {
                    cancellationTokenSource.Dispose();
                }
            }

            protected override Task OnStart(IMessageSession session)
            {
                cancellationTokenSource = new CancellationTokenSource();

                NotifyEndpointStartup(DateTime.UtcNow);
                StartHeartbeats();

                return Task.FromResult(0);
            }

            protected override Task OnStop(IMessageSession session)
            {
                heartbeatTimer?.Stop();

                cancellationTokenSource?.Cancel();

                return Task.FromResult(0);
            }

            void NotifyEndpointStartup(DateTime startupTime)
            {
                // don't block here since StartupTasks are executed synchronously.
                SendEndpointStartupMessage(startupTime, cancellationTokenSource.Token).Ignore();
            }

            void StartHeartbeats()
            {
                Logger.DebugFormat("Start sending heartbeats every {0}", heartbeatInterval);
                heartbeatTimer = new AsyncTimer();
                heartbeatTimer.Start(SendHeartbeatMessage, heartbeatInterval, e => { });
            }

            async Task SendEndpointStartupMessage(DateTime startupTime, CancellationToken cancellationToken)
            {
                try
                {
                    await backend.Send(
                        new RegisterEndpointStartup
                        {
                            HostId = HostId,
                            Host = Host,
                            Endpoint = endpointName,
                            HostDisplayName = Host,
                            HostProperties = Properties,
                            StartedAt = startupTime
                        }, ttlTimeSpan).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn(string.Format("Unable to register endpoint startup with ServiceControl. Going to reattempt registration after {0}.", registrationRetryInterval), ex);

                    await Task.Delay(registrationRetryInterval, cancellationToken).ConfigureAwait(false);
                    await SendEndpointStartupMessage(startupTime, cancellationToken).ConfigureAwait(false);
                }
            }

            async Task SendHeartbeatMessage()
            {
                var heartBeat = new EndpointHeartbeat
                {
                    ExecutedAt = DateTime.UtcNow,
                    EndpointName = endpointName,
                    Host = Host,
                    HostId = HostId
                };

                try
                {
                    await backend.Send(heartBeat, ttlTimeSpan).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    Logger.Warn("Unable to send heartbeat to ServiceControl:", ex);
                }
            }

            ServiceControlBackend backend;
            CancellationTokenSource cancellationTokenSource;
            string endpointName;
            AsyncTimer heartbeatTimer;
            TimeSpan ttlTimeSpan;
            TimeSpan heartbeatInterval = TimeSpan.FromSeconds(10);
            TimeSpan registrationRetryInterval = TimeSpan.FromMinutes(1);
            Guid HostId;
            string Host;
            Dictionary<string, string> Properties;
        }
    }
}