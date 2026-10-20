using System.Collections.Generic;
using Namines.Core.Analysis;
using Namines.Core.Models;

namespace Namines.Flow.Abstractions
{
    /// <summary>
    /// Namines Flow otomasyon tetikleyicilerini şema değişiklikleriyle eşleştiren sözleşme.
    /// </summary>
    public interface IFlowRuleMatcher
    {
        IReadOnlyList<AutomationMatch> Match(
            SchemaDiffResult diff,
            DatabaseSchema oldSchema,
            DatabaseSchema newSchema,
            IReadOnlyList<AutomationRule> rules);
    }
}
