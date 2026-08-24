export interface DataChangeRequest {
  id: string;
  userId: string;
  userEmail?: string;
  userFullName?: string;
  requestedChangesJson: string;
  originalValuesJson?: string;
  reason: string;
  status: string; // 'Pending', 'Approved', 'Rejected'
  createdAt: string;
  resolvedAt?: string;
  resolvedByAdminId?: string;
  autoResolvedByImportHistoryId?: string;
}

export interface CreateDataChangeRequestDto {
  requestedChangesJson: string;
  reason: string;
}

export interface ResolveDataChangeRequestDto {
  status: string; // 'Approved' | 'Rejected'
}

export interface RequestEmailChangeDto {
  newEmail: string;
  reason?: string;
}
