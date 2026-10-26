export type Priority = 'low' | 'medium' | 'high';
export type Status = 'open' | 'in_progress' | 'resolved';

export interface Issue {
  id: string;
  title: string;
  description: string;
  priority: Priority;
  status: Status;
  position: { x: number; y: number; z: number };
  createdAt: string;
  updatedAt: string;
}

export const statusLabels: Record<Status, string> = {
  open: '待处理', in_progress: '处理中', resolved: '已解决',
};
export const priorityLabels: Record<Priority, string> = {
  low: '低', medium: '中', high: '高',
};
