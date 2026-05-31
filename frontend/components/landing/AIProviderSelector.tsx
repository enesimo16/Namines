import React from 'react';
import { useSchemaStore } from '../../store/useSchemaStore';

export default function AIProviderSelector() {
  const { aiProvider, modelName, setProviderAndModel } = useSchemaStore();

  const groqModels = [
    { id: 'llama-3.3-70b-versatile', label: 'Llama 3.3 (70B)' },
    { id: 'llama-3.1-8b-instant', label: 'Llama 3.1 (8B)' },
    { id: 'mixtral-8x7b-32768', label: 'Mixtral 8x7B' }
  ];

  const ollamaModels = [
    { id: 'qwen2.5-coder', label: 'Qwen 2.5 Coder' },
    { id: 'deepseek-coder', label: 'DeepSeek Coder' },
    { id: 'sqlcoder', label: 'SQLCoder' }
  ];

  const models = aiProvider === 'Groq' ? groqModels : ollamaModels;

  return (
    <div className="flex flex-wrap md:flex-nowrap items-center gap-3 w-full md:w-auto">
      {/* Provider Toggle */}
      <div className="flex bg-ocean-dark/80 rounded-lg p-1 border border-white/5">
        <button
          type="button"
          onClick={() => setProviderAndModel('Groq', groqModels[0].id)}
          className={`flex items-center gap-2 px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
            aiProvider === 'Groq'
              ? 'bg-ocean-light/50 text-indigo-400 border border-indigo-500/30'
              : 'text-gray-500 hover:text-gray-300'
          }`}
        >
          <i className="fa-solid fa-cloud text-[10px]"></i>
          <div className="text-left leading-tight">
            <div>Groq</div>
            <div className="text-[9px] opacity-70">(Cloud)</div>
          </div>
        </button>
        
        <button
          type="button"
          onClick={() => setProviderAndModel('Ollama', ollamaModels[0].id)}
          className={`flex items-center gap-2 px-3 py-1.5 rounded-md text-xs font-medium transition-colors ${
            aiProvider === 'Ollama'
              ? 'bg-ocean-light/50 text-emerald-400 border border-emerald-500/30'
              : 'text-gray-500 hover:text-gray-300'
          }`}
        >
          <i className="fa-solid fa-server text-[10px]"></i>
          <div className="text-left leading-tight">
            <div>Ollama</div>
            <div className="text-[9px] opacity-70">(Local)</div>
          </div>
        </button>
      </div>

      {/* Model Select */}
      <div className="relative hidden sm:block">
        <select
          value={modelName}
          onChange={(e) => setProviderAndModel(aiProvider, e.target.value)}
          className="appearance-none glass-input rounded-lg pl-3 pr-8 py-2 text-sm text-gray-300 focus:ring-0 cursor-pointer w-[160px]"
        >
          {models.map(m => (
            <option key={m.id} value={m.id}>{m.label}</option>
          ))}
        </select>
        <div className="pointer-events-none absolute inset-y-0 right-0 flex items-center px-2 text-gray-400">
          <i className="fa-solid fa-chevron-down text-xs"></i>
        </div>
      </div>
    </div>
  );
}
