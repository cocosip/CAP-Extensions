using DotNetCore.CAP.Transport;
using Microsoft.Extensions.DependencyInjection;

namespace DotNetCore.CAP.InMemoryMessageQueue
{
    internal sealed class InMemoryMqOptionsExtension : ICapOptionsExtension
    {

        public void AddServices(IServiceCollection services)
        {
            services.AddSingleton<CapMessageQueueMakerService>();

            services.AddSingleton<InMemoryQueue>();
            services.AddSingleton<IConsumerClientFactory, InMemoryConsumerClientFactory>();
            services.AddSingleton<ITransport, InMemoryMqTransport>();
        }
    }
}
