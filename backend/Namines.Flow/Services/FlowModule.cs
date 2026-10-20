using System;
using Namines.Flow.Abstractions;

namespace Namines.Flow.Services
{
    /// <summary>
    /// Namines Flow modülü facade implementasyonu.
    /// </summary>
    public sealed class FlowModule : IFlowModule
    {
        public IFlowRuleMatcher RuleMatcher { get; }

        public FlowModule(IFlowRuleMatcher ruleMatcher)
        {
            RuleMatcher = ruleMatcher ?? throw new ArgumentNullException(nameof(ruleMatcher));
        }
    }
}
