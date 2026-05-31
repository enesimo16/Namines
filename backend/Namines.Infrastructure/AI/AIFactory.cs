using System;
using System.Collections.Generic;
using Namines.Core.Interfaces;

namespace Namines.Infrastructure.AI;

public class AIFactory : IAIFactory
{
    private readonly IEnumerable<IAIService> _services;

    public AIFactory(IEnumerable<IAIService> services)
    {
        _services = services;
    }

    public IAIService GetService(string provider)
    {
        if (string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var service in _services)
            {
                if (service is OllamaAIService) return service;
            }
            throw new Exception("Ollama service not found.");
        }
        else // Default to Groq
        {
            foreach (var service in _services)
            {
                if (service is GroqAIService) return service;
            }
            throw new Exception("Groq service not found.");
        }
    }
}
