namespace Namines.Flow.Abstractions
{
    /// <summary>
    /// Namines Flow koşul zincirlerini (trigger payload) değerlendiren sözleşme.
    /// </summary>
    public interface IFlowConditionEvaluator
    {
        bool Evaluate(string? conditionsJson, object? context);
    }
}
