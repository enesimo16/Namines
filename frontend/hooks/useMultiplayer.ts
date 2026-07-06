import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useSchemaStore } from '../store/useSchemaStore';
import { useMultiplayerStore } from '../store/useMultiplayerStore';
import { DatabaseSchema } from '../types/schema';
import { useToastStore } from '../store/useToastStore';

export function useMultiplayer() {
  const { schema, loadFromSchema } = useSchemaStore();
  const { 
    roomId, 
    userName, 
    isConnected, 
    isOffline,
    setRoomInfo, 
    setIsConnected, 
    setIsOffline,
    updateCursor, 
    removeCursor,
    clearCursors 
  } = useMultiplayerStore();

  const showToast = useToastStore(state => state.showToast);

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const isRemoteUpdateRef = useRef(false);
  const remoteUpdateTimeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const lastSentCursorRef = useRef({ x: 0, y: 0 });
  const schemaRef = useRef<DatabaseSchema | null>(null);
  
  // Initialize from URL directly if present to prevent double-mount race conditions
  const [roomIdFromUrl, setRoomIdFromUrl] = useState<string | null>(() => {
    if (typeof window !== 'undefined') {
      const urlParams = new URLSearchParams(window.location.search);
      return urlParams.get('roomId');
    }
    return null;
  });

  // Monitor the URL for room ID changes (polling is safe, simple, and avoids Next.js Suspense warnings)
  useEffect(() => {
    if (typeof window === 'undefined') return;

    const checkRoomId = () => {
      const urlParams = new URLSearchParams(window.location.search);
      const rId = urlParams.get('roomId');
      setRoomIdFromUrl(prev => {
        if (prev !== rId) {
          return rId;
        }
        return prev;
      });
    };

    checkRoomId();
    const interval = setInterval(checkRoomId, 500);
    window.addEventListener('popstate', checkRoomId);

    return () => {
      clearInterval(interval);
      window.removeEventListener('popstate', checkRoomId);
    };
  }, []);

  // Keep schema ref fresh for the SignalR callbacks
  useEffect(() => {
    schemaRef.current = schema;
  }, [schema]);

  useEffect(() => {
    if (typeof window === 'undefined') return;

    // 1. Get or Generate Room ID
    let currentRoomId = roomIdFromUrl;
    if (!currentRoomId) {
      const urlParams = new URLSearchParams(window.location.search);
      currentRoomId = urlParams.get('roomId');
      if (!currentRoomId) {
        currentRoomId = 'room-' + Math.random().toString(36).substring(2, 11);
        const newUrl = window.location.protocol + '//' + window.location.host + window.location.pathname + '?roomId=' + currentRoomId;
        window.history.pushState({ path: newUrl }, '', newUrl);
        setRoomIdFromUrl(currentRoomId);
        return; // Exit early, the state update will trigger the effect again with the correct roomIdFromUrl
      }
    }

    // 2. Get or Generate UserName
    let currentUserName = localStorage.getItem('namines_username');
    if (!currentUserName) {
      currentUserName = 'Designer-' + Math.floor(Math.random() * 9000 + 1000);
      localStorage.setItem('namines_username', currentUserName);
    }

    setRoomInfo(currentRoomId, currentUserName);

    // 3. Connect to SignalR Hub
    const connection = new signalR.HubConnectionBuilder()
      .withUrl('http://localhost:5000/hubs/canvas', {
        skipNegotiation: true,
        transport: signalR.HttpTransportType.WebSockets
      })
      .withAutomaticReconnect()
      .configureLogging(signalR.LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    // Register callbacks
    connection.on('ReceiveCursor', (connectionId: string, peerName: string, x: number, y: number) => {
      const colors = ['#FF007F', '#FFD700', '#00FFCC', '#FF5733', '#9D00FF', '#39FF14', '#00FFFF'];
      const hash = Array.from(connectionId).reduce((acc, char) => acc + char.charCodeAt(0), 0);
      const color = colors[hash % colors.length];

      updateCursor(connectionId, { userName: peerName, x, y, color });
    });

    connection.on('ReceiveUserJoined', (connectionId: string, peerName: string) => {
      showToast(`${peerName} joined the room!`, 'success');
      if (schemaRef.current) {
        connection.invoke('SendSchemaToPeer', connectionId, schemaRef.current)
          .catch(() => {});
      }
    });

    connection.on('ReceiveSchema', (remoteSchema: DatabaseSchema) => {
      isRemoteUpdateRef.current = true;
      loadFromSchema(remoteSchema);
      
      if (remoteUpdateTimeoutRef.current) {
        clearTimeout(remoteUpdateTimeoutRef.current);
      }
      
      remoteUpdateTimeoutRef.current = setTimeout(() => {
        isRemoteUpdateRef.current = false;
        remoteUpdateTimeoutRef.current = null;
      }, 300);
    });

    // Reconnection & connection status callbacks
    connection.onreconnecting((error) => {
      if (connectionRef.current === connection) {
        setIsOffline(true);
        showToast('⚠️ Reconnecting to multiplayer room...', 'warning');
      }
    });

    connection.onreconnected((connectionId) => {
      if (connectionRef.current === connection) {
        setIsOffline(false);
        showToast('✅ Reconnected to multiplayer room.', 'success');
      }
    });

    connection.onclose((error) => {
      if (connectionRef.current === connection) {
        setIsConnected(false);
        setIsOffline(true);
        showToast('❌ Disconnected from multiplayer room.', 'error');
      }
    });

    // Start Connection
    const start = async () => {
      try {
        await connection.start();
        if (connectionRef.current === connection) {
          setIsConnected(true);
          setIsOffline(false);
          showToast('Real-time Collaborative Room Connection Established!', 'success');
          await connection.invoke('JoinRoom', currentRoomId, currentUserName);
        }
      } catch (err) {
        // Errors silenced to avoid noise during fast mount/unmount
      }
    };

    start();

    // Browser network state listeners
    const handleOffline = () => {
      if (connectionRef.current === connection) {
        setIsOffline(true);
        showToast('⚠️ Connection lost. Canvas is now read-only.', 'warning');
      }
    };

    const handleOnline = () => {
      if (connectionRef.current === connection) {
        setIsOffline(false);
        showToast('✅ Connection restored. Syncing...', 'success');
        if (connection.state === signalR.HubConnectionState.Disconnected) {
          start();
        }
      }
    };

    window.addEventListener('offline', handleOffline);
    window.addEventListener('online', handleOnline);

    // Clean up
    const handleWindowMouseMove = (e: MouseEvent) => {
      if (useMultiplayerStore.getState().isOffline) return; // Don't track/send cursor position if offline
      if (!connection.state || connection.state !== signalR.HubConnectionState.Connected) return;

      const x = e.clientX;
      const y = e.clientY;
      const dist = Math.hypot(x - lastSentCursorRef.current.x, y - lastSentCursorRef.current.y);
      if (dist < 15) return;

      lastSentCursorRef.current = { x, y };

      connection.invoke('MoveCursor', currentRoomId, currentUserName, x, y)
        .catch(() => {});
    };

    window.addEventListener('mousemove', handleWindowMouseMove);

    // Clean up
    return () => {
      clearCursors();
      window.removeEventListener('mousemove', handleWindowMouseMove);
      window.removeEventListener('offline', handleOffline);
      window.removeEventListener('online', handleOnline);

      if (connectionRef.current === connection) {
        connectionRef.current = null;
      }

      if (connection.state === signalR.HubConnectionState.Connected) {
        connection.stop().catch(() => {});
      } else if (connection.state === signalR.HubConnectionState.Connecting) {
        // Wait for connection to finish handshaking before calling stop to avoid abort errors
        const stopInterval = setInterval(() => {
          if (connection.state === signalR.HubConnectionState.Connected) {
            connection.stop().catch(() => {});
            clearInterval(stopInterval);
          } else if (connection.state === signalR.HubConnectionState.Disconnected) {
            clearInterval(stopInterval);
          }
        }, 100);
        setTimeout(() => clearInterval(stopInterval), 5000);
      }

      if (connectionRef.current === null) {
        setIsConnected(false);
        setIsOffline(false);
      }
    };
  }, [loadFromSchema, setRoomInfo, setIsConnected, setIsOffline, updateCursor, clearCursors, roomIdFromUrl]);

  // 5. Sync Local Schema Changes
  useEffect(() => {
    if (!isConnected || isOffline || !connectionRef.current || !roomId || !schema) return;
    if (isRemoteUpdateRef.current) return;

    // Send update to peers only if connection is active
    if (connectionRef.current.state === signalR.HubConnectionState.Connected) {
      connectionRef.current.invoke('UpdateSchema', roomId, schema)
        .catch(() => {});
    }
  }, [schema, isConnected, isOffline, roomId]);
}
