// Per-category paging state for the document signature lists — see line-manager and basic-user
// components. Each of the 6 mini-lists (own/manager/instructor × pending/signed) tracks its own
// page independently, since each is backed by its own paginated API call.
export interface DocumentPageState {
  items: any[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export const emptyDocumentPageState = (pageSize = 10): DocumentPageState =>
  ({ items: [], page: 1, pageSize, totalCount: 0 });

export interface DocumentListPageResponse {
  items: any[];
  totalCount: number;
}
