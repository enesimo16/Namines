using System;
using System.Collections.Generic;
using System.Linq;
using Namines.AI.Domain;
using Namines.Core.Analysis;

namespace Namines.AI.Application;

/// <summary>
/// Dinamik sub-agent üretme, ajanlar arası mesajlaşma, emekliye ayırma ve silme işlemlerini
/// iş parçacığı güvenli şekilde yürüten sürü çalışma zamanı.
/// </summary>
public sealed class AgentSwarmRuntime : IAgentSwarmRuntime
{
    private readonly object _sync = new();
    private readonly Dictionary<string, AgentInstance> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<AgentMessage> _messages = new();
    private int _sequence;

    public AgentInstance SpawnAgent(
        AgentKind kind,
        string name,
        string purpose,
        NaiModel modelTier = NaiModel.Standard)
    {
        lock (_sync)
        {
            _sequence++;
            var safeName = string.IsNullOrWhiteSpace(name) ? kind.ToString() : name.Trim();
            var agentId = $"{kind.ToString().ToLowerInvariant()}-{_sequence}";
            var instance = new AgentInstance(
                AgentId: agentId,
                Name: safeName,
                Kind: kind,
                ModelTier: modelTier,
                State: AgentLifecycleState.Working,
                SpawnedAt: DateTimeOffset.UtcNow,
                Purpose: purpose ?? string.Empty);

            _agents[agentId] = instance;
            return instance;
        }
    }

    public void SendMessage(string fromAgentId, string toAgentId, string topic, string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            return;

        lock (_sync)
        {
            var msg = new AgentMessage(
                MessageId: $"msg-{_messages.Count + 1}",
                FromAgentId: string.IsNullOrWhiteSpace(fromAgentId) ? "system" : fromAgentId,
                ToAgentId: string.IsNullOrWhiteSpace(toAgentId) ? "master" : toAgentId,
                Topic: string.IsNullOrWhiteSpace(topic) ? "status" : topic,
                Content: content,
                SentAt: DateTimeOffset.UtcNow);

            _messages.Add(msg);
        }
    }

    public IReadOnlyList<AgentMessage> GetMessagesFor(string agentId)
    {
        lock (_sync)
        {
            return _messages
                .Where(m => string.Equals(m.ToAgentId, agentId, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(m.ToAgentId, "broadcast", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
    }

    public IReadOnlyList<AgentMessage> GetAllMessages()
    {
        lock (_sync)
            return _messages.ToList();
    }

    public void RetireAgent(string agentId)
    {
        lock (_sync)
        {
            if (_agents.TryGetValue(agentId, out var existing))
            {
                _agents[agentId] = existing with
                {
                    State = AgentLifecycleState.Retired,
                    RetiredAt = DateTimeOffset.UtcNow,
                };
            }
        }
    }

    public void TerminateAgent(string agentId)
    {
        lock (_sync)
        {
            _agents.Remove(agentId);
        }
    }

    public IReadOnlyList<AgentInstance> GetActiveAgents()
    {
        lock (_sync)
        {
            return _agents.Values
                .Where(a => a.State is AgentLifecycleState.Spawned
                    or AgentLifecycleState.Working
                    or AgentLifecycleState.Idle)
                .ToList();
        }
    }

    public (int Spawned, int Retired, int Active) GetSwarmStats()
    {
        lock (_sync)
        {
            var retired = _agents.Values.Count(a => a.State == AgentLifecycleState.Retired);
            var active = _agents.Values.Count(a => a.State is AgentLifecycleState.Spawned
                or AgentLifecycleState.Working
                or AgentLifecycleState.Idle);
            return (_sequence, retired, active);
        }
    }
}
