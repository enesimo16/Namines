using Namines.Infrastructure.Realtime;

namespace Namines.Tests.Realtime;

public class InMemoryPresenceStoreTests
{
    [Fact]
    public async Task Set_then_get_roundtrips()
    {
        var store = new InMemoryPresenceStore();

        await store.SetRoomAsync("conn1", "room-abc");
        var result = await store.TryGetRoomAsync("conn1");

        Assert.Equal("room-abc", result);
    }

    [Fact]
    public async Task Get_unknown_connection_returns_null()
    {
        var store = new InMemoryPresenceStore();

        var result = await store.TryGetRoomAsync("never-joined");

        Assert.Null(result);
    }

    [Fact]
    public async Task Remove_returns_the_room_that_was_removed()
    {
        var store = new InMemoryPresenceStore();
        await store.SetRoomAsync("conn1", "room-xyz");

        var removed = await store.RemoveAsync("conn1");

        Assert.Equal("room-xyz", removed);
    }

    [Fact]
    public async Task Remove_clears_the_entry_so_a_second_get_returns_null()
    {
        var store = new InMemoryPresenceStore();
        await store.SetRoomAsync("conn1", "room-xyz");
        await store.RemoveAsync("conn1");

        var result = await store.TryGetRoomAsync("conn1");

        Assert.Null(result);
    }

    [Fact]
    public async Task Remove_unknown_connection_returns_null_not_exception()
    {
        var store = new InMemoryPresenceStore();

        var result = await store.RemoveAsync("never-existed");

        Assert.Null(result);
    }

    [Fact]
    public async Task Setting_room_again_overwrites_previous_value()
    {
        // Yeniden bağlanma senaryosu (aynı ConnectionId farklı SignalR reconnect'inde
        // yeniden atanır aslında ama farklı roomId'lere JoinRoom çağrılabilir).
        var store = new InMemoryPresenceStore();
        await store.SetRoomAsync("conn1", "room-old");
        await store.SetRoomAsync("conn1", "room-new");

        var result = await store.TryGetRoomAsync("conn1");

        Assert.Equal("room-new", result);
    }

    [Fact]
    public async Task Different_connections_are_isolated()
    {
        var store = new InMemoryPresenceStore();
        await store.SetRoomAsync("conn1", "room-a");
        await store.SetRoomAsync("conn2", "room-b");

        Assert.Equal("room-a", await store.TryGetRoomAsync("conn1"));
        Assert.Equal("room-b", await store.TryGetRoomAsync("conn2"));
    }
}
