namespace ServiceControl.Plugin
{
    using System;
    using System.Configuration;
    using System.IO;
    using System.Text;
    using NServiceBus;
    using NServiceBus.Config;
    using NServiceBus.Serializers.Binary;
    using NServiceBus.Serializers.Json;
    using NServiceBus.Transports;
    using NServiceBus.Unicast;

    class ServiceControlBackend
    {
        Configure configure;
        public  ServiceControlBackend(ISendMessages messageSender, Configure configure)
        {
            this.configure = configure;
            this.messageSender = messageSender;
            serializer = new JsonMessageSerializer(new SimpleMessageMapper());

            serviceControlBackendAddress = GetServiceControlAddress();
        }

        public void Send(object messageToSend, TimeSpan timeToBeReceived)
        {
            var message = new TransportMessage
            {
                TimeToBeReceived = timeToBeReceived
            };

            using (var stream = new MemoryStream())
            {
                serializer.Serialize(new[] { messageToSend }, stream);
                message.Body = stream.ToArray();
            }

            //hack to remove the type info from the json
            var bodyString = Encoding.UTF8.GetString(message.Body);

            var toReplace = ", " + messageToSend.GetType().Assembly.GetName().Name;

            bodyString = bodyString.Replace(toReplace, ", ServiceControl");

            message.Body = Encoding.UTF8.GetBytes(bodyString);
            // end hack
            message.Headers[Headers.EnclosedMessageTypes] = messageToSend.GetType().FullName;
            message.Headers[Headers.ContentType] = ContentTypes.Json; //Needed for ActiveMQ transport

            messageSender.Send(message, new SendOptions(serviceControlBackendAddress) { ReplyToAddress = configure.LocalAddress });
        }

        public void Send(object messageToSend)
        {
            Send(messageToSend, TimeSpan.MaxValue);
        }

        Address GetServiceControlAddress()
        {
            var queueName = ConfigurationManager.AppSettings[@"ServiceControl/Queue"];
            if (!String.IsNullOrEmpty(queueName))
            {
                return Address.Parse(queueName);
            }

            Address errorAddress;
            if (TryGetErrorQueueAddress(out errorAddress))
            { 
                return new Address("Particular.ServiceControl", errorAddress.Machine);
            }

            if (VersionChecker.CoreVersionIsAtLeast(4, 1))
            {
                //audit config was added in 4.1
                Address address;
                if (TryGetAuditAddress(out address))
                {
                    return new Address("Particular.ServiceControl", address.Machine);
                }
            }

            return null;
        }

        bool TryGetErrorQueueAddress(out Address address)
        {
            var faultsForwarderConfig = configure.Settings.GetConfigSection<MessageForwardingInCaseOfFaultConfig>();
            if (faultsForwarderConfig != null && !string.IsNullOrEmpty(faultsForwarderConfig.ErrorQueue))
            {
                address = Address.Parse(faultsForwarderConfig.ErrorQueue);
                return true;
            }
            address = null;
            return false;
        }

        bool TryGetAuditAddress(out Address address)
        {
            var auditConfig = configure.Settings.GetConfigSection<AuditConfig>();
            if (auditConfig != null && !string.IsNullOrEmpty(auditConfig.QueueName))
            {
                address = Address.Parse(auditConfig.QueueName);
                return true;
            }
            address = null;

            return false;
        }

        JsonMessageSerializer serializer;
        ISendMessages messageSender;
        Address serviceControlBackendAddress;
    }
}