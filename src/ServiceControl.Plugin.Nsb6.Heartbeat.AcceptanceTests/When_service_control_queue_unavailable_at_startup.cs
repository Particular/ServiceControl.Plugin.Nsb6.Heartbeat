namespace ServiceControl.Plugin.Nsb6.Heartbeat.AcceptanceTests
{
    using System.Configuration;
    using NServiceBus;
    using NServiceBus.AcceptanceTesting;
    using NUnit.Framework;
    using System.Threading.Tasks;
    using NServiceBus.AcceptanceTests.EndpointTemplates;

    public class When_service_control_queue_unavailable_at_startup
    {
        [Test]
        public async void Should_not_fail_the_endpoint()
        {
            var testContext = await Scenario.Define<Context>()
                .WithEndpoint<EndpointWithMissingSCQueue>(b => b
                .CustomConfig((busConfig, context) => busConfig
                    .DefineCriticalErrorAction(c =>
                    {
                        context.CriticalExceptionReceived = true;
                        return Task.FromResult(0);
                    })))
                .Run();

            Assert.IsFalse(testContext.CriticalExceptionReceived);
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