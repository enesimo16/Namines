using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Namines.Core.Events;
using Namines.Infrastructure.Observability;

namespace Namines.Infrastructure.Events
{
    /// <summary>
    /// Namines Modular Monolith içi modüller arası asenkron olay iletimini yöneten servis.
    /// Her olay yayınında DI kapsamı oluşturarak kayıtlı tüm <see cref="IModuleEventHandler{TEvent}"/>
    /// işleyicilerini izole olarak çalıştırır ve olası hataları loglayarak diğer işleyicileri korur.
    /// OpenTelemetry W3C distributed trace ve Prometheus metrikleri ile donatılmıştır.
    /// </summary>
    public sealed class ModuleEventBus : IModuleEventBus
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ModuleEventBus> _logger;

        public ModuleEventBus(IServiceScopeFactory scopeFactory, ILogger<ModuleEventBus> logger)
        {
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default)
            where TEvent : IModuleEvent
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));

            var eventName = typeof(TEvent).Name;
            using var activity = NaminesActivitySource.StartEventPublish(eventName);
            activity?.SetTag("event.id", @event.EventId.ToString());

            var sw = Stopwatch.StartNew();
            bool hasError = false;

            using var scope = _scopeFactory.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IModuleEventHandler<TEvent>>();

            foreach (var handler in handlers)
            {
                try
                {
                    await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    hasError = true;
                    activity?.SetStatus(ActivityStatusCode.Error, ex.Message);

                    _logger.LogError(ex,
                        "Modül olayı işlenirken hata oluştu. Event: {EventType}, EventId: {EventId}, Handler: {HandlerType}",
                        eventName,
                        @event.EventId,
                        handler.GetType().FullName);
                }
            }

            NaminesMetrics.EventPublished(eventName, !hasError, sw.Elapsed);
        }
    }
}
