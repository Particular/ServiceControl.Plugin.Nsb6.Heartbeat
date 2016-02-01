﻿namespace ServiceControl.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Net;
    using System.Text;
    using System.Threading.Tasks;
    using NServiceBus;
    using NServiceBus.Config;
    using NServiceBus.Extensibility;
    using NServiceBus.Performance.TimeToBeReceived;
    using NServiceBus.Serializers.Json;
    using NServiceBus.Settings;
    using NServiceBus.Support;
    using NServiceBus.Transports;
    using ServiceControl.Plugin.Heartbeat.Messages;
    using NServiceBus.Routing;
    class ServiceControlBackend
    {
        public ServiceControlBackend(IDispatchMessages messageSender, ReadOnlySettings settings)
        {
            this.settings = settings;
            this.messageSender = messageSender;

            serializer = new JsonMessageSerializer(new SimpleMessageMapper());

            serviceControlBackendAddress = GetServiceControlAddress();
        }

        async Task Send(byte[] body, string messageType, TimeSpan timeToBeReceived)
        {
            var headers = new Dictionary<string, string>();
            headers[Headers.EnclosedMessageTypes] = messageType;
            headers[Headers.ContentType] = ContentTypes.Json; //Needed for ActiveMQ transport
            headers[Headers.ReplyToAddress] = settings.LocalAddress();
            headers[Headers.MessageIntent] = MessageIntentEnum.Send.ToString();

            var outgoingMessage = new OutgoingMessage(Guid.NewGuid().ToString(), headers, body);
            var operation = new TransportOperation(outgoingMessage, new UnicastAddressTag(serviceControlBackendAddress), deliveryConstraints: new[] { new DiscardIfNotReceivedBefore(timeToBeReceived) });
            await messageSender.Dispatch(new TransportOperations(operation), new ContextBag()).ConfigureAwait(false);
        }

        internal byte[] Serialize(object result)
        {
            byte[] body;
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(new[]
                {
                    result
                }, stream);
                body = stream.ToArray();
            }

            //hack to remove the type info from the json
            var bodyString = Encoding.UTF8.GetString(body);

            var toReplace = ", " + result.GetType().Assembly.GetName().Name;

            bodyString = bodyString.Replace(toReplace, ", ServiceControl");

            body = Encoding.UTF8.GetBytes(bodyString);
            return body;
        }

        public Task Send(EndpointHeartbeat messageToSend, TimeSpan timeToBeReceived)
        {
            var body = Serialize(messageToSend);
            return Send(body, messageToSend.GetType().FullName, timeToBeReceived);
        }

        public Task Send(RegisterEndpointStartup messageToSend, TimeSpan timeToBeReceived)
        {
            var body = Serialize(messageToSend);
            return Send(body, messageToSend.GetType().FullName, timeToBeReceived);
        }

        string GetServiceControlAddress()
        {
            var queueName = ConfigurationManager.AppSettings[@"ServiceControl/Queue"];
            if (!string.IsNullOrEmpty(queueName))
            {
                return queueName;
            }

            string errorAddress;
            if (TryGetErrorQueueAddress(out errorAddress))
            {
                var qm = Parse(errorAddress);
                return "Particular.ServiceControl" + "@" + qm.Item2;
            }

            if (VersionChecker.CoreVersionIsAtLeast(4, 1))
            {
                //audit config was added in 4.1
                string address;
                if (TryGetAuditAddress(out address))
                {
                    var qm = Parse(errorAddress);
                    return "Particular.ServiceControl" + "@" + qm.Item2;
                }
            }

            return null;
        }


        bool TryGetErrorQueueAddress(out string address)
        {
            var faultsForwarderConfig = settings.GetConfigSection<MessageForwardingInCaseOfFaultConfig>();
            if (!string.IsNullOrEmpty(faultsForwarderConfig?.ErrorQueue))
            {
                address = faultsForwarderConfig.ErrorQueue;
                return true;
            }
            address = null;
            return false;
        }

        bool TryGetAuditAddress(out string address)
        {
            var auditConfig = settings.GetConfigSection<AuditConfig>();
            if (!string.IsNullOrEmpty(auditConfig?.QueueName))
            {
                address = auditConfig.QueueName;
                return true;
            }
            address = null;

            return false;
        }

        static Tuple<string, string> Parse(string destination)
        {
            if (string.IsNullOrEmpty(destination))
            {
                throw new ArgumentException("Invalid destination address specified", nameof(destination));
            }

            var arr = destination.Split('@');

            var queue = arr[0];
            var machine = RuntimeEnvironment.MachineName;

            if (string.IsNullOrWhiteSpace(queue))
            {
                throw new ArgumentException("Invalid destination address specified", nameof(destination));
            }

            if (arr.Length == 2)
                if (arr[1] != "." && arr[1].ToLower() != "localhost" && arr[1] != IPAddress.Loopback.ToString())
                    machine = arr[1];

            return new Tuple<string, string>(queue, machine);
        }

        IDispatchMessages messageSender;

        JsonMessageSerializer serializer;
        string serviceControlBackendAddress;
        ReadOnlySettings settings;
    }
}