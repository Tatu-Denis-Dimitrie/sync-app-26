export interface CSVDepartmentDTO {
  name: string;
}

export interface DepartmentGETResponseDTO {
  id: string;
  name: string;
}

export interface CSVDepartmentComparisonDTO {
  csvDepartment?: CSVDepartmentDTO;
  dbDepartment?: DepartmentGETResponseDTO;
  status: 'new' | 'unchanged';
  selected?: boolean;
}

export interface DepartmentSyncRequestDTO {
  items: CSVDepartmentComparisonDTO[];
}