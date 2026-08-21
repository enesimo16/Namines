import { useEffect, useRef, useState } from 'react';
import * as signalR from '@microsoft/signalr';
import { useSchemaStore } from '../store/useSchemaStore';
import { useMultiplayerStore } from '../store/useMultiplayerStore';
import { useAuthStore } from '../store/useAuthStore';
import { useProjectHistoryStore } from '../store/useProjectHistoryStore';
import { DatabaseSchema } from '../types/schema';
import { useToastStore } from '../store/useToastStore';
import { CANVAS_HUB_URL } from '../lib/apiConfig';
import { branchService } from '../services/api';
import { screenToFlowPosition } from '../lib/flowCoords';
import { mergeSchemas } from '../utils/mergeSchemas';

export function useMultiplayer() {
  // Dar selector'lar. Selector'suz `useMultiplayerStore()` `cursors`a da abone olur;
  // her uzak imleç paketi (saniyede onlarca) bu hook'u çağıran canvas sayfasının
  // TAMAMINI yeniden render ederdi. Aynısı `useSchemaStore()` için de geçerli.
  const schema = useSchemaStore(s => s.schema);
  const loadFromSchema = useSchemaStore(s => s.loadFromSchema);

  const roomId = useMultiplayerStore(s => s.roomId);
  const userName = useMultiplayerStore(s => s.userName);
  const isConnected = useMultiplayerStore(s => s.isConnected);
  const isOffline = useMultiplayerStore(s => s.isOffline);
  const setRoomInfo = useMultiplayerStore(s => s.setRoomInfo);
  const setIsConnected = useMultiplayerStore(s => s.setIsConnected);
  const setIsOffline = useMultiplayerStore(s => s.setIsOffline);
  const updateCursor = useMultiplayerStore(s => s.updateCursor);
  const removeCursor = useMultiplayerStore(s => s.removeCursor);
  const clearCursors = useMultiplayerStore(s => s.clearCursors);

  const showToast = useToastStore(state => state.showToast);

  const connectionRef = useRef<signalR.HubConnection | null>(null);
  /** Peer'dan gelip store'a uygulanan son şemanın serileştirilmiş hâli (echo tespiti). */
  const lastAppliedRemoteRef = useRef<string | null>(null);
  /** Üç yönlü birleştirmenin ortak atası: iki tarafın en son üzerinde anlaştığı şema. */
  const lastAgreedSchemaRef = useRef<DatabaseSchema | null>(null);
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

    /** Effect sökülünce true olur; uçuşta kalan async işlerin geç etki etmesini engeller. */
    let cancelled = false;

    // 1. Get or Generate Room ID
    let currentRoomId = roomIdFromUrl;
    if (!currentRoomId) {
      const urlParams = new URLSearchParams(window.location.search);
      currentRoomId = urlParams.get('roomId');
      if (!currentRoomId) {
        // Branch çözümlemesi ağ üzerinden gelir; bu sırada kullanıcı başka bir sayfaya
        // geçerse (ör. /review) pushState O sayfanın URL'ini bozardı — pathname çağrı
        // anında okunuyor. Effect temizliğinde bayrak kalkar ve geç gelen yanıt yutulur.
        const pathnameAtStart = window.location.pathname;
        const pushRoomUrl = (roomId: string) => {
          if (cancelled || window.location.pathname !== pathnameAtStart) return;
          const newUrl = window.location.protocol + '//' + window.location.host + pathnameAtStart + '?roomId=' + roomId;
          window.history.pushState({ path: newUrl }, '', newUrl);
          setRoomIdFromUrl(roomId);
        };
        const randomRoomId = () => {
          const rand = (typeof crypto !== 'undefined' && crypto.randomUUID)
            ? crypto.randomUUID()
            : Math.random().toString(36).substring(2, 11);
          return 'room-' + rand;
        };

        // G17 — new-phase/30-SERVER-SIDE-BRANCHING.md §3 Adım 2: "CanvasHub'ın bugünkü
        // roomId kavramı branch_id'ye eşlenir." Kimliği doğrulanmış bir kullanıcının aktif
        // bir projesi varsa, rastgele bir oda üretmek yerine projenin sunucu-otoriteli
        // varsayılan branch'ini oda kimliği olarak kullan — aynı projenin aynı branch'i
        // üzerinde çalışan iki kullanıcı, link paylaşmadan otomatik aynı odada buluşur.
        // Guest/anonim kullanıcılar (proje yok) ve branch çözümlenemezse (ağ hatası vb.)
        // eski "tahmin edilemez roomId" (capability modeli) davranışına düşülür —
        // bu bilinçli: guest erişimi tasarım gereği, bu akış bozulmamalı.
        const { isAuthenticated } = useAuthStore.getState();
        const { activeProjectId } = useProjectHistoryStore.getState();

        if (isAuthenticated && activeProjectId) {
          branchService.getOrCreateDefault(activeProjectId)
            .then(branch => pushRoomUrl(branch.id))
            .catch(() => pushRoomUrl(randomRoomId()));
        } else {
          pushRoomUrl(randomRoomId());
        }
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
      .withUrl(CANVAS_HUB_URL, {
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

    // Peer odadan ayrılınca hayalet imlecini temizle.
    connection.on('ReceiveUserLeft', (connectionId: string) => {
      removeCursor(connectionId);
    });

    connection.on('ReceiveSchema', (remoteSchema: DatabaseSchema) => {
      const localSchema = useSchemaStore.getState().schema;
      const base = lastAgreedSchemaRef.current;

      // Üç yönlü birleştir: yereli körce ezme. Ortak ata (base) yoksa (odaya yeni
      // katıldık) gelen şemayı olduğu gibi al — birleştirilecek bir yerel geçmiş yok.
      const nextSchema = (base && localSchema)
        ? mergeSchemas(base, localSchema, remoteSchema)
        : remoteSchema;

      loadFromSchema(nextSchema);

      // Birleştirilmiş sonuç artık iki tarafın da yeni ortak atası. Store normalize
      // ettiği için ham nesneyi değil, gerçekten yazılan hâli referans al.
      const applied = useSchemaStore.getState().schema;
      lastAgreedSchemaRef.current = applied;

      // Echo guard: yayına çıkmaması gereken tek şey az önce uyguladığımız şemadır.
      // Süreyle değil içerikle tanınır (eski 300ms penceresi o an yapılan yerel
      // düzenlemeyi yutup bir daha denemiyordu → sessiz desync).
      //
      // NOT: birleştirme yerel değişiklik EKLEDİYSE applied ≠ remoteSchema olur;
      // bu durumda aşağıdaki sync effect farkı görüp merge sonucunu peer'a yayar,
      // böylece iki taraf da aynı birleşik şemaya yakınsar.
      lastAppliedRemoteRef.current = JSON.stringify(applied);
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
        // Reconnect'te yeni ConnectionId atanır → gruba yeniden katılmazsak peer,
        // artık ReceiveSchema/ReceiveCursor almaz (tek yönlü desync). Yeniden JoinRoom.
        connection.invoke('JoinRoom', currentRoomId, currentUserName).catch(() => {});
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
          // Odaya katılırken mevcut yerel şema ilk ortak atadır: ilk ReceiveSchema
          // gelene kadar yaptığımız yerel değişiklikler bu base'e göre korunur.
          lastAgreedSchemaRef.current = useSchemaStore.getState().schema;
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

    const handleWindowMouseMove = (e: MouseEvent) => {
      if (useMultiplayerStore.getState().isOffline) return; // Don't track/send cursor position if offline
      if (!connection.state || connection.state !== signalR.HubConnectionState.Connected) return;

      // Ekran koordinatı DEĞİL, flow (canvas) koordinatı gönder. Peer'lar farklı
      // pan/zoom'da olduğu için ham clientX/clientY imleci karşı tarafta yanlış
      // yere düşürür.
      const flowPos = screenToFlowPosition(e.clientX, e.clientY);
      if (!flowPos) return;

      const dist = Math.hypot(flowPos.x - lastSentCursorRef.current.x, flowPos.y - lastSentCursorRef.current.y);
      if (dist < 15) return;

      lastSentCursorRef.current = flowPos;

      connection.invoke('MoveCursor', currentRoomId, currentUserName, flowPos.x, flowPos.y)
        .catch(() => {});
    };

    window.addEventListener('mousemove', handleWindowMouseMove);

    // Clean up
    return () => {
      cancelled = true;
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
            clearTimeout(stopTimeout);
          } else if (connection.state === signalR.HubConnectionState.Disconnected) {
            clearInterval(stopInterval);
            clearTimeout(stopTimeout);
          }
        }, 100);
        // Emniyet: handshake hiç tamamlanmazsa yoklamayı sonlandır.
        // (Timeout'un kendisi de temizlenmeli, aksi halde unmount sonrası ateşlenir.)
        const stopTimeout = setTimeout(() => clearInterval(stopInterval), 5000);
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

    // Az önce peer'dan alıp uyguladığımız şemayı geri yayınlama (sonsuz echo).
    // İçerik farklıysa bu gerçek bir yerel düzenlemedir ve MUTLAKA yayınlanmalı.
    const serialized = JSON.stringify(schema);
    if (serialized === lastAppliedRemoteRef.current) return;
    lastAppliedRemoteRef.current = null;

    // Send update to peers only if connection is active
    if (connectionRef.current.state === signalR.HubConnectionState.Connected) {
      // Yayınladığımız şema, birleştirmenin yeni ortak atasıdır: bir sonraki
      // ReceiveSchema bu noktadan itibaren "kim ne değiştirdi"yi buna göre hesaplar.
      lastAgreedSchemaRef.current = schema;
      connectionRef.current.invoke('UpdateSchema', roomId, schema)
        .catch(() => {});
    }
  }, [schema, isConnected, isOffline, roomId]);
}
