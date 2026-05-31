import { useState, useCallback } from 'react';
import { schemaService } from '../services/api';
import { DatabaseSchema } from '../types/schema';

export interface DockerLog {
  message: string;
  timestamp: number;
}

export function useDockerJob() {
  const [jobId, setJobId] = useState<string | null>(null);
  const [logs, setLogs] = useState<DockerLog[]>([]);
  const [status, setStatus] = useState<'idle' | 'running' | 'done' | 'error'>('idle');
  const [downloadUrl, setDownloadUrl] = useState<string | null>(null);

  const startJob = useCallback(async (schema: DatabaseSchema, dbType: string) => {
    try {
      setStatus('running');
      setLogs([{ message: 'Initializing Docker sandbox...', timestamp: Date.now() }]);
      setDownloadUrl(null);
      
      const responseJobId = await schemaService.runDockerSandbox(schema, dbType);
      setJobId(responseJobId);

      const eventSource = new EventSource(`http://localhost:5000/api/docker/stream/${responseJobId}`);

      eventSource.onmessage = (event) => {
        const data = event.data;
        if (data === 'DONE') {
          setStatus('done');
          eventSource.close();
        } else if (data.startsWith('ERROR:')) {
          setStatus('error');
          setLogs(prev => [...prev, { message: data, timestamp: Date.now() }]);
          eventSource.close();
        } else if (data.startsWith('DOWNLOAD_URL|')) {
          setDownloadUrl(`http://localhost:5000${data.split('|')[1]}`);
          setStatus('done');
          eventSource.close();
        } else {
          setLogs(prev => [...prev, { message: data, timestamp: Date.now() }]);
        }
      };

      eventSource.onerror = (error) => {
        console.error('SSE Error:', error);
        setStatus('error');
        setLogs(prev => [...prev, { message: 'ERROR: Lost connection to server log stream.', timestamp: Date.now() }]);
        eventSource.close();
      };

    } catch (error: any) {
      console.error('Failed to start docker job', error);
      setStatus('error');
      setLogs(prev => [...prev, { message: `ERROR: ${error.message || 'Unknown error starting job'}`, timestamp: Date.now() }]);
    }
  }, []);

  const reset = useCallback(() => {
    setJobId(null);
    setLogs([]);
    setStatus('idle');
    setDownloadUrl(null);
  }, []);

  return { jobId, logs, status, downloadUrl, startJob, reset };
}
