import React from 'react';
import { useMultiplayerStore } from '../../store/useMultiplayerStore';

export default function MultiplayerCursors() {
  const cursors = useMultiplayerStore(state => state.cursors);

  return (
    <div className="absolute inset-0 pointer-events-none z-[60] overflow-hidden">
      {Object.entries(cursors).map(([connectionId, pos]) => (
        <div
          key={connectionId}
          className="absolute transition-all duration-100 ease-out"
          style={{
            left: pos.x,
            top: pos.y,
            transform: 'translate(-2px, -2px)',
          }}
        >
          {/* Figma Style Cursor Arrow */}
          <svg
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            className="drop-shadow-md"
          >
            <path
              d="M3 3V21L10 14H20L3 3Z"
              fill={pos.color}
              stroke="white"
              strokeWidth="2"
              strokeLinejoin="round"
            />
          </svg>

          {/* User Name Badge */}
          <div
            className="ml-2.5 px-2 py-0.5 rounded text-[10px] font-extrabold text-white shadow-md border animate-in fade-in zoom-in-90 duration-150 whitespace-nowrap"
            style={{
              backgroundColor: pos.color,
              borderColor: 'rgba(255, 255, 255, 0.4)',
            }}
          >
            {pos.userName}
          </div>
        </div>
      ))}
    </div>
  );
}
