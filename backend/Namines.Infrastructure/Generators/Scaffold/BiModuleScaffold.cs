using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Namines.Core.Models;

namespace Namines.Infrastructure.Generators.Scaffold;

/// <summary>
/// Istege bagli BI modulu: Text-to-SQL denetleyicisi + React sohbet bileseni.
///
/// <see cref="Namines.Infrastructure.Services.ScaffolderService"/>'ten ayrildi
/// (ARCH-003 / B-37): 1.760 satirlik tek dosya, hedef basina bir uretece
/// bolundu -- <c>Generators/Eject/</c> altindaki mevcut desenle ayni.
/// Metot govdeleri DEGISMEDI; <c>ScaffolderSnapshotTests</c> ciktinin bayt
/// bayt ayni kaldigini kanitliyor.
/// </summary>
internal static class BiModuleScaffold
{
    internal static string GenerateBiControllerCode()
    {
        return @"using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NaminesProject.Infrastructure.Data;

namespace NaminesProject.API.Controllers;

[ApiController]
[Route(""api/bi"")]
public class BiAnalyticsController : ControllerBase
{
    private readonly AppDbContext _context;

    public BiAnalyticsController(AppDbContext context)
    {
        _context = context;
    }

    public class AskRequest
    {
        public string Question { get; set; } = string.Empty;
    }

    [HttpPost(""ask"")]
    public async Task<IActionResult> Ask([FromBody] AskRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            return BadRequest(new { error = ""Question field cannot be empty"" });
        }

        string sql = TranslateToSql(request.Question);

        var sanitized = sql.Trim().TrimEnd(';');
        if (!Regex.IsMatch(sanitized, @""^\s*SELECT\s"", RegexOptions.IgnoreCase))
        {
            return BadRequest(new { error = ""For security reasons, only read-only SELECT queries can be executed."" });
        }

        var forbidden = new[] { ""INSERT"", ""UPDATE"", ""DELETE"", ""DROP"", ""ALTER"", ""CREATE"", 
                                ""TRUNCATE"", ""EXEC"", ""EXECUTE"", ""CALL"", ""GRANT"", ""REVOKE"", 
                                ""MERGE"", ""INTO"" };
        foreach (var kw in forbidden)
        {
            if (Regex.IsMatch(sanitized, $@""\b{kw}\b"", RegexOptions.IgnoreCase))
            {
                return BadRequest(new { error = ""Invalid SQL expression. Unsupported commands detected."" });
            }
        }

        if (sanitized.Contains(';'))
        {
            return BadRequest(new { error = ""Multiple statements are not allowed."" });
        }

        try
        {
            var data = new List<Dictionary<string, object>>();
            var connection = _context.Database.GetDbConnection();
            
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var row = new Dictionary<string, object>();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[reader.GetName(i)] = reader.GetValue(i);
                        }
                        data.Add(row);
                    }
                }
            }

            string chartType = RecommendChart(sql, data);
            var firstRow = data.FirstOrDefault();
            var xAxisKey = firstRow?.Keys.FirstOrDefault(k => k.ToLowerInvariant().Contains(""name"") || k.ToLowerInvariant().Contains(""date"") || k.ToLowerInvariant().Contains(""title"") || k.ToLowerInvariant().Contains(""id"")) ?? firstRow?.Keys.FirstOrDefault() ?? """";
            var yAxisKey = firstRow?.Keys.FirstOrDefault(k => !string.Equals(k, xAxisKey, StringComparison.OrdinalIgnoreCase) && (k.ToLowerInvariant().Contains(""count"") || k.ToLowerInvariant().Contains(""total"") || k.ToLowerInvariant().Contains(""price"") || k.ToLowerInvariant().Contains(""amount"") || k.ToLowerInvariant().Contains(""quantity"") || k.ToLowerInvariant().Contains(""id""))) ?? firstRow?.Keys.FirstOrDefault(k => !string.Equals(k, xAxisKey, StringComparison.OrdinalIgnoreCase)) ?? """";

            return Ok(new
            {
                question = request.Question,
                generatedSql = sql,
                chartType = chartType,
                xAxisKey = xAxisKey,
                yAxisKey = yAxisKey,
                data = data
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $""SQL Execution Error: {ex.Message}"", sql = sql });
        }
    }

    private string TranslateToSql(string question)
    {
        var q = question.ToLowerInvariant();
        var tables = _context.Model.GetEntityTypes().Select(t => t.GetTableName()).ToList();
        
        if (tables.Count == 0) return ""SELECT 1"";

        string primaryTable = tables[0];
        foreach (var t in tables)
        {
            if (q.Contains(t.ToLowerInvariant()))
            {
                primaryTable = t;
                break;
            }
        }

        if (q.Contains(""toplam"") || q.Contains(""total"") || q.Contains(""en çok"") || q.Contains(""most"") || q.Contains(""en yüksek"") || q.Contains(""highest""))
        {
            return $""SELECT {primaryTable}.*, COUNT(*) as Total FROM {primaryTable} GROUP BY 1 ORDER BY Total DESC LIMIT 10"";
        }
        
        if (q.Contains(""son"") || q.Contains(""latest"") || q.Contains(""tarih"") || q.Contains(""date"") || q.Contains(""yeni"") || q.Contains(""new""))
        {
            return $""SELECT * FROM {primaryTable} ORDER BY 1 DESC LIMIT 10"";
        }

        return $""SELECT * FROM {primaryTable} LIMIT 10"";
    }

    private string RecommendChart(string sql, List<Dictionary<string, object>> data)
    {
        if (data.Count == 0) return ""table"";
        return sql.ToUpperInvariant().Contains(""GROUP BY"") ? ""pie"" : ""bar"";
    }
}
";
    }

    internal static string GenerateBiComponentCode()
    {
        return @"import React, { useState } from 'react';
import { Sparkles, Send, Database, BarChart2, PieChart as PieIcon, LineChart as LineIcon, Table } from 'lucide-react';
import { BarChart, Bar, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, PieChart, Pie, Cell, LineChart, Line } from 'recharts';

const COLORS = ['#6366f1', '#10b981', '#f59e0b', '#ec4899', '#8b5cf6', '#3b82f6'];

export default function BiChatAssistant() {
  const [question, setQuestion] = useState('');
  const [isLoading, setIsLoading] = useState(false);
  const [messages, setMessages] = useState<any[]>([]);
  const [expandedSql, setExpandedSql] = useState<Record<number, boolean>>({});

  const handleAsk = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!question.trim()) return;

    const userMsg = { id: Date.now(), sender: 'user', text: question };
    setMessages(prev => [...prev, userMsg]);
    setQuestion('');
    setIsLoading(true);

    try {
      const response = await fetch('/api/bi/ask', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question: question })
      });
      
      const result = await response.json();
      
      if (response.ok) {
        setMessages(prev => [...prev, {
          id: Date.now() + 1,
          sender: 'assistant',
          sql: result.generatedSql,
          chartType: result.chartType,
          xAxisKey: result.xAxisKey,
          yAxisKey: result.yAxisKey,
          data: result.data
        }]);
      } else {
        setMessages(prev => [...prev, {
          id: Date.now() + 1,
          sender: 'assistant',
          error: result.error || 'An error occurred during analysis.'
        }]);
      }
    } catch (err) {
      setMessages(prev => [...prev, {
        id: Date.now() + 1,
        sender: 'assistant',
        error: 'API connection failed.'
      }]);
    } finally {
      setIsLoading(false);
    }
  };

  const renderChart = (msg: any) => {
    if (!msg.data || msg.data.length === 0) return <div className=""text-zinc-500 text-xs py-4 text-center"">No data to display.</div>;

    const xAxis = msg.xAxisKey;
    const yAxis = msg.yAxisKey;

    switch (msg.chartType) {
      case 'bar':
        return (
          <ResponsiveContainer width=""100%"" height={220}>
            <BarChart data={msg.data}>
              <CartesianGrid strokeDasharray=""3 3"" stroke=""#27272a"" />
              <XAxis dataKey={xAxis} stroke=""#a1a1aa"" fontSize={10} />
              <YAxis stroke=""#a1a1aa"" fontSize={10} />
              <Tooltip contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }} />
              <Bar dataKey={yAxis} fill=""#6366f1"" radius={[4, 4, 0, 0]}>
                {msg.data.map((entry: any, index: number) => (
                  <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>
        );
      case 'pie':
        return (
          <ResponsiveContainer width=""100%"" height={220}>
            <PieChart>
              <Pie data={msg.data} dataKey={yAxis} nameKey={xAxis} cx=""50%"" cy=""50%"" outerRadius={70} fill=""#6366f1"" label={{ fontSize: 9, fill: '#e4e4e7' }}>
                {msg.data.map((entry: any, index: number) => (
                  <Cell key={`cell-${index}`} fill={COLORS[index % COLORS.length]} />
                ))}
              </Pie>
              <Tooltip contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }} />
            </PieChart>
          </ResponsiveContainer>
        );
      case 'line':
        return (
          <ResponsiveContainer width=""100%"" height={220}>
            <LineChart data={msg.data}>
              <CartesianGrid strokeDasharray=""3 3"" stroke=""#27272a"" />
              <XAxis dataKey={xAxis} stroke=""#a1a1aa"" fontSize={10} />
              <YAxis stroke=""#a1a1aa"" fontSize={10} />
              <Tooltip contentStyle={{ backgroundColor: '#09090b', borderColor: '#27272a', borderRadius: '8px' }} />
              <Line type=""monotone"" dataKey={yAxis} stroke=""#10b981"" strokeWidth={2} activeDot={{ r: 6 }} />
            </LineChart>
          </ResponsiveContainer>
        );
      default:
        return (
          <div className=""overflow-x-auto max-h-48 border border-zinc-800 rounded-xl"">
            <table className=""min-w-full divide-y divide-zinc-800 text-left text-xs text-zinc-300"">
              <thead className=""bg-zinc-900/60 text-zinc-400 font-semibold"">
                <tr>
                  {Object.keys(msg.data[0] || {}).map(key => (
                    <th key={key} className=""px-4 py-2 border-b border-zinc-800"">{key}</th>
                  ))}
                </tr>
              </thead>
              <tbody className=""divide-y divide-zinc-800/50 bg-zinc-950/20"">
                {msg.data.map((row: any, i: number) => (
                  <tr key={i} className=""hover:bg-zinc-900/20"">
                    {Object.values(row).map((val: any, j: number) => (
                      <td key={j} className=""px-4 py-1.5 whitespace-nowrap"">{String(val)}</td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        );
    }
  };

  return (
    <div className=""flex flex-col bg-zinc-950 border border-zinc-800 rounded-2xl w-full max-w-lg h-[500px] shadow-2xl overflow-hidden text-zinc-100 font-sans"">
      <div className=""bg-gradient-to-r from-indigo-950/50 to-zinc-900/80 border-b border-zinc-800 px-4 py-3 flex items-center justify-between"">
        <div className=""flex items-center gap-2"">
          <Sparkles className=""w-5 h-5 text-indigo-400 animate-pulse"" />
          <div>
            <h3 className=""text-sm font-bold tracking-wide"">AI-BI Data Analytics</h3>
            <p className=""text-[10px] text-zinc-400"">Ask in natural language, see in charts.</p>
          </div>
        </div>
      </div>

      <div className=""flex-1 overflow-y-auto p-4 space-y-4"">
        {messages.length === 0 && (
          <div className=""h-full flex flex-col items-center justify-center text-center p-6 space-y-2"">
            <Database className=""w-10 h-10 text-zinc-700 animate-bounce"" />
            <p className=""text-xs text-zinc-400"">Try asking: ""Show total order count"" or ""List newest members""</p>
          </div>
        )}

        {messages.map(msg => (
          <div key={msg.id} className={`flex flex-col ${msg.sender === 'user' ? 'items-end' : 'items-start'}`}>
            <div className={`max-w-[85%] rounded-2xl px-4 py-2.5 text-xs ${
              msg.sender === 'user' 
                ? 'bg-indigo-600 text-white rounded-tr-none' 
                : 'bg-zinc-900 border border-zinc-800 text-zinc-200 rounded-tl-none space-y-3 w-full'
            }`}>
              {msg.sender === 'user' ? (
                msg.text
              ) : msg.error ? (
                <div className=""text-rose-400 font-medium"">⚠️ {msg.error}</div>
              ) : (
                <>
                  <div className=""bg-zinc-950 rounded-xl border border-zinc-800/80 overflow-hidden"">
                    <button
                      type=""button""
                      onClick={() => setExpandedSql(prev => ({ ...prev, [msg.id]: !prev[msg.id] }))}
                      className=""w-full px-3 py-2 flex items-center justify-between text-[10px] font-bold text-zinc-400 hover:text-white hover:bg-zinc-900/50 transition-all""
                    >
                      <span className=""flex items-center gap-1.5"">
                        <Database className=""w-3.5 h-3.5 text-indigo-400"" />
                        View Generated SQL
                      </span>
                      <span className=""text-[9px]"">
                        {expandedSql[msg.id] ? 'Hide' : 'Show'}
                      </span>
                    </button>
                    {expandedSql[msg.id] && (
                      <div className=""p-2.5 bg-zinc-900 border-t border-zinc-800/80 font-mono text-[10px] text-indigo-300 overflow-x-auto"">
                        {msg.sql}
                      </div>
                    )}
                  </div>
                  
                  <div className=""bg-zinc-950/45 p-2 rounded-xl border border-zinc-800/40"">
                    <div className=""flex justify-between items-center mb-2 px-1"">
                      <span className=""text-[9px] font-bold text-zinc-400 uppercase tracking-wide flex items-center gap-1"">
                        {msg.chartType === 'bar' && <BarChart2 className=""w-3.5 h-3.5 text-indigo-400"" />}
                        {msg.chartType === 'pie' && <PieIcon className=""w-3.5 h-3.5 text-emerald-400"" />}
                        {msg.chartType === 'line' && <LineIcon className=""w-3.5 h-3.5 text-sky-400"" />}
                        {msg.chartType === 'table' && <Table className=""w-3.5 h-3.5 text-amber-400"" />}
                        Visual Report ({msg.chartType})
                      </span>
                    </div>
                    {renderChart(msg)}
                  </div>
                </>
              )}
            </div>
          </div>
        ))}
        {isLoading && (
          <div className=""flex items-center gap-2 text-zinc-400 text-xs"">
            <Sparkles className=""w-4 h-4 text-indigo-400 animate-spin"" />
            <span>AI is analyzing data...</span>
          </div>
        )}
      </div>

      <form onSubmit={handleAsk} className=""border-t border-zinc-800/60 p-3 bg-zinc-900/45 flex gap-2"">
        </button>
      </form>
    </div>
  );
}
";
    }
}
