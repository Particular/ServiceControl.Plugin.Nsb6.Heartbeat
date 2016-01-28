namespace ServiceControl.Plugin.Nsb5.Heartbeat.AcceptanceTests
{
    using System.Configuration;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;

    public class When_service_control_queue_unavailable_at_startup
    {
        [Test]
        public void Should_not_fail_the_endpoint()
        {
            var context = new Context();

            TestDelegate action = () => Scenario.Define(context).WithEndpoint<EndpointWithMissingSCQueue>(b => b
                .CustomConfig(busConfig => busConfig
                    .DefineCriticalErrorAction((s, e) =>
                    {
                        context.CriticalExceptionReceived = true;
                    })))
                .Run();

            Assert.DoesNotThrow(action);
            Assert.IsFalse(context.CriticalExceptionReceived);
        }

        class EndpointWithMissingSCQueue : EndpointConfigurationBuilder
        {
            public EndpointWithMissingSCQueue()
            {
                EndpointSetup<DefaultServer>();
                // couldn't find a better way to configure the plugin settings. This will probably fail other tests in this project.
                ConfigurationManager.AppSettings[@"ServiceControl/Queue"] = "invalidSCQueue";
            }
        }

        class Context : ScenarioContext
        {
            public bool CriticalExceptionReceived { get; set; }
        }
    }
}