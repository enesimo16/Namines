import { useEffect, useRef } from 'react';
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
    setRoomInfo, 
    setIsConnected, 
    updateCursor, 
    removeCursor,
    clearCursors 
  } = useMultiplayerStore();

  const showToast = useToastStore(state => state.showToast);

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  const isRemoteUpdateRef = useRef(false);
  const lastSentCursorRef = useRef({ x: 0, y: 0 });
  const schemaRef = useRef<DatabaseSchema | null>(null);
  
  // Keep schema ref fresh for the SignalR callbacks
  useEffect(() => {
    schemaRef.current = schema;
  }, [schema]);

  useEffect(() => {
    if (typeof window === 'undefined') return;

    // 1. Get or Generate Room ID
    const urlParams = new URLSearchParams(window.location.search);
    let currentRoomId = urlParams.get('roomId');
    if (!currentRoomId) {
      currentRoomId = 'room-' + Math.random().toString(36).substring(2, 11);
      const newUrl = window.location.protocol + '//' + window.location.host + window.location.pathname + '?roomId=' + currentRoomId;
      window.history.pushState({ path: newUrl }, '', newUrl);
    }

    // 2. Get or Generate UserName
    let currentUserName = localStorage.getItem('namines_username');
    if (!currentUserName) {
      currentUserName = 'Tasarımcı-' + Math.floor(Math.random() * 9000 + 1000);
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
      showToast(`${peerName} odaya katıldı!`, 'success');
    });

    connection.on('ReceiveSchema', (remoteSchema: DatabaseSchema) => {
      isRemoteUpdateRef.current = true;
      loadFromSchema(remoteSchema);
      // Reset ref shortly after loading to avoid echo loops
      setTimeout(() => {
        isRemoteUpdateRef.current = false;
      }, 300);
    });

    // Start Connection
    const start = async () => {
      try {
        await connection.start();
        setIsConnected(true);
        showToast('Eşzamanlı Tasarım Odası Bağlantısı Kuruldu!', 'success');
        await connection.invoke('JoinRoom', currentRoomId, currentUserName);
      } catch (err) {
        // Hatalar hızlı mount/unmount sırasında gürültü yapmaması için susturuldu
      }
    };

    start();

    // Clean up
    const handleWindowMouseMove = (e: MouseEvent) => {
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
      if (connectionRef.current) {
        const conn = connectionRef.current;
        connectionRef.current = null;

        if (conn.state === signalR.HubConnectionState.Connected) {
          conn.stop().catch(() => {});
        } else if (conn.state === signalR.HubConnectionState.Connecting) {
          // Wait for connection to finish handshaking before calling stop to avoid abort errors
          const stopInterval = setInterval(() => {
            if (conn.state === signalR.HubConnectionState.Connected) {
              conn.stop().catch(() => {});
              clearInterval(stopInterval);
            } else if (conn.state === signalR.HubConnectionState.Disconnected) {
              clearInterval(stopInterval);
            }
          }, 100);
          setTimeout(() => clearInterval(stopInterval), 5000);
        }
        setIsConnected(false);
      }
    };
  }, [loadFromSchema, setRoomInfo, setIsConnected, updateCursor, clearCursors]);

  // 5. Sync Local Schema Changes
  useEffect(() => {
    if (!isConnected || !connectionRef.current || !roomId || !schema) return;
    if (isRemoteUpdateRef.current) return;

    // Send update to peers only if connection is active
    if (connectionRef.current.state === signalR.HubConnectionState.Connected) {
      connectionRef.current.invoke('UpdateSchema', roomId, schema)
        .catch(() => {});
    }
  }, [schema, isConnected, roomId]);
}
