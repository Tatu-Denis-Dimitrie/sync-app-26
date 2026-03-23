export interface DataChangeRequest {
  id: string;
  userId: string;
  userEmail?: string;
  userFullName?: string;
  requestedChangesJson: string;
  reason: string;
  status: string; // 'Pending', 'Approved', 'Rejected'
  createdAt: string;
  resolvedAt?: string;
  resolvedByAdminId?: string;
}

export interface CreateDataChangeRequestDto {
  requestedChangesJson: string;
  reason: string;
}

export interface ResolveDataChangeRequestDto {
  status: string; // 'Approved' | 'Rejected'
}
