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

export function useAIDba() {
  const { schema, dbType } = useSchemaStore();
  const { setDbaResults, setIsAnalyzing } = useDbaStore();

  const analyzeNow = async () => {
    if (!schema || schema.tables.length === 0) {
      setDbaResults({
        issues: [],
        score: 100,
        assessment: 'No tables found to analyze.'
      });
      setIsAnalyzing(false);
      return;
    }

    setIsAnalyzing(true);
    try {
      const result = await aiDbaService.analyze(schema, dbType);
      setDbaResults({
        issues: result.issues || [],
        score: result.totalScore ?? 100,
        assessment: result.overallAssessment || ''
      });
    } catch (err) {
      console.error("AI DBA Analysis error:", err);
      throw err;
    } finally {
      setIsAnalyzing(false);
    }
  };

  return { analyzeNow };
}

