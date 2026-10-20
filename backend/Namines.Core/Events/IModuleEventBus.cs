using System;
using System.Threading;
using System.Threading.Tasks;

namespace Namines.Core.Events
{
    /// <summary>
    /// Modular Monolith mimarisinde modüller arası taşınan in-process domain olay sözleşmesi.
    /// </summary>
    public interface IModuleEvent
    {
        string EventId { get; }
        DateTime OccurredAtUtc { get; }
    }

    /// <summary>
    /// Belirli bir <typeparamref name="TEvent"/> tipindeki modül olaylarını dinleyen işleyici sözleşmesi.
    /// </summary>
    public interface IModuleEventHandler<in TEvent> where TEvent : IModuleEvent
    {
        Task HandleAsync(TEvent @event, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Modüllerin birbirlerinin somut sınıflarına bağımlı olmadan olay fırlatmasını sağlayan
    /// in-process asenkron olay veri yolu (In-Process Event Bus).
    /// </summary>
    public interface IModuleEventBus
    {
        Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IModuleEvent;
    }
}
