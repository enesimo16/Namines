import React, { useState, useRef } from 'react';
import { useLinterStore } from '../../../store/useLinterStore';
import { AlertTriangle, XCircle, Info, ChevronUp, ChevronDown } from 'lucide-react';
import Draggable from 'react-draggable';

export default function LinterPanel() {
  const { result, isLinting } = useLinterStore();
  const [isExpanded, setIsExpanded] = useState(true);
  const nodeRef = useRef<HTMLDivElement>(null);

  if (!result || result.messages.length === 0) return null;

  const errors = result.messages.filter(m => m.severity === 2);
  const warnings = result.messages.filter(m => m.severity === 1);
  const infos = result.messages.filter(m => m.severity === 0);

  return (
    <Draggable nodeRef={nodeRef} bounds="parent" handle=".drag-handle">
      <div ref={nodeRef} className="absolute bottom-20 right-4 z-50 bg-zinc-900 border border-zinc-800 rounded-xl shadow-2xl w-80 overflow-hidden font-sans">
      {/* Header */}
      <div 
        className="drag-handle flex items-center justify-between px-4 py-3 cursor-move bg-zinc-800/50 hover:bg-zinc-800 transition-colors"
      >
        <div className="flex items-center gap-2">
          {errors.length > 0 ? (
            <XCircle className="w-5 h-5 text-red-500" />
          ) : warnings.length > 0 ? (
            <AlertTriangle className="w-5 h-5 text-yellow-500" />
          ) : (
            <Info className="w-5 h-5 text-blue-500" />
          )}
          <span className="font-semibold text-zinc-100 text-sm">
            Linter ({result.messages.length})
          </span>
          {isLinting && <span className="text-xs text-zinc-500 animate-pulse ml-2">Denetleniyor...</span>}
        </div>
        <button onClick={() => setIsExpanded(!isExpanded)} className="p-1 hover:bg-white/10 rounded">
          {isExpanded ? <ChevronDown className="w-4 h-4 text-zinc-400" /> : <ChevronUp className="w-4 h-4 text-zinc-400" />}
        </button>
      </div>

      {/* Body */}
      {isExpanded && (
        <div className="max-h-60 overflow-y-auto p-2 space-y-2">
          {result.messages.map((msg, idx) => {
            let bgColor = "bg-blue-500/10 border-blue-500/20";
            let textColor = "text-blue-400";
            let Icon = Info;
            
            if (msg.severity === 2) {
              bgColor = "bg-red-500/10 border-red-500/20";
              textColor = "text-red-400";
              Icon = XCircle;
            } else if (msg.severity === 1) {
              bgColor = "bg-yellow-500/10 border-yellow-500/20";
              textColor = "text-yellow-400";
              Icon = AlertTriangle;
            }

            return (
              <div key={idx} className={`flex items-start gap-2 p-2 rounded border ${bgColor}`}>
                <Icon className={`w-4 h-4 mt-0.5 shrink-0 ${textColor}`} />
                <span className="text-xs text-zinc-300 leading-relaxed">{msg.message}</span>
              </div>
            );
          })}
        </div>
      )}
      </div>
    </Draggable>
  );
}
