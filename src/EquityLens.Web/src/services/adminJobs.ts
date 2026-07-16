import { http } from './http'

export interface AdminJobRun {
  id: string
  jobType: string
  status: string
  progressPercent: number
  createdAtUtc: string
  startedAtUtc: string | null
  completedAtUtc: string | null
  errorMessage: string | null
  payloadJson: string | null
  resultJson: string | null
  redisJobId: string | null
}

export interface JobTypeOption {
  label: string
  value: string
}

export interface CreateAdminJobRequest {
  jobType: string
  from?: string
  to?: string
  ticker?: string
  exchange?: string
}

export async function listAdminJobs(params?: {
  limit?: number
  jobType?: string
  status?: string
}): Promise<AdminJobRun[]> {
  const response = await http.get<AdminJobRun[]>('/admin/jobs', { params })
  return response.data
}

export async function getAdminJob(id: string): Promise<AdminJobRun> {
  const response = await http.get<AdminJobRun>(`/admin/jobs/${id}`)
  return response.data
}

export async function createAdminJob(request: CreateAdminJobRequest): Promise<AdminJobRun> {
  const response = await http.post<AdminJobRun>('/admin/jobs', request)
  return response.data
}

export async function cancelAdminJob(id: string): Promise<AdminJobRun> {
  const response = await http.post<AdminJobRun>(`/admin/jobs/${id}/cancel`)
  return response.data
}
