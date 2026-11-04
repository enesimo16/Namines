using System;
using Microsoft.Extensions.DependencyInjection;
using Namines.Flow.Abstractions;
using Namines.Flow.Application;

namespace Namines.Flow.Services
{
    /// <summary>
    /// Namines Flow modülü facade implementasyonu.
    /// </summary>
    public sealed class FlowModule : IFlowModule
    {
        private readonly IServiceProvider? _serviceProvider;
        private IFlowApplicationService? _application;

        public IFlowRuleMatcher RuleMatcher { get; }
        public IFlowApplicationService Application =>
            _application ??= _serviceProvider!.GetRequiredService<IFlowApplicationService>();
        public IFlowJobQueue JobQueue { get; }

        [ActivatorUtilitiesConstructor]
        public FlowModule(
            IFlowRuleMatcher ruleMatcher,
            IFlowJobQueue jobQueue,
            IServiceProvider serviceProvider)
        {
            RuleMatcher = ruleMatcher ?? throw new ArgumentNullException(nameof(ruleMatcher));
            JobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

        public FlowModule(
            IFlowRuleMatcher ruleMatcher,
            IFlowApplicationService application,
            IFlowJobQueue jobQueue)
        {
            RuleMatcher = ruleMatcher ?? throw new ArgumentNullException(nameof(ruleMatcher));
            _application = application ?? throw new ArgumentNullException(nameof(application));
            JobQueue = jobQueue ?? throw new ArgumentNullException(nameof(jobQueue));
        }
    }
}
