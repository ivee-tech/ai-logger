import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';

import { environment } from '../../environments/environment';
import { LogAnalysisRequest, LogAnalysisResponse } from '../models/log-analysis.models';

@Injectable({ providedIn: 'root' })
export class LogAnalysisService {
  private readonly apiBase = environment.apiBaseUrl.replace(/\/$/, '');
  private readonly analyzeEndpoint = `${this.apiBase}/logs/analyze`;

  constructor(private readonly http: HttpClient) {}

  analyzeLog(file: File, request: LogAnalysisRequest): Observable<LogAnalysisResponse> {
    const formData = new FormData();
    formData.append('file', file, file.name);

    if (request.provider && request.provider.length > 0) {
      formData.append('provider', request.provider);
    }

    const trimmedNotes = request.notes?.trim();
    if (trimmedNotes) {
      formData.append('notes', trimmedNotes);
    }

    return this.http.post<LogAnalysisResponse>(this.analyzeEndpoint, formData);
  }
}
