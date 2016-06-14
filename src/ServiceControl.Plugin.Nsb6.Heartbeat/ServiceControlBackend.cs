namespace ServiceControl.Plugin
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.IO;
    using System.Net;
    using System.Runtime.Serialization;
    using System.Runtime.Serialization.Json;
    using System.Text;
    using System.Threading.Tasks;
    using Heartbeat.Messages;
    using NServiceBus;
    using NServiceBus.Extensibility;
    using NServiceBus.Performance.TimeToBeReceived;
    using NServiceBus.Routing;
    using NServiceBus.Settings;
    using NServiceBus.Support;
    using NServiceBus.Transports;

    class ServiceControlBackend
    {
        public ServiceControlBackend(IDispatchMessages messageSender, ReadOnlySettings settings)
        {
            this.settings = settings;
            this.messageSender = messageSender;

            startupSerializer = new DataContractJsonSerializer(typeof(RegisterEndpointStartup), new DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("o"),
                EmitTypeInformation = EmitTypeInformation.Never,
                UseSimpleDictionaryFormat = true
            });
            heartbeatSerializer = new DataContractJsonSerializer(typeof(EndpointHeartbeat), new DataContractJsonSerializerSettings
            {
                DateTimeFormat = new DateTimeFormat("o"),
                EmitTypeInformation = EmitTypeInformation.Never,
                UseSimpleDictionaryFormat = true
            });

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
            var operation = new TransportOperation(outgoingMessage, new UnicastAddressTag(serviceControlBackendAddress), deliveryConstraints: new[]
            {
                new DiscardIfNotReceivedBefore(timeToBeReceived)
            });
            await messageSender.Dispatch(new TransportOperations(operation), new ContextBag()).ConfigureAwait(false);
        }

        internal byte[] Serialize(EndpointHeartbeat message)
        {
            return Serialize(message, heartbeatSerializer);
        }

        internal byte[] Serialize(RegisterEndpointStartup message)
        {
            return Serialize(message, startupSerializer);
        }

        static byte[] Serialize(object result, XmlObjectSerializer serializer)
        {
            byte[] body;
            using (var stream = new MemoryStream())
            {
                serializer.WriteObject(stream, result);
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

            string auditAddress;
            if (settings.TryGetAuditQueueAddress(out auditAddress))
            {
                var qm = Parse(auditAddress);
                return "Particular.ServiceControl" + "@" + qm.Item2;
            }

            return null;
        }

        bool TryGetErrorQueueAddress(out string address)
        {
            address = settings.ErrorQueueAddress();
            return !string.IsNullOrWhiteSpace(address);
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

        DataContractJsonSerializer startupSerializer;
        DataContractJsonSerializer heartbeatSerializer;
        string serviceControlBackendAddress;
        ReadOnlySettings settings;
    }
}