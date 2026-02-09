import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { environment } from '../../environments/environment';

export interface VersionInfo {
  version: string;
}

@Injectable({
  providedIn: 'root'
})
export class VersionService {
  private apiUrl = environment.apiUrl + environment.endpoints.version;

  constructor(private http: HttpClient) {}

  getVersion(): Observable<VersionInfo> {
    return this.http.get<VersionInfo>(this.apiUrl);
  }
}
