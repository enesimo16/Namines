using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Models;
using Namines.Flow.Abstractions;

namespace Namines.Flow.Services
{
    /// <summary>
    /// Namines Flow modülünün kural eşleme motoru implementasyonu.
    /// </summary>
    public class FlowRuleMatcher : IFlowRuleMatcher
    {
        public IReadOnlyList<AutomationMatch> Match(
            SchemaDiffResult diff,
            DatabaseSchema oldSchema,
            DatabaseSchema newSchema,
            IReadOnlyList<AutomationRule> rules)
        {
            return AutomationRuleMatcher.Match(diff, oldSchema, newSchema, rules);
        }
    }
}
