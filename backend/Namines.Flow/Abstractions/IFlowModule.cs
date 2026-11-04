using Namines.Flow.Application;

namespace Namines.Flow.Abstractions
{
    /// <summary>
    /// Namines Flow modülünün dış dünyaya sunduğu üst seviye modül sözleşmesi (Modular Monolith Facade).
    /// </summary>
    public interface IFlowModule
    {
        IFlowRuleMatcher RuleMatcher { get; }
        IFlowApplicationService Application { get; }
        IFlowJobQueue JobQueue { get; }
    }
}
