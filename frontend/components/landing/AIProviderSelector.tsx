import React from 'react';
import { useSchemaStore } from '../../store/useSchemaStore';

export default function AIProviderSelector() {
  const { aiProvider, setProviderAndModel } = useSchemaStore();

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

  return (
    <div className="flex bg-ocean-dark/80 rounded-lg p-1 border border-white/5 shrink-0">
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
  );
}
