using Namines.Gateway.Application;

namespace Namines.Infrastructure.Observability;

/// <summary>Gateway kapısının ölçümünü süreç sayacına bağlar; modül sayaç altyapısını tanımaz.</summary>
public sealed class GatewayMetricsAdapter : IGatewayMetrics
{
    public void Request(string operation, string outcome) => NaminesMetrics.GatewayRequest(operation, outcome);
}
