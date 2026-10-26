import type { Issue, Status } from './types';

export const apiBase = (import.meta.env.VITE_API_BASE_URL || 'http://localhost:8080').replace(/\/+$/, '');

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  try {
    const response = await fetch(`${apiBase}${path}`, {
      ...init,
      signal: AbortSignal.timeout(10000),
    });
    const data = await response.json().catch(() => null);
    if (!response.ok) {
      throw new Error(data?.error?.message || `请求失败（HTTP ${response.status}）`);
    }
    if (data === null) throw new Error('后端返回了无效响应，请确认服务地址');
    return data as T;
  } catch (error) {
    if (error instanceof DOMException && (error.name === 'TimeoutError' || error.name === 'AbortError')) {
      throw new Error('请求超时，请检查后端服务和网络后重试');
    }
    if (error instanceof TypeError) throw new Error('无法连接后端，请检查服务、网络及 CORS 配置');
    throw error;
  }
}

export async function listIssues(): Promise<Issue[]> {
  const data = await request<{ items: Issue[] }>('/api/issues');
  if (!Array.isArray(data.items)) throw new Error('问题列表响应格式错误');
  return data.items;
}

export function updateStatus(id: string, status: Status): Promise<Issue> {
  return request<Issue>(`/api/issues/${encodeURIComponent(id)}/status`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ status }),
  });
}
