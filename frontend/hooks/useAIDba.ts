import { useEffect, useRef } from 'react';
import { useSchemaStore } from '../store/useSchemaStore';
import { useDbaStore } from '../store/useDbaStore';
import { aiDbaService } from '../services/api';

export interface DbaIssue {
  ruleId: string;
  tableName: string;
  columnName?: string;
  severity: 0 | 1 | 2; // 0: Info, 1: Warning, 2: Error
  message: string;
  suggestion?: string;
  source: string;
  category?: 'Performance' | 'Security' | 'FinOps';
}

const DEBOUNCE_MS = 2000;

export function useAIDba() {
  const { schema, dbType } = useSchemaStore();
  const { setDbaResults, setIsAnalyzing, isPanelOpen } = useDbaStore();
  const timerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const analyzeNow = async () => {
    if (!schema || schema.tables.length === 0) {
      setDbaResults({ issues: [], score: 100, assessment: 'No tables found to analyze.' });
      setIsAnalyzing(false);
      return;
    }

    setIsAnalyzing(true);
    try {
      const result = await aiDbaService.analyze(schema, dbType);
      setDbaResults({
        issues: result.issues || [],
        score: result.totalScore ?? 100,
        assessment: result.overallAssessment || '',
      });
    } catch (err) {
      console.error('AI DBA Analysis error:', err);
      throw err;
    } finally {
      setIsAnalyzing(false);
    }
  };

  // Auto-analyze with 2-second debounce when the panel is open and schema changes
  useEffect(() => {
    if (!isPanelOpen) return;
    if (timerRef.current) clearTimeout(timerRef.current);
    timerRef.current = setTimeout(() => { analyzeNow(); }, DEBOUNCE_MS);
    return () => { if (timerRef.current) clearTimeout(timerRef.current); };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [schema, isPanelOpen]);

  return { analyzeNow };
}

