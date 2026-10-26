import { useEffect, useRef, useState } from 'react';
import { listIssues, updateStatus } from './api';
import { priorityLabels, statusLabels } from './types';
import type { Issue, Status } from './types';
import './App.css';

function messageOf(error: unknown): string {
  return error instanceof Error ? error.message : '操作失败，请重试';
}

export default function App() {
  const [items, setItems] = useState<Issue[]>([]);
  const [drafts, setDrafts] = useState<Record<string, Status>>({});
  const [busy, setBusy] = useState(false);
  const [saving, setSaving] = useState<string | null>(null);
  const [loaded, setLoaded] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState('');
  // Guard synchronously: a double click must not race React's state update.
  const requestInFlight = useRef(false);

  async function refresh() {
    if (requestInFlight.current) return;
    requestInFlight.current = true;
    setBusy(true); setError(''); setNotice('');
    try {
      setItems(await listIssues());
      setDrafts({}); setLoaded(true);
    } catch (error) { setError(messageOf(error)); }
    finally { setBusy(false); requestInFlight.current = false; }
  }

  useEffect(() => { void refresh(); }, []);

  async function save(issue: Issue) {
    if (requestInFlight.current) return;
    const status = drafts[issue.id] ?? issue.status;
    if (status === issue.status) return;
    requestInFlight.current = true;
    setSaving(issue.id); setError(''); setNotice('');
    try {
      const updated = await updateStatus(issue.id, status);
      setItems(current => current.map(item => item.id === updated.id ? updated : item));
      setDrafts(current => { const next = { ...current }; delete next[issue.id]; return next; });
      setNotice(`“${issue.title}”已更新为${statusLabels[updated.status]}`);
    } catch (error) { setError(messageOf(error)); }
    finally { setSaving(null); requestInFlight.current = false; }
  }

  const disabled = busy || saving !== null;
  return <main>
    <header>
      <div><p className="eyebrow">现场巡检</p><h1>问题管理</h1><p className="subtitle">查看现场上报，跟进处理进度。</p></div>
      <button onClick={() => void refresh()} disabled={disabled}>{busy ? '加载中…' : '刷新列表'}</button>
    </header>
    {error && <div className="alert error" role="alert">{error}。可点击“刷新列表”重试；保存失败时可再次保存。</div>}
    {notice && <div className="alert success" role="status">{notice}</div>}
    <section className="card" aria-label="巡检问题列表" aria-busy={disabled}>
      <div className="section-title"><h2>全部问题</h2><span>{items.length} 条</span></div>
      {busy && <p className="empty" role="status">正在获取问题…</p>}
      {!busy && !loaded && error && <p className="empty">暂时无法获取列表，请重试。</p>}
      {!busy && loaded && items.length === 0 && <p className="empty">暂无问题，请先在手机端放置标记并提交，再刷新此页。</p>}
      {items.length > 0 && <div className="table-wrap"><table>
        <thead><tr><th>问题</th><th>优先级</th><th>已保存状态</th><th>位置（米）</th><th>创建时间</th><th>更新状态</th></tr></thead>
        <tbody>{items.map(issue => <tr key={issue.id}>
          <td><strong>{issue.title}</strong><p className="description">{issue.description || '无描述'}</p></td>
          <td><span className={`badge priority-${issue.priority}`}>{priorityLabels[issue.priority]}</span></td>
          <td>{statusLabels[issue.status]}</td>
          <td className="coordinates">{(['x', 'y', 'z'] as const).map(axis => <div key={axis}>{axis}: {issue.position[axis].toFixed(3)}</div>)}</td>
          <td className="date">{new Date(issue.createdAt).toLocaleString('zh-CN')}</td>
          <td><div className="actions">
            <select aria-label={`修改 ${issue.title} 的状态`} value={drafts[issue.id] ?? issue.status} disabled={disabled}
              onChange={event => setDrafts(current => ({ ...current, [issue.id]: event.target.value as Status }))}>
              {Object.entries(statusLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
            <button disabled={disabled || (drafts[issue.id] ?? issue.status) === issue.status} onClick={() => void save(issue)}>
              {saving === issue.id ? '保存中…' : '保存'}
            </button>
          </div></td>
        </tr>)}</tbody>
      </table></div>}
    </section>
    <p className="footnote">列表通过刷新获取最新数据；状态保存成功后会立即更新当前行。</p>
  </main>;
}
