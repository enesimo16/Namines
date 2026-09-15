using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Namines.Core.Models;
using Namines.Infrastructure.Services;
using Xunit;

namespace Namines.Tests.Services;

public class AutomationJobQueueTests
{
    private static AutomationJob Job(string projectId) =>
        new(projectId, new SchemaDiffResult(), new DatabaseSchema(), new DatabaseSchema());

    [Fact]
    public async Task Kuyruga_eklenen_is_sirayla_geri_aliniyor()
    {
        var queue = new AutomationJobQueue();

        Assert.True(queue.TryEnqueue(Job("p1")));
        Assert.True(queue.TryEnqueue(Job("p2")));

        Assert.Equal("p1", (await queue.DequeueAsync(CancellationToken.None)).ProjectId);
        Assert.Equal("p2", (await queue.DequeueAsync(CancellationToken.None)).ProjectId);
    }

    [Fact]
    public void Kuyruk_dolunca_BEKLEMEDEN_reddediyor()
    {
        var queue = new AutomationJobQueue();

        var accepted = Enumerable.Range(0, 100).Count(i => queue.TryEnqueue(Job($"p{i}")));

        Assert.True(accepted < 100);
        Assert.False(queue.TryEnqueue(Job("bir-fazla")));
    }

    [Fact]
    public void Null_is_reddediliyor()
    {
        var queue = new AutomationJobQueue();
        Assert.Throws<ArgumentNullException>(() => queue.TryEnqueue(null!));
    }
}
