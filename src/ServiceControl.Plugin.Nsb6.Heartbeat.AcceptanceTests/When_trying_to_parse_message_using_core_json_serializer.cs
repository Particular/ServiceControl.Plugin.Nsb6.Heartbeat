namespace ServiceControl.Plugin.Nsb6.Heartbeat.AcceptanceTests
{
    using System.Configuration;
    using System.Linq;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;
    using System.Threading.Tasks;
    using ServiceControl.Plugin.Heartbeat.Messages;

    public class When_trying_to_parse_message_using_core_json_serializer
    {
        [Test]
        public async void Should_not_fail()
        {
            var testContext = await Scenario.Define<Context>()
                .WithEndpoint<HeartbeatEndpoint>()
                .Done(c => c.RegisterMessage != null && c.HeartbeatMessage != null)
                .Run();

            Assert.IsTrue(testContext.RegisterMessage != null);
            Assert.AreEqual("HeartbeatEndpoint", testContext.RegisterMessage.Endpoint);
            Assert.IsTrue(testContext.RegisterMessage.HostProperties.ContainsKey("Machine"));

            Assert.IsTrue(testContext.HeartbeatMessage != null);
            Assert.AreEqual("HeartbeatEndpoint", testContext.HeartbeatMessage.EndpointName);
        }

        class HeartbeatEndpoint : EndpointConfigurationBuilder
        {
            public HeartbeatEndpoint()
            {
                EndpointSetup<DefaultServer>(c =>
                {
                    c.UseSerialization<JsonSerializer>();
                    c.Conventions().DefiningMessagesAs(t => t.GetInterfaces().Contains(typeof(IMessage)) && t.Assembly == typeof(When_trying_to_parse_message_using_core_json_serializer).Assembly);
                });
                ConfigurationManager.AppSettings[@"ServiceControl/Queue"] = "HeartbeatEndpoint";
            }

            public class RegisterHandler : IHandleMessages<RegisterEndpointStartup>
            {
                public Context Context { get; set; }

                public Task Handle(RegisterEndpointStartup message, IMessageHandlerContext context)
                {
                    Context.RegisterMessage = message;
                    return Task.FromResult(0);
                }
            }

            public class HeartbeatHandler : IHandleMessages<EndpointHeartbeat>
            {
                public Context Context { get; set; }

                public Task Handle(EndpointHeartbeat message, IMessageHandlerContext context)
                {
                    Context.HeartbeatMessage = message;
                    return Task.FromResult(0);
                }
            }
        }

        class Context : ScenarioContext
        {
            public RegisterEndpointStartup RegisterMessage { get; set; }
            public EndpointHeartbeat HeartbeatMessage { get; set; }
        }
    }
}
