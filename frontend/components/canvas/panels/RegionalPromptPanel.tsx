import React, { useState, useRef } from 'react';
import { useReactFlow } from '@xyflow/react';
import { useSchemaStore } from '../../../store/useSchemaStore';
import { useToastStore } from '../../../store/useToastStore';
import { schemaService } from '../../../services/api';
import { Sparkles, Loader2, X } from 'lucide-react';
import { flowToSchema } from '../../../lib/flowToSchema';
import { SchemaRelation } from '../../../types/schema';
import Draggable from 'react-draggable';

export default function RegionalPromptPanel() {
  const { getNodes, getEdges } = useReactFlow();
  const { schema, applyRevision, aiProvider, modelName } = useSchemaStore();
  const showToast = useToastStore(state => state.showToast);
  const [prompt, setPrompt] = useState('');
  const [isRevising, setIsRevising] = useState(false);
  const nodeRef = useRef<HTMLDivElement>(null);

  const [position, setPosition] = useState<{ x: number; y: number }>(() => {
    if (typeof window !== 'undefined') {
      const saved = localStorage.getItem('namines-regional-prompt-pos');
      if (saved) {
        try {
          return JSON.parse(saved);
        } catch {}
      }
    }
    return { x: 0, y: 0 };
  });

  const handleStop = (e: any, data: { x: number; y: number }) => {
    const newPos = { x: data.x, y: data.y };
    setPosition(newPos);
    localStorage.setItem('namines-regional-prompt-pos', JSON.stringify(newPos));
  };

  // Get selected nodes
  const selectedNodes = getNodes().filter(n => n.selected && n.type === 'tableNode');
  
  if (selectedNodes.length === 0) return null;

  const handleRevise = async () => {
    if (!prompt.trim() || !schema) return;

    try {
      setIsRevising(true);
      
      const currentSchema = flowToSchema(schema, getNodes(), getEdges());
      if (!currentSchema) return;

      const selectedTableIds = selectedNodes.map(n => n.id);
      const selectedTables = currentSchema.tables.filter(t => selectedTableIds.includes(t.id));
      
      // Get relations involving selected tables
      const existingRelations = currentSchema.relations.filter(r => 
        selectedTableIds.includes(r.sourceTableId) || selectedTableIds.includes(r.targetTableId)
      );

      const partialSchema = await schemaService.reviseSchema(selectedTables, existingRelations, prompt, aiProvider, modelName);
      
      applyRevision(partialSchema);
      setPrompt('');
    } catch (error) {
      console.error("Revision failed", error);
      showToast("An error occurred during revision.", "error");
    } finally {
      setIsRevising(false);
    }
  };

  return (
    <Draggable 
      nodeRef={nodeRef} 
      bounds="parent" 
      handle=".drag-handle"
      position={position}
      onStop={handleStop}
    >
      {/* 
        Positioning logic: To center a Draggable, we use percentages for top/left and 
        subtract half the width using calc() instead of transform: translate 
        to avoid conflicts with Draggable's own transform styles.
      */}
      <div ref={nodeRef} className="absolute top-[15%] left-8 z-[100] w-[340px] font-sans">
        
        {/* Outer Frame Wrapper */}
        <div className="relative bg-[#171D31] rounded-[20px] p-[5px] border border-[#2b375b] shadow-[0_20px_60px_rgba(0,0,0,0.6)] overflow-hidden">
          
          {/* Decorative Corner Stars */}
          <div className="absolute top-2 left-2.5 text-[#7d91be] text-[10px] pointer-events-none">✦</div>
          <div className="absolute top-2 right-2.5 text-[#7d91be] text-[10px] pointer-events-none">✦</div>
          <div className="absolute bottom-2 left-2.5 text-[#7d91be] text-[10px] pointer-events-none">✦</div>
          <div className="absolute bottom-2 right-2.5 text-[#7d91be] text-[10px] pointer-events-none">✦</div>
 
          {/* Inner Panel Background */}
          <div className="relative bg-gradient-to-b from-[#1c2445] to-[#141b31] border border-[#364472] rounded-[15px] h-full overflow-hidden flex flex-col">
            
            {/* Background Waves (SVG) */}
            <svg className="absolute bottom-0 left-0 w-full h-[150px] opacity-40 pointer-events-none" viewBox="0 0 1000 200" preserveAspectRatio="none">
              <path fill="none" stroke="rgba(129, 140, 248, 0.3)" strokeWidth="1.5" d="M0,150 C200,200 300,50 500,100 C700,150 800,50 1000,150" />
              <path fill="none" stroke="rgba(99, 102, 241, 0.4)" strokeWidth="1" d="M0,160 C250,220 350,30 500,120 C650,210 750,70 1000,160" />
              <path fill="none" stroke="rgba(79, 70, 229, 0.3)" strokeWidth="0.5" d="M0,170 C300,240 400,20 500,140 C600,260 700,90 1000,170" />
              <path fill="none" stroke="rgba(255, 255, 255, 0.1)" strokeWidth="1" d="M0,180 C150,210 450,10 500,160 C550,310 850,110 1000,180" />
              <path fill="none" stroke="rgba(165, 180, 252, 0.15)" strokeWidth="1.5" d="M0,120 C180,180 320,-20 500,110 C680,240 820,40 1000,120" />
            </svg>
 
            {/* Header */}
            <div className="drag-handle bg-gradient-to-b from-[#1b2647] to-[#17203b] border-b border-[#364472] px-4 py-3.5 rounded-t-[15px] flex justify-center items-center cursor-move relative z-10 shadow-[0_4px_15px_rgba(0,0,0,0.2)]">
              <div className="flex items-center gap-2">
                <span className="text-[#a5b4fc] text-[12px]">✦</span>
                <span className="text-[#f1f5f9] text-[15px] font-bold tracking-wide drop-shadow-md animate-none">
                  Regional Revision ({selectedNodes.length} Selected)
                </span>
                <span className="text-[#a5b4fc] text-[12px]">✦</span>
              </div>
            </div>
 
            {/* Content Area */}
            <div className="p-5 space-y-5 flex flex-col relative z-10">
              
              {/* Textarea Wrapper (Glowing Frame) */}
              <div className="relative rounded-[10px] bg-gradient-to-r from-[#4f46e5]/60 via-[#818cf8]/60 to-[#4f46e5]/60 p-[1.5px] shadow-[0_0_20px_rgba(99,102,241,0.25)]">
                <textarea
                  value={prompt}
                  onChange={(e) => setPrompt(e.target.value)}
                  placeholder="How would you like to modify the selected tables?"
                  className="w-full h-[90px] bg-[#0c1222] rounded-[8px] p-3 text-[14px] text-[#f8fafc] placeholder:text-[#64748b] focus:outline-none resize-none"
                  disabled={isRevising}
                  style={{
                    backgroundImage: 'radial-gradient(1.5px 1.5px at 15% 25%, rgba(255,255,255,0.4), transparent), radial-gradient(1px 1px at 85% 35%, rgba(255,255,255,0.4), transparent), radial-gradient(1.5px 1.5px at 25% 75%, rgba(255,255,255,0.3), transparent), radial-gradient(1px 1px at 75% 85%, rgba(255,255,255,0.5), transparent)'
                  }}
                />
              </div>
 
              {/* Button */}
              <button
                onClick={handleRevise}
                disabled={isRevising || !prompt.trim()}
                className="group relative w-full flex items-center justify-center gap-2 bg-gradient-to-r from-[#4f46e5] to-[#6366f1] hover:from-[#5b4ff8] hover:to-[#818cf8] text-white px-4 py-3 rounded-[10px] text-[15px] font-bold transition-all disabled:opacity-50 disabled:cursor-not-allowed border border-[#818cf8]/40 shadow-[0_0_25px_rgba(79,70,229,0.6)] overflow-hidden animate-none"
              >
                {/* Button Starry Background */}
                <div 
                  className="absolute inset-0 opacity-50 mix-blend-screen pointer-events-none"
                  style={{
                    backgroundImage: 'radial-gradient(2px 2px at 20% 30%, rgba(255,255,255,0.9), transparent), radial-gradient(1.5px 1.5px at 80% 40%, rgba(255,255,255,0.8), transparent), radial-gradient(2px 2px at 40% 70%, rgba(255,255,255,0.9), transparent), radial-gradient(1.5px 1.5px at 70% 80%, rgba(255,255,255,0.7), transparent)'
                  }}
                />
                
                {isRevising ? (
                  <>
                    <Loader2 className="w-5 h-5 animate-spin relative z-10" />
                    <span className="relative z-10 tracking-wider">Revising...</span>
                  </>
                ) : (
                  <>
                    <Sparkles className="w-4 h-4 relative z-10 text-white animate-none" />
                    <span className="relative z-10 tracking-wider">Revise with AI</span>
                  </>
                )}
              </button>
            </div>
            
          </div>
        </div>
      </div>
    </Draggable>
  );
}
