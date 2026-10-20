namespace Namines.Desk.Abstractions
{
    /// <summary>
    /// Namines Desk modülünün dış dünyaya sunduğu üst seviye modül sözleşmesi (Modular Monolith Facade).
    /// </summary>
    public interface IDeskModule
    {
        IDeskHandoffTokenValidator TokenValidator { get; }
    }
}
